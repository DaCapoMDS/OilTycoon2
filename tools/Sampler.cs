using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

// A sampling profiler for the game, with no dependencies.
//
// The game is CPU-bound: ~3,500 draw calls cost ~90 ms with the GPU under
// 10%. That could be the engine's own per-object work, or Direct3D 9 and
// driver overhead per draw call. The two have completely different fixes, so
// this settles which by repeatedly stopping the busiest thread, reading its
// instruction pointer, and attributing that address to a loaded module.
//
// Built as x86 because game.exe is a 32-bit process; a 32-bit sampler can
// read a 32-bit thread context directly.
class Sampler {
    const int THREAD_SUSPEND_RESUME = 0x0002;
    const int THREAD_GET_CONTEXT = 0x0008;
    const int THREAD_QUERY_INFORMATION = 0x0040;
    const uint CONTEXT_i386 = 0x00010000;
    const uint CONTEXT_CONTROL = CONTEXT_i386 | 0x0001;
    const int EIP_OFFSET = 0xB8;      // offset of Eip inside the x86 CONTEXT
    const int CONTEXT_SIZE = 716;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenThread(int access, bool inherit, uint threadId);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint SuspendThread(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern int ResumeThread(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetThreadContext(IntPtr h, byte[] ctx);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr h);

    class Mod { public string Name; public ulong Lo, Hi; }

    static int Main(string[] args) {
        int seconds = args.Length > 0 ? int.Parse(args[0]) : 10;

        var procs = Process.GetProcessesByName("game");
        if (procs.Length == 0) { Console.WriteLine("Oil Tycoon 2 is not running."); return 1; }
        var p = procs[0];
        Console.WriteLine("sampling pid {0} for {1}s", p.Id, seconds);

        // module map, so an address can be named
        var mods = new List<Mod>();
        try {
            foreach (ProcessModule m in p.Modules) {
                ulong b = (ulong)m.BaseAddress.ToInt64();
                mods.Add(new Mod { Name = m.ModuleName, Lo = b, Hi = b + (ulong)m.ModuleMemorySize });
            }
        } catch (Exception ex) {
            Console.WriteLine("Could not read the module list: " + ex.Message);
            Console.WriteLine("Build this as x86 and run it while the game is running.");
            return 1;
        }
        Console.WriteLine("{0} modules loaded", mods.Count);

        // the thread that has burned the most CPU is the render/main thread
        ProcessThread busiest = null;
        foreach (ProcessThread t in p.Threads) {
            try { if (busiest == null || t.TotalProcessorTime > busiest.TotalProcessorTime) busiest = t; }
            catch { }
        }
        if (busiest == null) { Console.WriteLine("No readable threads."); return 1; }
        Console.WriteLine("busiest thread {0} ({1:N1}s cpu)", busiest.Id, busiest.TotalProcessorTime.TotalSeconds);

        IntPtr h = OpenThread(THREAD_SUSPEND_RESUME | THREAD_GET_CONTEXT | THREAD_QUERY_INFORMATION,
                              false, (uint)busiest.Id);
        if (h == IntPtr.Zero) { Console.WriteLine("OpenThread failed: {0}", Marshal.GetLastWin32Error()); return 1; }

        var hits = new Dictionary<string, int>();
        var hotspots = new List<ulong>();
        var ctx = new byte[CONTEXT_SIZE];
        int taken = 0, failed = 0;
        var until = DateTime.UtcNow.AddSeconds(seconds);

        while (DateTime.UtcNow < until) {
            Array.Clear(ctx, 0, ctx.Length);
            BitConverter.GetBytes(CONTEXT_CONTROL).CopyTo(ctx, 0);   // ContextFlags

            if (SuspendThread(h) == 0xFFFFFFFF) { failed++; Thread.Sleep(1); continue; }
            bool ok = GetThreadContext(h, ctx);
            ResumeThread(h);

            if (!ok) { failed++; Thread.Sleep(1); continue; }

            ulong eip = BitConverter.ToUInt32(ctx, EIP_OFFSET);
            hotspots.Add(eip);
            string name = "(unknown)";
            foreach (var m in mods) if (eip >= m.Lo && eip < m.Hi) { name = m.Name; break; }
            hits[name] = hits.ContainsKey(name) ? hits[name] + 1 : 1;
            taken++;
            Thread.Sleep(1);
        }
        CloseHandle(h);

        Console.WriteLine();
        Console.WriteLine("{0} samples ({1} failed)", taken, failed);
        if (taken == 0) return 1;
        Console.WriteLine();
        Console.WriteLine("where the CPU time goes:");
        var list = new List<KeyValuePair<string, int>>(hits);
        list.Sort((a, b) => b.Value.CompareTo(a.Value));
        foreach (var kv in list) {
            double pct = 100.0 * kv.Value / taken;
            if (pct < 0.4) continue;
            Console.WriteLine("  {0,-28} {1,6:N1}%  {2}", kv.Key, pct, new string('#', (int)Math.Round(pct / 2)));
        }

        // Within the dominant module, cluster the addresses. If the time sits
        // in a handful of spots it is a hot loop worth disassembling; if it is
        // spread evenly it is diffuse work with no single culprit.
        string top = list[0].Key;
        Mod topMod = mods.Find(m => m.Name == top);
        if (topMod != null && hotspots.Count > 0) {
            Console.WriteLine();
            Console.WriteLine("hot addresses inside {0} (RVA, 64-byte buckets):", top);
            var buckets = new Dictionary<ulong, int>();
            int inTop = 0;
            foreach (var eip in hotspots) {
                if (eip < topMod.Lo || eip >= topMod.Hi) continue;
                inTop++;
                ulong rva = (eip - topMod.Lo) & ~(ulong)63;
                buckets[rva] = buckets.ContainsKey(rva) ? buckets[rva] + 1 : 1;
            }
            var bl = new List<KeyValuePair<ulong, int>>(buckets);
            bl.Sort((a, b) => b.Value.CompareTo(a.Value));
            Console.WriteLine("  {0} distinct 64-byte regions across {1} samples", bl.Count, inTop);
            int shown = 0;
            double cume = 0;
            foreach (var kv in bl) {
                double pct = 100.0 * kv.Value / inTop;
                cume += pct;
                Console.WriteLine("    RVA 0x{0:X6}  {1,5:N1}%   (cumulative {2,5:N1}%)", kv.Key, pct, cume);
                if (++shown >= 12) break;
            }
        }
        return 0;
    }
}
