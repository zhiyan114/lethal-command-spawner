using BepInEx.Logging;
using GameNetcodeStuff;
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace LethalChatSpawner
{
    public static class Utils
    {
        public static bool isPlayerConnected(PlayerControllerB? player)
        {
            if (player == null)
                return false;
            if (NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.ConnectedClients.ContainsKey(player.NetworkObject.OwnerClientId))
                return false;
            if (player.NetworkObject == null || !player.NetworkObject.IsSpawned)
                return false;
            if (player.deadBody == null && !player.isPlayerControlled)
                return false;
            if (player.disconnectedMidGame)
                return false;
            return true;
        }
        public static Vector3 SearchBestPosition(PlayerControllerB player, GameObject prefab, float radius = 5f)
        {
            Vector3 bestPos = player.transform.position;
            float bestScore = float.MaxValue;

            // Determine object size
            float objectRadius = 1f;
            float objectHeight = 2f;

            NavMeshAgent agent = prefab.GetComponentInChildren<NavMeshAgent>();
            if (agent != null)
            {
                objectRadius = agent.radius;
                objectHeight = agent.height;
            }
            else
            {
                Collider col = prefab.GetComponentInChildren<Collider>();
                if (col != null)
                {
                    Bounds bounds = col.bounds;
                    objectRadius = Mathf.Max(bounds.extents.x, bounds.extents.z);
                    objectHeight = bounds.size.y;
                }
            }

            for (int i = 0; i < 50; i++)
            {
                Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * radius;

                Vector3 candidate = new Vector3(
                    player.transform.position.x + randomCircle.x,
                    player.transform.position.y,
                    player.transform.position.z + randomCircle.y
                );

                if (!NavMesh.SamplePosition(
                        candidate,
                        out NavMeshHit navHit,
                        10f,
                        NavMesh.AllAreas))
                    continue;

                if (!Physics.Raycast(
                        navHit.position + Vector3.up * 5f,
                        Vector3.down,
                        out RaycastHit groundHit,
                        20f))
                    continue;

                Vector3 spawnPos = groundHit.point;

                // Reject positions where the object would intersect geometry
                bool blocked = Physics.CheckCapsule(
                    spawnPos + Vector3.up * objectRadius,
                    spawnPos + Vector3.up * Mathf.Max(objectRadius, objectHeight - objectRadius),
                    objectRadius
                );

                if (blocked)
                    continue;

                // Ensure path exists
                NavMeshPath path = new NavMeshPath();
                if (!NavMesh.CalculatePath(
                        spawnPos,
                        player.transform.position,
                        NavMesh.AllAreas,
                        path))
                    continue;

                if (path.status != NavMeshPathStatus.PathComplete)
                    continue;

                float yDifference =
                    Mathf.Abs(spawnPos.y - player.transform.position.y);

                float distance =
                    Vector3.Distance(spawnPos, player.transform.position);

                float distancePenalty =
                    Mathf.Abs(distance - radius);

                float score =
                    yDifference * 10f +
                    distancePenalty;

                if (score < bestScore)
                {
                    bestScore = score;
                    bestPos = spawnPos;
                }
            }

            return bestPos;
        }
    }
#if !DEBUG && !TESTPLAY
    // For thunderstore release and support sentry logging for development purpose without breaking compatibility
    public class LoggerWarpper: ManualLogSource
    {
        public LoggerWarpper(string modName) : base(modName) { }

        public new void LogDebug(object data)
        {
            base.LogDebug(data);
        }
        public void LogDebug(object data, Dictionary<string, object> attributes)
        {
            base.LogDebug(data);
        }
        public void LogInfo(object data, Dictionary<string, object> attributes)
        {
            base.LogInfo(data);
        }
        public void LogWarning(object data, Dictionary<string, object> attributes)
        {
            base.LogWarning(data);
        }
        public void LogError(object data, Dictionary<string, object> attributes)
        {
            base.LogError(data);
        }
        public void LogFatal(object data, Dictionary<string, object> attributes)
        {
            base.LogFatal(data);
        }

    }
#endif
}
