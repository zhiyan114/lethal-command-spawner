using GameNetcodeStuff;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Unity.Netcode;
using UnityEngine;

namespace LethalChatSpawner
{
    public class PlayerManager : NetworkBehaviour
    {
        public static PlayerManager? Instance { get; private set; }
        private void Awake()
        {
            PlayerManager.Instance = this;
        }

        private void _updatePlayerStat(PlayerControllerB target, string type, int val)
        {
            switch (type)
            {
                case "health":
                    target.health = val;
                    break;
                case "speed":
                    target.movementSpeed = val;
                    break;
                case "jmpheight":
                    target.jumpForce = val;
                    break;
                case "reset":
                    target.health = 100;
                    target.movementSpeed = 5;
                    target.jumpForce = 13;
                    break;
            }
        }
        private void _setPlayerStat(PlayerControllerB target, string type, int val)
        {
            if (!NetworkManager.Singleton.IsServer) return;

            _updatePlayerStat(target, type, val);
            syncStatClientRpc(target.playerClientId, type, val);
            LCSMain.logger.LogInfo("Changed Player Stat Successfully!", new Dictionary<string, object>()
            {
                ["target"] = target.playerUsername,
                ["type"] = type,
                ["value"] = val,
            });
        }
        public void setPlayerStat(PlayerControllerB target, string type, int val)
        {
            if (NetworkManager.Singleton.IsServer)
            {
                _setPlayerStat(target, type, val);
                return;
            }

            setPlayerStatServerRpc(target.playerClientId, type, val);
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void setPlayerStatServerRpc(ulong playerid, string type, int val)
        {
            PlayerControllerB? target = StartOfRound.Instance.allPlayerScripts.FirstOrDefault(k => k.playerClientId == playerid);
            if (target == null)
            {
                LCSMain.logger.LogWarning("setPlayerStatServerRpc: Attempt to match playerid, but player not found!");
                return;
            }
            _setPlayerStat(target, type, val);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void syncStatClientRpc(ulong playerid, string type, int val)
        {
            PlayerControllerB? target = StartOfRound.Instance.allPlayerScripts.FirstOrDefault(k => k.playerClientId == playerid);
            if (target == null)
            {
                LCSMain.logger.LogWarning("syncStatClientRpc: Attempt to match playerid, but player not found!");
                return;
            }

            _updatePlayerStat(target, type, val);
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void requestPlrMessageServerRpc(ulong[] playerid, string header, string message, RpcParams param = default)
        {
            if(playerid.Length == 0)
            {
                LCSMain.logger.LogWarning("requestPlrMessageServerRpc: No playerid are supplied!");
                return;
            }
            PlayerControllerB[] players = StartOfRound.Instance.allPlayerScripts;
            // LCSMain.logger.LogDebug($"Server Player ID Received: {playerid.Join(k => k.ToString(), ", ")} || Actual: {players.Where(k=>k.isPlayerControlled).Join(k=>k.actualClientId.ToString(), ", ")}");

            PlayerControllerB[] targets = players
                .Where(k => playerid.Contains(k.playerClientId))
                .ToArray();
                
            if (targets.Count() == 0)
            {
                LCSMain.logger.LogWarning("requestPlrMessageServerRpc: No Player are found!");
                return;
            }

            receivePlrMessageClientRpc(header, message, RpcTarget.Group(targets.Select(k => k.NetworkObject.OwnerClientId).ToArray(), RpcTargetUse.Temp));
            string sender = players.FirstOrDefault(k => k.NetworkObject.OwnerClientId == param.Receive.SenderClientId).playerUsername;
            string targetNames = targets.Join(k => k.playerUsername, ", ");
            LCSMain.logger.LogInfo($"{sender} sent {targetNames} messages", new Dictionary<string, object>()
            {
                ["header"] = header,
                ["message"] = message,
            });
        }

        [Rpc(SendTo.SpecifiedInParams)]
        public void receivePlrMessageClientRpc(string header, string message, RpcParams param = default)
        {
            HUDManager.Instance.DisplayTip(header, message);
        }

        private void _teleportPlayer(PlayerControllerB playerA, PlayerControllerB playerB)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            playerA.TeleportPlayer(playerB.transform.position);
            syncTpPlayerClientRpc(playerA.playerClientId, playerB.playerClientId);
            LCSMain.logger.LogInfo($"Server teleported {playerA.playerUsername} to {playerB.playerUsername}!");
        }

        public void teleportPlayer(PlayerControllerB playerA, PlayerControllerB playerB)
        {
            if(NetworkManager.Singleton.IsServer)
            {
                _teleportPlayer(playerA, playerB);
                return;
            }
            teleportPlayerServerRpc(playerA.playerClientId, playerB.playerClientId);
        }

        public string? tpPlayerValidation(PlayerControllerB playerA, PlayerControllerB playerB)
        {
            if (playerA.playerClientId == playerB.playerClientId)
                return $"You cannot teleport a player to themselves";
            if (playerA.isInsideFactory != playerB.isInsideFactory)
                return $"{playerA.playerUsername} and {playerA.playerUsername} are not in the same area (in-door plr can only tp to in-door plr and vice versa)";
            if (!Utils.isPlayerConnected(playerA) || !Utils.isPlayerConnected(playerB))
                return $"{playerA.playerUsername} or {playerB.playerUsername} may be disconnected from the game during tp process";
            if (playerA.isPlayerDead || playerB.isPlayerDead)
                return $"You cannot teleport (to) dead players {playerA.playerUsername}:{playerA.isPlayerDead} or {playerB.playerUsername}:{playerB.isPlayerDead}";
            return null;
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void teleportPlayerServerRpc(ulong playeridA, ulong playeridB)
        {
            PlayerControllerB playerA = StartOfRound.Instance.allPlayerScripts.FirstOrDefault(k=>k.playerClientId == playeridA);
            PlayerControllerB playerB = StartOfRound.Instance.allPlayerScripts.FirstOrDefault(k => k.playerClientId == playeridB);

            if(playerA == null || playerB == null)
            {
                LCSMain.logger.LogWarning($"Either targets are not found -> A: {playerA} | B: {playerB}");
                return;
            }

            string? tpStatus = tpPlayerValidation(playerA, playerB);
            if(tpStatus != null)
            {
                LCSMain.logger.LogWarning($"Player Teleportation Failed -> {tpStatus}");
                return;
            }
            _teleportPlayer(playerA, playerB);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void syncTpPlayerClientRpc(ulong playeridA, ulong playeridB)
        {
            PlayerControllerB playerA = StartOfRound.Instance.allPlayerScripts.FirstOrDefault(k => k.playerClientId == playeridA);
            PlayerControllerB playerB = StartOfRound.Instance.allPlayerScripts.FirstOrDefault(k => k.playerClientId == playeridB);
            playerA.TeleportPlayer(playerB.transform.position);
        }

        private void _killPlayer(PlayerControllerB player)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            player.KillPlayer(UnityEngine.Vector3.zero, true);
            syncKillPlayerClientRpc(player.playerClientId);
            LCSMain.logger.LogInfo($"Killed player {player.playerUsername}!");
        }
        public void killPlayer(PlayerControllerB player)
        {
            if (NetworkManager.Singleton.IsServer)
            {
                _killPlayer(player);
                return;
            }
            killPlayerServerRPC(player.playerClientId);
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void killPlayerServerRPC(ulong playeridA)
        {
            PlayerControllerB player = StartOfRound.Instance.allPlayerScripts.FirstOrDefault(k => k.playerClientId == playeridA);

            if (player == null)
            {
                LCSMain.logger.LogWarning($"Player not found!");
                return;
            }
            if(player.isPlayerDead)
            {
                LCSMain.logger.LogWarning($"Cannot kill dead player!");
                return;
            }
            _killPlayer(player);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void syncKillPlayerClientRpc(ulong playerid)
        {
            PlayerControllerB player = StartOfRound.Instance.allPlayerScripts.FirstOrDefault(k => k.playerClientId == playerid);
            player.KillPlayer(UnityEngine.Vector3.zero, true);
        }
    }
}
