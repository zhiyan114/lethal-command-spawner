using BepInEx;
#if !DEBUG && !TESTPLAY
using BepInEx.Logging;
#endif
using GameNetcodeStuff;
using HarmonyLib;
#if DEBUG || TESTPLAY
using SentryUtils;
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Unity.Netcode;
using UnityEngine;

namespace LethalChatSpawner
{

    [BepInPlugin(modGUID, modName, modVersion)]
    public class LCSMain: BaseUnityPlugin
    {
        public const string modGUID = "FurryNet.lethalCommService";
        public const string modName = "lethalCommService";
        public const string modVersion = "1.0.3";
#if DEBUG
        public const string environment = "dev";
        public const string sentryDSN = null;
        public const string sentrySpotlight = "http://localhost:8969/stream";
#elif TESTPLAY
        public const string environment = "prod";
        //public const string sentryDSN = "https://e9f37f809b74965305d2b6238d290d2c@o4504678874021888.ingest.us.sentry.io/4511095694163968";
        public const string sentryDSN = "https://0a9ce62339978190df10f9dcf2cbf599@o125145.ingest.us.sentry.io/4511562379493376";
        public const string sentrySpotlight = null;
#endif

#if DEBUG || TESTPLAY
        public static readonly string SessionID = Guid.NewGuid().ToString();
        public static readonly SentryLogger logger = SentryLogger.Initialize(modGUID, sentryDSN, SessionID, sentrySpotlight);
#else
        public static readonly LoggerWarpper logger = new LoggerWarpper(modGUID);
#endif
        private static readonly Harmony harmony = new Harmony(modGUID);

        // Internal Objects
        public static GameObject netPrefab;

        void Awake()
        {
            // Regular Patcher
            var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in types)
            {
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (var method in methods)
                {
                    var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                    if (attributes.Length > 0)
                        method.Invoke(null, null);
                }
            }

            // Setup RPCHandle Prefab
            logger.LogMessage("RPCHandle Object Created!");
            AssetBundle NetCodeModBundle = AssetBundle.LoadFromMemory(Properties.Resources.netcodemod);
            netPrefab = NetCodeModBundle.LoadAsset<GameObject>("Assets/InternalAssets/NetPrefab.prefab");
            netPrefab.AddComponent<RPCHandle>();
            netPrefab.AddComponent<PingUtil>();
            netPrefab.AddComponent<PlayerManager>();

            //InitNetworkBehaviour(typeof(RPCHandle));
            //InitNetworkBehaviour(typeof(PingUtil));
            //InitNetworkBehaviour(typeof(PlayerManager));
            //InitNetworkBehaviour(typeof(FakeObject));


            // Patch Game
            logger.LogMessage(modName + " Loaded...");
            harmony.PatchAll();

            //// Hook Keybind x (for portable terminal)
            //InputAction terminalKeyBind = new InputAction(type: InputActionType.Button, binding: "<Keyboard>/x");
            //terminalKeyBind.performed += _ =>
            //{
            //    if(!ShipLandState.loaded)
            //    {
            //        logger.LogWarning("Attempt to access ship terminal without starting game session!");
            //        return;
            //    }
            //    Terminal terminal = UnityEngine.Object.FindObjectOfType<Terminal>();
            //    if (terminal == null)
            //    {
            //        logger.LogWarning("Terminal not found!");
            //        return;
            //    }
            //    terminal.BeginUsingTerminal();
            //    logger.LogInfo("Terminal Launched!");
            //};
            //terminalKeyBind.Enable();
        }

        /**
         * Call this on every existing NetworkBehavior class
         */
        private void InitNetworkBehaviour(Type type)
        {
            var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            foreach (var method in methods)
            {
                var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                if (attributes.Length == 0) continue;
                method.Invoke(null, null);
            }
        }
    }

    [HarmonyPatch(typeof(StartOfRound))]
    public class ShipLandState
    {
        private static bool _onMoon = false;
        private static bool _loaded = false;
        public static bool onMoon { get { return _onMoon; }}
        public static bool loaded { get { return _loaded; }}

        [HarmonyPatch("OnShipLandedMiscEvents")]
        [HarmonyPrefix]
        public static void shipLanded()
        {
            _onMoon = true;
            LCSMain.logger.LogInfo("Player Land On Moon");

        }

        [HarmonyPatch("ShipHasLeft")]
        [HarmonyPrefix]
        public static void shipLeft()
        {
            _onMoon = false;
            RPCHandle.Instance?.cleanFakeObjects();
            LCSMain.logger.LogInfo("Player Left Moon");
        }

        [HarmonyPatch("Start")]
        [HarmonyPrefix]
        public static void saveLoaded()
        {
            // Setup logic list
            LCSLogic.syncInternalSet();

            // Init RPCHandle Prefab
            if (NetworkManager.Singleton.IsServer)
                GameObject.Instantiate(LCSMain.netPrefab).GetComponent<NetworkObject>().Spawn();

            LCSMain.logger.LogInfo("Mod Initialized...");
            _loaded = true;
        }
    }

    [HarmonyPatch(typeof(GameNetworkManager))]
    public class NetworkHook
    {
        [HarmonyPatch("Start")]
        [HarmonyPostfix]
        public static void NetStart(GameNetworkManager __instance)
        {
            // Load Network Prefab
            LCSMain.logger.LogInfo("Loading netPrefab into NetworkManager");
            __instance.GetComponent<NetworkManager>().AddNetworkPrefab(LCSMain.netPrefab);

        }
    }

    [HarmonyPatch(typeof(Terminal))]
    public class TerminalHook
    {
        public static Terminal instance;
        [HarmonyPatch("Awake")]
        [HarmonyPostfix]
        public static void getTerminal(Terminal __instance)
        {
            TerminalHook.instance = __instance;
        }
        [HarmonyPatch("ParsePlayerSentence")]
        [HarmonyPrefix]
        public static bool TerminalHandle(Terminal __instance)
        {
            //MethodInfo RemovePunctuation = AccessTools.Method(typeof(Terminal), "RemovePunctuation");
            //string s = (string)RemovePunctuation.Invoke(__instance, new object[] { __instance.screenText.text.Substring(__instance.screenText.text.Length - __instance.textAdded) });
            // Preserve underscore punct to allow search for spaced entity name
            char[] whitelistPunc = { '_', ',' };
            string s = new string(__instance.screenText.text.Substring(__instance.screenText.text.Length - __instance.textAdded).Where(k => !char.IsPunctuation(k) || whitelistPunc.Contains(k)).ToArray());
            string[] args = s.ToLower().Split(" ", StringSplitOptions.RemoveEmptyEntries);

            // Prefix/Length Checks
            if (args.Length < 1)
                return true;
            if (args[0] != "lcs")
                return true;
            if (args.Length < 2)
            {
                string output = TerminalCmd.lists.Values.Join(c =>
                {
                    if (c.argDesc?.Count > 0)
                        return $"{c.name} {c.argDesc.Join(p => p.Key, " ")}";
                    return c.name;
                }, "\n");
                TerminalCmd.Print(__instance, $"All Available Commands:\n{output}");
                return false;
            }

            // Find and execute commands
            TerminalCmd cmd;
            if (!TerminalCmd.lists.TryGetValue(args[1], out cmd))
            {
                TerminalCmd.Print(__instance, $"lcs: Bad command :(");
                LCSMain.logger.LogWarning($"{args[1]} is not a valid command");
                return false;
            }

            string[] cmd_args = args.Skip(2).ToArray();
            cmd.callfunc(__instance, cmd_args);
            LCSMain.logger.LogInfo($"Successfully execute command: {args[1]}", new Dictionary<string, object>
            {
                ["args"] = cmd_args.Join(k => k, ","),
            });
            return false;
        }

        [HarmonyPatch("TextChanged")]
        [HarmonyPrefix]
        public static bool BypassLimit(Terminal __instance)
        {
            if (__instance.currentNode != null)
                __instance.currentNode.maxCharactersToType = 100;
            return true;
        }
    }

    [HarmonyPatch(typeof(PlayerControllerB))]
    public class PlayerControllerHook
    {
        [HarmonyPatch(nameof(PlayerControllerB.DamagePlayer))]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            var clampMethod = typeof(Mathf).GetMethod(
                "Clamp",
                new[] { typeof(int), typeof(int), typeof(int) }
            );

            for (int i = 0; i < codes.Count; i++)
            {
                // Find instruction with clamp call
                if (codes[i].Calls(clampMethod))
                {
                    // Remove Clamp call
                    codes[i] = new CodeInstruction(OpCodes.Nop);

                    // Remove the two constants BEFORE it (0 and 100)
                    // (safe because this pattern is stable in Unity C# compilation)
                    if (i - 1 >= 0) codes[i - 1] = new CodeInstruction(OpCodes.Nop);
                    if (i - 2 >= 0) codes[i - 2] = new CodeInstruction(OpCodes.Nop);
                    break;
                }
            }
            return codes;

        }
    }

    public class LCSLogic
    {
        public static HashSet<EnemyType> globalEnemies = new HashSet<EnemyType>();
        public static HashSet<IndoorMapHazardType> globalTraps = new HashSet<IndoorMapHazardType>();
        private static List<GameObject> fakeItems = new List<GameObject>();
        public static int FakeItemsCount {  get { return fakeItems.Count; } }

        public static void syncInternalSet()
        {
            foreach (EnemyType enemyType in Resources.FindObjectsOfTypeAll<EnemyType>())
                globalEnemies.Add(enemyType);
            foreach(IndoorMapHazardType traps in Resources.FindObjectsOfTypeAll<IndoorMapHazardType>())
            {
                // Patch Trap with custom Net Object
                traps.prefabToSpawn.AddComponent<FakeObject>();
                globalTraps.Add(traps);
                
            }
            LCSMain.logger.LogInfo("All traps prefab patched with FakeObject class!");
            LCSMain.logger.LogInfo("Enemy/Trap set has been successfully synced with the global state!");
        }
        public static void cleanFakeObjects()
        {
            if (!NetworkManager.Singleton.IsServer)
                return;
            if (fakeItems.Count == 0)
                return;
            foreach (GameObject obj in fakeItems)
                obj.GetComponent<NetworkObject>().Despawn(true);
            LCSMain.logger.LogInfo($"Cleaned up {fakeItems.Count} managed objects!");
            fakeItems.Clear();
        }

        public static void spawnItem(PlayerControllerB player, Item item, bool isFake = false, int price = 0)
        {
            if (!NetworkManager.Singleton.IsServer)
                return;

            GameObject obj = UnityEngine.Object.Instantiate(item.spawnPrefab, Utils.SearchBestPosition(player, item.spawnPrefab, 1f), Quaternion.identity);
            if(isFake)
            {
                foreach (GrabbableObject g in obj.GetComponentsInChildren<GrabbableObject>())
                    UnityEngine.Object.Destroy(g);
                foreach (InteractTrigger t in obj.GetComponentsInChildren<InteractTrigger>())
                    UnityEngine.Object.Destroy(t);
                fakeItems.Add(obj);
            }

            // Net Replication
            NetworkObject netObj = obj.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                LCSMain.logger.LogWarning("Object has not network property?");
                return;
            }
            netObj.Spawn();

            // Update item value states
            GrabbableObject grabObj = obj.GetComponentInChildren<GrabbableObject>();
            if (!isFake && price > 0 && grabObj != null)
            {
                grabObj.SetScrapValue(price);
                RoundManager.Instance.SyncScrapValuesClientRpc(new NetworkObjectReference[] { new NetworkObjectReference(netObj) }, new int[] { price });
            }

            // Check if item is spawned on ship ground
            bool isShip = StartOfRound.Instance.shipInnerRoomBounds.bounds.Contains(obj.transform.position);
            if(isShip)
            {
                grabObj.isInShipRoom = true;
                grabObj.isInElevator = true;
                grabObj.transform.SetParent(StartOfRound.Instance.elevatorTransform, true);
            }

            LCSMain.logger.LogInfo("Item Spawned!",new Dictionary<string, object>()
            {
                ["target"] = player.playerUsername,
                ["item"] = item.itemName,
                ["isFake"] = isFake,
                ["price"] = price
            });
        }

        public static void spawnEntity(PlayerControllerB player, EnemyType enemy, bool isFake = false)
        {
            if (!NetworkManager.Singleton.IsServer)
                return;

            GameObject gameObject = UnityEngine.Object.Instantiate(enemy.enemyPrefab, Utils.SearchBestPosition(player, enemy.enemyPrefab), Quaternion.identity);
            EnemyAI AIState = gameObject.GetComponent<EnemyAI>();
            if (isFake)
            {
                UnityEngine.Object.Destroy(AIState);
                fakeItems.Add(gameObject);
            }

            gameObject.GetComponentInChildren<NetworkObject>().Spawn(destroyWithScene: true);
            if (!isFake)
            {
                RoundManager.Instance.SpawnedEnemies.Add(AIState);
                AIState.enemyType.numberSpawned++;
                AIState.enemyType.hasSpawnedAtLeastOne = true;
            }
            else
                RPCHandle.Instance.syncFakeEntityClientRpc(gameObject.GetComponent<NetworkObject>().NetworkObjectId);


            // NetworkObjectReference NetRef = RoundManager.Instance.SpawnEnemyGameObject(SearchBestPosition(player, enemy.enemyPrefab), 0f, 0, enemy);

            LCSMain.logger.LogInfo("Entity Spawned!", new Dictionary<string, object>()
            {
                ["target"] = player.playerUsername,
                ["item"] = enemy.enemyName,
                ["isFake"] = isFake
            });
        }

        public static void spawnTrap(PlayerControllerB player, IndoorMapHazardType trap, bool isFake = false)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            Vector3 OptimalLocation = Utils.SearchBestPosition(player, trap.prefabToSpawn);
            Vector3 direction = player.transform.position - OptimalLocation;
            direction.y = 0;
            GameObject trapObj = UnityEngine.Object.Instantiate(trap.prefabToSpawn,
                OptimalLocation,
                Quaternion.LookRotation(direction.normalized),
                RoundManager.Instance.mapPropsContainer.transform);
            if(isFake)
            {
                trapObj.GetComponent<FakeObject>().isFake.Value = isFake;
                fakeItems.Add(trapObj);
            }
            
            trapObj.GetComponent<NetworkObject>().Spawn(true);
            LCSMain.logger.LogInfo("Trap Spawned!", new Dictionary<string, object>()
            {
                ["target"] = player.playerUsername,
                ["trap"] = trap.prefabToSpawn.name,
                ["isFake"] = isFake
            });
        }

        public static void changeQuota(string type, int val = -1)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            TimeOfDay ToDInstance = TimeOfDay.Instance;
            switch (type)
            {
                case "time":
                    // ToDInstance.quotaVariables.deadlineDaysAmount = val > 0 ? val : 100;
                    ToDInstance.timeUntilDeadline = (val > -1 ? val : 100) * ToDInstance.totalTime;
                    break;
                case "cur":
                    ToDInstance.quotaFulfilled = val > -1 ? val : 9999;
                    break;
                case "due":
                    ToDInstance.profitQuota = val > -1 ? val : 9999;
                    break;
            }
            ToDInstance.UpdateProfitQuotaCurrentTime();
            ToDInstance.SetBuyingRateForDay();

            RPCHandle.Instance.syncQuotaClientRpc(ToDInstance.quotaFulfilled, ToDInstance.profitQuota, ToDInstance.timeUntilDeadline);
            LCSMain.logger.LogInfo($"Changed quota for {type} to {val}");
        }

        public static void SetBalance(int val)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            Terminal terminal = UnityEngine.Object.FindObjectOfType<Terminal>();
            if (terminal == null)
            {
                LCSMain.logger.LogError("Terminal not found!");
                return;
            }
            terminal.groupCredits = val;
            TerminalHook.instance.SyncGroupCreditsClientRpc(val, TerminalHook.instance.numberOfItemsInDropship);
            LCSMain.logger.LogInfo("Terminal Balance successfully updated!");
        }
    }
}
