using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// A small front-end over the revival pipeline, so this is usable without a
// terminal. It drives setup.ps1 and the command line tools rather than
// duplicating their logic.
//
// Theming note: the window icon is loaded at RUNTIME from the user's own
// installed copy (BO-icon.ico). No game asset is shipped with this tool.
// The palette below is original - crude oil black, brass and derrick amber -
// chosen to sit alongside the game, not copied from it.
class Launcher : Form {
    static readonly Color Ink     = Color.FromArgb(24, 22, 19);   // crude black
    static readonly Color Panel   = Color.FromArgb(34, 31, 27);
    static readonly Color Field   = Color.FromArgb(44, 40, 35);
    static readonly Color Brass   = Color.FromArgb(232, 163,  61); // derrick amber
    static readonly Color BrassDk = Color.FromArgb(150, 102,  30);
    static readonly Color Parch   = Color.FromArgb(226, 219, 205); // aged paper
    static readonly Color Muted   = Color.FromArgb(146, 136, 121);

    string repoRoot;
    TextBox tbImage, tbTarget;
    CheckBox cbDecrypt;
    Button btInstall, btVerify, btPlay, btDecrypt, btBrowseImage, btBrowseTarget;
    RichTextBox log;
    Label lbImageInfo, lbStatus;
    ProgressBar bar;
    PictureBox icon;

    [STAThread]
    static void Main() {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new Launcher());
    }

    public Launcher() {
        repoRoot = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(Application.ExecutablePath), "..", ".."));

        Text = "Oil Tycoon 2 - Revival Launcher";
        ClientSize = new Size(820, 660);
        MinimumSize = new Size(720, 600);
        Font = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Ink;
        ForeColor = Parch;

        BuildHeader();

        int y = 96;

        Add(SectionLabel("1.   Disc image        your own Big Oil CD, dumped as .bin/.cue or .iso", 14, y));
        y += 26;

        tbImage = Field_(14, y, 640);
        tbImage.TextChanged += (s, e) => InspectImage();
        btBrowseImage = Btn("Browse...", 664, y - 2, 110, 26, false);
        btBrowseImage.Click += (s, e) => BrowseImage();
        Add(tbImage); Add(btBrowseImage);
        y += 28;

        lbImageInfo = new Label { Left = 16, Top = y, Width = 760, ForeColor = Muted, BackColor = Color.Transparent };
        Add(lbImageInfo);
        y += 30;

        Add(SectionLabel("2.   Install to", 14, y));
        y += 26;

        tbTarget = Field_(14, y, 640);
        tbTarget.Text = Path.Combine(repoRoot, "game");
        tbTarget.TextChanged += (s, e) => LoadGameIcon();
        btBrowseTarget = Btn("Browse...", 664, y - 2, 110, 26, false);
        btBrowseTarget.Click += (s, e) => BrowseTarget();
        Add(tbTarget); Add(btBrowseTarget);
        y += 30;

        cbDecrypt = new CheckBox {
            Text = "Also decrypt the data files  (needed for modding; writes to decrypted\\)",
            Left = 16, Top = y, Width = 660, ForeColor = Parch, BackColor = Color.Transparent,
            FlatStyle = FlatStyle.Flat
        };
        Add(cbDecrypt);
        y += 34;

        btInstall = Btn("Install and verify", 14, y, 158, 34, true);
        btInstall.Click += (s, e) => RunAsync(DoInstall);
        btVerify = Btn("Verify existing", 180, y, 132, 34, false);
        btVerify.Click += (s, e) => RunAsync(DoVerify);
        btDecrypt = Btn("Decrypt data", 320, y, 122, 34, false);
        btDecrypt.Click += (s, e) => RunAsync(DoDecrypt);
        btPlay = Btn("Play", 450, y, 104, 34, true);
        btPlay.Click += (s, e) => DoPlay();
        Add(btInstall); Add(btVerify); Add(btDecrypt); Add(btPlay);
        y += 42;

        bar = new ProgressBar {
            Left = 14, Top = y, Width = 760, Height = 4,
            Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30, Visible = false,
            ForeColor = Brass, BackColor = Panel,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        Add(bar);
        y += 12;

        log = new RichTextBox {
            Left = 14, Top = y, Width = 760, Height = 372,
            ReadOnly = true, BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 9f),
            BackColor = Panel, ForeColor = Parch,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };
        Add(log);

        lbStatus = new Label {
            Left = 16, Top = y + 380, Width = 760, ForeColor = Muted, BackColor = Color.Transparent,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };
        Add(lbStatus);

        Say("Oil Tycoon 2 / Big Oil  -  revival launcher");
        Say("");
        Say("Installs a game you already own, from your own disc.");
        Say("No game files ship with this tool.");
        Say("");
        Say("Pick your disc image above, then \"Install and verify\".");
        Say("Every extracted file is checked against the CRC32 values held in");
        Say("the installer's own manifest, so a bad dump shows up immediately.");
        Say("");

        AutoDetect();
        LoadGameIcon();
        Status("Ready.");
    }

    // ---- chrome -------------------------------------------------------------

    void BuildHeader() {
        var head = new Panel {
            Left = 0, Top = 0, Width = ClientSize.Width, Height = 78,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Panel
        };
        head.Paint += (s, e) => {
            var r = head.ClientRectangle;
            using (var b = new LinearGradientBrush(r, Color.FromArgb(46, 40, 33), Ink, 90f))
                e.Graphics.FillRectangle(b, r);
            using (var p = new Pen(BrassDk, 2))
                e.Graphics.DrawLine(p, 0, r.Bottom - 1, r.Right, r.Bottom - 1);
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (var f = new Font("Segoe UI", 17f, FontStyle.Bold))
            using (var b = new SolidBrush(Brass))
                e.Graphics.DrawString("OIL TYCOON 2", f, b, 70, 14);
            using (var f = new Font("Segoe UI", 8.5f))
            using (var b = new SolidBrush(Muted))
                e.Graphics.DrawString("Big Oil  -  build an oil empire   ·   2006, revived for Windows 11",
                                      f, b, 72, 45);
        };
        icon = new PictureBox {
            Left = 16, Top = 14, Width = 48, Height = 48,
            SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent
        };
        head.Controls.Add(icon);
        Controls.Add(head);
    }

    Label SectionLabel(string text, int x, int y) {
        return new Label {
            Text = text, Left = x, Top = y, Width = 700,
            ForeColor = Brass, BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };
    }

    TextBox Field_(int x, int y, int w) {
        return new TextBox {
            Left = x, Top = y, Width = w,
            BackColor = Field, ForeColor = Parch, BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
    }

    Button Btn(string text, int x, int y, int w, int h, bool primary) {
        var b = new Button {
            Text = text, Left = x, Top = y, Width = w, Height = h,
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false,
            BackColor = primary ? BrassDk : Field,
            ForeColor = primary ? Color.FromArgb(28, 24, 18) : Parch,
            Cursor = Cursors.Hand
        };
        if (primary) { b.BackColor = Brass; b.Font = new Font("Segoe UI", 9f, FontStyle.Bold); }
        b.FlatAppearance.BorderColor = primary ? Brass : BrassDk;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = primary
            ? Color.FromArgb(245, 184, 92) : Color.FromArgb(60, 54, 46);
        return b;
    }

    // Uses the icon from the player's own installed copy. Nothing bundled.
    void LoadGameIcon() {
        try {
            foreach (var dir in new[] { tbTarget != null ? tbTarget.Text.Trim() : null,
                                        Path.Combine(repoRoot, "game"), repoRoot }) {
                if (string.IsNullOrEmpty(dir)) continue;
                string p = Path.Combine(dir, "BO-icon.ico");
                if (!File.Exists(p)) continue;
                using (var fs = File.OpenRead(p)) {
                    var ic = new Icon(fs, new Size(48, 48));
                    icon.Image = ic.ToBitmap();
                    Icon = ic;
                }
                return;
            }
            icon.Image = null;
        } catch { /* cosmetic only */ }
    }

    void Add(Control c) { Controls.Add(c); c.BringToFront(); }

    void Say(string s) {
        if (log.InvokeRequired) { log.BeginInvoke((MethodInvoker)(() => Say(s))); return; }
        log.AppendText(s + Environment.NewLine);
        log.SelectionStart = log.TextLength;
        log.ScrollToCaret();
    }

    void Status(string s) {
        if (InvokeRequired) { BeginInvoke((MethodInvoker)(() => Status(s))); return; }
        lbStatus.Text = s;
    }

    // ---- disc image handling ------------------------------------------------

    void BrowseImage() {
        using (var d = new OpenFileDialog()) {
            d.Title = "Select your Big Oil disc image";
            d.Filter = "Disc images (*.bin;*.cue;*.iso)|*.bin;*.cue;*.iso|All files (*.*)|*.*";
            if (d.ShowDialog(this) == DialogResult.OK) tbImage.Text = d.FileName;
        }
    }

    void BrowseTarget() {
        using (var d = new FolderBrowserDialog()) {
            d.Description = "Where should the game be installed?";
            if (d.ShowDialog(this) == DialogResult.OK) tbTarget.Text = d.SelectedPath;
        }
    }

    void AutoDetect() {
        foreach (var dir in new[] { repoRoot, Path.Combine(repoRoot, "Big-Oil_Win_EN_Disc-Image") }) {
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.GetFiles(dir, "*.bin")) { tbImage.Text = f; return; }
            foreach (var f in Directory.GetFiles(dir, "*.iso")) { tbImage.Text = f; return; }
        }
    }

    void InspectImage() {
        string p = tbImage.Text.Trim();
        if (p.Length == 0 || !File.Exists(p)) { lbImageInfo.Text = ""; return; }
        var fi = new FileInfo(p);
        string ext = Path.GetExtension(p).ToLower();
        var sb = new StringBuilder();
        sb.AppendFormat("{0:N0} bytes", fi.Length);

        if (ext == ".bin") {
            if (fi.Length % 2352 == 0)
                sb.AppendFormat("   ·   {0:N0} sectors, MODE1/2352 - looks like a raw dump", fi.Length / 2352);
            else
                sb.Append("   ·   not a multiple of 2352; may not be a raw MODE1/2352 dump");
        } else if (ext == ".iso") {
            if (fi.Length % 2048 == 0)
                sb.AppendFormat("   ·   {0:N0} sectors, 2048-byte ISO", fi.Length / 2048);
            else
                sb.Append("   ·   not a multiple of 2048; unexpected for an ISO");
        } else if (ext == ".cue") {
            sb.Append("   ·   cue sheet - point at the .bin instead");
        }

        if (fi.Length == 319756752L) sb.Append("      [matches the known-good EN release dump]");
        lbImageInfo.Text = sb.ToString();
        lbImageInfo.ForeColor = (fi.Length == 319756752L) ? Brass : Muted;
    }

    // ---- running work -------------------------------------------------------

    void RunAsync(ThreadStart work) {
        SetBusy(true);
        var t = new Thread(() => {
            try { work(); }
            catch (Exception ex) { Say("ERROR: " + ex.Message); }
            finally { SetBusy(false); }
        });
        t.IsBackground = true;
        t.Start();
    }

    void SetBusy(bool busy) {
        if (InvokeRequired) { BeginInvoke((MethodInvoker)(() => SetBusy(busy))); return; }
        btInstall.Enabled = btVerify.Enabled = btDecrypt.Enabled = btPlay.Enabled = !busy;
        btBrowseImage.Enabled = btBrowseTarget.Enabled = !busy;
        bar.Visible = busy;
        if (!busy) Status("Ready.");
    }

    int Run(string exe, string args, string workDir) {
        var psi = new ProcessStartInfo(exe, args) {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workDir ?? repoRoot
        };
        using (var p = new Process()) {
            p.StartInfo = psi;
            p.OutputDataReceived += (s, e) => { if (e.Data != null) Say(e.Data); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) Say(e.Data); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();
            return p.ExitCode;
        }
    }

    string ToolsBin { get { return Path.Combine(repoRoot, "tools", "bin"); } }

    void EnsureTools() {
        if (File.Exists(Path.Combine(ToolsBin, "Extract.exe"))) return;
        Say("Building tools (one time)...");
        Run("powershell", "-NoProfile -ExecutionPolicy Bypass -File \"" +
            Path.Combine(repoRoot, "tools", "build.ps1") + "\"", repoRoot);
    }

    void DoInstall() {
        string img = tbImage.Text.Trim();
        if (!File.Exists(img)) { Say("Pick a disc image first."); return; }
        if (Path.GetExtension(img).ToLower() == ".cue") {
            string bin = Path.ChangeExtension(img, ".bin");
            if (File.Exists(bin)) { Say("Using " + Path.GetFileName(bin) + " instead of the cue sheet."); img = bin; }
        }
        EnsureTools();
        Status("Installing - a UAC prompt and the 2006 wizard will appear.");
        Say("");
        Say("=== Install ===");
        Say("A UAC prompt will appear, then the original setup wizard.");
        Say("Install anywhere you like - this launcher finds it afterwards.");
        Say("");

        string args = "-NoProfile -ExecutionPolicy Bypass -File \"" +
                      Path.Combine(repoRoot, "setup.ps1") + "\"" +
                      " -Bin \"" + img + "\"" +
                      " -Target \"" + tbTarget.Text.Trim() + "\"" +
                      (cbDecrypt.Checked ? " -Decrypt" : "");
        int rc = Run("powershell", args, repoRoot);
        Say(rc == 0 ? "\r\nInstall finished." : "\r\nInstall reported exit code " + rc + ".");
        LoadGameIcon();
    }

    void DoVerify() {
        string img = tbImage.Text.Trim();
        string target = tbTarget.Text.Trim();
        if (!File.Exists(img)) { Say("Pick a disc image first - verification reads the manifest from it."); return; }
        if (!File.Exists(Path.Combine(target, "game.exe"))) { Say("No game.exe at " + target); return; }
        EnsureTools();

        // The manifest lives inside the disc image, so setup.ps1 -VerifyOnly
        // mounts it for us rather than us duplicating the mount logic here.
        Status("Verifying 3,337 files...");
        Say("");
        Say("=== Verify ===");
        string args = "-NoProfile -ExecutionPolicy Bypass -File \"" +
                      Path.Combine(repoRoot, "setup.ps1") + "\"" +
                      " -Bin \"" + img + "\" -Target \"" + target + "\" -VerifyOnly";
        Run("powershell", args, repoRoot);
    }

    void DoDecrypt() {
        EnsureTools();
        string target = tbTarget.Text.Trim();
        string core = Path.Combine(target, "core.dll");
        if (!File.Exists(core)) { Say("No install at " + target); return; }
        Status("Decrypting data files...");
        Say("");
        Say("=== Decrypt ===");
        Run(Path.Combine(ToolsBin, "Ot2Crypt.exe"),
            "decrypt \"" + core + "\" \"" + Path.Combine(target, "DATA") + "\" \"" +
            Path.Combine(repoRoot, "decrypted", "DATA") + "\"", repoRoot);
    }

    void DoPlay() {
        string target = tbTarget.Text.Trim();
        string exe = Path.Combine(target, "game.exe");
        if (!File.Exists(exe)) { Say("No game.exe at " + target); return; }
        // The game resolves config and logs against the working directory, not
        // the executable, so this must be set or it scatters files around.
        try {
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = target, UseShellExecute = true });
            Say("Launched " + exe);
            Status("Game running.");
        } catch (Exception ex) { Say("Could not launch: " + ex.Message); }
    }
}
