using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace Advertisement;

public class Ads : BasePlugin
{
    public override string ModuleName => "Advertisement by thesamefabius";
    public override string ModuleVersion => "v1.0.1";

    private int _panelCount;
    private Config _config = null!;
    private readonly List<Timer> _timers = new();

    public override void Load(bool hotReload)
    {
        _config = LoadConfig();
        RegisterEventHandler<EventCsWinPanelRound>(EventCsWinPanelRound);
        StartTimers();
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

    public void StartTimers()
    {
        foreach (var ad in _config.Ads)
        {
            _timers.Add(AddTimer(ad.Interval, () => ShowAd(ad), TimerFlags.REPEAT));
        }
    }

    private HookResult EventCsWinPanelRound(EventCsWinPanelRound handle, GameEventInfo info)
    {
        var panel = _config.Panel;

        if (panel.Count == 0) return HookResult.Continue;

        if (_panelCount >= panel.Count) _panelCount = 0;

        handle.FunfactToken = panel[_panelCount];
        handle.TimerTime = 5;
        _panelCount++;

        return HookResult.Continue;
    }

    [ConsoleCommand("css_advert_reload", "configuration restart")]
    public void ReloadAdvertConfig(CCSPlayerController? controller, CommandInfo command)
    {
        if (controller != null)
        {
            if (!_config.Admins.Contains(controller.SteamID))
            {
                controller.PrintToChat("\x08[\x0C Advertisement \x08] you do not have access to this command");
                return;
            }
        }
            
        _config = LoadConfig();

        foreach (var timer in _timers) timer.Kill();
        _timers.Clear();
        StartTimers();
            
        const string msg = "\x08[\x0C Advertisement \x08] configuration successfully rebooted!";

        if (controller == null)
            Console.WriteLine(msg);
        else
            controller.PrintToChat(msg);
    }

    private void PrintWrappedLine(HudDestination destination, string message)
    {
        message = ReplaceMessageTags(message);

        if (destination != HudDestination.Center)
        {
            var parts = message.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            foreach (var part in parts)
                VirtualFunctions.ClientPrintAll(destination, $" {part}", 0, 0, 0, 0);
        }
        else
            VirtualFunctions.ClientPrintAll(destination, $" {message}", 0, 0, 0, 0);
    }

    private string ReplaceMessageTags(string message)
    {
        var replacedMessage = message
            .Replace("{MAP}", NativeAPI.GetMapName())
            .Replace("{TIME}", DateTime.Now.ToString("HH:mm:ss"))
            .Replace("{DATE}", DateTime.Now.ToString("dd.MM.yyyy"))
            .Replace("{N}", "\n");

        replacedMessage = ReplaceColorTags(replacedMessage);

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

        for (var i = 0; i < colorPatterns.Length; i++)
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
            Admins = new List<ulong>
            {
                76561199096378663
            },
            Ads = new List<Advertisement>
            {
                new()
                {
                    Interval = 5,
                    Messages = new List<Dictionary<string, string>>()
                    {
                        new()
                        {
                            ["Chat"] = "Section 1 Chat 1",
                            ["Center"] = "Section 1 Center 1"
                        },
                        new()
                        {
                            ["Chat"] = "Section 1 Chat 2"
                        },
                    }
                },
                new()
                {
                    Interval = 10,
                    Messages = new List<Dictionary<string, string>>()
                    {
                        new()
                        {
                            ["Chat"] = "Section 2 Chat 1"
                        },
                        new()
                        {
                            ["Chat"] = "Section 2 Chat 2",
                            ["Center"] = "Section 2 Center 1"
                        },
                    }
                }
            },
            Panel = new List<string> { "Panel Advertising 1", "Panel Advertising 2", "Panel Advertising 3" }
        };

        File.WriteAllText(configPath, JsonSerializer.Serialize(config));

        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine("[Advertisement] The configuration was successfully saved to a file: " + configPath);
        Console.ResetColor();

        return config;
    }
}

public class Config
{
    public required List<ulong> Admins { get; set; }
    public List<Advertisement> Ads { get; set; }
    public List<string> Panel { get; set; }
}

public class Advertisement
{
    public float Interval { get; set; }
    public List<Dictionary<string, string>> Messages { get; set; }

    private int _currentMessageIndex;

    [JsonIgnore]
    public Dictionary<string, string> NextMessages => Messages[_currentMessageIndex++ % Messages.Count];
}