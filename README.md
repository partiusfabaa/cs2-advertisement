# cs2-advertisement
A plugin for cs2 that allows you to show ads in chat/center/panel

# Installation
1. Install [CounterStrike Sharp](https://github.com/roflmuffin/CounterStrikeSharp) and [Metamod:Source](https://www.sourcemm.net/downloads.php/?branch=master)
3. Download [Advertisement](https://github.com/partiusfabaa/cs2-advertisement/releases/tag/v1.0.0)
4. Unzip the archive and upload it to the game server

# Config
The config is created automatically in the same place where the dll is located
```

{
    "Admins":[],       // Write your SteamID64 here to reload the configuration. If you want to add more than 1, write them separated by commas
    "Delay":35,        //a timer after which the advertisement will be shown
    "MessageSections":{
	"Chat":[
		"Chat Advertising 1",    // Chat advertising
		"Chat Advertising 2",
		"Chat Advertising 3"
	],
	"Center":[
		"Center Advertising 1",  // Advertising in the center
		"Center Advertising 2",
		"Center Advertising 3"
	],
	"Panel":[
		"Panel Advertising 1",   // Advertising in the panel, only at the end of the round
		"Panel Advertising 2",
		"Panel Advertising 3"
	]
    }
}

CHAT COLORS: {DEFAULT}, {RED}, {LIGHTPURPLE}, {GREEN}, {LIME}, {LIGHTGREEN}, {LIGHTRED}, {GRAY}, {LIGHTOLIVE}, {OLIVE}, {LIGHTBLUE}, {BLUE}, {PURPLE}, {GRAYBLUE}
	
PANEL COLORS: <font color='HEXCOLOR'>TEXT</font>
	
TAGS:
	{MAP} 	- current map
	{TIME} 	- server time
	{DATE} 	- current date
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
`css_advert_reload` - reloads the configuration. Only for those specified in the configuration
