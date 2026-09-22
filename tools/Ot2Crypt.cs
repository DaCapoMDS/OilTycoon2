using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

// Oil Tycoon 2 "encr" container.
//   layout : 'e','n','c','r' then payload
//   cipher : plain[i] = payload[i] ^ KEY[i % 64]
//   KEY    : 64 bytes held in core.dll at 0x270A98
// XOR is symmetric, so the same routine encrypts and decrypts.
class Ot2Crypt {
    const int MAGIC = 4;
    const int KEYOFF = 0x270A98;
    const int KEYLEN = 64;

    static byte[] LoadKey(string coreDll) {
        var core = File.ReadAllBytes(coreDll);
        var k = new byte[KEYLEN];
        Array.Copy(core, KEYOFF, k, 0, KEYLEN);
        return k;
    }

    static bool IsEncr(byte[] b) {
        return b.Length >= MAGIC && b[0] == 'e' && b[1] == 'n' && b[2] == 'c' && b[3] == 'r';
    }

    static int Main(string[] args) {
        string mode = args[0];                  // decrypt | encrypt | key
        string coreDll = args[1];
        var key = LoadKey(coreDll);

        if (mode == "key") {
            Console.WriteLine("KEY @0x{0:X} ({1} bytes):", KEYOFF, KEYLEN);
            for (int r = 0; r < KEYLEN; r += 16) {
                var sb = new StringBuilder();
                for (int i = r; i < r + 16; i++) sb.AppendFormat("{0:X2} ", key[i]);
                sb.Append(" |");
                for (int i = r; i < r + 16; i++) sb.Append(key[i] >= 32 && key[i] < 127 ? (char)key[i] : '.');
                sb.Append('|');
                Console.WriteLine("  " + sb);
            }
            return 0;
        }

        string src = args[2], dst = args[3];
        int done = 0, skipped = 0, xmlOk = 0, xmlBad = 0;
        var bad = new List<string>();

        foreach (var p in Directory.GetFiles(src, "*.*", SearchOption.AllDirectories)) {
            var b = File.ReadAllBytes(p);
            string rel = p.Substring(src.Length).TrimStart('\\', '/');
            string outPath = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));

            if (mode == "decrypt") {
                if (!IsEncr(b)) { skipped++; continue; }
                int n = b.Length - MAGIC;
                var o = new byte[n];
                for (int i = 0; i < n; i++) o[i] = (byte)(b[MAGIC + i] ^ key[i % KEYLEN]);
                File.WriteAllBytes(outPath, o);
                done++;
                if (Path.GetExtension(p).ToLower() == ".xml") {
                    try {
                        var d = new XmlDocument();
                        d.XmlResolver = null;
                        using (var ms = new MemoryStream(o)) d.Load(ms);
                        xmlOk++;
                    } catch (Exception ex) { xmlBad++; if (bad.Count < 10) bad.Add(rel + " :: " + ex.Message.Split('\n')[0]); }
                }
            } else { // encrypt
                if (IsEncr(b)) { skipped++; continue; }   // already encrypted
                var o = new byte[b.Length + MAGIC];
                o[0] = (byte)'e'; o[1] = (byte)'n'; o[2] = (byte)'c'; o[3] = (byte)'r';
                for (int i = 0; i < b.Length; i++) o[MAGIC + i] = (byte)(b[i] ^ key[i % KEYLEN]);
                File.WriteAllBytes(outPath, o);
                done++;
            }
        }
        Console.WriteLine("{0}: {1} files, {2} skipped", mode, done, skipped);
        if (mode == "decrypt") {
            Console.WriteLine("XML well-formed: {0} ok, {1} failed", xmlOk, xmlBad);
            foreach (var s in bad) Console.WriteLine("   " + s);
        }
        return 0;
    }
}
