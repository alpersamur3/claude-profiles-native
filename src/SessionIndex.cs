using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClaudeProfilesNative;

public sealed record IndexImportResult(int Added, int Existing, int Rejected, int Updated = 0);

public static class SessionIndex
{
    const string Folder = "claude-code-sessions";
    const int MaxRecordBytes = 4 * 1024 * 1024;
    static readonly Regex RecordName = new("\\Alocal_[a-fA-F0-9-]{36}\\.json\\z");

    public static IEnumerable<string> LegacyRoots()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(roaming, "Claude", Folder);
        yield return Path.Combine(local, "Claude", Folder);
        var packages = Path.Combine(local, "Packages");
        if (!Directory.Exists(packages)) yield break;
        foreach (var package in Directories(packages, "Claude_*"))
        {
            yield return Path.Combine(package, "LocalCache", "Roaming", "Claude", Folder);
            yield return Path.Combine(package, "LocalCache", "Local", "Claude", Folder);
        }
    }

    public static IndexImportResult Import(Store store, Profile profile, IEnumerable<string>? roots = null, bool knownAccountsOnly = false)
    {
        var target = Path.Combine(store.Data(profile), Folder);
        var accounts = Directory.Exists(target) ? Directories(target).Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
        int added = 0, existing = 0, rejected = 0, updated = 0;
        foreach (var input in (roots ?? LegacyRoots()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var source = Path.GetFullPath(input);
            if (!Directory.Exists(source) || IsLink(source)) continue;
            foreach (var file in Records(source))
            {
                var rel = Path.GetRelativePath(source, file);
                var parts = rel.Split(Path.DirectorySeparatorChar);
                // Claude's layout is account UUID / workspace UUID / local_session-UUID.json.
                if (parts.Length != 3 || !Guid.TryParseExact(parts[0], "D", out _) || !Guid.TryParseExact(parts[1], "D", out _)
                    || !RecordName.IsMatch(parts[2]) || !Guid.TryParseExact(parts[2][6..^5], "D", out _)
                    || (knownAccountsOnly && !accounts.Contains(parts[0]))) { rejected++; continue; }
                var destination = Path.Combine(target, rel);
                string? staging = null;
                try
                {
                    var info = new FileInfo(file);
                    if (info.Length > MaxRecordBytes || (info.Attributes & FileAttributes.ReparsePoint) != 0) { rejected++; continue; }
                    using var read = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var buffer = new MemoryStream();
                    var chunk = new byte[16384];
                    int length;
                    while ((length = read.Read(chunk)) > 0) { if (buffer.Length + length > MaxRecordBytes) throw new IOException(Locale.T("ErrorIndexTooLarge")); buffer.Write(chunk, 0, length); }
                    var bytes = buffer.ToArray();
                    using var json = JsonDocument.Parse(bytes);
                    if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("sessionId", out var sid) || sid.ValueKind != JsonValueKind.String
                        || sid.GetString() != Path.GetFileNameWithoutExtension(file)) { rejected++; continue; }
                    byte[]? previous = null;
                    if (File.Exists(destination))
                    {
                        var destinationInfo = new FileInfo(destination);
                        if (destinationInfo.Length > MaxRecordBytes || (destinationInfo.Attributes & FileAttributes.ReparsePoint) != 0) { rejected++; continue; }
                        previous = File.ReadAllBytes(destination);
                        if (previous.SequenceEqual(bytes) || !IsNewer(json.RootElement, previous)) { existing++; continue; }
                    }
                    // Only list records are copied. No config, cookie database, CLI credentials, or Cowork snapshots.
                    EnsurePlainDirectory(target);
                    EnsurePlainDirectory(Path.Combine(target, parts[0]));
                    var parent = Path.GetDirectoryName(destination)!;
                    EnsurePlainDirectory(parent);
                    staging = Path.Combine(parent, "profiles-index-" + Guid.NewGuid().ToString("N") + ".tmp");
                    File.WriteAllBytes(staging, bytes);
                    File.SetLastWriteTimeUtc(staging, info.LastWriteTimeUtc);
                    if (previous == null) { File.Move(staging, destination, overwrite: false); added++; }
                    else
                    {
                        // Recheck after staging so a concurrently changed target is not overwritten.
                        if (!File.ReadAllBytes(destination).SequenceEqual(previous)) { existing++; continue; }
                        var backupFolder = Path.Combine(store.Root, "history-backups", profile.Id, parts[0], parts[1]);
                        EnsurePlainDirectory(Path.Combine(store.Root, "history-backups"));
                        EnsurePlainDirectory(Path.Combine(store.Root, "history-backups", profile.Id));
                        EnsurePlainDirectory(Path.GetDirectoryName(backupFolder)!);
                        EnsurePlainDirectory(backupFolder);
                        var backup = Path.Combine(backupFolder, Path.GetFileNameWithoutExtension(file) + "." + Guid.NewGuid().ToString("N") + ".bak.json");
                        File.Replace(staging, destination, backup);
                        updated++;
                    }
                    staging = null;
                }
                catch (IOException) { if (File.Exists(destination)) existing++; else rejected++; }
                catch (UnauthorizedAccessException) { rejected++; }
                catch (JsonException) { rejected++; }
                finally { if (staging != null && File.Exists(staging)) File.Delete(staging); }
            }
        }
        return new(added, existing, rejected, updated);
    }

    static bool IsNewer(JsonElement source, byte[] target)
    {
        try
        {
            using var json = JsonDocument.Parse(target);
            return Activity(source) is { } sourceTime && Activity(json.RootElement) is { } targetTime && sourceTime > targetTime;
        }
        catch (JsonException) { return false; }
    }
    static long? Activity(JsonElement value) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty("lastActivityAt", out var activity) && activity.ValueKind == JsonValueKind.Number
        && activity.TryGetInt64(out var time) && time > 0 ? time : null;

    static bool IsLink(string directory) => (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0;
    static void EnsurePlainDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        if (IsLink(directory)) throw new IOException(Locale.T("ErrorIndexLink"));
    }
    static IEnumerable<string> Records(string root)
    {
        foreach (var account in Directories(root))
        {
            if (IsLink(account)) continue;
            foreach (var workspace in Directories(account))
            {
                if (IsLink(workspace)) continue;
                foreach (var file in Files(workspace)) yield return file;
            }
        }
    }
    static string[] Directories(string path, string pattern = "*")
    {
        try { return Directory.GetDirectories(path, pattern, SearchOption.TopDirectoryOnly); }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }
    static string[] Files(string path)
    {
        try { return Directory.GetFiles(path, "local_*.json", SearchOption.TopDirectoryOnly); }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }
}
