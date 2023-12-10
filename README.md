# cs2-advertisement
A plugin for cs2 that allows you to show ads in chat/center/panel

# Installation
1. Install [CounterStrike Sharp](https://github.com/roflmuffin/CounterStrikeSharp) and [Metamod:Source](https://www.sourcemm.net/downloads.php/?branch=master)
3. Download [Advertisement](https://github.com/partiusfabaa/cs2-advertisement/releases/tag/v1.0.5)
4. Unzip the archive and upload it to the game server

# Config
The config is created automatically in the same place where the dll is located
```
{
  "PrintToCenterHtml": false, 	// if true, the text displayed in CENTER can use html
  "WelcomeMessage": {
    "MessageType": 0,                              //0 - CHAT | 1 - CENTER | 2 - CENTER HTML
    "Message": "{RED}Welcome, {GREEN}{PLAYERNAME}" //The text that the player will see can use color tags and the `{PLAYERNAME}` tag.
  },
  "Ads": [
    {
      "Interval": 35,			//a timer after which the advertisement will be shown
      "Messages": [
        {
          "Chat": "IP: {RED}{IP}:{PORT}",// Chat advertising
          "Center": "Server name: {SERVERNAME}" 		// Advertising in the center
        },
        {
          "Chat": "map_name",
          "Center": "Center Advertising 2"
        }
      ]
    },
    {
      "Interval": 40,
      "Messages": [
        {
          "Chat": "current_time"
	//you can only write "Chat" or "Center".
        },
        {
          "Chat": "{RED}Chat {BLUE}Advertising {GREEN}4"
        }
      ] 
    }
  ],
  "Panel":[
	"<font color='#ff00ff'>Panel Advertising 1</font>",   // Advertising in the panel, only at the end of the round
	"Panel Advertising 2",
	"Panel Advertising 3"
  ],
  "DefaultLang": "US", // Default language (it will be shown if there is no player's language in the config)
  "LanguageMessages": {
    "map_name": { 	//It is what you write that will define your message
      "RU": "{GRAY}Текущая карта: {RED}{MAP}",
      "US": "{GRAY}Current map: {RED}{MAP}",
      "CN": "{GRAY}当前地图: {RED}{MAP}"
    },
    "current_time": {
      "RU": "{GRAY}Текущее время: {RED}{TIME}",
      "US": "{GRAY}Current time: {RED}{TIME}",
      "CN": "{GRAY}当前时间: {RED}{TIME}"
    }
  },
  "MapsName": {
    "de_mirage": "Mirage",
    "de_dust2": "Dust II"
  }
}

CHAT COLORS: {DEFAULT}, {RED}, {LIGHTPURPLE}, {GREEN}, {LIME}, {LIGHTGREEN}, {LIGHTRED}, {GRAY}, {LIGHTOLIVE}, {OLIVE}, {LIGHTBLUE}, {BLUE}, {PURPLE}, {GRAYBLUE}
	
PANEL COLORS: <font color='HEXCOLOR'>TEXT</font>
	
TAGS:
	{MAP} 	- current map
	{TIME} 	- server time
	{DATE} 	- current date
	{SERVERNAME} - server name
	{IP} - server ip
	{PORT} - server port
	{PLAYERS} - number of players on the server
	{MAXPLAYERS} - how many slots are available on the server
	\n		- new line
```

# Images
CHAT:
![image](https://github.com/partiusfabaa/cs2-advertisement/assets/96542489/c6b008b4-9b66-4d8a-9cd8-c40505d0f1c3)

CENTER:
![image](https://github.com/partiusfabaa/cs2-advertisement/assets/96542489/5f56cb66-6aac-423a-b7d0-efa066e37da4)

PANEL:
![image](https://github.com/partiusfabaa/cs2-advertisement/assets/96542489/cd1e788f-9e8e-4276-a90c-e08d8adb21f5)

# Commands
`css_advert_reload` - reloads the configuration. The `@css/root` flag is required for use.
