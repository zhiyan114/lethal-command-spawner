using GameNetcodeStuff;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace LethalChatSpawner
{
    public class TerminalCmd
    {
        public static Dictionary<string, TerminalCmd> lists = new Dictionary<string, TerminalCmd>()
        {
            ["help"] = new TerminalCmd
            {
                name = "help",
                description = "Show command description and arg usages",
                argDesc = new Dictionary<string, string>()
                {
                    ["<cmdName>"] = "Command Name to show help for"
                },
                callfunc = (t, args) =>
                {
                    if (args.Length == 0)
                    {
                        Print(t, $"Please specify command name to show help for!");
                        return;
                    }
                    TerminalCmd cmd;
                    if (!TerminalCmd.lists.TryGetValue(args[0], out cmd))
                    {
                        Print(t, $"The command you specified doesnt exist!");
                        return;
                    }

                    string output = $"{cmd.name}\n" +
                    "------------------------------------\n" +
                    $"{cmd.description}\n\n" +
                    "Args:\n" +
                    (cmd.argDesc?.Count > 0 ? cmd.argDesc.Join((a) => $"{a.Key} - {a.Value}", "\n") : "No args available");
                    Print(t, output);

                }
            },
            ["debug"] = new TerminalCmd
            {
                name = "debug",
                description = "Get mod internal state for debug purpose",
                callfunc = (t, _) =>
                {
#if DEBUG || TESTPLAY
                    Print(t, $"Session ID: {LCSMain.SessionID}\nLoaded: {ShipLandState.loaded}\nonMoon: {ShipLandState.onMoon}\nManaged Object Count: {LCSLogic.FakeItemsCount}");
#else
                    Print(t, $"Loaded: {ShipLandState.loaded}\nonMoon: {ShipLandState.onMoon}\nManaged Object Count: {LCSLogic.FakeItemsCount}");
#endif
                }
            },
            ["ping"] = new TerminalCmd
            {
                name = "ping",
                description = "Test Unity RPC Communication!",
                callfunc = (t, _) =>
                {
                    Print(t, "Pinging...");
                    PingUtil.Instance.pingServerRpc(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                }
            },
            ["list"] = new TerminalCmd
            {
                name = "list",
                description = "Show a list of actionable entity, item, or player names",
                argDesc = new Dictionary<string, string>()
                {
                    ["<type>"] = "Whether to show the list for grabbable [item], spawnable [entity], actionable [player], or spawnable [trap]"
                },
                callfunc = (t, args) =>
                {
                    if (args.Length == 0)
                    {
                        Print(t, "Please choose a list you want to see, either item, entity, player, or trap");
                        return;
                    }
                    switch (args[0])
                    {
                        case "entity":
                            {
                                string output = "Available Monster to spawn\n" +
                                "-----------------------------------------\n" +
                                "Note that spawning indoor enemy outside of the facility and vice versa may break discovery state\n\n" +
                                $"Indoor: {LCSLogic.globalEnemies.Where(c => !c.isOutsideEnemy).Join(c => c.enemyName, ", ")}\n\n" +
                                $"Outdoor: {LCSLogic.globalEnemies.Where(c => c.isOutsideEnemy).Join(c => c.enemyName, ", ")}\n\n";
                                Print(t, output);
                                break;
                            }
                        case "item":
                            Print(t, $"Available Items to spawn: {StartOfRound.Instance.allItemsList.itemsList.Join(c => c.itemName, ", ")}");
                            break;
                        case "player":
                            {
                                string output = StartOfRound.Instance.allPlayerScripts.Where(c => Utils.isPlayerConnected(c)).Join(c =>
                                {
                                    List<string> PlayerState = new List<string>();
                                    if (c.isPlayerDead)
                                        PlayerState.Add("dead");
                                    if (c.isInsideFactory)
                                        PlayerState.Add("indoor");
                                    return PlayerState.Count == 0 ? c.playerUsername : $"{c.playerUsername} ({string.Join(",", PlayerState)})";
                                }, "\n");
                                Print(t, $"Available Players to execute on:\n{output}");
                                break;
                            }
                        case "trap":
                            {
                                Print(t, $"Available Traps to spawn: {LCSLogic.globalTraps.Join(c => c.prefabToSpawn.name, ", ")}");
                                break;
                            }
                        default:
                            Print(t, $"{args[0]} is not a valid list!");
                            break;
                    }

                }
            },
            ["spawnitem"] = new TerminalCmd
            {
                name = "spawnitem",
                description = "Spawn interactable-type item next to a given player",
                argDesc = new Dictionary<string, string>()
                {
                    ["<playerName>"] = "Player username to run the command on (can be partially completed and return first one if multiple shares same partial). Input 'me' for self.",
                    ["<itemName>"] = "Item to spawn next to a given player (support partial completion)",
                    ["[value]"] = "Spawn item with specific scrap value (default: 0)",
                    ["[isFake]"] = "Whether to spawn fake item or not (fake items are not interactable by the users). Input 1 for true!",
                },
                callfunc = (t, args) =>
                {
                    if (args.Length < 2)
                    {
                        Print(t, $"Missing args! 2 is required but got {args.Length}");
                        return;
                    }
                    bool isFake = args.Length > 3 && args[3] == "1";
                    string partialPlayerName = args[0].ToLower();
                    string partialItemName = new string(args[1].ToLower().Select(k => (k != '_') ? k : ' ').ToArray());
                    int scrapValue = 0;
                    if (args.Length > 2 && !int.TryParse(args[2], out scrapValue))
                    {
                        Print(t, "Invalid Scrap value set!");
                        return;
                    }
                    PlayerControllerB target = (partialPlayerName == "me") ? GameNetworkManager.Instance.localPlayerController :
                    StartOfRound.Instance.allPlayerScripts.FirstOrDefault(k => k.playerUsername.ToLower().StartsWith(partialPlayerName));
                    if (target == null || !Utils.isPlayerConnected(target))
                    {
                        Print(t, "Target Player Not Found!");
                        return;
                    }
                    if (target.isPlayerDead)
                    {
                        Print(t, "Target Player is already dead!");
                        return;
                    }
                    Item item = StartOfRound.Instance.allItemsList.itemsList.Find(k => k.itemName.ToLower().StartsWith(partialItemName));
                    if (item == null)
                    {
                        Print(t, "Item Not Found!");
                        return;
                    }

                    RPCHandle.Instance.spawnItem(target, item, isFake, scrapValue);
                    Print(t, $"Spawned {(isFake ? "fake" : "real")} item ({item.itemName}) near {target.playerUsername}");

                }
            },
            ["spawnentity"] = new TerminalCmd
            {
                name = "spawnentity",
                description = "Spawn map entity next to a given player",
                argDesc = new Dictionary<string, string>()
                {
                    ["<playerName>"] = "Player username to run the command on (can be partially completed and return first one if multiple shares same partial). Type 'me' for self.",
                    ["<entityName>"] = "Item to spawn next to a given player (support partial completion) OR type 'all' to spawn all possible entity to target",
                    ["[isFake]"] = "Whether to spawn fake entity or not (fake entity will not move/attack players). Input 1 for true!",
                },
                callfunc = (t, args) =>
                {
                    if (!ShipLandState.onMoon)
                    {
                        Print(t, $"Entity spawning only works when you landed the ship to allow game to auto-cleanup entity (and prevent potential undefined behavior lol).");
                        return;
                    }
                    if (args.Length < 2)
                    {
                        Print(t, $"Missing args! 2 is required but got {args.Length}");
                        return;
                    }
                    bool isFake = args.Length > 2 && args[2] == "1";
                    string partialPlayerName = args[0].ToLower();
                    string partialEntityName = new string(args[1].ToLower().Select(k => (k != '_') ? k : ' ').ToArray());
                    PlayerControllerB target = (partialPlayerName == "me") ? GameNetworkManager.Instance.localPlayerController : 
                    StartOfRound.Instance.allPlayerScripts.FirstOrDefault(k => k.playerUsername.ToLower().StartsWith(partialPlayerName));
                    if (target == null || !Utils.isPlayerConnected(target))
                    {
                        Print(t, "Target Player Not Found!");
                        return;
                    }
                    if (target.isPlayerDead)
                    {
                        Print(t, "Target Player is already dead!");
                        return;
                    }
                    switch (partialEntityName)
                    {
                        case "all":
                            {
                                foreach (EnemyType entity in LCSLogic.globalEnemies)
                                    RPCHandle.Instance.spawnEntity(target, entity, isFake);
                                Print(t, $"Spawned {(isFake ? "fake" : "real")} entity (all from the list) near {target.playerUsername}");
                                break;
                            }
                        default:
                            {
                                EnemyType entity = LCSLogic.globalEnemies.FirstOrDefault(k => k.enemyName.ToLower().StartsWith(partialEntityName));
                                if (entity == null)
                                {
                                    Print(t, "Entity Not Found!");
                                    return;
                                }

                                RPCHandle.Instance.spawnEntity(target, entity, isFake);
                                Print(t, $"Spawned {(isFake ? "fake" : "real")} entity ({entity.enemyName}) near {target.playerUsername}");
                                break;
                            }
                    }
                }
            },
            ["spawntrap"] = new TerminalCmd
            {
                name = "spawntrap",
                description = "Spawn indoor trap near the target",
                argDesc = new Dictionary<string, string>()
                {
                    ["<playerName>"] = "Player username to run the command on (can be partially completed and return first one if multiple shares same partial). Input 'me' for self.",
                    ["<trapName>"] = "Trap to spawn next to a given player (support partial completion)",
                    ["[isFake]"] = "Whether to spawn fake trap or not (fake traps are real but do zero damage). Input 1 for true!",
                },
                callfunc = (t, args) =>
                {
                    if (args.Length < 2)
                    {
                        Print(t, $"Missing args! 2 is required but got {args.Length}");
                        return;
                    }
                    if (!ShipLandState.onMoon)
                    {
                        Print(t, $"Trap spawning only works when you landed the ship to allow game to auto-cleanup entity (and prevent potential undefined behavior lol).");
                        return;
                    }
                    bool isFake = args.Length > 2 && args[2] == "1";
                    string partialPlayerName = args[0].ToLower();
                    string partialTrapName = new string(args[1].ToLower().Select(k => (k != '_') ? k : ' ').ToArray());

                    PlayerControllerB? target = (partialPlayerName == "me") ? GameNetworkManager.Instance.localPlayerController :
                    StartOfRound.Instance.allPlayerScripts.FirstOrDefault(k => k.playerUsername.ToLower().StartsWith(partialPlayerName));
                    if (target == null || !Utils.isPlayerConnected(target))
                    {
                        Print(t, "Target Player Not Found!");
                        return;
                    }
                    if (target.isPlayerDead)
                    {
                        Print(t, "Target Player is already dead!");
                        return;
                    }
                    IndoorMapHazardType trap = LCSLogic.globalTraps.FirstOrDefault(k => k.prefabToSpawn.name.ToLower().StartsWith(partialTrapName));
                    if (trap == null)
                    {
                        Print(t, "Trap Not Found!");
                        return;
                    }

                    RPCHandle.Instance.spawnTrap(target, trap, isFake);
                    Print(t, $"Spawned {(isFake ? "fake" : "real")} trap ({trap.prefabToSpawn.name}) near {target.playerUsername}");

                }
            },
            ["cleanup"] = new TerminalCmd
            {
                name = "cleanup",
                description = "Cleanup LCS managed unity objects (such as fake items/entity)",
                callfunc = (t, _) =>
                {
                    RPCHandle.Instance.cleanFakeObjects();
                    Print(t, "Successfully cleaned LCS managed objects!");
                }
            },
            ["setbalance"] = new TerminalCmd
            {
                name = "setbalance",
                description = "Set your spendable shop balance",
                argDesc = new Dictionary<string, string>()
                {
                    ["[balance]"] = "Set a custom balance instead of fixed large value"
                },
                callfunc = (t, args) =>
                {
                    int credit = 999999;
                    if (args.Length > 0 && int.TryParse(args[0], out credit))
                        LCSMain.logger.LogDebug("User Specified setbalance!");

                    if (NetworkManager.Singleton.IsServer)
                        LCSLogic.SetBalance(credit);
                    else
                        RPCHandle.Instance.SetBalanceServerRpc(credit);
                    Print(t, $"Successfully changed credit to {credit}!");

                }
            },
            ["setquota"] = new TerminalCmd
            {
                name = "setquota",
                description = "Change the game quota parameters",
                argDesc = new Dictionary<string, string>()
                {
                    ["<type>"] = "Choose the type of quota to change: time (days to complete quota), cur (Total fulfilled amount), or due (required quota to complete fulfillment)",
                    ["[amount]"] = "Default value is 9999 for cur and due and 100 for time"
                },
                callfunc = (t, args) =>
                {
                    if (args.Length == 0)
                    {
                        Print(t, "Please choose the type of quota to modify");
                        return;
                    }
                    int amount = 0;
                    if (args.Length > 1)
                        int.TryParse(args[1], out amount);

                    switch (args[0])
                    {
                        case "time":
                        case "cur":
                        case "due":
                            break;
                        default:
                            Print(t, "Invalid type of quota, choose: time, cur, or due");
                            return;
                    }

                    RPCHandle.Instance.changeQuota(args[0], amount);
                    Print(t, "Successfully Change the quota");
                }
            },
            ["setplrstat"] = new TerminalCmd
            {
                name = "setplrstat",
                description = "Change player control stats",
                argDesc = new Dictionary<string, string>()
                {
                    ["<type>"] = "Type of stats to change: health, speed, jmpheight (Jump Power), or reset (reset all stats to default)",
                    ["<target>"] = "Player to take action on (support partial completion). Input 'me' for self.",
                    ["[value]"] = "Customize the stat value (default value: 100 for health, 5 for speed, and 13 jumpheight)"
                },
                callfunc = (t,args) =>
                {
                    
                    if (args.Length < 2)
                    {
                        Print(t, $"Require 2 args, received: {args.Length}. Refer to help for usages!");
                        return;
                    }
                    string type = args[0].ToLower();
                    string partialPlayerUsername = args[1].ToLower();
                    int val = 0;

                    if (args.Length > 2 && !int.TryParse(args[2], out val))
                    {
                        Print(t, "Non-numerical [value] arg provided. Try again!");
                        return;
                    }
                    switch (type)
                    {
                        case "health":
                            val = val > 0 ? val : 100;
                            break;
                        case "speed":
                            val = val > 0 ? val : 5;
                            break;
                        case "jmpheight":
                            val = val > 0 ? val : 13;
                            break;
                        case "reset":
                            val = 1;
                            break;
                        default:
                            Print(t, "Invalid stat change type, choose: health, speed, or jmpheight");
                            break;
                    }

                    PlayerControllerB target = (partialPlayerUsername == "me") ? GameNetworkManager.Instance.localPlayerController :
                    StartOfRound.Instance.allPlayerScripts.FirstOrDefault(k => k.playerUsername.ToLower().StartsWith(partialPlayerUsername));
                    if(target == null)
                    {
                        Print(t, "Your selected target player is not found!");
                        return;
                    }
                    PlayerManager.Instance.setPlayerStat(target, type, val);
                    Print(t, $"Set player's ({target.playerUsername}) {type} to {val}!");
                }
            },
            ["sendmsg"] = new TerminalCmd
            {
                name = "sendmsg",
                description = "Send Custom message to player!",
                argDesc = new Dictionary<string, string>()
                {
                    ["<players>"] = "List of players to send the message to. Use commas to include multiple players. Type _ for default (all players)",
                    ["<header>"] = "Notification Header, type _ for default (Warning)",
                    ["<...message>"] = "The remaining args will be used for message content"
                },
                callfunc = (t,args) =>
                {
                    if(args.Length < 3)
                    {
                        Print(t, $"Required 3 args. Received: {args.Length}. Please refer to help for more information");
                        return;
                    }

                    string players = args[0];
                    string header = args[1] != "_" ? args[1] : "Warning";
                    string message = args.Skip(2).Join(c=>c, " ");

                    if(players == "_")
                    {
                        ulong[] allPlayers = StartOfRound.Instance.allPlayerScripts
                        .Where(k=> Utils.isPlayerConnected(k))
                        .Select(k=>k.playerClientId)
                        .ToArray();
                        // LCSMain.logger.LogDebug($"Terminal Player ID Found: {allPlayers.Join(k=>k.ToString(),", ")}");
                        PlayerManager.Instance.requestPlrMessageServerRpc(allPlayers, header, message);
                        Print(t, "Successfully send all player a message!");
                        return;
                    }

                    HashSet<PlayerControllerB> SelectedPlayers = new HashSet<PlayerControllerB>();
                    foreach(string plr in players.Split(","))
                    {
                        PlayerControllerB? plrControl = StartOfRound.Instance.allPlayerScripts.FirstOrDefault(k => k.playerUsername.ToLower().StartsWith(plr));
                        if(plrControl != null)
                            SelectedPlayers.Add(plrControl);
                    }
                    PlayerManager.Instance.requestPlrMessageServerRpc(SelectedPlayers.Select(k=>k.playerClientId).ToArray(), header, message);
                    Print(t, $"Successfully send {SelectedPlayers.Join(k => k.playerUsername, ", ")} messages!");
                    

                }
            },
            ["tp"] = new TerminalCmd
            {
                name = "tp",
                description = "Teleport players in the same region",
                argDesc = new Dictionary<string, string>()
                {
                    ["<from>"] = "Player to be teleported (support partial completion and 'me' for self)",
                    ["<to>"] = "Player to be teleported to (support partial completion and 'me' for self)"
                },
                callfunc =(t, args) =>
                {
                    if(args.Length < 2)
                    {
                        Print(t, $"Required 2 args got {args.Length}");
                        return;
                    }
                    string partialPlayerAName = args[0];
                    string partialPlayerBName = args[1];
                    PlayerControllerB PlayerA = (partialPlayerAName == "me") ? GameNetworkManager.Instance.localPlayerController :
                    StartOfRound.Instance.allPlayerScripts.FirstOrDefault(k => k.playerUsername.ToLower().StartsWith(partialPlayerAName));
                    PlayerControllerB PlayerB = (partialPlayerBName == "me") ? GameNetworkManager.Instance.localPlayerController :
                    StartOfRound.Instance.allPlayerScripts.FirstOrDefault(k => k.playerUsername.ToLower().StartsWith(partialPlayerBName));

                    if(PlayerA.playerClientId == GameNetworkManager.Instance.localPlayerController.playerClientId)
                    {
                        Print(t, "You cant teleport yourself to others because you're on the terminal :(");
                        return;
                    }
                    if(PlayerA == null ||  PlayerB == null)
                    {
                        Print(t, "");
                        if (PlayerA == null)
                            Print(t, $"{partialPlayerAName} partial not found", false);
                        if (PlayerB == null)
                            Print(t, $"{partialPlayerBName} partial not found", false);
                        return;
                    }

                    string? validation = PlayerManager.Instance.tpPlayerValidation(PlayerA, PlayerB);
                    if(validation != null)
                    {
                        Print(t, validation);
                        return;
                    }

                    PlayerManager.Instance.teleportPlayer(PlayerA, PlayerB);
                    Print(t, $"Successfully teleported {PlayerA.playerUsername} to {PlayerB.playerUsername}!");
                }
            },
            ["kill"] = new TerminalCmd
            {
                name = "kill",
                description = "kill a target player",
                argDesc = new Dictionary<string, string>()
                {
                    ["<Player>"] = "Target player to kill (support partial completion and type 'me' for self)"
                },
                callfunc = (t,args) =>
                {
                    if(args.Length < 1)
                    {
                        Print(t, "You need to specify the player you want to kill");
                        return;
                    }
                    if (!ShipLandState.onMoon)
                    {
                        Print(t, "Game will only allow you to kill players when you landed the ship!");
                        return;
                    }
                    string partialPlayerName = args[0];
                    PlayerControllerB Player = (partialPlayerName == "me") ? GameNetworkManager.Instance.localPlayerController :
                    StartOfRound.Instance.allPlayerScripts.FirstOrDefault(k => k.playerUsername.ToLower().StartsWith(partialPlayerName));

                    if(Player == null)
                    {
                        Print(t, "Player not found!");
                        return;
                    }
                    if(Player.isPlayerDead)
                    {
                        Print(t, "Cannot kill a dead player!");
                        return;
                    }
                    PlayerManager.Instance.killPlayer(Player);
                    Print(t, "Player successfully killed!");
                }
            }
        };

        public string name;
        public string description;
        public Dictionary<string, string>? argDesc; // key: argument name, value: argument description
        public Action<Terminal, string[]> callfunc; // Terminal Instance + args array

        private TerminalCmd() {}
        public static void Print(Terminal terminal, string text, bool cleartext = true, bool logPrint = true)
        {
            TerminalNode node = ScriptableObject.CreateInstance<TerminalNode>();
            node.displayText = text + "\n";
            node.clearPreviousText = cleartext;
            terminal.LoadNewNode(node);
            if(logPrint)
                LCSMain.logger.LogDebug($"[Term Out]: {text}");
        }
    }
}
