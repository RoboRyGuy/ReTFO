# Archipelago (Beta Release)

An Archipelago plugin for GTFO! Allows players to use the Archipelago software to randomize items across the multiworld.

The source for this mod can currently be found in my ReTFO repository, which contains all my GTFO mods: https://github.com/RoboRyGuy/ReTFO

The AP World source (and releases) can be found in a different repository: https://github.com/RoboRyGuy/Archipelago-GTFO

Issues and feedback can be submitted on the APWorld GitHub's issues page: https://github.com/RoboRyGuy/Archipelago-GTFO/issues

## Getting Started

### MID Files and Modded Support

*As of version 0.0.4, the AP World file for GTFO now contains the MID data needed for vanilla worlds.*
If you wish to play a modded game, you will still need to follow the steps for exporting a MID
file. Those steps can be found below in the **Modded Support** section.

### Multiplayer Support

As of version 0.0.4, there is now official support for multiplayer. Multiplayer supports 3 different models:

- **Parallel Play:** Two or more players each play their own copy of GTFO. Both connect to the Archipelago server
  and are able to send/receive items and checks, but they are not in the same lobby.
- **Multi-Connected:** One player connects to the Archipelago server and hosts a GTFO lobby. One or more clients
  connect to the Archipelago server and join the lobby.
- **Proxy-Connected:** One player connects to Archipelago and hosts a GTFO lobby. One or more clients then connect
  to only the GTFO lobby. These players are automatically connected to the Archipelago server using the Host as a
  proxy; they are able to send and receive checks normally, but have no ability to execute commands or request hints.

*Proxy-Connected is currently unstable. I don't know the cause, but be aware you may have connectivity issues.*

These three models can be mixed and matched as needed. When playing multiplayer, Multi-Connected is the preferred
connection methodology; it will be more stable and act more predictably than Proxy-Connected. Moreso, it will
support host migration and other behaviours that require multiple players to be hosting. 

The main purpose of Proxy-Connected is to offer a secure way to allow strangers to play with you; when strangers
connect, they will not know of or have access to your Archipelago server or its address, and they will not have
the permissions needed to ruin your Archipelago game. All their access is limited to what GTFO allows them to do
in order to play the game.

To use either multiplayer model, simply connect using a lobby code as normal. The model used depends on the context;
if you connected to Archipelago before joining the lobby, you will be multi-connected; if you did not, then you will
become proxy-connected.

### Standard Setup

Here are the steps required to get started with a vanilla world:

1. Create a new modded profile in your preferred mod client. Include Archipelago and its dependencies in your mod list.
    - It is strongly recommended you also install CConsole at this time, in order to get past any bugs or improperly-balanced sections.
    - If you wish to play with any other mods, install them now and see the **Modded Support** section for details below
2. Download and add the APWorld file to your Archipelago installation.
3. Using your preferred method, create a YAML options file. Help for this can be found below in the YAML section.
4. Generate and host your multiworld.
5. Start GTFO using the modded profile you created and enter the *Network Settings* menu from the main menu.
6. Set your server details as needed.
7. Return to the main menu (using the *Rundown* button at the top) and connect!

## Cautions

- There are still a handful of a logic errors in the game which are known but not fixed at this time.
  - You may sometimes be told you can complete secondary and overload when you need another bulkhead key.
    This shouldn't prevent you from getting more checks, including checks needed to actually clear the sector.
  - If you randomize warps, levels like R6B1 will be considered beatable without the return warp.
    Again, for R6B1 this shouldn't prevent you from getting other necessary checks, but can be confusing.
    There may be other levels I have no considered where this is more of a problem.
  - If you find logic errors, I'd appreciate if you report them on the GitHub issues page for the AP World: 
    https://github.com/RoboRyGuy/Archipelago-GTFO/issues
- There is no difficulty balancing at this time, nor is any difficulty balancing planned - GTFO is intentionally a
  hard game, so set up your randomization with care to avoid, for example, needing to survive 8 reactor waves with no
  gear or lobby slots.
- Survival objectives, such as R8E1, have checks related to simply surviving (normally, these checks unlock doors). 
  This is technically possible to do even if all the doors are locked, and as such is currently allowed in the 
  logic. With that said, if you choose to add such a level to your requirements, be aware that this may be necessary; 
  for that reason, consider cheesing these. (Again using R8E1 as an example, you can stand on the big pipe 
  tunnel in the first hallway and simply wait out the 10 minutes, which checks 3 randomizable locations)
- Certain softlocks are possible; for example, you can accidentally bring necessary items to another dimension
  and leave them there, which is generally impossible in vanilla. These simply force you to restart the expedition.

## The AP Command

On each and every terminal is a new command called `AP`. This command will be essential for using items.
It is strongly recommended you take a look at the command in-game using the `AP HELP` sub-command; this should
give you enough information to get started.

## Items and Locations

A wide collection of items and locations are currently supported. You have the option
to enable or disable randomization of each individual location and item in the game.
The below describes the behaviour of some locations and items when they are enabled.

Locations:

- Picking up progression pickups, such as keys or IDs, as well as most big pickups,
  including cells, fog turbines, the MWP, etc will check a location and despawn the item.
  Note that if events are tied to picking up an item, they will not trigger when you attempt
  to pick it up.
- When certain events trigger (for example, by opening a door or completing special scans), the 
  event is cancelled and a location is checked instead. This includes events which unlock or open 
  doors, warp the team to a different dimension, start scans, or immediately clear the expedition.
- On each terminal is up to 3 location checks which can be completed using the `AP EXTRACT` and `AP RELEASE`
  sub-commands. These are added to account for items which are not normally collectibles, for example
  expedition unlocks, gear unlocks, and lobby slot unlocks. The more of these items you enable (and the
  fewer terminals you have access too), the more the terminals will be filled up.
- Any time you would learn a reactor code (checking a log or seeing it on-screen), you instead
  get a location check.
- Learning a terminal's password or password part is also a check in the same manner.
- Certain progression scans (Gen Cluster ending scans, HSU scans, and Dimension Portal scans)
  are checks; the trigger which would normally start the scan instead checks a location.
- Entering certain zones / "regions" will check locations. Most of these checks are for control
  locations used by logic to ensure beatability; however, some of these are randomized. Most notably,
  during a GatherSmallItems objective (for example, R1B1) you will automatically get checks for
  entering a Zone with fewer than the max count of objective items in it (in R1B1, this would be 3).

Some of the randomized items:

- Many progression-related pickups, such as keys, objective oickups (IDs, data cubes), and big pickups 
  such as cells, fog turbines, and MWPs are supported. These items will be received into the terminal
  system and can be spawned in using a terminal.
- Certain events, including door unlock events, custom scan events, warp events, and win events.
  For most events, it must be manually triggered from a terminal; door unlock events will automatically
  unlock the relevant door when received or when the expedition is started.
- Reactor codes and terminal passwords received from other players will show up in custom UI in the top left
  once you enter the zone containing the reactor or terminal. They can also be viewed at the terminal.
- Certain progression scans (Gen Cluster ending scans, HSU scans, and Dimension Portal scans).
  Once obtained, these scans can be started from a terminal.

An additional set of "floating" or "optional" items:

- Expedition unlocks, if randomized, prevent access to an expedition until found. If not randomized,
  all expeditions are unlocked immediately upon start (this will perhaps change in the future)
- Gear items can be locked, preventing them from being equipped. If a gear item is received while in an expedition,
  one copy is sent the terminal, allowing up to one player to equip it.
  Notably, if you do not start with a gear item available for a slot (ie by adding it as a starting item) you
  are given default gear for that slot. *As soon as you gain a gear item, the default gear is lost and everyone
  is forced to switch to the new gear*. It's recommended you specify your starting gear when you create
  the multiworld if you want to avoid this.
- Lobby slots, if randomized, become unusable until the corresponding unlock item is found.
- If randomized, a free checkpoint can be found for every enabled expedition. Once obtained, you can activate
  the checkpoint at any terminal while in the level, once per attempt at the level. This works the same
  as any other checkpoint, but does not require a team scan to activate.

## Random-like Items

A limited set of items are marked as "Random-like", the most notable of which are bulkhead keys and cells.
These items will always behave as if they're randomized, even if not actually randomized. This means that
the first time you pick one up, it will try to behave normally; however, if you restart the expedition, it'll
act as if you were sent it via the multiworld. For most items, this simply means you must retrieve it from
the terminal.

Random-like items exist to prevent logic errors. Archipelago has a hard time reasoning about these items because
they are both consumable and can be used in multiple places. GTFO's nature as a game where the expeditions can be
replayed means we give Archipelago much more breathing space by treating these items as "random-like".

## Death Link

This integration supports Death Link, though it hasn't been tested. The settings for Death Link are not tied to the 
YAML config and can be changed at any time in the mod settings in-game. Punishments range from spawning a tank
to instantly killing the whole team, so you should be able to find a middleground that works for you.

## Energy Link

This integration uses Energy Link. Picking up a Warden Artifact will add energy to your team;
equipping a Booster costs energy. This is also not very tested, but theoretically works :)

## Modded Support

This integrations aims to be compatible with modded rundowns. With that said, not much testing has been
done to ensure it works. 

To create a modded game, you will need a file called the MID file. This is a file with the name "GTFO-abcdefhij.ini"
which contains a list of everything Archipelago needs to generate a game for this world. To create this file,
start the game with all your mods enabled and go to the Server Settings. There will be a button which allows
you to export the MID file; this outputs it to your Downloads folder.

Take the MID file and place it in your Players folder, then restart the Archipelago client. This adds your modded
world as a new APWorld, and it can be treated the same. If you are having someone else perform the generation
of your game, supply them with both the MID file and your YAML; both go in the Players folder.

MID files are uniquely named to represent the set of mods in your game. If two sets of players generate a MID
file with the same name, there is a 1/(2^60) chance that the games are incompatible. In other words, it is
extremely likely they can share the file with no issues.

It is also worth noting that the "Vanilla" MID file uses the reserved name "GTFO.ini". If your generated MID file
has this name, you do not need a MID file (though supplying it should cause no issues).

### More Details

When the game generates MID data (which it does at startup), it will output logs around errors, issues, etc.
These include some common errors (like how R8E1 has no extraction point) and some assumptions being made
in order to aid generation. It will also output logs with the name of the game and its unique hash. The 
MID's file name is the hash truncated to 60 bits; the full 256 bit hash is what gets logged, and can be 
used to check for uniqueness if you feel that you've somehow beaten the 1 in a quintillion odds of getting 
non-compatible games with the same name.

If you'd like to see these logs (as well as other logs output by Archipelago), I recommend going into 
BepInEx.cfg and disabling Unity Log Listening. This will significantly reduce the number of logs sent to 
the console.