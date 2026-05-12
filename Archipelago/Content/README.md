# Archipelago (Beta Release)

An Archipelago plugin for GTFO! Allows players to use the Archipelago software to randomize items across the multiworld.

The source for this mod can currently be found in my ReTFO repository, which contains all my GTFO mods: https://github.com/RoboRyGuy/ReTFO

The AP World source (and releases) can be found in a different repository: https://github.com/RoboRyGuy/Archipelago-GTFO

## Getting Started

*These steps have changed compared to version 0.0.1*

Currently, this mod requires some extra steps compared to other Archipelago integrations; this is intended
to allow future support of modded rundowns, though this may change. 

1. Download and add the APWorld file to your Archipelago installation.
2. Create a new modded profile in your preferred mod client. Include Archipelago and its dependencies in your mod list.
    - It is strongly recommended you also install CConsole at this time, in order to get past any bugs or improperly-balanced sections.
    - If you wish to play with any other mods, you should install them now. If you add more later, you should restart from this step.
2. Launch the game.
3. In the main menu, select "Export MID Data". This will create a new file in your Downloads folder. This file's name will start with "GTFO-" and end with ".ini"
    - The name of this file represents the mods you installed, and acts as a sort of "subworld" for Archipelago.
    - If the Archipelago client recognizes your mod set, it will name it accordingly.
    - Otherwise, it will use a psuedo-random hash to identify your mods as a unique GTFO world.
    - At this time, only the vanilla mod set is named.
4. Move this file to your Players folder, the same folder you would place a YAML file in.
5. With this file in your Players folder, you can now launch the Options Editor. An entry for GTFO will now appear.
6. Create your YAML config per usual. Some guidance on this is included below.
7. To generate, you will need both the file exported from GTFO and your YAML. Place both into the Players folder.
8. From here, generate and host as normal.
9. You can use the "Network Settings" button in GTFO to configure the server address.

## Cautions

- Currently, there is no support for multiplayer - attempt it at your own risk.
- There are still logic errors in the game. The most notable is R8A2, due to the one-way nature of the secondary.
- There is no difficulty balancing at this time - it is assumed the player is capable of soloing all alarms and other hazards.
- Survival objectives, such as R8E1, have checks related to simply surviving. This is technically possible
  to do even if all the doors are locked, and as such is currently allowed in the logic. With that said,
  if you choose to add such a level to your requirements, be aware that this may be necssary; for that reason,
  consider cheesing these. (Again using R8E1 as an example, you can stand on the big pipe tunnel in the first 
  hallway and simply wait out the 10 minutes, which checks 3 randomizable locations)
- Certain softlocks are possible; for example, you can accidentally bring necessary items to another dimension
  and leave them there, which is generally impossible in vanilla. These simply force you to restart the expedition.

## Supported Items and Locations

At this time, many items and locations are supported. In general, if you receive an item, it will be 
placed "into the terminal system". You can use the custom AP command to "claim", or retrieve, the item. The custom AP
command is also required to perform location checks using the EXTRACT and RELEASE commands, depending on how 
many floating items are randomized.

Some of the randomized items:

- Many progression-realted pickups, such as Keys, Objective Pickups (IDs, Data Cubes), and Big Pickups (MWP, Cell, Fog Turbine)
  can be randomized. Attempting to pick them up will despawn them and grant an item; if they are received, you can spawn
  them in at any time from any terminal (if you're in the correct expedition)
- Certain events, including door unlock events, custom scan events, warp events, and win events are randomized.
  When these events would trigger, you instead receive an item; if you receive the event as an item, you can
  trigger it from a terminal (except for door unlocks, which immediately unlock the relevant door). 
- Reactor codes and terminal passwords are randomized. Viewing the code will grant an item; when the code
  is received as an item, you can check it from the terminal or see it in some new, custom UI which displays it
- Certain scans, specifically Generator Cluster scans, Dimension Portal scans, and HSU scans, can be randomized.

An additional set of "floating" items is also included:

- Expedition unlocks, if randomized, prevent access to an expedition until found. If not randomized,
  all expeditions are unlocked immediately upon start (this will perhaps change in the future)
- Gear items can be locked, preventing them from being equipped. If a gear item is received while in an expedition,
  one copy is sent the terminal, allowing up to one player to equip it. (This works but is a little buggy-looking)
  Currently, bots are not checked correctly, so they can spawn in with illegal gear if it was equipped beforehand.
- Lobby slots, if randomized, become unusable until the corresponding unlock item is found.
- If randomized, a free checkpoint can be found for every enabled expedition. If obtained, you can activate
  the checkpoint at any terminal while in the level, only once during the level.

## Configuring your YAML

### Tagging

The YAML file supports six options at this time (as well as some options common to all Archipelago games).
The key to the YAML is a tagging system. All items and locations each have a single, unique tag identifying them.
Each tag has a parent, creating a tree which meets at the root tag "All". When using tags in the options,
you can choose to either specify the leaf tags (which each correlate to a single item or location), or you can
choose a common parent to affect groups of items and locations.

As an example of tag hierarchy: The item "R1A1 ZONE_52 Colored Key" is the tag for the key required to unlock the door
to Zone 52 in R1A1, which is the checkpoint door. This tag is a child of "Colored Key Items", which is a parent of all
colored keys. This tag, in turn, is a child of "Small Pickup Items", which is a child of "Pickup Items", which is a child
of "All Items", which is a child of "All".

"All" > "All Items" > "Pickup Items" > "Small Pickup Items" > "Colored Key Items" > "R1A1 ZONE_52 Colored Key"

If you want to enable randomization of this key, you could whitelist any of these tags; if you whitelist just the
leaf tag ("R1A1 ZONE_52 Colored Key"), you will only randomize the single item; as you move up the tree,
you randomize more and more related items.

A full list of tags can be exported from the Network Settings menu. This will dump all tag names, IDs, descriptions, 
and parents to either a JSON file or a CSV file for your purusal. Both of these are normal text files which can be
opened in a text editor (such as Notepad), though you may prefer to open the JSON file in VS Code or an online viewer
and the CSV file in Excel or Google Sheets.

### Options

At this time, the following options are supported:

#### required_expeditions 

A list of expedition names, for example "R1A1", "R8E2", and so forth.
Each expedition name must be unique. Each name listed will be added as a playable level, and
you will be required to clear its main (and potentially side) objectives to clear the goal.

#### require_secondaries

If true, clearing the goal will also require you clear all secondaries on any expedition you
selected which has a secondary.

#### require_overloads

If true, clearing the goal will also require you clear all overload objectives on any expedition
you selected which has an overload.

Note that there is no support for requiring PE objectives at this time.

#### whitelist

This is a set of tags which enable items and locations for randomization. Items and locations
which are either in the whitelist or which are children of tags in the whitelist will be 
randomized (unless they are on the blacklist). *Randomization only occurs if both the item
and its vanilla spawn location are randomized.* For simplicity, I recommend putting either at
least one of either "All Items" or "All Locations" to help ensure one of the pair is randomized, 
and then filtering the other half as desired.

You may also use "All" to enable all items and locations for randomization.

#### blacklist

This is a set of tags which disables items and locations for randomization. This works exactly
the same as the whitelist, except it disables items. The blacklist overrules the whitelist.

#### start_items

GTFO supports its own custom start_items option. This works slightly differently from normal start items;
instead of creating new items, it instead pulls matching items out of the randomization pool to give them
to the player. 

This also supports the tagging system; if you specify a parent tag, the desired number of child items
are randomly selected from all available items and moved to the starting inventory, removing them from the game.

Each item removed in this manner is replaced with a filler item, "Empty", which does nothing.

#### early_items

This works exactly the same as start_items, but instead of moving items to the start inventory it marks
them as early items for the fill system. This can cause fill errors if insufficient early spots are
available, so use with caution.

## Example YAML

Below is a sample YAML file.

```
name: RoboRyGuy
description: YAML generated by Archipelago 0.6.7.
game: GTFO
GTFO:
  progression_balancing: normal
  accessibility: full
  required_expeditions:
  - R1B1
  - R6A1
  - R3A3
  - R6B1
  - R8C2
  - R4D2
  - R8E1
  require_secondaries: true
  require_overloads: true
  whitelist:
  - All
  blacklist: []
  early_items: {
    "Expedition Unlock Items": 1 
  }
  local_items: []
  non_local_items: []
  start_items: { 
    "Melee Gear Items": 1,
    "Primary Gear Items": 1,
    "Special Gear Items": 1,
    "Tool Gear Items": 1,
    "Expedition Unlock Items": 1 
  }
  start_hints: []
  start_location_hints: []
  exclude_locations: []
  priority_locations: []
```

This file is configured for player RoboRyGuy. The game is set to "GTFO (Vanilla-0_0_3)", which is the MID generated
by Archipelago when no other notable mods are installed.

This file enables randomization of all supported items. The player will have to beat all objectives
for 7 exepditions, including R1B1, R6A1, ..., R8E1 to reach the goal. 

Because all expedition unlocks are randomized, the player must specify at least one starting expedition. 
In this case, they're having Archipelago randomly pick one of their expeditions and add it as a starting item. 
If they fail to add a starting expedition, Archipelago will randomly choose any of the expeditions and add it.

Because all gear items are randomized, the player must specify at least one starting gear item for each slot.
In this case, they're having Archipelago randomly pick an item for each slot (melee, primary, special, and tool).
If they fail to add staring gear, temporary gear will be added in-game, which will be lost as soon as gear is found.

To aid the fill system, the player has added one additional expedition unlock to the early items. This helps ensure
that they have options when it comes to exploration.

It should also be noted that, since all items are randomized, the player starts with 3 locked lobby slots, meaning
they will have to find the lobby slot unlocks as they play before humans or bots can aid them. If this is harder than 
desired, one could add `"Unlock Lobby Slot Items": 1` to either the early or start items to ensure they quickly have allies.

## Death Link

This integration supports Death Link, though it hasn't been tested. The settings
for Death Link are not tied to the YAML config and can be changed at any time in the mod
settings in-game. See there for more details on ways to punish yourself for others' mistakes.

## Energy Link

This integrates Energy Link. Picking up a Warden Artifact will add energy to your team;
equipping a Booster costs energy. This is also not very tested, but theoretically works :)

## Mod Support

This integrations aims to be compatible with modded rundowns. With that said, at this time, attempt it at your
own risk; I need to get vanilla working before I will devote bandwidth to ensuring modded rundowns can work.
The more similar a mod is to Vanilla GTFO, the more likely it'll work; specifically, mods like AWO or which use
custom event triggers are more likely to cause errors.