using GameNetcodeStuff;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Unity.Netcode;
using UnityEngine;

namespace LethalChatSpawner
{
    public class FakeObject: NetworkBehaviour
    {
        public NetworkVariable<bool> isFake = new NetworkVariable<bool>(false);

        public bool isReal()
        {
            return !isFake.Value;
        }
    }

    [HarmonyPatch(typeof(Landmine))]
    public class LandMineHook
    {
        [HarmonyPatch(nameof(Landmine.Detonate))]
        [HarmonyPrefix]
        public static bool FakeStatePatch(Landmine __instance)
        {
            FakeObject fakeState = __instance.GetComponentInParent<FakeObject>();
            if (fakeState == null || !fakeState.isFake.Value)
                return true;
            __instance.mineAudio.pitch = UnityEngine.Random.Range(0.93f, 1.07f);
            __instance.mineAudio.PlayOneShot(__instance.mineDetonate, 1f);
            Landmine.SpawnExplosion(__instance.transform.position + Vector3.up, spawnExplosionEffect: true, 0f, 0f);
            LCSMain.logger.LogDebug($"Landmine Fake State: true (triggered)");
            return false;
        }
    }

    [HarmonyPatch(typeof(SpikeRoofTrap))]
    class SpikeTrapHook
    {
        [HarmonyPatch(nameof(SpikeRoofTrap.OnTriggerStay))]
        [HarmonyPrefix]
        public static bool FakeStatePatch(SpikeRoofTrap __instance)
        {
            FakeObject fakeState = __instance.GetComponentInParent<FakeObject>();
            return fakeState == null || !fakeState.isFake.Value;
        }
    }

    [HarmonyPatch(typeof(Turret))]
    class TurretTrapHook
    {

        [HarmonyPatch(nameof(Turret.CheckForPlayersInLineOfSight))]
        [HarmonyPostfix]
        static void FakeStatePatch(Turret __instance, float radius, ref PlayerControllerB? __result)
        {
            if (radius != 3f || __result == null)
                return;
            if (__instance.GetComponentInParent<FakeObject>().isFake.Value)
                __result = null;
        }
    }
}
