using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Config;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using PanoramaManager;

namespace Advertisement;

public class User
{
    public bool HtmlPrint { get; set; }
    public string Message { get; set; } = string.Empty;
    public long PrintTime { get; set; }
}

public class Ads : BasePlugin, IPluginConfig<Config>
{
    private const float PanoramaCloseDelay = 0.95f;
    public override string ModuleAuthor => "thesamefabius";
    public override string ModuleName => "Advertisement";
    public override string ModuleVersion => $"v{typeof(Ads).Assembly.GetName().Version!.ToString(3)}";

    private readonly List<Timer> _timers = [];
    private readonly List<Timer> _panoramaAnimationTimers = [];
    private readonly Dictionary<string, CommandInfo.CommandCallback> _adCommands = new(StringComparer.OrdinalIgnoreCase);
    private readonly long[] _panoramaGenerations = new long[66];
    private PanelHandle? _panoramaLayout;
    private bool _panoramaInitialized;
    private int _panoramaMessageIndex;
    private static readonly string[] PanelIds = Enumerable.Range(0, PanoramaText.MaxRuns).Select(i => $"AdvertisementText{i}").ToArray();
    private static readonly string[] VariableIds = Enumerable.Range(0, PanoramaText.MaxRuns).Select(i => $"message{i}").ToArray();
    private readonly List<PanoramaTextRun>?[] _panoramaRuns = new List<PanoramaTextRun>?[66];
    private readonly bool[] _panoramaEnter = new bool[66], _panoramaExit = new bool[66];

    public Config Config { get; set; } = new();
    private readonly User?[] _users = new User?[66];
    private readonly HashSet<int> _activeHtml = [];
    private readonly List<int> _expiredHtml = [];
    private readonly nint[] _controllers = new nint[66];
    private readonly long[] _connections = new long[66];
    private readonly Timer?[] _welcomeTimers = new Timer?[66];
    private long _mapGeneration;
    private long _configGeneration;
    private double _durationTicks;
    private readonly Dictionary<(string Message, string Language), string> _localized = [];
    private static readonly Regex TranslationTags = new(@"\{([^{}]+)\}", RegexOptions.Compiled);
    private readonly Dictionary<string, string> _dispatchValues = [];

    private Timer TrackTimer(float interval, Action callback, TimerFlags flags = 0)
    {
        Timer? timer = null;
        timer = AddTimer(interval, () =>
        {
            if ((flags & TimerFlags.REPEAT) == 0 && timer is not null)
            {
                Timers.Remove(timer);
                _timers.Remove(timer);
                _panoramaAnimationTimers.Remove(timer);
            }
            callback();
        }, flags);
        return timer;
    }

    private void CancelTimer(Timer timer)
    {
        if (Timers.Remove(timer)) timer.Kill();
    }

    private void ResetSlot(int slot)
    {
        if ((uint)slot >= _users.Length) return;
        if (_welcomeTimers[slot] is { } pending) CancelTimer(pending);
        _welcomeTimers[slot] = null;
        _connections[slot]++;
        _panoramaGenerations[slot]++;
        _controllers[slot] = 0;
        _panoramaRuns[slot] = null;
        _activeHtml.Remove(slot);
        _users[slot] = null;
    }

    public void OnConfigParsed(Config config)
    {
        if (config.WelcomeMessage is { } welcome && string.IsNullOrWhiteSpace(welcome.Message))
        {
            Console.WriteLine("[Advertisement] Config 'WelcomeMessage.Message' must contain text; the welcome message will be disabled.");
            config.WelcomeMessage = null;
        }

        if (config.Ads is null)
        {
            Console.WriteLine("[Advertisement] Config 'Ads' is null; no rotating ads will be registered.");
            config.Ads = [];
        }

        if (config.Commands is null)
        {
            Console.WriteLine("[Advertisement] Config 'Commands' is null; no ad commands will be registered.");
            config.Commands = [];
        }

        if (config.Ads.RemoveAll(ad => ad is null) > 0)
            Console.WriteLine("[Advertisement] Null ad groups were found in config and have been ignored.");

        foreach (var ad in config.Ads)
        {
            if (ad.Messages is null)
            {
                Console.WriteLine("[Advertisement] An ad group has a null 'Messages' list; the group will be skipped.");
                ad.Messages = [];
            }

            if (ad.Messages.RemoveAll(message => message is null) > 0)
                Console.WriteLine("[Advertisement] Null messages were found in an ad group and have been ignored.");
        }

        if (config.Commands.RemoveAll(command => command is null) > 0)
            Console.WriteLine("[Advertisement] Null ad commands were found in config and have been ignored.");

        foreach (var command in config.Commands)
        {
            if (command.Names is null)
            {
                Console.WriteLine("[Advertisement] An ad command has a null 'Names' list; it will not be registered.");
                command.Names = [];
            }

            if (command.Names.RemoveAll(name => name is null) > 0)
                Console.WriteLine("[Advertisement] Null command names were found in config and have been ignored.");
        }

        if (config.HtmlCenterDuration is float duration && (!float.IsFinite(duration) || duration < 0))
        {
            Console.WriteLine("[Advertisement] Config 'HtmlCenterDuration' must be a finite, non-negative number; using 5 seconds.");
            config.HtmlCenterDuration = 5;
        }

        if (config.PanoramaMessages is null)
        {
            Console.WriteLine("[Advertisement] Config 'PanoramaMessages' is null; Panorama ads will be disabled.");
            config.PanoramaMessages = [];
        }

        if (config.PanoramaMessages.RemoveAll(string.IsNullOrWhiteSpace) > 0)
            Console.WriteLine("[Advertisement] Empty Panorama messages were found in config and have been ignored.");

        if (!float.IsFinite(config.PanoramaInterval) || config.PanoramaInterval < 1)
        {
            Console.WriteLine("[Advertisement] Config 'PanoramaInterval' must be at least 1 second; using 60 seconds.");
            config.PanoramaInterval = 60;
        }

        if (!float.IsFinite(config.PanoramaDuration) || config.PanoramaDuration < 0)
        {
            Console.WriteLine("[Advertisement] Config 'PanoramaDuration' must be a finite, non-negative number; using 2 seconds.");
            config.PanoramaDuration = 2;
        }

        if (config.PanoramaEnabled && config.PanoramaMessages.Count > 0
            && config.PanoramaInterval < PanoramaCloseDelay + config.PanoramaDuration)
            Console.WriteLine("[Advertisement] Config 'PanoramaInterval' is shorter than 'PanoramaDuration' + 0.95 seconds; each new card will interrupt the previous animation. Increase the interval to allow it to finish.");

        Config = config;
        _durationTicks = Math.Ceiling((double)(config.HtmlCenterDuration ?? 5) / Server.TickInterval);
        _localized.Clear();
        _configGeneration++;
    }

    public override void Load(bool hotReload)
    {
        RegisterEventHandler<EventPlayerConnectFull>(EventPlayerConnectFull);
        RegisterListener<Listeners.OnClientAuthorized>(OnClientAuthorized);
        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterListener<Listeners.OnClientDisconnect>(ResetSlot);
        RegisterListener<Listeners.OnMapEnd>(() =>
        {
            _mapGeneration++;
            for (var slot = 0; slot < _users.Length; slot++) ResetSlot(slot);
            StopPanoramaAnimationTimers();
        });

        StartTimers();
        RegisterAdCommands();

        if (hotReload)
        {
            foreach (var player in Utilities.GetPlayers())
            {
                _users[player.Slot] = new User();
                _controllers[player.Slot] = player.Handle;
            }
        }
    }

    public override void Unload(bool hotReload)
    {
        InvalidatePanoramaMessages();
        for (var slot = 0; slot < _welcomeTimers.Length; slot++)
        {
            if (_welcomeTimers[slot] is { } pending) CancelTimer(pending);
            _welcomeTimers[slot] = null;
        }
        StopPanoramaAnimationTimers();
        foreach (var timer in _timers)
            CancelTimer(timer);
        _timers.Clear();
        _panoramaLayout?.Dispose();
        _panoramaLayout = null;
        if (_panoramaInitialized)
        {
            Panorama.Shutdown();
            _panoramaInitialized = false;
        }
    }

    private void RegisterAdCommands()
    {
        foreach (var (name, callback) in _adCommands)
            RemoveCommand(name, callback);
        _adCommands.Clear();

        foreach (var command in Config.Commands)
        {
            foreach (var name in command.Names.Select(c => c.Trim()).Where(c => c.Length > 0))
            {
                CommandInfo.CommandCallback callback = (player, _) =>
                {
                    if (player is { IsValid: true, IsBot: false })
                        ShowMessage(command, [player]);
                };

                if (!name.Equals("css_advert_reload", StringComparison.OrdinalIgnoreCase) && _adCommands.TryAdd(name, callback))
                    AddCommand(name, "Shows the advertisement", callback);
            }
        }
    }

    private void OnClientAuthorized(int slot, SteamID id)
    {
        if (slot < 0 || slot >= _users.Length)
            return;

        ResetSlot(slot);
        _users[slot] = new User();
    }

    private HookResult EventPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        if (Config.WelcomeMessage == null)
            return HookResult.Continue;

        var player = @event.Userid;
        if (player is null || !player.IsValid)
            return HookResult.Continue;

        ShowWelcomeMessage(player, Config.WelcomeMessage);
        return HookResult.Continue;
    }

    private void OnTick()
    {
        if (_activeHtml.Count == 0) return;
        _expiredHtml.Clear();
        foreach (var slot in _activeHtml)
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            var user = _users[slot];
            if (player is null || !player.IsValid || player.Connected != PlayerConnectedState.Connected
                || player.Handle != _controllers[slot] || user is null)
            {
                _expiredHtml.Add(slot);
                continue;
            }
            var elapsed = user.PrintTime;
            var decision = HtmlPlayback.Advance(ref elapsed, _durationTicks, Config.ShowHtmlWhenDead == true, player.PawnIsAlive);
            user.PrintTime = elapsed;
            if (decision == HtmlDecision.Paused) continue;
            if (decision == HtmlDecision.Expired)
            {
                user.HtmlPrint = false;
                _expiredHtml.Add(slot);
                continue;
            }
            player.PrintToCenterHtml(user.Message);
        }
        foreach (var slot in _expiredHtml) _activeHtml.Remove(slot);
    }

    private void ShowAd(Advertisement ad)
    {
        var players = Utilities.GetPlayers();
        players.RemoveAll(u => u.IsBot || !u.IsValid);
        ShowMessage(ad.NextMessage, players);
    }

    private void ShowMessage(AdMessage message, List<CCSPlayerController> players)
    {
        _dispatchValues.Clear();
        foreach (var player in players)
        {
            if (message.Chat != null)
                PrintToPlayer(player, HudDestination.Chat, message.Chat);
            if (message.Center != null)
                PrintToPlayer(player, HudDestination.Center, message.Center);
        }
    }

    private void StartTimers()
    {
        foreach (var ad in Config.Ads.Where(ad => ad.Messages.Count > 0 && float.IsFinite(ad.Interval) && ad.Interval > 0))
        {
            _timers.Add(TrackTimer(ad.Interval, () => ShowAd(ad), TimerFlags.REPEAT));
        }

        EnsurePanoramaLayout();
        if (Config.PanoramaEnabled && Config.PanoramaMessages.Count > 0)
            _timers.Add(TrackTimer(Config.PanoramaInterval, ShowPanoramaMessage, TimerFlags.REPEAT));
    }

    private void EnsurePanoramaLayout()
    {
        if (!Config.PanoramaEnabled || Config.PanoramaMessages.Count == 0)
        {
            _panoramaLayout?.Dispose();
            _panoramaLayout = null;
            return;
        }

        if (!_panoramaInitialized)
        {
            Panorama.Init(this);
            _panoramaInitialized = true;
        }

        if (_panoramaLayout is not null) return;
        _panoramaLayout = Panorama.Spawn(
            "panorama/layout/custom_game/advertisement_panorama.vxml_c",
            new LayoutContract
            {
                RootPanelId = "AdvertisementRoot",
                RowCount = 0,
                CaptureInput = false
            });
        _panoramaLayout.OnEvent += e =>
        {
            if (e.Action != PanelAction.Restored || (uint)e.Player.Slot >= _panoramaRuns.Length) return;
            var slot = e.Player.Slot;
            if (_panoramaRuns[slot] is not { } runs) return;
            DrawPanoramaRuns(e.Player, runs);
            _panoramaLayout.SetClassFor(e.Player, "AdvertisementCard", "show", true);
            _panoramaLayout.SetClassFor(e.Player, "AdvertisementCard", "enter", _panoramaEnter[slot]);
            _panoramaLayout.SetClassFor(e.Player, "AdvertisementCard", "exit", _panoramaExit[slot]);
        };
    }

    private void ShowPanoramaMessage()
    {
        if (_panoramaLayout is null || Config.PanoramaMessages.Count == 0)
            return;

        StopPanoramaAnimationTimers();
        _dispatchValues.Clear();
        var message = Config.PanoramaMessages[_panoramaMessageIndex];
        _panoramaMessageIndex = (_panoramaMessageIndex + 1) % Config.PanoramaMessages.Count;
        var recipients = new List<(CCSPlayerController Player, long Generation)>();
        var parsedMessages = new Dictionary<string, List<PanoramaTextRun>>(StringComparer.Ordinal);
        foreach (var player in Utilities.GetPlayers().Where(player => player is { IsValid: true, IsBot: false }))
        {
            if (player.Slot < 0 || player.Slot >= _panoramaGenerations.Length)
                continue;

            var generation = ++_panoramaGenerations[player.Slot];
            _panoramaLayout.Close(player);
            _panoramaLayout.Open(player);
            var formatted = ReplaceMessageTags(message, player, applyColorTags: false, panorama: true);
            if (!parsedMessages.TryGetValue(formatted, out var runs))
                parsedMessages[formatted] = runs = PanoramaText.Parse(formatted);
            if (runs.Count > PanoramaText.MaxRuns)
            {
                Console.WriteLine($"[Advertisement] Panorama message exceeds {PanoramaText.MaxRuns} color segments; displaying the full message in the default color.");
                runs = [new PanoramaTextRun(string.Concat(runs.Select(run => run.Text)), "default")];
            }

            _panoramaRuns[player.Slot] = runs;
            _panoramaEnter[player.Slot] = _panoramaExit[player.Slot] = false;
            DrawPanoramaRuns(player, runs);
            _panoramaLayout.SetClassFor(player, "AdvertisementCard", "show", true);
            _panoramaLayout.SetClassFor(player, "AdvertisementCard", "enter", false);
            _panoramaLayout.SetClassFor(player, "AdvertisementCard", "exit", false);
            recipients.Add((player, generation));
        }

        if (recipients.Count == 0)
            return;

        SchedulePanoramaAnimation(0.05f, () => SetPanoramaClasses(recipients, enter: true));
        SchedulePanoramaAnimation(0.5f + Config.PanoramaDuration, () =>
        {
            SetPanoramaClasses(recipients, enter: false);
            SetPanoramaClasses(recipients, exit: true);
        });
        SchedulePanoramaAnimation(PanoramaCloseDelay + Config.PanoramaDuration, () =>
        {
            SetPanoramaClasses(recipients, exit: false);
            SetPanoramaClasses(recipients, show: false);
            foreach (var (player, generation) in recipients)
            {
                if (player.IsValid && _panoramaGenerations[player.Slot] == generation)
                {
                    _panoramaLayout?.Close(player);
                    _panoramaRuns[player.Slot] = null;
                }
            }
        });
    }

    private void DrawPanoramaRuns(CCSPlayerController player, List<PanoramaTextRun> runs)
    {
        if (_panoramaLayout is null) return;
        for (var index = 0; index < PanoramaText.MaxRuns; index++)
        {
            var panelId = PanelIds[index];
            var active = index < runs.Count;
            _panoramaLayout.SetVariableFor(player, VariableIds[index], active ? runs[index].Text : string.Empty);
            _panoramaLayout.SetClassFor(player, panelId, "hidden", !active);
            if (active)
                _panoramaLayout.SetClassFor(player, panelId, $"color-{runs[index].Color}", true);
        }
    }

    private void SchedulePanoramaAnimation(float delay, Action callback)
    {
        Timer? timer = null;
        timer = TrackTimer(delay, () =>
        {
            if (timer is not null)
                _panoramaAnimationTimers.Remove(timer);

            callback();
        });
        _panoramaAnimationTimers.Add(timer);
    }

    private void SetPanoramaClasses(
        IEnumerable<(CCSPlayerController Player, long Generation)> recipients,
        bool? show = null,
        bool? enter = null,
        bool? exit = null)
    {
        foreach (var (player, generation) in recipients)
        {
            if (!player.IsValid
                || player.Slot < 0
                || player.Slot >= _panoramaGenerations.Length
                || _panoramaGenerations[player.Slot] != generation
                || _panoramaLayout is null)
            {
                continue;
            }

            if (show.HasValue)
                _panoramaLayout.SetClassFor(player, "AdvertisementCard", "show", show.Value);
            if (enter.HasValue) _panoramaEnter[player.Slot] = enter.Value;
            if (exit.HasValue) _panoramaExit[player.Slot] = exit.Value;
            if (enter.HasValue)
                _panoramaLayout.SetClassFor(player, "AdvertisementCard", "enter", enter.Value);
            if (exit.HasValue)
                _panoramaLayout.SetClassFor(player, "AdvertisementCard", "exit", exit.Value);
        }
    }

    private void StopPanoramaAnimationTimers()
    {
        foreach (var timer in _panoramaAnimationTimers)
            CancelTimer(timer);
        _panoramaAnimationTimers.Clear();
    }

    private void HidePanoramaCards()
    {
        if (_panoramaLayout is null)
            return;

        foreach (var player in Utilities.GetPlayers().Where(player => player is { IsValid: true, IsBot: false }))
        {
            _panoramaLayout.SetClassFor(player, "AdvertisementCard", "show", false);
            _panoramaLayout.SetClassFor(player, "AdvertisementCard", "enter", false);
            _panoramaLayout.SetClassFor(player, "AdvertisementCard", "exit", false);
            _panoramaLayout.Close(player);
        }
    }

    private void InvalidatePanoramaMessages()
    {
        for (var slot = 0; slot < _panoramaGenerations.Length; slot++)
        {
            _panoramaGenerations[slot]++;
            _panoramaRuns[slot] = null;
        }
    }

    [RequiresPermissions("@css/root")]
    [ConsoleCommand("css_advert_reload", "configuration restart")]
    public void ReloadAdvertConfig(CCSPlayerController? controller, CommandInfo command)
    {
        Config replacement;
        try
        {
            var name = Path.GetFileName(ModuleDirectory);
            var directory = Path.Combine(Path.GetDirectoryName(typeof(BasePlugin).Assembly.Location)!, "..", "configs", "plugins", name);
            if (!File.Exists(Path.Combine(directory, name + ".json")) && !File.Exists(Path.Combine(directory, name + ".toml")))
                throw new FileNotFoundException("Existing configuration was not found; retaining current settings");
            replacement = ConfigManager.Load<Config>(Path.GetFileName(ModuleDirectory)) ?? throw new InvalidDataException("Config is null");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[Advertisement] Reload rejected: {exception.Message}");
            return;
        }
        InvalidatePanoramaMessages();
        for (var slot = 0; slot < _welcomeTimers.Length; slot++)
        {
            if (_welcomeTimers[slot] is { } pending) CancelTimer(pending);
            _welcomeTimers[slot] = null;
        }
        StopPanoramaAnimationTimers();
        HidePanoramaCards();
        OnConfigParsed(replacement);
        _panoramaMessageIndex = 0;

        foreach (var timer in _timers) CancelTimer(timer);
        _timers.Clear();
        StartTimers();
        RegisterAdCommands();

        const string msg = "\x08[\x0C Advertisement \x08] configuration reloaded successfully!";
        if (controller == null)
            Console.WriteLine(msg);
        else
            controller.PrintToChat(msg);
    }

    private void ShowWelcomeMessage(CCSPlayerController player, WelcomeMessage welcomeMessage)
    {
        if (player.IsBot || !float.IsFinite(welcomeMessage.DisplayDelay) || welcomeMessage.DisplayDelay < 0)
            return;

        var slot = player.Slot;
        if ((uint)slot >= _connections.Length) return;
        var connection = _connections[slot];
        var map = _mapGeneration;
        var config = _configGeneration;
        var handle = player.Handle;
        if (_welcomeTimers[slot] is { } previous) CancelTimer(previous);
        _welcomeTimers[slot] = TrackTimer(welcomeMessage.DisplayDelay, () =>
        {
            _welcomeTimers[slot] = null;
            if (connection != _connections[slot] || map != _mapGeneration || config != _configGeneration
                || !player.IsValid || player.Handle != handle || player.Connected != PlayerConnectedState.Connected)
                return;

            _dispatchValues.Clear();
            var processedMessage = ReplaceMessageTags(welcomeMessage.Message, player);
            switch (welcomeMessage.MessageType)
            {
                case MessageType.Chat:
                    player.PrintToChat(processedMessage);
                    break;
                case MessageType.Center:
                    player.PrintToCenter(processedMessage);
                    break;
                case MessageType.CenterHtml:
                    SetHtmlPrintSettings(player, processedMessage);
                    break;
            }
        });
    }

    private void PrintToPlayer(CCSPlayerController player, HudDestination destination, string message)
    {
        var processedMessage = ReplaceMessageTags(message, player);
        if (destination == HudDestination.Chat)
        {
            player.PrintToChat($" {processedMessage}");
        }
        else
        {
            if (Config.PrintToCenterHtml != null && Config.PrintToCenterHtml.Value)
                SetHtmlPrintSettings(player, processedMessage);
            else
                player.PrintToCenter(processedMessage);
        }
    }

    private void SetHtmlPrintSettings(CCSPlayerController player, string message)
    {
        if ((uint)player.Slot >= _users.Length) return;
        _controllers[player.Slot] = player.Handle;
        _activeHtml.Add(player.Slot);
        var user = _users[player.Slot] ??= new User();

        user.HtmlPrint = true;
        user.PrintTime = 0;
        user.Message = message;
    }

    private string ReplaceMessageTags(string message, CCSPlayerController? player, bool applyColorTags = true, bool panorama = false)
    {
        if (player != null && player.IsValid)
            message = LocalizeMessage(message, player);

        string Value(string tag, Func<string> resolve)
        {
            if (!_dispatchValues.TryGetValue(tag, out var value))
                _dispatchValues[tag] = value = resolve();
            return value;
        }
        string Replace(string text, string tag, Func<string> resolve) =>
            text.Contains(tag, StringComparison.Ordinal) ? text.Replace(tag, Value(tag, resolve)) : text;
        var replacedMessage = Replace(message, "{MAP}", () =>
        {
            var name = Server.MapName;
            return Config.MapsName != null && Config.MapsName.TryGetValue(name, out var display) ? display : name;
        });
        replacedMessage = Replace(replacedMessage, "{TIME}", () => DateTime.Now.ToString("HH:mm:ss"));
        replacedMessage = Replace(replacedMessage, "{DATE}", () => DateTime.Now.ToString("dd.MM.yyyy"));
        replacedMessage = Replace(replacedMessage, "{SERVERNAME}", () => ConVar.Find("hostname")!.StringValue);
        replacedMessage = Replace(replacedMessage, "{IP}", () => ConVar.Find("ip")!.StringValue);
        replacedMessage = Replace(replacedMessage, "{PORT}", () => ConVar.Find("hostport")!.GetPrimitiveValue<int>().ToString());
        replacedMessage = Replace(replacedMessage, "{MAXPLAYERS}", () => Server.MaxPlayers.ToString());
        replacedMessage = Replace(replacedMessage, "{PLAYERS}", () => Utilities.GetPlayers().Count(u => u.PlayerPawn.Value != null && u.PlayerPawn.Value.IsValid).ToString());
        replacedMessage = replacedMessage.Replace("\n", panorama ? " " : "\u2029");

        if (applyColorTags)
            replacedMessage = replacedMessage.ReplaceColorTags();

        if (player != null && player.IsValid)
            replacedMessage = replacedMessage.Replace("{PLAYERNAME}", player.PlayerName);

        return replacedMessage;
    }

    private string LocalizeMessage(string message, CCSPlayerController player)
    {
        var language = player.GetLanguage();
        var key = (message, language.Name);
        if (_localized.TryGetValue(key, out var cached)) return cached;
        using var culture = new WithTemporaryCulture(language);
        var result = TranslationTags.Replace(
            message,
            match =>
            {
                try
                {
                    var localized = Localizer[match.Groups[1].Value];
                    return localized.ResourceNotFound ? match.Value : localized.Value;
                }
                catch (Exception exception)
                {
                    Console.WriteLine($"[Advertisement] Failed to localize '{{{match.Groups[1].Value}}}': {exception.Message}");
                    return match.Value;
                }
            });
        if (_localized.Count < 4096) _localized[key] = result;
        return result;
    }
}

public class Config : IBasePluginConfig
{
    public bool? PrintToCenterHtml { get; init; } = false;
    public float? HtmlCenterDuration { get; set; } = 5;
    public bool? ShowHtmlWhenDead { get; set; }
    public bool PanoramaEnabled { get; set; }
    public float PanoramaInterval { get; set; } = 60;
    public float PanoramaDuration { get; set; } = 2;
    public List<string> PanoramaMessages { get; set; } =
    [
        "{GREEN}Welcome to {DEFAULT}{SERVERNAME}!",
        "{DEFAULT}Visit our Discord: {BLUE}discord.gg/example"
    ];

    public WelcomeMessage? WelcomeMessage { get; set; } = new WelcomeMessage
    {
        //0 - CHAT | 1 - CENTER | 2 - CENTER HTML
        MessageType = 0,
        Message = "Welcome, {BLUE}{PLAYERNAME}",
        DisplayDelay = 5
    };

    public List<Advertisement> Ads { get; set; } =
    [
        new()
        {
            Interval = 35,
            Messages =
            [
                new()
                {
                    Chat = "Section 1 Chat 1",
                    Center = "Section 1 Center 1"
                },
                new()
                {
                    Chat = "{current_map} {BLUE}{MAP}"
                },
                new()
                {
                    Chat = "{current_time} {RED}{TIME}"
                }
            ]
        },
    ];

    public List<AdCommand> Commands { get; set; } = [];

    public List<string>? Panel { get; init; }

    public Dictionary<string, string>? MapsName { get; init; } = new()
    {
        ["de_mirage"] = "Mirage",
        ["de_dust"] = "Dust II"
    };

    public int Version { get; set; } = 1;
}

public enum MessageType
{
    Chat = 0,
    Center,
    CenterHtml
}

public class WelcomeMessage
{
    public MessageType MessageType { get; init; }
    public required string Message { get; init; }
    public float DisplayDelay { get; set; } = 2;
}
