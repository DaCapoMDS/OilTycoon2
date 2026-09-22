using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

// Remaps keys for Oil Tycoon 2, whose input is DirectInput with the keys
// compiled in -- there is no binding table in the data files and no 'bind'
// command, so the only alternatives are patching core.dll or remapping
// outside the game. This does the latter.
//
// Scope, deliberately narrow:
//   * it only rewrites keys while the Oil Tycoon 2 window is in the
//     foreground; every other application is passed through untouched,
//   * it translates one key into another and nothing else. No keystroke is
//     recorded, stored, counted or written anywhere. There is no file output.
//   * close the console window to stop it.
//
// Mapping lives in keybinds.ini next to the executable, created on first run.
class Keybinds {
    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101;
    const int WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT {
        public uint vkCode, scanCode, flags, time;
        public IntPtr dwExtraInfo;
    }

    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")]
    static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint n, INPUT[] inputs, int cb);

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
        public int pad1, pad2;   // pad INPUT out to the union's size
    }
    const uint INPUT_KEYBOARD = 1;
    const uint KEYEVENTF_KEYUP = 0x0002;

    // marks input we generated, so the hook ignores its own output
    static readonly IntPtr MARKER = new IntPtr(0x07C2BD);

    static IntPtr hook;
    static HookProc proc;                       // kept alive; a local would be collected
    static Dictionary<uint, ushort> map = new Dictionary<uint, ushort>();
    static string targetProcess = "game";

    static int Main(string[] args) {
        string ini = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "keybinds.ini");
        if (!File.Exists(ini)) WriteDefaultIni(ini);
        LoadIni(ini);

        Console.WriteLine("Oil Tycoon 2 - key remapper");
        Console.WriteLine();
        Console.WriteLine("Active only while the game window has focus.");
        Console.WriteLine("Translates keys; records nothing.");
        Console.WriteLine();
        Console.WriteLine("Mapping ({0}):", Path.GetFileName(ini));
        foreach (var kv in map)
            Console.WriteLine("    {0,-12} -> {1}", NameOf(kv.Key), NameOf(kv.Value));
        Console.WriteLine();
        Console.WriteLine("Waiting for \"{0}\". Close this window to stop.", targetProcess);

        proc = HookCallback;
        hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) {
            Console.WriteLine("Failed to install the keyboard hook: {0}", Marshal.GetLastWin32Error());
            return 1;
        }
        Application.Run();                       // message loop; hooks need one
        UnhookWindowsHookEx(hook);
        return 0;
    }

    static bool GameHasFocus() {
        IntPtr h = GetForegroundWindow();
        if (h == IntPtr.Zero) return false;
        uint pid;
        GetWindowThreadProcessId(h, out pid);
        if (pid == 0) return false;
        try {
            using (var p = Process.GetProcessById((int)pid))
                return string.Equals(p.ProcessName, targetProcess, StringComparison.OrdinalIgnoreCase);
        } catch { return false; }
    }

    static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam) {
        if (nCode >= 0) {
            var info = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
            if (info.dwExtraInfo != MARKER && GameHasFocus()) {
                ushort to;
                if (map.TryGetValue(info.vkCode, out to)) {
                    int msg = wParam.ToInt32();
                    bool up = (msg == WM_KEYUP || msg == WM_SYSKEYUP);
                    Send(to, up);
                    return new IntPtr(1);        // swallow the original
                }
            }
        }
        return CallNextHookEx(hook, nCode, wParam, lParam);
    }

    static void Send(ushort vk, bool keyUp) {
        var inputs = new INPUT[1];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].ki.wVk = vk;
        inputs[0].ki.dwFlags = keyUp ? KEYEVENTF_KEYUP : 0;
        inputs[0].ki.dwExtraInfo = MARKER;
        SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
    }

    // ---- configuration ------------------------------------------------------

    static void WriteDefaultIni(string path) {
        File.WriteAllText(path, string.Join(Environment.NewLine, new[] {
            "; Oil Tycoon 2 key remapper",
            ";",
            "; from = to        both are virtual-key names (see the list below)",
            "; Only applied while the game window is focused.",
            ";",
            "; The game's own camera keys are the arrows; these lines put WASD",
            "; on top of them. Delete or change any line you don't want.",
            "",
            "[process]",
            "name = game",
            "",
            "[keys]",
            "W = Up",
            "A = Left",
            "S = Down",
            "D = Right",
            "",
            "; Examples you may want, commented out by default:",
            "; Q = Prior          ; page up   - rotate/zoom, depending on the game",
            "; E = Next           ; page down",
            "; R = Home",
            "; F = End",
            "",
            "; Recognised names: A-Z, 0-9, F1-F12, Up, Down, Left, Right,",
            "; Space, Escape, Tab, Enter, Home, End, Prior, Next, Insert,",
            "; Delete, Plus, Minus, Shift, Control, Alt"
        }) + Environment.NewLine);
    }

    static void LoadIni(string path) {
        string section = "";
        foreach (var raw in File.ReadAllLines(path)) {
            string line = raw;
            int c = line.IndexOf(';');
            if (c >= 0) line = line.Substring(0, c);
            line = line.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("[")) { section = line.Trim('[', ']').ToLower(); continue; }
            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            string k = line.Substring(0, eq).Trim();
            string v = line.Substring(eq + 1).Trim();
            if (section == "process" && k.Equals("name", StringComparison.OrdinalIgnoreCase)) {
                targetProcess = v;
            } else if (section == "keys") {
                uint from = VkOf(k); ushort to = (ushort)VkOf(v);
                if (from != 0 && to != 0) map[from] = to;
                else Console.WriteLine("  (ignoring unrecognised mapping: {0} = {1})", k, v);
            }
        }
    }

    static readonly Dictionary<string, uint> Names = BuildNames();
    static Dictionary<string, uint> BuildNames() {
        var d = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        for (char ch = 'A'; ch <= 'Z'; ch++) d[ch.ToString()] = ch;
        for (char ch = '0'; ch <= '9'; ch++) d[ch.ToString()] = ch;
        for (int i = 1; i <= 12; i++) d["F" + i] = (uint)(0x70 + i - 1);
        d["Up"] = 0x26; d["Down"] = 0x28; d["Left"] = 0x25; d["Right"] = 0x27;
        d["Space"] = 0x20; d["Escape"] = 0x1B; d["Esc"] = 0x1B; d["Tab"] = 0x09;
        d["Enter"] = 0x0D; d["Return"] = 0x0D;
        d["Home"] = 0x24; d["End"] = 0x23; d["Prior"] = 0x21; d["PageUp"] = 0x21;
        d["Next"] = 0x22; d["PageDown"] = 0x22;
        d["Insert"] = 0x2D; d["Delete"] = 0x2E;
        d["Plus"] = 0x6B; d["Minus"] = 0x6D;
        d["Shift"] = 0x10; d["Control"] = 0x11; d["Ctrl"] = 0x11; d["Alt"] = 0x12;
        return d;
    }
    static uint VkOf(string name) { uint v; return Names.TryGetValue(name, out v) ? v : 0; }
    static string NameOf(uint vk) {
        foreach (var kv in Names) if (kv.Value == vk) return kv.Key;
        return "0x" + vk.ToString("X2");
    }
}
