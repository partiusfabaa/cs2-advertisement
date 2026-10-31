# Advertisement Panorama Workshop addon

This folder contains the Panorama layout source files for the Advertisement plugin.

1. Create an addon in Counter-Strike 2 Workshop Tools named `advertisement_panorama`.
2. Extract `Advertisement-Panorama-Workshop.zip` into the Workshop Tools project directory, merging its `content/csgo_addons/advertisement_panorama/` folder with the addon created in step 1.
3. Open and publish the addon with Workshop Tools. The tools compile the `.xml` and `.css` Panorama files.
4. Make the published addon available to players through the server's Workshop map or collection.
5. In `Advertisement.json`, set `PanoramaEnabled` to `true` and add messages to `PanoramaMessages`.

The plugin creates the `custom_hud_layout` entity and updates it from the server. No map entity or client-side script is needed. Clients without the published addon simply do not have the layout to display; the plugin itself continues running and its chat/center ads are unaffected.

The card is centered horizontally, 140 Panorama pixels from the top to leave space for the score HUD. Its width and height fit the text content, with a maximum width of 90% of the screen, 16px vertical / 28px horizontal padding and 36px text. Short messages get compact cards; longer messages get wider cards up to the screen limit. Position and size changes require recompiling and republishing the addon, not reloading the plugin config.

If Panorama ads are disabled or `PanoramaMessages` is empty, the plugin does not create the layout entity.

After upgrading the plugin's Panorama integration, rebuild and republish this addon too: the dialog-variable root is now `AdvertisementRoot`, matching the plugin's explicit layout contract. The cards are read-only and never capture the player's mouse.

For multicolor PMM messages, update both the plugin and this addon. The layout has a fixed pool of 16 labels, driven through `message0` through `message15` and `color-*` CSS classes. Tags such as `{RED}`, `{GREEN}` and `{DEFAULT}` color successive text segments, including tags inside translations. No client scripts or HTML are used.
