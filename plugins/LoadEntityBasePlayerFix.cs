using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Network;

namespace Oxide.Plugins
{
    [Info("LoadEntityBasePlayerFix", "Pengoo", "1.0.0")]
    [Description("Removes BasePlayer data from non-player entities when loading saves to prevent erroneous skipping.")]
    public class LoadEntityBasePlayerFix : RustPlugin
    {
        private const string PlayerPrefabPath = "assets/prefabs/player/player.prefab";

        private static LoadEntityBasePlayerFix Instance;

        private Harmony harmony;

        private void Init()
        {
            Instance = this;
            TryPatchProtoEntityHooks();
        }

        private void Unload()
        {
            harmony?.UnpatchAll(harmony?.Id);
            harmony = null;
            Instance = null;
        }

        private void TryPatchProtoEntityHooks()
        {
            try
            {
                harmony = new Harmony("loadentitybaseplayerfix.harmony");
            }
            catch (Exception ex)
            {
                PrintError($"Failed to initialize Harmony: {ex}");
                harmony = null;
                return;
            }

            var targetTypes = FindProtoEntityTypes();

            if (targetTypes.Count == 0)
            {
                PrintWarning("Could not find ProtoBuf.Entity type. No patches were applied.");
                return;
            }

            var postfixMethod = typeof(LoadEntityBasePlayerFix).GetMethod(nameof(ReadFromStreamPostfix), BindingFlags.Static | BindingFlags.NonPublic);
            var postfixPatch = postfixMethod != null ? new HarmonyMethod(postfixMethod) : null;

            if (postfixPatch == null)
            {
                PrintWarning("Failed to locate ReadFromStreamPostfix method. No patches were applied.");
                return;
            }

            var patchedAny = false;

            foreach (var type in targetTypes)
            {
                MethodInfo[] methods;
                try
                {
                    methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Where(m => m.Name == "ReadFromStream")
                        .ToArray();
                }
                catch (Exception ex)
                {
                    PrintWarning($"Failed while scanning methods on {type.FullName}: {ex.Message}");
                    continue;
                }

                foreach (var method in methods)
                {
                    try
                    {
                        harmony.Patch(method, postfix: postfixPatch);
                        patchedAny = true;
                    }
                    catch (Exception ex)
                    {
                        PrintWarning($"Failed to patch {type.FullName}.{method.Name}: {ex.Message}");
                    }
                }
            }

            if (!patchedAny)
            {
                PrintWarning("No ReadFromStream overloads were patched.");
            }
            else
            {
                Puts($"LoadEntityBasePlayerFix: patched ReadFromStream on {targetTypes.Count} ProtoBuf.Entity type(s).");
            }
        }

        private static List<Type> FindProtoEntityTypes()
        {
            var result = new List<Type>();
            var visited = new HashSet<Assembly>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!visited.Add(assembly))
                {
                    continue;
                }

                try
                {
                    var type = assembly.GetType("ProtoBuf.Entity", false);
                    if (type != null && !result.Contains(type))
                    {
                        result.Add(type);
                    }
                }
                catch
                {
                    // ignore assemblies we cannot inspect
                }
            }

            return result;
        }

        private static void ReadFromStreamPostfix(global::ProtoBuf.Entity __instance)
        {
            if (Instance == null || __instance == null)
            {
                return;
            }

            if (__instance.basePlayer == null)
            {
                return;
            }

            var networkable = __instance.baseNetworkable;
            if (networkable == null)
            {
                return;
            }

            var prefabId = networkable.prefabID;
            if (prefabId == 0u)
            {
                return;
            }

            string prefabName = null;
            try
            {
                prefabName = StringPool.Get(prefabId);
            }
            catch
            {
                // ignored
            }

            if (string.Equals(prefabName, PlayerPrefabPath, StringComparison.Ordinal))
            {
                return;
            }

            var playerId = GetProtoPlayerUserId(__instance.basePlayer);
            var netId = networkable.uid.IsValid ? networkable.uid.Value : 0UL;
            Instance.LogFix(prefabName, prefabId, playerId, netId);

            TryResetProtoPlayer(__instance.basePlayer);
            __instance.basePlayer = null;
        }

        private static ulong GetProtoPlayerUserId(object protoPlayer)
        {
            if (protoPlayer == null)
            {
                return 0UL;
            }

            var type = protoPlayer.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            try
            {
                var property = type.GetProperty("userid", flags);
                if (property != null)
                {
                    var value = property.GetValue(protoPlayer, null);
                    if (value != null && ulong.TryParse(value.ToString(), out var parsed))
                    {
                        return parsed;
                    }
                }
            }
            catch
            {
                // ignored
            }

            try
            {
                var field = type.GetField("userid", flags);
                if (field != null)
                {
                    var value = field.GetValue(protoPlayer);
                    if (value != null && ulong.TryParse(value.ToString(), out var parsed))
                    {
                        return parsed;
                    }
                }
            }
            catch
            {
                // ignored
            }

            return 0UL;
        }

        private static void TryResetProtoPlayer(object protoPlayer)
        {
            if (protoPlayer == null)
            {
                return;
            }

            var type = protoPlayer.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            try
            {
                var method = type.GetMethod("ResetToPool", flags, null, Type.EmptyTypes, null);
                method?.Invoke(protoPlayer, Array.Empty<object>());
            }
            catch
            {
                // ignored
            }
        }

        private void LogFix(string prefabName, uint prefabId, ulong playerId, ulong netId)
        {
            if (string.IsNullOrEmpty(prefabName))
            {
                prefabName = $"#{prefabId}";
            }

            Puts($"[LoadEntityBasePlayerFix] Cleared basePlayer (playerId={playerId}) from prefab={prefabName} netId={netId}");
        }
    }
}