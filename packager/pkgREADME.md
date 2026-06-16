# LCS (Lethal Command Service)
A simple ship terminal command tool that allows all players to run command and mess with each other. Both host and client are required to have this mod installed.

## Usage
1. Go to the ship's Terminal
2. Type `lcs`

Typical Usage usually involves: `lcs [commandName] [Arguments]`

## Argument Types
- `<RequiredArg>` denotes required argument
- `[OptionalArgs]` denotes as optional argument and comes after required arguments
- `<...RequiredArgs>` denote as required multi-args that takes as many argument space as needed at the end of the command
- `[...OptionalMultiArgs]` Similar to required multi-args except this is optional (RESERVED FOR FUTURE)

## Helps
Use `lcs` to show all available commands and then use `lcs help <commandName>` to show more a detail usage for a command.

## Available Commands
- `help <cmdName>` (More detail command usages)
- `debug` (shows debug information)
- `ping` (test Unity RPC and see network latency for a client)
- `list <type>` (list all available entity, item, trap, and players)
- `spawnitem <playerName> <itemName> [value] [isFake]` (spawn any grabbable items (including unreleased ones). isFake flag determines if the item can be picked up or not)
- `spawnentity <playerName> <entityName> [isFake]` (spawn any available in-game entity (including unreleased ones). isFake flag determines if the entity will freeze and not damage player)
- `spawntrap <playerName> <trapName> [isFake]` (spawn any available indoor facility trap. isFake flag will mark the trap as fully functional but does not damage player. Note that isFake flag will only work with vanilla traps. Any modded traps that were added to be game will be unaffected.)
- `cleanup` (manually clean-up any internally managed spawn objects, usually fake objects that game doesnt cleanup afterward. This is also done automatically after the ship leaves)
- `setbalance [balance]` (change in-game store credits)
- `setquota <type> [amount]` (change ship's quota status. Important to note that this may break the game's next quota calculation)
- `setplrstat <type> <target> [value]` (change player stats)
- `sendmsg <players> <header> <...message>` (send player custom message)
- `tp <from> <to>` (teleport player within the same region)
- `kill <Player>` (kill a player)