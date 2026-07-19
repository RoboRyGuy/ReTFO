# Changelog

# First Alpha Release

Alpha release of Beta-Archipelago. 
 - I make no guarantee that anything works correctly
 - I make no guarantee that everything will stay the same in coming versions

## Version 0.0.2

Second Alpha Release of Beta-Archipelago. With any luck, it works this time
- Fixed numerous issues
- Rewrote the APWorld
- Double-checked that things do, in fact, work before uploading them

## Version 0.0.3

Third Alpha Release of Beta-Archipelago.
- Added async login, which will allow non-local hosts (specifically, allows connecting to archipelago.gg)
- Fixed ITEMS subcommand. It is now much more durable and does not give all items instantly
- Fixed main menu; it now shows with entering sub menus
- Probably some other fixes/changes too

## Version 0.0.4

Fourth Alpha Release of Beta-Archipelago.
- Began adding official support for Multiplayer
  - Multiplayer supports three modes: Parallel play, Multi-connected, and Proxy-connected
- Fixed some sync issues related to (but not part of) multiplayer
- Fixed a major bug where restarting the game could disable item collection from Archipelago
- Improved the gear spawn mechanics (at least slightly)
- Bots no longer reset their gear when returning to the expedition start screen
- Changed the terminal command names to better convey their intended use
  - `CLAIM_ALL` has been removed
  - `CLAIM` now acts like `CLAIM_ALL`
  - `CLAIM_CODE` now acts `CLAIM` used to act
  - `CA` is unchanged
- Added "trash" items system, where locations can be marked as trash to ignore them
  - You can now use the `AP TRASH` command to mark all extractable items in a terminal as trash
- Changed objective item names to no longer include an objective summary (to make the names shorter)
- Add chat settings to help decrease chat message size and quantity
- Add a reset button to the Rundown menu which allows players to disconnect from Archipelago
- Moved pickup logic to synced method to hopefully make it more consistent

## Version 0.0.5

Fifth Alpha Release of Beta-Archipelago
- Regions are now tagged and sorted hierachally, granting more control what is randomized
- New options API and system to support the YAML, which adds in more user-friendly confiuration
- Added custom implementations of most Archipelago options (Local items, hints, etc)
- Added progressive expedition ordering, so expeditions can be completed in order
- Internal-only items and locations are now "Event" locations in Archipelago, which should make UT and similar more useful.
- Fixed a lot of bugs (but not all of them)
- Probably some other things. I changed a lot, I did not make many notes.
