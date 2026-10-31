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
    public override string ModuleAuthor => "thesamefabius";
    public override string ModuleName => "Advertisement";
    public override string ModuleVersion => $"v{typeof(Ads).Assembly.GetName().Version!.ToString(3)}";

    private readonly List<Timer> _timers = [];
    private readonly List<Timer> _panoramaAnimationTimers = [];
    private readonly Dictionary<string, CommandInfo.CommandCallback> _adCommands = [];
    private readonly long[] _panoramaGenerations = new long[66];
    private PanelHandle? _panoramaLayout;
    private bool _panoramaInitialized;
    private int _panoramaMessageIndex;

    public Config Config { get; set; } = new();
    private readonly User?[] _users = new User?[66];

    public void OnConfigParsed(Config config)
    {
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

        Config = config;
    }

    public override void Load(bool hotReload)
    {
        RegisterEventHandler<EventPlayerConnectFull>(EventPlayerConnectFull);
        RegisterListener<Listeners.OnClientAuthorized>(OnClientAuthorized);
        RegisterListener<Listeners.OnTick>(OnTick);

        StartTimers();
        RegisterAdCommands();

        if (hotReload)
        {
            foreach (var player in Utilities.GetPlayers())
            {
                _users[player.Slot] = new User();
            }
        }
    }

    public override void Unload(bool hotReload)
    {
        InvalidatePanoramaMessages();
        StopPanoramaAnimationTimers();
        foreach (var timer in _timers)
            timer.Kill();
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

                if (_adCommands.TryAdd(name, callback))
                    AddCommand(name, "Shows the advertisement", callback);
            }
        }
    }

    private void OnClientAuthorized(int slot, SteamID id)
    {
        if (slot < 0 || slot >= _users.Length)
            return;

        _users[slot] = new User();
        _panoramaGenerations[slot]++;
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
        var durationTicks = Math.Ceiling((double)(Config.HtmlCenterDuration ?? 5) / Server.TickInterval);
        foreach (var player in Utilities.GetPlayers())
        {
            var user = _users[player.Slot];
            if (user is null || !user.HtmlPrint)
                continue;

            if (Config.ShowHtmlWhenDead == true && player.PawnIsAlive)
                continue;

            if (user.PrintTime++ >= durationTicks)
            {
                user.HtmlPrint = false;
                continue;
            }

            player.PrintToCenterHtml(user.Message);
        }
    }

    private void ShowAd(Advertisement ad)
    {
        var players = Utilities.GetPlayers().Where(u => u is { IsBot: false, IsValid: true }).ToList();
        ShowMessage(ad.NextMessage, players);
    }

    private void ShowMessage(AdMessage message, List<CCSPlayerController> players)
    {
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
        foreach (var ad in Config.Ads.Where(ad => ad.Messages.Count > 0 && ad.Interval > 0))
        {
            _timers.Add(AddTimer(ad.Interval, () => ShowAd(ad), TimerFlags.REPEAT));
        }

        EnsurePanoramaLayout();
        if (Config.PanoramaEnabled && Config.PanoramaMessages.Count > 0)
            _timers.Add(AddTimer(Config.PanoramaInterval, ShowPanoramaMessage, TimerFlags.REPEAT));
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

        _panoramaLayout ??= Panorama.Spawn(
            "panorama/layout/custom_game/advertisement_panorama.vxml_c",
            new LayoutContract
            {
                RootPanelId = "AdvertisementRoot",
                CaptureInput = false
            });
    }

    private void ShowPanoramaMessage()
    {
        if (_panoramaLayout is null || Config.PanoramaMessages.Count == 0)
            return;

        StopPanoramaAnimationTimers();
        var message = Config.PanoramaMessages[_panoramaMessageIndex];
        _panoramaMessageIndex = (_panoramaMessageIndex + 1) % Config.PanoramaMessages.Count;
        var recipients = new List<(CCSPlayerController Player, long Generation)>();
        foreach (var player in Utilities.GetPlayers().Where(player => player is { IsValid: true, IsBot: false }))
        {
            if (player.Slot < 0 || player.Slot >= _panoramaGenerations.Length)
                continue;

            var generation = ++_panoramaGenerations[player.Slot];
            _panoramaLayout.Close(player);
            _panoramaLayout.Open(player);
            var runs = PanoramaText.Parse(ReplaceMessageTags(message, player, applyColorTags: false, panorama: true));
            if (runs.Count > PanoramaText.MaxRuns)
            {
                Console.WriteLine($"[Advertisement] Panorama message exceeds {PanoramaText.MaxRuns} color segments; displaying the full message in the default color.");
                runs = [new PanoramaTextRun(string.Concat(runs.Select(run => run.Text)), "default")];
            }

            for (var index = 0; index < PanoramaText.MaxRuns; index++)
            {
                var panelId = $"AdvertisementText{index}";
                var active = index < runs.Count;
                _panoramaLayout.SetVariableFor(player, $"message{index}", active ? runs[index].Text : string.Empty);
                _panoramaLayout.SetClassFor(player, panelId, "hidden", !active);
                if (active)
                    _panoramaLayout.SetClassFor(player, panelId, $"color-{runs[index].Color}", true);
            }
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
        SchedulePanoramaAnimation(0.95f + Config.PanoramaDuration, () =>
        {
            SetPanoramaClasses(recipients, exit: false);
            SetPanoramaClasses(recipients, show: false);
            foreach (var (player, generation) in recipients)
            {
                if (player.IsValid && _panoramaGenerations[player.Slot] == generation)
                    _panoramaLayout?.Close(player);
            }
        });
    }

    private void SchedulePanoramaAnimation(float delay, Action callback)
    {
        Timer? timer = null;
        timer = AddTimer(delay, () =>
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
            if (enter.HasValue)
                _panoramaLayout.SetClassFor(player, "AdvertisementCard", "enter", enter.Value);
            if (exit.HasValue)
                _panoramaLayout.SetClassFor(player, "AdvertisementCard", "exit", exit.Value);
        }
    }

    private void StopPanoramaAnimationTimers()
    {
        foreach (var timer in _panoramaAnimationTimers)
            timer.Kill();
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
            _panoramaGenerations[slot]++;
    }

    [RequiresPermissions("@css/root")]
    [ConsoleCommand("css_advert_reload", "configuration restart")]
    public void ReloadAdvertConfig(CCSPlayerController? controller, CommandInfo command)
    {
        InvalidatePanoramaMessages();
        StopPanoramaAnimationTimers();
        HidePanoramaCards();
        OnConfigParsed(ConfigManager.Load<Config>(Path.GetFileName(ModuleDirectory)));
        _panoramaMessageIndex = 0;

        foreach (var timer in _timers) timer.Kill();
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
        if (player.IsBot)
            return;

        AddTimer(welcomeMessage.DisplayDelay, () =>
        {
            if (!player.IsValid)
                return;

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
        var user = _users[player.Slot] ??= new User();

        user.HtmlPrint = true;
        user.PrintTime = 0;
        user.Message = message;
    }

    private string ReplaceMessageTags(string message, CCSPlayerController? player, bool applyColorTags = true, bool panorama = false)
    {
        if (player != null && player.IsValid)
            message = LocalizeMessage(message, player);

        var mapName = NativeAPI.GetMapName();
        if (Config.MapsName != null && Config.MapsName.TryGetValue(mapName, out var displayMapName))
            mapName = displayMapName;

        var replacedMessage = message
            .Replace("{MAP}", mapName)
            .Replace("{TIME}", DateTime.Now.ToString("HH:mm:ss"))
            .Replace("{DATE}", DateTime.Now.ToString("dd.MM.yyyy"))
            .Replace("{SERVERNAME}", ConVar.Find("hostname")!.StringValue)
            .Replace("{IP}", ConVar.Find("ip")!.StringValue)
            .Replace("{PORT}", ConVar.Find("hostport")!.GetPrimitiveValue<int>().ToString())
            .Replace("{MAXPLAYERS}", Server.MaxPlayers.ToString())
            .Replace("{PLAYERS}",
                Utilities.GetPlayers().Count(u => u.PlayerPawn.Value != null && u.PlayerPawn.Value.IsValid).ToString())
            .Replace("\n", panorama ? " " : "\u2029");

        if (applyColorTags)
            replacedMessage = replacedMessage.ReplaceColorTags();

        if (player != null && player.IsValid)
            replacedMessage = replacedMessage.Replace("{PLAYERNAME}", player.PlayerName);

        return replacedMessage;
    }

    private string LocalizeMessage(string message, CCSPlayerController player)
    {
        using var culture = new WithTemporaryCulture(player.GetLanguage());

        return Regex.Replace(
            message,
            @"\{([^{}]+)\}",
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

    public WelcomeMessage? WelcomeMessage { get; init; } = new WelcomeMessage
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

public class Advertisement
{
    public float Interval { get; init; }
    public List<AdMessage> Messages { get; set; } = [];
    private int _currentMessageIndex;
    [JsonIgnore] public AdMessage NextMessage => Messages[_currentMessageIndex++ % Messages.Count];
}

public class AdMessage
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Chat { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Center { get; init; }
}

public class AdCommand : AdMessage
{
    [JsonPropertyOrder(-1)]
    public List<string> Names { get; set; } = [];
}