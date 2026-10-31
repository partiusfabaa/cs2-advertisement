<div align="center">

# cs2-advertisement

A [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) plugin for CS2 that shows advertisements, a welcome message and server info in chat or in the center of the screen.

[![Release](https://img.shields.io/github/v/release/partiusfabaa/cs2-advertisement?style=flat-square)](https://github.com/partiusfabaa/cs2-advertisement/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/partiusfabaa/cs2-advertisement/total?style=flat-square)](https://github.com/partiusfabaa/cs2-advertisement/releases)
[![Build](https://img.shields.io/github/actions/workflow/status/partiusfabaa/cs2-advertisement/build.yml?branch=main&style=flat-square)](https://github.com/partiusfabaa/cs2-advertisement/actions/workflows/build.yml)

</div>

## Features

- Rotating ad groups, each on its own interval
- Chat, center and center HTML messages
- Optional animated Panorama message cards from a Workshop addon
- Welcome message when a player joins
- Chat commands that show an ad on request (`!discord`, `!rules`, …)
- Placeholders for the map, time, date, server name, IP, online players and more
- Chat color tags
- Per-player translations: each player sees the text in their language (`!lang`)
- Friendly map names (`de_dust2` → `Dust II`)
- Config reload without restarting the server

## Installation

1. Install [Metamod:Source](https://www.sourcemm.net/downloads.php/?branch=master) and [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp).
2. Download `Advertisement.zip` from the [latest release](https://github.com/partiusfabaa/cs2-advertisement/releases/latest).
3. Unpack it into `game/csgo/` on the server: the plugin goes to `addons/counterstrikesharp/plugins/Advertisement`.
4. Start the server. The config is created at `addons/counterstrikesharp/configs/plugins/Advertisement/Advertisement.json`.
5. To use Panorama cards, install and mount the [Advertisement Panorama addon on Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3830960508) (Workshop ID: `3830960508`) so clients can load its UI resources, then set `PanoramaEnabled` to `true`.

### Panorama Workshop addon

[Download / subscribe to Advertisement Panorama on Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3830960508).

The addon supplies the Panorama layout and styles; the Advertisement plugin supplies the messages, colors, interval and display duration. Subscribing alone does not mount the addon on your server: include it in your server's Workshop setup and ensure clients receive its resources. Without the addon, Panorama cards are unavailable, but regular chat and center HTML ads do not require it.

For custom layouts, download `Advertisement-Panorama-Workshop.zip` from the release and follow [the Workshop authoring instructions](Advertisement/workshop/README.md).

## Commands

| Command | Description | Flag |
|---|---|---|
| `css_advert_reload` | Reloads the config, restarts the ad timers and re-registers ad commands | `@css/root` |

Your own commands from the `Commands` section of the config are added on top of these, see [Ad commands](#ad-commands).

## Config

| Option | Default | Description |
|---|---|---|
| `PrintToCenterHtml` | `false` | `Center` ads are shown as HTML (`<font color='#ff0000'>`, `<br>`, …) |
| `HtmlCenterDuration` | `5` | How long an HTML center message stays on screen, in seconds |
| `ShowHtmlWhenDead` | `false` | `true` shows HTML center messages only to dead players |
| `PanoramaEnabled` | `false` | Enables Panorama ads; requires the Workshop addon on clients |
| `PanoramaInterval` | `60` | Seconds between Panorama messages (minimum `1`) |
| `PanoramaDuration` | `2` | Seconds the Panorama card stays centered before sliding away |
| `PanoramaMessages` | Two example messages | Panorama card messages, shown in rotation; use `[]` to disable the rotation |
| `WelcomeMessage` | | Message shown after a player joins. Remove it to turn it off |
| `WelcomeMessage.MessageType` | `0` | `0` chat, `1` center, `2` center HTML |
| `WelcomeMessage.DisplayDelay` | `5` | Delay in seconds after the player joins |
| `Ads` | | Ad groups. Each group shows its next message every `Interval` seconds |
| `Ads[].Messages` | | Messages of the group, shown in turn. A message has a `Chat` text, a `Center` text, or both |
| `Commands` | `[]` | Commands that show a message to the player who typed them, see [Ad commands](#ad-commands) |
| `MapsName` | | Friendly map names for `{MAP}` |

```json
{
  "PrintToCenterHtml": false,
  "HtmlCenterDuration": 5,
  "ShowHtmlWhenDead": false,
  "PanoramaEnabled": true,
  "PanoramaInterval": 60,
  "PanoramaDuration": 2,
  "PanoramaMessages": [
    "{GREEN}Welcome to {DEFAULT}{SERVERNAME}!",
    "{DEFAULT}Visit our Discord: {BLUE}discord.gg/example"
  ],
  "WelcomeMessage": {
    "MessageType": 0,
    "Message": "{RED}Welcome, {GREEN}{PLAYERNAME}",
    "DisplayDelay": 5
  },
  "Ads": [
    {
      "Interval": 35,
      "Messages": [
        {
          "Chat": "IP: {RED}{IP}:{PORT}",
          "Center": "Server name: {SERVERNAME}"
        },
        {
          "Chat": "{current_map} {BLUE}{MAP}"
        },
        {
          "Chat": "{current_time} {RED}{TIME}"
        }
      ]
    },
    {
      "Interval": 60,
      "Messages": [
        {
          "Chat": "{RED}Online: {GREEN}{PLAYERS}/{MAXPLAYERS}"
        }
      ]
    }
  ],
  "Commands": [
    {
      "Names": ["css_discord", "css_ds"],
      "Chat": "Our Discord: {BLUE}discord.gg"
    }
  ],
  "MapsName": {
    "de_mirage": "Mirage",
    "de_dust2": "Dust II"
  },
  "Version": 1
}
```

Panorama cards slide in from the left below the score HUD (140px from the top), stay centered for `PanoramaDuration`, then slide out to the right. Card width and height automatically fit the text with padding, up to 90% of the screen width; the font remains 36px. They reuse the normal placeholders and translations, and support the same color tags as chat: `"{RED}Important: {GREEN}Welcome, {DEFAULT}{PLAYERNAME}!"`. A tag changes the color until the next tag; `{DEFAULT}` resets it. Up to 16 differently colored text segments are supported. Messages exceeding that limit are displayed completely in the default color with a console warning. Keep messages short enough for a single card row. No HTML is needed or interpreted. The Workshop source files and publishing steps are in [Advertisement/workshop](Advertisement/workshop).

Newly generated configs include two sample Panorama messages, with `PanoramaEnabled` set to `false`. Existing config files are not overwritten: add the Panorama fields from the example above manually if they are missing, or replace an empty `PanoramaMessages` array with your messages, then run `css_advert_reload`. Enable Panorama only after installing the Workshop addon.

The plugin creates its HUD layout only when `PanoramaEnabled` is `true` and `PanoramaMessages` has at least one non-empty entry. Panorama support requires CounterStrikeSharp `1.0.374` or newer. If a client does not have the Workshop addon, that client will not see Panorama cards; regular chat and center ads continue to work. Build and publish the Workshop addon using [its README](Advertisement/workshop/README.md).

The plugin bundles PanoramaManager `0.4.4`, which uses CounterStrikeSharp's custom HUD API rather than its own signature scanning. When upgrading from the original Panorama integration, replace both `Advertisement.dll` and `PanoramaManager.dll` (and `Advertisement.deps.json`) in the plugin directory, then restart the server or reload the plugin. No `panoramamanager.json` is required by this version. Recompile and republish the Workshop addon when its layout files change.

### Ad commands

The `Commands` section holds messages that players call on request. When a player types one of the `Names`, the `Chat` and `Center` texts are shown only to that player. These messages are not part of the `Ads` rotation; to also show one on a timer, add it to `Ads` as well.

```json
"Commands": [
  {
    "Names": ["css_discord", "css_ds"],
    "Chat": "Our Discord: {BLUE}discord.gg"
  },
  {
    "Names": ["css_rules"],
    "Chat": "{RED}No cheats, no toxicity",
    "Center": "Read the rules on our site"
  }
]
```

- Start the name with `css_` so it works in chat: `css_discord` is `!discord` and `/discord`.
- Placeholders, translations and colors work the same as in `Ads`.
- If two entries use the same name, the first one wins.
- `css_advert_reload` picks up added, changed and removed commands.

### Placeholders

| Tag | Value |
|---|---|
| `{PLAYERNAME}` | Player name |
| `{MAP}` | Current map (or its name from `MapsName`) |
| `{TIME}` | Server time, `HH:mm:ss` |
| `{DATE}` | Current date, `dd.MM.yyyy` |
| `{SERVERNAME}` | Server name |
| `{IP}` | Server IP |
| `{PORT}` | Server port |
| `{PLAYERS}` | Players on the server |
| `{MAXPLAYERS}` | Server slots |
| `\n` | New line |

### Colors

Chat messages support color tags:

`{DEFAULT}` `{WHITE}` `{DARKRED}` `{GREEN}` `{LIGHTYELLOW}` `{LIGHTBLUE}` `{OLIVE}` `{LIME}` `{RED}` `{LIGHTPURPLE}` `{PURPLE}` `{GREY}` `{YELLOW}` `{GOLD}` `{SILVER}` `{BLUE}` `{DARKBLUE}` `{BLUEGREY}` `{MAGENTA}` `{LIGHTRED}` `{ORANGE}`

HTML center messages use HTML instead: `<font color='#ff00ff'>TEXT</font>`.

Panorama color parsing and the Workshop label/CSS contract can be checked with `dotnet run --project Tests/PanoramaText.Tests.csproj -- .` from the repository root. Update both the plugin and Workshop addon to enable multicolor cards; older compiled layouts do not contain the required text segments.

## Translations

Any `{key}` in a message is looked up in `plugins/Advertisement/lang/<language>.json` and replaced with the text in the player's language. Players change their language with `!lang`. Translations can use color tags and placeholders too.

`lang/en.json`:
```json
{
  "current_map": "{GREY}Current map:",
  "current_time": "{GREY}Current time:"
}
```

`lang/ru.json`:
```json
{
  "current_map": "{GREY}Текущая карта:",
  "current_time": "{GREY}Текущее время:"
}
```

With `"Chat": "{current_map} {BLUE}{MAP}"` an English player sees `Current map: Mirage` and a Russian player sees `Текущая карта: Mirage`. To add a language, add a file named after it, for example `lang/de.json`.

## Screenshots

**Chat**

![chat](https://github.com/partiusfabaa/cs2-advertisement/assets/96542489/c6b008b4-9b66-4d8a-9cd8-c40505d0f1c3)

**Center**

![center](https://github.com/partiusfabaa/cs2-advertisement/assets/96542489/5f56cb66-6aac-423a-b7d0-efa066e37da4)
