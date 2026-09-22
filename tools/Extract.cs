using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// ---------------- LZMA range decoder ----------------
class RangeDecoder {
    public const uint kTopValue = 1 << 24;
    public uint Range, Code;
    public byte[] Buf; public int Pos; public int End;
    public void Init(byte[] buf, int pos, int end) {
        Buf = buf; Pos = pos; End = end;
        Code = 0; Range = 0xFFFFFFFF;
        Pos++; // first byte is always 0 and ignored
        for (int i = 0; i < 4; i++) Code = (Code << 8) | ReadByte();
    }
    public byte ReadByte() { return Pos < End ? Buf[Pos++] : (byte)0; }
    public void Normalize() {
        if (Range < kTopValue) { Code = (Code << 8) | ReadByte(); Range <<= 8; }
    }
    public uint DecodeDirectBits(int numTotalBits) {
        uint range = Range, code = Code, result = 0;
        for (int i = numTotalBits; i > 0; i--) {
            range >>= 1;
            uint t = (code - range) >> 31;
            code -= range & (t - 1);
            result = (result << 1) | (1 - t);
            if (range < kTopValue) { code = (code << 8) | ReadByte(); range <<= 8; }
        }
        Range = range; Code = code;
        return result;
    }
    public uint DecodeBit(ushort[] probs, uint index) {
        uint prob = probs[index];
        uint newBound = (Range >> 11) * prob;
        uint symbol;
        if (Code < newBound) {
            Range = newBound;
            probs[index] = (ushort)(prob + ((2048 - prob) >> 5));
            symbol = 0;
        } else {
            Range -= newBound; Code -= newBound;
            probs[index] = (ushort)(prob - (prob >> 5));
            symbol = 1;
        }
        Normalize();
        return symbol;
    }
}

class BitTree {
    public ushort[] Probs; public int NumBits;
    public BitTree(int numBits) { NumBits = numBits; Probs = new ushort[1 << numBits]; Init(); }
    public void Init() { for (int i = 0; i < Probs.Length; i++) Probs[i] = 1024; }
    public uint Decode(RangeDecoder rc) {
        uint m = 1;
        for (int i = NumBits; i > 0; i--) m = (m << 1) + rc.DecodeBit(Probs, m);
        return m - (uint)(1 << NumBits);
    }
    public uint ReverseDecode(RangeDecoder rc) {
        uint m = 1, symbol = 0;
        for (int i = 0; i < NumBits; i++) {
            uint bit = rc.DecodeBit(Probs, m);
            m = (m << 1) + bit;
            symbol |= bit << i;
        }
        return symbol;
    }
    public static uint ReverseDecode(ushort[] probs, uint startIndex, RangeDecoder rc, int numBits) {
        uint m = 1, symbol = 0;
        for (int i = 0; i < numBits; i++) {
            uint bit = rc.DecodeBit(probs, startIndex + m);
            m = (m << 1) + bit;
            symbol |= bit << i;
        }
        return symbol;
    }
}

class LenDecoder {
    public ushort[] Choice = new ushort[2];
    public BitTree[] Low = new BitTree[16];
    public BitTree[] Mid = new BitTree[16];
    public BitTree High = new BitTree(8);
    public LenDecoder() {
        for (int i = 0; i < 16; i++) { Low[i] = new BitTree(3); Mid[i] = new BitTree(3); }
        Choice[0] = 1024; Choice[1] = 1024;
    }
    public uint Decode(RangeDecoder rc, uint posState) {
        if (rc.DecodeBit(Choice, 0) == 0) return Low[posState].Decode(rc);
        if (rc.DecodeBit(Choice, 1) == 0) return 8 + Mid[posState].Decode(rc);
        return 16 + High.Decode(rc);
    }
}

class LzmaDecoder {
    const int kNumPosBitsMax = 4, kNumStates = 12;
    const int kStartPosModelIndex = 4, kEndPosModelIndex = 14, kNumFullDistances = 1 << (kEndPosModelIndex / 2);
    const int kNumAlignBits = 4, kMatchMinLen = 2;

    int lc, lp, pb;
    ushort[] isMatch = new ushort[kNumStates << kNumPosBitsMax];
    ushort[] isRep = new ushort[kNumStates];
    ushort[] isRepG0 = new ushort[kNumStates];
    ushort[] isRepG1 = new ushort[kNumStates];
    ushort[] isRepG2 = new ushort[kNumStates];
    ushort[] isRep0Long = new ushort[kNumStates << kNumPosBitsMax];
    BitTree[] posSlot = new BitTree[4];
    ushort[] posDecoders = new ushort[kNumFullDistances - kEndPosModelIndex];
    BitTree posAlign = new BitTree(kNumAlignBits);
    LenDecoder lenDec = new LenDecoder(), repLenDec = new LenDecoder();
    ushort[][] lit;

    public LzmaDecoder(int lc_, int lp_, int pb_) {
        lc = lc_; lp = lp_; pb = pb_;
        for (int i = 0; i < 4; i++) posSlot[i] = new BitTree(6);
        Fill(isMatch); Fill(isRep); Fill(isRepG0); Fill(isRepG1); Fill(isRepG2);
        Fill(isRep0Long); Fill(posDecoders);
        int n = 1 << (lc + lp);
        lit = new ushort[n][];
        for (int i = 0; i < n; i++) { lit[i] = new ushort[0x300]; Fill(lit[i]); }
    }
    static void Fill(ushort[] a) { for (int i = 0; i < a.Length; i++) a[i] = 1024; }

    static int StateUpdateChar(int s) { if (s < 4) return 0; if (s < 10) return s - 3; return s - 6; }

    public bool Decode(byte[] inBuf, int inPos, int inLen, byte[] outBuf, int outSize) {
        var rc = new RangeDecoder();
        rc.Init(inBuf, inPos, inPos + inLen);
        uint posStateMask = (uint)((1 << pb) - 1);
        uint litPosMask = (uint)((1 << lp) - 1);
        int state = 0;
        uint rep0 = 0, rep1 = 0, rep2 = 0, rep3 = 0;
        int nowPos = 0; byte prevByte = 0;

        while (nowPos < outSize) {
            uint posState = (uint)nowPos & posStateMask;
            if (rc.DecodeBit(isMatch, (uint)(state << kNumPosBitsMax) + posState) == 0) {
                uint litState = (((uint)nowPos & litPosMask) << lc) + (uint)(prevByte >> (8 - lc));
                ushort[] probs = lit[litState];
                uint symbol = 1;
                if (state >= 7) {
                    if (rep0 + 1 > (uint)nowPos) return false;
                    byte matchByte = outBuf[nowPos - (int)rep0 - 1];
                    do {
                        uint matchBit = (uint)(matchByte >> 7) & 1;
                        matchByte <<= 1;
                        uint bit = rc.DecodeBit(probs, ((1 + matchBit) << 8) + symbol);
                        symbol = (symbol << 1) | bit;
                        if (matchBit != bit) {
                            while (symbol < 0x100) symbol = (symbol << 1) | rc.DecodeBit(probs, symbol);
                            break;
                        }
                    } while (symbol < 0x100);
                } else {
                    while (symbol < 0x100) symbol = (symbol << 1) | rc.DecodeBit(probs, symbol);
                }
                prevByte = (byte)symbol;
                outBuf[nowPos++] = prevByte;
                state = StateUpdateChar(state);
                continue;
            }

            uint len;
            if (rc.DecodeBit(isRep, (uint)state) == 1) {
                if (rc.DecodeBit(isRepG0, (uint)state) == 0) {
                    if (rc.DecodeBit(isRep0Long, (uint)(state << kNumPosBitsMax) + posState) == 0) {
                        state = state < 7 ? 9 : 11;
                        if (rep0 + 1 > (uint)nowPos) return false;
                        prevByte = outBuf[nowPos - (int)rep0 - 1];
                        outBuf[nowPos++] = prevByte;
                        continue;
                    }
                } else {
                    uint dist;
                    if (rc.DecodeBit(isRepG1, (uint)state) == 0) dist = rep1;
                    else {
                        if (rc.DecodeBit(isRepG2, (uint)state) == 0) dist = rep2;
                        else { dist = rep3; rep3 = rep2; }
                        rep2 = rep1;
                    }
                    rep1 = rep0; rep0 = dist;
                }
                len = repLenDec.Decode(rc, posState) + kMatchMinLen;
                state = state < 7 ? 8 : 11;
            } else {
                rep3 = rep2; rep2 = rep1; rep1 = rep0;
                len = kMatchMinLen + lenDec.Decode(rc, posState);
                state = state < 7 ? 7 : 10;
                uint lenToPos = len - kMatchMinLen; if (lenToPos > 3) lenToPos = 3;
                uint slot = posSlot[lenToPos].Decode(rc);
                if (slot >= kStartPosModelIndex) {
                    int numDirect = (int)((slot >> 1) - 1);
                    rep0 = (2 | (slot & 1)) << numDirect;
                    if (slot < kEndPosModelIndex)
                        rep0 += BitTree.ReverseDecode(posDecoders, rep0 - slot - 1, rc, numDirect);
                    else {
                        rep0 += rc.DecodeDirectBits(numDirect - kNumAlignBits) << kNumAlignBits;
                        rep0 += posAlign.ReverseDecode(rc);
                        if (rep0 == 0xFFFFFFFF) break; // end marker
                    }
                } else rep0 = slot;
            }
            if (rep0 + 1 > (uint)nowPos) return false;
            for (uint i = 0; i < len && nowPos < outSize; i++) {
                prevByte = outBuf[nowPos - (int)rep0 - 1];
                outBuf[nowPos++] = prevByte;
            }
        }
        return nowPos == outSize;
    }
}

// ---------------- manifest ----------------
class Rec {
    public string Rel; public string Name; public string Ext;
    public uint USize, CSize, CRC; public byte Attr;
    public int LenPos; public long Offset;
}

class Program {
    static uint[] crcTable = BuildCrc();
    static uint[] BuildCrc() {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++) {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }
    static uint Crc32(byte[] b, int len) {
        uint c = 0xFFFFFFFF;
        for (int i = 0; i < len; i++) c = crcTable[(c ^ b[i]) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }

    const string PREFIX = @"E:\MasterInstall\Big Oil\Master Full";

    // Manifest strings are Windows ANSI (cp1252), not ASCII -- several asset
    // filenames carry German umlauts.
    static Encoding Ansi = Encoding.GetEncoding(1252);

    static string RdStr(byte[] b, ref int p) {
        int l = b[p];
        if (l == 0xFF) return null;
        p++;
        string s = Ansi.GetString(b, p, l);
        p += l;
        return s;
    }

    static List<Rec> ParseManifest(byte[] b) {
        var pat = Encoding.ASCII.GetBytes(PREFIX);
        var recs = new List<Rec>();
        for (int i = 1; i < b.Length - pat.Length; i++) {
            if (b[i] != pat[0]) continue;
            bool ok = true;
            for (int j = 1; j < pat.Length; j++) if (b[i + j] != pat[j]) { ok = false; break; }
            if (!ok) continue;
            int lenPos = i - 1;
            int L = b[lenPos];
            if (L <= pat.Length) continue;
            int p = lenPos;
            string src = RdStr(b, ref p); if (src == null || src.Length != L) continue;
            string nm = RdStr(b, ref p); if (nm == null) continue;
            int slash = src.LastIndexOf('\\');
            if (nm != src.Substring(slash + 1)) continue;
            string fold = RdStr(b, ref p); if (fold == null) continue;
            if (fold != src.Substring(0, slash)) continue;
            string ext = RdStr(b, ref p);
            string grp = RdStr(b, ref p);
            if (grp != "Archive") continue;
            var r = new Rec();
            r.Rel = src.Substring(PREFIX.Length + 1);
            r.Name = nm; r.Ext = ext;
            r.USize = BitConverter.ToUInt32(b, p + 3);
            r.Attr = b[p + 7];
            r.LenPos = lenPos;
            recs.Add(r);
            i = p;
        }
        for (int k = 0; k < recs.Count - 1; k++) {
            int n = recs[k + 1].LenPos;
            recs[k].CSize = BitConverter.ToUInt32(b, n - 16);
            recs[k].CRC = BitConverter.ToUInt32(b, n - 12);
        }
        var tail = new List<byte>();
        tail.Add(0x03); tail.AddRange(Encoding.ASCII.GetBytes("All"));
        tail.Add(0x04); tail.AddRange(Encoding.ASCII.GetBytes("None"));
        tail.Add(0); tail.Add(0); tail.Add(0);
        var last = recs[recs.Count - 1];
        for (int i = last.LenPos; i < b.Length - 20; i++) {
            bool ok = true;
            for (int j = 0; j < tail.Count; j++) if (b[i + j] != tail[j]) { ok = false; break; }
            if (ok) {
                last.CSize = BitConverter.ToUInt32(b, i + tail.Count);
                last.CRC = BitConverter.ToUInt32(b, i + tail.Count + 4);
                break;
            }
        }
        return recs;
    }

    static int Main(string[] args) {
        string dat = args[0], exe = args[1], outDir = args[2];
        bool probe = Array.IndexOf(args, "--probe") >= 0;
        bool list = Array.IndexOf(args, "--list") >= 0;
        int LC = 3, LP = 0, PB = 2;
        int pi = Array.IndexOf(args, "--props");
        if (pi >= 0) { var q = args[pi + 1].Split(','); LC = int.Parse(q[0]); LP = int.Parse(q[1]); PB = int.Parse(q[2]); }

        byte[] mb = File.ReadAllBytes(dat);
        var recs = ParseManifest(mb);
        long sumC = 0, sumU = 0;
        foreach (var r in recs) { sumC += r.CSize; sumU += r.USize; }
        long exeLen = new FileInfo(exe).Length;
        long dataStart = exeLen - sumC;
        long off = dataStart;
        foreach (var r in recs) { r.Offset = off; off += r.CSize; }

        Console.WriteLine("records={0} sumC={1:N0} sumU={2:N0} dataStart=0x{3:X}", recs.Count, sumC, sumU, dataStart);

        if (list) {
            foreach (var r in recs)
                Console.WriteLine("{0}\t{1}\t{2}\t0x{3:X}\t{4:X8}", r.Rel, r.USize, r.CSize, r.Offset, r.CRC);
            return 0;
        }

        bool storedOnly = Array.IndexOf(args, "--stored") >= 0;
        int hi = Array.IndexOf(args, "--head");
        int si = Array.IndexOf(args, "--scan");
        bool stats = Array.IndexOf(args, "--stats") >= 0;
        int li = Array.IndexOf(args, "--lzss");
        int vi = Array.IndexOf(args, "--verify");
        int ri = Array.IndexOf(args, "--repair");

        if (ri >= 0) {
            // rewrite any file that fails verification, when it is stored uncompressed
            string root = args[ri + 1];
            int fixedN = 0, cannot = 0, fine = 0;
            using (var fs = File.OpenRead(exe)) {
                foreach (var r in recs) {
                    string p = Path.Combine(root, r.Rel);
                    bool bad = true;
                    if (File.Exists(p)) {
                        var d = File.ReadAllBytes(p);
                        bad = d.Length != r.USize || Crc32(d, d.Length) != r.CRC;
                    }
                    if (!bad) { fine++; continue; }
                    if (r.CSize != r.USize) {
                        cannot++;
                        Console.WriteLine("  CANNOT (compressed) {0}", r.Rel);
                        continue;
                    }
                    var buf = new byte[r.CSize];
                    fs.Position = r.Offset;
                    ReadFull(fs, buf, (int)r.CSize);
                    if (Crc32(buf, buf.Length) != r.CRC) {
                        cannot++;
                        Console.WriteLine("  ARCHIVE CRC MISMATCH {0}", r.Rel);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(p));
                    File.WriteAllBytes(p, buf);
                    Console.WriteLine("  REPAIRED {0} ({1:N0} bytes)", r.Rel, r.USize);
                    fixedN++;
                }
            }
            Console.WriteLine("repair: ok-already={0} repaired={1} unrepairable={2}", fine, fixedN, cannot);
            return cannot == 0 ? 0 : 2;
        }

        if (vi >= 0) {
            // verify an installed tree against the archive manifest (size + CRC32)
            string root = args[vi + 1];
            int ok = 0, badCrc = 0, badSize = 0, missing = 0;
            var problems = new List<string>();
            foreach (var r in recs) {
                string p = Path.Combine(root, r.Rel);
                if (!File.Exists(p)) { missing++; problems.Add("MISSING  " + r.Rel); continue; }
                var fi = new FileInfo(p);
                if (fi.Length != r.USize) { badSize++; problems.Add(string.Format("SIZE     {0} want {1} got {2}", r.Rel, r.USize, fi.Length)); continue; }
                var data = File.ReadAllBytes(p);
                if (Crc32(data, data.Length) != r.CRC) { badCrc++; problems.Add("CRC      " + r.Rel); continue; }
                ok++;
            }
            Console.WriteLine("VERIFY {0}", root);
            Console.WriteLine("  match={0}  missing={1}  wrong-size={2}  wrong-crc={3}  (of {4})", ok, missing, badSize, badCrc, recs.Count);
            int shownV = 0;
            foreach (var p in problems) { Console.WriteLine("  " + p); if (++shownV >= 40) { Console.WriteLine("  ... {0} more", problems.Count - shownV); break; } }
            return (missing + badSize + badCrc) == 0 ? 0 : 2;
        }

        using (var fs = File.OpenRead(exe)) {
            if (li >= 0) {
                string want = args[li + 1];
                Rec t = null;
                foreach (var r in recs) if (r.Rel.Equals(want, StringComparison.OrdinalIgnoreCase) && r.CSize != r.USize) { t = r; break; }
                var blk = new byte[t.CSize];
                fs.Position = t.Offset;
                ReadFull(fs, blk, (int)t.CSize);
                Console.WriteLine("{0} U={1} C={2} hdr={3:x2} {4:x2}", t.Rel, t.USize, t.CSize, blk[0], blk[1]);
                int bit = 0;                       // bit index into stream starting at blk[2]
                Func<int, int> GetBit = idx => (blk[2 + (idx >> 3)] >> (idx & 7)) & 1;
                var outb = new List<byte>();
                for (int ev = 0; ev < 14; ev++) {
                    int flag = GetBit(bit); bit++;
                    if (flag == 0) {
                        int v = 0;
                        for (int k = 0; k < 8; k++) { v |= GetBit(bit) << k; bit++; }
                        outb.Add((byte)v);
                        Console.WriteLine("  pos {0,4}: LIT {1:x2}", outb.Count - 1, v);
                    } else {
                        var sb = new StringBuilder();
                        for (int k = 0; k < 48; k++) sb.Append((char)('0' + GetBit(bit + k)));
                        Console.WriteLine("  pos {0,4}: MATCH  next48={1}", outb.Count, sb);
                        break;
                    }
                }
                return 0;
            }
            if (stats) {
                var b1 = new Dictionary<int, int>();
                var b2 = new Dictionary<int, int>();
                var hdr = new byte[8];
                foreach (var r in recs) {
                    if (r.CSize == r.USize || r.CSize < 8) continue;
                    fs.Position = r.Offset;
                    ReadFull(fs, hdr, 8);
                    int k1 = hdr[0], k2 = (hdr[0] << 8) | hdr[1];
                    b1[k1] = b1.ContainsKey(k1) ? b1[k1] + 1 : 1;
                    b2[k2] = b2.ContainsKey(k2) ? b2[k2] + 1 : 1;
                }
                Console.WriteLine("-- first byte distribution --");
                var l1 = new List<KeyValuePair<int, int>>(b1); l1.Sort((x, y) => y.Value.CompareTo(x.Value));
                foreach (var kv in l1) Console.WriteLine("  {0:x2}  {1}", kv.Key, kv.Value);
                Console.WriteLine("-- first 2 bytes (top 12) --");
                var l2 = new List<KeyValuePair<int, int>>(b2); l2.Sort((x, y) => y.Value.CompareTo(x.Value));
                for (int k = 0; k < Math.Min(12, l2.Count); k++) Console.WriteLine("  {0:x4}  {1}", l2[k].Key, l2[k].Value);
                Console.WriteLine("distinct first-2-byte values: {0}", l2.Count);
                return 0;
            }
            if (si >= 0) {
                // brute force: stream start offset x props, validated against known PE plaintext
                byte[] expect = { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00 };
                Rec t = null;
                foreach (var r in recs) if (r.Rel.Equals("game.exe", StringComparison.OrdinalIgnoreCase)) { t = r; break; }
                var cb0 = new byte[t.CSize];
                fs.Position = t.Offset;
                ReadFull(fs, cb0, (int)t.CSize);
                Console.WriteLine("scanning {0} C={1}", t.Rel, t.CSize);
                int hits = 0;
                for (int start = 0; start <= 64; start++)
                for (int lc = 0; lc <= 8; lc++)
                for (int lp = 0; lp <= 4; lp++)
                for (int pb = 0; pb <= 4; pb++) {
                    if (lc + lp > 8) continue;
                    var ob = new byte[Math.Min((int)t.USize, 4096)];
                    try { new LzmaDecoder(lc, lp, pb).Decode(cb0, start, cb0.Length - start, ob, ob.Length); }
                    catch { continue; }
                    bool m = true;
                    for (int k = 0; k < expect.Length; k++) if (ob[k] != expect[k]) { m = false; break; }
                    if (m) {
                        Console.WriteLine("HIT start={0} lc={1} lp={2} pb={3}", start, lc, lp, pb);
                        if (++hits > 12) return 0;
                    }
                }
                Console.WriteLine("hits={0}", hits);
                return 0;
            }
            if (hi >= 0) {
                string want = args[hi + 1];   // target file (substring match)
                Rec t = null;
                foreach (var r in recs) if (r.Rel.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0 && r.CSize != r.USize) { t = r; break; }
                Console.WriteLine("target {0} U={1} C={2} @0x{3:X}", t.Rel, t.USize, t.CSize, t.Offset);
                var cb0 = new byte[t.CSize];
                fs.Position = t.Offset;
                ReadFull(fs, cb0, (int)t.CSize);
                for (int lc = 0; lc <= 8; lc++)
                for (int lp = 0; lp <= 4; lp++)
                for (int pb = 0; pb <= 4; pb++) {
                    if (lc + lp > 8) continue;
                    var ob = new byte[t.USize];
                    try { new LzmaDecoder(lc, lp, pb).Decode(cb0, 0, cb0.Length, ob, (int)t.USize); } catch { }
                    var sb = new StringBuilder();
                    for (int k = 0; k < 12; k++) sb.AppendFormat("{0:x2} ", ob[k]);
                    uint c = Crc32(ob, ob.Length);
                    string flag = (c == t.CRC) ? "  <== CRC MATCH" : "";
                    if (ob[0] != 0 || lc + lp + pb == 0 || flag != "")
                        Console.WriteLine("lc={0} lp={1} pb={2}: {3}{4}", lc, lp, pb, sb, flag);
                }
                return 0;
            }
            if (storedOnly) {
                int sOk = 0, sBad = 0, shownS = 0;
                foreach (var r in recs) {
                    if (r.CSize != r.USize) continue;
                    var cb = new byte[r.CSize];
                    fs.Position = r.Offset;
                    ReadFull(fs, cb, (int)r.CSize);
                    uint c = Crc32(cb, cb.Length);
                    if (c == r.CRC) sOk++;
                    else {
                        sBad++;
                        if (shownS++ < 5)
                            Console.WriteLine("  mismatch {0} size={1} want={2:X8} got={3:X8}", r.Rel, r.USize, r.CRC, c);
                    }
                }
                Console.WriteLine("stored files: CRC32 ok={0} bad={1}", sOk, sBad);
                return 0;
            }
            if (probe) {
                var targets = new List<Rec>();
                foreach (var r in recs) {
                    if (r.CSize != r.USize && r.USize > 0 && r.USize < 200000) { targets.Add(r); if (targets.Count == 3) break; }
                }
                Console.WriteLine("probe targets: {0}", targets.Count);
                for (int lc = 0; lc <= 8; lc++)
                for (int lp = 0; lp <= 4; lp++)
                for (int pb = 0; pb <= 4; pb++) {
                    if (lc + lp > 8) continue;
                    bool all = true;
                    foreach (var r in targets) {
                        var cb = new byte[r.CSize];
                        fs.Position = r.Offset;
                        ReadFull(fs, cb, (int)r.CSize);
                        var ob = new byte[r.USize];
                        bool ok;
                        try { ok = new LzmaDecoder(lc, lp, pb).Decode(cb, 0, cb.Length, ob, (int)r.USize); }
                        catch { ok = false; }
                        if (!ok || Crc32(ob, ob.Length) != r.CRC) { all = false; break; }
                    }
                    if (all) { Console.WriteLine("MATCH lc={0} lp={1} pb={2}", lc, lp, pb); return 0; }
                }
                Console.WriteLine("no props matched");
                return 1;
            }

            int good = 0, bad = 0, stored = 0;
            var failures = new List<string>();
            foreach (var r in recs) {
                var cb = new byte[r.CSize];
                fs.Position = r.Offset;
                ReadFull(fs, cb, (int)r.CSize);
                byte[] ob;
                if (r.CSize == r.USize) { ob = cb; stored++; }
                else {
                    ob = new byte[r.USize];
                    bool ok;
                    try { ok = new LzmaDecoder(LC, LP, PB).Decode(cb, 0, cb.Length, ob, (int)r.USize); }
                    catch (Exception) { ok = false; }
                    if (!ok) { bad++; failures.Add(r.Rel + " (decode)"); continue; }
                }
                if (Crc32(ob, ob.Length) != r.CRC) { bad++; failures.Add(r.Rel + " (crc)"); continue; }
                string dest = Path.Combine(outDir, r.Rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.WriteAllBytes(dest, ob);
                good++;
            }
            Console.WriteLine("OK={0} stored={1} FAILED={2}", good, stored, bad);
            int shown = 0;
            foreach (var f in failures) { Console.WriteLine("  FAIL " + f); if (++shown >= 40) break; }
        }
        return 0;
    }

    static void ReadFull(FileStream fs, byte[] b, int len) {
        int r = 0;
        while (r < len) { int n = fs.Read(b, r, len - r); if (n <= 0) break; r += n; }
    }
}
