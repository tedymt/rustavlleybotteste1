using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins.BetterNpcExtensionMethods;
using Rust.Ai.Gen2;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;

namespace Oxide.Plugins
{
    [Info("BetterNpc", "KpucTaJl", "2.1.5")]
    internal class BetterNpc : RustPlugin
    {
        #region Config
        private const bool En = true;

        private PluginConfig Cfg { get; set; }

        protected override void LoadDefaultConfig()
        {
            Puts("Creating a default config...");
            Cfg = PluginConfig.DefaultConfig();
            Cfg.PluginVersion = Version;
            SaveConfig();
            Puts("Creation of the default config completed!");
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            Cfg = Config.ReadObject<PluginConfig>();
            if (Cfg.PluginVersion < Version) UpdateConfigValues();
        }

        private void UpdateConfigValues()
        {
            Puts("Config update detected! Updating config values...");
            Cfg.PluginVersion = Version;
            Puts("Config update completed!");
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(Cfg);

        [JsonConverter(typeof(StringEnumConverter))]
        public enum SpawnType
        {
            Random,
            Custom
        }

        public class PresetConfig
        {
            [JsonProperty(En ? "Enabled? [true/false]" : "Включен? [true/false]")] public bool Enabled { get; set; }
            [JsonProperty(En ? "Minimum numbers - Day" : "Минимальное кол-во днем")] public int MinDay { get; set; }
            [JsonProperty(En ? "Maximum numbers - Day" : "Максимальное кол-во днем")] public int MaxDay { get; set; }
            [JsonProperty(En ? "Minimum numbers - Night" : "Минимальное кол-во ночью")] public int MinNight { get; set; }
            [JsonProperty(En ? "Maximum numbers - Night" : "Максимальное кол-во ночью")] public int MaxNight { get; set; }
            [JsonProperty(En ? "Minimum respawn time after death [sec.]" : "Минимальное время респавна после смерти [sec.]")] public int RespawnMinTime { get; set; }
            [JsonProperty(En ? "Maximum respawn time after death [sec.]" : "Максимальное время респавна после смерти [sec.]")] public int RespawnMaxTime { get; set; }
            [JsonProperty(En ? "The name of the NPC preset from the NpcSpawn plugin" : "Название пресета NPC из плагина NpcSpawn")] public string PresetName { get; set; }
            [JsonProperty(En ? "Rewards for killing the NPC (key - plugin name, value - points to give)" : "Награды за убийство NPC (ключ - название плагина, значение - сколько очков выдать)")] public Dictionary<string, double> Economics { get; set; }
            [JsonProperty(En ? "Spawn Type [Random/Custom]" : "Тип спавна [Random/Custom]")] public SpawnType SpawnType { get; set; }
            [JsonProperty(En ? "List of spawn positions (X, Y, Z) (used if Spawn Type - Custom)" : "Список точек спавна (X, Y, Z) (используется если тип спавна - Custom)")] public List<string> CustomPositions { get; set; }

            [JsonIgnore] public bool IsEventFile { get; set; }
            [JsonIgnore] public bool IsCargoFile { get; set; }
            [JsonIgnore] public bool IsRoadOrBiomeFile { get; set; }

            public bool ShouldSerializeRespawnMinTime()
            {
                if (IsEventFile) return false;
                if (IsCargoFile) return false;
                return true;
            }

            public bool ShouldSerializeRespawnMaxTime()
            {
                if (IsEventFile) return false;
                if (IsCargoFile) return false;
                return true;
            }

            public bool ShouldSerializeSpawnType()
            {
                if (IsRoadOrBiomeFile) return false;
                if (IsEventFile) return false;
                if (IsCargoFile) return false;
                return true;
            }

            public bool ShouldSerializeCustomPositions()
            {
                if (IsRoadOrBiomeFile) return false;
                if (IsEventFile) return false;
                return true;
            }
        }

        public class SpawnPoint
        {
            [JsonProperty(En ? "Enabled? [true/false]" : "Включен? [true/false]")] public bool Enabled { get; set; }
            [JsonProperty(En ? "Presets" : "Наборы NPC")] public List<PresetConfig> Presets { get; set; }
        }

        public class MonumentSpawnPoint : SpawnPoint
        {
            [JsonProperty(En ? "Monument size (X, Y, Z)" : "Размер монумента (X, Y, Z)")] public string Size { get; set; }
            [JsonProperty(En ? "Remove default NPCs? [true/false]" : "Удалить стандартных NPC? [true/false]")] public bool RemoveDefaultNpc { get; set; }
        }

        public class CustomMonumentSpawnPoint : MonumentSpawnPoint
        {
            [JsonProperty(En ? "Map ID" : "ID карты")] public float Id { get; set; }
            [JsonProperty(En ? "Map marker name (leave empty if using Position+Rotation)" : "Название маркера на карте (оставить пустым, если используется Позиция+Вращение)")] public string MapMarkerName { get; set; }
            [JsonProperty(En ? "Position (X, Y, Z) (leave empty if using Map marker name)" : "Позиция (X, Y, Z) (оставить пустым, если используется название маркера на карте)")] public string Position { get; set; }
            [JsonProperty(En ? "Rotation (X, Y, Z) (leave empty if using Map marker name)" : "Вращение (X, Y, Z) (оставить пустым, если используется название маркера на карте)")] public string Rotation { get; set; }
        }

        public class EventSpawnPoint : SpawnPoint
        {
            [JsonProperty(En ? "Radius" : "Радиус")] public float Radius { get; set; }
        }

        public class BradleySpawnPoint : EventSpawnPoint
        {
            [JsonProperty(En ? "Remove default NPCs? [true/false]" : "Удалить стандартных NPC? [true/false]")] public bool RemoveDefaultNpc { get; set; }
        }

        public class CargoSpawnPoint : SpawnPoint
        {
            [JsonProperty(En ? "Remove default NPCs? [true/false]" : "Удалять стандартных NPC? [true/false]")] public bool RemoveDefaultNpc { get; set; }
            [JsonProperty(En ? "Respawn NPCs when the cargo ship docks at the harbor? [true/false]" : "Респавнить NPC, когда корабль заплывает в порт? [true/false]")] public bool RespawnNpcHarbor { get; set; }
            [JsonProperty(En ? "Respawn NPCs when new crates spawn? [true/false]" : "Респавнить NPC, когда на корабле появляются новые ящики? [true/false]")] public bool RespawnNpcCrates { get; set; }
        }

        public class RoadOrBiomeSpawnPoint : SpawnPoint
        {
            [JsonProperty(En ? "Minimum distance that must be maintained between nearby NPCs [m]" : "Минимальное расстояние, которое должно соблюдаться между ближайшими NPC [м]")] public float MinDistanceBetweenNpc { get; set; }
        }

        private class PluginConfig
        {
            [JsonProperty(En ? "Start time of day" : "Время начала дня")] public string StartDayTime { get; set; }
            [JsonProperty(En ? "Start time of night" : "Время начала ночи")] public string StartNightTime { get; set; }
            [JsonProperty(En ? "Use the plugin's PVE mode? (only if you use the PveMode plugin)" : "Использовать PVE-режим работы плагина? (только если используется плагин PveMode)")] public bool Pve { get; set; }
            [JsonProperty(En ? "Distance from the center of the safe zone to the nearest NPC spawn point [m]" : "Расстояние от центра безопасной зоны до ближайшего места спавна NPC [м]")] public float SafeZoneRange { get; set; }
            [JsonProperty(En ? "List of NPC types that should not be deleted (can use class name, NPC name, SkinID, or ShortPrefabName)" : "Список типов NPC, которые не должны удаляться (можно использовать имя класса, имя NPC, SkinID или ShortPrefabName)")] public HashSet<string> WhitelistNpc { get; set; }
            [JsonProperty(En ? "Run the debug.puzzlereset command when the plugin loads or reloads to refresh puzzles, IO, and NPCs at Facepunch monuments [true/false]" : "Обновлять головоломки, IO и NPC на монументах при загрузке или перезагрузке плагина? (debug.puzzlereset) [true/false]")] public bool PuzzleReset { get; set; }
            [JsonProperty(En ? "Refresh NPCs and crates at monuments when the plugin loads or reloads? [true/false]" : "Обновлять NPC и ящики на монументах при загрузке или перезагрузке плагина? [true/false]")] public bool SpawnGroupFill { get; set; }
            [JsonProperty(En ? "Enable simplified loading logs in the server console? (intended for advanced users who want fewer loading messages) [true/false]" : "Включить упрощённые сообщения о загрузке в консоли сервера? (для опытных пользователей, которые хотят меньше логов) [true/false]")] public bool EnabledMinLogs { get; set; }
            [JsonProperty(En ? "Configuration version" : "Версия конфигурации")] public VersionNumber PluginVersion { get; set; }

            public static PluginConfig DefaultConfig()
            {
                return new PluginConfig
                {
                    StartDayTime = "8:00",
                    StartNightTime = "20:00",
                    Pve = false,
                    SafeZoneRange = 150f,
                    WhitelistNpc = new HashSet<string>
                    {
                        "11162132011012",
                        "NpcRaider",
                        "RandomRaider",
                        "56485621526987",
                        "CustomScientistNpc"
                    },
                    PuzzleReset = true,
                    SpawnGroupFill = true,
                    EnabledMinLogs = false,
                    PluginVersion = new VersionNumber()
                };
            }
        }
        #endregion Config

        #region Oxide Hooks
        [PluginReference] private readonly Plugin NpcSpawn, PveMode;

        private static BetterNpc _;

        private void Init() => _ = this;

        private void OnServerInitialized()
        {
            if (!plugins.Exists("NpcSpawn"))
            {
                PrintError("NpcSpawn plugin doesn`t exist! (https://drive.google.com/drive/folders/1-18L-mG7yiGxR-PQYvd11VvXC2RQ4ZCu?usp=sharing)");
                NextTick(() => Interface.Oxide.UnloadPlugin(Name));
                return;
            }

            permission.RegisterPermission("betternpc.admin", this);

            IsDay = IsInDayRange();

            InitializationCoroutine = PluginInitialization().Start();

            CheckVersionPlugin();
        }

        private void Unload()
        {
            if (InitializationCoroutine != null)
            {
                InitializationCoroutine.Stop();
                InitializationCoroutine = null;
            }

            StopRespawnCounter();
            StopCheckDay();

            foreach (ControllerSpawnPoint controller in Controllers)
            {
                if (controller == null) continue;
                UnityEngine.Object.Destroy(controller.gameObject);
            }
            Controllers.Clear();
            Controllers = null;

            if (CargoControllers != null)
            {
                foreach (CargoControllerSpawnPoint controller in CargoControllers)
                {
                    if (controller == null) continue;
                    controller.Destroy();
                }
                CargoControllers.Clear();
                CargoControllers = null;
            }

            if (CargoConfig is { RemoveDefaultNpc: true }) ConVar.AI.npc_spawn_on_cargo_ship = true;

            ClearVariablesForMonuments();

            ClearVariablesForCustomMonuments();

            ClearVariablesForEvents();

            RoadSpawnPoints.Clear();
            RoadSpawnPoints = null;

            BiomeSpawnPoints.Clear();
            BiomeSpawnPoints = null;

            _ = null;
        }

        private void OnCorpsePopulate(ScientistNPC npc, NPCPlayerCorpse corpse)
        {
            if (npc == null || npc.skinID != 11162132011012) return;

            ControllerSpawnPoint controller = Controllers.FirstOrDefault(x => x.Contains(npc));
            if (controller != null)
            {
                controller.DieNpc(npc);
                return;
            }

            if (CargoControllers is { Count: > 0 })
            {
                CargoControllerSpawnPoint cargoController = CargoControllers.FirstOrDefault(x => x.Contains(npc));
                if (cargoController != null)
                {
                    cargoController.DieNpc(npc);
                    return;
                }
            }
        }

        private void OnEntitySpawned(global::HumanNPC npc) => TryKillDefaultNpc(npc);
        private void OnEntitySpawned(ScientistNPC2 npc) => TryKillDefaultNpc(npc);
        private void TryKillDefaultNpc(BaseCombatEntity npc)
        {
            ControllerSpawnPoint controller = Controllers.FirstOrDefault(s => s.RemoveDefaultNpc && s.CanRemoveNpc(npc));
            if (controller != null)
            {
                NextTick(() =>
                {
                    if (npc.IsExists())
                        npc.Kill();
                });
                return;
            }
        }
        #endregion Oxide Hooks

        #region Day or Night
        private Coroutine CheckDayCoroutine { get; set; }
        private bool IsDay { get; set; }

        private void StartCheckDay()
        {
            StopCheckDay();

            float startDay = ParseHourOrDefaultNormalized(Cfg.StartDayTime, 8f);
            float startNight = ParseHourOrDefaultNormalized(Cfg.StartNightTime, 20f);

            CheckDayCoroutine = CheckDay(startDay, startNight).Start();
        }

        private void StopCheckDay()
        {
            CheckDayCoroutine.Stop();
            CheckDayCoroutine = null;
        }

        private IEnumerator CheckDay(float startDay, float startNight)
        {
            while (true)
            {
                float currentTime = TOD_Sky.Instance == null || TOD_Sky.Instance.Cycle == null ? 12f : TOD_Sky.Instance.Cycle.Hour;
                bool dayNow = IsInDayRange(currentTime, startDay, startNight);

                if (dayNow != IsDay)
                {
                    IsDay = dayNow;

                    if (Controllers != null)
                        foreach (ControllerSpawnPoint controller in Controllers)
                            controller?.UpdatePopulation();

                    if (CargoControllers != null)
                        foreach (CargoControllerSpawnPoint controller in CargoControllers)
                            controller?.UpdatePopulation();
                }

                yield return CoroutineEx.waitForSeconds(30f);
            }
        }

        private static float ParseHourOrDefaultNormalized(string input, float fallback)
        {
            float hours;

            if (string.IsNullOrWhiteSpace(input)) hours = fallback;
            else if (TimeSpan.TryParse(input, out TimeSpan ts)) hours = (float)ts.TotalHours;
            else if (!float.TryParse(input, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out hours)) hours = fallback;

            hours %= 24f;
            if (hours < 0f) hours += 24f;

            return hours;
        }

        private bool IsInDayRange()
        {
            float currentTime = TOD_Sky.Instance.Cycle.Hour;

            float startDay = ParseHourOrDefaultNormalized(Cfg.StartDayTime, 8f);
            float startNight = ParseHourOrDefaultNormalized(Cfg.StartNightTime, 20f);

            return IsInDayRange(currentTime, startDay, startNight);
        }

        private static bool IsInDayRange(float current, float startDay, float startNight)
        {
            if (startDay.AreEqual(startNight)) return false;
            if (startDay < startNight) return current >= startDay && current < startNight;
            return current >= startDay || current < startNight;
        }
        #endregion Day or Night

        #region DeadNpc Respawn Counter
        private Coroutine RespawnCounterCoroutine { get; set; }

        private void StartRespawnCounter()
        {
            StopRespawnCounter();
            RespawnCounterCoroutine = UpdateRespawnTime(5).Start();
        }

        private void StopRespawnCounter()
        {
            RespawnCounterCoroutine.Stop();
            RespawnCounterCoroutine = null;
        }

        private IEnumerator UpdateRespawnTime(int timeRepeat)
        {
            while (true)
            {
                foreach (ControllerSpawnPoint controller in Controllers)
                    controller.UpdateDeadNpcTime(timeRepeat);

                yield return CoroutineEx.waitForSeconds(timeRepeat);
            }
        }
        #endregion DeadNpc Respawn Counter

        #region Controller
        private HashSet<ControllerSpawnPoint> Controllers { get; set; } = new HashSet<ControllerSpawnPoint>();

        internal class ControllerSpawnPoint : FacepunchBehaviour
        {
            internal string Name { get; set; }
            internal bool RemoveDefaultNpc { get; set; }
            internal Vector3 Size { get; set; }

            private float SquareDistanceBetweenNpc { get; set; } = 36f;

            internal bool IsRoad { get; set; }
            internal bool IsBiome { get; set; }
            internal bool IsEvent { get; set; }
            internal bool IsTunnel { get; set; }
            internal bool IsUnderwaterLab { get; set; }

            private HashSet<PresetConfig> Presets { get; set; } = new HashSet<PresetConfig>();
            internal Dictionary<string, List<Vector3>> PositionsPerPreset { get; set; } = null;

            internal HashSet<ActiveNpcInfo> ActiveNpc { get; set; } = new HashSet<ActiveNpcInfo>();
            private List<DeadNpcInfo> DeadNpc { get; set; } = new List<DeadNpcInfo>();

            private int GetAmountNpc(PresetConfig preset) => ActiveNpc.Where(x => x.Preset == preset).Count + DeadNpc.Where(x => x.Preset == preset).Count;
            private static int GetShouldAmountNpc(PresetConfig preset) => _.IsDay ? UnityEngine.Random.Range(preset.MinDay, preset.MaxDay + 1) : UnityEngine.Random.Range(preset.MinNight, preset.MaxNight + 1);

            private void OnDestroy()
            {
                if (Presets != null)
                {
                    Presets.Clear();
                    Presets = null;
                }

                if (PositionsPerPreset != null)
                {
                    PositionsPerPreset.Clear();
                    PositionsPerPreset = null;
                }

                if (ActiveNpc != null)
                {
                    foreach (ActiveNpcInfo active in ActiveNpc) if (active.Npc.IsExists()) active.Npc.Kill();
                    ActiveNpc.Clear();
                    ActiveNpc = null;
                }

                if (DeadNpc != null)
                {
                    DeadNpc.Clear();
                    DeadNpc = null;
                }
            }

            internal void Init(SpawnPoint spawnPoint, Vector3 position, Quaternion rotation, string spawnPointName)
            {
                transform.position = position;
                transform.rotation = rotation;

                Name = spawnPointName;

                foreach (PresetConfig preset in spawnPoint.Presets) if (preset.Enabled) Presets.Add(preset);
                CalculatePositions();

                if (spawnPoint is MonumentSpawnPoint monumentSpawnPoint)
                {
                    Size = monumentSpawnPoint.Size.ToVector3();
                    RemoveDefaultNpc = monumentSpawnPoint.RemoveDefaultNpc;
                }

                if (spawnPoint is EventSpawnPoint eventSpawnPoint)
                {
                    IsEvent = true;
                    float radius = eventSpawnPoint.Radius;
                    Size = new Vector3(radius, radius, radius);
                }

                if (spawnPoint is RoadOrBiomeSpawnPoint roadOrBiomeSpawnPoint)
                {
                    float distanceBetweenNpc = roadOrBiomeSpawnPoint.MinDistanceBetweenNpc;
                    SquareDistanceBetweenNpc = distanceBetweenNpc * distanceBetweenNpc;
                }

                if (RemoveDefaultNpc)
                {
                    List<BaseCombatEntity> list = Facepunch.Pool.Get<List<BaseCombatEntity>>();
                    float radius = Mathf.Max(Size.x, Size.y, Size.z);
                    Vis.Entities(transform.position, radius, list, 133120); // 1 << 11 | 1 << 17
                    foreach (BaseCombatEntity npc in list) if (CanRemoveNpc(npc)) npc.Kill();
                    Facepunch.Pool.FreeUnmanaged(ref list);
                }

                foreach (PresetConfig preset in Presets)
                {
                    int amount = GetShouldAmountNpc(preset);
                    for (int i = 0; i < amount; i++) SpawnNpc(preset);
                }
            }

            private void CalculatePositions()
            {
                foreach (PresetConfig preset in Presets)
                {
                    if (preset.SpawnType != SpawnType.Custom) continue;

                    PositionsPerPreset ??= new Dictionary<string, List<Vector3>>();
                    if (!PositionsPerPreset.ContainsKey(preset.PresetName)) PositionsPerPreset.Add(preset.PresetName, new List<Vector3>());

                    foreach (string str in preset.CustomPositions)
                    {
                        Vector3 local = str.ToVector3();
                        Vector3 global = transform.GetGlobalPosition(local);
                        PositionsPerPreset[preset.PresetName].Add(global);
                    }
                }
            }

            internal void UpdateDeadNpcTime(int lossTime)
            {
                if (DeadNpc.Count == 0 || IsEvent) return;
                for (int i = DeadNpc.Count - 1; i >= 0; i--)
                {
                    DeadNpcInfo data = DeadNpc[i];
                    data.TimeToSpawn -= lossTime;
                    if (data.TimeToSpawn <= 0)
                    {
                        DeadNpc.Remove(data);
                        int amountPresetConfig = GetShouldAmountNpc(data.Preset);
                        int amountPreset = GetAmountNpc(data.Preset);
                        if (amountPresetConfig > amountPreset) SpawnNpc(data.Preset);
                    }
                }
            }

            internal void UpdatePopulation()
            {
                foreach (PresetConfig preset in Presets)
                {
                    int shouldAmountNpc = GetShouldAmountNpc(preset);
                    int amountNpc = GetAmountNpc(preset);
                    if (shouldAmountNpc > amountNpc)
                    {
                        int amount = shouldAmountNpc - amountNpc;
                        for (int i = 0; i < amount; i++) SpawnNpc(preset);
                    }
                    else if (shouldAmountNpc < amountNpc)
                    {
                        int amount = amountNpc - shouldAmountNpc;
                        for (int i = 0; i < amount; i++) KillNpc(preset);
                    }
                }
            }

            private void SpawnNpc(PresetConfig preset)
            {
                Vector3 pos = TryFindPosition(preset);
                if (pos == Vector3.zero) return;

                ScientistNPC npc = (ScientistNPC)_.NpcSpawn.Call("SpawnPreset", pos, preset.PresetName);
                if (npc == null) return;

                ActiveNpc.Add(new ActiveNpcInfo { Preset = preset, Npc = npc });
                _.TrySendNpcToPveMode(npc);
            }

            private Vector3 TryFindPosition(PresetConfig preset)
            {
                int attempts = 0;

                while (attempts < 100)
                {
                    attempts++;

                    Vector3 pos = Vector3.zero;

                    if (preset.IsRoadOrBiomeFile)
                    {
                        object point = Name switch
                        {
                            "Arid" or "Temperate" or "Tundra" or "Arctic" or "Jungle" => _.NpcSpawn.Call("GetSpawnPoint", Name),
                            "ExtraNarrow" or "ExtraWide" or "Standard" => _.NpcSpawn.Call("GetRoadSpawnPoint", Name)
                        };
                        if (point is not Vector3 vector3 || IsInsideSafeZone(vector3)) continue;
                        pos = vector3;
                    }
                    else if (preset.IsEventFile || preset.SpawnType == SpawnType.Random)
                        pos = GetRandomSpawnPos();
                    else if (preset.SpawnType == SpawnType.Custom)
                        pos = PositionsPerPreset[preset.PresetName].GetRandom();

                    if (pos == Vector3.zero) continue;

                    if (HasNpcTooClose(pos)) continue;

                    return pos;
                }

                return Vector3.zero;

                bool HasNpcTooClose(Vector3 pos)
                {
                    foreach (ActiveNpcInfo info in ActiveNpc)
                    {
                        if (!info.Npc.IsExists()) continue;
                        Vector3 npcPos = info.Npc.transform.position;
                        float sqr = (npcPos - pos).sqrMagnitude;
                        if (sqr < SquareDistanceBetweenNpc) return true;
                    }
                    return false;
                }

                bool IsInsideSafeZone(Vector3 pos)
                {
                    if (TriggerSafeZone.allSafeZones.Count == 0) return false;

                    float range = _.Cfg.SafeZoneRange;
                    float rangeSqr = range * range;

                    foreach (TriggerSafeZone safeZone in TriggerSafeZone.allSafeZones)
                    {
                        Vector3 center = safeZone.transform.position;
                        float sqr = (pos - center).sqrMagnitude;
                        if (sqr < rangeSqr) return true;
                    }

                    return false;
                }

                Vector3 GetRandomSpawnPos()
                {
                    int attempts = 0;
                    while (attempts < 10)
                    {
                        attempts++;

                        float x = UnityEngine.Random.Range(-Size.x, Size.x);
                        float z = UnityEngine.Random.Range(-Size.z, Size.z);
                        Vector3 pos = transform.GetGlobalPosition(new Vector3(x, 500f, z));

                        if (pos.IsAvailableTopology(82048, false)) continue; //TerrainTopology.Enum.Ocean | TerrainTopology.Enum.River | TerrainTopology.Enum.Lake

                        if (!pos.IsRaycast(500f, 8454144, out RaycastHit raycastHit)) continue; //1 << 16 | 1 << 23
                        pos.y = raycastHit.point.y;

                        if (!pos.IsNavMesh(2f, 1, out NavMeshHit navmeshHit)) continue;
                        pos = navmeshHit.position;

                        if (IsInsideSafeZone(pos)) continue;

                        return pos;
                    }
                    return Vector3.zero;
                }
            }

            private void KillNpc(PresetConfig preset)
            {
                DeadNpcInfo dead = DeadNpc.FirstOrDefault(x => x.Preset == preset);
                if (dead != null)
                {
                    DeadNpc.Remove(dead);
                    return;
                }

                ActiveNpcInfo active = ActiveNpc.FirstOrDefault(x => x.Preset == preset);
                if (active != null)
                {
                    if (active.Npc.IsExists()) active.Npc.Kill();
                    ActiveNpc.Remove(active);
                }
            }

            internal void DieNpc(ScientistNPC npc)
            {
                ActiveNpcInfo activeNpc = ActiveNpc.FirstOrDefault(x => x.Npc == npc);
                PresetConfig preset = activeNpc.Preset;

                DeadNpc.Add(new DeadNpcInfo { Preset = preset, TimeToSpawn = IsEvent ? 0 : UnityEngine.Random.Range(preset.RespawnMinTime, preset.RespawnMaxTime) });
                ActiveNpc.Remove(activeNpc);

                BasePlayer attacker = npc.lastAttacker as BasePlayer;
                if (attacker.IsPlayer()) _.SendBalance(attacker.userID, preset.Economics);

                if (IsEvent && ActiveNpc.Count == 0)
                {
                    _.NextTick(() =>
                    {
                        _.Controllers.Remove(this);
                        Destroy(gameObject);
                    });
                }
            }

            internal bool CanRemoveNpc(BaseCombatEntity npc)
            {
                if (!npc.IsExists()) return false;

                string displayName = npc switch
                {
                    global::HumanNPC humanNpc => humanNpc.displayName,
                    ScientistNPC2 scientistNpc2 => scientistNpc2.displayName,
                    _ => string.Empty
                };

                if (_.Cfg.WhitelistNpc.Contains(npc.GetType().Name)) return false;
                if (!string.IsNullOrEmpty(displayName) && _.Cfg.WhitelistNpc.Contains(displayName)) return false;
                if (_.Cfg.WhitelistNpc.Contains(npc.skinID.ToString())) return false;
                if (_.Cfg.WhitelistNpc.Contains(npc.ShortPrefabName)) return false;

                if (!IsInsidePos(npc.transform.position)) return false;

                string key = IsTunnel ? "Tunnel" : IsUnderwaterLab ? "Underwater Lab" : Name;
                return _.DefaultScientists.TryGetValue(key, out HashSet<string> shortnames) && shortnames.Contains(npc.ShortPrefabName);
            }

            private bool IsInsidePos(Vector3 pos)
            {
                Vector3 localPos = transform.GetLocalPosition(pos);
                if (localPos.x < -Size.x || localPos.x > Size.x) return false;
                if (localPos.y < -Size.y || localPos.y > Size.y) return false;
                if (localPos.z < -Size.z || localPos.z > Size.z) return false;
                return true;
            }

            internal bool Contains(ScientistNPC npc) => ActiveNpc.Any(x => x.Npc == npc);

            public class ActiveNpcInfo { public PresetConfig Preset; public ScientistNPC Npc; }
            public class DeadNpcInfo { public PresetConfig Preset; public int TimeToSpawn; }
        }

        internal class CargoControllerSpawnPoint
        {
            internal CargoShip Cargo { get; set; }
            private Transform transform => Cargo.transform;

            private float SquareDistanceBetweenNpc { get; set; } = 9f;

            private Queue<NPCSpawner> Spawners { get; set; } = null;

            private HashSet<PresetConfig> Presets { get; set; } = new HashSet<PresetConfig>();

            internal void Init(CargoSpawnPoint spawnPoint, CargoShip cargo)
            {
                Cargo = cargo;

                foreach (PresetConfig preset in spawnPoint.Presets) if (preset.Enabled) Presets.Add(preset);

                UpdateSpawners(cargo);

                foreach (PresetConfig preset in Presets)
                {
                    ActiveNpc.Add(preset.PresetName, new HashSet<ScientistNPC>());
                    DeadNpc.Add(preset.PresetName, 0);
                    int amount = GetShouldAmountNpc(preset);
                    for (int i = 0; i < amount; i++) SpawnNpc(preset);
                }
            }

            internal void Destroy()
            {
                foreach (KeyValuePair<string, HashSet<ScientistNPC>> kvp in ActiveNpc)
                {
                    foreach (ScientistNPC scientistNpc in kvp.Value)
                        if (scientistNpc.IsExists())
                            scientistNpc.Kill();
                    kvp.Value.Clear();
                }
                ActiveNpc.Clear();
                ActiveNpc = null;

                DeadNpc.Clear();
                DeadNpc = null;

                Cargo = null;

                Spawners.Clear();
                Spawners = null;

                Presets.Clear();
                Presets = null;
            }

            private void UpdateSpawners(CargoShip cargo)
            {
                Spawners ??= new Queue<NPCSpawner>();
                Spawners.Clear();

                foreach (Component component in cargo.GetComponentsInChildren<Component>())
                    if (component is NPCSpawner spawner && spawner.AStarGraph != null)
                        Spawners.Enqueue(spawner);
            }

            internal void RespawnPresets()
            {
                DeadNpc.Clear();
                foreach (PresetConfig preset in Presets) DeadNpc.Add(preset.PresetName, 0);
                UpdatePopulation();
            }

            internal void UpdatePopulation()
            {
                foreach (PresetConfig preset in Presets)
                {
                    int shouldAmountNpc = GetShouldAmountNpc(preset);
                    int amountNpc = GetAmountNpc(preset.PresetName);
                    if (shouldAmountNpc > amountNpc)
                    {
                        int amount = shouldAmountNpc - amountNpc;
                        for (int i = 0; i < amount; i++) SpawnNpc(preset);
                    }
                    else if (shouldAmountNpc < amountNpc)
                    {
                        int amount = amountNpc - shouldAmountNpc;
                        for (int i = 0; i < amount; i++) KillNpc(preset.PresetName);
                    }
                }
            }

            private void SpawnNpc(PresetConfig preset)
            {
                (Vector3 local, Vector3 global, NPCSpawner spawner) = TryFindPosition(preset);
                if (global == Vector3.zero) return;

                ScientistNPC npc = (ScientistNPC)_.NpcSpawn.Call("SpawnPreset", global, preset.PresetName);
                if (npc == null) return;

                _.NpcSpawn.Call("SetParent", npc, transform, local, 0.5f);

                if (spawner != null)
                {
                    _.NextTick(() =>
                    {
                        BaseNavigator navigator = npc.Brain.Navigator;
                        navigator.CanUseNavMesh = false;
                        navigator.Path = spawner.Path;
                        navigator.AStarGraph = spawner.AStarGraph;
                        navigator.CanUseAStar = true;
                    });
                }

                ActiveNpc[preset.PresetName].Add(npc);

                _.TrySendNpcToPveMode(npc);
            }

            private (Vector3 local, Vector3 global, NPCSpawner spawner) TryFindPosition(PresetConfig preset)
            {
                bool stationary = (bool)_.NpcSpawn.Call("IsStationaryPreset", preset.PresetName);

                if (stationary)
                {
                    foreach (string str in preset.CustomPositions)
                    {
                        Vector3 local = str.ToVector3();
                        Vector3 global1 = transform.GetGlobalPosition(local);
                        if (HasNpcTooClose(global1)) continue;
                        return (local, global1, null);
                    }
                }

                NPCSpawner spawner = Spawners.Dequeue();
                Spawners.Enqueue(spawner);

                BasePathNode node = spawner.AStarGraph.nodes.GetRandom();
                Vector3 global = node.Position;

                return (transform.GetLocalPosition(global), global, spawner);

                bool HasNpcTooClose(Vector3 pos)
                {
                    foreach (ScientistNPC npc in ActiveNpc[preset.PresetName])
                    {
                        if (!npc.IsExists()) continue;
                        Vector3 npcPos = npc.transform.position;
                        float sqr = (npcPos - pos).sqrMagnitude;
                        if (sqr < SquareDistanceBetweenNpc) return true;
                    }
                    return false;
                }
            }

            private void KillNpc(string presetName)
            {
                if (DeadNpc[presetName] > 0)
                {
                    DeadNpc[presetName]--;
                    return;
                }

                int index = 0;
                int random = UnityEngine.Random.Range(0, ActiveNpc[presetName].Count);
                ScientistNPC npc = null;
                foreach (ScientistNPC scientistNpc in ActiveNpc[presetName])
                {
                    if (index == random)
                    {
                        npc = scientistNpc;
                        break;
                    }
                    index++;
                }
                if (npc == null) return;

                if (!npc.IsDestroyed) npc.Kill();
                ActiveNpc[presetName].Remove(npc);
            }

            internal void DieNpc(ScientistNPC npc)
            {
                string presetName = GetPresetName(npc);

                DeadNpc[presetName]++;
                ActiveNpc[presetName].Remove(npc);

                BasePlayer attacker = npc.lastAttacker as BasePlayer;
                if (attacker.IsPlayer()) _.SendBalance(attacker.userID, GetEconomics(presetName));
            }

            private Dictionary<string, double> GetEconomics(string presetName)
            {
                PresetConfig preset = Presets.FirstOrDefault(x => x.PresetName == presetName);
                if (preset == null) return null;
                return preset.Economics;
            }

            private int GetAmountNpc(string presetName) => ActiveNpc[presetName].Count + DeadNpc[presetName];
            private static int GetShouldAmountNpc(PresetConfig preset) => _.IsDay ? UnityEngine.Random.Range(preset.MinDay, preset.MaxDay + 1) : UnityEngine.Random.Range(preset.MinNight, preset.MaxNight + 1);

            private string GetPresetName(ScientistNPC npc)
            {
                foreach (KeyValuePair<string, HashSet<ScientistNPC>> kvp in ActiveNpc)
                    foreach (ScientistNPC scientistNpc in kvp.Value)
                        if (scientistNpc == npc)
                            return kvp.Key;
                return null;
            }

            internal bool Contains(ScientistNPC npc) => ActiveNpc.Any(x => x.Value.Contains(npc));

            internal Dictionary<string, HashSet<ScientistNPC>> ActiveNpc { get; set; } = new Dictionary<string, HashSet<ScientistNPC>>();
            private Dictionary<string, int> DeadNpc { get; set; } = new Dictionary<string, int>();
        }
        #endregion Controller

        #region Economy
        [PluginReference] private readonly Plugin Economics, ServerRewards, IQEconomic, XPerience;

        internal void SendBalance(ulong playerId, Dictionary<string, double> dic)
        {
            if (dic == null || dic.Count == 0) return;

            if (plugins.Exists("Economics") && TryGetAmount(dic, out double amount1, "economics"))
                Economics?.Call("Deposit", playerId.ToString(), amount1);

            if (plugins.Exists("ServerRewards") && TryGetAmount(dic, out double amount2, "serverrewards"))
            {
                int amount = amount2.SafeToInt();
                if (amount > 0) ServerRewards?.Call("AddPoints", playerId, amount);
            }

            if (plugins.Exists("IQEconomic") && TryGetAmount(dic, out double amount3, "iqeconomic"))
            {
                int amount = amount3.SafeToInt();
                if (amount > 0) IQEconomic?.Call("API_SET_BALANCE", playerId, amount);
            }

            if (plugins.Exists("XPerience") && TryGetAmount(dic, out double amount4, "xperience"))
            {
                BasePlayer player = BasePlayer.FindByID(playerId);
                if (player != null) XPerience?.Call("GiveXP", player, amount4);
            }
        }

        private static bool TryGetAmount(Dictionary<string, double> dic, out double amount, string alias)
        {
            amount = 0d;

            foreach (KeyValuePair<string, double> kvp in dic)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key)) continue;
                if (kvp.Value < 0.001d) continue;
                string keyNorm = kvp.Key.Trim().ToLower();
                if (keyNorm != alias) continue;
                amount = kvp.Value;
                return true;
            }

            return false;
        }
        #endregion Economy

        #region Initialization
        private Coroutine InitializationCoroutine { get; set; } = null;

        private IEnumerator PluginInitialization()
        {
            PrintWarning("Starting plugin initialization process...");

            string path = Interface.Oxide.DataDirectory + "/BetterNpc/";
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);

            yield return LoadMonumentSpawnPoints();
            PrintWarning("Plugin loading progress at 8%");

            yield return LoadUnderwaterLabSpawnPoints();
            PrintWarning("Plugin loading progress at 14%");

            yield return LoadTunnelSpawnPoints();
            PrintWarning("Plugin loading progress at 23%");

            LoadIds();
            yield return LoadCustomSpawnPoints();
            PrintWarning("Plugin loading progress at 27%");

            yield return LoadEventSpawnPoints();
            PrintWarning("Plugin loading progress at 28%");

            yield return LoadRoadSpawnPoints();
            PrintWarning("Plugin loading progress at 29%");

            yield return LoadBiomeSpawnPoints();
            PrintWarning("Plugin loading progress at 30%");

            yield return SpawnMonumentSpawnPoints();
            PrintWarning("Plugin loading progress at 46%");

            yield return SpawnUnderwaterLabSpawnPoints();
            PrintWarning("Plugin loading progress at 58%");

            yield return SpawnTunnelSpawnPoints();
            PrintWarning("Plugin loading progress at 74%");

            yield return SpawnCustomSpawnPoints();
            PrintWarning("Plugin loading progress at 82%");

            yield return SpawnRoadSpawnPoints();
            PrintWarning("Plugin loading progress at 85%");

            yield return SpawnBiomeSpawnPoints();
            PrintWarning("Plugin loading progress at 90%");

            StartCheckDay();
            StartRespawnCounter();
            PrintWarning("Plugin loading progress at 92%");

            if (Cfg.SpawnGroupFill)
            {
                SpawnHandler handler = SingletonComponent<SpawnHandler>.Instance;
                if (handler != null) handler.FillGroups();
                if (!Cfg.EnabledMinLogs) Puts("All spawn groups have been successfully filled!");
            }
            PrintWarning("Plugin loading progress at 96%");

            if (Cfg.PuzzleReset)
            {
                PuzzleReset[] puzzleResetArray = UnityEngine.Object.FindObjectsOfType<PuzzleReset>();
                for (int i = 0; i < puzzleResetArray.Length; i++)
                {
                    PuzzleReset puzzleReset = puzzleResetArray[i];
                    puzzleReset.DoReset();
                    puzzleReset.ResetTimer();
                }
                if (!Cfg.EnabledMinLogs) Puts("All puzzles have been successfully reset!");
            }

            PrintWarning("Completed plugin initialization successfully!");
        }
        #endregion Initialization

        #region Analyzing Data Files
        private void AnalyzingCustomMonumentSpawnPoint(CustomMonumentSpawnPoint spawnPoint, string name)
        {
            if (string.IsNullOrWhiteSpace(spawnPoint.MapMarkerName)) spawnPoint.MapMarkerName = string.Empty;

            if (!spawnPoint.Position.CorrectVector3()) spawnPoint.Position = string.Empty;
            if (!spawnPoint.Rotation.CorrectVector3()) spawnPoint.Rotation = string.Empty;

            if (spawnPoint.Position != string.Empty && spawnPoint.Rotation == string.Empty) spawnPoint.Rotation = "(0.0, 0.0, 0.0)";

            if (spawnPoint.MapMarkerName == string.Empty && spawnPoint.Position == string.Empty) spawnPoint.Enabled = false;

            AnalyzingMonumentSpawnPoint(spawnPoint, name);
        }

        private void AnalyzingMonumentSpawnPoint(MonumentSpawnPoint spawnPoint, string name)
        {
            if (!spawnPoint.Size.CorrectVector3()) spawnPoint.Size = "(9.0, 9.0, 9.0)";
            AnalyzingSpawnPoint(spawnPoint, name);
        }

        private void AnalyzingEventSpawnPoint(EventSpawnPoint spawnPoint, string name)
        {
            if (spawnPoint.Radius < 9f) spawnPoint.Radius = 9f;
            AnalyzingSpawnPoint(spawnPoint, name);
        }

        private void AnalyzingSpawnPoint(SpawnPoint spawnPoint, string name)
        {
            spawnPoint.Presets ??= new List<PresetConfig>();
            foreach (PresetConfig preset in spawnPoint.Presets) AnalyzingPreset(preset);
            if (spawnPoint.Enabled && (spawnPoint.Presets.Count == 0 || !spawnPoint.Presets.Any(x => x.Enabled))) spawnPoint.Enabled = false;
            TryRegisterPresetsUsage(spawnPoint, name);
        }

        private static void AnalyzingPreset(PresetConfig preset)
        {
            if (preset.RespawnMaxTime < preset.RespawnMinTime) preset.RespawnMaxTime = preset.RespawnMinTime;

            preset.Economics ??= new Dictionary<string, double>();
            preset.CustomPositions ??= new List<string>();

            for (int i = preset.CustomPositions.Count - 1; i >= 0; i--)
            {
                string str = preset.CustomPositions[i];
                if (str.CorrectVector3()) continue;
                preset.CustomPositions.Remove(str);
            }

            int amountPositions = preset.CustomPositions.Count;

            if (preset.SpawnType != SpawnType.Random && preset.SpawnType != SpawnType.Custom)
                preset.SpawnType = amountPositions == 0 ? SpawnType.Random : SpawnType.Custom;

            if (amountPositions == 0 && preset.SpawnType == SpawnType.Custom) preset.SpawnType = SpawnType.Random;

            if (preset.SpawnType == SpawnType.Custom)
            {
                if (preset.MinDay > amountPositions) preset.MinDay = amountPositions;
                if (preset.MaxDay > amountPositions) preset.MaxDay = amountPositions;
                if (preset.MinNight > amountPositions) preset.MinNight = amountPositions;
                if (preset.MaxNight > amountPositions) preset.MaxNight = amountPositions;
            }

            if (preset.MaxDay < preset.MinDay) preset.MaxDay = preset.MinDay;
            if (preset.MaxNight < preset.MinNight) preset.MaxNight = preset.MinNight;

            if (preset.Enabled)
            {
                if (preset.MinDay == 0 && preset.MaxDay == 0 && preset.MinNight == 0 && preset.MaxNight == 0) preset.Enabled = false;
                if (string.IsNullOrWhiteSpace(preset.PresetName)) preset.Enabled = false;
            }
        }
        #endregion Analyzing Data Files

        #region Monuments
        private Dictionary<string, HashSet<string>> DefaultScientists { get; set; } = new Dictionary<string, HashSet<string>>
        {
            ["Abandoned Military Base A"] = new HashSet<string>
            {
                "scientistnpc_roamtethered"
            },
            ["Abandoned Military Base B"] = new HashSet<string>
            {
                "scientistnpc_roamtethered"
            },
            ["Abandoned Military Base C"] = new HashSet<string>
            {
                "scientistnpc_roamtethered"
            },
            ["Abandoned Military Base D"] = new HashSet<string>
            {
                "scientistnpc_roamtethered"
            },
            ["Giant Excavator Pit"] = new HashSet<string>
            {
                "scientistnpc_excavator"
            },
            ["Military Tunnel"] = new HashSet<string>
            {
                "scientistnpc_full_lr300",
                "scientistnpc_full_shotgun"
            },
            ["Oil Rig"] = new HashSet<string>
            {
                "scientistnpc_oilrig",
                "scientist2"
            },
            ["Large Oil Rig"] = new HashSet<string>
            {
                "scientistnpc_oilrig",
                "scientist2"
            },
            ["Missile Silo"] = new HashSet<string>
            {
                "scientistnpc_roam",
                "scientistnpc_roam_nvg_variant"
            },
            ["Arctic Research Base"] = new HashSet<string>
            {
                "scientistnpc_roam",
                "scientistnpc_roamtethered",
                "scientistnpc_patrol"
            },
            ["Launch Site"] = new HashSet<string>
            {
                "scientistnpc_patrol"
            },
            ["Airfield"] = new HashSet<string>
            {
                "scientistnpc_patrol"
            },
            ["Train Yard"] = new HashSet<string>
            {
                "scientistnpc_patrol"
            },
            ["Tunnel"] = new HashSet<string>
            {
                "npc_tunneldweller"
            },
            ["Underwater Lab"] = new HashSet<string>
            {
                "npc_underwaterdweller"
            }
        };

        private static string GetNameMonument(MonumentInfo monument)
        {
            if (monument == null || monument.name == null || monument.displayPhrase?.english == null) return string.Empty;

            string name = monument.displayPhrase.english.Replace("\n", string.Empty);

            if (monument.name.Contains("harbor_1")) return "Small " + name;
            else if (monument.name.Contains("harbor_2")) return "Large " + name;
            else if (monument.name.Contains("desert_military_base_a")) return name + " A";
            else if (monument.name.Contains("desert_military_base_b")) return name + " B";
            else if (monument.name.Contains("desert_military_base_c")) return name + " C";
            else if (monument.name.Contains("desert_military_base_d")) return name + " D";
            else return name;
        }

        private void ClearVariablesForMonuments()
        {
            MonumentSpawnPoints.Clear();
            MonumentSpawnPoints = null;

            UnderwaterLabSpawnPoints.Clear();
            UnderwaterLabSpawnPoints = null;

            TunnelSpawnPoints.Clear();
            TunnelSpawnPoints = null;
        }

        private Dictionary<string, MonumentSpawnPoint> MonumentSpawnPoints { get; set; } = new Dictionary<string, MonumentSpawnPoint>();

        private IEnumerator LoadMonumentSpawnPoints()
        {
            string shortPath = "BetterNpc/Monument/";

            string path = Interface.Oxide.DataDirectory + "/" + shortPath;
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);

            foreach (string filePath in Interface.Oxide.DataFileSystem.GetFiles(shortPath))
            {
                string fileName = filePath.GetFileName();
                MonumentSpawnPoint spawnPoint = Interface.Oxide.DataFileSystem.ReadObject<MonumentSpawnPoint>($"{shortPath}{fileName}");
                if (spawnPoint != null)
                {
                    if (!Cfg.EnabledMinLogs) Puts($"File {fileName} has been loaded successfully!");
                    AnalyzingMonumentSpawnPoint(spawnPoint, fileName);
                    MonumentSpawnPoints.Add(fileName, spawnPoint);
                }
                else PrintError($"File {fileName} is corrupted and cannot be loaded!");
                yield return CoroutineEx.waitForSeconds(0.1f);
            }
        }

        private IEnumerator SpawnMonumentSpawnPoints()
        {
            foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
            {
                string monumentName = GetNameMonument(monument);
                if (string.IsNullOrEmpty(monumentName)) continue;

                if (!MonumentSpawnPoints.TryGetValue(monumentName, out MonumentSpawnPoint spawnPoint)) continue;
                if (!spawnPoint.Enabled) continue;

                ControllerSpawnPoint controller = new GameObject().AddComponent<ControllerSpawnPoint>();
                controller.Init(spawnPoint, monument.transform.position, monument.transform.rotation, monumentName);
                Controllers.Add(controller);

                if (!Cfg.EnabledMinLogs) Puts($"Monument {monumentName} has been successfully loaded!");

                yield return CoroutineEx.waitForSeconds(0.2f);
            }
        }

        private Dictionary<string, MonumentSpawnPoint> UnderwaterLabSpawnPoints { get; set; } = new Dictionary<string, MonumentSpawnPoint>();

        private IEnumerator LoadUnderwaterLabSpawnPoints()
        {
            string shortPath = "BetterNpc/Monument/Underwater Lab/";

            string path = Interface.Oxide.DataDirectory + "/" + shortPath;
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);

            foreach (string filePath in Interface.Oxide.DataFileSystem.GetFiles(shortPath))
            {
                string fileName = filePath.GetFileName();
                MonumentSpawnPoint spawnPoint = Interface.Oxide.DataFileSystem.ReadObject<MonumentSpawnPoint>($"{shortPath}{fileName}");
                if (spawnPoint != null)
                {
                    if (!Cfg.EnabledMinLogs) Puts($"File {fileName} has been loaded successfully!");
                    AnalyzingMonumentSpawnPoint(spawnPoint, fileName);
                    UnderwaterLabSpawnPoints.Add(fileName, spawnPoint);
                }
                else PrintError($"File {fileName} is corrupted and cannot be loaded!");
                yield return CoroutineEx.waitForSeconds(0.1f);
            }
        }

        private IEnumerator SpawnUnderwaterLabSpawnPoints()
        {
            foreach (DungeonBaseInfo baseModule in TerrainMeta.Path.DungeonBaseEntrances)
            {
                if (UnderwaterLabSpawnPoints.TryGetValue(baseModule.name, out MonumentSpawnPoint spawnPoint1))
                    yield return SpawnUnderwaterLabSpawnPoint(baseModule.name, baseModule.transform, spawnPoint1);

                foreach (GameObject module in baseModule.Links)
                {
                    string moduleName = module.name.GetFileName();
                    if (UnderwaterLabSpawnPoints.TryGetValue(moduleName, out MonumentSpawnPoint spawnPoint2))
                        yield return SpawnUnderwaterLabSpawnPoint(moduleName, module.transform, spawnPoint2);
                }
            }
        }

        private IEnumerator SpawnUnderwaterLabSpawnPoint(string moduleName, Transform transform, MonumentSpawnPoint spawnPoint)
        {
            if (!spawnPoint.Enabled) yield break;

            ControllerSpawnPoint controller = new GameObject().AddComponent<ControllerSpawnPoint>();
            controller.IsUnderwaterLab = true;
            controller.Init(spawnPoint, transform.position, transform.rotation, moduleName);
            Controllers.Add(controller);

            if (!Cfg.EnabledMinLogs) Puts($"Underwater Module {moduleName} has been successfully loaded!");

            yield return CoroutineEx.waitForSeconds(0.2f);
        }

        private Dictionary<string, MonumentSpawnPoint> TunnelSpawnPoints { get; set; } = new Dictionary<string, MonumentSpawnPoint>();

        private IEnumerator LoadTunnelSpawnPoints()
        {
            string shortPath = "BetterNpc/Monument/Tunnel/";

            string path = Interface.Oxide.DataDirectory + "/" + shortPath;
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);

            foreach (string filePath in Interface.Oxide.DataFileSystem.GetFiles(shortPath))
            {
                string fileName = filePath.GetFileName();
                MonumentSpawnPoint spawnPoint = Interface.Oxide.DataFileSystem.ReadObject<MonumentSpawnPoint>($"{shortPath}{fileName}");
                if (spawnPoint != null)
                {
                    if (!Cfg.EnabledMinLogs) Puts($"File {fileName} has been loaded successfully!");
                    AnalyzingMonumentSpawnPoint(spawnPoint, fileName);
                    TunnelSpawnPoints.Add(fileName, spawnPoint);
                }
                else PrintError($"File {fileName} is corrupted and cannot be loaded!");
                yield return CoroutineEx.waitForSeconds(0.1f);
            }
        }

        private IEnumerator SpawnTunnelSpawnPoints()
        {
            foreach (DungeonGridCell gridCell in TerrainMeta.Path.DungeonGridCells)
            {
                string cellName = gridCell.name.GetFileName();

                if (!TunnelSpawnPoints.TryGetValue(cellName, out MonumentSpawnPoint spawnPoint)) continue;
                if (!spawnPoint.Enabled) continue;

                ControllerSpawnPoint controller = new GameObject().AddComponent<ControllerSpawnPoint>();
                controller.IsTunnel = true;
                controller.Init(spawnPoint, gridCell.transform.position, gridCell.transform.rotation, cellName);
                Controllers.Add(controller);

                if (!Cfg.EnabledMinLogs) Puts($"Tunnel Module {cellName} has been successfully loaded!");

                yield return CoroutineEx.waitForSeconds(0.2f);
            }
        }
        #endregion Monuments

        #region Custom
        private Dictionary<string, CustomMonumentSpawnPoint> CustomSpawnPoints { get; set; } = new Dictionary<string, CustomMonumentSpawnPoint>();

        private void ClearVariablesForCustomMonuments()
        {
            CustomSpawnPoints.Clear();
            CustomSpawnPoints = null;

            if (Ids != null)
            {
                Ids.Clear();
                Ids = null;
            }
        }

        private IEnumerator LoadCustomSpawnPoints()
        {
            string shortPath = "BetterNpc/Custom/";

            string path = Interface.Oxide.DataDirectory + "/" + shortPath;
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);

            foreach (string name in Interface.Oxide.DataFileSystem.GetFiles(shortPath))
            {
                string fileName = name.GetFileName();

                CustomMonumentSpawnPoint spawnPoint = Interface.Oxide.DataFileSystem.ReadObject<CustomMonumentSpawnPoint>($"{shortPath}{fileName}");
                if (spawnPoint == null)
                {
                    PrintError($"File {fileName} is corrupted and cannot be loaded!");
                    continue;
                }

                if (!spawnPoint.Id.AreEqual(0f) && !Ids.Any(x => x.AreEqual(spawnPoint.Id)))
                {
                    if (!Cfg.EnabledMinLogs) PrintWarning($"File {fileName} cannot be loaded on the current map!");
                    continue;
                }

                if (!Cfg.EnabledMinLogs) Puts($"File {fileName} has been loaded successfully!");

                AnalyzingCustomMonumentSpawnPoint(spawnPoint, fileName);
                CustomSpawnPoints.Add(fileName, spawnPoint);

                yield return CoroutineEx.waitForSeconds(0.1f);
            }
        }

        private IEnumerator SpawnCustomSpawnPoints()
        {
            foreach (KeyValuePair<string, CustomMonumentSpawnPoint> kvp in CustomSpawnPoints)
            {
                if (!kvp.Value.Enabled) continue;

                (Vector3 pos, Quaternion rot) = GetLocationCustomSpawnPoint(kvp.Value);
                if (pos == Vector3.zero) continue;

                ControllerSpawnPoint controller = new GameObject().AddComponent<ControllerSpawnPoint>();
                controller.Init(kvp.Value, pos, rot, kvp.Key);
                Controllers.Add(controller);

                if (!Cfg.EnabledMinLogs) Puts($"Custom location {kvp.Key} has been successfully loaded!");

                yield return CoroutineEx.waitForSeconds(0.2f);
            }
        }

        private static (Vector3 pos, Quaternion rot) GetLocationCustomSpawnPoint(CustomMonumentSpawnPoint spawnPoint)
        {
            Vector3 pos = Vector3.zero;
            Quaternion rot = Quaternion.identity;

            if (string.IsNullOrEmpty(spawnPoint.MapMarkerName))
            {
                pos = spawnPoint.Position.ToVector3();
                rot = Quaternion.Euler(spawnPoint.Rotation.ToVector3());
            }
            else
            {
                foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
                {
                    if (!monument.name.Contains("monument_marker.prefab")) continue;

                    string monumentName = monument.transform.root.name;
                    if (string.IsNullOrWhiteSpace(monumentName) || !EqualsIgnoreWhitespace(monumentName, spawnPoint.MapMarkerName)) continue;

                    pos = monument.transform.position;
                    rot = monument.transform.rotation;

                    break;
                }

                if (pos == Vector3.zero && !string.IsNullOrEmpty(spawnPoint.Position))
                {
                    pos = spawnPoint.Position.ToVector3();
                    rot = Quaternion.Euler(spawnPoint.Rotation.ToVector3());
                }
            }

            return (pos, rot);
        }

        private HashSet<float> Ids { get; set; } = new HashSet<float>();
        private void LoadIds()
        {
            foreach (BaseNetworkable baseNetworkable in BaseNetworkable.serverEntities)
            {
                if (baseNetworkable is not RANDSwitch randSwitch) continue;
                Vector3 pos = randSwitch.transform.position;
                Ids.Add(pos.x + pos.y + pos.z);
            }
        }
        #endregion Custom

        #region Events
        private Dictionary<string, EventSpawnPoint> EventSpawnPoints { get; set; } = new Dictionary<string, EventSpawnPoint>();

        private void ClearVariablesForEvents()
        {
            EventSpawnPoints.Clear();
            EventSpawnPoints = null;

            if (CargoPlanesSignaled != null)
            {
                CargoPlanesSignaled.Clear();
                CargoPlanesSignaled = null;
            }

            if (Ch47Crates != null)
            {
                Ch47Crates.Clear();
                Ch47Crates = null;
            }

            if (BradleyCrates != null)
            {
                BradleyCrates.Clear();
                BradleyCrates = null;
            }

            if (HelicopterCrates != null)
            {
                HelicopterCrates.Clear();
                HelicopterCrates = null;
            }

            CargoConfig = null;
        }

        private IEnumerator LoadEventSpawnPoints()
        {
            string shortPath = "BetterNpc/Event/";

            string path = Interface.Oxide.DataDirectory + "/" + shortPath;
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);

            foreach (string filePath in Interface.Oxide.DataFileSystem.GetFiles(shortPath))
            {
                string fileName = filePath.GetFileName();
                if (fileName == "CargoShip")
                {
                    CargoConfig = Interface.Oxide.DataFileSystem.ReadObject<CargoSpawnPoint>($"{shortPath}{fileName}");
                    if (CargoConfig == null) PrintError($"File {fileName} is corrupted and cannot be loaded!");
                    else
                    {
                        foreach (PresetConfig preset in CargoConfig.Presets) preset.IsCargoFile = true;
                        if (!Cfg.EnabledMinLogs) Puts($"File {fileName} has been loaded successfully!");
                        AnalyzingSpawnPoint(CargoConfig, fileName);
                        if (CargoConfig is { RemoveDefaultNpc: true, Enabled: true }) ConVar.AI.npc_spawn_on_cargo_ship = false;
                    }
                }
                else
                {
                    EventSpawnPoint spawnPoint = fileName == "Bradley" ? Interface.Oxide.DataFileSystem.ReadObject<BradleySpawnPoint>($"{shortPath}{fileName}") : Interface.Oxide.DataFileSystem.ReadObject<EventSpawnPoint>($"{shortPath}{fileName}");
                    if (spawnPoint != null)
                    {
                        foreach (PresetConfig preset in spawnPoint.Presets) preset.IsEventFile = true;
                        if (!Cfg.EnabledMinLogs) Puts($"File {fileName} has been loaded successfully!");
                        AnalyzingEventSpawnPoint(spawnPoint, fileName);
                        EventSpawnPoints.Add(fileName, spawnPoint);
                    }
                    else PrintError($"File {fileName} is corrupted and cannot be loaded!");
                }
                yield return CoroutineEx.waitForSeconds(0.1f);
            }

            if (!EventSpawnPoints.TryGetValue("AirDrop", out EventSpawnPoint spawnPoint1) || !spawnPoint1.Enabled) DisableAirDrop();
            if (!EventSpawnPoints.TryGetValue("CH47", out EventSpawnPoint spawnPoint2) || !spawnPoint2.Enabled) DisableCh47();
            if (!EventSpawnPoints.TryGetValue("Bradley", out EventSpawnPoint spawnPoint3) || !spawnPoint3.Enabled) DisableBradley();
            if (!EventSpawnPoints.TryGetValue("Helicopter", out EventSpawnPoint spawnPoint4) || !spawnPoint4.Enabled) DisableHelicopter();
            if (CargoConfig is not { Enabled: true }) DisableCargoShip();
        }

        #region AirDrop
        private HashSet<CargoPlane> CargoPlanesSignaled { get; set; } = new HashSet<CargoPlane>();

        private void DisableAirDrop()
        {
            Unsubscribe("OnCargoPlaneSignaled");
            Unsubscribe("OnSupplyDropDropped");
            Unsubscribe("OnSupplyDropDropped");
            CargoPlanesSignaled.Clear();
            CargoPlanesSignaled = null;
        }

        private void OnCargoPlaneSignaled(CargoPlane cargoPlane, SupplySignal supplySignal)
        {
            if (cargoPlane == null) return;
            if (!CargoPlanesSignaled.Contains(cargoPlane))
                CargoPlanesSignaled.Add(cargoPlane);
        }

        private void OnSupplyDropDropped(SupplyDrop supplyDrop, CargoPlane cargoPlane)
        {
            if (supplyDrop == null || supplyDrop.net == null || cargoPlane == null) return;

            if (CargoPlanesSignaled.Contains(cargoPlane))
            {
                CargoPlanesSignaled.Remove(cargoPlane);
                return;
            }

            if (!EventSpawnPoints.TryGetValue("AirDrop", out EventSpawnPoint spawnPoint)) return;

            if (Interface.CallHook("CanAirDropSpawnNpc", supplyDrop) is bool) return;

            Vector3 pos = supplyDrop.transform.position;
            pos.y = TerrainMeta.HeightMap.GetHeight(pos);

            MonumentInfo monument = TerrainMeta.Path.Monuments.FirstOrDefault(x => GetNameMonument(x) == "Giant Excavator Pit");
            if (monument != null)
            {
                Vector3 localPos = monument.transform.GetLocalPosition(pos);
                if (localPos.x is > -110f and < 110f && localPos.y is > -40f and < 40f && localPos.z is > -90f and < 90f) return;
            }

            ControllerSpawnPoint controller = new GameObject().AddComponent<ControllerSpawnPoint>();
            controller.Init(spawnPoint, pos, Quaternion.identity, supplyDrop.net.ID.Value.ToString());
            Controllers.Add(controller);
        }

        private void OnEntityKill(SupplyDrop supplyDrop)
        {
            if (supplyDrop == null || supplyDrop.net == null || CargoPlanesSignaled == null) return;

            ControllerSpawnPoint controller = Controllers.FirstOrDefault(x => x.Name == supplyDrop.net.ID.Value.ToString());
            if (controller == null) return;

            Controllers.Remove(controller);
            UnityEngine.Object.Destroy(controller.gameObject);
        }
        #endregion AirDrop

        #region CH47
        public class CrateCh47
        {
            public string Name { get; set; }
            public Vector3 Position { get; set; }
            public HackableLockedCrate Crate { get; set; }
        }
        private HashSet<CrateCh47> Ch47Crates { get; set; } = new HashSet<CrateCh47>();

        private void DisableCh47()
        {
            Unsubscribe("OnHelicopterDropCrate");
            Ch47Crates.Clear();
            Ch47Crates = null;
        }

        private void OnHelicopterDropCrate(CH47HelicopterAIController ai)
        {
            if (ai == null || ai.net == null) return;

            if (Interface.CallHook("CanCh47SpawnNpc", ai) is bool) return;

            Vector3 pos = ai.transform.position;
            pos.y = TerrainMeta.HeightMap.GetHeight(pos);

            if (!EventSpawnPoints.TryGetValue("CH47", out EventSpawnPoint spawnPoint)) return;

            string name = ai.net.ID.Value.ToString();

            ControllerSpawnPoint controller = new GameObject().AddComponent<ControllerSpawnPoint>();
            controller.Init(spawnPoint, pos, Quaternion.identity, name);
            Controllers.Add(controller);

            Ch47Crates.Add(new CrateCh47 { Name = name, Position = pos, Crate = null });
        }

        private void OnEntitySpawned(HackableLockedCrate crate)
        {
            if (crate == null || Ch47Crates == null) return;

            Vector3 pos = crate.transform.position;
            pos.y = TerrainMeta.HeightMap.GetHeight(crate.transform.position);

            CrateCh47 crateCh47 = Ch47Crates.FirstOrDefault(x => x.Crate == null && Vector3.Distance(x.Position, pos) < EventSpawnPoints["CH47"].Radius);
            if (crateCh47 == null) return;

            crateCh47.Crate = crate;
        }

        private void OnEntityKill(HackableLockedCrate crate)
        {
            if (crate == null || Ch47Crates == null) return;

            CrateCh47 crateCh47 = Ch47Crates.FirstOrDefault(x => x.Crate == crate);
            if (crateCh47 == null) return;
            Ch47Crates.Remove(crateCh47);

            ControllerSpawnPoint controller = Controllers.FirstOrDefault(x => x.Name == crateCh47.Name);
            if (controller == null) return;

            Controllers.Remove(controller);
            UnityEngine.Object.Destroy(controller.gameObject);
        }
        #endregion CH47

        #region Bradley and Helicopter
        private Dictionary<ulong, HashSet<ulong>> BradleyCrates { get; set; } = new Dictionary<ulong, HashSet<ulong>>();
        private Dictionary<ulong, HashSet<ulong>> HelicopterCrates { get; set; } = new Dictionary<ulong, HashSet<ulong>>();

        private void DisableBradley()
        {
            Unsubscribe("CanDeployScientists");
            BradleyCrates.Clear();
            BradleyCrates = null;
        }

        private void DisableHelicopter()
        {
            HelicopterCrates.Clear();
            HelicopterCrates = null;
        }

        private object CanDeployScientists(BradleyAPC bradley, BaseEntity attacker, List<GameObjectRef> scientistPrefabs, List<Vector3> spawnPositions)
        {
            if (bradley == null || bradley.net == null || BradleyCrates == null) return null;

            ulong id = bradley.net.ID.Value;
            if (!BradleyCrates.ContainsKey(id)) return null;

            if (!EventSpawnPoints.TryGetValue("Bradley", out EventSpawnPoint spawnPoint)) return null;
            BradleySpawnPoint bradleySpawnPoint = spawnPoint as BradleySpawnPoint;
            if (bradleySpawnPoint == null) return null;

            if (bradleySpawnPoint.RemoveDefaultNpc) return false;

            return null;
        }

        private void OnEntityDeath(BradleyAPC bradley, HitInfo info)
        {
            if (bradley == null || bradley.net == null || BradleyCrates == null) return;

            if (Interface.CallHook("CanBradleySpawnNpc", bradley) is bool) return;

            Vector3 pos = bradley.transform.position;
            pos.y = TerrainMeta.HeightMap.GetHeight(pos);

            if (!EventSpawnPoints.TryGetValue("Bradley", out EventSpawnPoint spawnPoint)) return;

            ulong id = bradley.net.ID.Value;
            string name = id.ToString();

            ControllerSpawnPoint controller = new GameObject().AddComponent<ControllerSpawnPoint>();
            controller.Init(spawnPoint, pos, Quaternion.identity, name);
            Controllers.Add(controller);

            BradleyCrates.Add(id, new HashSet<ulong>());
        }

        private void OnEntityDeath(PatrolHelicopter helicopter, HitInfo info)
        {
            if (helicopter == null || helicopter.net == null || HelicopterCrates == null) return;

            if (Interface.CallHook("CanHelicopterSpawnNpc", helicopter) is bool) return;

            Vector3 pos = helicopter.transform.position;
            pos.y = TerrainMeta.HeightMap.GetHeight(pos);

            if (!EventSpawnPoints.TryGetValue("Helicopter", out EventSpawnPoint spawnPoint)) return;

            ulong id = helicopter.net.ID.Value;
            string name = id.ToString();

            ControllerSpawnPoint controller = new GameObject().AddComponent<ControllerSpawnPoint>();
            controller.Init(spawnPoint, pos, Quaternion.identity, name);
            Controllers.Add(controller);

            HelicopterCrates.Add(id, new HashSet<ulong>());
        }

        private void OnCrateSpawned(BradleyAPC bradley, LockedByEntCrate crate)
        {
            if (BradleyCrates == null) return;
            if (crate == null || crate.net == null) return;
            if (bradley == null || bradley.net == null) return;
            if (BradleyCrates.TryGetValue(bradley.net.ID.Value, out HashSet<ulong> crates)) crates.Add(crate.net.ID.Value);
        }

        private void OnCrateSpawned(PatrolHelicopter helicopter, LockedByEntCrate crate)
        {
            if (HelicopterCrates == null) return;
            if (crate == null || crate.net == null) return;
            if (helicopter == null || helicopter.net == null) return;
            if (HelicopterCrates.TryGetValue(helicopter.net.ID.Value, out HashSet<ulong> crates)) crates.Add(crate.net.ID.Value);
        }

        private void OnEntityKill(LockedByEntCrate crate)
        {
            if (crate == null || crate.net == null) return;

            ulong crateId = crate.net.ID.Value;
            string name = string.Empty;

            if (crate.ShortPrefabName == "bradley_crate")
            {
                if (BradleyCrates == null) return;

                ulong bradleyId = 0;
                foreach (KeyValuePair<ulong, HashSet<ulong>> kvp in BradleyCrates)
                {
                    foreach (ulong id in kvp.Value)
                    {
                        if (id != crateId) continue;
                        bradleyId = kvp.Key;
                        break;
                    }
                    if (bradleyId != 0) break;
                }
                if (bradleyId == 0) return;

                HashSet<ulong> crates = BradleyCrates[bradleyId];

                crates.Remove(crateId);
                if (crates.Count > 0) return;

                crates = null;
                BradleyCrates.Remove(bradleyId);

                name = bradleyId.ToString();
            }
            else if (crate.ShortPrefabName == "heli_crate")
            {
                if (HelicopterCrates == null) return;

                ulong helicopterId = 0;
                foreach (KeyValuePair<ulong, HashSet<ulong>> kvp in HelicopterCrates)
                {
                    foreach (ulong id in kvp.Value)
                    {
                        if (id != crateId) continue;
                        helicopterId = kvp.Key;
                        break;
                    }
                    if (helicopterId != 0) break;
                }
                if (helicopterId == 0) return;

                HashSet<ulong> crates = HelicopterCrates[helicopterId];

                crates.Remove(crateId);
                if (crates.Count > 0) return;

                crates = null;
                HelicopterCrates.Remove(helicopterId);

                name = helicopterId.ToString();
            }

            if (string.IsNullOrEmpty(name)) return;

            ControllerSpawnPoint controller = Controllers.FirstOrDefault(x => x.Name == name);
            if (controller == null) return;

            Controllers.Remove(controller);
            UnityEngine.Object.Destroy(controller.gameObject);
        }
        #endregion Bradley and Helicopter

        #region CargoShip
        private CargoSpawnPoint CargoConfig { get; set; } = null;
        private HashSet<CargoControllerSpawnPoint> CargoControllers { get; set; } = new HashSet<CargoControllerSpawnPoint>();

        private void DisableCargoShip()
        {
            Unsubscribe("OnCargoShipSpawnCrate");
            Unsubscribe("OnCargoShipHarborArrived");
            CargoControllers.Clear();
            CargoControllers = null;
        }

        private void OnEntitySpawned(CargoShip cargo)
        {
            if (cargo == null || CargoConfig is not { Enabled: true }) return;

            if (Interface.CallHook("CanCargoShipSpawnNpc", cargo) is bool) return;

            CargoControllerSpawnPoint controller = new CargoControllerSpawnPoint();
            controller.Init(CargoConfig, cargo);
            CargoControllers.Add(controller);
        }

        private void OnEntityKill(CargoShip cargo)
        {
            if (cargo == null || CargoConfig is not { Enabled: true }) return;

            CargoControllerSpawnPoint controller = CargoControllers.FirstOrDefault(x => x.Cargo == cargo);
            if (controller == null) return;

            controller.Destroy();
            CargoControllers.Remove(controller);
        }

        private object OnCargoShipSpawnCrate(CargoShip cargo)
        {
            if (cargo == null || CargoConfig is not { RespawnNpcCrates: true }) return null;

            CargoControllerSpawnPoint controller = CargoControllers.FirstOrDefault(x => x.Cargo == cargo);
            if (controller == null) return null;

            controller.RespawnPresets();

            return null;
        }

        private void OnCargoShipHarborArrived(CargoShip cargo)
        {
            if (cargo == null || !CargoConfig.RespawnNpcHarbor) return;

            CargoControllerSpawnPoint controller = CargoControllers.FirstOrDefault(x => x.Cargo == cargo);
            if (controller == null) return;

            controller.RespawnPresets();
        }
        #endregion CargoShip 
        #endregion Events

        #region Roads
        private Dictionary<string, RoadOrBiomeSpawnPoint> RoadSpawnPoints { get; set; } = new Dictionary<string, RoadOrBiomeSpawnPoint>();

        private IEnumerator LoadRoadSpawnPoints()
        {
            string shortPath = "BetterNpc/Road/";

            string path = Interface.Oxide.DataDirectory + "/" + shortPath;
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);

            foreach (string filePath in Interface.Oxide.DataFileSystem.GetFiles(shortPath))
            {
                string fileName = filePath.GetFileName();
                RoadOrBiomeSpawnPoint spawnPoint = Interface.Oxide.DataFileSystem.ReadObject<RoadOrBiomeSpawnPoint>($"{shortPath}{fileName}");
                if (spawnPoint != null)
                {
                    foreach (PresetConfig preset in spawnPoint.Presets) preset.IsRoadOrBiomeFile = true;
                    if (!Cfg.EnabledMinLogs) Puts($"File {fileName} has been loaded successfully!");
                    AnalyzingSpawnPoint(spawnPoint, fileName);
                    RoadSpawnPoints.Add(fileName, spawnPoint);
                }
                else PrintError($"File {fileName} is corrupted and cannot be loaded!");
                yield return CoroutineEx.waitForSeconds(0.1f);
            }
        }

        private IEnumerator SpawnRoadSpawnPoints()
        {
            foreach (KeyValuePair<string, RoadOrBiomeSpawnPoint> kvp in RoadSpawnPoints)
            {
                if (!kvp.Value.Enabled) continue;

                ControllerSpawnPoint controller = new GameObject().AddComponent<ControllerSpawnPoint>();
                controller.IsRoad = true;
                controller.Init(kvp.Value, Vector3.zero, Quaternion.identity, kvp.Key);
                Controllers.Add(controller);

                if (!Cfg.EnabledMinLogs) Puts($"Road {kvp.Key} has been successfully loaded!");

                yield return CoroutineEx.waitForSeconds(0.4f);
            }
        }
        #endregion Roads

        #region Biomes
        private Dictionary<string, RoadOrBiomeSpawnPoint> BiomeSpawnPoints { get; set; } = new Dictionary<string, RoadOrBiomeSpawnPoint>();

        private IEnumerator LoadBiomeSpawnPoints()
        {
            string shortPath = "BetterNpc/Biome/";

            string path = Interface.Oxide.DataDirectory + "/" + shortPath;
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);

            foreach (string filePath in Interface.Oxide.DataFileSystem.GetFiles(shortPath))
            {
                string fileName = filePath.GetFileName();
                RoadOrBiomeSpawnPoint spawnPoint = Interface.Oxide.DataFileSystem.ReadObject<RoadOrBiomeSpawnPoint>($"{shortPath}{fileName}");
                if (spawnPoint != null)
                {
                    foreach (PresetConfig preset in spawnPoint.Presets) preset.IsRoadOrBiomeFile = true;
                    if (!Cfg.EnabledMinLogs) Puts($"File {fileName} has been loaded successfully!");
                    AnalyzingSpawnPoint(spawnPoint, fileName);
                    BiomeSpawnPoints.Add(fileName, spawnPoint);
                }
                else PrintError($"File {fileName} is corrupted and cannot be loaded!");
                yield return CoroutineEx.waitForSeconds(0.1f);
            }
        }

        private IEnumerator SpawnBiomeSpawnPoints()
        {
            foreach (KeyValuePair<string, RoadOrBiomeSpawnPoint> kvp in BiomeSpawnPoints)
            {
                if (!kvp.Value.Enabled) continue;

                ControllerSpawnPoint controller = new GameObject().AddComponent<ControllerSpawnPoint>();
                controller.IsBiome = true;
                controller.Init(kvp.Value, Vector3.zero, Quaternion.identity, kvp.Key);
                Controllers.Add(controller);

                if (!Cfg.EnabledMinLogs) Puts($"Biome {kvp.Key} has been successfully loaded!");

                yield return CoroutineEx.waitForSeconds(0.4f);
            }
        }
        #endregion Biomes

        #region Failed NavMesh
        public class FailedNavMeshPoint
        {
            public string ControllerName { get; set; }
            public string PresetName { get; set; }
            public int Index { get; set; }
            public Vector3 Position { get; set; }
            public bool IsStationary { get; set; }
            public int AreaMask { get; set; }
            public bool HasGround { get; set; }
            public bool HasNavMesh { get; set; }
            public bool HasAltNavMesh { get; set; }
        }

        private void CheckFailedNavMesh(BasePlayer player = null, float duration = 60f)
        {
            HashSet<FailedNavMeshPoint> points = new HashSet<FailedNavMeshPoint>();

            foreach (ControllerSpawnPoint controller in Controllers)
            {
                if (controller.PositionsPerPreset == null) continue;
                foreach (KeyValuePair<string, List<Vector3>> kvp in controller.PositionsPerPreset)
                {
                    int areaMask = (int)NpcSpawn.Call("GetAreaMask", kvp.Key);
                    if (areaMask == 0) continue;

                    int altAreaMask = areaMask == 1 ? 25 : 1;

                    bool isStationary = (bool)NpcSpawn.Call("IsStationaryPreset", kvp.Key);

                    int index = 0;
                    foreach (Vector3 pos in kvp.Value)
                    {
                        index++;

                        bool hasGround = Physics.Raycast(pos.AddToY(0.75f), Vector3.down, out RaycastHit raycastHit, 1.5f);
                        bool hasNavMesh = pos.IsNavMesh(1.5f, areaMask, out NavMeshHit navMeshHit);

                        if (hasGround && hasNavMesh) continue;

                        if (points.Any(x => x.ControllerName == controller.Name && x.PresetName == kvp.Key && x.Index == index)) continue;

                        bool hasAltNavMesh = pos.IsNavMesh(1.5f, altAreaMask, out navMeshHit);

                        points.Add(new FailedNavMeshPoint
                        {
                            ControllerName = controller.Name,
                            PresetName = kvp.Key,
                            Index = index,
                            Position = pos,
                            IsStationary = isStationary,
                            AreaMask = areaMask,
                            HasGround = hasGround,
                            HasNavMesh = hasNavMesh,
                            HasAltNavMesh = hasAltNavMesh
                        });
                    }
                }
            }

            foreach (FailedNavMeshPoint point in points)
            {
                if (!point.HasNavMesh && point.IsStationary && point.HasGround) continue;

                if (!point.HasNavMesh && !point.HasGround)
                {
                    PrintError($"SpawnPoint: {point.ControllerName}. Preset: {point.PresetName}. Index: {point.Index}. No surface and no navigation grid");
                    if (player != null)
                    {
                        DebugDraw.Line(player, point.Position.AddToY(100f), point.Position, Color.red, duration);
                        DebugDraw.Sphere(player, point.Position, Color.red, 1.5f, duration);
                    }
                }
                else if (!point.HasGround)
                {
                    PrintWarning($"SpawnPoint: {point.ControllerName}. Preset: {point.PresetName}. Index: {point.Index}. No surface");
                    if (player != null)
                    {
                        DebugDraw.Line(player, point.Position.AddToY(100f), point.Position, Color.yellow, duration);
                        DebugDraw.Sphere(player, point.Position, Color.yellow, 1.5f, duration);
                    }
                }
                else if (!point.HasNavMesh)
                {
                    if (point.HasAltNavMesh)
                    {
                        PrintWarning($"SpawnPoint: {point.ControllerName}. Preset: {point.PresetName}. Index: {point.Index}. Wrong type of navigation grid ({(point.AreaMask == 1 ? 0 : 1)} -> {(point.AreaMask == 1 ? 1 : 0)})");
                        if (player != null)
                        {
                            DebugDraw.Line(player, point.Position.AddToY(100f), point.Position, Color.yellow, duration);
                            DebugDraw.Sphere(player, point.Position, Color.yellow, 1.5f, duration);
                        }
                    }
                    else
                    {
                        PrintWarning($"SpawnPoint: {point.ControllerName}. Preset: {point.PresetName}. Index: {point.Index}. No navigation grid");
                        if (player != null)
                        {
                            DebugDraw.Line(player, point.Position.AddToY(100f), point.Position, Color.yellow, duration);
                            DebugDraw.Sphere(player, point.Position, Color.yellow, 1.5f, duration);
                        }
                    }
                }
            }

            points.Clear();
            points = null;
        }
        #endregion Failed NavMesh

        #region Helper
        private void CheckVersionPlugin()
        {
            webrequest.Enqueue("http://37.153.157.216:5000/Api/GetPluginVersions?pluginName=BetterNpc", null, (code, response) =>
            {
                if (code != 200 || string.IsNullOrEmpty(response)) return;
                string[] array = response.Replace("\"", string.Empty).Split('.');
                VersionNumber latestVersion = new VersionNumber(Convert.ToInt32(array[0]), Convert.ToInt32(array[1]), Convert.ToInt32(array[2]));
                if (Version < latestVersion) PrintWarning($"A new version ({latestVersion}) of the plugin is available! You need to update the plugin:\n- https://lone.design/product/betternpc\n- https://codefling.com/plugins/better-npc");
            }, this);
        }

        private void TrySendNpcToPveMode(ScientistNPC npc)
        {
            if (Cfg.Pve && plugins.Exists("PveMode"))
                PveMode?.Call("ScientistAddPveMode", npc);
        }

        private static string GetNameArgs(string[] args, int first)
        {
            string result = "";
            for (int i = first; i < args.Length; i++) result += i == first ? args[i] : $" {args[i]}";
            return result;
        }

        private ControllerSpawnPoint GetNearController(Vector3 pos, string name)
        {
            ControllerSpawnPoint closest = null;
            float closestDistSqr = float.MaxValue;

            foreach (ControllerSpawnPoint point in Controllers)
            {
                if (point.IsEvent || point.IsRoad || point.IsBiome) continue;
                if (!EqualsIgnoreWhitespace(point.Name, name)) continue;

                float distSqr = (pos - point.transform.position).sqrMagnitude;
                if (distSqr < closestDistSqr)
                {
                    closest = point;
                    closestDistSqr = distSqr;
                }
            }

            return closest;
        }

        private static bool EqualsIgnoreWhitespace(string a, string b)
        {
            if (a == null || b == null) return false;

            string cleanA = RemoveWhitespace(a);
            string cleanB = RemoveWhitespace(b);

            return string.Equals(cleanA, cleanB, StringComparison.OrdinalIgnoreCase);
        }

        private static string RemoveWhitespace(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            System.Text.StringBuilder sb = new System.Text.StringBuilder(input.Length);

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (!char.IsWhiteSpace(c)) sb.Append(c);
            }

            return sb.ToString();
        }

        private bool HasController(Vector3 pos, string name)
        {
            ControllerSpawnPoint controller = GetNearController(pos, name);
            if (controller == null) return false;
            return Vector3.Distance(controller.transform.position, pos) < 1f;
        }
        #endregion Helper

        #region Commands
        [ChatCommand("SpawnPointAdd")]
        private void ChatCommandSpawnPointAdd(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, "betternpc.admin")) return;

            if (args == null || args.Length == 0)
            {
                PrintToChat(player, "You <color=#ce3f27>didn't</color> write the name of the spawn point!");
                return;
            }

            string name = GetNameArgs(args, 0);

            CustomMonumentSpawnPoint spawnPoint = new CustomMonumentSpawnPoint
            {
                Enabled = true,
                Presets = new List<PresetConfig>
                {
                    new PresetConfig
                    {
                        Enabled = true,
                        MinDay = 1,
                        MaxDay = 1,
                        MinNight = 1,
                        MaxNight = 1,
                        RespawnMinTime = 600,
                        RespawnMaxTime = 600,
                        PresetName = "Default_BetterNpc",
                        Economics = new Dictionary<string, double>(),
                        SpawnType = SpawnType.Random,
                        CustomPositions = new List<string>()
                    }
                },
                Size = "(9, 9, 9)",
                RemoveDefaultNpc = true,
                Id = 0f,
                MapMarkerName = string.Empty,
                Position = player.transform.position.ToString(),
                Rotation = "(0, 0, 0)"
            };

            Interface.Oxide.DataFileSystem.WriteObject($"BetterNpc/Custom/{name}", spawnPoint);
            CustomSpawnPoints.Add(name, spawnPoint);

            ControllerSpawnPoint controller = new GameObject().AddComponent<ControllerSpawnPoint>();
            controller.Init(spawnPoint, player.transform.position, Quaternion.identity, name);
            Controllers.Add(controller);

            PrintToChat(player, $"You <color=#738d43>have successfully added</color> a new spawn point named <color=#55aaff>{name}</color>. You <color=#738d43>can edit</color> this spawn point in the file <color=#55aaff>BetterNpc/Custom/{name}</color>");
            Puts($"Custom location {name} has been successfully loaded!");
        }

        [ChatCommand("SpawnPointPos")]
        private void ChatCommandSpawnPointPos(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, "betternpc.admin")) return;

            if (args == null || args.Length == 0)
            {
                PrintToChat(player, "You <color=#ce3f27>didn't</color> write the name of the spawn point!");
                return;
            }

            string name = GetNameArgs(args, 0);

            ControllerSpawnPoint controller = GetNearController(player.transform.position, name);
            if (controller == null)
            {
                PrintToChat(player, $"Spawn Point with the name <color=#55aaff>{name}</color> was <color=#ce3f27>not</color> found!");
                return;
            }

            Vector3 local = controller.transform.GetLocalPosition(player.transform.position);

            Puts($"Spawn Point: {name}. Position: {local}");
            PrintToChat(player, $"Spawn Point: <color=#55aaff>{name}</color>\nPosition: <color=#55aaff>{local}</color>");
        }

        [ChatCommand("SpawnPointAddPos")]
        private void ChatCommandSpawnPointAddPos(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, "betternpc.admin")) return;

            if (args == null || args.Length == 0)
            {
                PrintToChat(player, "You <color=#ce3f27>didn't</color> write the preset number!");
                return;
            }

            if (args.Length == 1)
            {
                PrintToChat(player, "You <color=#ce3f27>didn't</color> write the name of the spawn point!");
                return;
            }

            int number = Convert.ToInt32(args[0]) - 1;
            if (number < 0) return;

            string name = GetNameArgs(args, 1);

            ControllerSpawnPoint controller = GetNearController(player.transform.position, name);
            if (controller == null)
            {
                PrintToChat(player, $"Spawn Point with the name <color=#55aaff>{name}</color> was <color=#ce3f27>not</color> found!");
                return;
            }

            string pos = controller.transform.GetLocalPosition(player.transform.position).ToString();

            if (MonumentSpawnPoints.TryGetValue(name, out MonumentSpawnPoint spawnPoint1))
            {
                if (spawnPoint1.Presets.Count < number + 1) return;

                spawnPoint1.Presets[number].CustomPositions.Add(pos);

                Interface.Oxide.DataFileSystem.WriteObject($"BetterNpc/Monument/{name}", spawnPoint1);
                PrintToChat(player, $"You <color=#738d43>have successfully saved</color> a new position for Spawn Point <color=#55aaff>{name}</color> and for preset <color=#55aaff>{number + 1}</color>!");
            }
            else if (UnderwaterLabSpawnPoints.TryGetValue(name, out MonumentSpawnPoint spawnPoint2))
            {
                if (spawnPoint2.Presets.Count < number + 1) return;

                spawnPoint2.Presets[number].CustomPositions.Add(pos);

                Interface.Oxide.DataFileSystem.WriteObject($"BetterNpc/Monument/Underwater Lab/{name}", spawnPoint2);
                PrintToChat(player, $"You <color=#738d43>have successfully saved</color> a new position for Spawn Point <color=#55aaff>{name}</color> and for preset <color=#55aaff>{number + 1}</color>!");
            }
            else if (TunnelSpawnPoints.TryGetValue(name, out MonumentSpawnPoint spawnPoint3))
            {
                if (spawnPoint3.Presets.Count < number + 1) return;

                spawnPoint3.Presets[number].CustomPositions.Add(pos);

                Interface.Oxide.DataFileSystem.WriteObject($"BetterNpc/Monument/Tunnel/{name}", spawnPoint3);
                PrintToChat(player, $"You <color=#738d43>have successfully saved</color> a new position for Spawn Point <color=#55aaff>{name}</color> and for preset <color=#55aaff>{number + 1}</color>!");
            }
            else if (CustomSpawnPoints.TryGetValue(name, out CustomMonumentSpawnPoint spawnPoint4))
            {
                if (spawnPoint4.Presets.Count < number + 1) return;

                spawnPoint4.Presets[number].CustomPositions.Add(pos);

                Interface.Oxide.DataFileSystem.WriteObject($"BetterNpc/Custom/{name}", spawnPoint4);
                PrintToChat(player, $"You <color=#738d43>have successfully saved</color> a new position for Spawn Point <color=#55aaff>{name}</color> and for preset <color=#55aaff>{number + 1}</color>!");
            }
        }

        [ChatCommand("SpawnPointRemovePos")]
        private void ChatCommandSpawnPointRemovePos(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, "betternpc.admin")) return;

            if (args == null || args.Length == 0)
            {
                PrintToChat(player, "You <color=#ce3f27>didn't</color> write the preset number!");
                return;
            }

            if (args.Length == 1)
            {
                PrintToChat(player, "You <color=#ce3f27>didn't</color> write the name of the spawn point!");
                return;
            }

            int number = Convert.ToInt32(args[0]) - 1;
            if (number < 0) return;

            string name = GetNameArgs(args, 1);

            ControllerSpawnPoint controller = GetNearController(player.transform.position, name);
            if (controller == null)
            {
                PrintToChat(player, $"Spawn Point with the name <color=#55aaff>{name}</color> was <color=#ce3f27>not</color> found!");
                return;
            }

            Vector3 pos = controller.transform.GetLocalPosition(player.transform.position);

            if (MonumentSpawnPoints.TryGetValue(name, out MonumentSpawnPoint spawnPoint1))
            {
                if (spawnPoint1.Presets.Count < number + 1) return;
                PresetConfig preset = spawnPoint1.Presets[number];

                string remove = preset.CustomPositions.Min(x => Vector3.Distance(x.ToVector3(), pos));
                preset.CustomPositions.Remove(remove);

                Interface.Oxide.DataFileSystem.WriteObject($"BetterNpc/Monument/{name}", spawnPoint1);
                PrintToChat(player, $"You <color=#738d43>have successfully removed</color> a position for Spawn Point <color=#55aaff>{name}</color> and for preset <color=#55aaff>{number + 1}</color>!");
            }
            else if (UnderwaterLabSpawnPoints.TryGetValue(name, out MonumentSpawnPoint spawnPoint2))
            {
                if (spawnPoint2.Presets.Count < number + 1) return;
                PresetConfig preset = spawnPoint2.Presets[number];

                string remove = preset.CustomPositions.Min(x => Vector3.Distance(x.ToVector3(), pos));
                preset.CustomPositions.Remove(remove);

                Interface.Oxide.DataFileSystem.WriteObject($"BetterNpc/Monument/Underwater Lab/{name}", spawnPoint2);
                PrintToChat(player, $"You <color=#738d43>have successfully removed</color> a position for Spawn Point <color=#55aaff>{name}</color> and for preset <color=#55aaff>{number + 1}</color>!");
            }
            else if (TunnelSpawnPoints.TryGetValue(name, out MonumentSpawnPoint spawnPoint3))
            {
                if (spawnPoint3.Presets.Count < number + 1) return;
                PresetConfig preset = spawnPoint3.Presets[number];

                string remove = preset.CustomPositions.Min(x => Vector3.Distance(x.ToVector3(), pos));
                preset.CustomPositions.Remove(remove);

                Interface.Oxide.DataFileSystem.WriteObject($"BetterNpc/Monument/Tunnel/{name}", spawnPoint3);
                PrintToChat(player, $"You <color=#738d43>have successfully removed</color> a position for Spawn Point <color=#55aaff>{name}</color> and for preset <color=#55aaff>{number + 1}</color>!");
            }
            else if (CustomSpawnPoints.TryGetValue(name, out CustomMonumentSpawnPoint spawnPoint4))
            {
                if (spawnPoint4.Presets.Count < number + 1) return;
                PresetConfig preset = spawnPoint4.Presets[number];

                string remove = preset.CustomPositions.Min(x => Vector3.Distance(x.ToVector3(), pos));
                preset.CustomPositions.Remove(remove);

                Interface.Oxide.DataFileSystem.WriteObject($"BetterNpc/Custom/{name}", spawnPoint4);
                PrintToChat(player, $"You <color=#738d43>have successfully removed</color> a position for Spawn Point <color=#55aaff>{name}</color> and for preset <color=#55aaff>{number + 1}</color>!");
            }
        }

        [ChatCommand("SpawnPointShowPos")]
        private void ChatCommandSpawnPointShowPos(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, "betternpc.admin")) return;

            if (args == null || args.Length == 0)
            {
                PrintToChat(player, "You <color=#ce3f27>didn't</color> write the preset number!");
                return;
            }

            if (args.Length == 1)
            {
                PrintToChat(player, "You <color=#ce3f27>didn't</color> write the name of the spawn point!");
                return;
            }

            int number = Convert.ToInt32(args[0]) - 1;
            if (number < 0) return;

            string name = GetNameArgs(args, 1);

            ControllerSpawnPoint controller = GetNearController(player.transform.position, name);
            if (controller == null)
            {
                PrintToChat(player, $"Spawn Point with the name <color=#55aaff>{name}</color> was <color=#ce3f27>not</color> found!");
                return;
            }

            if (MonumentSpawnPoints.TryGetValue(name, out MonumentSpawnPoint spawnPoint1))
            {
                if (spawnPoint1.Presets.Count < number + 1) return;
                ShowPositions(spawnPoint1.Presets[number]);
            }
            else if (UnderwaterLabSpawnPoints.TryGetValue(name, out MonumentSpawnPoint spawnPoint2))
            {
                if (spawnPoint2.Presets.Count < number + 1) return;
                ShowPositions(spawnPoint2.Presets[number]);
            }
            else if (TunnelSpawnPoints.TryGetValue(name, out MonumentSpawnPoint spawnPoint3))
            {
                if (spawnPoint3.Presets.Count < number + 1) return;
                ShowPositions(spawnPoint3.Presets[number]);
            }
            else if (CustomSpawnPoints.TryGetValue(name, out CustomMonumentSpawnPoint spawnPoint4))
            {
                if (spawnPoint4.Presets.Count < number + 1) return;
                ShowPositions(spawnPoint4.Presets[number]);
            }

            return;

            void ShowPositions(PresetConfig preset)
            {
                for (int index = 0; index < preset.CustomPositions.Count; index++)
                {
                    string str = preset.CustomPositions[index];
                    Vector3 position = controller.transform.GetGlobalPosition(str.ToVector3());
                    DebugDraw.Sphere(player, position, Color.green, 2f);
                    DebugDraw.Line(player, position + Vector3.up * 200f, position, Color.green);
                    DebugDraw.Text(player, position, $"{index + 1}", Color.green, 40);
                }
            }
        }

        [ChatCommand("SpawnPointReload")]
        private void ChatCommandSpawnPointReload(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, "betternpc.admin")) return;

            if (args == null || args.Length == 0)
            {
                PrintToChat(player, "You <color=#ce3f27>didn't</color> write the name of the spawn point!");
                return;
            }

            string name = GetNameArgs(args, 0);

            DestroyController(name);

            if (MonumentSpawnPoints.TryGetValue(name, out MonumentSpawnPoint oldSpawnPoint1))
            {
                TryClearPresetUsage(oldSpawnPoint1, name);
                MonumentSpawnPoint spawnPoint = Interface.Oxide.DataFileSystem.ReadObject<MonumentSpawnPoint>($"BetterNpc/Monument/{name}");
                if (spawnPoint != null)
                {
                    AnalyzingMonumentSpawnPoint(spawnPoint, name);
                    MonumentSpawnPoints.Remove(name);
                    MonumentSpawnPoints.Add(name, spawnPoint);
                }
            }
            else if (UnderwaterLabSpawnPoints.TryGetValue(name, out MonumentSpawnPoint oldSpawnPoint2))
            {
                TryClearPresetUsage(oldSpawnPoint2, name);
                MonumentSpawnPoint spawnPoint = Interface.Oxide.DataFileSystem.ReadObject<MonumentSpawnPoint>($"BetterNpc/Monument/Underwater Lab/{name}");
                if (spawnPoint != null)
                {
                    AnalyzingMonumentSpawnPoint(spawnPoint, name);
                    UnderwaterLabSpawnPoints.Remove(name);
                    UnderwaterLabSpawnPoints.Add(name, spawnPoint);
                }
            }
            else if (TunnelSpawnPoints.TryGetValue(name, out MonumentSpawnPoint oldSpawnPoint3))
            {
                TryClearPresetUsage(oldSpawnPoint3, name);
                MonumentSpawnPoint spawnPoint = Interface.Oxide.DataFileSystem.ReadObject<MonumentSpawnPoint>($"BetterNpc/Monument/Tunnel/{name}");
                if (spawnPoint != null)
                {
                    AnalyzingMonumentSpawnPoint(spawnPoint, name);
                    TunnelSpawnPoints.Remove(name);
                    TunnelSpawnPoints.Add(name, spawnPoint);
                }
            }
            else if (CustomSpawnPoints.TryGetValue(name, out CustomMonumentSpawnPoint oldSpawnPoint4))
            {
                TryClearPresetUsage(oldSpawnPoint4, name);
                CustomMonumentSpawnPoint spawnPoint = Interface.Oxide.DataFileSystem.ReadObject<CustomMonumentSpawnPoint>($"BetterNpc/Custom/{name}");
                if (spawnPoint != null)
                {
                    CustomSpawnPoints.Remove(name);
                    if (!spawnPoint.Id.AreEqual(0f) && !Ids.Any(x => x.AreEqual(spawnPoint.Id))) return;
                    AnalyzingCustomMonumentSpawnPoint(spawnPoint, name);
                    CustomSpawnPoints.Add(name, spawnPoint);
                }
            }
            else if (BiomeSpawnPoints.TryGetValue(name, out RoadOrBiomeSpawnPoint oldSpawnPoint5))
            {
                TryClearPresetUsage(oldSpawnPoint5, name);
                RoadOrBiomeSpawnPoint spawnPoint = Interface.Oxide.DataFileSystem.ReadObject<RoadOrBiomeSpawnPoint>($"BetterNpc/Biome/{name}");
                if (spawnPoint != null)
                {
                    foreach (PresetConfig preset in spawnPoint.Presets) preset.IsRoadOrBiomeFile = true;
                    AnalyzingSpawnPoint(spawnPoint, name);
                    BiomeSpawnPoints.Remove(name);
                    BiomeSpawnPoints.Add(name, spawnPoint);
                }
            }
            else if (RoadSpawnPoints.TryGetValue(name, out RoadOrBiomeSpawnPoint oldSpawnPoint6))
            {
                TryClearPresetUsage(oldSpawnPoint6, name);
                RoadOrBiomeSpawnPoint spawnPoint = Interface.Oxide.DataFileSystem.ReadObject<RoadOrBiomeSpawnPoint>($"BetterNpc/Road/{name}");
                if (spawnPoint != null)
                {
                    foreach (PresetConfig preset in spawnPoint.Presets) preset.IsRoadOrBiomeFile = true;
                    AnalyzingSpawnPoint(spawnPoint, name);
                    RoadSpawnPoints.Remove(name);
                    RoadSpawnPoints.Add(name, spawnPoint);
                }
            }

            CreateController(name);

            PrintToChat(player, $"SpawnPoint with the name <color=#55aaff>{name}</color> <color=#738d43>has been reloaded</color>!");
        }

        [ConsoleCommand("SpawnPointCreate")]
        private void ConsoleCommandSpawnPointCreate(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null) return;

            if (arg.Args == null || arg.Args.Length == 0)
            {
                Puts("You didn't write the name of the spawn point!");
                return;
            }

            string name = GetNameArgs(arg.Args, 0);

            CreateController(name);
            Puts($"SpawnPoint with the name {name} has been created!");
        }

        [ConsoleCommand("SpawnPointDestroy")]
        private void ConsoleCommandSpawnPointDestroy(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null) return;

            if (arg.Args == null || arg.Args.Length == 0)
            {
                Puts("You didn't write the name of the spawn point!");
                return;
            }

            string name = GetNameArgs(arg.Args, 0);

            DestroyController(name);
            Puts($"SpawnPoint with the name {name} has been destroyed!");
        }

        [ConsoleCommand("ShowAllNpc")]
        private void ConsoleCommandShowAllNpc(ConsoleSystem.Arg arg)
        {
            if (arg.Player() == null)
            {
                string message = "The number of NPCs from the BetterNpc plugin:";
                int all = 0;
                foreach (ControllerSpawnPoint controller in Controllers)
                {
                    message += $"\n- {controller.Name} = {controller.ActiveNpc.Count}";
                    all += controller.ActiveNpc.Count;
                }
                if (CargoControllers != null)
                {
                    foreach (CargoControllerSpawnPoint controller in CargoControllers)
                    {
                        if (controller.Cargo == null || controller.Cargo.net == null) continue;
                        int amount = 0; foreach (KeyValuePair<string, HashSet<ScientistNPC>> dic in controller.ActiveNpc) amount += dic.Value.Count;
                        message += $"\n- {controller.Cargo.net.ID.Value} = {amount}";
                        all += amount;
                    }
                }
                message += $"\nTotal number = {all}";
                Puts(message);
            }
        }

        [ChatCommand("ShowAllZones")]
        private void ChatCommandShowAllZones(BasePlayer player)
        {
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, "betternpc.admin")) return;
            foreach (ControllerSpawnPoint controller in Controllers)
            {
                Vector3 center = controller.transform.position;

                Vector3 pos1 = center + controller.transform.rotation * (new Vector3(center.x + controller.Size.x, center.y + controller.Size.y, center.z + controller.Size.z) - center);
                Vector3 pos2 = center + controller.transform.rotation * (new Vector3(center.x + controller.Size.x, center.y - controller.Size.y, center.z + controller.Size.z) - center);
                Vector3 pos3 = center + controller.transform.rotation * (new Vector3(center.x - controller.Size.x, center.y - controller.Size.y, center.z + controller.Size.z) - center);
                Vector3 pos4 = center + controller.transform.rotation * (new Vector3(center.x - controller.Size.x, center.y + controller.Size.y, center.z + controller.Size.z) - center);
                Vector3 pos5 = center + controller.transform.rotation * (new Vector3(center.x + controller.Size.x, center.y + controller.Size.y, center.z - controller.Size.z) - center);
                Vector3 pos6 = center + controller.transform.rotation * (new Vector3(center.x + controller.Size.x, center.y - controller.Size.y, center.z - controller.Size.z) - center);
                Vector3 pos7 = center + controller.transform.rotation * (new Vector3(center.x - controller.Size.x, center.y - controller.Size.y, center.z - controller.Size.z) - center);
                Vector3 pos8 = center + controller.transform.rotation * (new Vector3(center.x - controller.Size.x, center.y + controller.Size.y, center.z - controller.Size.z) - center);

                DebugDraw.Line(player, pos1, pos2, Color.green, 30f);
                DebugDraw.Line(player, pos2, pos3, Color.green, 30f);
                DebugDraw.Line(player, pos3, pos4, Color.green, 30f);
                DebugDraw.Line(player, pos4, pos1, Color.green, 30f);

                DebugDraw.Line(player, pos5, pos6, Color.green, 30f);
                DebugDraw.Line(player, pos6, pos7, Color.green, 30f);
                DebugDraw.Line(player, pos7, pos8, Color.green, 30f);
                DebugDraw.Line(player, pos8, pos5, Color.green, 30f);

                DebugDraw.Line(player, pos1, pos5, Color.green, 30f);
                DebugDraw.Line(player, pos2, pos6, Color.green, 30f);
                DebugDraw.Line(player, pos3, pos7, Color.green, 30f);
                DebugDraw.Line(player, pos4, pos8, Color.green, 30f);
            }
        }

        [ConsoleCommand("ShowFailedNavMesh")]
        private void ConsoleCommandShowFailedNavMesh(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null) return;
            CheckFailedNavMesh();
        }

        [ChatCommand("ShowFailedNavMesh")]
        private void ChatCommandShowFailedNavMesh(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, "betternpc.admin")) return;
            float duration = args is { Length: 1 } ? Convert.ToSingle(args[0]) : 60f;
            CheckFailedNavMesh(player, duration);
        }

        [ChatCommand("TeleportToSpawnPoint")]
        private void ChatCommandTeleportToSpawnPoint(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, "betternpc.admin")) return;

            if (args == null || args.Length == 0)
            {
                PrintToChat(player, "You <color=#ce3f27>didn't</color> write the name of the spawn point!");
                return;
            }

            string name = GetNameArgs(args, 0);

            ControllerSpawnPoint controller = Controllers.FirstOrDefault(x => x.Name == name);
            if (controller == null)
            {
                PrintToChat(player, $"Spawn Point with the name <color=#55aaff>{name}</color> was <color=#ce3f27>not</color> found!");
                return;
            }

            Teleport(player, controller.transform.position);
        }

        public void Teleport(BasePlayer player, Vector3 newPosition)
        {
            if (!player.IsValid() || Vector3.Distance(newPosition, Vector3.zero) < 5f) return;

            newPosition.y += 0.1f;

            player.PauseFlyHackDetection(5f);
            player.PauseSpeedHackDetection(5f);
            player.ApplyStallProtection(4f);
            player.UpdateActiveItem(default);
            player.EnsureDismounted();
            player.Server_CancelGesture();

            if (player.HasParent()) player.SetParent(null, true, true);

            if (player.IsConnected)
            {
                player.StartSleeping();
                if (player.IsAdmin) player.RunOfflineMetabolism(state: false);
                player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, true);
                player.ClientRPC(RpcTarget.Player("StartLoading_Quick", player), arg1: true);
            }

            player.Teleport(newPosition);

            if (player.IsConnected)
            {
                if (!player.limitNetworking && !player.isInvisible)
                {
                    player.UpdateNetworkGroup();
                    player.SendNetworkUpdateImmediate();
                }

                player.ClearEntityQueue(null);
                player.SendFullSnapshot();
                if (player.IsOnGround() || player.limitNetworking || player.isInvisible || player.IsFlying || player.IsAdmin) player.Invoke(() =>
                {
                    if (player && player.IsConnected)
                    {
                        if (player.limitNetworking || player.isInvisible) player.EndSleeping();
                        else player.EndSleeping();
                    }
                }, 0.5f);
            }

            if (!player.limitNetworking && !player.isInvisible) player.ForceUpdateTriggers();
        }
        #endregion Commands

        #region API
        private void DestroyController(string name)
        {
            HashSet<ControllerSpawnPoint> controllers = new HashSet<ControllerSpawnPoint>();

            foreach (ControllerSpawnPoint point in Controllers)
            {
                if (point.IsEvent) continue;
                if (!EqualsIgnoreWhitespace(point.Name, name)) continue;
                controllers.Add(point);
            }

            foreach (ControllerSpawnPoint point in controllers)
            {
                Controllers.Remove(point);
                UnityEngine.Object.Destroy(point.gameObject);
            }

            controllers.Clear();
            controllers = null;
        }

        private void DestroyController(string name, Vector3 position)
        {
            ControllerSpawnPoint controller = GetNearController(position, name);
            Controllers.Remove(controller);
            UnityEngine.Object.Destroy(controller.gameObject);
        }

        private void CreateController(string name)
        {
            if (MonumentSpawnPoints.TryGetValue(name, out MonumentSpawnPoint spawnPoint1))
            {
                if (!spawnPoint1.Enabled) return;
                foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
                {
                    string monumentName = GetNameMonument(monument);
                    if (string.IsNullOrEmpty(monumentName) || monumentName != name) continue;

                    if (HasController(monument.transform.position, name)) continue;

                    ControllerSpawnPoint controller = new GameObject().AddComponent<ControllerSpawnPoint>();
                    controller.Init(spawnPoint1, monument.transform.position, monument.transform.rotation, name);
                    Controllers.Add(controller);
                }
            }
            else if (UnderwaterLabSpawnPoints.TryGetValue(name, out MonumentSpawnPoint spawnPoint2))
            {
                if (!spawnPoint2.Enabled) return;
                foreach (DungeonBaseInfo baseModule in TerrainMeta.Path.DungeonBaseEntrances)
                {
                    if (baseModule.name == name && !HasController(baseModule.transform.position, name))
                    {
                        ControllerSpawnPoint controller = new GameObject().AddComponent<ControllerSpawnPoint>();
                        controller.IsUnderwaterLab = true;
                        controller.Init(spawnPoint2, baseModule.transform.position, baseModule.transform.rotation, name);
                        Controllers.Add(controller);
                    }
                    foreach (GameObject module in baseModule.Links)
                    {
                        string moduleName = module.name.GetFileName();
                        if (moduleName != name) continue;

                        if (HasController(module.transform.position, name)) continue;

                        ControllerSpawnPoint controller = new GameObject().AddComponent<ControllerSpawnPoint>();
                        controller.IsUnderwaterLab = true;
                        controller.Init(spawnPoint2, module.transform.position, module.transform.rotation, name);
                        Controllers.Add(controller);
                    }
                }
            }
            else if (TunnelSpawnPoints.TryGetValue(name, out MonumentSpawnPoint spawnPoint3))
            {
                if (!spawnPoint3.Enabled) return;
                foreach (DungeonGridCell gridCell in TerrainMeta.Path.DungeonGridCells)
                {
                    string cellName = gridCell.name.GetFileName();
                    if (cellName != name) continue;

                    if (HasController(gridCell.transform.position, name)) continue;

                    ControllerSpawnPoint controller = new GameObject().AddComponent<ControllerSpawnPoint>();
                    controller.IsTunnel = true;
                    controller.Init(spawnPoint3, gridCell.transform.position, gridCell.transform.rotation, name);
                    Controllers.Add(controller);
                }
            }
            else if (CustomSpawnPoints.TryGetValue(name, out CustomMonumentSpawnPoint spawnPoint4))
            {
                if (!spawnPoint4.Enabled) return;

                (Vector3 pos, Quaternion rot) = GetLocationCustomSpawnPoint(spawnPoint4);
                if (pos == Vector3.zero) return;

                if (HasController(pos, name)) return;

                ControllerSpawnPoint controller = new GameObject().AddComponent<ControllerSpawnPoint>();
                controller.Init(spawnPoint4, pos, rot, name);
                Controllers.Add(controller);
            }
            else if (BiomeSpawnPoints.TryGetValue(name, out RoadOrBiomeSpawnPoint spawnPoint5))
            {
                if (!spawnPoint5.Enabled) return;

                if (Controllers.Any(x => x.Name == name)) return;

                ControllerSpawnPoint controller = new GameObject().AddComponent<ControllerSpawnPoint>();
                controller.IsBiome = true;
                controller.Init(spawnPoint5, Vector3.zero, Quaternion.identity, name);
                Controllers.Add(controller);
            }
            else if (RoadSpawnPoints.TryGetValue(name, out RoadOrBiomeSpawnPoint spawnPoint6))
            {
                if (!spawnPoint6.Enabled) return;

                if (Controllers.Any(x => x.Name == name)) return;

                ControllerSpawnPoint controller = new GameObject().AddComponent<ControllerSpawnPoint>();
                controller.IsRoad = true;
                controller.Init(spawnPoint6, Vector3.zero, Quaternion.identity, name);
                Controllers.Add(controller);
            }
        }

        private void CreateController(string name, Vector3 position)
        {
            if (MonumentSpawnPoints.TryGetValue(name, out MonumentSpawnPoint spawnPoint1))
            {
                if (!spawnPoint1.Enabled) return;

                Transform closest = null;
                float closestDistSqr = float.MaxValue;

                foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
                {
                    string monumentName = GetNameMonument(monument);
                    if (string.IsNullOrEmpty(monumentName) || monumentName != name) continue;
                    if (IsNearest(monument.transform, closestDistSqr, out float distSqr))
                    {
                        closest = monument.transform;
                        closestDistSqr = distSqr;
                    }
                }

                if (closest == null) return;

                if (HasController(closest.position, name)) return;

                ControllerSpawnPoint controller = new GameObject().AddComponent<ControllerSpawnPoint>();
                controller.Init(spawnPoint1, closest.position, closest.rotation, name);
                Controllers.Add(controller);
            }
            else if (UnderwaterLabSpawnPoints.TryGetValue(name, out MonumentSpawnPoint spawnPoint2))
            {
                if (!spawnPoint2.Enabled) return;

                Transform closest = null;
                float closestDistSqr = float.MaxValue;

                foreach (DungeonBaseInfo baseModule in TerrainMeta.Path.DungeonBaseEntrances)
                {
                    if (baseModule.name == name)
                    {
                        if (IsNearest(baseModule.transform, closestDistSqr, out float distSqr))
                        {
                            closest = baseModule.transform;
                            closestDistSqr = distSqr;
                        }
                    }
                    foreach (GameObject module in baseModule.Links)
                    {
                        string moduleName = module.name.GetFileName();
                        if (moduleName != name) continue;
                        if (IsNearest(module.transform, closestDistSqr, out float distSqr))
                        {
                            closest = baseModule.transform;
                            closestDistSqr = distSqr;
                        }
                    }
                }

                if (closest == null) return;

                if (HasController(closest.position, name)) return;

                ControllerSpawnPoint controller = new GameObject().AddComponent<ControllerSpawnPoint>();
                controller.IsUnderwaterLab = true;
                controller.Init(spawnPoint2, closest.position, closest.rotation, name);
                Controllers.Add(controller);
            }
            else if (TunnelSpawnPoints.TryGetValue(name, out MonumentSpawnPoint spawnPoint3))
            {
                if (!spawnPoint3.Enabled) return;

                Transform closest = null;
                float closestDistSqr = float.MaxValue;

                foreach (DungeonGridCell gridCell in TerrainMeta.Path.DungeonGridCells)
                {
                    string cellName = gridCell.name.GetFileName();
                    if (cellName != name) continue;
                    if (IsNearest(gridCell.transform, closestDistSqr, out float distSqr))
                    {
                        closest = gridCell.transform;
                        closestDistSqr = distSqr;
                    }
                }

                if (closest == null) return;

                if (HasController(closest.position, name)) return;

                ControllerSpawnPoint controller = new GameObject().AddComponent<ControllerSpawnPoint>();
                controller.IsTunnel = true;
                controller.Init(spawnPoint3, closest.position, closest.rotation, name);
                Controllers.Add(controller);
            }

            return;

            bool IsNearest(Transform tr, float closestDistSqr, out float distSqr)
            {
                distSqr = (position - tr.position).sqrMagnitude;
                return distSqr < closestDistSqr;
            }
        }

        private void CreateControllerDelayed(string name, float seconds) => timer.In(seconds, () => CreateController(name));

        private void CreateControllerDelayed(string name, Vector3 position, float seconds) => timer.In(seconds, () => CreateController(name, position));
        #endregion API

        #region API From NpcSpawn
        private void TryRegisterPresetsUsage(SpawnPoint spawnPoint, string name)
        {
            if (!spawnPoint.Enabled) return;
            foreach (PresetConfig preset in spawnPoint.Presets)
            {
                if (!preset.Enabled) continue;
                NpcSpawn?.Call("RegisterPresetUsage", preset.PresetName, "BetterNpc", name);
            }
        }

        private void TryClearPresetUsage(SpawnPoint spawnPoint, string name)
        {
            if (!spawnPoint.Enabled) return;
            foreach (PresetConfig preset in spawnPoint.Presets)
            {
                if (!preset.Enabled) continue;
                NpcSpawn?.Call("UnregisterPresetUsage", preset.PresetName, "BetterNpc", name);
            }
        }

        private void OnNpcSpawnInitialized()
        {
            foreach (KeyValuePair<string, MonumentSpawnPoint> kvp in MonumentSpawnPoints) TryRegisterPresetsUsage(kvp.Value, kvp.Key);
            foreach (KeyValuePair<string, MonumentSpawnPoint> kvp in UnderwaterLabSpawnPoints) TryRegisterPresetsUsage(kvp.Value, kvp.Key);
            foreach (KeyValuePair<string, MonumentSpawnPoint> kvp in TunnelSpawnPoints) TryRegisterPresetsUsage(kvp.Value, kvp.Key);

            foreach (KeyValuePair<string, CustomMonumentSpawnPoint> kvp in CustomSpawnPoints) TryRegisterPresetsUsage(kvp.Value, kvp.Key);

            foreach (KeyValuePair<string, EventSpawnPoint> kvp in EventSpawnPoints) TryRegisterPresetsUsage(kvp.Value, kvp.Key);
            if (CargoConfig != null) TryRegisterPresetsUsage(CargoConfig, "CargoShip");

            foreach (KeyValuePair<string, RoadOrBiomeSpawnPoint> kvp in RoadSpawnPoints) TryRegisterPresetsUsage(kvp.Value, kvp.Key);
            foreach (KeyValuePair<string, RoadOrBiomeSpawnPoint> kvp in BiomeSpawnPoints) TryRegisterPresetsUsage(kvp.Value, kvp.Key);
        }
        #endregion API From NpcSpawn
    }
}

namespace Oxide.Plugins.BetterNpcExtensionMethods
{
    public static class LinqManager
    {
        public static bool Any<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) if (predicate(enumerator.Current)) return true;
            return false;
        }

        public static HashSet<TSource> Where<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            HashSet<TSource> result = new HashSet<TSource>();
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) if (predicate(enumerator.Current)) result.Add(enumerator.Current);
            return result;
        }

        public static TSource FirstOrDefault<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) if (predicate(enumerator.Current)) return enumerator.Current;
            return default(TSource);
        }

        public static string GetFileName(this string path) => path.Split('/')[^1].Split('.')[0];

        public static TSource Min<TSource>(this IEnumerable<TSource> source, Func<TSource, float> predicate)
        {
            TSource result = default(TSource);
            float resultValue = float.MaxValue;
            using (var enumerator = source.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    TSource element = enumerator.Current;
                    float elementValue = predicate(element);
                    if (elementValue < resultValue)
                    {
                        result = element;
                        resultValue = elementValue;
                    }
                }
            }
            return result;
        }

        public static bool AreEqual(this float a, float b, float epsilon = 0.001f) => Math.Abs(a - b) < epsilon;

        public static int SafeToInt(this double value)
        {
            double rounded = Math.Round(value, 0, MidpointRounding.AwayFromZero);
            if (rounded > int.MaxValue) return int.MaxValue;
            if (rounded < int.MinValue) return int.MinValue;
            return (int)rounded;
        }

        public static bool CorrectVector3(this string str)
        {
            if (string.IsNullOrWhiteSpace(str)) return false;
            if (str.ToVector3() == default) return false;
            return true;
        }
    }

    public static class FindPositionManager
    {
        public static bool IsRaycast(this Vector3 position, float height, int layers, out RaycastHit raycastHit) => Physics.Raycast(position, Vector3.down, out raycastHit, height, layers);

        public static bool IsNavMesh(this Vector3 position, float radius, int areaMask, out NavMeshHit navMeshHit) => NavMesh.SamplePosition(position, out navMeshHit, radius, areaMask);

        public static bool IsAvailableTopology(this Vector3 position, int findTopology, bool isBlocked)
        {
            int topology = TerrainMeta.TopologyMap.GetTopology(position);
            if (isBlocked) return (topology & findTopology) == 0;
            else return (topology & findTopology) != 0;
        }
    }

    public static class EntityManager
    {

        public static bool IsExists(this BaseNetworkable entity) => entity != null && !entity.IsDestroyed;
        public static bool IsPlayer(this BasePlayer player) => player != null && player.userID.IsSteamId();
        public static Vector3 AddToY(this Vector3 vector3, float offset) => vector3.WithY(vector3.y + offset);
        public static Vector3 GetGlobalPosition(this Transform tr, Vector3 local) => tr.TransformPoint(local);
        public static Vector3 GetLocalPosition(this Transform tr, Vector3 global) => tr.InverseTransformPoint(global);
    }

    public static class CoroutineManager
    {
        public static Coroutine Start(this IEnumerator action) => ServerMgr.Instance.StartCoroutine(action);

        public static void Stop(this Coroutine coroutine)
        {
            if (coroutine == null) return;
            ServerMgr.Instance.StopCoroutine(coroutine);
        }
    }

    public static class DebugDraw
    {

        public static void Sphere(BasePlayer player, Vector3 pos, Color color, float radius = 1f, float duration = 10f, bool distanceFade = false, bool zTest = false)
        {
            player.SendConsoleCommand("ddraw.sphere", duration, color, pos, radius, distanceFade, zTest);
        }

        public static void Line(BasePlayer player, Vector3 start, Vector3 end, Color color, float duration = 10f, bool distanceFade = false, bool zTest = false)
        {
            player.SendConsoleCommand("ddraw.line", duration, color, start, end, distanceFade, zTest);
        }

        public static void Text(BasePlayer player, Vector3 pos, string text, Color color, int textSize = 12, float duration = 10f, bool distanceFade = false, bool zTest = false)
        {
            player.SendConsoleCommand("ddraw.text", duration, color, pos, $"<size={textSize}>{text}</size>", distanceFade, zTest);
        }
    }
}