using ClaudeProfilesNative;

var root = Path.GetFullPath(args.Length == 2 && args[0] == "--scratch" ? args[1] : throw new ArgumentException("Use --scratch PATH."));
Directory.CreateDirectory(root);
int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed++; }
void Reject(Action action, string name) { try { action(); } catch (IOException) { passed++; return; } throw new Exception(name); }
var store = new Store(Path.Combine(root, "state with spaces ğ"));
var s = new Settings(); store.Save(s); var read = store.Load();
Check(read.Profiles[0].Id == s.Profiles[0].Id, "Stable profile identity after restart");
Check(store.Data(s.Profiles[0]) != store.Data(s.Profiles[1]), "Different account directories");
Reject(() => store.Data(new Profile { Id = "..\\..\\Claude", Name = "Bad" }), "Prevent path traversal");
Reject(() => Store.Validate(new Settings { Profiles = [s.Profiles[0], s.Profiles[0]] }), "Reject identity collisions");
Reject(() => Store.Validate(new Settings { Version = 2 }), "Reject unknown metadata versions");
Reject(() => Store.ValidateProfile(new Profile { Name = "bad\nname" }), "Reject control characters");
foreach (var uri in new[] { "https://claude.ai/login", "claude://evil.example/callback", "claude://user@login/google-auth", "claude://login:123/google-auth", "claude://login/google-auth\n--bad" }) Reject(() => Launcher.ValidateCallback(uri), "Reject unsafe callback");
const string callback = "claude://login/google-auth?code=synthetic-test-only&state=two%20words";
Launcher.ValidateCallback(callback); passed++;
var fakeExe = Path.Combine(root, "unchanged Claude ğ", "Claude.exe"); Directory.CreateDirectory(Path.GetDirectoryName(fakeExe)!); File.WriteAllText(fakeExe, "test placeholder, never executed");
var info = Launcher.StartInfo(fakeExe, store.Data(s.Profiles[0]), callback);
Check(info.ArgumentList.Count == 2 && info.ArgumentList[1] == callback, "Callback is a single unchanged process argument");
Check(info.ArgumentList[0] == "--user-data-dir=" + store.Data(s.Profiles[0]), "Unicode and spaces preserved in native directory switch");
Check(!info.UseShellExecute && info.FileName == fakeExe, "No shell interpolation");
foreach (var key in new[] { "APPDATA", "LOCALAPPDATA", "USERPROFILE", "HOME", "CLAUDE_CONFIG_DIR" })
{
    info.Environment.TryGetValue(key, out var inherited);
    Check(inherited == Environment.GetEnvironmentVariable(key), "Preserve host path: " + key);
}
Check(!info.Environment.ContainsKey("CLAUDE_USER_DATA_DIR") && !info.Environment.ContainsKey("CLAUDE_CDP_AUTH"), "No guarded development override");
Check(!info.Environment.ContainsKey("ANTHROPIC_API_KEY") && !info.Environment.ContainsKey("CLAUDE_CODE_OAUTH_TOKEN"), "No inherited account auth override");
var legacy = Path.Combine(root, "legacy-index");
var account = Guid.NewGuid().ToString("D"); var workspace = Guid.NewGuid().ToString("D");
var relative = Path.Combine(account, workspace);
var sourceFolder = Path.Combine(legacy, relative); Directory.CreateDirectory(sourceFolder);
var targetFolder = Path.Combine(store.Data(s.Profiles[0]), "claude-code-sessions", relative); Directory.CreateDirectory(targetFolder);
string Record(string directory, string body) { var id = "local_" + Guid.NewGuid().ToString("D"); var path = Path.Combine(directory, id + ".json"); File.WriteAllText(path, "{\"sessionId\":\"" + id + "\",\"cliSessionId\":\"synthetic\",\"title\":\"" + body + "\"}"); return path; }
var oldRecord = Record(sourceFolder, "old"); var sourceBytes = File.ReadAllBytes(oldRecord);
var keepRecord = Record(sourceFolder, "source-version"); var keepTarget = Path.Combine(targetFolder, Path.GetFileName(keepRecord)); File.WriteAllText(keepTarget, "existing-target-must-stay");
File.WriteAllText(Path.Combine(sourceFolder, "config.json"), "credential-sentinel");
var mismatch = Path.Combine(sourceFolder, "local_" + Guid.NewGuid().ToString("D") + ".json"); File.WriteAllText(mismatch, "{\"sessionId\":\"wrong\"}");
var broken = Path.Combine(sourceFolder, "local_" + Guid.NewGuid().ToString("D") + ".json"); File.WriteAllText(broken, "invalid-json");
var otherFolder = Path.Combine(legacy, Guid.NewGuid().ToString("D"), workspace); Directory.CreateDirectory(otherFolder); Record(otherFolder, "other-account");
var result = SessionIndex.Import(store, s.Profiles[0], [legacy], knownAccountsOnly: true);
Check(result.Added == 1 && result.Existing == 1 && result.Rejected == 3, "Import only valid missing records of a known account");
Check(File.ReadAllBytes(Path.Combine(targetFolder, Path.GetFileName(oldRecord))).SequenceEqual(sourceBytes), "Preserve record bytes and transcript identity");
Check(File.ReadAllText(keepTarget) == "existing-target-must-stay", "Never overwrite an existing conversation");
Check(!File.Exists(Path.Combine(targetFolder, "config.json")), "Never copy authentication or unrelated files");
Check(File.ReadAllBytes(oldRecord).SequenceEqual(sourceBytes), "Source history remains unchanged");
Check(Directory.GetDirectories(Path.Combine(store.Data(s.Profiles[0]), "claude-code-sessions")).Length == 1, "Other accounts are excluded");
Check(!Directory.EnumerateFiles(targetFolder, "*.tmp").Any(), "No incomplete list records remain");
result = SessionIndex.Import(store, s.Profiles[0], [legacy], knownAccountsOnly: true);
Check(result.Added == 0 && result.Existing == 2, "Repeated import is idempotent");
var changedSource = Record(sourceFolder, "advanced-source");
File.WriteAllText(changedSource, File.ReadAllText(changedSource).Replace("\"title\"", "\"lastActivityAt\":200,\"title\""));
var changedTarget = Path.Combine(targetFolder, Path.GetFileName(changedSource));
var prior = File.ReadAllText(changedSource).Replace("200", "100").Replace("advanced-source", "old-target");
File.WriteAllText(changedTarget, prior);
File.SetLastWriteTimeUtc(changedTarget, DateTime.UtcNow.AddHours(1));
var advancedNative = Record(sourceFolder, "source-is-older");
File.WriteAllText(advancedNative, File.ReadAllText(advancedNative).Replace("\"title\"", "\"lastActivityAt\":100,\"title\""));
var advancedTarget = Path.Combine(targetFolder, Path.GetFileName(advancedNative));
var advancedBytes = File.ReadAllText(advancedNative).Replace("100", "300"); File.WriteAllText(advancedTarget, advancedBytes);
result = SessionIndex.Import(store, s.Profiles[0], [legacy], knownAccountsOnly: true);
Check(result.Updated == 1, "Newer source conversation updates an existing list entry");
Check(File.ReadAllBytes(changedTarget).SequenceEqual(File.ReadAllBytes(changedSource)), "Source activity wins even if target file timestamp is newer");
var backups = Directory.GetFiles(Path.Combine(store.Root, "history-backups"), "*.bak.json", SearchOption.AllDirectories);
Check(backups.Length == 1 && File.ReadAllText(backups[0]) == prior, "Atomic replacement preserves an exact recoverable backup");
Check(File.ReadAllText(advancedTarget) == advancedBytes, "A conversation advanced in the native profile is preserved");
result = SessionIndex.Import(store, s.Profiles[0], [legacy], knownAccountsOnly: true);
Check(result.Updated == 0 && Directory.GetFiles(Path.Combine(store.Root, "history-backups"), "*.bak.json", SearchOption.AllDirectories).Length == 1, "No repeat updates or duplicate backups for equal activity");
Check(File.ReadAllText(changedSource).Contains("advanced-source"), "Update never mutates the source");
Check(Locale.Catalog("en").Keys.Order().SequenceEqual(Locale.Catalog("tr").Keys.Order()), "English and Turkish cover identical translation keys");
foreach (var key in Locale.Catalog("en").Keys)
{
    string[] Placeholders(string text) => System.Text.RegularExpressions.Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).Order().ToArray();
    Check(Placeholders(Locale.Catalog("en")[key]).SequenceEqual(Placeholders(Locale.Catalog("tr")[key])), "Matching format placeholders: " + key);
    foreach (var language in new[] { "en", "tr" }) { Locale.Current = language; Check(!string.IsNullOrWhiteSpace(Locale.T(key, 1, 2, 3, 4)), "Translation can be formatted: " + language + "/" + key); }
}
Check(Locale.Detect("tr-TR") == "tr" && Locale.Detect("TR") == "tr" && Locale.Detect("de-DE") == "en", "System locale detection and English fallback");
var ids = s.Profiles.Select(p => p.Id).ToArray(); var names = s.Profiles.Select(p => p.Name).ToArray();
s.Language = "tr"; store.Save(s); Locale.Current = "en"; var turkish = store.Load();
Check(turkish.Language == "tr" && Locale.Current == "tr", "Saved language applies after restart, including core errors");
Check(turkish.Profiles.Select(p => p.Id).SequenceEqual(ids) && turkish.Profiles.Select(p => p.Name).SequenceEqual(names), "Changing language preserves existing profile identities and names");
Check(Locale.T("ErrorSelectProfile") == "Önce bir profil seç.", "Turkish error messages are localized");
s.Language = "en"; store.Save(s); store.Load(); Check(Locale.T("ErrorSelectProfile") == "Select a profile first.", "English language can be restored");
var settingsPath = Path.Combine(store.Root, "profiles.json");
var oldSettings = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(settingsPath))!; oldSettings.AsObject().Remove("Language"); File.WriteAllText(settingsPath, oldSettings.ToJsonString());
var migrated = store.Load(); Check(migrated.Profiles.Select(p => p.Id).SequenceEqual(ids) && (migrated.Language == "en" || migrated.Language == "tr"), "Pre-localization settings migrate without new account directories");
var packages = Path.Combine(root, "WindowsApps with spaces ğ"); Directory.CreateDirectory(packages);
string Package(string name, bool present = true)
{
    var directory = Path.Combine(packages, name); Directory.CreateDirectory(Path.Combine(directory, "app"));
    if (present) File.WriteAllText(Path.Combine(directory, "app", "Claude.exe"), "synthetic test executable, never run");
    return directory;
}
var low = Package("Claude_2.99.0.0_x64__publisher123"); var high = Package("Claude_2.100.0.0_x64__publisher123");
var arm = Package("Claude_9.100.0.0_arm64__publisher123"); var missing = Package("Claude_9.100.0.0_x64__publisher123", present: false);
var other = Package("Other_9.100.0.0_x64__publisher123"); var malformed = Package("Claude_bad_x64__publisher123");
var highExe = Path.Combine(high, "app", "Claude.exe");
Check(ClaudeDiscovery.SelectNewest([low, high, arm, missing, other, malformed]) == highExe, "Choose numerically newest existing x64 Claude package");
Check(ClaudeDiscovery.FindInDirectory(packages) == highExe, "Discover across versioned WindowsApps directories with Unicode and spaces");
Check(ClaudeDiscovery.FindInDirectory(Path.Combine(root, "does-not-exist")) == null, "Missing or unavailable root is a normal detection failure");
var single = Package("Claude_2.101.0.0_x64_publisher123");
Check(ClaudeDiscovery.SelectNewest([single]) == Path.Combine(single, "app", "Claude.exe"), "Accept both single and double architecture separators");
Check(ClaudeDiscovery.SelectNewest([arm, missing, other, malformed]) == null, "Reject wrong architecture, unrelated names, malformed versions and absent executable");
bool searched = false;
var manual = new Settings { ClaudeExe = fakeExe, AutoDetectClaude = false };
Check(!await ClaudeDiscovery.RefreshAsync(manual, () => { searched = true; return Task.FromResult<string?>(highExe); }) && !searched && manual.ClaudeExe == fakeExe, "Never search or overwrite a manually selected path");
var automatic = new Settings();
Check(await ClaudeDiscovery.RefreshAsync(automatic, () => Task.FromResult<string?>(Path.Combine(low, "app", "Claude.exe"))) && automatic.AutoDetectClaude, "Empty path enables persistent automatic discovery");
Check(await ClaudeDiscovery.RefreshAsync(automatic, () => Task.FromResult<string?>(highExe)) && automatic.ClaudeExe == highExe, "Auto-detected installation refreshes after a version update");
Check(!await ClaudeDiscovery.RefreshAsync(automatic, () => Task.FromResult<string?>(null)) && automatic.ClaudeExe == highExe, "Keep a valid automatic path when package enumeration is temporarily unavailable");
automatic.ClaudeExe = Path.Combine(root, "removed-version", "Claude.exe");
Check(await ClaudeDiscovery.RefreshAsync(automatic, () => Task.FromResult<string?>(null)) && automatic.ClaudeExe == "", "Clear a missing automatic path so manual selection remains available");
var legacyPath = migrated.ClaudeExe = fakeExe; store.Save(migrated);
var legacyJson = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(settingsPath))!; legacyJson.AsObject().Remove("AutoDetectClaude"); File.WriteAllText(settingsPath, legacyJson.ToJsonString());
var legacySettings = store.Load();
Check(!legacySettings.AutoDetectClaude && legacySettings.ClaudeExe == legacyPath, "Old saved application paths stay manual after migration");
Console.WriteLine($"PASS: {passed} isolation, history, localization and discovery checks. No real credentials read.");
