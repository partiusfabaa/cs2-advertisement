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
			"Chat Advertising 1",     // Chat advertising
			"Chat Advertising 2",
			"Chat Advertising 3"
		],
		"Center":[
			"Center Advertising 1",   // Advertising in the center
			"Center Advertising 2",
			"Center Advertising 3"
		],
		"Panel":[
			"Panel Advertising 1",    // Advertising in the panel, only at the end of the round
			"Panel Advertising 2",
			"Panel Advertising 3"
		]
	}
}
```
# Commands
`css_advert_reload` - reloads the configuration. Only for those specified in the configuration
