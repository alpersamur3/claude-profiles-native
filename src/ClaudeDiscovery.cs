using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ClaudeProfilesNative;

public static class ClaudeDiscovery
{
    static readonly Regex PackageName = new(@"\AClaude_(?<version>\d+(?:\.\d+){3})_x64_{1,2}[a-z0-9]+\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Version ordering is numeric: 2.100 is newer than 2.99.
    public static string? SelectNewest(IEnumerable<string> packageDirectories)
    {
        var candidates = new List<(Version Version, string Path)>();
        foreach (var directory in packageDirectories)
        {
            var full = Path.GetFullPath(directory);
            var match = PackageName.Match(Path.GetFileName(Path.TrimEndingDirectorySeparator(full)));
            if (!match.Success || !Version.TryParse(match.Groups["version"].Value, out var version)) continue;
            var exe = Path.Combine(full, "app", "Claude.exe");
            if (File.Exists(exe)) candidates.Add((version, exe));
        }
        return candidates.OrderByDescending(item => item.Version).ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase).Select(item => item.Path).FirstOrDefault();
    }

    public static async Task<string?> FindAsync()
    {
        // WindowsApps enumeration can be restricted. Query the current user's registered packages first.
        var registered = await RegisteredPackagesAsync();
        var found = SelectNewest(registered);
        if (found != null) return found;
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
        return await Task.Run(() => FindInDirectory(root));
    }

    public static string? FindInDirectory(string root)
    {
        try { return SelectNewest(Directory.GetDirectories(root, "Claude_*", SearchOption.TopDirectoryOnly)); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static async Task<bool> RefreshAsync(Settings settings, Func<Task<string?>>? find = null)
    {
        if (!settings.AutoDetectClaude && !string.IsNullOrWhiteSpace(settings.ClaudeExe)) return false;
        var previous = settings.ClaudeExe; var wasAutomatic = settings.AutoDetectClaude;
        var result = await (find ?? FindAsync)();
        if (result != null) settings.ClaudeExe = result;
        else if (!File.Exists(settings.ClaudeExe)) settings.ClaudeExe = "";
        settings.AutoDetectClaude = true;
        return !wasAutomatic || !string.Equals(previous, settings.ClaudeExe, StringComparison.OrdinalIgnoreCase);
    }

    static async Task<string[]> RegisteredPackagesAsync()
    {
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell)) return [];
        var info = new ProcessStartInfo(powershell) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "$ErrorActionPreference='Stop'; Get-AppxPackage -Name Claude | Where-Object { $_.Architecture -eq 'X64' } | Select-Object -ExpandProperty InstallLocation" }) info.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(info);
            if (process == null) return [];
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (Win32Exception) { }
                await Task.WhenAll(output, errors);
                return [];
            }
            await errors;
            return process.ExitCode == 0 ? (await output).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
        }
        catch (Win32Exception) { return []; }
        catch (IOException) { return []; }
    }
}
