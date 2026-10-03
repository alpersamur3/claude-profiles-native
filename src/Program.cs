using System.Diagnostics;

namespace ClaudeProfilesNative;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            var store = new Store(Store.DefaultRoot);
            if (args.Length >= 2 && (args[0] == "--smoke-test" || args[0] == "--smoke-callback" || args[0] == "--smoke-switch" || args[0] == "--smoke-detect"))
            {
                var language = args.Length == 3 ? Locale.Detect(args[2]) : "en";
                Locale.Current = language;
                using Form form = args[0] == "--smoke-callback" ? new CallbackChooser(new Settings().Profiles) : new MainForm(new Store(Path.GetFullPath(args[1])), language);
                form.Opacity = 0; form.ShowInTaskbar = false; form.Show(); Application.DoEvents();
                if (args[0] == "--smoke-switch") { ((MainForm)form).VerifyLanguageSwitch(); Application.DoEvents(); }
                if (args[0] == "--smoke-detect")
                {
                    var check = ((MainForm)form).VerifyAutoDetection();
                    while (!check.IsCompleted) { Application.DoEvents(); Thread.Sleep(10); }
                    check.GetAwaiter().GetResult();
                }
                using var bitmap = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                Directory.CreateDirectory(args[1]);
                bitmap.Save(Path.Combine(args[1], args[0] == "--smoke-callback" ? "callback.png" : "preview.png"));
                return;
            }
            if (args.Length == 1 && args[0] == "--register-callback") { store.Load(); CallbackRegistration.Register(store); return; }
            if (args.Length == 2 && args[0] == "--callback")
            {
                var settings = store.Load();
                Launcher.ValidateCallback(args[1]);
                using var chooser = new CallbackChooser(settings.Profiles);
                if (chooser.ShowDialog() == DialogResult.OK && chooser.Selected != null)
                {
                    // The modal dialog's message loop has ended; continue on the thread pool.
                    Task.Run(async () => { await Launcher.Open(store, settings, chooser.Selected, args[1]); CallbackRegistration.Register(store); }).GetAwaiter().GetResult();
                }
                return;
            }
            using var main = new MainForm(store, openTwo: args.Length == 1 && args[0] == "--open-two");
            Application.Run(main);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, Locale.T("ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}

sealed class CallbackChooser : Form
{
    readonly ListBox list = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
    public Profile? Selected => list.SelectedItem as Profile;
    public CallbackChooser(List<Profile> profiles)
    {
        Text = Locale.T("CallbackTitle"); ClientSize = new Size(520, 340); StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10); TopMost = true; BackColor = Color.FromArgb(248, 250, 252);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(22) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.Controls.Add(new Label { Text = Locale.T("CallbackHelp"), Dock = DockStyle.Fill }, 0, 0);
        list.Items.AddRange(profiles.Cast<object>().ToArray()); list.ClearSelected(); layout.Controls.Add(list, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 0) };
        var ok = new Button { Text = Locale.T("ContinueProfile"), AutoSize = true, Height = 36, Enabled = false };
        var cancel = new Button { Text = Locale.T("Cancel"), AutoSize = true, Height = 36, DialogResult = DialogResult.Cancel };
        list.SelectedIndexChanged += (_, _) => ok.Enabled = Selected != null;
        ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        buttons.Controls.Add(ok); buttons.Controls.Add(cancel); layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout); AcceptButton = ok; CancelButton = cancel;
    }
}

sealed class MainForm : Form
{
    readonly Store store;
    readonly Settings settings;
    readonly ListBox profiles = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
    readonly TextBox name = new();
    readonly TextBox exe = new();
    readonly TextBox log = new() { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
    readonly List<Control> actions = [];
    readonly List<Action> translations = [];
    readonly System.Windows.Forms.Timer protocolMonitor = new() { Interval = 4000 };
    readonly ComboBox languages = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 126 };
    bool monitorEnabled;
    bool changingLanguage;
    Profile? Selected => profiles.SelectedItem as Profile;
    public MainForm(Store store, string? language = null, bool openTwo = false)
    {
        this.store = store; settings = store.Load();
        if (language != null) settings.Language = Locale.Detect(language);
        Locale.Current = settings.Language;
        Text = "Claude Profiles Native · 0.7"; ClientSize = new Size(1080, 790); MinimumSize = new Size(1060, 770);
        Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(248, 250, 252); ForeColor = Color.FromArgb(30, 41, 59); StartPosition = FormStartPosition.CenterScreen;
        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(24) };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 198)); outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 66)); outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        header.Controls.Add(new Label { Text = "Claude Profiles Native", Font = new Font("Segoe UI", 22, FontStyle.Bold), Dock = DockStyle.Fill }, 0, 0);
        var languageRow = Row(); languageRow.FlowDirection = FlowDirection.RightToLeft;
        languages.Items.AddRange(["English", "Türkçe"]); languages.SelectedIndex = settings.Language == "tr" ? 1 : 0;
        languageRow.Controls.Add(languages); languageRow.Controls.Add(Label("Language", auto: true)); header.Controls.Add(languageRow, 1, 0);
        outer.Controls.Add(header, 0, 0); outer.SetColumnSpan(header, 2);
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(0, 0, 12, 0) };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); left.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        left.Controls.Add(Label("Profiles", small: true), 0, 0);
        profiles.Items.AddRange(settings.Profiles.Cast<object>().ToArray()); left.Controls.Add(profiles, 0, 1);
        var add = Button("AddProfile", () => { Save(); var profile = new Profile(); settings.Profiles.Add(profile); store.Save(settings); profiles.Items.Add(profile); profiles.SelectedItem = profile; });
        add.Dock = DockStyle.Fill; left.Controls.Add(add, 0, 2); outer.Controls.Add(left, 0, 1);
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 12, Padding = new Padding(18, 0, 0, 0) };
        foreach (var height in new[] { 82f, 50f, 50f, 54f, 28f, 46f, 54f, 28f, 48f, 54f, 26f }) right.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var intro = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        intro.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); intro.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var headline = Label("Headline"); headline.Font = new Font("Segoe UI", 17, FontStyle.Bold); intro.Controls.Add(headline, 0, 0);
        intro.Controls.Add(Label("Subtitle"), 0, 1); right.Controls.Add(intro, 0, 0);
        right.Controls.Add(Input("ProfileName", name, Button("Save", () => { Save(); Log(Locale.T("ProfileSaved")); })), 0, 1);
        exe.Text = settings.ClaudeExe;
        right.Controls.Add(Input("ClaudeApp", exe, Button("Browse", () =>
        {
            using var dialog = new OpenFileDialog { Filter = "Claude|Claude.exe", FileName = "Claude.exe", Title = Locale.T("BrowseTitle") };
            if (dialog.ShowDialog(this) == DialogResult.OK) { settings.AutoDetectClaude = false; exe.Text = dialog.FileName; Save(); }
        })), 0, 2);
        var launch = Row(); launch.Controls.Add(AsyncButton("OpenProfile", async () => { Save(); await Open(Selected ?? throw new IOException(Locale.T("ErrorSelectProfile"))); }, primary: true));
        launch.Controls.Add(AsyncButton("OpenTwo", async () => { Save(); foreach (var p in settings.Profiles.Take(2)) await Open(p); }));
        launch.Controls.Add(Button("ProfileFolder", () => { var p = Selected ?? throw new IOException(Locale.T("ErrorSelectProfile")); OpenFolder(store.Data(p)); })); right.Controls.Add(launch, 0, 3);
        right.Controls.Add(Label("SignInHeading", heading: true), 0, 4); right.Controls.Add(Label("SignInHelp"), 0, 5);
        var signin = Row(); signin.Controls.Add(Button("RegisterHandler", () => { Save(); CallbackRegistration.Register(store); monitorEnabled = true; Log(Locale.T("HandlerRegistered")); }));
        signin.Controls.Add(Button("DefaultApps", () => { CallbackRegistration.Register(store); CallbackRegistration.OpenDefaultSettings(); Log(Locale.T("DefaultAppsOpened")); }));
        signin.Controls.Add(Button("RestoreHandler", () => { CallbackRegistration.Restore(store); monitorEnabled = false; Log(Locale.T("HandlerRestored")); })); right.Controls.Add(signin, 0, 6);
        right.Controls.Add(Label("HistoryHeading", heading: true), 0, 7); right.Controls.Add(Label("HistoryHelp"), 0, 8);
        var history = Row(); history.Controls.Add(Button("UpdateHistory", () =>
        {
            Save(); var profile = Selected ?? throw new IOException(Locale.T("ErrorSelectProfile"));
            var result = SessionIndex.Import(store, profile, knownAccountsOnly: true);
            Log(Locale.T("HistoryResult", result.Added, result.Updated, result.Existing, result.Rejected));
        }));
        history.Controls.Add(Button("BackupsFolder", () => { var p = Selected ?? throw new IOException(Locale.T("ErrorSelectProfile")); OpenFolder(Path.Combine(store.Root, "history-backups", p.Id)); })); right.Controls.Add(history, 0, 9);
        right.Controls.Add(Label("Activity", small: true), 0, 10); right.Controls.Add(log, 0, 11); outer.Controls.Add(right, 1, 1); Controls.Add(outer);
        profiles.SelectedIndexChanged += (_, _) => name.Text = Selected?.Name ?? "";
        profiles.SelectedIndex = 0;
        languages.SelectedIndexChanged += (_, _) => { if (!changingLanguage) ChangeLanguage(languages.SelectedIndex == 1 ? "tr" : "en"); };
        actions.Add(languages);
        Log(Locale.T("Ready"));
        if (language == null) Shown += async (_, _) =>
        {
            SetBusy(true);
            try
            {
                await DetectInstalledClaude();
            }
            catch (Exception ex) { Error(ex); }
            finally { SetBusy(false); }
            if (openTwo) await LaunchTwo();
        };
        protocolMonitor.Tick += (_, _) => { if (monitorEnabled && !CallbackRegistration.IsOurs()) { try { CallbackRegistration.Register(store); } catch (Exception ex) { monitorEnabled = false; Error(ex); } } };
        protocolMonitor.Start(); FormClosed += (_, _) => protocolMonitor.Dispose();
    }
    void ChangeLanguage(string language)
    {
        var previous = settings.Language;
        try
        {
            Save(); settings.Language = language; store.Save(settings); Locale.Current = language;
            foreach (var translate in translations) translate();
            log.Clear(); Log(Locale.T("LanguageChanged"));
        }
        catch (Exception ex)
        {
            settings.Language = previous; changingLanguage = true;
            try { languages.SelectedIndex = previous == "tr" ? 1 : 0; } finally { changingLanguage = false; }
            Error(ex);
        }
    }
    async Task DetectInstalledClaude()
    {
        if (!settings.AutoDetectClaude && !string.IsNullOrWhiteSpace(settings.ClaudeExe)) return;
        Log(Locale.T("DetectingClaude"));
        if (await ClaudeDiscovery.RefreshAsync(settings)) store.Save(settings);
        exe.Text = settings.ClaudeExe;
        Log(Locale.T(string.IsNullOrWhiteSpace(settings.ClaudeExe) ? "ClaudeNotFound" : "ClaudeDetected"));
    }
    public async Task VerifyAutoDetection()
    {
        var ids = settings.Profiles.Select(p => p.Id).ToArray();
        settings.ClaudeExe = ""; settings.AutoDetectClaude = false; exe.Text = "";
        await DetectInstalledClaude();
        var saved = store.Load();
        if (!saved.AutoDetectClaude || !File.Exists(saved.ClaudeExe) || exe.Text != saved.ClaudeExe
            || !saved.Profiles.Select(p => p.Id).SequenceEqual(ids)) throw new IOException("Automatic discovery smoke check failed.");
        File.WriteAllText(Path.Combine(store.Root, "auto-detection-pass.txt"), "PASS: startup discovery, visible field, automatic preference and unchanged profile identities. Claude was not launched.");
    }
    public void VerifyLanguageSwitch()
    {
        var originalIds = settings.Profiles.Select(p => p.Id).ToArray();
        name.Text = "Custom profile ğ";
        languages.SelectedIndex = 1;
        var saved = store.Load();
        if (saved.Language != "tr" || Selected?.Name != "Custom profile ğ" || !saved.Profiles.Select(p => p.Id).SequenceEqual(originalIds)
            || !actions.OfType<Button>().Any(button => button.Text == Locale.T("OpenProfile"))) throw new IOException("Language switch smoke check failed.");
        languages.SelectedIndex = 0;
        if (store.Load().Language != "en" || !actions.OfType<Button>().Any(button => button.Text == "Open profile")) throw new IOException("English switch smoke check failed.");
        File.WriteAllText(Path.Combine(store.Root, "language-switch-pass.txt"), "PASS: live language switching, persistence, edited name and profile identity.");
    }
    void Bind(Control control, string key) { void Translate() => control.Text = Locale.T(key); translations.Add(Translate); Translate(); }
    Label Label(string key, bool auto = false, bool small = false, bool heading = false)
    {
        var label = new Label { AutoSize = auto, Dock = auto ? DockStyle.None : DockStyle.Fill, Margin = new Padding(0, 4, 6, 0) };
        if (small) { label.Font = new Font("Segoe UI", 9, FontStyle.Bold); label.ForeColor = Color.FromArgb(100, 116, 139); }
        if (heading) label.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        Bind(label, key); return label;
    }
    TableLayoutPanel Input(string key, TextBox input, Button button)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(0, 5, 0, 5) };
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 153)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        row.Controls.Add(Label(key), 0, 0); input.Dock = DockStyle.Fill; row.Controls.Add(input, 1, 0);
        button.AutoSize = false; button.Margin = new Padding(4, 0, 0, 0); button.Dock = DockStyle.Fill; row.Controls.Add(button, 2, 0); return row;
    }
    static FlowLayoutPanel Row() => new() { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 5, 0, 0) };
    Button CreateButton(string key, bool primary = false)
    {
        var button = new Button { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 36), Height = 36, Padding = new Padding(10, 0, 10, 0), FlatStyle = FlatStyle.Flat, BackColor = Color.White, Margin = new Padding(0, 3, 8, 3) };
        button.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        if (primary) { button.BackColor = Color.FromArgb(37, 99, 235); button.ForeColor = Color.White; button.FlatAppearance.BorderColor = button.BackColor; }
        Bind(button, key); actions.Add(button); return button;
    }
    Button Button(string key, Action action)
    {
        var button = CreateButton(key); button.Click += (_, _) => { try { action(); } catch (Exception ex) { Error(ex); } }; return button;
    }
    Button AsyncButton(string key, Func<Task> action, bool primary = false)
    {
        var button = CreateButton(key, primary);
        button.Click += async (_, _) => { SetBusy(true); try { await action(); } catch (Exception ex) { Error(ex); } finally { SetBusy(false); } }; return button;
    }
    void SetBusy(bool busy) { foreach (var control in actions) control.Enabled = !busy; profiles.Enabled = !busy; name.ReadOnly = busy; exe.ReadOnly = busy; }
    void Save()
    {
        if (Selected is { } p) p.Name = name.Text.Trim();
        var entered = exe.Text.Trim();
        if (!string.Equals(entered, settings.ClaudeExe, StringComparison.OrdinalIgnoreCase)) settings.AutoDetectClaude = false;
        if (string.IsNullOrWhiteSpace(entered)) settings.AutoDetectClaude = true;
        settings.ClaudeExe = entered; store.Save(settings);
        if (Selected is { } selected) { var index = profiles.SelectedIndex; profiles.Items[index] = selected; profiles.SelectedIndex = index; }
    }
    public async Task LaunchTwo()
    {
        SetBusy(true); try { Save(); foreach (var p in settings.Profiles.Take(2)) await Open(p); }
        catch (Exception ex) { Error(ex); } finally { SetBusy(false); }
    }
    async Task Open(Profile p)
    {
        Log(Locale.T("Opening", p.Name)); await Launcher.Open(store, settings, p); exe.Text = settings.ClaudeExe;
        CallbackRegistration.Register(store); monitorEnabled = true; Log(Locale.T("Opened", p.Name));
    }
    static void OpenFolder(string path) { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
    void Log(string text) => log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine);
    void Error(Exception ex) { Log(ex.Message); MessageBox.Show(this, ex.Message, Locale.T("ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
}
