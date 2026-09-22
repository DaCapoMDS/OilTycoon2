using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Oil Tycoon 2 "encr" files: 4-byte magic, then plain[i] = cipher[i] ^ key[i].
// The keystream is position-based and shared by every file.
//
//   phase 1: recover key[0..43] from the XML declaration crib, cross-checked
//            across every encrypted .xml (they must all agree).
//   phase 2: recover the rest statistically -- at each position the correct
//            key byte maximises text-likeness across all files.
class Decrypt {
    const int MAGIC = 4;
    const string CRIB = "<?xml version=\"1.0\" encoding=\"ISO-8859-1\" ?>";

    static int[] W = BuildWeights();
    static int[] BuildWeights() {
        var w = new int[256];
        for (int i = 0; i < 256; i++) w[i] = -40;              // non printable
        for (int i = 0x20; i <= 0x7E; i++) w[i] = 1;
        foreach (char c in "abcdefghijklmnopqrstuvwxyz") w[c] = 6;
        foreach (char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ") w[c] = 4;
        foreach (char c in "0123456789") w[c] = 4;
        w[' '] = 7; w['\t'] = 4; w['\r'] = 4; w['\n'] = 5;
        foreach (char c in "<>/=\"-_.,;:()[]{}#!*") w[c] = 3;
        return w;
    }

    class Enc { public string Path; public byte[] Data; public string Ext; }

    static int Main(string[] args) {
        string root = args[0];
        string outDir = args.Length > 1 ? args[1] : null;

        var all = new List<Enc>();
        foreach (var p in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)) {
            var b = File.ReadAllBytes(p);
            if (b.Length < MAGIC) continue;
            if (!(b[0] == 'e' && b[1] == 'n' && b[2] == 'c' && b[3] == 'r')) continue;
            all.Add(new Enc { Path = p, Data = b, Ext = Path.GetExtension(p).ToLower() });
        }
        long maxLen = 0;
        foreach (var f in all) maxLen = Math.Max(maxLen, f.Data.Length - MAGIC);
        Console.WriteLine("encrypted files: {0}, longest payload {1:N0}", all.Count, maxLen);

        int keyLen = (int)maxLen;
        var key = new byte[keyLen];
        var known = new bool[keyLen];

        // ---- phase 1: crib on XML declarations ----
        var conflicts = 0; var used = 0;
        foreach (var f in all) {
            if (f.Ext != ".xml") continue;
            if (f.Data.Length - MAGIC < CRIB.Length) continue;
            used++;
            for (int i = 0; i < CRIB.Length; i++) {
                byte k = (byte)(f.Data[MAGIC + i] ^ (byte)CRIB[i]);
                if (known[i] && key[i] != k) conflicts++;
                key[i] = k; known[i] = true;
            }
        }
        Console.WriteLine("phase 1: crib applied to {0} xml files, {1} conflicts", used, conflicts);
        Console.WriteLine("  key[0..7] = {0}", BitConverter.ToString(key, 0, 8));

        // ---- phase 2: statistical recovery ----
        var score = new long[256];
        int ambiguous = 0;
        for (int i = 0; i < keyLen; i++) {
            if (known[i]) continue;
            Array.Clear(score, 0, 256);
            int samples = 0;
            foreach (var f in all) {
                if (f.Ext == ".o2m" || f.Ext == ".wav") continue;   // binary payloads
                int idx = MAGIC + i;
                if (idx >= f.Data.Length) continue;
                samples++;
                byte c = f.Data[idx];
                for (int k = 0; k < 256; k++) score[k] += W[c ^ k];
            }
            if (samples == 0) { ambiguous++; continue; }
            long best = long.MinValue, second = long.MinValue; int bestK = 0;
            for (int k = 0; k < 256; k++) {
                if (score[k] > best) { second = best; best = score[k]; bestK = k; }
                else if (score[k] > second) second = score[k];
            }
            key[i] = (byte)bestK; known[i] = true;
            if (best == second) ambiguous++;
        }
        Console.WriteLine("phase 2: done, {0} ambiguous positions", ambiguous);

        // ---- periodicity ----
        int probe = Math.Min(keyLen, 1 << 20);
        for (int period = 1; period <= probe / 2; period++) {
            bool ok = true;
            for (int i = 0; i + period < probe; i++)
                if (key[i] != key[i + period]) { ok = false; break; }
            if (ok) { Console.WriteLine("KEYSTREAM PERIOD = {0}", period); break; }
        }
        Console.WriteLine("key[0..31] = {0}", BitConverter.ToString(key, 0, Math.Min(32, keyLen)));

        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "ot2-keystream.bin"), key);

        // ---- sample / write out ----
        if (outDir == null) {
            foreach (var f in all) {
                if (f.Ext != ".xml" && f.Ext != ".lua") continue;
                int n = Math.Min(f.Data.Length - MAGIC, 300);
                if (n <= 0) continue;
                var sb = new StringBuilder();
                for (int i = 0; i < n; i++) sb.Append((char)(f.Data[MAGIC + i] ^ key[i]));
                Console.WriteLine("---- {0} ----", Path.GetFileName(f.Path));
                Console.WriteLine(sb.ToString());
                Console.WriteLine();
                if (--used < 0) break;
                break;
            }
            // show one lua too
            foreach (var f in all) {
                if (f.Ext != ".lua" || f.Data.Length - MAGIC < 100) continue;
                int n = Math.Min(f.Data.Length - MAGIC, 400);
                var sb = new StringBuilder();
                for (int i = 0; i < n; i++) sb.Append((char)(f.Data[MAGIC + i] ^ key[i]));
                Console.WriteLine("---- {0} ----", Path.GetFileName(f.Path));
                Console.WriteLine(sb.ToString());
                break;
            }
        } else {
            int wrote = 0;
            foreach (var f in all) {
                int n = f.Data.Length - MAGIC;
                var outb = new byte[n];
                for (int i = 0; i < n; i++) outb[i] = (byte)(f.Data[MAGIC + i] ^ key[i]);
                string rel = f.Path.Substring(root.Length).TrimStart('\\', '/');
                string dst = Path.Combine(outDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.WriteAllBytes(dst, outb);
                wrote++;
            }
            Console.WriteLine("decrypted {0} files -> {1}", wrote, outDir);
        }
        return 0;
    }
}
