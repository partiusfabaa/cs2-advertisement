using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace Advertisement;

public class Advertisement : BasePlugin
{
    public override string ModuleName => "Advertisement by thesamefabius";
    public override string ModuleVersion => "v1.0.0";

    private Advert _config = null!;

    private int _chatCount;
    private int _centerCount;
    private int _panelCount;
    private Timer _timer = null!;

    public override void Load(bool hotReload)
    {
        _config = LoadConfig();
        RegisterEventHandler<EventCsWinPanelRound>(EventCsWinPanelRound);

        _timer = AddTimer(_config.Delay, TimerAdvertisement, TimerFlags.REPEAT);
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

        _timer.Kill();
        _timer = AddTimer(_config.Delay, TimerAdvertisement, TimerFlags.REPEAT);

        const string msg = "\x08[\x0C Advertisement \x08] configuration successfully rebooted!";

        if (controller == null)
            Console.WriteLine(msg);
        else
            controller.PrintToChat(msg);
    }

    private void TimerAdvertisement()
    {
        var chat = _config.MessageSections.Chat;
        var center = _config.MessageSections.Center;

        if (chat.Count != 0)
        {
            if (_chatCount >= chat.Count) _chatCount = 0;

            PrintWrappedLine(HudDestination.Chat, chat[_chatCount]);
            _chatCount++;
        }

        if (center.Count != 0)
        {
            if (_centerCount >= center.Count) _centerCount = 0;

            var centerMsg = ReplaceMessageTags(center[_centerCount]);
            VirtualFunctions.ClientPrintAll(HudDestination.Center, centerMsg, 0, 0, 0, 0);
            _centerCount++;
        }
    }

    private void PrintWrappedLine(HudDestination destination, string message)
    {
        var parts = ReplaceMessageTags(message).Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        foreach (var part in parts)
            VirtualFunctions.ClientPrintAll(destination, $" {part}", 0, 0, 0, 0);
    }


    private HookResult EventCsWinPanelRound(EventCsWinPanelRound handle, GameEventInfo info)
    {
        var panel = _config.MessageSections.Panel;

        if (panel.Count == 0) return HookResult.Continue;

        if (_panelCount >= panel.Count) _panelCount = 0;

        handle.FunfactToken = panel[_panelCount];
        handle.TimerTime = 5;
        _panelCount++;

        return HookResult.Continue;
    }

    private string ReplaceMessageTags(string message)
    {
        var replacedMessage = message
            .Replace("{MAP}", NativeAPI.GetMapName())
            .Replace("{TIME}", DateTime.Now.ToString("HH:mm:ss"))
            .Replace("{DATE}", DateTime.Now.ToString("dd.MM.yyyy"));

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

    private Advert LoadConfig()
    {
        var configPath = Path.Combine(ModuleDirectory, "advertisement.json");

        if (!File.Exists(configPath)) return CreateConfig(configPath);

        var config = JsonSerializer.Deserialize<Advert>(File.ReadAllText(configPath))!;

        var messages = config.MessageSections;

        for (var i = 0; i < messages.Chat.Count; i++)
        {
            messages.Chat[i] = ReplaceColorTags(messages.Chat[i]);
        }

        for (var i = 0; i < messages.Center.Count; i++)
        {
            messages.Center[i] = ReplaceColorTags(messages.Center[i]);
        }

        for (var i = 0; i < messages.Panel.Count; i++)
        {
            messages.Panel[i] = ReplaceColorTags(messages.Panel[i]);
        }

        return config;
    }

    private Advert CreateConfig(string configPath)
    {
        var config = new Advert
        {
            Admins = new List<ulong> { 76543199045778423 },
            Delay = 40.0f,
            MessageSections = new MessageSections
            {
                Chat = new List<string>
                    { "Chat Advertising 1", "Chat Advertising 2", "Chat Advertising 3" },
                Center = new List<string>
                    { "Center Advertising 1", "Center Advertising 2", "Center Advertising 3" },
                Panel = new List<string>
                    { "Panel Advertising 1", "Panel Advertising 2", "Panel Advertising 3" },
            }
        };

        File.WriteAllText(configPath, JsonSerializer.Serialize(config));

        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine("[Advertisement] The configuration was successfully saved to a file: " + configPath);
        Console.ResetColor();

        return config;
    }
}

public class Advert
{
    public required List<ulong> Admins { get; set; }
    public float Delay { get; set; }
    public required MessageSections MessageSections { get; set; }
}

public class MessageSections
{
    public List<string> Chat { get; init; } = null!;
    public List<string> Center { get; init; } = null!;
    public List<string> Panel { get; init; } = null!;
}