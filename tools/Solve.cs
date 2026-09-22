using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Recover the LZSS match encoding from a known plaintext/ciphertext pair.
// Branch only on (len, bitsConsumed) -- the distance does not affect the output
// stream, because output is verified against the known plaintext anyway.
class Solve {
    static byte[] plain;
    static byte[] comp;

    static int GetBit(int idx) { return (comp[idx >> 3] >> (idx & 7)) & 1; }
    static int GetBits(int idx, int n) {
        int v = 0;
        for (int k = 0; k < n; k++) v |= GetBit(idx + k) << k;
        return v;
    }
    static int MaxBit;

    // all match lengths at `pos` that some earlier distance can reproduce
    static List<int> AchievableLens(int pos, int maxLen, int maxDist) {
        var res = new List<int>();
        int best = 0;
        for (int dist = 1; dist <= Math.Min(pos, maxDist); dist++) {
            int l = 0;
            while (l < maxLen && pos + l < plain.Length && plain[pos + l] == plain[pos - dist + l]) l++;
            if (l > best) best = l;
        }
        for (int l = 2; l <= best; l++) res.Add(l);
        return res;
    }

    static List<int> DistsFor(int pos, int len, int maxDist) {
        var res = new List<int>();
        for (int dist = 1; dist <= Math.Min(pos, maxDist); dist++) {
            bool ok = true;
            for (int i = 0; i < len; i++) if (plain[pos + i] != plain[pos - dist + i]) { ok = false; break; }
            if (ok) res.Add(dist);
            if (res.Count > 6) break;
        }
        return res;
    }

    class St { public int bit, pos; public string path; }

    static int Main(string[] args) {
        string exe = args[0];
        long streamOff = Convert.ToInt64(args[1], 16);
        plain = File.ReadAllBytes(args[2]);
        int take = 8192;
        comp = new byte[take];
        using (var fs = File.OpenRead(exe)) {
            fs.Position = streamOff + 2;
            int r = 0; while (r < take) { int n = fs.Read(comp, r, take - r); if (n <= 0) break; r += n; }
        }
        MaxBit = take * 8 - 64;

        // walk literals up to the first match
        int bit = 0, pos = 0;
        while (GetBit(bit) == 0) {
            if (GetBits(bit + 1, 8) != plain[pos]) { Console.WriteLine("desync at {0}", pos); return 1; }
            bit += 9; pos++;
        }
        var sb = new StringBuilder();
        for (int k = 0; k < 40; k++) sb.Append((char)('0' + GetBit(bit + 1 + k)));
        Console.WriteLine("first MATCH at out={0} bits after flag = {1}", pos, sb);
        var lens0 = AchievableLens(pos, 200, 65536);
        foreach (var l in lens0) {
            var ds = DistsFor(pos, l, 65536);
            Console.WriteLine("   achievable len={0} dists={1}", l, string.Join(",", ds.ConvertAll(x => x.ToString()).ToArray()));
        }

        // BFS over (len, B) choices, requiring consistency for `depth` further events
        int depth = int.Parse(args[3]);
        var frontier = new List<St>();
        foreach (int len in lens0)
            for (int B = 2; B <= 48; B++) {
                var ds = DistsFor(pos, len, 65536);
                frontier.Add(new St { bit = bit + 1 + B, pos = pos + len,
                    path = string.Format("[d={0} l={1} B={2}]", ds.Count > 0 ? ds[0] : -1, len, B) });
            }

        for (int d = 0; d < depth; d++) {
            var next = new List<St>();
            foreach (var s in frontier) {
                if (s.bit >= MaxBit || s.pos >= plain.Length - 300) continue;
                if (GetBit(s.bit) == 0) {
                    if (GetBits(s.bit + 1, 8) == plain[s.pos])
                        next.Add(new St { bit = s.bit + 9, pos = s.pos + 1, path = s.path });
                } else {
                    foreach (int len in AchievableLens(s.pos, 200, 65536)) {
                        var ds = DistsFor(s.pos, len, 65536);
                        for (int B = 2; B <= 48; B++)
                            next.Add(new St { bit = s.bit + 1 + B, pos = s.pos + len,
                                path = s.path + string.Format("[d={0} l={1} B={2}]", ds.Count > 0 ? ds[0] : -1, len, B) });
                    }
                }
            }
            if (next.Count > 3000000) { Console.WriteLine("frontier blew up at depth {0} ({1})", d, next.Count); break; }
            frontier = next;
            Console.WriteLine("depth {0}: {1} states", d, frontier.Count);
            if (frontier.Count == 0) break;
        }

        Console.WriteLine("-- surviving paths (up to 12) --");
        int shown2 = 0;
        var seen = new HashSet<string>();
        foreach (var s in frontier) {
            if (!seen.Add(s.path)) continue;
            Console.WriteLine("   " + s.path);
            if (++shown2 >= 12) break;
        }
        Console.WriteLine("distinct paths: {0}", seen.Count);
        return 0;
    }
}
