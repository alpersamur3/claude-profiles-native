using ClaudeProfilesNative;
var store = new Store(Store.DefaultRoot);
var settings = store.Load();
if (args.Length == 2 && args[0] == "--configure")
{
    settings.ClaudeExe = Path.GetFullPath(args[1]); store.Save(settings); Console.WriteLine("Native launcher configured with installed Claude. No account data migrated.");
}
else if (args.Length == 1 && args[0] == "--launch-two")
{
    foreach (var profile in settings.Profiles.Take(2)) { var pid = await Launcher.Open(store, settings, profile); Console.WriteLine(profile.Name + ": native PID " + pid + ", separate directory confirmed."); }
}
else if (args.Length == 1 && args[0] == "--status")
{
    foreach (var profile in settings.Profiles)
    {
        var data = store.Data(profile);
        Console.WriteLine(profile.Name + ": config=" + File.Exists(Path.Combine(data, "config.json")) + ", cookies=" + File.Exists(Path.Combine(data, "Network", "Cookies")));
    }
}
else if (args.Length == 1 && args[0] == "--discover")
{
    var found = await ClaudeDiscovery.FindAsync();
    Console.WriteLine(found ?? "No compatible installed Claude application found.");
}
else if (args.Length == 1 && args[0] == "--import-known")
{
    foreach (var profile in settings.Profiles)
    {
        var result = SessionIndex.Import(store, profile, knownAccountsOnly: true);
        Console.WriteLine(profile.Name + ": " + result.Added + " added; " + result.Updated + " updated with backup; " + result.Existing + " existing records preserved; " + result.Rejected + " outside account or invalid.");
    }
}
else throw new ArgumentException("Use --configure EXE, --launch-two, or --status. Callbacks are handled only by the GUI executable.");
