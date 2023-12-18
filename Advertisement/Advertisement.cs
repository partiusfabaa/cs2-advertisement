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
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using MaxMind.GeoIP2;

namespace Advertisement;

public class Ads : BasePlugin
{
    public override string ModuleAuthor => "thesamefabius";
    public override string ModuleName => "Advertisement";
    public override string ModuleVersion => "v1.0.6";

    private int _panelCount;
    private Config _config = null!;
    private readonly List<Timer> _timers = new();
    private readonly Dictionary<ulong, string> _playerIsoCode = new();

    public override void Load(bool hotReload)
    {
        _config = LoadConfig();

        RegisterListener<Listeners.OnClientAuthorized>((slot, id) =>
        {
            var player = Utilities.GetPlayerFromSlot(slot);

            if (player.IpAddress != null)
                _playerIsoCode.Add(id.SteamId64, GetPlayerIsoCode(player.IpAddress.Split(':')[0]));
        });

        RegisterEventHandler<EventCsWinPanelRound>(EventCsWinPanelRound, HookMode.Pre);
        RegisterEventHandler<EventPlayerConnectFull>(EventPlayerConnectFull);
        RegisterEventHandler<EventPlayerDisconnect>((@event, _) =>
        {
            _playerIsoCode.Remove(@event.Userid.SteamID);

            return HookResult.Continue;
        });

        StartTimers();
    }

    private HookResult EventPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        if (_config.WelcomeMessage == null) return HookResult.Continue;

        var player = @event.Userid;

        if (!player.IsValid) return HookResult.Continue;

        var welcomeMsg = _config.WelcomeMessage;

        var msg = ReplaceColorTags(welcomeMsg.Message).Replace("{PLAYERNAME}", player.PlayerName);

        switch (welcomeMsg.MessageType)
        {
            case 0:
                // foreach (var s in WrappedLine(msg))
                //     player.PrintToChat($" {s}");
                PrintWrappedLine(HudDestination.Chat, msg, player.PlayerName, true);
                return HookResult.Continue;
            case 1:
                PrintWrappedLine(HudDestination.Center, msg, player.PlayerName, true);
                return HookResult.Continue;
        }

        return HookResult.Continue;
    }

    private void ShowAd(Advertisement ad)
    {
        var messages = ad.NextMessages;

        foreach (var (type, message) in messages)
        {
            switch (type)
            {
                case "Chat":
                    PrintWrappedLine(HudDestination.Chat, message);
                    break;
                case "Center":
                    PrintWrappedLine(HudDestination.Center, message);
                    break;
            }
        }
    }

    private void StartTimers()
    {
        foreach (var ad in _config.Ads)
        {
            _timers.Add(AddTimer(ad.Interval, () => ShowAd(ad), TimerFlags.REPEAT));
        }
    }

    private HookResult EventCsWinPanelRound(EventCsWinPanelRound handle, GameEventInfo info)
    {
        if (_config.Panel == null) return HookResult.Continue;

        var panel = _config.Panel;

        if (panel.Count == 0) return HookResult.Continue;

        if (_panelCount >= panel.Count) _panelCount = 0;

        handle.FunfactToken = ReplaceMessageTags(panel[_panelCount]);
        handle.TimerTime = 5;
        _panelCount ++;

        return HookResult.Changed;
    }

    [RequiresPermissions("@css/root")]
    [ConsoleCommand("css_advert_reload", "configuration restart")]
    public void ReloadAdvertConfig(CCSPlayerController? controller, CommandInfo command)
    {
        _config = LoadConfig();

        foreach (var timer in _timers) timer.Kill();
        _timers.Clear();
        StartTimers();

        foreach (var player in Utilities.GetPlayers())
        {
            if (player.IpAddress == null || player.AuthorizedSteamID == null) continue;
            _playerIsoCode.TryAdd(player.AuthorizedSteamID.SteamId64,
                GetPlayerIsoCode(player.IpAddress.Split(':')[0]));
        }

        const string msg = "\x08[\x0C Advertisement \x08] configuration successfully rebooted!";

        if (controller == null)
            Console.WriteLine(msg);
        else
            controller.PrintToChat(msg);
    }

    private void PrintWrappedLine(HudDestination destination, string message, string? playerName = null,
        bool isWelcome = false)
    {
        foreach (var player in Utilities.GetPlayers().Where(u => u.IpAddress != null))
        {
            var matches = Regex.Matches(message, @"\{([^}]*)\}");

            var msg = message;

            if (_config.LanguageMessages != null)
            {
                foreach (Match match in matches)
                {
                    var tag = match.Groups[0].Value;
                    var tagName = match.Groups[1].Value;

                    if (!_config.LanguageMessages.TryGetValue(tagName, out var language)) continue;
                    var isoCode = _playerIsoCode.TryGetValue(player.SteamID, out var playerCountryIso)
                        ? playerCountryIso
                        : _config.DefaultLang;

                    if (isoCode != null && language.TryGetValue(isoCode, out var tagReplacement))
                        msg = msg.Replace(tag, tagReplacement);
                    else if (_config.DefaultLang != null && language.TryGetValue(_config.DefaultLang, out var defaultReplacement))
                        msg = msg.Replace(tag, defaultReplacement);
                }
            }

            msg = ReplaceMessageTags(msg);

            if (playerName != null)
                msg = msg.Replace("{PLAYERNAME}", playerName);

            if (destination != HudDestination.Center)
            {
                foreach (var part in WrappedLine(msg))
                {
                    if (isWelcome)
                    {
                        if (player.PlayerName == playerName)
                            player.PrintToChat($" {part}");
                    }
                    else
                        player.PrintToChat($" {part}");
                }
            }
            else
            {
                if (_config.PrintToCenterHtml != null && _config.PrintToCenterHtml.Value)
                    player.PrintToCenterHtml(msg);
                else
                    player.PrintToCenter(msg);
            }
        }
    }

    private string[] WrappedLine(string message)
    {
        return message.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
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
                Utilities.GetPlayers().Count(u => u.PlayerPawn.Value != null && u.PlayerPawn.Value.IsValid).ToString());

        replacedMessage = ReplaceColorTags(replacedMessage);

        if (_config.MapsName != null)
        {
            foreach (var mapsName in _config.MapsName)
            {
                if (mapName != mapsName.Key) continue;

                return replacedMessage.Replace(mapName, mapsName.Value);
            }
        }

        return replacedMessage;
    }

    private string ReplaceColorTags(string input)
    {
        string[] colorPatterns =
        {
            "{DEFAULT}", "{RED}", "{LIGHTPURPLE}", "{GREEN}", "{LIME}", "{LIGHTGREEN}", "{LIGHTRED}", "{GRAY}",
            "{LIGHTOLIVE}", "{OLIVE}", "{LIGHTBLUE}", "{BLUE}", "{PURPLE}", "{GRAYBLUE}"
        };
        string[] colorReplacements =
        {
            "\x01", "\x02", "\x03", "\x04", "\x05", "\x06", "\x07", "\x08", "\x09", "\x10", "\x0B", "\x0C", "\x0E",
            "\x0A"
        };

        for (var i = 0; i < colorPatterns.Length; i ++)
            input = input.Replace(colorPatterns[i], colorReplacements[i]);

        return input;
    }

    private Config LoadConfig()
    {
        var configPath = Path.Combine(ModuleDirectory, "advertisement.json");

        if (!File.Exists(configPath)) return CreateConfig(configPath);

        var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(configPath))!;

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
                Message = "Welcome, {BLUE}{PLAYERNAME}"
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
            Panel = new List<string> { "Panel Advertising 1", "Panel Advertising 2", "Panel Advertising 3" },
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
        if (_config.DefaultLang != null)
            defaultLang = _config.DefaultLang;

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
    public WelcomeMessage? WelcomeMessage { get; init; }
    public List<Advertisement> Ads { get; init; } = null!;
    public List<string>? Panel { get; init; }

    public string? DefaultLang { get; init; }
    public Dictionary<string, Dictionary<string, string>>? LanguageMessages { get; init; }
    public Dictionary<string, string>? MapsName { get; init; }
}

public class WelcomeMessage
{
    public int MessageType { get; init; }
    public required string Message { get; init; }
}

public class Advertisement
{
    public float Interval { get; init; }
    public List<Dictionary<string, string>> Messages { get; init; } = null!;

    private int _currentMessageIndex;

    [JsonIgnore] public Dictionary<string, string> NextMessages => Messages[_currentMessageIndex ++ % Messages.Count];
}