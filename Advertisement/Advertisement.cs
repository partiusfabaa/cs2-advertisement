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

namespace Advertisement;

public class User
{
    public bool HtmlPrint { get; set; }
    public string Message { get; set; } = string.Empty;
    public int PrintTime { get; set; }
}

public class Ads : BasePlugin, IPluginConfig<Config>
{
    public override string ModuleAuthor => "thesamefabius";
    public override string ModuleName => "Advertisement";
    public override string ModuleVersion => $"v{typeof(Ads).Assembly.GetName().Version!.ToString(3)}";

    private readonly List<Timer> _timers = [];
    private readonly Dictionary<string, CommandInfo.CommandCallback> _adCommands = [];

    public Config Config { get; set; } = new();
    private readonly User?[] _users = new User?[66];

    public void OnConfigParsed(Config config)
    {
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

    private void OnClientAuthorized(int slot, SteamID id) => _users[slot] = new User();

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
        var durationTicks = (Config.HtmlCenterDuration ?? 5) * 64;
        foreach (var player in Utilities.GetPlayers())
        {
            var user = _users[player.Slot];
            if (user is null || !user.HtmlPrint)
                continue;

            if (user.PrintTime++ >= durationTicks)
            {
                user.HtmlPrint = false;
                continue;
            }

            if (Config.ShowHtmlWhenDead == true && player.PawnIsAlive)
                continue;

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
    }

    [RequiresPermissions("@css/root")]
    [ConsoleCommand("css_advert_reload", "configuration restart")]
    public void ReloadAdvertConfig(CCSPlayerController? controller, CommandInfo command)
    {
        OnConfigParsed(ConfigManager.Load<Config>(Path.GetFileName(ModuleDirectory)));

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

    private string ReplaceMessageTags(string message, CCSPlayerController? player)
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
            .Replace("\n", "\u2029")
            .ReplaceColorTags();

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
                catch
                {
                    return match.Value;
                }
            });
    }
}

public class Config : IBasePluginConfig
{
    public bool? PrintToCenterHtml { get; init; } = false;
    public float? HtmlCenterDuration { get; init; } = 5;
    public bool? ShowHtmlWhenDead { get; set; }

    public WelcomeMessage? WelcomeMessage { get; init; } = new WelcomeMessage
    {
        //0 - CHAT | 1 - CENTER | 2 - CENTER HTML
        MessageType = 0,
        Message = "Welcome, {BLUE}{PLAYERNAME}",
        DisplayDelay = 5
    };

    public List<Advertisement> Ads { get; init; } =
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

    public List<AdCommand> Commands { get; init; } = [];

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
    public List<AdMessage> Messages { get; init; } = [];
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
    public List<string> Names { get; init; } = [];
}