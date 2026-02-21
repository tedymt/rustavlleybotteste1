using System;
using System.Reflection;
using HarmonyLib;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("ChickenBeeLove", "Mestre Pardal", "1.0.2")]
    [Description("Deixa galinhas e colméias sempre felizes para donos com permissão.")]
    public class ChickenBeeLove : CovalencePlugin
    {
        public const string PermissionUse = "chickenbeelove.use";

        private Harmony _harmony;
        private static ChickenBeeLove _instance;

        private const float PerfectBeeTemperature = 23f;
        private const float ChickenUpdateInterval = 10f;
        private const float MinChickenSunlight = 60f;

        // Mantém o "FORA" sempre ON (cache/UI e produção) para quem tem permissão
        private const float BeehiveForceInterval = 3f;
        private Timer _beehiveTimer;

        private Timer _chickenTimer;

        private static readonly PropertyInfo AnimalLoveProperty =
            typeof(FarmableAnimal).GetProperty("AnimalLove", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly PropertyInfo AnimalSunlightProperty =
            typeof(FarmableAnimal).GetProperty("AnimalSunlight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        // Cache de reflection para tentar setar o "fora" que vai pra UI (varia por build)
        private static FieldInfo _beehiveOutsideBoolField;
        private static FieldInfo _beehiveOutsideFloatField;

        #region Lifecycle

        private void Init()
        {
            _instance = this;
            permission.RegisterPermission(PermissionUse, this);
        }

        private void OnServerInitialized()
        {
            SetupBeehiveReflection();
            SetupHarmonyPatches();
            StartChickenTimer();
            StartBeehiveForceTimer();
        }

        private void Unload()
        {
            _chickenTimer?.Destroy();
            _chickenTimer = null;

            _beehiveTimer?.Destroy();
            _beehiveTimer = null;

            if (_harmony != null)
            {
                try { _harmony.UnpatchAll(_harmony.Id); }
                catch (Exception e) { PrintError($"Error unpatching Harmony: {e}"); }
                _harmony = null;
            }

            _instance = null;
        }

        #endregion

        #region Beehive Reflection

        private void SetupBeehiveReflection()
        {
            // Tentamos achar fields comuns usados em diferentes builds/branches.
            // A ideia é: se existir um cache (bool/float) que a UI lê, a gente mantém ele "fora".
            try
            {
                var t = typeof(Beehive);

                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var n = f.Name.ToLowerInvariant();
                    if (n.Contains("outside") || n.Contains("isoutside") || n.Contains("outdoors"))
                    {
                        if (_beehiveOutsideBoolField == null && f.FieldType == typeof(bool))
                            _beehiveOutsideBoolField = f;

                        if (_beehiveOutsideFloatField == null && (f.FieldType == typeof(float) || f.FieldType == typeof(double)))
                            _beehiveOutsideFloatField = f;
                    }
                }

                if (_beehiveOutsideBoolField != null)
                    PrintWarning($"ChickenBeeLove: Found Beehive outside bool field: {_beehiveOutsideBoolField.Name}");

                if (_beehiveOutsideFloatField != null)
                    PrintWarning($"ChickenBeeLove: Found Beehive outside float field: {_beehiveOutsideFloatField.Name}");
            }
            catch (Exception e)
            {
                PrintError($"ChickenBeeLove: SetupBeehiveReflection failed: {e}");
            }
        }

        #endregion

        #region Harmony / Beehive

        private void SetupHarmonyPatches()
        {
            try
            {
                _harmony = new Harmony("chickenbeelove.patches");

                // Temperatura perfeita
                var tempMethod = AccessTools.Method(typeof(Beehive), nameof(Beehive.CalculateTemperature));
                if (tempMethod != null)
                {
                    _harmony.Patch(tempMethod,
                        prefix: new HarmonyMethod(typeof(ChickenBeeLovePatches),
                            nameof(ChickenBeeLovePatches.Beehive_CalculateTemperature_Prefix)));
                }

                // Sem chuva/umidade ruim (se o método existir)
                var rainMethod = AccessTools.Method(typeof(Beehive), "CalculateRain");
                if (rainMethod != null)
                {
                    _harmony.Patch(rainMethod,
                        prefix: new HarmonyMethod(typeof(ChickenBeeLovePatches),
                            nameof(ChickenBeeLovePatches.Beehive_CalculateRain_Prefix)));
                }

                // >>> O pulo do gato:
                // Patch em QUALQUER método da Beehive que calcule "Outside" (bool/float), sem depender do nome exato.
                int patchedOutside = 0;
                foreach (var m in AccessTools.GetDeclaredMethods(typeof(Beehive)))
                {
                    if (m == null) continue;
                    if (m.IsGenericMethod) continue;
                    if (m.GetParameters().Length != 0) continue;

                    var name = m.Name ?? string.Empty;
                    if (name.IndexOf("outside", StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf("outdoor", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    if (m.ReturnType == typeof(bool))
                    {
                        _harmony.Patch(m,
                            prefix: new HarmonyMethod(typeof(ChickenBeeLovePatches),
                                nameof(ChickenBeeLovePatches.Beehive_AnyOutsideBool_Prefix)));
                        patchedOutside++;
                    }
                    else if (m.ReturnType == typeof(float))
                    {
                        _harmony.Patch(m,
                            prefix: new HarmonyMethod(typeof(ChickenBeeLovePatches),
                                nameof(ChickenBeeLovePatches.Beehive_AnyOutsideFloat_Prefix)));
                        patchedOutside++;
                    }
                }

                PrintWarning($"ChickenBeeLove: Harmony patches applied (Beehive). Outside-method patches: {patchedOutside}");
            }
            catch (Exception e)
            {
                PrintError($"ChickenBeeLove: Failed to apply Harmony patches: {e}");
            }
        }

        #endregion

        #region Beehive Force Timer

        private void StartBeehiveForceTimer()
        {
            _beehiveTimer = timer.Every(BeehiveForceInterval, ForceBeehivesOutsideForPermittedOwners);
        }

        private void ForceBeehivesOutsideForPermittedOwners()
        {
            try
            {
                foreach (var ent in BaseNetworkable.serverEntities)
                {
                    if (ent is not Beehive hive) continue;
                    if (hive == null || hive.IsDestroyed) continue;

                    var ownerId = hive.OwnerID;
                    if (!ownerId.IsSteamId()) continue;
                    if (!HasUsePermission(ownerId)) continue;

                    // Força o cache (se existir), pra UI e lógica não voltarem pra "NÃO"
                    TryForceOutsideCache(hive);

                    // Atualiza rede pra UI refletir (FORA: SIM)
                    hive.SendNetworkUpdate();
                }
            }
            catch (Exception e)
            {
                PrintError($"ChickenBeeLove: Error in ForceBeehivesOutsideForPermittedOwners: {e}");
            }
        }

        private void TryForceOutsideCache(Beehive hive)
        {
            try
            {
                if (_beehiveOutsideBoolField != null)
                    _beehiveOutsideBoolField.SetValue(hive, true);

                if (_beehiveOutsideFloatField != null)
                {
                    // Se o build usar 0..1 ou 0..100, 1f é seguro como "outside ok"
                    if (_beehiveOutsideFloatField.FieldType == typeof(float))
                        _beehiveOutsideFloatField.SetValue(hive, 1f);
                    else
                        _beehiveOutsideFloatField.SetValue(hive, 1.0);
                }
            }
            catch
            {
                // Silencioso: esse campo pode não existir nesse build
            }
        }

        #endregion

        #region Chicken Coop Loop

        private void StartChickenTimer()
        {
            _chickenTimer = timer.Every(ChickenUpdateInterval, CheckChickenCoops);
        }

        private void CheckChickenCoops()
        {
            try
            {
                foreach (var ent in BaseNetworkable.serverEntities)
                {
                    if (ent is not ChickenCoop coop)
                        continue;

                    if (!coop.OwnerID.IsSteamId())
                        continue;

                    if (!HasUsePermission(coop.OwnerID))
                        continue;

                    MakeChickensHappy(coop);
                }
            }
            catch (Exception e)
            {
                PrintError($"ChickenBeeLove: Error in CheckChickenCoops: {e}");
            }
        }

        private void MakeChickensHappy(ChickenCoop coop)
        {
            if (coop.Animals == null || coop.Animals.Count == 0)
                return;

            foreach (var animalSlot in coop.Animals)
            {
                var spawnedRef = animalSlot.SpawnedAnimal;
                if (!spawnedRef.IsValid(true))
                    continue;

                if (!spawnedRef.TryGet(true, out FarmableAnimal farmableAnimal))
                    continue;

                if (farmableAnimal == null || farmableAnimal.IsDead())
                    continue;

                TrySetAnimalLove(farmableAnimal, 100f);
                TryEnsureSunlight(farmableAnimal, MinChickenSunlight);
            }
        }

        private void TrySetAnimalLove(FarmableAnimal animal, float value)
        {
            if (AnimalLoveProperty == null)
                return;

            try { AnimalLoveProperty.SetValue(animal, value); }
            catch { }
        }

        private void TryEnsureSunlight(FarmableAnimal animal, float minValue)
        {
            if (AnimalSunlightProperty == null)
                return;

            try
            {
                var currentObj = AnimalSunlightProperty.GetValue(animal);
                var current = currentObj is float f ? f : 0f;

                if (current < minValue)
                    AnimalSunlightProperty.SetValue(animal, minValue);
            }
            catch { }
        }

        #endregion

        #region Permission Helper

        internal static bool HasUsePermission(ulong userId)
        {
            if (_instance == null) return false;
            if (!userId.IsSteamId()) return false;
            return _instance.permission.UserHasPermission(userId.ToString(), PermissionUse);
        }

        #endregion
    }

    internal static class ChickenBeeLovePatches
    {
        public static bool Beehive_CalculateTemperature_Prefix(Beehive __instance, ref float __result)
        {
            if (__instance == null || !__instance.OwnerID.IsSteamId())
                return true;

            if (!ChickenBeeLove.HasUsePermission(__instance.OwnerID))
                return true;

            __result = 23f;
            return false;
        }

        public static bool Beehive_CalculateRain_Prefix(Beehive __instance, ref float __result)
        {
            if (__instance == null || !__instance.OwnerID.IsSteamId())
                return true;

            if (!ChickenBeeLove.HasUsePermission(__instance.OwnerID))
                return true;

            __result = 0f;
            return false;
        }

        // Patch genérico: qualquer método bool "Outside/Outdoor" (sem params) vira TRUE se o dono tiver permissão
        public static bool Beehive_AnyOutsideBool_Prefix(Beehive __instance, ref bool __result)
        {
            if (__instance == null || !__instance.OwnerID.IsSteamId())
                return true;

            if (!ChickenBeeLove.HasUsePermission(__instance.OwnerID))
                return true;

            __result = true;
            return false;
        }

        // Patch genérico: qualquer método float "Outside/Outdoor" (sem params) vira 1.0f se o dono tiver permissão
        public static bool Beehive_AnyOutsideFloat_Prefix(Beehive __instance, ref float __result)
        {
            if (__instance == null || !__instance.OwnerID.IsSteamId())
                return true;

            if (!ChickenBeeLove.HasUsePermission(__instance.OwnerID))
                return true;

            __result = 1f;
            return false;
        }
    }
}
