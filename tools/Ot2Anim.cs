using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Builds a mod that changes how fast the baked model animations play.
//
// .O2M models carry a plain-text header. Animation timing lives entirely in
// the FrameAnimationKeys block:
//
//     FrameAnimationKeys
//     {
//         key 0 { StartTimeInAnimation 0   }
//         key 1 { StartTimeInAnimation 259 }
//         ...
//
// There is no duration field - FrameAnimations only names the animation and
// gives start_key/end_key - so those timestamps alone set the speed. Scaling
// them by 0.5 makes an animation play twice as fast.
//
// The numbers are rewritten IN PLACE, right-aligned and space-padded to the
// exact byte width they had. The rest of an .O2M is binary mesh data sitting
// at fixed offsets after the header, so keeping the file length identical
// means none of it moves.
//
// Output is written to mods\<name>\DATA\models\... as plain text; Ot2Mod
// encrypts on apply, because the originals carry the encr header.
class Ot2Anim {
    const string FIELD = "StartTimeInAnimation ";

    static int Main(string[] args) {
        if (args.Length < 1) {
            Console.WriteLine("Ot2Anim <decryptedModelsDir> [factor] [modName]");
            Console.WriteLine();
            Console.WriteLine("  factor   multiplier on the key timestamps.");
            Console.WriteLine("           0.5 = twice as fast (default), 2.0 = half speed.");
            Console.WriteLine("  modName  defaults to animations-<speed>x");
            Console.WriteLine();
            Console.WriteLine("Example:  Ot2Anim ..\\..\\decrypted\\DATA\\models 0.5");
            return 1;
        }

        string modelsDir = Path.GetFullPath(args[0]);
        double factor = args.Length > 1 ? double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 0.5;
        if (factor <= 0) { Console.WriteLine("factor must be positive"); return 1; }
        double speed = 1.0 / factor;
        string modName = args.Length > 2 ? args[2]
            : "animations-" + speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x";

        if (!Directory.Exists(modelsDir)) { Console.WriteLine("No such folder: {0}", modelsDir); return 1; }

        string repo = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", ".."));
        string outRoot = Path.Combine(repo, "mods", modName, "DATA", "models");

        int scanned = 0, changed = 0, keys = 0, skippedWidth = 0;
        foreach (var path in Directory.GetFiles(modelsDir, "*.O2M", SearchOption.AllDirectories)) {
            scanned++;
            var bytes = File.ReadAllBytes(path);
            if (!LooksAnimated(bytes)) continue;

            int n = Rescale(bytes, factor, ref skippedWidth);
            if (n == 0) continue;

            string rel = path.Substring(modelsDir.Length).TrimStart('\\', '/');
            string dst = Path.Combine(outRoot, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.WriteAllBytes(dst, bytes);
            changed++; keys += n;
        }

        if (changed == 0) {
            Console.WriteLine("No animated models found under {0}", modelsDir);
            Console.WriteLine("Decrypt the game data first (launcher: 'Decrypt data').");
            return 1;
        }

        string modTxt = Path.Combine(repo, "mods", modName, "mod.txt");
        if (!File.Exists(modTxt))
            File.WriteAllText(modTxt, string.Format(
                "Model animations at {0}x speed{1}{1}" +
                "Scales every StartTimeInAnimation key in the animated .O2M models{1}" +
                "by {2}. Generated from your own decrypted data - no game content is{1}" +
                "distributed with this repository.{1}{1}" +
                "{3} models, {4} keyframes.{1}",
                speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                Environment.NewLine,
                factor.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                changed, keys));

        Console.WriteLine("scanned {0} models, rewrote {1} ({2} keyframes) at {3}x speed",
            scanned, changed, keys, speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        if (skippedWidth > 0)
            Console.WriteLine("{0} value(s) left alone - would not fit their original width", skippedWidth);
        Console.WriteLine("mod written to mods\\{0}", modName);
        Console.WriteLine("apply it with:  Ot2Mod apply {0}", modName);
        return 0;
    }

    static bool LooksAnimated(byte[] b) {
        int n = Math.Min(b.Length, 256);
        string head = Encoding.ASCII.GetString(b, 0, n);
        return head.StartsWith("O2MT") && head.Contains("frame_animated");
    }

    // Rewrites each timestamp in place, preserving the field's byte width.
    static int Rescale(byte[] b, double factor, ref int skipped) {
        var pat = Encoding.ASCII.GetBytes(FIELD);
        int count = 0;
        int limit = Math.Min(b.Length, 1 << 20);      // the header; never the mesh data

        for (int i = 0; i <= limit - pat.Length; i++) {
            bool hit = true;
            for (int j = 0; j < pat.Length; j++) if (b[i + j] != pat[j]) { hit = false; break; }
            if (!hit) continue;

            int p = i + pat.Length;
            int start = p;
            while (p < b.Length && b[p] >= (byte)'0' && b[p] <= (byte)'9') p++;
            int width = p - start;
            if (width == 0) continue;

            long value = long.Parse(Encoding.ASCII.GetString(b, start, width));
            long scaled = (long)Math.Round(value * factor);
            string s = scaled.ToString();
            if (s.Length > width) { skipped++; continue; }   // cannot widen without moving data

            var repl = Encoding.ASCII.GetBytes(s.PadLeft(width));
            Array.Copy(repl, 0, b, start, width);
            count++;
            i = p;
        }
        return count;
    }
}
