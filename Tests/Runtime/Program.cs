using System.Globalization;
using System.Text.Json;
using Advertisement;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities;
using PanoramaManager;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static string CaptureOutput(Action action)
{
    var previous = Console.Out;
    using var output = new StringWriter();
    try
    {
        Console.SetOut(output);
        action();
        return output.ToString();
    }
    finally { Console.SetOut(previous); }
}
static CCSPlayerController Player(int slot, int identity = 1)
{
    var player = new CCSPlayerController { Slot = slot, Handle = identity * 100 + slot, PlayerName = "P" + slot };
    Utilities.Players[slot] = player;
    return player;
}
static Ads Start(Config? config = null, bool hotReload = true)
{
    Array.Clear(Utilities.Players);
    var plugin = new Ads();
    plugin.OnConfigParsed(config ?? new Config { Ads = [], WelcomeMessage = null });
    plugin.Load(hotReload);
    return plugin;
}
static void Tick(Ads plugin) => ((Listeners.OnTick)plugin.Hooks[typeof(Listeners.OnTick)])();
static void Authorize(Ads plugin, CCSPlayerController p) =>
    ((Listeners.OnClientAuthorized)plugin.Hooks[typeof(Listeners.OnClientAuthorized)])(p.Slot, new SteamID());
static void Connect(Ads plugin, CCSPlayerController p) =>
    ((Func<EventPlayerConnectFull, GameEventInfo, HookResult>)plugin.Hooks[typeof(EventPlayerConnectFull)])(new() { Userid = p }, new());
static void Disconnect(Ads plugin, int slot) => ((Listeners.OnClientDisconnect)plugin.Hooks[typeof(Listeners.OnClientDisconnect)])(slot);
static void MapEnd(Ads plugin) => ((Listeners.OnMapEnd)plugin.Hooks[typeof(Listeners.OnMapEnd)])();
static void Command(Ads plugin, CCSPlayerController p, string name = "css_test") => plugin.Commands[name](p, new());
static Config HtmlConfig(float duration = 2f / 64, bool deadOnly = false) => new()
{
    Ads = [], WelcomeMessage = null, PrintToCenterHtml = true,
    HtmlCenterDuration = duration, ShowHtmlWhenDead = deadOnly,
    Commands = [new() { Names = ["css_test"], Center = "{PLAYERNAME}" }]
};

// Exercise production OnTick, command, timer and lifecycle callbacks, not a copied model.
foreach (var count in new[] { 0, 1, 8, 32 })
foreach (var duration in new[] { 0f, 1f / 64, 1.2f / 64, 5f })
{
    var plugin = Start(HtmlConfig(duration));
    var players = Enumerable.Range(0, count).Select(slot => Player(slot)).ToArray();
    foreach (var p in players) { Authorize(plugin, p); Command(plugin, p); }
    var expected = (int)Math.Ceiling((double)duration / Server.TickInterval);
    for (var t = 0; t < expected + 2; t++) Tick(plugin);
    Check(players.All(p => p.HtmlSends == expected && (expected == 0 || p.LastText == p.PlayerName)), "HTML duration/text changed");
    Utilities.Lookups = 0;
    Tick(plugin);
    Check(Utilities.Lookups == 0, "Idle/expired HTML queried players");
    plugin.Unload(false);
}
Console.WriteLine("PASS: actual OnTick 0/1/8/32 recipients, fractional/zero/5s expiry and idle fast path.");

{
    var plugin = Start(HtmlConfig(deadOnly: true));
    var p = Player(0); Authorize(plugin, p); Command(plugin, p);
    p.PawnIsAlive = true;
    for (var i = 0; i < 1000; i++) Tick(plugin);
    Check(p.HtmlSends == 0, "Alive displayed dead-only HTML");
    p.PawnIsAlive = false; Tick(plugin);
    p.PawnIsAlive = true; Tick(plugin);
    p.PawnIsAlive = false; Tick(plugin); Tick(plugin);
    Check(p.HtmlSends == 2, "Alive/dead transition changed PrintTime");
    Command(plugin, p); Tick(plugin); Command(plugin, p); Tick(plugin); Tick(plugin); Tick(plugin);
    Check(p.HtmlSends == 5, "Replacement did not reset budget");
    Command(plugin, p);
    var replacement = Player(0, 2); Tick(plugin);
    Check(replacement.HtmlSends == 0, "Controller replacement received stale HTML");
    Authorize(plugin, replacement); Command(plugin, replacement); Disconnect(plugin, 0); Tick(plugin);
    Check(replacement.HtmlSends == 0, "Disconnect retained active HTML");
    Authorize(plugin, replacement); Command(plugin, replacement); MapEnd(plugin); Tick(plugin);
    Check(replacement.HtmlSends == 0, "Map retained active HTML");
    plugin.Unload(false);
}
Console.WriteLine("PASS: alive/dead budget, replacement, controller identity, disconnect, slot reuse and map cleanup.");

foreach (var type in new[] { MessageType.Chat, MessageType.Center, MessageType.CenterHtml })
{
    var plugin = Start(new Config { Ads = [], HtmlCenterDuration = 1f / 64,
        WelcomeMessage = new() { Message = "Welcome {PLAYERNAME}", DisplayDelay = 3, MessageType = type } });
    var p = Player(1); Authorize(plugin, p); Connect(plugin, p);
    var first = plugin.Timers.Single();
    Connect(plugin, p);
    Check(first.Cancelled && plugin.Timers.Count == 1, "Duplicate welcome retained timer");
    plugin.Timers.Single().Fire(); Tick(plugin);
    Check(plugin.Timers.Count == 0 && p.LastText == "Welcome P1" && p.ChatSends + p.CenterSends + p.HtmlSends == 1, "Welcome output/completion failed");
    for (var i = 0; i < 1000; i++) { Connect(plugin, p); plugin.Timers.Single().Fire(); }
    Check(plugin.Timers.Count == 0, "One-shot timer ownership grows");
    Connect(plugin, p); var pending = plugin.Timers.Single(); Disconnect(plugin, 1);
    Check(pending.Cancelled && plugin.Timers.Count == 0, "Disconnect retained welcome");
    Authorize(plugin, p); Connect(plugin, p); pending = plugin.Timers.Single(); MapEnd(plugin);
    Check(pending.Cancelled && plugin.Timers.Count == 0, "Map retained welcome");
    Authorize(plugin, p); Connect(plugin, p); pending = plugin.Timers.Single(); plugin.Unload(false);
    Check(pending.Cancelled && plugin.Timers.Count == 0, "Unload retained welcome");
}
Console.WriteLine("PASS: Welcome destinations/delay, duplicate connect, 1000 timer completions, disconnect/map/unload cancellation.");

foreach (var text in new string?[] { null, "", " \t\r\n" })
{
    // Exercise explicit JSON null as well as empty text, which nullable annotations do not reject.
    var json = JsonSerializer.Serialize(new Config
    {
        Ads = [new() { Interval = 35, Messages = [new() { Chat = "rotation" }] }],
        Commands = [new() { Names = ["css_test"], Chat = "command" }],
        WelcomeMessage = new() { Message = text!, DisplayDelay = 0 }
    });
    var config = JsonSerializer.Deserialize<Config>(json)!;
    Ads plugin = null!;
    var output = CaptureOutput(() => plugin = Start(config));
    Check(plugin.Config.WelcomeMessage is null && output.Contains("WelcomeMessage.Message"),
        "Invalid welcome text was not disabled with a diagnostic");
    var p = Player(0); Authorize(plugin, p); Connect(plugin, p);
    Check(plugin.Timers.Count == 1, "Invalid welcome scheduled a timer");
    plugin.Timers.Single().Fire(); Check(p.LastText == " rotation", "Invalid welcome disabled rotating ads");
    Command(plugin, p); Check(p.LastText == " command", "Invalid welcome disabled ad commands");
    plugin.Unload(false);
}
{
    var disabled = JsonSerializer.Deserialize<Config>("""{"Ads":[],"WelcomeMessage":null}""")!;
    Ads plugin = null!;
    var output = CaptureOutput(() => plugin = Start(disabled));
    var p = Player(0); Authorize(plugin, p); Connect(plugin, p);
    Check(plugin.Timers.Count == 0 && output.Length == 0, "Explicitly disabled welcome warned or scheduled work");
    plugin.Unload(false);
    var defaults = JsonSerializer.Deserialize<Config>("""{"Ads":[]}""")!;
    Check(defaults.WelcomeMessage is not null, "Omitted welcome no longer retains defaults");
}
Console.WriteLine("PASS: JSON null/empty/whitespace welcome, isolated disable, explicit null and omitted defaults.");

foreach (var (interval, duration, enabled, hasMessages, warn) in new[]
{
    (1f, 2f, true, true, true),
    (2.95f, 2f, true, true, false),
    (3f, 2f, true, true, false),
    (1f, 0f, true, true, false),
    (60f, 2f, true, true, false),
    (1f, 2f, false, true, false),
    (1f, 2f, true, false, false)
})
{
    var plugin = new Ads();
    var output = CaptureOutput(() => plugin.OnConfigParsed(new Config
    {
        Ads = [], WelcomeMessage = null, PanoramaEnabled = enabled,
        PanoramaInterval = interval, PanoramaDuration = duration,
        PanoramaMessages = hasMessages ? ["test"] : []
    }));
    Check(output.Contains("interrupt") == warn, "Panorama overlap diagnostic boundary/enablement changed");
    Check(plugin.Config.PanoramaInterval == interval && plugin.Config.PanoramaDuration == duration,
        "Panorama warning changed configured timing");
}
{
    Ads plugin = null!;
    CaptureOutput(() => plugin = Start(new Config
    {
        Ads = [], WelcomeMessage = null, PanoramaEnabled = true,
        PanoramaInterval = 1, PanoramaDuration = 2, PanoramaMessages = ["test"]
    }));
    var p = Player(0); Authorize(plugin, p);
    var rotation = plugin.Timers.Single();
    for (var i = 0; i < 100; i++)
    {
        var previousAnimations = plugin.Timers.Where(t => t != rotation).ToArray();
        rotation.Fire();
        Check(previousAnimations.All(t => t.Cancelled) && plugin.Timers.Count == 4,
            "Overlapping Panorama retained old timers or added diagnostic timers");
    }
    Check(plugin.Timers.Where(t => t != rotation).Select(t => t.Interval).Order()
        .SequenceEqual(new[] { 0.05f, 2.5f, 2.95f }), "Panorama animation timing changed");
    plugin.Unload(false); Check(plugin.Timers.Count == 0, "Overlap retained timers on unload");
}
Console.WriteLine("PASS: Panorama overlap warning boundaries, unchanged timing and bounded timers.");

{
    var plugin = Start(new Config { Ads = [], WelcomeMessage = null,
        Commands = [new() { Names = ["css_test", "CSS_TEST", "CSS_ADVERT_RELOAD"], Chat = "{hello} {PLAYERNAME} {unknown} {PLAYERS}" }] });
    Check(plugin.Commands.Count == 1, "Duplicate/reserved commands registered");
    var p = Player(0); Authorize(plugin, p); Command(plugin, p);
    Check(p.LastText == " Hello P0 {unknown} 1", "English translation/tags changed");
    var reads = plugin.Localizer.Reads;
    Command(plugin, p); Check(plugin.Localizer.Reads == reads, "Localization cache missed");
    p.Language = CultureInfo.GetCultureInfo("ru"); Command(plugin, p);
    Check(p.LastText == " Привет P0 {unknown} 1", "Language switch reused stale translation");
    var q = Player(1); q.PlayerPawn.Value = null; Command(plugin, p);
    Check(p.LastText.EndsWith(" 1"), "Player count no longer follows valid pawns");
    plugin.OnConfigParsed(plugin.Config); Command(plugin, p);
    Check(plugin.Localizer.Reads > reads, "Config did not invalidate localization");
    plugin.Unload(false);
}
Console.WriteLine("PASS: command aliases, reserved reload, localization cache/invalidation, language switching and pawn count.");

{
    var plugin = Start(new Config { WelcomeMessage = null,
        Ads = [new() { Interval = 35, Messages = [new() { Chat = "{SERVERNAME} {PLAYERS}" }] }],
        Commands = Enumerable.Range(0, 4200).Select(i => new AdCommand { Names = ["css_" + i], Chat = "{hello} " + i }).ToList() });
    var p = Player(0); Authorize(plugin, p);
    for (var i = 0; i < 4200; i++) Command(plugin, p, "css_" + i);
    var reads = plugin.Localizer.Reads;
    Command(plugin, p, "css_4199"); Command(plugin, p, "css_4199");
    Check(plugin.Localizer.Reads == reads + 2, "Localization cache exceeded bound");
    for (var slot = 1; slot < 8; slot++) { var q = Player(slot); Authorize(plugin, q); }
    Utilities.Lookups = 0; ConVar.Reads = 0;
    plugin.Timers.Single().Fire();
    Check(ConVar.Reads == 1 && Utilities.Lookups == 128, "Shared placeholders repeated native reads per player");
    plugin.Unload(false);
}

{
    Array.Clear(Utilities.Players);
    var p = Player(0);
    var plugin = new Ads(); plugin.OnConfigParsed(HtmlConfig()); plugin.Load(true);
    Command(plugin, p); Tick(plugin);
    Check(p.HtmlSends == 1, "Hot reload did not initialize connected player");
    plugin.Unload(true);
}
Console.WriteLine("PASS: bounded localization, shared dispatch values and hot reload with a connected player.");

{
    var plugin = Start(new Config { WelcomeMessage = null,
        Ads = [new() { Interval = 35, Messages = [new() { Chat = "A" }, new() { Chat = "B" }] },
               new() { Interval = 0, Messages = [new() { Chat = "disabled" }] }] });
    var p = Player(0); Authorize(plugin, p);
    Check(plugin.Timers.Single().Interval == 35, "Ad interval changed");
    plugin.Timers.Single().Fire(); Check(p.LastText == " A", "First ad changed");
    plugin.Timers.Single().Fire(); Check(p.LastText == " B", "Rotation changed");
    plugin.Timers.Single().Fire(); Check(p.LastText == " A", "Rotation wrap changed");
    plugin.Unload(false);
    Check(plugin.Timers.Count == 0, "Unload retained repeating timer");
}

// Reload reads the same path as the API. All files are under this test's bin/obj tree.
{
    var plugin = Start(new Config { PrintToCenterHtml = true,
        Ads = [new() { Interval = 60, Messages = [new() { Chat = "original" }] }],
        Commands = [new() { Names = ["css_test"], Center = "original" }],
        WelcomeMessage = new() { Message = "welcome", DisplayDelay = 5 } });
    var p = Player(0); Authorize(plugin, p); Connect(plugin, p);
    var oldTimers = plugin.Timers.ToArray();
    var directory = Path.Combine(Path.GetDirectoryName(typeof(BasePlugin).Assembly.Location)!, "..", "configs", "plugins", "Advertisement");
    Directory.CreateDirectory(directory);
    var file = Path.Combine(directory, "Advertisement.json");
    if (File.Exists(file)) File.Delete(file);
    var previous = plugin.Config;
    plugin.ReloadAdvertConfig(null, new());
    Check(ReferenceEquals(previous, plugin.Config) && plugin.Commands.Count == 1, "Missing config mutated state");
    File.WriteAllText(file, "{"); plugin.ReloadAdvertConfig(null, new());
    Check(ReferenceEquals(previous, plugin.Config) && plugin.Commands.Count == 1, "Invalid config mutated state");
    Check(plugin.Timers.SequenceEqual(oldTimers) && oldTimers.All(t => !t.Cancelled), "Rejected reload cancelled live timers");
    Command(plugin, p); Tick(plugin); Check(p.LastText == "original", "Rejected reload lost live output");
    File.WriteAllText(file, JsonSerializer.Serialize(new Config { Ads = [], Commands = [new() { Names = ["css_new"], Chat = "new" }],
        WelcomeMessage = new() { Message = null!, DisplayDelay = 0 } }));
    var reloadOutput = CaptureOutput(() => plugin.ReloadAdvertConfig(null, new()));
    Check(!ReferenceEquals(previous, plugin.Config) && plugin.Commands.ContainsKey("css_new") && !plugin.Commands.ContainsKey("css_test"), "Reload command invalidation failed");
    Check(plugin.Timers.Count == 0 && oldTimers.All(t => t.Cancelled), "Successful reload retained old/welcome timers");
    Connect(plugin, p); Command(plugin, p, "css_new");
    Check(plugin.Config.WelcomeMessage is null && plugin.Timers.Count == 0 && p.LastText == " new"
        && reloadOutput.Contains("WelcomeMessage.Message"), "Reload did not isolate invalid welcome text");
    File.Delete(file); plugin.Unload(false);
}
Console.WriteLine("PASS: ad intervals/rotation, repeating timer cleanup, missing/malformed/successful reload.");

{
    var plugin = Start(new Config { Ads = [], WelcomeMessage = null, PanoramaEnabled = true,
        PanoramaInterval = 60, PanoramaDuration = 2, PanoramaMessages = ["{RED}{PLAYERNAME}"] });
    var p = Player(0); Authorize(plugin, p);
    var rotation = plugin.Timers.Single(); rotation.Fire();
    var layout = Panorama.Last!;
    Check(!layout.Contract.CaptureInput, "Panorama captures input");
    Check(layout.Text[(0, "message0")] == "P0", "Personal Panorama text lost");
    plugin.Timers.Single(t => t.Interval == 0.05f).Fire();
    layout.Restore(p);
    Check(layout.Text[(0, "message0")] == "P0" && layout.Classes[(0, "AdvertisementCard", "enter")], "Restored lost personal text/phase");
    foreach (var timer in plugin.Timers.Where(t => t != rotation).ToArray()) timer.Fire();
    Check(plugin.Timers.Count == 1, "Panorama animation timers leaked");
    rotation.Fire(); MapEnd(plugin);
    Check(plugin.Timers.Count == 1, "Map retained animation timers");
    plugin.Unload(false); Check(plugin.Timers.Count == 0, "Unload retained Panorama timers");
}
Console.WriteLine("PASS: Panorama personal text/Restored phase, CaptureInput=false and animation timer lifecycle.");
