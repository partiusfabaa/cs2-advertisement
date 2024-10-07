using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using MaxMind.GeoIP2;

namespace Advertisement;

public class User
{
    public bool HtmlPrint { get; set; }
    public string Message { get; set; } = string.Empty;
    public int PrintTime { get; set; }
}

public class Ads : BasePlugin
{
    public override string ModuleAuthor => "thesamefabius";
    public override string ModuleName => "Advertisement";
    public override string ModuleVersion => "v1.0.8";

    private readonly List<Timer> _timers = new();
    private readonly Dictionary<ulong, string> _playerIsoCode = new();

    public Config Config { get; set; }

    private readonly User?[] _users = new User?[66];

    public override void Load(bool hotReload)
    {
        Config = LoadConfig();
        Console.WriteLine(Config.Panel == null);

        RegisterEventHandler<EventPlayerConnectFull>(EventPlayerConnectFull);
        RegisterEventHandler<EventPlayerDisconnect>(EventPlayerDisconnect);

        RegisterListener<Listeners.OnClientAuthorized>(OnClientAuthorized);
        RegisterListener<Listeners.OnTick>(OnTick);

        StartTimers();

        if (hotReload)
        {
            foreach (var player in Utilities.GetPlayers())
            {
                _users[player.Slot] = new User();
            }
        }
    }

    private HookResult EventPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        if (Config.LanguageMessages == null) return HookResult.Continue;
        var player = @event.Userid;
        if (player is null) return HookResult.Continue;

        _playerIsoCode.Remove(player.SteamID);

        return HookResult.Continue;
    }

    private void OnClientAuthorized(int slot, SteamID id)
    {
        var player = Utilities.GetPlayerFromSlot(slot);
        _users[slot] = new User();

        if (Config.LanguageMessages == null) return;

        if (player is not null && player.IpAddress != null)
            _playerIsoCode.TryAdd(id.SteamId64, GetPlayerIsoCode(player.IpAddress.Split(':')[0]));
    }

    private HookResult EventPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        if (Config.WelcomeMessage == null) return HookResult.Continue;

        var player = @event.Userid;
        if (player is null || !player.IsValid || player.SteamID == null) return HookResult.Continue;

        var welcomeMsg = Config.WelcomeMessage;
        var msg = welcomeMsg.Message.Replace("{PLAYERNAME}", player.PlayerName).ReplaceColorTags();

        PrintWrappedLine(0, msg, player, true);

        return HookResult.Continue;
    }

    private void OnTick()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            var user = _users[player.Slot];
            var showWhenDead = Config.ShowHtmlWhenDead;
            if (user is not null && 
                user.HtmlPrint && 
                (showWhenDead is null || showWhenDead == false ||
                 (showWhenDead == true && !player.PawnIsAlive)))
            {
                var duration = Config.HtmlCenterDuration;
                if (duration != null && TimeSpan.FromSeconds(user.PrintTime / 64.0).Seconds < duration.Value)
                {
                    player.PrintToCenterHtml(user.Message);
                    user.PrintTime++;
                }
                else
                {
                    user.HtmlPrint = false;
                }
            }
        }
    }

    private void ShowAd(Advertisement ad)
    {
        var messages = ad.NextMessages;

        foreach (var (type, message) in messages)
        {
            switch (type)
            {
                case "Chat":
                    PrintWrappedLine(destination: HudDestination.Chat, message: message);
                    break;
                case "Center":
                    PrintWrappedLine(destination: HudDestination.Center, message: message);
                    break;
            }
        }
    }

    private void StartTimers()
    {
        foreach (var ad in Config.Ads)
        {
            _timers.Add(AddTimer(ad.Interval, () => ShowAd(ad), TimerFlags.REPEAT));
        }
    }

    [RequiresPermissions("@css/root")]
    [ConsoleCommand("css_advert_reload", "configuration restart")]
    public void ReloadAdvertConfig(CCSPlayerController? controller, CommandInfo command)
    {
        Config = LoadConfig();

        foreach (var timer in _timers) timer.Kill();
        _timers.Clear();
        StartTimers();

        if (Config.LanguageMessages != null)
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player.IpAddress == null || player.AuthorizedSteamID == null) continue;

                _playerIsoCode.TryAdd(player.AuthorizedSteamID.SteamId64,
                    GetPlayerIsoCode(player.IpAddress.Split(':')[0]));
            }
        }

        const string msg = "\x08[\x0C Advertisement \x08] configuration successfully rebooted!";

        if (controller == null)
            Console.WriteLine(msg);
        else
            controller.PrintToChat(msg);
    }

    private void PrintWrappedLine(HudDestination? destination, string message,
        CCSPlayerController? connectPlayer = null, bool isWelcome = false)
    {
        if (connectPlayer != null && !connectPlayer.IsBot && isWelcome)
        {
            var welcomeMessage = Config.WelcomeMessage;
            if (welcomeMessage is null) return;

            AddTimer(welcomeMessage.DisplayDelay, () =>
            {
                if (connectPlayer == null || !connectPlayer.IsValid || connectPlayer.SteamID == null) return;

                var processedMessage = ProcessMessage(message, connectPlayer.SteamID)
                    .Replace("{PLAYERNAME}", connectPlayer.PlayerName);

                switch (welcomeMessage.MessageType)
                {
                    case MessageType.Chat:
                        connectPlayer.PrintToChat(processedMessage);
                        break;
                    case MessageType.Center:
                        connectPlayer.PrintToChat(processedMessage);
                        break;
                    case MessageType.CenterHtml:
                        SetHtmlPrintSettings(connectPlayer, processedMessage);
                        break;
                }
            });
        }
        else
        {
            foreach (var player in Utilities.GetPlayers()
                         .Where(u => !isWelcome && !u.IsBot && u.IsValid && u.SteamID != null))
            {
                var processedMessage = ProcessMessage(message, player.SteamID);

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
        }
    }

    private void SetHtmlPrintSettings(CCSPlayerController player, string message)
    {
        var user = _users[player.Slot];
        if (user is null)
        {
            _users[player.Slot] = new User();
            return;
        }
        
        user.HtmlPrint = true;
        user.PrintTime = 0;
        user.Message = message;
    }

    private string ProcessMessage(string message, ulong steamId)
    {
        if (Config.LanguageMessages == null) return ReplaceMessageTags(message);

        var matches = Regex.Matches(message, @"\{([^}]*)\}");

        foreach (Match match in matches)
        {
            var tag = match.Groups[0].Value;
            var tagName = match.Groups[1].Value;

            if (!Config.LanguageMessages.TryGetValue(tagName, out var language)) continue;

            var isoCode = _playerIsoCode.TryGetValue(steamId, out var playerCountryIso)
                ? playerCountryIso
                : Config.DefaultLang;

            if (isoCode != null && language.TryGetValue(isoCode, out var tagReplacement))
                message = message.Replace(tag, tagReplacement);
            else if (Config.DefaultLang != null &&
                     language.TryGetValue(Config.DefaultLang, out var defaultReplacement))
                message = message.Replace(tag, defaultReplacement);
        }

        return ReplaceMessageTags(message);
    }

    private string ReplaceMessageTags(string message)
    {
        var mapName = NativeAPI.GetMapName();

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
            .Replace("\n", "\u2029");

        replacedMessage = replacedMessage.ReplaceColorTags();

        if (Config.MapsName != null)
        {
            foreach (var mapsName in Config.MapsName.Where(mapsName => mapName == mapsName.Key))
            {
                return replacedMessage.Replace(mapName, mapsName.Value);
            }
        }

        return replacedMessage;
    }

    private Config LoadConfig()
    {
        var directory = Path.Combine(Application.RootDirectory, "configs/plugins/Advertisement");
        Directory.CreateDirectory(directory);

        var configPath = Path.Combine(directory, "Advertisement.json");

        if (!File.Exists(configPath)) return CreateConfig(configPath);

        var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(configPath),
            new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip })!;

        return config;
    }

    private Config CreateConfig(string configPath)
    {
        var config = new Config
        {
            PrintToCenterHtml = false,
            WelcomeMessage = new WelcomeMessage
            {
                //0 - CHAT | 1 - CENTER | 2 - CENTER HTML
                MessageType = 0,
                Message = "Welcome, {BLUE}{PLAYERNAME}",
                DisplayDelay = 5
            },
            Ads = new List<Advertisement>
            {
                new()
                {
                    Interval = 35,
                    Messages = new List<Dictionary<string, string>>
                    {
                        new()
                        {
                            ["Chat"] = "{map_name}",
                            ["Center"] = "Section 1 Center 1"
                        },
                        new()
                        {
                            ["Chat"] = "{current_time}"
                        }
                    }
                },
                new()
                {
                    Interval = 40,
                    Messages = new List<Dictionary<string, string>>
                    {
                        new()
                        {
                            ["Chat"] = "Section 2 Chat 1"
                        },
                        new()
                        {
                            ["Chat"] = "Section 2 Chat 2",
                            ["Center"] = "Section 2 Center 1"
                        }
                    }
                }
            },
            //Panel = new List<string> { "Panel Advertising 1", "Panel Advertising 2", "Panel Advertising 3" },
            DefaultLang = "US",
            LanguageMessages = new Dictionary<string, Dictionary<string, string>>()
            {
                {
                    "map_name", new Dictionary<string, string>
                    {
                        ["RU"] = "Текущая карта: {MAP}",
                        ["US"] = "Current map: {MAP}",
                        ["CN"] = "{GRAY}当前地图: {RED}{MAP}"
                    }
                },
                {
                    "current_time", new Dictionary<string, string>
                    {
                        ["RU"] = "{GRAY}Текущее время: {RED}{TIME}",
                        ["US"] = "{GRAY}Current time: {RED}{TIME}",
                        ["CN"] = "{GRAY}当前时间: {RED}{TIME}"
                    }
                }
            },
            MapsName = new Dictionary<string, string>
            {
                ["de_mirage"] = "Mirage",
                ["de_dust"] = "Dust II"
            }
        };

        File.WriteAllText(configPath,
            JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine("[Advertisement] The configuration was successfully saved to a file: " + configPath);
        Console.ResetColor();

        return config;
    }

    private string GetPlayerIsoCode(string ip)
    {
        var defaultLang = string.Empty;
        if (Config.DefaultLang != null)
            defaultLang = Config.DefaultLang;

        if (ip == "127.0.0.1") return defaultLang;

        try
        {
            using var reader = new DatabaseReader(Path.Combine(ModuleDirectory, "GeoLite2-Country.mmdb"));

            var response = reader.Country(IPAddress.Parse(ip));

            return response.Country.IsoCode ?? defaultLang;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{ex}");
        }

        return defaultLang;
    }
}

public class Config
{
    public bool? PrintToCenterHtml { get; init; }
    public float? HtmlCenterDuration { get; init; }
    public bool? ShowHtmlWhenDead { get; set; }
    public WelcomeMessage? WelcomeMessage { get; init; }
    public List<Advertisement> Ads { get; init; }
    public List<string>? Panel { get; init; }
    public string? DefaultLang { get; init; }
    public Dictionary<string, Dictionary<string, string>>? LanguageMessages { get; init; }
    public Dictionary<string, string>? MapsName { get; init; }
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
    public List<Dictionary<string, string>> Messages { get; init; } = null!;

    private int _currentMessageIndex;

    [JsonIgnore] public Dictionary<string, string> NextMessages => Messages[_currentMessageIndex++ % Messages.Count];
}