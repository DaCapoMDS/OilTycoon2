using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Applies and reverts mods for Oil Tycoon 2.
//
// The engine has no mod system: no override folder, no load order, no patch
// layer. Changing data means overwriting files inside the install. So this
// keeps every change in mods\ and makes the overwrite reversible by backing
// up each original before it is touched.
//
// Revert restores from those backups rather than from the disc archive,
// because Extract.exe --repair can only restore the 681 files the archive
// stores uncompressed - the per-file compression is not solved.
//
// Files are re-encrypted on apply when the original carried the 'encr'
// header, so a mod is authored in plain text.
class Ot2Mod {
    const int MAGIC = 4;
    const int KEYOFF = 0x270A98;
    const int KEYLEN = 64;

    static byte[] LoadKey(string gameDir) {
        var core = File.ReadAllBytes(Path.Combine(gameDir, "core.dll"));
        var k = new byte[KEYLEN];
        Array.Copy(core, KEYOFF, k, 0, KEYLEN);
        return k;
    }

    static bool IsEncr(byte[] b) {
        return b.Length >= MAGIC && b[0] == 'e' && b[1] == 'n' && b[2] == 'c' && b[3] == 'r';
    }

    static byte[] Encrypt(byte[] plain, byte[] key) {
        var o = new byte[plain.Length + MAGIC];
        o[0] = (byte)'e'; o[1] = (byte)'n'; o[2] = (byte)'c'; o[3] = (byte)'r';
        for (int i = 0; i < plain.Length; i++) o[MAGIC + i] = (byte)(plain[i] ^ key[i % KEYLEN]);
        return o;
    }

    static string ModsRoot, BackupRoot;

    static int Main(string[] args) {
        if (args.Length < 1) { Usage(); return 1; }
        string cmd = args[0].ToLower();

        string repo = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", ".."));
        ModsRoot = Path.Combine(repo, "mods");
        BackupRoot = Path.Combine(ModsRoot, ".backups");
        string gameDir = args.Length > 2 ? args[2] : Path.Combine(repo, "game");

        switch (cmd) {
            case "list":   return List();
            case "status": return Status(gameDir);
            case "apply":  if (args.Length < 2) { Usage(); return 1; } return Apply(args[1], gameDir);
            case "revert": if (args.Length < 2) { Usage(); return 1; } return Revert(args[1], gameDir);
            default: Usage(); return 1;
        }
    }

    static void Usage() {
        Console.WriteLine("Ot2Mod  -  mod manager for Oil Tycoon 2");
        Console.WriteLine();
        Console.WriteLine("  Ot2Mod list");
        Console.WriteLine("  Ot2Mod status          [gameDir]");
        Console.WriteLine("  Ot2Mod apply  <mod>    [gameDir]");
        Console.WriteLine("  Ot2Mod revert <mod>    [gameDir]     (use 'all' to revert everything)");
        Console.WriteLine();
        Console.WriteLine("gameDir defaults to ..\\..\\game next to this executable.");
    }

    static IEnumerable<string> ModNames() {
        if (!Directory.Exists(ModsRoot)) yield break;
        foreach (var d in Directory.GetDirectories(ModsRoot)) {
            string n = Path.GetFileName(d);
            if (n.StartsWith(".")) continue;
            yield return n;
        }
    }

    static bool IsApplied(string mod) {
        return File.Exists(Path.Combine(BackupRoot, mod, "files.txt"));
    }

    static string Describe(string mod) {
        string f = Path.Combine(ModsRoot, mod, "mod.txt");
        if (!File.Exists(f)) return "";
        foreach (var line in File.ReadAllLines(f)) {
            var t = line.Trim();
            if (t.Length > 0 && !t.StartsWith("#")) return t;
        }
        return "";
    }

    // every file in the mod folder except its metadata
    static List<string> ModFiles(string mod) {
        string root = Path.Combine(ModsRoot, mod);
        var list = new List<string>();
        if (!Directory.Exists(root)) return list;
        foreach (var f in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)) {
            string rel = f.Substring(root.Length).TrimStart('\\', '/');
            if (rel.Equals("mod.txt", StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(rel);
        }
        return list;
    }

    static int List() {
        var names = new List<string>(ModNames());
        if (names.Count == 0) { Console.WriteLine("No mods in {0}", ModsRoot); return 0; }
        Console.WriteLine("Mods in {0}:", ModsRoot);
        Console.WriteLine();
        foreach (var m in names) {
            Console.WriteLine("  [{0}] {1,-28} {2}", IsApplied(m) ? "applied" : "       ", m, Describe(m));
            foreach (var f in ModFiles(m)) Console.WriteLine("            {0}", f);
        }
        return 0;
    }

    static int Status(string gameDir) {
        var names = new List<string>(ModNames());
        int applied = 0;
        foreach (var m in names) if (IsApplied(m)) applied++;
        Console.WriteLine("game dir : {0}", gameDir);
        Console.WriteLine("mods     : {0} available, {1} applied", names.Count, applied);
        foreach (var m in names)
            if (IsApplied(m)) {
                Console.WriteLine("  applied: {0}", m);
                foreach (var rel in File.ReadAllLines(Path.Combine(BackupRoot, m, "files.txt")))
                    Console.WriteLine("      {0}", rel);
            }
        if (applied == 0)
            Console.WriteLine("Nothing applied - the install matches the disc apart from files the game rewrites itself.");
        return 0;
    }

    static int Apply(string mod, string gameDir) {
        string root = Path.Combine(ModsRoot, mod);
        if (!Directory.Exists(root)) { Console.WriteLine("No such mod: {0}", mod); return 1; }
        if (IsApplied(mod)) { Console.WriteLine("{0} is already applied. Revert it first.", mod); return 1; }
        if (!File.Exists(Path.Combine(gameDir, "core.dll"))) { Console.WriteLine("Not a game folder: {0}", gameDir); return 1; }

        var key = LoadKey(gameDir);
        var files = ModFiles(mod);
        if (files.Count == 0) { Console.WriteLine("{0} contains no files.", mod); return 1; }

        string backupDir = Path.Combine(BackupRoot, mod);
        Directory.CreateDirectory(backupDir);
        int done = 0;

        // The manifest is appended to as we go, and each entry is flushed
        // before its file is overwritten. If this is interrupted - killed,
        // power cut - every file already touched is listed with a complete
        // backup beside it, so revert still puts everything back. Writing the
        // manifest at the end instead would strand a half-applied install
        // with no recorded way home.
        using (var manifest = new StreamWriter(Path.Combine(backupDir, "files.txt"), true)) {
            manifest.AutoFlush = true;

            foreach (var rel in files) {
                string src = Path.Combine(root, rel);
                string dst = Path.Combine(gameDir, rel);
                bool encrypt = false;

                // 1. capture the original, via a temp file so a partial write
                //    can never masquerade as a good backup
                if (File.Exists(dst)) {
                    var original = File.ReadAllBytes(dst);
                    encrypt = IsEncr(original);
                    string bak = Path.Combine(backupDir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(bak));
                    string tmp = bak + ".part";
                    File.WriteAllBytes(tmp, original);
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Move(tmp, bak);
                } else {
                    string bak = Path.Combine(backupDir, rel + ".absent");
                    Directory.CreateDirectory(Path.GetDirectoryName(bak));
                    File.WriteAllText(bak, "");
                }

                // 2. record it before touching the install
                manifest.WriteLine(rel);

                // 3. now it is safe to overwrite
                var content = File.ReadAllBytes(src);
                if (encrypt && !IsEncr(content)) content = Encrypt(content, key);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.WriteAllBytes(dst, content);

                done++;
                Console.WriteLine("  {0}{1}", rel, encrypt ? "   (encrypted to match the original)" : "");
            }
        }
        Console.WriteLine("Applied {0}: {1} file(s). Originals saved under mods\\.backups\\{0}", mod, done);
        return 0;
    }

    static int Revert(string mod, string gameDir) {
        if (mod.Equals("all", StringComparison.OrdinalIgnoreCase)) {
            int rc = 0;
            foreach (var m in ModNames()) if (IsApplied(m)) rc |= Revert(m, gameDir);
            return rc;
        }
        string backupDir = Path.Combine(BackupRoot, mod);
        string listFile = Path.Combine(backupDir, "files.txt");
        if (!File.Exists(listFile)) { Console.WriteLine("{0} is not applied.", mod); return 1; }

        foreach (var rel in File.ReadAllLines(listFile)) {
            string dst = Path.Combine(gameDir, rel);
            string bak = Path.Combine(backupDir, rel);
            if (File.Exists(bak)) {
                File.WriteAllBytes(dst, File.ReadAllBytes(bak));
                Console.WriteLine("  restored {0}", rel);
            } else if (File.Exists(bak + ".absent")) {
                if (File.Exists(dst)) { File.Delete(dst); Console.WriteLine("  removed  {0}", rel); }
            } else {
                Console.WriteLine("  WARNING no backup for {0} - left as is", rel);
            }
        }
        Directory.Delete(backupDir, true);
        Console.WriteLine("Reverted {0}.", mod);
        return 0;
    }
}
