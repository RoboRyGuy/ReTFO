# Archipelago (Beta Release)

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/Q8J62670V6)

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
  connect to the Archipelago server and join the lobby. *All connected players use the same slot* - ie, the GTFO
  game as a whole is considered one player as far as the Archipelago server is concerned.
- **Proxy-Connected:** One player connects to Archipelago and hosts a GTFO lobby. One or more clients then connect
  to only the GTFO lobby. These players are automatically connected to the Archipelago server using the Host as a
  proxy; they are able to send and receive checks normally, but have no ability to execute commands or request hints.

*Proxy-Connected is currently unstable. I don't know the cause, but be aware you may have connectivity issues.*

These three models can be mixed and matched as needed. When playing multiplayer, Multi-Connected is the preferred
connection methodology; it will be more stable and act more predictably than Proxy-Connected. Moreso, it will
support host migration and other behaviours that require other players who can act as the host. 

The main purpose of Proxy-Connected is to offer a secure way to allow strangers to play with you; when strangers
connect, they will not know of or have access to your Archipelago server or its address, and they will not have
the permissions needed to ruin your Archipelago game. All their access is limited to what GTFO allows them to do
in order to play the game.

To use either multiplayer model, simply connect using a lobby code as normal. The model used depends on the context;
if you connected to Archipelago before joining the lobby, you will be multi-connected; if you did not, then you will
become proxy-connected.

## Standard Setup

Here are the steps required to get started with a vanilla world:

1. Create a new profile in your preferred mod client. Include Archipelago and its dependencies in your mod list.
    - I recommend using R2modman for modding GTFO.
    - It is recommended you also install CConsole at this time, in order to get past any bugs or improperly-balanced sections.
    - If you wish to play with any other mods, install them now and see the **Modded Support** section for details below.
2. Download and add the APWorld file to your Archipelago installation.
3. Using your preferred method, create a YAML options file. Help for this can be found below in the YAML section.
4. Generate and host your multiworld.
5. Start GTFO using the modded profile you created and enter the *Network Settings* menu from the main menu.
6. Set your server details as needed.
7. Return to the main menu (using the *Rundown* button at the top) and connect!

### Cautions

- There are still a handful of logic errors in the game which are known but not fixed at this time.
  - Specific expeditions, such as R8B2 and R7D1, have unique features which are not currently supported.
  - Some other expeditions, such as R8D2, have unintuitive checks - in R8D2, the dual scan at the end has two
    different checks depending on what order you complete the scans in.
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
- Certain new softlocks are possible; for example, you can accidentally bring necessary items (ie cells) to another dimension
  and leave them there, which is generally impossible in vanilla. These simply force you to restart the expedition.

### YAML Help

GTFO uses a tagging system for all its Regions, Locations (Checks), and Items. You do not need to
know or use the tagging system to set up Archipelago; you can learn/use it to perform fine-grained
customization. 

>  **More information on tagging. This is optional to read, intended for advanced users.**
>
>  Each Region, Location, and Item is assigned a unique tag, identifiable by name (and ID, though
>  IDs are not used in the YAML). Each tag can have any number of parent tags (which in turn can
>  have their own parent tags). When working with options that support tags, you can instead specify 
>  a parent tag to target all children of that tag.
>
>  Tags can be exported from the settings same menu as MID data; they can be exported in either JSON
>  or CSV format, whichever you prefer for your data-intense reading pleasure.
>
>  Options supporting tags are most commonly found in the *GTFO Item & Location Options* category.
>  Note that this category is "Advanced", as it has the most direct control over how GTFO behaves.
>  Internally, most other options simply modify options in this group.
>
>  A few notes and examples about/using tags:
>  - The "Enabled Expeditions" option simply copies the tags you pick to the Region Whitelist.
>    Besides the fact it supports randomization, it is identical to simply putting the list of 
>    expeditions in the Region Whitelist.
>  - Most regions are organized as such: Expedition > Layer > Zone > Terminal
>    - A layer is a dimension or sector, ie "(Main)" or "(Dim #1)"
>  - Many settings, ie "Scan Randomization", allow you to pick whitelist, blacklist, or none.
>    These simply put the relevant tag into the relevant white or blacklist for you.
>  - The "Progression Style" option works by intelligently moving specific tags into specific
>    lists. For example, for the "FREE" progression style, it does the following:
>      - Add the tag "Floating Expedition Unlock Items" to Start Vouchers with a count of -1 (match all)
>      - Add the tag "Floating Expedition Unlock Items" to the Item Whitelist
>      - Add the tag "Floating Expedition Paths" to the Region Whitelist. This tag adds a region with
>        paths connecting it to the menu and each expedition, each requiring the relevant expedition
>        unlock item to traverse.

#### Creating a YAML

When randomizing GTFO, it's recommended you generate a YAML template using the Generate Templates action
in the Archipelago Launcher. The Options Creator has some troubles with a few of GTFO's options, and
can produce bad results.

#### Multi-Choice Options

Some of GTFO's options are "multi-choice". At present, the only two multi-choice options are *Enabled 
Expedtiions* and *Starting Expedition*. These choices allow you to choose as many or as few options 
as you want.

Multi-Choice options contain one special field, `random`. This determines how many choices are picked
from the multi-choice, and how they are picked.
- If `random` is 0, no items will be taken from the list
- If `random` is a number greater than 0, that many items will be taken from the list.
  For example, if it's set to `random: 5`, 5 items will be picked from the list.
  This will pick items based on their weight; items with a weight of 0 or lower will not be picked.
  If there are not enough items available, this will throw an error.
- If `random` is -1, all items with a weight greater than 0 will be picked from the list.


#### Game Options

These settings control some overarching parts of the generation.
- **accessibility** is the default Archipelago accessibility option. It controls whether you're
  guaranteed able to reach all checks or not.
- **progression_balancing** is also the default archipelago option. Higher values make it easier to
  find progression items early on.
- **Root Seed** controls many parts of randomization. You may leave it at 0 for a random root seed.
  *Due to technical limitations, the root seed, when randomized, ignores the multiworld seed; if 
  you're trying to share/recreate a seed, make sure you also share the GTFO root seed.*
- **Fail If Insufficient Empty Locations** will ensure an error is thrown if there is not enough
  space in the world to generate. If you disable this, any items which could not be distributed
  into empty spaces will be placed into your starting inventory, for better or worse.
- **Fail During Sampling** will throw an error if something goes wrong while sampling items.
  Generally, you want this left on, as it'll ensure your random starting items and such are not 
  simply omitted out due to an unforseen issue. However, the option to turn it off is available.

#### Goal

The goal options are fairly straightforward; they control what is required to beat the world.
Most of the settings control which sectors must be cleared. For each enabled expedition you have
(see below), if it has one of the goal sectors, that will be added as a separate "Goal item".
This is then counted; ie, if your expeditions are R4A1 and R4A2 with Main, Overload, and PE, your
goal count would be 4:
- R4A1 Main
- R4A2 Main
- R4A2 Overload
- R4A2 PE

> Note that, for this example, while secondary isn't required, you'd still have to complete it 
  in R4A2 in order to get the PE clear item.

> Also note that disabling a sector as a goal does not prevent items from being randomized in to
  or out of that sector.

Once you have your goal count, you can use **Skippable Goal Count** to decrease it. Using
the same example above, with a skippable goal count of 1, you could skip any 1 sector, which could
be the entirety of R4A1 or the R4A2 clear. (It's not technically possible to skip an other sectors
in that particular example, but if you found a way to it would work)

#### Expeditions

These settings control several things related to expeditions:

**Enabled Expeditions**

This option controls which expeditions are available when you play; any expedition not listed will 
be inaccessible, and no items will randomize in or out of it (barring you edit edit the Region 
Whitelist or Blacklist).

Each expedition listed here will be required to meet the goal. Depending on the other settings you've
enabled, you will be required to clear the main, secondary, and PE of these expeditions.
However, even if you have it set to main only, items will still randomized into/out of secondary
and overload on these expeditions; plan accordingly.

**Progression Style**

This option controls how expeditions are unlocked, if at all. There are two main progression styles:
- **Item** unlocks expeditions by finding randomly placed items in the multiworld.
- **Progressive** unlocks an additional expedition each time you clear an expedition

All other options are variants of these, described accordingly in its description. The only variant
warranting more detail is the SEMI variant. SEMI only allows Archipelago to recognize the Progressive
style, but still places Item-style unlocks in the multiworld. This guarantees that you can clear the
expeditions in order, without entering a future expedition, but still allows you to break order and 
do other stuff if you're stuck waiting for something in your "main" expedition.

**Starting Expeditions (ITEM)**, **Random Starting Expeditions (ITEM)**, and **Number of Unlocked 
Expeditions (PROGRESSIVE)** do as advertised. Each only operates when its relevant progression style
is selected. **Random Starting Expeditions** should be used when you've randomized your enabled
expeditions because **Starting Expeditions** is not guaranteed to give you an expedition you enabled
when it's randomized.

#### Lobby Slots

Lobby slots skip the host slot. If you have `Number of Unlocked Lobby Slots: 1`, then you will start
with two slots; the host slot plus 1 more. Unless you're playing with lobby expansion, there are
3 lobby slots total.

### The AP Command

On each and every terminal is a new command called `AP`. This command will be essential for using items and completing checks.
It is strongly recommended you take a look at the command in-game using the `AP HELP` sub-command; this should
give you enough information to get started.

### Items and Locations

A wide collection of items and locations are currently supported. You have the option
to enable or disable randomization of each individual location and item in the game.
The below describes the behaviour of some locations and items when they are enabled.

Locations (Checks):

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

- Most progression-related pickups, such as keys, objective pickups (IDs, data cubes), and big pickups 
  such as cells, fog turbines, and MWPs are supported. These items will be received into the terminal
  system and can be spawned in using a terminal.
- Certain events, including door unlock events, custom scan events, warp events, and win events.
  For most events, they must be manually triggered from a terminal; door unlock events will automatically
  unlock the relevant door when received or when the expedition is started.
  - Some events force doors open. As these events are often intended to hurt the player, these events
    have all been replaced with "unlock" events, so that they don't trigger early.
- Reactor codes and terminal passwords received from other players will show up in custom UI in the top left
  once you enter the zone containing the reactor or terminal. They can also be viewed at the terminal.
- Certain progression scans (Gen Cluster ending scans, HSU scans, and Dimension Portal scans, and "event" scans).
  Once obtained, these scans can be started from a terminal.

An additional set of "floating" or "optional" items:

- Expedition unlocks, if randomized, prevent access to an expedition until found. If not randomized,
  all expeditions are unlocked immediately upon start (this will perhaps change in the future)
- Gear items can be locked, preventing them from being equipped. If a gear item is received while in an expedition,
  one copy is sent the terminal, allowing up to one player to equip it.
  *If you do not start with a gear item available for a slot (ie by adding it as a starting item) you
  are given default gear for that slot. As soon as you gain a gear item, the default gear is lost and everyone
  is forced to switch to the new gear*. It's recommended you specify your starting gear when you create
  the multiworld if you want to avoid this.
- Lobby slots, if randomized, become unusable until the corresponding unlock item is found.
- If randomized, a free checkpoint can be found for every enabled expedition. Once obtained, you can activate
  the checkpoint at any terminal while in the level, once per attempt at the level. This works the same
  as any other checkpoint, but does not require a team scan to activate.

### Logic Notes

The logic is set to only consider checks reachable if you are *guaranteed* able to reach it. This is opposed to 
things you *might* be able to reach with a bit of luck.

For example, in R5D1 there is a colored key card which can spawn in two zones. The logic will only consider this
check reachable if you can reach *both* zones, even though you only need to be able to reach one of the possible
zones (and re-drop into the expedition until the keycard is in that zone).

As part of choice processing, checks are considered reachable if, for every possible spawn for that check, there 
exists at least one choice state in which it is reachable. For example, in R4A2 Secondary, the HSU can spawn 
behind 3 different doors, each requiring a cell to unlock. The HSU is considered reachable if you only have 1 cell 
(as opposed to 3) because you can choose to consume that cell on any of three doors, therefore ensuring whichever
zone the HSU spawned in is reachable. IE, it only takes one cell to guarantee that the HSU is reachable.

## Death Link

This integration supports Death Link, though it hasn't been tested. The settings for Death Link are not tied to the 
YAML config and can be changed at any time in the mod settings in-game. Punishments range from spawning a tank
to instantly killing the whole team, so you should be able to find a middleground that works for you.

## Energy Link

This integration uses Energy Link. Picking up a Warden Artifact will add energy to your team;
equipping a Booster costs energy. This is currently buggy, so feedback is appreciated :)

## Modded Support

This integrations aims to be compatible with modded rundowns. Full modded support will be added in the final
release (with expansion mods to help improve compatibility with rundowns created based on demand). At present,
if you're lucky enough that it works out of the box then enjoy it full-heartedly, otherwise support will be limited.

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

When you export MID data, the mod performs choice processing in which it computes all possible relevant choice
states and compresses them. 
 - A choice is an action which improves reachability of one or more regions at some cost, typically by consuming
   an item. The most common examples are using bulkhead keys and inserting cells into generators.
 - A choice state is the set of unique choices that have been taken. It identifies a state of the game in which
   one or more regions are reachable because of the choices which have been made, and what the cost of making
   those choices was.
When choice state processing concludes, the mod will use the newly-computed choice states and region map
to attempt to beat the game. It will dump to the log which expeditions and sectors it was able to clear, and
which it was not. As a rule of thumb, if Archipelago is able to find a route to clear a sector, you most likely
can play that sector in Archipelago with minimal issues. If it cannot, then it is guaranteed impossible to
play that expedition in Archipelago.