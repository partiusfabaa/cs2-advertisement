// Deterministic managed doubles, not a native/ABI or client-rendering emulator.
// Timer ownership, config path/JSON options and GetPlayers filtering follow CSS v1.0.378.
global using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;
using System.Globalization;
using System.Text.Json;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Timers;

namespace CounterStrikeSharp.API
{
    public static class Server
    {
        public static float TickInterval => 1f / 64;
        public static int MaxPlayers => 64;
        public static string MapName => "de_mirage";
    }
    public static class NativeAPI { public static string GetMapName() => Server.MapName; }
    public static class Utilities
    {
        public static readonly CCSPlayerController?[] Players = new CCSPlayerController?[64];
        public static int Lookups;
        public static CCSPlayerController? GetPlayerFromSlot(int slot) { Lookups++; return Players[slot]; }
        public static List<CCSPlayerController> GetPlayers()
        {
            var result = new List<CCSPlayerController>();
            for (var slot = 0; slot < Server.MaxPlayers; slot++)
                if (GetPlayerFromSlot(slot) is { IsValid: true, Connected: PlayerConnectedState.Connected } player)
                    result.Add(player);
            return result;
        }
    }
}
namespace CounterStrikeSharp.API.Core
{
    public enum PlayerConnectedState { Disconnected, Connected }
    public enum HookResult { Continue }
    public class GameEventInfo;
    public class EventPlayerConnectFull { public CCSPlayerController? Userid { get; init; } }
    public class Pawn { public bool IsValid { get; set; } = true; }
    public class PawnRef { public Pawn? Value { get; set; } = new(); }
    public class CCSPlayerController
    {
        public int Slot { get; init; }
        public nint Handle { get; init; }
        public bool IsValid { get; set; } = true;
        public bool IsBot { get; set; }
        public bool PawnIsAlive { get; set; }
        public PlayerConnectedState Connected { get; set; } = PlayerConnectedState.Connected;
        public PawnRef PlayerPawn { get; } = new();
        public string PlayerName { get; set; } = "Player";
        public CultureInfo Language { get; set; } = CultureInfo.GetCultureInfo("en");
        public int HtmlSends, ChatSends, CenterSends;
        public string LastText = "";
        public void PrintToCenterHtml(string text) { HtmlSends++; LastText = text; }
        public void PrintToChat(string text) { ChatSends++; LastText = text; }
        public void PrintToCenter(string text) { CenterSends++; LastText = text; }
    }
    public interface IPluginConfig<T>;
    public static class Listeners
    {
        public delegate void OnTick();
        public delegate void OnClientAuthorized(int slot, SteamID id);
        public delegate void OnClientDisconnect(int slot);
        public delegate void OnMapEnd();
    }
    public record LocalizedText(string Value, bool ResourceNotFound);
    public class TestLocalizer
    {
        public int Reads;
        public LocalizedText this[string key]
        {
            get
            {
                Reads++;
                return key == "hello" ? new(CultureInfo.CurrentUICulture.Name == "ru" ? "Привет" : "Hello", false)
                    : new(key, true);
            }
        }
    }
    public abstract class BasePlugin
    {
        public abstract string ModuleName { get; }
        public abstract string ModuleVersion { get; }
        public virtual string ModuleAuthor => "";
        public string ModuleDirectory { get; set; } = "Advertisement";
        public TestLocalizer Localizer { get; } = new();
        public readonly List<Timer> Timers = [];
        public readonly Dictionary<Type, Delegate> Hooks = [];
        public readonly Dictionary<string, CommandInfo.CommandCallback> Commands = new(StringComparer.OrdinalIgnoreCase);
        public virtual void Load(bool hotReload) { }
        public virtual void Unload(bool hotReload) { }
        public void RegisterEventHandler<T>(Func<T, GameEventInfo, HookResult> callback) => Hooks[typeof(T)] = callback;
        public void RegisterListener<T>(T callback) where T : Delegate => Hooks[typeof(T)] = callback;
        public void AddCommand(string name, string description, CommandInfo.CommandCallback callback) => Commands.Add(name, callback);
        public void RemoveCommand(string name, CommandInfo.CommandCallback callback) => Commands.Remove(name);
        public Timer AddTimer(float interval, Action callback, TimerFlags flags = 0)
        {
            var timer = new Timer(interval, callback, flags);
            Timers.Add(timer);
            return timer;
        }
    }
}
namespace CounterStrikeSharp.API.Core.Attributes.Registration
{
    public class ConsoleCommandAttribute(string command, string description) : Attribute
    {
        public string Command => command;
        public string Description => description;
    }
}
namespace CounterStrikeSharp.API.Modules.Admin
{
    public class RequiresPermissionsAttribute(string permission) : Attribute { public string Permission => permission; }
}
namespace CounterStrikeSharp.API.Modules.Commands
{
    public class CommandInfo { public delegate void CommandCallback(CCSPlayerController? player, CommandInfo command); }
}
namespace CounterStrikeSharp.API.Modules.Entities { public class SteamID; }
namespace CounterStrikeSharp.API.Modules.Utils { public enum HudDestination { Chat, Center } }
namespace CounterStrikeSharp.API.Core.Translations
{
    public static class TranslationExtensions
    {
        public static CultureInfo GetLanguage(this CCSPlayerController player) => player.Language;
        public static string ReplaceColorTags(this string text) => text.Replace("{RED}", "\x07").Replace("{DEFAULT}", "\x01");
    }
    public sealed class WithTemporaryCulture : IDisposable
    {
        private readonly CultureInfo _before = CultureInfo.CurrentUICulture;
        public WithTemporaryCulture(CultureInfo culture) => CultureInfo.CurrentUICulture = culture;
        public void Dispose() => CultureInfo.CurrentUICulture = _before;
    }
}
namespace CounterStrikeSharp.API.Modules.Cvars
{
    public class ConVar(string name)
    {
        public static int Reads;
        public static ConVar Find(string name) { Reads++; return new(name); }
        public string StringValue => name == "hostname" ? "Test Server" : "127.0.0.1";
        public T GetPrimitiveValue<T>() => (T)(object)27015;
    }
}
namespace CounterStrikeSharp.API.Modules.Config
{
    public interface IBasePluginConfig;
    public static class ConfigManager
    {
        public static T Load<T>(string name) where T : class, IBasePluginConfig, new()
        {
            var path = Path.Combine(Path.GetDirectoryName(typeof(BasePlugin).Assembly.Location)!, "..", "configs", "plugins", name, name + ".json");
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip })!;
        }
    }
}
namespace CounterStrikeSharp.API.Modules.Timers
{
    [Flags] public enum TimerFlags { REPEAT = 1, STOP_ON_MAPCHANGE = 2 }
    public class Timer(float interval, Action callback, TimerFlags flags)
    {
        public float Interval => interval;
        public bool Cancelled { get; private set; }
        public bool Completed { get; private set; }
        public void Kill() => Cancelled = true;
        public void Fire()
        {
            if (Cancelled || Completed) return;
            callback();
            if (!flags.HasFlag(TimerFlags.REPEAT)) Completed = true;
        }
    }
}
namespace PanoramaManager
{
    public enum PanelAction { Restored }
    public record PanelEvent(PanelAction Action, CCSPlayerController Player);
    public class LayoutContract
    {
        public string RootPanelId { get; set; } = "";
        public int RowCount { get; set; }
        public bool CaptureInput { get; set; }
    }
    public static class Panorama
    {
        public static PanelHandle? Last;
        public static void Init(BasePlugin plugin) { }
        public static void Shutdown() { }
        public static PanelHandle Spawn(string path, LayoutContract contract) => Last = new(contract);
    }
    public class PanelHandle(LayoutContract contract) : IDisposable
    {
        public LayoutContract Contract => contract;
        public event Action<PanelEvent>? OnEvent;
        public readonly Dictionary<(int, string), string> Text = [];
        public readonly Dictionary<(int, string, string), bool> Classes = [];
        public void SetVariableFor(CCSPlayerController p, string key, string text) => Text[(p.Slot, key)] = text;
        public void SetClassFor(CCSPlayerController p, string panel, string name, bool value) => Classes[(p.Slot, panel, name)] = value;
        public void Open(CCSPlayerController p) { }
        public void Close(CCSPlayerController p) { }
        public void Dispose() { }
        public void Restore(CCSPlayerController p) { Text.Clear(); Classes.Clear(); OnEvent?.Invoke(new(PanelAction.Restored, p)); }
    }
}
