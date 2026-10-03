using System.Globalization;
using System.Text.Json;

namespace ClaudeProfilesNative;

public static class Locale
{
    static readonly Dictionary<string, Dictionary<string, string>> Catalogs = new()
    {
        ["en"] = Read("en"), ["tr"] = Read("tr")
    };
    static string current = Detect(CultureInfo.CurrentUICulture.Name);
    public static string Current { get => current; set => current = Detect(value); }
    public static string Detect(string? culture) => culture?.Split('-')[0].Equals("tr", StringComparison.OrdinalIgnoreCase) == true ? "tr" : "en";
    public static IReadOnlyDictionary<string, string> Catalog(string language) => Catalogs[Detect(language)];
    public static string T(string key, params object[] args)
    {
        if (!Catalogs[Current].TryGetValue(key, out var text) && !Catalogs["en"].TryGetValue(key, out text)) throw new KeyNotFoundException("Unknown translation key: " + key);
        return args.Length == 0 ? text : string.Format(CultureInfo.InvariantCulture, text, args);
    }
    static Dictionary<string, string> Read(string language)
    {
        using var stream = typeof(Locale).Assembly.GetManifestResourceStream("ClaudeProfilesNative.Locales." + language + ".json")
            ?? throw new InvalidOperationException("Missing translation catalog: " + language);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? throw new InvalidOperationException("Invalid translation catalog.");
    }
}
