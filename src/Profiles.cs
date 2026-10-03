using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace ClaudeProfilesNative;

public sealed class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = Locale.T("ProfileDefault");
    public override string ToString() => Name;
}

public sealed class Settings
{
    public int Version { get; set; } = 1;
    public string Language { get; set; } = Locale.Detect(System.Globalization.CultureInfo.CurrentUICulture.Name);
    public string ClaudeExe { get; set; } = "";
    public bool AutoDetectClaude { get; set; }
    public List<Profile> Profiles { get; set; } = [new() { Name = Locale.T("ProfileNumber", 1) }, new() { Name = Locale.T("ProfileNumber", 2) }];
}

public sealed class Store(string root)
{
    public string Root { get; } = Path.GetFullPath(root);
    // MSIX transparently redirects LocalAppData into package LocalCache. Documents is outside that mapping,
    // so the launcher and installed Claude see the same physical profile directory.
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ClaudeProfilesNative");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public Settings Load()
    {
        var file = Path.Combine(Root, "profiles.json");
        var value = File.Exists(file) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(file)) ?? throw new IOException(Locale.T("ErrorReadProfiles")) : new Settings();
        value.Language = Locale.Detect(value.Language);
        Locale.Current = value.Language;
        Validate(value);
        return value;
    }
    public void Save(Settings value)
    {
        Validate(value);
        Directory.CreateDirectory(Root);
        var temp = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".tmp");
        try { File.WriteAllText(temp, JsonSerializer.Serialize(value, Json)); File.Move(temp, Path.Combine(Root, "profiles.json"), true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public string Data(Profile p)
    {
        ValidateProfile(p);
        return Path.Combine(Root, "accounts", p.Id, "desktop-session");
    }
    public static void ValidateProfile(Profile p)
    {
        if (!Regex.IsMatch(p.Id, "\\A[a-f0-9]{32}\\z")) throw new IOException(Locale.T("ErrorProfileId"));
        if (string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 80 || p.Name.Any(char.IsControl)) throw new IOException(Locale.T("ErrorProfileName"));
    }
    public static void Validate(Settings s)
    {
        if (s.Version != 1 || s.Profiles.Count == 0 || (s.Language != "en" && s.Language != "tr")) throw new IOException(Locale.T("ErrorProfiles"));
        foreach (var p in s.Profiles) ValidateProfile(p);
        if (s.Profiles.Select(p => p.Id).Distinct().Count() != s.Profiles.Count) throw new IOException(Locale.T("ErrorDuplicateIds"));
    }
}

public static class Launcher
{
    public static ProcessStartInfo StartInfo(string exe, string data, string? callback = null)
    {
        exe = Path.GetFullPath(exe);
        data = Path.GetFullPath(data);
        if (!File.Exists(exe) || !Path.GetFileName(exe).Equals("claude.exe", StringComparison.OrdinalIgnoreCase)) throw new IOException(Locale.T("ErrorSelectExe"));
        if (callback != null) ValidateCallback(callback);
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
        info.ArgumentList.Add("--user-data-dir=" + data);
        if (callback != null) info.ArgumentList.Add(callback);
        // Preserve home, APPDATA, LOCALAPPDATA, PATH and CLAUDE_CONFIG_DIR. Desktop supplies its own Code session auth.
        foreach (var key in new[] { "CLAUDE_USER_DATA_DIR", "CLAUDE_CDP_AUTH", "CLAUDE_CODE_OAUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR", "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN" }) info.Environment.Remove(key);
        return info;
    }
    public static void ValidateCallback(string url)
    {
        if (url.Length > 16384 || url.Any(char.IsControl) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != "claude" || (uri.Host != "login" && uri.Host != "claude.ai") || uri.UserInfo.Length != 0 || !uri.IsDefaultPort)
            throw new IOException(Locale.T("ErrorCallback"));
    }
    public static async Task<int> Open(Store store, Settings settings, Profile profile, string? callback = null)
    {
        if (await ClaudeDiscovery.RefreshAsync(settings)) store.Save(settings);
        var start = StartInfo(settings.ClaudeExe, store.Data(profile), callback);
        var data = store.Data(profile);
        Directory.CreateDirectory(data);
        SessionIndex.Import(store, profile, knownAccountsOnly: true);
        using var process = Process.Start(start) ?? throw new IOException(Locale.T("ErrorLaunch"));
        var pid = process.Id;
        // A second launch can legitimately forward to the existing instance for this same profile.
        for (int i = 0; i < 120; i++)
        {
            if (Directory.Exists(Path.Combine(data, "Network")) && File.Exists(Path.Combine(data, "config.json"))) return pid;
            if (process.HasExited) break;
            await Task.Delay(250);
        }
        throw new IOException(Locale.T("ErrorSessionDirectory"));
    }
}

// Per-user URL handler. No tokens are written to our metadata or logs. The OS passes the callback directly to Claude.
public static class CallbackRegistration
{
    const string AppName = "Claude Profiles Native";
    const string ProgId = "ClaudeProfilesNative.Url";
    const string Capabilities = @"Software\ClaudeProfilesNative\Capabilities";
    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
    static void NotifyShell() => SHChangeNotify(0x08000000, 0x1000, IntPtr.Zero, IntPtr.Zero); // ASSOCCHANGED, FLUSH
    const string KeyPath = @"Software\Classes\claude\shell\open\command";
    const string ProtocolPath = @"Software\Classes\claude";
    sealed record Backup(bool Existed, string? Command, bool UrlProtocolExisted);
    static string BackupFile(Store store) => Path.Combine(store.Root, "protocol-backup.json");
    public static string OwnCommand => "\"" + Environment.ProcessPath + "\" --callback \"%1\"";
    public static bool IsOurs()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue("") as string == OwnCommand;
    }
    public static void Register(Store store)
    {
        Directory.CreateDirectory(store.Root);
        using var guard = new FileStream(Path.Combine(store.Root, "protocol.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (!File.Exists(BackupFile(store)))
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            using var protocol = Registry.CurrentUser.OpenSubKey(ProtocolPath);
            var backup = new Backup(key != null, key?.GetValue("") as string, protocol?.GetValueNames().Contains("URL Protocol") == true);
            var temp = BackupFile(store) + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(backup));
            File.Move(temp, BackupFile(store));
        }
        using (var protocol = Registry.CurrentUser.CreateSubKey(ProtocolPath)) protocol.SetValue("URL Protocol", "");
        using (var key = Registry.CurrentUser.CreateSubKey(KeyPath)) key.SetValue("", OwnCommand);
        // Advertise a distinct handler to Windows Default Apps. A Store package can win the default
        // resolution even when the legacy scheme command has changed. Never alter UserChoice hashes.
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId))
        {
            key.SetValue("", Locale.T("HandlerLink"));
            key.SetValue("URL Protocol", "");
            using var icon = key.CreateSubKey("DefaultIcon"); icon.SetValue("", "\"" + Environment.ProcessPath + "\",0");
        }
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId + @"\shell\open\command")) key.SetValue("", OwnCommand);
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId + @"\Application"))
        {
            key.SetValue("ApplicationName", AppName);
            key.SetValue("ApplicationDescription", Locale.T("HandlerAppDescription"));
            key.SetValue("ApplicationIcon", "\"" + Environment.ProcessPath + "\",0");
        }
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Applications\ClaudeProfilesNative.exe"))
        {
            key.SetValue("FriendlyAppName", AppName);
            using var command = key.CreateSubKey(@"shell\open\command"); command.SetValue("", OwnCommand);
        }
        using (var key = Registry.CurrentUser.CreateSubKey(Capabilities))
        {
            key.SetValue("ApplicationName", AppName);
            key.SetValue("ApplicationDescription", Locale.T("HandlerDescription"));
            key.SetValue("ApplicationIcon", "\"" + Environment.ProcessPath + "\",0");
            using var associations = key.CreateSubKey("URLAssociations"); associations.SetValue("claude", ProgId);
        }
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
        {
            key.SetValue(AppName, Capabilities);
            if (key.GetValue("ClaudeProfilesNative") as string == Capabilities) key.DeleteValue("ClaudeProfilesNative", false);
        }
        NotifyShell();
    }
    public static void OpenDefaultSettings() => Process.Start(new ProcessStartInfo("ms-settings:defaultapps?registeredAppUser=" + Uri.EscapeDataString(AppName)) { UseShellExecute = true });
    public static void Restore(Store store)
    {
        using var guard = new FileStream(Path.Combine(store.Root, "protocol.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (!File.Exists(BackupFile(store))) throw new IOException(Locale.T("ErrorNoBackup"));
        if (!IsOurs()) throw new IOException(Locale.T("ErrorHandlerChanged"));
        var backup = JsonSerializer.Deserialize<Backup>(File.ReadAllText(BackupFile(store))) ?? throw new IOException(Locale.T("ErrorReadBackup"));
        if (backup.Existed)
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            if (backup.Command != null) key.SetValue("", backup.Command); else key.DeleteValue("", false);
        }
        else Registry.CurrentUser.DeleteSubKey(KeyPath, false);
        if (!backup.UrlProtocolExisted) { using var key = Registry.CurrentUser.OpenSubKey(ProtocolPath, true); key?.DeleteValue("URL Protocol", false); }
        File.Delete(BackupFile(store));
        NotifyShell();
    }
}
