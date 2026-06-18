using GameNetcodeStuff;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

namespace LethalChatSpawner
{
    public class RPCHandle : NetworkBehaviour
    {
        public static RPCHandle? Instance { get; private set; }

        private void Awake()
        {
            RPCHandle.Instance = this;
        }

        public void cleanFakeObjects()
        {
            if (NetworkManager.Singleton.IsServer)
            {
                LCSLogic.cleanFakeObjects();
                return;
            }
            cleanFakeObjectsServerRpc();
        }

        [Rpc(SendTo.Server, RequireOwnership = false, Delivery = RpcDelivery.Reliable)]
        public void cleanFakeObjectsServerRpc()
        { LCSLogic.cleanFakeObjects(); }

        public void spawnItem(PlayerControllerB player, Item item, bool isFake = false, int price = 0)
        {
            if (NetworkManager.Singleton.IsServer)
            {
                LCSLogic.spawnItem(player, item, isFake, price);
                return;
            }
            spawnItemServerRpc(player.playerClientId, item.itemName, isFake, price);
        }

        [Rpc(SendTo.Server, RequireOwnership = false, Delivery = RpcDelivery.Reliable)]
        private void spawnItemServerRpc(ulong playerID, string itemName, bool isFake, int price)
        {
            PlayerControllerB player = StartOfRound.Instance.allPlayerScripts.FirstOrDefault(k => k.playerClientId == playerID);
            if (!player)
            {
                LCSMain.logger.LogWarning($"Player {player.playerUsername} left before spawnItemRPC processes them!");
                return;
            }
            Item item = StartOfRound.Instance.allItemsList.itemsList.Find(k => k.itemName == itemName);
            LCSLogic.spawnItem(player, item, isFake, price);
        }

        public void spawnEntity(PlayerControllerB player, EnemyType enemy, bool isFake = false)
        {
            if (NetworkManager.Singleton.IsServer)
            {
                LCSLogic.spawnEntity(player, enemy, isFake);
                return;
            }
            spawnEntityServerRpc(player.playerClientId, enemy.enemyName, isFake);
        }

        [Rpc(SendTo.Server, RequireOwnership = false, Delivery = RpcDelivery.Reliable)]
        private void spawnEntityServerRpc(ulong playerID, string entityName, bool isFake)
        {
            PlayerControllerB player = StartOfRound.Instance.allPlayerScripts.FirstOrDefault(k => k.playerClientId == playerID);
            if (!player)
            {
                LCSMain.logger.LogWarning($"Player {player.playerUsername} left before spawnEntityRPC processes them!");
                return;
            }
            EnemyType enemy = LCSLogic.globalEnemies.FirstOrDefault(k => k.enemyName == entityName);
            LCSLogic.spawnEntity(player, enemy, isFake);
        }

        [Rpc(SendTo.ClientsAndHost, DeferLocal = true)]
        public void syncFakeEntityClientRpc(ulong netObjId)
        {
            NetworkObject netObj = NetworkManager.Singleton.SpawnManager.SpawnedObjects.GetValueOrDefault(netObjId);
            UnityEngine.Object.Destroy(netObj.GetComponentInParent<EnemyAI>());
        }

        public void spawnTrap(PlayerControllerB player, IndoorMapHazardType trap, bool isFake = false)
        {
            if (NetworkManager.Singleton.IsServer)
            {
                LCSLogic.spawnTrap(player, trap, isFake);
                return;
            }
            spawnTrapServerRpc(player.playerClientId, trap.prefabToSpawn.name, isFake);
        }

        [Rpc(SendTo.Server, RequireOwnership = false, Delivery = RpcDelivery.Reliable)]
        private void spawnTrapServerRpc(ulong playerID, string trapName, bool isFake)
        {
            PlayerControllerB player = StartOfRound.Instance.allPlayerScripts.FirstOrDefault(k => k.playerClientId == playerID);
            if (!player)
            {
                LCSMain.logger.LogWarning($"Player {player.playerUsername} left before spawnTrapServerRpc processes them!");
                return;
            }
            IndoorMapHazardType trap = LCSLogic.globalTraps.FirstOrDefault(k => k.prefabToSpawn.name == trapName);
            LCSLogic.spawnTrap(player, trap, isFake);
        }

        [Rpc(SendTo.Server, RequireOwnership = false, Delivery = RpcDelivery.Reliable)]
        public void SetBalanceServerRpc(int val)
        {
            LCSLogic.SetBalance(val);
        }
        public void changeQuota(string type, int val = -1)
        {
            if (NetworkManager.Singleton.IsServer)
            {
                LCSLogic.changeQuota(type, val);
                return;
            }
            changeQuotaServerRpc(type, val);
        }

        [Rpc(SendTo.Server, RequireOwnership = false, Delivery = RpcDelivery.Reliable)]
        private void changeQuotaServerRpc(string type, int val)
        {
            LCSLogic.changeQuota(type, val);
        }

        [Rpc(SendTo.ClientsAndHost)]
        public void syncQuotaClientRpc(int Fulfilled, int reqQuota, float time)
        {
            TimeOfDay ToDInstance = TimeOfDay.Instance;
            // ToDInstance.quotaVariables.deadlineDaysAmount = time;
            ToDInstance.timeUntilDeadline = time;
            ToDInstance.quotaFulfilled = Fulfilled;
            ToDInstance.profitQuota = reqQuota;
            ToDInstance.UpdateProfitQuotaCurrentTime();
            ToDInstance.SetBuyingRateForDay();
        }
    }

    public class PingUtil : NetworkBehaviour
    {
        public static PingUtil? Instance { get; private set; }

        private void Awake()
        {
            PingUtil.Instance = this;
        }

        [Rpc(SendTo.Server, RequireOwnership = false, Delivery = RpcDelivery.Reliable)]
        public void pingServerRpc(long timestamp, RpcParams rpcParams = default)
        {
            pongClientRpc(timestamp, RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, Delivery = RpcDelivery.Reliable)]
        public void pongClientRpc(long timestamp, RpcParams rpcParams = default)
        {
            Terminal t = UnityEngine.Object.FindObjectOfType<Terminal>();
            TerminalCmd.Print(t, $"Done, took: {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - timestamp}ms!", false);
        }
    }
}
