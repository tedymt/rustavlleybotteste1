using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Text;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using Oxide.Core.Plugins;
using Oxide.Core;
using UnityEngine;
using Building = BuildingManager.Building;
using Pool = Facepunch.Pool;

namespace Oxide.Plugins
{
    [Info("Limit Entities", "MON@H", "2.3.6")]
    [Description("Limiting the number of entities a player can build")]
    public class LimitEntities : RustPlugin
    {
        #region Class Fields

        [PluginReference] private readonly Plugin RustTranslationAPI, ZoneManager;

        private readonly Cache _cache = new();

        private static class Static
        {
            public const string PermissionAdmin = "limitentities.admin";
            public const string PermissionImmunity = "limitentities.immunity";
            public static float BuildingDetectionRangeSqr = 1.51f * 1.51f;
            public static LimitEntities Instance;
            public static readonly object False = false;
            private static readonly Regex Tags = new("<color=.+?>|</color>|<size=.+?>|</size>|<i>|</i>|<b>|</b>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
            public static readonly string LogLine = new('=', 30);

            public static string StripRustTags(string text) => string.IsNullOrWhiteSpace(text) ? text : Tags.Replace(text, string.Empty);
        }

        private class Cache
        {
            public readonly Dictionary<string, Dictionary<uint, string>> DisplayNames = new();
            public readonly Dictionary<uint, BuildingEntities> Buildings = new();
            public readonly Dictionary<uint, HashSet<Vector3>> RadiusPrefabPositions = new();
            public readonly Dictionary<ulong, PlayerData> PlayersData = new();
            public readonly Dictionary<Vector3, ulong> PoweredLightsDeployers = new();
            public readonly PermissionData PermissionData = new();
            public readonly Prefabs Prefabs = new();
            public readonly HashSet<string> ZoneManagerZoneIDs = new();
        }

        public enum LogLevel : byte
        {
            Off,
            Error,
            Warning,
            Info,
            Debug,
        }

        public class BuildingEntities
        {
            public uint BuildingID { get; set; }
            public readonly Dictionary<uint, int> EntitiesCount = new();
            public readonly HashSet<ulong> EntitiesIds = new();

            public void AddEntity(BaseEntity entity)
            {
                EntitiesIds.Add(entity.net.ID.Value);
                AddRange(Static.Instance.GetPrefabID(entity.prefabID), 1);
            }

            private void AddRange(uint prefabID, int count)
            {
                EntitiesCount[prefabID] = GetEntityCount(prefabID) + count;
            }

            public void RemoveEntity(BaseEntity entity)
            {
                EntitiesIds.Remove(entity.net.ID.Value);
                uint prefabID = Static.Instance.GetPrefabID(entity.prefabID);
                if (!EntitiesCount.TryGetValue(prefabID, out int count))
                {
                    return;
                }

                if (count < 2)
                {
                    EntitiesCount.Remove(prefabID);
                    return;
                }

                EntitiesCount[prefabID]--;
            }

            public int GetEntityCount(uint prefabID)
            {
                EntitiesCount.TryGetValue(prefabID, out int count);
                return count;
            }

            public BuildingEntities(uint buildingID)
            {
                BuildingID = buildingID;
            }
        }

        public class PlayerData
        {
            public readonly string PlayerIdString;
            public PermissionEntry Perms;
            public bool HasImmunity { get; private set; }
            public readonly PlayerEntities PlayerEntities = new();

            public PlayerData(ulong playerId)
            {
                PlayerIdString = playerId.ToString();
                UpdatePerms();
            }

            public void UpdatePerms()
            {
                HasImmunity = Static.Instance.permission.UserHasPermission(PlayerIdString, Static.PermissionImmunity);
                Perms = Static.Instance.GetPlayerPermissions(this);
            }

            public void AddEntity(uint prefabID)
            {
                PlayerEntities.AddEntity(prefabID);
            }

            public void RemoveEntity(uint prefabID)
            {
                PlayerEntities.RemoveEntity(prefabID);
            }

            public bool CanBuild()
            {
                return Perms == null || HasImmunity || Perms.LimitsGlobal.LimitTotal != 0;
            }

            public bool IsGlobalLimit()
            {
                if (Perms == null)
                {
                    return false;
                }

                return PlayerEntities.TotalCount >= Perms.LimitsGlobal.LimitTotal;
            }

            public bool IsGlobalLimit(uint prefabId)
            {
                if (Perms == null)
                {
                    return false;
                }

                if (!Perms.LimitsGlobal.LimitEntitiesCache.TryGetValue(prefabId, out int limitGlobal))
                {
                    return false;
                }

                PlayerEntities.Entities.TryGetValue(prefabId, out int countPlayersEntities);
                return countPlayersEntities >= limitGlobal;
            }

            public bool IsBuildingLimit(BuildingEntities entities)
            {
                if (Perms == null)
                {
                    return false;
                }

                return entities.EntitiesIds.Count >= Perms.LimitsBuilding.LimitTotal;
            }

            public bool IsBuildingLimit(BuildingEntities entities, uint prefabId)
            {
                if (Perms == null)
                {
                    return false;
                }

                if (!Perms.LimitsBuilding.LimitEntitiesCache.TryGetValue(prefabId, out int limitBuildingEntity))
                {
                    return false;
                }

                entities.EntitiesCount.TryGetValue(prefabId, out int countBuildingEntity);
                return countBuildingEntity >= limitBuildingEntity;
            }

            public bool IsRadiusLimit(uint prefabId, Vector3 position, Dictionary<uint, HashSet<Vector3>> radiusPrefabPositions)
            {
                if (Perms == null)
                {
                    return false;
                }

                if (Perms.LimitsRadius.Radius <= 0f)
                {
                    return false;
                }

                if (!Perms.LimitsRadius.LimitEntitiesCache.TryGetValue(prefabId, out int limitRadius))
                {
                    return false;
                }

                if (!radiusPrefabPositions.TryGetValue(prefabId, out HashSet<Vector3> positions) || positions.Count == 0)
                {
                    return false;
                }

                int count = 0;
                float radiusSqr = Perms.LimitsRadius.Radius * Perms.LimitsRadius.Radius;

                foreach (Vector3 entityPos in positions)
                {
                    if (Vector3.SqrMagnitude(position - entityPos) <= radiusSqr)
                    {
                        if (++count >= limitRadius)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            public float GetGlobalPercentage()
            {
                if (Perms == null)
                {
                    return 0f;
                }

                return (float)PlayerEntities.TotalCount / Perms.LimitsGlobal.LimitTotal * 100;
            }

            public float GetGlobalPercentage(uint prefabId)
            {
                if (Perms == null)
                {
                    return 0f;
                }

                if (!Perms.LimitsGlobal.LimitEntitiesCache.TryGetValue(prefabId, out int limitGlobal))
                {
                    return 0f;
                }

                if (!PlayerEntities.Entities.TryGetValue(prefabId, out int countPlayersEntities))
                {
                    return 0f;
                }

                return (float)countPlayersEntities / limitGlobal * 100;
            }

            public float GetBuildingPercentage(BuildingEntities entities)
            {
                if (Perms == null)
                {
                    return 0f;
                }

                return (float)entities.EntitiesIds.Count / Perms.LimitsBuilding.LimitTotal * 100;
            }

            public float GetBuildingPercentage(BuildingEntities entities, uint prefabId)
            {
                if (Perms == null)
                {
                    return 0f;
                }

                if (!Perms.LimitsBuilding.LimitEntitiesCache.TryGetValue(prefabId, out int limitBuildingEntity))
                {
                    return 0f;
                }

                if (!entities.EntitiesCount.TryGetValue(prefabId, out int countBuildingEntity))
                {
                    return 0f;
                }

                return (float)countBuildingEntity / limitBuildingEntity * 100;
            }
        }

        public class PlayerEntities
        {
            public int TotalCount;
            public readonly Dictionary<uint, int> Entities = new();

            public void AddEntity(uint prefabID)
            {
                AddRange(prefabID, 1);
            }

            public void AddRange(uint prefabID, int count)
            {
                TotalCount += count;
                Entities[prefabID] = GetEntityCount(prefabID) + count;
            }

            public void RemoveEntity(uint prefabID)
            {
                if (TotalCount > 0)
                {
                    TotalCount--;
                }

                if (!Entities.TryGetValue(prefabID, out int count))
                {
                    return;
                }

                if (count < 2)
                {
                    Entities.Remove(prefabID);
                    return;
                }

                Entities[prefabID]--;
            }

            public void RemoveRange(uint prefabID, int count)
            {
                if (count >= TotalCount)
                {
                    TotalCount = 0;
                }
                else
                {
                    TotalCount -= count;
                }

                if (!Entities.TryGetValue(prefabID, out int value))
                {
                    return;
                }

                if (count >= value)
                {
                    Entities.Remove(prefabID);
                    return;
                }

                Entities[prefabID] = value - count;
            }

            public int GetEntityCount(uint prefabID) => Entities.TryGetValue(prefabID, out int count) ? count : 0;
        }

        private class Prefabs
        {
            public readonly Dictionary<uint, string> ShortNames = new();
            public readonly Dictionary<uint, uint> Groups = new();
            public readonly HashSet<uint> BuildingBlocks = new();
            public readonly HashSet<uint> Tracked = new();
        }

        private class PermissionData
        {
            public PermissionEntry[] Descending { get; set; }
            public string[] Registered { get; set; }
        }

        #endregion Class Fields

        #region Initialization

        private void Init() => HooksUnsubscribe();

        private void OnServerInitialized()
        {
            Log($"{Static.LogLine}\nStart", LogLevel.Debug);
            Static.Instance = this;
            RegisterPermissions();
            AddCommands();
            CacheGroupIds();
            CachePermissions();
            StoredDataLoad();
            CachePrefabIds();
            CacheEntities();
            RegisterMessages();

            foreach (BasePlayer activePlayer in BasePlayer.activePlayerList)
            {
                GetPlayerData(activePlayer.userID);
            }

            HooksSubscribe();
            Log("Finish", LogLevel.Debug);
        }

        private void Unload()
        {
            StoredDataSave();
            Static.Instance = null;
            Log($"Plugin unloaded\n{Static.LogLine}\n", LogLevel.Debug);
        }

        private void OnNewSave() => StoredDataClear();

        #endregion Initialization

        #region Configuration

        private PluginConfig _pluginConfig;

        public class PluginConfig
        {
            [JsonConverter(typeof(StringEnumConverter))]
            [DefaultValue(LogLevel.Off)]
            [JsonProperty(PropertyName = "Log Level (Debug, Info, Warning, Error, Off)", Order = 4)]
            public LogLevel LoggingLevel { get; set; }

            [JsonProperty(PropertyName = "Enable GameTip notifications")]
            public bool GameTipNotificationsEnabled { get; set; }

            [JsonProperty(PropertyName = "Enable notifications in chat")]
            public bool ChatNotificationsEnabled { get; set; }

            [JsonProperty(PropertyName = "Chat steamID icon")]
            public ulong SteamIDIcon { get; set; }

            [JsonProperty(PropertyName = "Commands list")]
            public string[] Commands { get; set; }

            [JsonProperty(PropertyName = "Warn when more than %")]
            [DefaultValue(80f)]
            public float WarnPercent { get; set; }

            [JsonProperty(PropertyName = "Building detection range")]
            [DefaultValue(1.51f)]
            public float BuildingDetectionRange { get; set; }

            [JsonProperty(PropertyName = "Track growable entities")]
            public bool TrackGrowable { get; set; }

            [JsonProperty(PropertyName = "Track powered lights")]
            public bool TrackPowerLights { get; set; }

            [JsonProperty(PropertyName = "Excluded list")]
            public string[] Excluded { get; set; }

            [JsonProperty(PropertyName = "Excluded skin IDs")]
            public ulong[] ExcludedSkinID { get; set; }

            [JsonProperty(PropertyName = "Use ZoneManager")]
            public bool UseZoneManager { get; init; }

            [JsonProperty(PropertyName = "ZoneManager include mode (true = include mode / false = exclude mode)")]
            public bool ZoneManagerIncludeMode { get; init; }

            [JsonProperty(PropertyName = "ZoneIDs", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public string[] ZoneManagerZoneIDs { get; set; }

            [JsonProperty(PropertyName = "Entity Groups")]
            public List<EntityGroup> EntityGroups { get; set; }

            [JsonProperty(PropertyName = "Permissions")]
            public PermissionEntry[] Permissions { get; set; }
        }

        public class PermissionEntry
        {
            [JsonProperty(PropertyName = "Permission")]
            public string Permission { get; set; }

            [JsonProperty(PropertyName = "Priority")]
            public int Priority { get; init; }

            [JsonProperty(PropertyName = "Limits Global")]
            public LimitsEntry LimitsGlobal { get; init; }

            [JsonProperty(PropertyName = "Limits Building")]
            public LimitsEntry LimitsBuilding { get; init; }

            [JsonProperty(PropertyName = "Limits Radius")]
            public LimitsRadius LimitsRadius { get; init; }

            [JsonProperty(PropertyName = "Limits Powered Lights")]
            public LimitsPoweredLights LimitsPoweredLights { get; init; }

            [JsonProperty(PropertyName = "Prevent excessive merging of buildings")]
            public bool MergingCheck { get; set; }

            [JsonConstructor]
            public PermissionEntry() { }

            public PermissionEntry(PermissionEntry entry)
            {
                Permission = entry?.Permission ?? string.Empty;
                Priority = entry?.Priority ?? 0;
                LimitsGlobal = entry?.LimitsGlobal ?? new LimitsEntry();
                LimitsBuilding = entry?.LimitsBuilding ?? new LimitsEntry();
                LimitsRadius = entry?.LimitsRadius ?? new LimitsRadius();
                LimitsPoweredLights = new LimitsPoweredLights(entry?.LimitsPoweredLights);
                MergingCheck = entry?.MergingCheck ?? false;
            }
        }

        public class LimitsEntry
        {
            [JsonProperty(PropertyName = "Limit Total")]
            public int LimitTotal { get; init; }

            [JsonProperty(PropertyName = "Limits Entities")]
            public SortedDictionary<string, int> LimitsEntities { get; init; }

            [JsonIgnore] public readonly Dictionary<uint, int> LimitEntitiesCache = new();

            public int GetEntityLimit(uint prefabID)
            {
                LimitEntitiesCache.TryGetValue(prefabID, out int limit);
                return limit;
            }
        }

        public class LimitsPoweredLights
        {
            [JsonProperty(PropertyName = "Maximum Point Count")]
            [DefaultValue(-1)]
            public int MaxPoints { get; init; }

            [JsonProperty(PropertyName = "Maximum Total Length")]
            [DefaultValue(-1f)]
            public float MaxLength { get; init; }

            [JsonProperty(PropertyName = "Maximum Distance Between Points")]
            [DefaultValue(-1f)]
            public float MaxLengthPoint { get; init; }

            [JsonConstructor]
            public LimitsPoweredLights()
            {
                MaxPoints = -1;
                MaxLength = -1f;
                MaxLengthPoint = -1f;
            }
            public LimitsPoweredLights(LimitsPoweredLights entry)
            {
                MaxPoints = entry?.MaxPoints ?? -1;
                MaxLength = entry?.MaxLength ?? -1f;
                MaxLengthPoint = entry?.MaxLengthPoint ?? -1f;
            }
        }

        public class LimitsRadius
        {
            [JsonProperty(PropertyName = "Radius")]
            public float Radius { get; init; }

            [JsonProperty(PropertyName = "Limits Entities")]
            public SortedDictionary<string, int> LimitsEntities { get; init; }

            [JsonIgnore] public readonly Dictionary<uint, int> LimitEntitiesCache = new();

            public LimitsRadius()
            {
                LimitsEntities = new SortedDictionary<string, int>();
            }

            public int GetEntityLimit(uint prefabID)
            {
                LimitEntitiesCache.TryGetValue(prefabID, out int limit);
                return limit;
            }
        }

        public class EntityGroup
        {
            [JsonProperty(PropertyName = "Group name")]
            public string Name { get; init; }

            [JsonIgnore] public uint ID { get; set; }

            [JsonProperty(PropertyName = "Group Entities list")]
            public List<string> ListEntities { get; init; }

            [JsonIgnore] public readonly List<uint> ListEntitiesCache = new();
        }

        protected override void LoadDefaultConfig() => PrintWarning("Loading Default Config");

        protected override void LoadConfig()
        {
            base.LoadConfig();
            Config.Settings.DefaultValueHandling = DefaultValueHandling.Populate;
            _pluginConfig = AdditionalConfig(Config.ReadObject<PluginConfig>());
            Config.WriteObject(_pluginConfig);
        }

        public PluginConfig AdditionalConfig(PluginConfig config)
        {
            config.Commands ??= new[] { "limits", "limit" };
            config.Excluded ??= new[] { "assets/prefabs/building/ladder.wall.wood/ladder.wooden.wall.prefab" };
            config.ExcludedSkinID ??= new ulong[0];
            config.ZoneManagerZoneIDs ??= new string[0];
            foreach (string zone in config.ZoneManagerZoneIDs)
            {
                _cache.ZoneManagerZoneIDs.Add(zone);
            }
            config.EntityGroups ??= new List<EntityGroup>
            {
                new()
                {
                    Name = "Foundations",
                    ListEntities = new List<string>
                    {
                        "assets/prefabs/building core/foundation.triangle/foundation.triangle.prefab",
                        "assets/prefabs/building core/foundation/foundation.prefab",
                    }
                },
                new()
                {
                    Name = "Furnace",
                    ListEntities = new List<string>
                    {
                        "assets/prefabs/deployable/furnace/furnace.prefab",
                        "assets/prefabs/deployable/legacyfurnace/legacy_furnace.prefab",
                        "assets/prefabs/deployable/playerioents/electricfurnace/electricfurnace.deployed.prefab",
                    }
                },
                new()
                {
                    Name = "PlanterBoxes",
                    ListEntities = new List<string>
                    {
                        "assets/prefabs/deployable/planters/planter.large.deployed.prefab",
                        "assets/prefabs/deployable/planters/planter.small.deployed.prefab",
                        "assets/prefabs/deployable/planters/planter.triangle.deployed.prefab",
                        "assets/prefabs/misc/decor_dlc/bath tub planter/bathtub.planter.deployed.prefab",
                        "assets/prefabs/misc/decor_dlc/minecart planter/minecart.planter.deployed.prefab",
                        "assets/prefabs/misc/decor_dlc/rail road planter/railroadplanter.deployed.prefab",
                        "assets/prefabs/misc/decor_dlc/rail road planter/triangle_railroad_planter.deployed.prefab",
                    }
                },
                new()
                {
                    Name = "Quarries",
                    ListEntities = new List<string>
                    {
                        "assets/prefabs/deployable/oil jack/mining.pumpjack.prefab",
                        "assets/prefabs/deployable/quarry/mining_quarry.prefab",
                    }
                },
                new()
                {
                    Name = "Roof",
                    ListEntities = new List<string>
                    {
                        "assets/prefabs/building core/roof.triangle/roof.triangle.prefab",
                        "assets/prefabs/building core/roof/roof.prefab",
                    }
                },
                new()
                {
                    Name = "TC",
                    ListEntities = new List<string>
                    {
                        "assets/prefabs/deployable/tool cupboard/cupboard.tool.deployed.prefab",
                        "assets/prefabs/deployable/tool cupboard/retro/cupboard.tool.retro.deployed.prefab",
                        "assets/prefabs/deployable/tool cupboard/shockbyte/cupboard.tool.shockbyte.deployed.prefab",
                    }
                },
            };
            config.Permissions ??= new PermissionEntry[]
            {
                new()
                {
                    Permission = "default",
                    Priority = 10,
                    LimitsGlobal = new LimitsEntry
                    {
                        LimitTotal = 2000,
                        LimitsEntities = new SortedDictionary<string, int>
                        {
                            { "assets/content/props/strobe light/strobelight.prefab", 10 },
                            { "assets/prefabs/deployable/campfire/campfire.prefab", 20 },
                            { "assets/prefabs/deployable/ceiling light/ceilinglight.deployed.prefab", 50 },
                            { "assets/prefabs/deployable/furnace.large/furnace.large.prefab", 5 },
                            { "assets/prefabs/deployable/oil refinery/refinery_small_deployed.prefab", 5 },
                            { "assets/prefabs/deployable/search light/searchlight.deployed.prefab", 10 },
                            { "assets/prefabs/deployable/windmill/electric.windmill.small.prefab", 10 },
                            { "assets/prefabs/npc/sam_site_turret/sam_site_turret_deployed.prefab", 0 },
                            { "Foundations", 300 },
                            { "Furnace", 10 },
                            { "PlanterBoxes", 50 },
                            { "Quarries", 2 },
                            { "Roof", 200 },
                            { "TC", 10 },
                        }
                    },
                    LimitsBuilding = new LimitsEntry
                    {
                        LimitTotal = 1000,
                        LimitsEntities = new SortedDictionary<string, int>
                        {
                            { "assets/content/props/strobe light/strobelight.prefab", 5 },
                            { "assets/prefabs/deployable/search light/searchlight.deployed.prefab", 5 },
                            { "assets/prefabs/deployable/windmill/electric.windmill.small.prefab", 5 },
                        }
                    },
                    LimitsRadius = new LimitsRadius
                    {
                        Radius = 20f,
                        LimitsEntities = new SortedDictionary<string, int>
                        {
                            { "assets/content/props/strobe light/strobelight.prefab", 5 },
                            { "assets/prefabs/deployable/search light/searchlight.deployed.prefab", 5 },
                            { "assets/prefabs/deployable/windmill/electric.windmill.small.prefab", 5 },
                        }
                    }
                },
                new()
                {
                    Permission = "vip",
                    Priority = 20,
                    LimitsGlobal = new LimitsEntry
                    {
                        LimitTotal = 5000,
                        LimitsEntities = new SortedDictionary<string, int>
                        {
                            { "Foundations", 500 },
                            { "Roof", 400 },
                        },
                    },
                    LimitsBuilding = new LimitsEntry
                    {
                        LimitTotal = 2000,
                        LimitsEntities = new SortedDictionary<string, int>
                        {
                            { "assets/content/props/strobe light/strobelight.prefab", 15 },
                            { "assets/prefabs/deployable/search light/searchlight.deployed.prefab", 15 },
                            { "assets/prefabs/deployable/windmill/electric.windmill.small.prefab", 15 },
                        }
                    }
                },
                new()
                {
                    Permission = "elite",
                    Priority = 30,
                    LimitsGlobal = new LimitsEntry
                    {
                        LimitTotal = 10000,
                        LimitsEntities = new SortedDictionary<string, int>
                        {
                            { "Foundations", 2000 },
                            { "Roof", 1000 },
                        },
                    },
                    LimitsBuilding = new LimitsEntry
                    {
                        LimitTotal = 5000,
                        LimitsEntities = new SortedDictionary<string, int>
                        {
                            { "assets/content/props/strobe light/strobelight.prefab", 20 },
                            { "assets/prefabs/deployable/search light/searchlight.deployed.prefab", 20 },
                            { "assets/prefabs/deployable/windmill/electric.windmill.small.prefab", 20 },
                        }
                    }
                },
            };
            for (int index = 0; index < config.Permissions.Length; index++)
            {
                config.Permissions[index] = new PermissionEntry(config.Permissions[index]);
            }

            if (config.BuildingDetectionRange < 0f)
            {
                config.BuildingDetectionRange = 0f;
            }
            Static.BuildingDetectionRangeSqr = config.BuildingDetectionRange * config.BuildingDetectionRange;
            return config;
        }

        #endregion Configuration

        #region Stored Data

        private StoredData _storedData;

        private class StoredData
        {
            public readonly Dictionary<uint, ulong> BuildingsOwners = new();
        }

        public void StoredDataLoad()
        {
            try
            {
                _storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);
            }
            catch
            {
                StoredDataClear();
                StoredDataLoad();
            }

            if (_storedData.BuildingsOwners.Count > 0)
            {
                ListDictionary<uint, Building> serverBuildings = BuildingManager.server.buildingDictionary;

                List<uint> buildingsToRemove = Pool.Get<List<uint>>();
                foreach (uint buildingId in _storedData.BuildingsOwners.Keys)
                {
                    if (!serverBuildings.Contains(buildingId))
                    {
                        buildingsToRemove.Add(buildingId);
                    }
                }

                for (int index = 0; index < buildingsToRemove.Count; index++)
                {
                    uint buildingId = buildingsToRemove[index];
                    _storedData.BuildingsOwners.Remove(buildingId);
                }

                Pool.FreeUnsafe(ref buildingsToRemove);

                Log($"{_storedData.BuildingsOwners.Count} buildings with owners loaded from data file", LogLevel.Debug);
                return;
            }

            Log("Collecting all buildings owners on the server", LogLevel.Debug);

            for (int i = 0; i < BuildingManager.server.buildingDictionary.Values.Count; i++)
            {
                Building building = BuildingManager.server.buildingDictionary.Values[i];
                if (!building.HasDecayEntities() || _storedData.BuildingsOwners.ContainsKey(building.ID))
                {
                    continue;
                }

                ulong netID = uint.MaxValue;
                ulong ownerId = 0;
                for (int index = 0; index < building.decayEntities.Count; index++)
                {
                    DecayEntity decayEntity = building.decayEntities[index];
                    if (decayEntity.IsValid() && decayEntity.OwnerID.IsSteamId() && decayEntity.net.ID.Value < netID)
                    {
                        netID = decayEntity.net.ID.Value;
                        ownerId = decayEntity.OwnerID;
                    }
                }

                if (ownerId != 0)
                {
                    Log($"Adding missing owner ID: {ownerId} for building ID: {building.ID}", LogLevel.Debug);
                    _storedData.BuildingsOwners[building.ID] = ownerId;
                }
            }

            if (_storedData.BuildingsOwners.Count > 0)
            {
                Log($"{_storedData.BuildingsOwners.Count} buildings with owners added to data file", LogLevel.Debug);
                StoredDataSave();
                return;
            }

            Log("No buildings with owners found on the server!", LogLevel.Warning);
        }

        public void StoredDataClear()
        {
            PrintWarning("Creating a new data file");
            _storedData = new StoredData();
            StoredDataSave();
        }

        public void StoredDataSave()
        {
            if (_storedData != null)
            {
                Interface.Oxide.DataFileSystem.WriteObject(Name, _storedData);
            }
        }

        #endregion Stored Data

        #region Localization

        public string Lang(string key, string userIDString = null, params object[] args)
        {
            try
            {
                return string.Format(lang.GetMessage(key, this, userIDString), args);
            }
            catch (Exception ex)
            {
                PrintError($"Lang Key '{key}' threw exception:\n{ex}");
                throw;
            }
        }

        private static class LangKeys
        {
            public static class Error
            {
                private const string Base = nameof(Error) + ".";
                public const string EntityIsNotAllowed = Base + nameof(EntityIsNotAllowed);
                public const string NoPermission = Base + nameof(NoPermission);
                public const string PlayerNotFound = Base + nameof(PlayerNotFound);

                public static class LimitBuilding
                {
                    private const string SubBase = Base + nameof(LimitBuilding) + ".";
                    public const string EntityMergeBlocked = SubBase + nameof(EntityMergeBlocked);
                    public const string EntityReached = SubBase + nameof(EntityReached);
                    public const string MergeBlocked = SubBase + nameof(MergeBlocked);
                    public const string Reached = SubBase + nameof(Reached);
                }

                public static class LimitGlobal
                {
                    private const string SubBase = Base + nameof(LimitGlobal) + ".";
                    public const string EntityReached = SubBase + nameof(EntityReached);
                    public const string Reached = SubBase + nameof(Reached);
                }

                public static class LimitPoweredLights
                {
                    private const string SubBase = Base + nameof(LimitPoweredLights) + ".";
                    public const string Length = SubBase + nameof(Length);
                    public const string LengthPoint = SubBase + nameof(LengthPoint);
                    public const string PointCount = SubBase + nameof(PointCount);
                }
                public static class LimitRadius
                {
                    private const string SubBase = Base + nameof(LimitRadius) + ".";
                    public const string EntityReached = SubBase + nameof(EntityReached);
                }
            }

            public static class Format
            {
                private const string Base = nameof(Format) + ".";
                public const string Meters = Base + nameof(Meters);
                public const string Prefix = Base + nameof(Prefix);
            }

            public static class Info
            {
                private const string Base = nameof(Info) + ".";
                public const string Help = Base + nameof(Help);
                public const string LimitBuilding = Base + nameof(LimitBuilding);
                public const string LimitBuildingEntity = Base + nameof(LimitBuildingEntity);
                public const string LimitGlobal = Base + nameof(LimitGlobal);
                public const string LimitGlobalEntity = Base + nameof(LimitGlobalEntity);
                public const string Limits = Base + nameof(Limits);
                public const string TotalAmount = Base + nameof(TotalAmount);
                public const string Unlimited = Base + nameof(Unlimited);
            }
        }

        private readonly Dictionary<string, string> _langEn = new()
        {
            [LangKeys.Error.EntityIsNotAllowed] = "You are not allowed to build <color=#FFA500>{0}</color>",
            [LangKeys.Error.PlayerNotFound] = "Player <color=#FFA500>{0}</color> not found!",
            [LangKeys.Error.LimitBuilding.EntityMergeBlocked] = "You can't merge these buildings because the limit of <color=#FFA500>{1}</color> will be exceeded by <color=#FFA500>{0}</color>",
            [LangKeys.Error.LimitBuilding.EntityReached] = "You have reached the limit <color=#FFA500>{0}</color> of <color=#FFA500>{1}</color> <color=#FFA500>{2}</color> in this building",
            [LangKeys.Error.LimitBuilding.MergeBlocked] = "You can't merge these buildings because the limit of entities will be exceeded by <color=#FFA500>{0}</color>",
            [LangKeys.Error.LimitBuilding.Reached] = "You have reached the limit <color=#FFA500>{0}</color> of <color=#FFA500>{1}</color> entities in this building",
            [LangKeys.Error.LimitGlobal.EntityReached] = "You have reached the limit <color=#FFA500>{0}</color> of <color=#FFA500>{1}</color> <color=#FFA500>{2}</color>",
            [LangKeys.Error.LimitGlobal.Reached] = "You have reached the global limit <color=#FFA500>{0}</color> of <color=#FFA500>{1}</color> entities",
            [LangKeys.Error.LimitPoweredLights.Length] = "You have exceeded the total length limit of <color=#FFA500>{0}</color> meters for <color=#FFA500>{1}</color>",
            [LangKeys.Error.LimitPoweredLights.LengthPoint] = "The distance between points exceeds the limit of <color=#FFA500>{0}</color> meters for <color=#FFA500>{1}</color>",
            [LangKeys.Error.LimitPoweredLights.PointCount] = "You have reached the limit of <color=#FFA500>{0}</color> points for <color=#FFA500>{1}</color>",
            [LangKeys.Error.LimitRadius.EntityReached] = "You have reached the limit <color=#FFA500>{0}</color> of <color=#FFA500>{1}</color> <color=#FFA500>{2}</color> {3}",
            [LangKeys.Error.NoPermission] = "You do not have permission to use this command!",
            [LangKeys.Format.Meters] = "within a radius of <color=#FFA500>{0}</color> meters",
            [LangKeys.Format.Prefix] = "<color=#00FF00>[Limit Entities]</color>: ",
            [LangKeys.Info.Help] = "Get current limits: <color=#FFFF00>/{0}</color>",
            [LangKeys.Info.LimitBuilding] = "You have built <color=#FFA500>{0}</color> of <color=#FFA500>{1}</color> entities in this building",
            [LangKeys.Info.LimitBuildingEntity] = "You have built <color=#FFA500>{0}</color> of <color=#FFA500>{1}</color> <color=#FFA500>{2}</color> in this building",
            [LangKeys.Info.LimitGlobal] = "You have built <color=#FFA500>{0}</color> of <color=#FFA500>{1}</color> entities",
            [LangKeys.Info.LimitGlobalEntity] = "You have built <color=#FFA500>{0}</color> of <color=#FFA500>{1}</color> <color=#FFA500>{2}</color>",
            [LangKeys.Info.Limits] = "\nYour global limits are:\n<color=#FFA500>{0}</color>\nYour limits per building are:\n<color=#FFA500>{1}</color>\n{2}",
            [LangKeys.Info.TotalAmount] = "Total amount: <color=#FFA500>{0}</color>",
            [LangKeys.Info.Unlimited] = "Your ability to build is unlimited",

            ["Foundations"] = "Foundations",
            ["Furnace"] = "Furnace",
            ["PlanterBoxes"] = "PlanterBoxes",
            ["Quarries"] = "Quarries",
            ["Roof"] = "Roof",
            ["TC"] = "TC",
        };

        private readonly Dictionary<string, string> _langRu = new()
        {
            [LangKeys.Error.EntityIsNotAllowed] = "Вам не разрешено строить <color=#FFA500>{0}</color>",
            [LangKeys.Error.PlayerNotFound] = "Игрок <color=#FFA500>{0}</color> не найден!",
            [LangKeys.Error.LimitBuilding.EntityMergeBlocked] = "Вы не можете объединить эти здания, поскольку лимит <color=#FFA500>{1}</color> будет превышен на <color=#FFA500>{0}</color>",
            [LangKeys.Error.LimitBuilding.EntityReached] = "Вы достигли лимита <color=#FFA500>{0}</color> из <color=#FFA500>{1}</color> для <color=#FFA500>{2}</color> в этом здании",
            [LangKeys.Error.LimitBuilding.MergeBlocked] = "Вы не можете объединить эти здания, поскольку лимит объектов будет превышен на <color=#FFA500>{0}</color>",
            [LangKeys.Error.LimitBuilding.Reached] = "Вы достигли лимита <color=#FFA500>{0}</color> из <color=#FFA500>{1}</color> объектов в этом здании",
            [LangKeys.Error.LimitGlobal.EntityReached] = "Вы достигли лимита <color=#FFA500>{0}</color> из <color=#FFA500>{1}</color> для <color=#FFA500>{2}</color>",
            [LangKeys.Error.LimitGlobal.Reached] = "Вы достигли глобального лимита <color=#FFA500>{0}</color> из <color=#FFA500>{1}</color> объектов",
            [LangKeys.Error.LimitPoweredLights.Length] = "Вы превысили общий лимит длины <color=#FFA500>{0}</color> метров для <color=#FFA500>{1}</color>",
            [LangKeys.Error.LimitPoweredLights.LengthPoint] = "Расстояние между точками превышает лимит в <color=#FFA500>{0}</color> метров для <color=#FFA500>{1}</color>",
            [LangKeys.Error.LimitPoweredLights.PointCount] = "Вы достигли лимита в <color=#FFA500>{0}</color> точек для <color=#FFA500>{1}</color>",
            [LangKeys.Error.LimitRadius.EntityReached] = "Вы достигли лимита <color=#FFA500>{0}</color> из <color=#FFA500>{1}</color> для <color=#FFA500>{2}</color> {3}",
            [LangKeys.Error.NoPermission] = "У вас нет разрешения на использование этой команды!",
            [LangKeys.Format.Meters] = "в радиусе <color=#FFA500>{0}</color> метров",
            [LangKeys.Format.Prefix] = "<color=#00FF00>[Лимит построек]</color>: ",
            [LangKeys.Info.Help] = "Отобразить текущие лимиты: <color=#FFFF00>/{0}</color>",
            [LangKeys.Info.LimitBuilding] = "Вы построили <color=#FFA500>{0}</color> из <color=#FFA500>{1}</color> объектов в этом здании",
            [LangKeys.Info.LimitBuildingEntity] = "Вы построили <color=#FFA500>{0}</color> из <color=#FFA500>{1}</color> <color=#FFA500>{2}</color> в этом здании",
            [LangKeys.Info.LimitGlobal] = "Вы построили <color=#FFA500>{0}</color> из <color=#FFA500>{1}</color> объектов",
            [LangKeys.Info.LimitGlobalEntity] = "Вы построили <color=#FFA500>{0}</color> из <color=#FFA500>{1}</color> <color=#FFA500>{2}</color>",
            [LangKeys.Info.Limits] = "\nВаши глобальные лимиты:\n<color=#FFA500>{0}</color>\nВаши лимиты для постройки:\n<color=#FFA500>{1}</color>\n{2}",
            [LangKeys.Info.TotalAmount] = "Общее количество: <color=#FFA500>{0}</color>",
            [LangKeys.Info.Unlimited] = "Ваши возможности строительства не ограничены",

            ["Foundations"] = "Фундамент",
            ["Furnace"] = "Печь",
            ["PlanterBoxes"] = "Плантация",
            ["Quarries"] = "Карьер",
            ["Roof"] = "Крыша",
            ["TC"] = "Шкаф с инструментами",
        };

        private readonly Dictionary<string, string> _langUk = new()
        {
            [LangKeys.Error.EntityIsNotAllowed] = "Вам не дозволено будувати <color=#FFA500>{0}</color>",
            [LangKeys.Error.PlayerNotFound] = "Гравеця <color=#FFA500>{0}</color> не знайдено!",
            [LangKeys.Error.LimitBuilding.EntityMergeBlocked] = "Ви не можете об'єднати ці будівлі, оскільки ліміт <color=#FFA500>{1}</color> буде перевищено на <color=#FFA500>{0}</color>",
            [LangKeys.Error.LimitBuilding.EntityReached] = "Ви досягли ліміту <color=#FFA500>{0}</color> із <color=#FFA500>{1}</color> для <color=#FFA500>{2}</color> у цій будівлі",
            [LangKeys.Error.LimitBuilding.MergeBlocked] = "Ви не можете об'єднати ці будівлі, оскільки ліміт об'єктів буде перевищено на <color=#FFA500>{0}</color>",
            [LangKeys.Error.LimitBuilding.Reached] = "Ви досягли ліміту <color=#FFA500>{0}</color> із <color=#FFA500>{1}</color> об'єктів в цьому будинку",
            [LangKeys.Error.LimitGlobal.EntityReached] = "Ви досягли ліміту <color=#FFA500>{0}</color> із <color=#FFA500>{1}</color> для <color=#FFA500>{2}</color>",
            [LangKeys.Error.LimitGlobal.Reached] = "Ви досягли глобального ліміту <color=#FFA500>{0}</color> із <color=#FFA500>{1}</color> об'єктів",
            [LangKeys.Error.LimitPoweredLights.Length] = "Ви перевищили загальний ліміт довжини <color=#FFA500>{0}</color> метрів для <color=#FFA500>{1}</color>",
            [LangKeys.Error.LimitPoweredLights.LengthPoint] = "Відстань між точками перевищує ліміт у <color=#FFA500>{0}</color> метрів для <color=#FFA500>{1}</color>",
            [LangKeys.Error.LimitPoweredLights.PointCount] = "Ви досягли ліміту в <color=#FFA500>{0}</color> точок для <color=#FFA500>{1}</color>",
            [LangKeys.Error.LimitRadius.EntityReached] = "Ви досягли ліміту <color=#FFA500>{0}</color> із <color=#FFA500>{1}</color> для <color=#FFA500>{2}</color> {3}",
            [LangKeys.Error.NoPermission] = "У вас немає дозволу використовувати цю команду!",
            [LangKeys.Format.Meters] = "у радіусі <color=#FFA500>{0}</color> метрів",
            [LangKeys.Format.Prefix] = "<color=#00FF00>[Ліміт об'єктів]</color>: ",
            [LangKeys.Info.Help] = "Відобразити поточні ліміти: <color=#FFFF00>/{0}</color>",
            [LangKeys.Info.LimitBuilding] = "Ви побудували <color=#FFA500>{0}</color> із <color=#FFA500>{1}</color> об'єктів в цьому будинку",
            [LangKeys.Info.LimitBuildingEntity] = "Ви побудували <color=#FFA500>{0}</color> із <color=#FFA500>{1}</color> <color=#FFA500>{2}</color> в цьому будинку",
            [LangKeys.Info.LimitGlobal] = "Ви побудували <color=#FFA500>{0}</color> із <color=#FFA500>{1}</color> об'єктів",
            [LangKeys.Info.LimitGlobalEntity] = "Ви побудували <color=#FFA500>{0}</color> із <color=#FFA500>{1}</color> <color=#FFA500>{2}</color>",
            [LangKeys.Info.Limits] = "\nВаші глобальні ліміти:\n<color=#FFA500>{0}</color>\nВаші ліміти для будівництва:\n<color=#FFA500>{1}</color>\n{2}",
            [LangKeys.Info.TotalAmount] = "Загальна кількість: <color=#FFA500>{0}</color>",
            [LangKeys.Info.Unlimited] = "Ваша можливість будувати необмежена",

            ["Foundations"] = "Фундамент",
            ["Furnace"] = "Піч",
            ["PlanterBoxes"] = "Плантація",
            ["Quarries"] = "Кар'єр",
            ["Roof"] = "Дах",
            ["TC"] = "Шафа з інструментами",
        };

        public void RegisterMessages()
        {
            lang.RegisterMessages(_langEn, this);
            lang.RegisterMessages(_langRu, this, "ru");
            lang.RegisterMessages(_langUk, this, "uk");
        }

        #endregion Localization

        #region Commands

        private void CmdLimitEntities(BasePlayer player, string _, string[] args)
        {
            if (!player.IsValid() || !player.userID.IsSteamId())
            {
                return;
            }

            BasePlayer target = player;

            if (args is { Length: > 0 })
            {
                string strNameOrIDOrIP = args[0];

                target = BasePlayer.FindAwakeOrSleeping(strNameOrIDOrIP);

                if (!target)
                {
                    PlayerSendMessage(player, Lang(LangKeys.Error.PlayerNotFound, player.UserIDString, strNameOrIDOrIP));
                    return;
                }
            }

            PlayerSendMessage(player, GetPlayerLimitString(player, target));
        }

        [ConsoleCommand("limitentities.list")]
        private void CmdLimitEntitiesList(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();

            if (player.IsValid() && !IsPlayerAdmin(player))
            {
                return;
            }

            StringBuilder sb = new();

            sb.AppendLine();
            sb.Append("All tracked entities list start");
            sb.AppendLine();

            foreach (uint trackedPrefabID in _cache.Prefabs.Tracked)
            {
                sb.AppendLine();
                sb.Append("PrefabID: ");
                sb.Append(trackedPrefabID);
                sb.AppendLine();
                sb.Append("PrefabShortName: ");
                if (_cache.Prefabs.ShortNames.TryGetValue(trackedPrefabID, out string shortName))
                {
                    sb.Append(shortName);
                }

                sb.AppendLine();
                sb.Append("Prefab: ");
                sb.Append(StringPool.Get(trackedPrefabID));
                sb.AppendLine();
            }

            sb.AppendLine();
            sb.Append("All tracked entities list finish");
            sb.AppendLine();
            Log(sb.ToString(), LogLevel.Off);
            Puts($"Successfully listed {_cache.Prefabs.Tracked.Count} entities in the log file. You can find the log at: {Interface.Oxide.LogDirectory}{Path.DirectorySeparatorChar}{Name}");
        }

        #endregion Commands

        #region Oxide Hooks

        private object CanBuild(Planner planner, Construction entity, Construction.Target target) => HandleCanBuild(planner?.GetOwnerPlayer(), entity, target);

        private object OnPoweredLightsPointAdd(StringLights stringLights, BasePlayer player, Vector3 vector, Vector3 _) => HandlePoweredLightsAddPoint(stringLights, player, vector);

        private void OnBuildingMerge(ServerBuildingManager _, Building to, Building from)
        {
            uint oldId = from.ID;
            uint newId = to.ID;

            NextTick(() => { HandleBuildingChange(oldId, newId, false); });
        }

        private void OnBuildingSplit(Building building, uint newId)
        {
            uint oldId = building.ID;
            NextTick(() => { HandleBuildingChange(oldId, newId, true); });
        }

        private void OnPlayerConnected(BasePlayer player) => GetPlayerData(player.userID);

        private void OnEntitySpawned(GrowableEntity entity)
        {
            if (!_pluginConfig.TrackGrowable)
            {
                return;
            }

            NextTick(() => { OnEntitySpawned(entity as BaseEntity); });
        }

        private void OnEntitySpawned(BaseEntity entity)
        {
            if (!IsValidEntity(entity))
            {
                return;
            }

            Vector3 position = entity.transform.position;
            ulong ownerID = entity.OwnerID;
            uint prefabID = GetPrefabID(entity.prefabID);
            uint buildingID = AddBuildingEntity(entity);

            if (buildingID > 0 && !_storedData.BuildingsOwners.ContainsKey(buildingID))
            {
                _storedData.BuildingsOwners[buildingID] = ownerID;
                Log($"{position} {ownerID} Added new building {buildingID}", LogLevel.Debug);
            }

            PlayerData playerData = GetPlayerData(ownerID);
            playerData.AddEntity(prefabID);

            if (_cache.RadiusPrefabPositions.TryGetValue(prefabID, out HashSet<Vector3> positions))
            {
                positions.Add(position);
            }

            if (_pluginConfig.LoggingLevel >= LogLevel.Debug)
            {
                PlayerEntities entities = playerData.PlayerEntities;
                Log($"{position} {ownerID} Increased player entity {GetShortName(prefabID)} count {entities.GetEntityCount(prefabID)}", LogLevel.Debug);
                Log($"{position} {ownerID} Increased player total entities count {entities.TotalCount}", LogLevel.Debug);
            }

            if ((_pluginConfig.ChatNotificationsEnabled || _pluginConfig.GameTipNotificationsEnabled)
                && _pluginConfig.WarnPercent > 0
                && playerData.Perms != null
                && !playerData.HasImmunity)
            {
                HandleEntityNotification(BasePlayer.FindByID(entity.OwnerID), playerData, buildingID, prefabID);
            }
        }

        private void OnEntityKill(BaseEntity entity)
        {
            if (!IsValidEntity(entity))
            {
                return;
            }

            uint prefabID = GetPrefabID(entity.prefabID);
            ulong ownerID = entity.OwnerID;
            uint buildingID = RemoveBuildingEntity(entity);
            Vector3 position = entity.transform.position;

            if (entity is BuildingBlock block)
            {
                if (BuildingManager.server.buildingDictionary.TryGetValue(buildingID, out Building building)
                    && building.decayEntities.Count == 1
                    && building.decayEntities.Contains(block))
                {
                    _storedData.BuildingsOwners.Remove(buildingID);
                    Log($"{position} {ownerID} Removed building {buildingID}", LogLevel.Debug);
                }
            }

            PlayerData playerData = GetPlayerData(ownerID);
            PlayerEntities entities = playerData.PlayerEntities;
            playerData.RemoveEntity(prefabID);

            if (_cache.RadiusPrefabPositions.TryGetValue(prefabID, out HashSet<Vector3> positions))
            {
                positions.Remove(position);
            }

            if (_pluginConfig.LoggingLevel >= LogLevel.Debug)
            {
                Log($"{position} {ownerID} Reduced player entity {GetShortName(prefabID)} count {entities.GetEntityCount(prefabID)}", LogLevel.Debug);
                Log($"{position} {ownerID} Reduced player total entities count {entities.TotalCount}", LogLevel.Debug);
            }
        }

        #endregion Oxide Hooks

        #region Permissions Hooks

        private void OnGroupCreated(string groupName) => OnGroupDeleted(groupName);

        private void OnGroupDeleted(string groupName)
        {
            foreach (string perm in _cache.PermissionData.Registered)
            {
                if (permission.GroupHasPermission(groupName, perm))
                {
                    HandleGroup(groupName);
                    break;
                }
            }
        }

        private void OnGroupPermissionGranted(string groupName, string perm) => OnGroupPermissionRevoked(groupName, perm);

        private void OnGroupPermissionRevoked(string groupName, string perm)
        {
            if (!_cache.PermissionData.Registered.Contains(perm))
            {
                return;
            }

            HandleGroup(groupName);
        }

        private void OnUserGroupAdded(string userIDString, string groupName) => OnUserGroupRemoved(userIDString, groupName);

        private void OnUserGroupRemoved(string userIDString, string groupName)
        {
            foreach (string perm in _cache.PermissionData.Registered)
            {
                if (permission.GroupHasPermission(groupName, perm))
                {
                    HandleUserByString(userIDString);
                    break;
                }
            }
        }

        private void OnUserPermissionGranted(string userIDString, string perm) => OnUserPermissionRevoked(userIDString, perm);

        private void OnUserPermissionRevoked(string userIDString, string perm)
        {
            if (!_cache.PermissionData.Registered.Contains(perm))
            {
                return;
            }

            HandleUserByString(userIDString);
        }

        #endregion Permissions Hooks

        #region Permissions Methods

        public void HandleGroup(string groupName)
        {
            foreach (string userIDString in permission.GetUsersInGroup(groupName))
            {
                HandleUserByString(userIDString[..17]);
            }
        }

        public void HandleUserByString(string userIDString)
        {
            if (!ulong.TryParse(userIDString, out ulong userID) || !_cache.PlayersData.TryGetValue(userID, out PlayerData playerData))
            {
                return;
            }

            playerData.UpdatePerms();
        }

        #endregion Permissions Methods

        #region Core Methods

        public void CacheGroupIds()
        {
            Log($"Cache creation for entities groups started", LogLevel.Debug);

            uint groupID = StringPool.closest + 10000;

            foreach (EntityGroup entityGroup in _pluginConfig.EntityGroups)
            {
                foreach (string prefab in entityGroup.ListEntities)
                {
                    if (!StringPool.toNumber.TryGetValue(prefab, out uint prefabID))
                    {
                        Log($"You have invalid entity '{prefab}' in group '{entityGroup.Name}' in your config file!", LogLevel.Error);
                        continue;
                    }

                    entityGroup.ListEntitiesCache.Add(prefabID);
                }

                if (entityGroup.ListEntitiesCache.Count == 0)
                {
                    Log($"You have 0 valid entities in '{entityGroup.Name}' group in your config file! To get a list of all supported prefabs use console command limitentities.list", LogLevel.Error);
                    continue;
                }

                do
                {
                    groupID++;
                } while (StringPool.toString.ContainsKey(groupID));

                entityGroup.ID = groupID;

                foreach (uint prefabID in entityGroup.ListEntitiesCache)
                {
                    _cache.Prefabs.Groups[prefabID] = groupID;
                }

                _cache.Prefabs.ShortNames[groupID] = entityGroup.Name;

                if (!_langEn.ContainsKey(entityGroup.Name))
                {
                    _langEn[entityGroup.Name] = entityGroup.Name;
                }

                if (!_langRu.ContainsKey(entityGroup.Name))
                {
                    _langRu[entityGroup.Name] = entityGroup.Name;
                }

                if (!_langUk.ContainsKey(entityGroup.Name))
                {
                    _langUk[entityGroup.Name] = entityGroup.Name;
                }
            }

            Log($"Cache creation for entities groups finished", LogLevel.Debug);
        }

        public void CachePermissions()
        {
            Dictionary<string, uint> groupStringPool = new();

            foreach (EntityGroup entityGroup in _pluginConfig.EntityGroups)
            {
                if (entityGroup.ID == 0)
                {
                    continue;
                }

                groupStringPool[entityGroup.Name] = entityGroup.ID;
            }

            foreach (PermissionEntry entry in _pluginConfig.Permissions)
            {
                foreach (KeyValuePair<string, int> entity in entry.LimitsBuilding.LimitsEntities)
                {
                    if (!groupStringPool.TryGetValue(entity.Key, out uint prefabID)
                    && !StringPool.toNumber.TryGetValue(entity.Key, out prefabID))
                    {
                        Log($"You have invalid entity '{entity.Key}' in your config file ({entry.Permission}: LimitsBuilding)!", LogLevel.Error);
                        continue;
                    }

                    entry.LimitsBuilding.LimitEntitiesCache[prefabID] = entity.Value;
                }

                foreach (KeyValuePair<string, int> entity in entry.LimitsGlobal.LimitsEntities)
                {
                    if (!groupStringPool.TryGetValue(entity.Key, out uint prefabID)
                    && !StringPool.toNumber.TryGetValue(entity.Key, out prefabID))
                    {
                        Log($"You have invalid entity '{entity.Key}' in your config file ({entry.Permission}: LimitsGlobal)!", LogLevel.Error);
                        continue;
                    }

                    entry.LimitsGlobal.LimitEntitiesCache[prefabID] = entity.Value;
                }

                foreach (KeyValuePair<string, int> entity in entry.LimitsRadius.LimitsEntities)
                {
                    if (!groupStringPool.TryGetValue(entity.Key, out uint prefabID)
                    && !StringPool.toNumber.TryGetValue(entity.Key, out prefabID))
                    {
                        Log($"You have invalid entity '{entity.Key}' in your config file ({entry.Permission}: LimitsRadius)!", LogLevel.Error);
                        continue;
                    }

                    entry.LimitsRadius.LimitEntitiesCache[prefabID] = entity.Value;
                    _cache.RadiusPrefabPositions[prefabID] = new HashSet<Vector3>();
                }
            }
        }

        public void CachePrefabIds()
        {
            Log("Cache creation started for deployables to be tracked", LogLevel.Debug);

            foreach (ItemDefinition itemDefinition in ItemManager.GetItemDefinitions())
            {
                BaseEntity entity = null;

                if (itemDefinition.GetComponent<ItemModDeployable>() is { entityPrefab.isValid: true } deployableComponent)
                {
                    // Standard method
                    if (deployableComponent.entityPrefab.Get() is { } prefabObject)
                    {
                        entity = prefabObject.GetComponent<BaseEntity>();
                    }
                    else
                    {
                        // Fallback method via GameManager
                        try
                        {
                            if (GameManager.server.FindPrefab(deployableComponent.entityPrefab.resourcePath) is { } foundPrefab)
                            {
                                entity = foundPrefab.GetComponent<BaseEntity>();
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"GameManager fallback failed for {itemDefinition.shortname}: {ex.Message}", LogLevel.Debug);
                        }
                    }
                }

                if (!entity
                    || !_pluginConfig.TrackGrowable && entity is GrowableEntity
                    || _pluginConfig.Excluded.Contains(entity.PrefabName))
                {
                    continue;
                }

                _cache.Prefabs.Tracked.Add(entity.prefabID);

                if (!_cache.Prefabs.ShortNames.ContainsKey(entity.prefabID))
                {
                    _cache.Prefabs.ShortNames[entity.prefabID] = entity.ShortPrefabName;
                }
            }

            // Building planner processing
            ProcessPlanner("building.planner");
            ProcessPlanner("boat.planner");

            Log($"Cache created for {_cache.Prefabs.Tracked.Count} deployables to be tracked", LogLevel.Debug);

            Log("All entities in config file check start", LogLevel.Debug);

            List<string> groupNames = Pool.Get<List<string>>();
            List<string> groupPrefabs = Pool.Get<List<string>>();
            List<string> prefabWrong = Pool.Get<List<string>>();

            foreach (EntityGroup entityGroup in _pluginConfig.EntityGroups)
            {
                if (entityGroup.ID == 0U)
                {
                    continue;
                }

                if (groupNames.Contains(entityGroup.Name))
                {
                    Log($"Group names must be unique! Skipped: '{entityGroup.Name}'", LogLevel.Debug);
                    continue;
                }

                groupNames.Add(entityGroup.Name);
                groupPrefabs.AddRange(entityGroup.ListEntities);
            }

            foreach (PermissionEntry entry in _pluginConfig.Permissions)
            {
                foreach (string entityPrefab in entry.LimitsBuilding.LimitsEntities.Keys)
                {
                    if (groupPrefabs.Contains(entityPrefab))
                    {
                        Log($"You can't use the same prefabs in groups and individually. Choose one and remove it from the other '{entityPrefab}'", LogLevel.Error);
                        continue;
                    }

                    if (groupNames.Contains(entityPrefab))
                    {
                        continue;
                    }

                    if (!StringPool.toNumber.TryGetValue(entityPrefab, out uint prefabID))
                    {
                        Log($"prefabID not found for '{entityPrefab}'", LogLevel.Debug);
                        if (!prefabWrong.Contains(entityPrefab))
                        {
                            prefabWrong.Add(entityPrefab);
                        }
                        continue;
                    }

                    if (!_cache.Prefabs.Tracked.Contains(prefabID))
                    {
                        Log($"Untracked prefab '{entityPrefab}' ({prefabID})", LogLevel.Debug);
                        if (!prefabWrong.Contains(entityPrefab))
                        {
                            prefabWrong.Add(entityPrefab);
                        }
                    }
                }

                foreach (string entityPrefab in entry.LimitsGlobal.LimitsEntities.Keys)
                {
                    if (groupPrefabs.Contains(entityPrefab))
                    {
                        Log($"You can't use the same prefabs in groups and individually. Choose one and remove it from the other '{entityPrefab}'", LogLevel.Error);
                        continue;
                    }

                    if (groupNames.Contains(entityPrefab))
                    {
                        continue;
                    }

                    if (!StringPool.toNumber.TryGetValue(entityPrefab, out uint prefabID))
                    {
                        Log($"prefabID not found for '{entityPrefab}'", LogLevel.Debug);
                        if (!prefabWrong.Contains(entityPrefab))
                        {
                            prefabWrong.Add(entityPrefab);
                        }
                        continue;
                    }

                    if (!_cache.Prefabs.Tracked.Contains(prefabID))
                    {
                        Log($"Untracked prefab '{entityPrefab}' ({prefabID})", LogLevel.Debug);
                        if (!prefabWrong.Contains(entityPrefab))
                        {
                            prefabWrong.Add(entityPrefab);
                        }
                    }
                }
            }

            if (prefabWrong.Count > 0)
            {
                Log($"You have {prefabWrong.Count} untracked prefabs in your config file! To get a list of all supported prefabs use console command limitentities.list\n{string.Join("\n", prefabWrong)}", LogLevel.Error);
            }

            Pool.FreeUnmanaged(ref groupNames);
            Pool.FreeUnmanaged(ref groupPrefabs);
            Pool.FreeUnmanaged(ref prefabWrong);
            Log($"All entities in config file check finish", LogLevel.Debug);
        }

        public void ProcessPlanner(string plannerShortName)
        {
            if (ItemManager.FindItemDefinition(plannerShortName) is not { } itemDefinition)
            {
                Log($"{plannerShortName} planner not found!", LogLevel.Error);
                return;
            }

            if (itemDefinition.GetComponent<ItemModEntity>() is { entityPrefab: not null } plannerEntityComponent)
            {
                GameObject plannerObject = plannerEntityComponent.entityPrefab.Get();

                if (!plannerObject && plannerEntityComponent.entityPrefab.isValid)
                {
                    try
                    {
                        plannerObject = GameManager.server.FindPrefab(plannerEntityComponent.entityPrefab.resourcePath);
                    }
                    catch (Exception ex)
                    {
                        Log($"{plannerShortName} planner GameManager fallback failed: {ex.Message}", LogLevel.Error);
                    }
                }

                if (plannerObject)
                {
                    if (plannerObject.GetComponent<Planner>() is { buildableList: not null } plannerComponent)
                    {
                        for (int i = 0; i < plannerComponent.buildableList.Length; i++)
                        {
                            if (plannerComponent.buildableList[i] is not { } entity)
                            {
                                continue;
                            }

                            _cache.Prefabs.Tracked.Add(entity.prefabID);

                            if (!_cache.Prefabs.ShortNames.ContainsKey(entity.prefabID))
                            {
                                _cache.Prefabs.ShortNames[entity.prefabID] = entity.ShortPrefabName;
                            }

                            if (entity is BuildingBlock)
                            {
                                _cache.Prefabs.BuildingBlocks.Add(entity.prefabID);
                            }
                        }
                    }
                }
            }
        }

        public void CacheEntities()
        {
            Log("Cache creation started for all players entities on server", LogLevel.Debug);

            int i = 0;
            int count = BaseEntity.saveList.Count;
            foreach (BaseEntity entity in BaseEntity.saveList)
            {
                if (++i == 1 || i % 10000 == 0 || i == count)
                {
                    Log($"{i} / {count}", LogLevel.Debug);
                }

                if (!IsValidEntity(entity))
                {
                    continue;
                }

                ulong ownerID = entity.OwnerID;
                AddBuildingEntity(entity);

                uint prefabID = GetPrefabID(entity.prefabID);
                GetPlayerData(ownerID).AddEntity(prefabID);

                if (_cache.RadiusPrefabPositions.TryGetValue(prefabID, out HashSet<Vector3> positions))
                {
                    positions.Add(entity.transform.position);
                }
            }

            Log($"Cache created for {_cache.PlayersData.Count} players", LogLevel.Debug);
        }

        public uint GetPrefabID(uint prefabID)
        {
            if (_cache.Prefabs.Groups.TryGetValue(prefabID, out uint group))
            {
                return group;
            }

            return prefabID;
        }

        public PlayerData GetPlayerData(ulong playerId)
        {
            if (!_cache.PlayersData.TryGetValue(playerId, out PlayerData playerData))
            {
                _cache.PlayersData[playerId] = playerData = new PlayerData(playerId);
            }

            return playerData;
        }

        public PermissionEntry GetPlayerPermissions(PlayerData player)
        {
            for (int index = 0; index < _cache.PermissionData.Descending.Length; index++)
            {
                PermissionEntry entry = _cache.PermissionData.Descending[index];
                if (permission.UserHasPermission(player.PlayerIdString, entry.Permission))
                {
                    return entry;
                }
            }

            return null;
        }

        public BuildingEntities GetBuildingData(uint buildingID)
        {
            if (!_cache.Buildings.TryGetValue(buildingID, out BuildingEntities buildingEntities))
            {
                _cache.Buildings[buildingID] = buildingEntities = new BuildingEntities(buildingID);
            }

            return buildingEntities;
        }

        public bool IsMergeBlocked(Construction component, Construction.Target placement, BasePlayer player, PlayerData playerData, BuildingEntities buildingEntities)
        {
            if (GameManager.server.CreatePrefab(component.fullName, Vector3.zero, Quaternion.identity, false) is not { } gameObject)
            {
                Log($"Failed to create prefab '{component.fullName}' for '{player.UserIDString}'", LogLevel.Error);
                return false;
            }

            component.UpdatePlacement(gameObject.transform, component, ref placement);
            BaseEntity baseEntity = gameObject.ToBaseEntity();
            OBB oBb = baseEntity.WorldSpaceBounds();

            if (!baseEntity.IsValid())
            {
                GameManager.Destroy(gameObject);
            }
            else
            {
                baseEntity.KillAsMapEntity();
            }

            bool mergeBlocked = false;
            List<uint> processedBuildings = Pool.Get<List<uint>>();
            processedBuildings.Add(buildingEntities.BuildingID);
            List<BuildingBlock> adjoiningBlocks = Pool.Get<List<BuildingBlock>>();
            Vis.Entities(oBb.position, oBb.extents.magnitude + 1f, adjoiningBlocks);

            if (adjoiningBlocks.Count > 0)
            {
                Dictionary<uint, int> limitEntitiesCache = playerData.Perms.LimitsBuilding.LimitEntitiesCache;
                int allowedBuildingTotal = playerData.Perms.LimitsBuilding.LimitTotal - buildingEntities.EntitiesIds.Count;
                Dictionary<uint, int> allowedBuildingEntities = new();

                foreach (BuildingBlock adjoiningBlock in adjoiningBlocks)
                {
                    if (processedBuildings.Contains(adjoiningBlock.buildingID) ||
                        !_cache.Buildings.TryGetValue(adjoiningBlock.buildingID, out BuildingEntities adjoiningBuilding))
                    {
                        continue;
                    }

                    foreach (KeyValuePair<uint, int> adjoiningEntity in adjoiningBuilding.EntitiesCount)
                    {
                        allowedBuildingTotal -= adjoiningEntity.Value;
                        if (allowedBuildingTotal < 0)
                        {
                            Log($"{oBb.position} {playerData.PlayerIdString} prevented from merge building {buildingEntities.BuildingID} Limit Building Total", LogLevel.Debug);
                            HandleNotification(player, Lang(LangKeys.Error.LimitBuilding.MergeBlocked, playerData.PlayerIdString, allowedBuildingTotal * -1), true);
                            mergeBlocked = true;
                            break;
                        }

                        if (!limitEntitiesCache.TryGetValue(adjoiningEntity.Key, out int limitEntity))
                        {
                            // Entity is not limited
                            continue;
                        }

                        if (!allowedBuildingEntities.TryGetValue(adjoiningEntity.Key, out int allowedCount))
                        {
                            buildingEntities.EntitiesCount.TryGetValue(adjoiningEntity.Key, out int existingCount);
                            allowedBuildingEntities[adjoiningEntity.Key] = allowedCount = limitEntity - existingCount;
                        }

                        allowedBuildingEntities[adjoiningEntity.Key] = allowedCount -= adjoiningEntity.Value;

                        if (allowedCount < 0)
                        {
                            Log($"{oBb.position} {playerData.PlayerIdString} prevented from building merge block entity {GetShortName(adjoiningEntity.Key)} in building {buildingEntities.BuildingID}", LogLevel.Debug);
                            HandleNotification(player, Lang(LangKeys.Error.LimitBuilding.EntityMergeBlocked, playerData.PlayerIdString, allowedCount * -1, GetItemDisplayName(adjoiningEntity.Key, playerData.PlayerIdString)), true);
                            mergeBlocked = true;
                            break;
                        }
                    }

                    if (mergeBlocked)
                    {
                        break;
                    }

                    processedBuildings.Add(adjoiningBlock.buildingID);
                }
            }

            Pool.FreeUnmanaged(ref adjoiningBlocks);
            Pool.FreeUnmanaged(ref processedBuildings);

            return mergeBlocked;
        }

        public bool IsValidEntity(BaseEntity entity) => entity.IsValid() && entity.OwnerID.IsSteamId() && (entity.skinID == 0UL || !_pluginConfig.ExcludedSkinID.Contains(entity.skinID)) && _cache.Prefabs.Tracked.Contains(entity.prefabID) && !IsExcludedByZone(entity);

        public object HandleCanBuild(BasePlayer player, Construction component, Construction.Target placement)
        {
            if (!player.IsValid()
                || !player.userID.IsSteamId()
                || !_cache.Prefabs.Tracked.Contains(component.prefabID)
                || IsExcludedByZone(player))
            {
                return null;
            }

            if (GetPlayerData(player.userID) is not { Perms: not null, HasImmunity: false } playerData)
            {
                return null;
            }

            PlayerEntities entities = playerData.PlayerEntities;

            Vector3 position = placement.entity.IsValid() && placement.socket ? placement.GetWorldPosition() : placement.position;
            uint prefabID = GetPrefabID(component.prefabID);
            if (!playerData.CanBuild())
            {
                Log($"{position} {playerData.PlayerIdString} prevented from building entity {GetShortName(prefabID)} cannot build", LogLevel.Debug);
                HandleNotification(player, Lang(LangKeys.Error.EntityIsNotAllowed, playerData.PlayerIdString, GetItemDisplayName(prefabID, playerData.PlayerIdString)), true);
                return Static.False;
            }

            if (playerData.IsGlobalLimit())
            {
                Log($"{position} {playerData.PlayerIdString} prevented from building entity {GetShortName(prefabID)} global limit", LogLevel.Debug);
                HandleNotification(player, Lang(LangKeys.Error.LimitGlobal.Reached, playerData.PlayerIdString, playerData.PlayerEntities.TotalCount, playerData.Perms.LimitsGlobal.LimitTotal), true);
                return Static.False;
            }

            if (playerData.IsGlobalLimit(prefabID))
            {
                Log($"{position} {playerData.PlayerIdString} prevented from building entity {GetShortName(prefabID)} global entity limit", LogLevel.Debug);
                HandleNotification(player, Lang(LangKeys.Error.LimitGlobal.EntityReached, playerData.PlayerIdString, entities.GetEntityCount(prefabID), playerData.Perms.LimitsGlobal.GetEntityLimit(prefabID), GetItemDisplayName(prefabID, playerData.PlayerIdString)), true);
                return Static.False;
            }

            if (playerData.IsRadiusLimit(prefabID, position, _cache.RadiusPrefabPositions))
            {
                Log($"{position} {playerData.PlayerIdString} prevented from building entity {GetShortName(prefabID)} radius entity limit", LogLevel.Debug);
                HandleNotification(player, Lang(LangKeys.Error.LimitRadius.EntityReached, playerData.PlayerIdString, entities.GetEntityCount(prefabID), playerData.Perms.LimitsRadius.GetEntityLimit(prefabID), GetItemDisplayName(prefabID, playerData.PlayerIdString), Lang(LangKeys.Format.Meters, playerData.PlayerIdString, playerData.Perms.LimitsRadius.Radius)), true);
                return Static.False;
            }

            uint buildingID;
            if (_pluginConfig.TrackGrowable && placement.entity is PlanterBox planterBox)
            {
                buildingID = planterBox.buildingID;
            }
            else
            {
                buildingID = GetBuildingID(placement);
            }

            if (_cache.Buildings.TryGetValue(buildingID, out BuildingEntities building))
            {
                if (playerData.IsBuildingLimit(building))
                {
                    Log($"{position} {playerData.PlayerIdString} prevented from building entity {GetShortName(prefabID)} in building {buildingID}", LogLevel.Debug);
                    HandleNotification(player, Lang(LangKeys.Error.LimitBuilding.Reached, playerData.PlayerIdString, building.EntitiesIds.Count, playerData.Perms.LimitsBuilding.LimitTotal), true);
                    return Static.False;
                }

                if (playerData.IsBuildingLimit(building, prefabID))
                {
                    Log($"{position} {playerData.PlayerIdString} prevented from building entity {GetShortName(prefabID)} in building {buildingID}", LogLevel.Debug);
                    HandleNotification(player, Lang(LangKeys.Error.LimitBuilding.EntityReached, playerData.PlayerIdString, building.GetEntityCount(prefabID), playerData.Perms.LimitsBuilding.GetEntityLimit(prefabID), GetItemDisplayName(prefabID, playerData.PlayerIdString)), true);
                    return Static.False;
                }

                if (playerData.Perms.MergingCheck
                    && _cache.Prefabs.BuildingBlocks.Contains(component.prefabID)
                    && IsMergeBlocked(component, placement, player, playerData, building))
                {
                    return Static.False;
                }
            }

            return null;
        }

        public object HandlePoweredLightsAddPoint(StringLights stringLights, BasePlayer player, Vector3 position)
        {
            if (GetPlayerData(player.userID) is not { Perms: not null, HasImmunity: false } playerData)
            {
                return null;
            }

            if (!_cache.Prefabs.Tracked.Contains(stringLights.prefabID))
            {
                return null;
            }

            uint prefabID = GetPrefabID(stringLights.prefabID);

            if (playerData.Perms.LimitsPoweredLights.MaxPoints == 0)
            {
                Log($"{position} {playerData.PlayerIdString} prevented from building entity {GetShortName(prefabID)} points limit", LogLevel.Debug);
                HandleNotification(player, Lang(LangKeys.Error.LimitPoweredLights.PointCount, playerData.PlayerIdString, playerData.Perms.LimitsPoweredLights.MaxPoints, GetItemDisplayName(prefabID, playerData.PlayerIdString)), true);
                return Static.False;
            }

            if (playerData.Perms.LimitsPoweredLights.MaxLength == 0)
            {
                Log($"{position} {playerData.PlayerIdString} prevented from building entity {GetShortName(prefabID)} points limit", LogLevel.Debug);
                HandleNotification(player, Lang(LangKeys.Error.LimitPoweredLights.Length, playerData.PlayerIdString, playerData.Perms.LimitsPoweredLights.MaxLength, GetItemDisplayName(prefabID, playerData.PlayerIdString)), true);
                return Static.False;
            }

            if (playerData.Perms.LimitsPoweredLights.MaxLengthPoint == 0)
            {
                Log($"{position} {playerData.PlayerIdString} prevented from building entity {GetShortName(prefabID)} points limit", LogLevel.Debug);
                HandleNotification(player, Lang(LangKeys.Error.LimitPoweredLights.LengthPoint, playerData.PlayerIdString, playerData.Perms.LimitsPoweredLights.MaxLengthPoint, GetItemDisplayName(prefabID, playerData.PlayerIdString)), true);
                return Static.False;
            }

            if (stringLights.points?.Count == 0)
            {
                if (!playerData.CanBuild())
                {
                    Log($"{position} {playerData.PlayerIdString} prevented from building entity {GetShortName(prefabID)} cannot build", LogLevel.Debug);
                    HandleNotification(player, Lang(LangKeys.Error.EntityIsNotAllowed, playerData.PlayerIdString, GetItemDisplayName(prefabID, playerData.PlayerIdString)), true);
                    return Static.False;
                }

                if (playerData.IsGlobalLimit())
                {
                    Log($"{position} {playerData.PlayerIdString} prevented from building entity {GetShortName(prefabID)} global limit", LogLevel.Debug);
                    HandleNotification(player, Lang(LangKeys.Error.LimitGlobal.Reached, playerData.PlayerIdString, playerData.PlayerEntities.TotalCount, playerData.Perms.LimitsGlobal.LimitTotal), true);
                    return Static.False;
                }

                if (playerData.IsGlobalLimit(prefabID))
                {
                    Log($"{position} {playerData.PlayerIdString} prevented from building entity {GetShortName(prefabID)} global entity limit", LogLevel.Debug);
                    HandleNotification(player, Lang(LangKeys.Error.LimitGlobal.EntityReached, playerData.PlayerIdString, playerData.PlayerEntities.GetEntityCount(prefabID), playerData.Perms.LimitsGlobal.GetEntityLimit(prefabID), GetItemDisplayName(prefabID, playerData.PlayerIdString)), true);
                    return Static.False;
                }

                uint buildingID = GetBuildingID(position, Math.Max(2.2801f, Static.BuildingDetectionRangeSqr));

                if (buildingID > 0 && _cache.Buildings.TryGetValue(buildingID, out BuildingEntities building))
                {
                    if (playerData.IsBuildingLimit(building))
                    {
                        Log($"{position} {playerData.PlayerIdString} prevented from building entity {GetShortName(prefabID)} in building {buildingID}", LogLevel.Debug);
                        HandleNotification(player, Lang(LangKeys.Error.LimitBuilding.Reached, playerData.PlayerIdString, building.EntitiesIds.Count, playerData.Perms.LimitsBuilding.LimitTotal), true);
                        return Static.False;
                    }

                    if (playerData.IsBuildingLimit(building, prefabID))
                    {
                        Log($"{position} {playerData.PlayerIdString} prevented from building entity {GetShortName(prefabID)} in building {buildingID}", LogLevel.Debug);
                        HandleNotification(player, Lang(LangKeys.Error.LimitBuilding.EntityReached, playerData.PlayerIdString, building.GetEntityCount(prefabID), playerData.Perms.LimitsBuilding.GetEntityLimit(prefabID), GetItemDisplayName(prefabID, playerData.PlayerIdString)), true);
                        return Static.False;
                    }
                }

                _cache.PoweredLightsDeployers[position] = player.userID;
                return null;
            }

            if (playerData.Perms.LimitsPoweredLights.MaxPoints > 0 && stringLights.points.Count + 1 > playerData.Perms.LimitsPoweredLights.MaxPoints)
            {
                HandleNotification(player, Lang(LangKeys.Error.LimitPoweredLights.PointCount, playerData.PlayerIdString, playerData.Perms.LimitsPoweredLights.MaxPoints, GetItemDisplayName(prefabID, playerData.PlayerIdString)), true);
                return Static.False;
            }

            Vector3 positionLastPoint = stringLights.points[^1].point;
            float lengthBetweenPoints = Vector3.SqrMagnitude(positionLastPoint - position);
            if (playerData.Perms.LimitsPoweredLights.MaxLengthPoint > 0 && lengthBetweenPoints > playerData.Perms.LimitsPoweredLights.MaxLengthPoint * playerData.Perms.LimitsPoweredLights.MaxLengthPoint)
            {
                HandleNotification(player, Lang(LangKeys.Error.LimitPoweredLights.LengthPoint, playerData.PlayerIdString, playerData.Perms.LimitsPoweredLights.MaxLengthPoint, GetItemDisplayName(prefabID, playerData.PlayerIdString)), true);
                return Static.False;
            }

            float totalLength = lengthBetweenPoints;
            for (int i = 1; i < stringLights.points.Count; i++)
            {
                totalLength += Vector3.SqrMagnitude(stringLights.points[i - 1].point - stringLights.points[i].point);
            }
            if (playerData.Perms.LimitsPoweredLights.MaxLength > 0 && totalLength > playerData.Perms.LimitsPoweredLights.MaxLength * playerData.Perms.LimitsPoweredLights.MaxLength)
            {
                HandleNotification(player, Lang(LangKeys.Error.LimitPoweredLights.Length, playerData.PlayerIdString, playerData.Perms.LimitsPoweredLights.MaxLength, GetItemDisplayName(prefabID, playerData.PlayerIdString)), true);
                return Static.False;
            }

            return null;
        }

        public void HandleBuildingChange(uint oldId, uint newId, bool split)
        {
            if (!_storedData.BuildingsOwners.TryGetValue(oldId, out ulong ownerId) || !ownerId.IsSteamId())
            {
                return;
            }

            if (!BuildingManager.server.buildingDictionary.ContainsKey(newId))
            {
                return;
            }

            _storedData.BuildingsOwners[newId] = ownerId;

            if (_pluginConfig.LoggingLevel >= LogLevel.Debug
                && BuildingManager.server.buildingDictionary.TryGetValue(newId, out Building building)
                && building?.buildingBlocks?.Count > 0)
            {
                string mode = split ? "split" : "merge";
                Log($"{building.buildingBlocks[0].ServerPosition} Building {mode}. Saved the owner {ownerId} of old building {oldId} to new building {newId}", LogLevel.Debug);
            }

            if (!_cache.Buildings.TryGetValue(oldId, out BuildingEntities entitiesOld))
            {
                return;
            }

            List<BaseEntity> movedEntites = Pool.Get<List<BaseEntity>>();
            foreach (ulong id in entitiesOld.EntitiesIds)
            {
                if (BaseNetworkable.serverEntities.Find(new NetworkableId(id)) is not BaseEntity entity || !entity.IsValid())
                {
                    continue;
                }

                uint currentBuildingId = GetBuildingID(entity);
                if (currentBuildingId == oldId || currentBuildingId == 0)
                {
                    continue;
                }

                movedEntites.Add(entity);
            }

            foreach (BaseEntity entity in movedEntites)
            {
                if (!entity.IsValid())
                {
                    continue;
                }

                entitiesOld.RemoveEntity(entity);
                if (_pluginConfig.LoggingLevel >= LogLevel.Debug && entity.transform != null)
                {
                    Vector3 position = entity.transform.position;
                    uint prefabID = GetPrefabID(entity.prefabID);
                    Log($"{position} {ownerId} Reduced building {oldId} entities {GetShortName(prefabID)} count {entitiesOld.GetEntityCount(prefabID)}", LogLevel.Debug);
                    Log($"{position} {ownerId} Reduced building {oldId} total entities count {entitiesOld.EntitiesIds.Count}", LogLevel.Debug);
                }

                AddBuildingEntity(entity);
            }

            Pool.FreeUnmanaged(ref movedEntites);
        }

        public uint AddBuildingEntity(BaseEntity entity)
        {
            uint buildingId = GetBuildingID(entity);
            if (buildingId == 0)
            {
                Log($"Failed to get building for {entity} {entity.GetType().Name} {entity.transform.position}", LogLevel.Debug);
                return 0;
            }

            BuildingEntities buildingEntities = GetBuildingData(buildingId);
            buildingEntities.AddEntity(entity);

            if (_pluginConfig.LoggingLevel >= LogLevel.Debug && entity.transform != null)
            {
                Vector3 position = entity.transform.position;
                uint prefabID = GetPrefabID(entity.prefabID);
                Log($"{position} {entity.OwnerID} Increased building {buildingId} entity {GetShortName(prefabID)} count {buildingEntities.GetEntityCount(prefabID)}", LogLevel.Debug);
                Log($"{position} {entity.OwnerID} Increased building {buildingId} total entities count {buildingEntities.EntitiesIds.Count}", LogLevel.Debug);
            }

            return buildingId;
        }

        public uint RemoveBuildingEntity(BaseEntity entity)
        {
            uint buildingId = GetBuildingID(entity);
            if (buildingId == 0
                || !_cache.Buildings.TryGetValue(buildingId, out BuildingEntities buildingEntities))
            {
                return buildingId;
            }

            buildingEntities.RemoveEntity(entity);

            if (_pluginConfig.LoggingLevel >= LogLevel.Debug && entity.transform != null)
            {
                Vector3 position = entity.transform.position;
                uint prefabID = GetPrefabID(entity.prefabID);
                Log($"{position} {entity.OwnerID} Reduced building {buildingId} entities {GetShortName(prefabID)} count {buildingEntities.GetEntityCount(prefabID)}", LogLevel.Debug);
                Log($"{position} {entity.OwnerID} Reduced building {buildingId} total entities count {buildingEntities.EntitiesIds.Count}", LogLevel.Debug);
            }

            return buildingId;
        }

        public uint GetBuildingID(Construction.Target target)
        {
            if (target.entity is { } baseEntity && baseEntity.IsValid())
            {
                if (baseEntity is DecayEntity decayEntity)
                {
                    return decayEntity.buildingID;
                }

                return GetBuildingID(target.socket ? target.GetWorldPosition() : target.position, Static.BuildingDetectionRangeSqr);
            }

            return GetBuildingID(target.position, Static.BuildingDetectionRangeSqr);
        }

        public uint GetBuildingID(BaseEntity entity)
        {
            if (entity is DecayEntity decayEntity)
            {
                return decayEntity.buildingID;
            }

            if (entity.GetParentEntity() is DecayEntity parentEntity)
            {
                return parentEntity.buildingID;
            }

            return GetBuildingID(entity.transform.position, Static.BuildingDetectionRangeSqr);
        }

        public uint GetBuildingID(Vector3 position, float radius = 0)
        {
            if (radius <= 0f)
            {
                return 0U;
            }

            List<Collider> entities = Pool.Get<List<Collider>>();
            GamePhysics.OverlapSphere(position, radius, entities, Rust.Layers.Construction);

            if (entities.Count == 0)
            {
                Pool.FreeUnmanaged(ref entities);
                return 0U;
            }

            BuildingBlock nearestBlock = null;
            float nearestDistanceSqr = float.MaxValue;

            for (int i = 0; i < entities.Count; i++)
            {
                if (entities[i].ToBaseEntity() is not BuildingBlock currentBlock)
                {
                    continue;
                }

                float currentDistanceSqr = Vector3.SqrMagnitude(position - currentBlock.transform.position);
                if (nearestBlock == null || currentDistanceSqr < nearestDistanceSqr)
                {
                    nearestBlock = currentBlock;
                    nearestDistanceSqr = currentDistanceSqr;
                }
            }

            Pool.FreeUnmanaged(ref entities);
            return nearestBlock == null ? 0U : nearestBlock.buildingID;
        }

        public void HandleEntityNotification(BasePlayer player, PlayerData playerData, uint buildingID, uint prefabID)
        {
            if (!player.IsValid() || player.IsDead() || !player.IsConnected || _pluginConfig.WarnPercent <= 0)
            {
                return;
            }

            if (_cache.Buildings.TryGetValue(buildingID, out BuildingEntities building) && playerData.GetBuildingPercentage(building, prefabID) >= _pluginConfig.WarnPercent)
            {
                HandleNotification(player, Lang(LangKeys.Info.LimitBuildingEntity, playerData.PlayerIdString, building.GetEntityCount(prefabID), playerData.Perms.LimitsBuilding.GetEntityLimit(prefabID), GetItemDisplayName(prefabID, playerData.PlayerIdString)));
                return;
            }

            if (building != null && playerData.GetBuildingPercentage(building) >= _pluginConfig.WarnPercent)
            {
                HandleNotification(player, Lang(LangKeys.Info.LimitBuilding, playerData.PlayerIdString, building.EntitiesIds.Count, playerData.Perms.LimitsBuilding.LimitTotal));
                return;
            }

            if (playerData.GetGlobalPercentage(prefabID) >= _pluginConfig.WarnPercent)
            {
                HandleNotification(player, Lang(LangKeys.Info.LimitGlobalEntity, playerData.PlayerIdString, playerData.PlayerEntities.GetEntityCount(prefabID), playerData.Perms.LimitsGlobal.GetEntityLimit(prefabID), GetItemDisplayName(prefabID, playerData.PlayerIdString)));
                return;
            }

            if (playerData.GetGlobalPercentage() >= _pluginConfig.WarnPercent)
            {
                HandleNotification(player, Lang(LangKeys.Info.LimitGlobal, playerData.PlayerIdString, playerData.PlayerEntities.TotalCount, playerData.Perms.LimitsGlobal.LimitTotal));
            }
        }

        public string GetPlayerLimitString(BasePlayer player, BasePlayer target)
        {
            if (!_cache.PlayersData.TryGetValue(target.userID, out PlayerData playerData) || playerData.Perms == null || playerData.HasImmunity)
            {
                return Lang(LangKeys.Info.Unlimited, player.UserIDString);
            }

            PlayerEntities entities = playerData.PlayerEntities;
            PermissionEntry perms = playerData.Perms;
            StringBuilder sb = new();

            sb.AppendLine(Lang(LangKeys.Info.TotalAmount, player.UserIDString, $"{entities.TotalCount} / {perms.LimitsGlobal.LimitTotal}"));
            foreach (KeyValuePair<uint, int> limitEntry in perms.LimitsGlobal.LimitEntitiesCache)
            {
                uint prefabID = GetPrefabID(limitEntry.Key);
                sb.AppendLine();
                sb.Append(GetItemDisplayName(prefabID, player.UserIDString));
                sb.Append("  ");
                sb.Append(entities.GetEntityCount(prefabID));
                sb.Append(" / ");
                sb.Append(limitEntry.Value);
            }
            sb.AppendLine();
            string globalLimits = sb.ToString();

            sb.Clear();
            sb.AppendLine(Lang(LangKeys.Info.TotalAmount, player.UserIDString, perms.LimitsBuilding.LimitTotal));
            foreach (KeyValuePair<uint, int> limitEntry in perms.LimitsBuilding.LimitEntitiesCache)
            {
                sb.AppendLine();
                sb.Append(GetItemDisplayName(GetPrefabID(limitEntry.Key), player.UserIDString));
                sb.Append("  ");
                sb.Append(limitEntry.Value);
            }
            string buildingLimits = sb.ToString();

            string radiusLimits = string.Empty;
            if (perms.LimitsRadius.Radius > 0f && perms.LimitsRadius.LimitEntitiesCache.Count > 0)
            {
                sb.Clear();
                sb.Append(Lang(LangKeys.Format.Meters, player.UserIDString, perms.LimitsRadius.Radius));
                sb.AppendLine(":");
                foreach (KeyValuePair<uint, int> limitEntry in perms.LimitsRadius.LimitEntitiesCache)
                {
                    sb.AppendLine();
                    sb.Append(GetItemDisplayName(GetPrefabID(limitEntry.Key), player.UserIDString));
                    sb.Append("  ");
                    sb.Append(limitEntry.Value);
                }
                radiusLimits = sb.ToString();
            }

            return Lang(LangKeys.Info.Limits, player.UserIDString, globalLimits, buildingLimits, radiusLimits);
        }

        // IncludeMode = true  -> limits apply only IN zones    -> exclude when NOT in zone
        // IncludeMode = false -> limits apply only OUTSIDE   -> exclude when IN zone
        public bool IsExcludedByZone(BasePlayer player) => _pluginConfig.ZoneManagerIncludeMode ? !IsInZoneManagerZone(player) : IsInZoneManagerZone(player);
        public bool IsExcludedByZone(BaseEntity entity) => _pluginConfig.ZoneManagerIncludeMode ? !IsInZoneManagerZone(entity) : IsInZoneManagerZone(entity);

        public bool IsInZoneManagerZone(object target)
        {
            if (!_pluginConfig.UseZoneManager || _cache.ZoneManagerZoneIDs.Count == 0)
            {
                return false;
            }

            if (!IsPluginLoaded(ZoneManager))
            {
                Log("UseZoneManager is set to true, but ZoneManager plugin is not loaded!", LogLevel.Error);
                return false;
            }

            List<string> targetZones = Pool.Get<List<string>>();

            try
            {
                switch (target)
                {
                    case BasePlayer player:
                        ZoneManager.Call("GetPlayerZoneIDsNoAlloc", player, targetZones);
                        break;
                    case BaseEntity entity:
                        ZoneManager.Call("GetEntityZoneIDsNoAlloc", entity, targetZones);
                        break;
                    default:
                        Log($"Unknown target type {target.GetType().Name}", LogLevel.Error);
                        return false;
                }

                for (int i = 0; i < targetZones.Count; i++)
                {
                    if (_cache.ZoneManagerZoneIDs.Contains(targetZones[i]))
                    {
                        return true;
                    }
                }

                return false;
            }
            finally
            {
                Pool.FreeUnmanaged(ref targetZones);
            }
        }

        #endregion Core Methods

        #region API Methods

        private ulong API_GetBuildingOwner(uint buildingID)
        {
            if (!_storedData.BuildingsOwners.TryGetValue(buildingID, out ulong ownerID))
            {
                return 0UL;
            }

            return ownerID;
        }

        private ulong API_GetBuildingOwner(BaseEntity entity)
        {
            if (!_storedData.BuildingsOwners.TryGetValue(GetBuildingID(entity), out ulong ownerID))
            {
                return 0UL;
            }

            return ownerID;
        }

        private ulong API_GetBuildingOwner(Vector3 position)
        {
            if (!_storedData.BuildingsOwners.TryGetValue(GetBuildingID(position, Static.BuildingDetectionRangeSqr), out ulong ownerID))
            {
                return 0UL;
            }

            return ownerID;
        }

        private void API_RemoveBuildingOwner(List<BaseEntity> entitiesList)
        {
            foreach (BaseEntity entity in entitiesList)
            {
                if (!entity.OwnerID.IsSteamId())
                {
                    continue;
                }

                entity.OwnerID = 0;
                Log($"Owner of {entity} was set to 0", LogLevel.Debug);

                PlayerData playerData = GetPlayerData(entity.OwnerID);
                if (playerData.Perms == null || playerData.HasImmunity)
                {
                    continue;
                }

                playerData.RemoveEntity(GetPrefabID(entity.prefabID));

                uint buildingID = GetBuildingID(entity);
                if (buildingID > 0)
                {
                    _cache.Buildings.Remove(buildingID);
                    _storedData.BuildingsOwners.Remove(buildingID);
                    StoredDataSave();
                }
            }
        }

        private HashSet<ulong> API_ChangeBuildingOwner(List<BaseEntity> entitiesList, ulong newOwner, bool applyChanges = true)
        {
            if (!newOwner.IsSteamId())
            {
                return null;
            }

            HashSet<ulong> limitedEntitiesIds = new();

            if (GetPlayerData(newOwner) is not { Perms: not null, HasImmunity: false } playerData)
            {
                return limitedEntitiesIds;
            }

            Log($"{newOwner} is claiming {entitiesList.Count} entities", LogLevel.Debug);
            Dictionary<ulong, Dictionary<uint, int>> oldOwnerEntities = new();
            Dictionary<ulong, Dictionary<uint, int>> sameOwnerEntities = new();

            int newTotalCount = playerData.PlayerEntities.TotalCount;
            Dictionary<uint, int> newEntities = new(playerData.PlayerEntities.Entities);
            Dictionary<uint, BuildingEntities> newBuildingEntities = new();

            Dictionary<uint, int> limitEntities = playerData.Perms.LimitsGlobal.LimitEntitiesCache;
            Dictionary<uint, int> limitBuildingEntities = playerData.Perms.LimitsBuilding.LimitEntitiesCache;
            Dictionary<uint, int> newBuildingTotal = new();

            foreach (BaseEntity entity in entitiesList)
            {
                if (!entity.IsValid())
                {
                    Log($"{newOwner} prevented from ChangeBuildingOwner invalid entity {entity}", LogLevel.Error);
                    return null;
                }

                uint buildingID = GetBuildingID(entity);
                uint prefabID = GetPrefabID(entity.prefabID);
                ulong networkableId = entity.net.ID.Value;
                Vector3 position = entity.transform.position;
                bool sameOwner = entity.OwnerID == newOwner;
                bool hasSteamOwner = entity.OwnerID.IsSteamId();

                // Track same owner entities separately but don't skip limit checks
                if (sameOwner)
                {
                    Log($"{entity} already owned by {newOwner}", LogLevel.Debug);
                    if (!sameOwnerEntities.TryGetValue(buildingID, out Dictionary<uint, int> sameOwnerBuildingEntities))
                    {
                        sameOwnerEntities[buildingID] = sameOwnerBuildingEntities = new Dictionary<uint, int>();
                    }

                    sameOwnerBuildingEntities.TryGetValue(prefabID, out int countSameOwnerEntity);
                    sameOwnerBuildingEntities[prefabID] = ++countSameOwnerEntity;
                }

                // Check global total limit only for personal counts
                bool exceedsLimit = false;
                if (!sameOwner)
                {
                    if (++newTotalCount > playerData.Perms.LimitsGlobal.LimitTotal)
                    {
                        if (entity is BuildingBlock)
                        {
                            Log($"{newOwner} prevented from ChangeBuildingOwner {buildingID} Limit Global Total ({newTotalCount} / {playerData.Perms.LimitsGlobal.LimitTotal})", LogLevel.Debug);
                            return null;
                        }

                        newTotalCount--;
                        limitedEntitiesIds.Add(networkableId);
                        exceedsLimit = true;
                    }
                    else if (limitEntities.TryGetValue(prefabID, out int limitEntity))
                    {
                        if (!newEntities.TryGetValue(prefabID, out int newEntityCount))
                        {
                            newEntities[prefabID] = newEntityCount;
                        }

                        if (++newEntityCount > limitEntity)
                        {
                            if (entity is BuildingBlock)
                            {
                                Log($"{newOwner} prevented from ChangeBuildingOwner {buildingID} Limit Global entity {entity} ({newEntityCount} / {limitEntity})", LogLevel.Debug);
                                return null;
                            }

                            newEntities[prefabID] = --newEntityCount;
                            limitedEntitiesIds.Add(networkableId);
                            exceedsLimit = true;
                        }
                        // else - value already updated by ++newEntityCount above
                    }

                    // Check radius limit
                    if (!exceedsLimit && playerData.IsRadiusLimit(prefabID, position, _cache.RadiusPrefabPositions))
                    {
                        if (entity is BuildingBlock)
                        {
                            Log($"{newOwner} prevented from ChangeBuildingOwner radius limit for {entity} at {position}", LogLevel.Debug);
                            return null;
                        }

                        limitedEntitiesIds.Add(networkableId);
                        exceedsLimit = true;
                    }

                    // Skip adding to BuildingEntities if entity doesn't have Steam ID owner
                    if (exceedsLimit && !hasSteamOwner)
                    {
                        continue;
                    }
                }

                // Add to building entities for tracking and limit checks
                if (!newBuildingEntities.TryGetValue(buildingID, out BuildingEntities buildingEntities))
                {
                    newBuildingEntities[buildingID] = buildingEntities = new BuildingEntities(buildingID);
                }

                buildingEntities.EntitiesIds.Add(networkableId);
                buildingEntities.EntitiesCount.TryGetValue(prefabID, out int countBuildingEntity);
                buildingEntities.EntitiesCount[prefabID] = ++countBuildingEntity;

                // Building limits apply regardless of owner
                if (buildingID > 0 && !exceedsLimit)
                {
                    newBuildingTotal.TryGetValue(buildingID, out int buildingTotal);
                    newBuildingTotal[buildingID] = ++buildingTotal;

                    if (buildingTotal > playerData.Perms.LimitsBuilding.LimitTotal)
                    {
                        if (entity is BuildingBlock)
                        {
                            Log($"{newOwner} prevented from ChangeBuildingOwner {buildingID} Limit Building Total ({buildingTotal} / {playerData.Perms.LimitsBuilding.LimitTotal})", LogLevel.Debug);
                            return null;
                        }

                        newBuildingTotal[buildingID]--;
                        limitedEntitiesIds.Add(networkableId);
                        exceedsLimit = true;

                        // Remove from building entities count and skip if no Steam ID
                        if (!hasSteamOwner)
                        {
                            buildingEntities.EntitiesCount[prefabID]--;
                            buildingEntities.EntitiesIds.Remove(networkableId);
                            continue;
                        }
                    }
                    else if (limitBuildingEntities.TryGetValue(prefabID, out int limitBuildingEntity) && countBuildingEntity > limitBuildingEntity)
                    {
                        if (entity is BuildingBlock)
                        {
                            Log($"{newOwner} prevented from ChangeBuildingOwner {buildingID} Limit Building entity {entity} ({countBuildingEntity} / {limitBuildingEntity})", LogLevel.Debug);
                            return null;
                        }

                        limitedEntitiesIds.Add(networkableId);
                        exceedsLimit = true;

                        // Remove from building entities count and skip if no Steam ID
                        if (!hasSteamOwner)
                        {
                            buildingEntities.EntitiesCount[prefabID]--;
                            buildingEntities.EntitiesIds.Remove(networkableId);
                            continue;
                        }
                    }
                }

                // Track old owner entities for proper count decrease
                // Only for entities that don't exceed limits and aren't the same owner
                if (!sameOwner && !exceedsLimit && hasSteamOwner)
                {
                    if (!oldOwnerEntities.TryGetValue(entity.OwnerID, out Dictionary<uint, int> ownerEntities))
                    {
                        oldOwnerEntities[entity.OwnerID] = ownerEntities = new Dictionary<uint, int>();
                    }

                    ownerEntities.TryGetValue(prefabID, out int count);
                    ownerEntities[prefabID] = ++count;
                }
            }

            if (!applyChanges)
            {
                return limitedEntitiesIds;
            }

            // Update old owner counts
            foreach (KeyValuePair<ulong, Dictionary<uint, int>> ownedEntities in oldOwnerEntities)
            {
                PlayerData ownerData = GetPlayerData(ownedEntities.Key);
                foreach (KeyValuePair<uint, int> entity in ownedEntities.Value)
                {
                    ownerData.PlayerEntities.RemoveRange(entity.Key, entity.Value);
                    Log($"Decreased {ownedEntities.Key} entity {GetShortName(entity.Key)} count for {entity.Value}", LogLevel.Debug);
                }
            }

            // Update new owner and building cache
            foreach (KeyValuePair<uint, BuildingEntities> building in newBuildingEntities)
            {
                if (building.Key > 0)
                {
                    _cache.Buildings[building.Key] = building.Value;
                    Log($"Added building {building.Key} with {building.Value.EntitiesIds.Count} entities to cache", LogLevel.Debug);

                    _storedData.BuildingsOwners[building.Key] = newOwner;
                    StoredDataSave();
                    Log($"Owner changed to {newOwner} for building {building.Key}", LogLevel.Debug);
                }

                foreach (KeyValuePair<uint, int> entity in building.Value.EntitiesCount)
                {
                    int value = entity.Value;
                    if (sameOwnerEntities.TryGetValue(building.Key, out Dictionary<uint, int> sameOwnerBuildingEntities) &&
                        sameOwnerBuildingEntities.TryGetValue(entity.Key, out int sameOwnerCount))
                    {
                        value -= sameOwnerCount;
                    }

                    if (value < 1)
                    {
                        continue;
                    }

                    playerData.PlayerEntities.AddRange(entity.Key, value);
                    Log($"Increased {newOwner} entity {GetShortName(entity.Key)} count for {value}", LogLevel.Debug);
                }
            }

            // No need to update radius positions cache - entities don't move, only owner changes
            // Positions were already added to cache when entities were originally spawned

            return limitedEntitiesIds;
        }

        #endregion API Methods

        #region Helpers

        public void HooksUnsubscribe()
        {
            Unsubscribe(nameof(CanBuild));
            Unsubscribe(nameof(OnBuildingMerge));
            Unsubscribe(nameof(OnBuildingSplit));
            Unsubscribe(nameof(OnEntityKill));
            Unsubscribe(nameof(OnEntitySpawned));
            Unsubscribe(nameof(OnGroupCreated));
            Unsubscribe(nameof(OnGroupDeleted));
            Unsubscribe(nameof(OnGroupPermissionGranted));
            Unsubscribe(nameof(OnGroupPermissionRevoked));
            Unsubscribe(nameof(OnPlayerConnected));
            Unsubscribe(nameof(OnPoweredLightsPointAdd));
            Unsubscribe(nameof(OnUserGroupAdded));
            Unsubscribe(nameof(OnUserGroupRemoved));
            Unsubscribe(nameof(OnUserPermissionGranted));
            Unsubscribe(nameof(OnUserPermissionRevoked));
        }

        public void HooksSubscribe()
        {
            Subscribe(nameof(CanBuild));
            Subscribe(nameof(OnBuildingMerge));
            Subscribe(nameof(OnBuildingSplit));
            Subscribe(nameof(OnEntityKill));
            Subscribe(nameof(OnEntitySpawned));
            Subscribe(nameof(OnGroupCreated));
            Subscribe(nameof(OnGroupDeleted));
            Subscribe(nameof(OnGroupPermissionGranted));
            Subscribe(nameof(OnGroupPermissionRevoked));
            Subscribe(nameof(OnPlayerConnected));
            Subscribe(nameof(OnUserGroupAdded));
            Subscribe(nameof(OnUserGroupRemoved));
            Subscribe(nameof(OnUserPermissionGranted));
            Subscribe(nameof(OnUserPermissionRevoked));

            if (_pluginConfig.TrackPowerLights)
            {
                Subscribe(nameof(OnPoweredLightsPointAdd));
            }
        }

        public void RegisterPermissions()
        {
            permission.RegisterPermission(Static.PermissionAdmin, this);
            permission.RegisterPermission(Static.PermissionImmunity, this);

            List<PermissionEntry> entries = Pool.Get<List<PermissionEntry>>();
            List<string> perms = Pool.Get<List<string>>();
            perms.Add(Static.PermissionImmunity);

            foreach (PermissionEntry entry in _pluginConfig.Permissions)
            {
                if (string.IsNullOrWhiteSpace(entry.Permission))
                {
                    Log("You have empty 'Permission' in config file! Skipped", LogLevel.Error);
                    continue;
                }

                string permPrefix = Name.ToLower();
                if (!entry.Permission.StartsWith(permPrefix))
                {
                    entry.Permission = $"{permPrefix}.{entry.Permission.ToLower()}";
                }

                if (!permission.PermissionExists(entry.Permission))
                {
                    permission.RegisterPermission(entry.Permission, this);
                }

                if (!perms.Contains(entry.Permission))
                {
                    perms.Add(entry.Permission);
                }

                entries.Add(entry);
            }

            entries.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            _cache.PermissionData.Descending = entries.ToArray();

            perms.Sort();
            _cache.PermissionData.Registered = perms.ToArray();

            Pool.FreeUnmanaged(ref entries);
            Pool.FreeUnmanaged(ref perms);
        }

        public void AddCommands()
        {
            foreach (string command in _pluginConfig.Commands)
            {
                cmd.AddChatCommand(command, this, nameof(CmdLimitEntities));
            }
        }

        public static bool IsPluginLoaded(Plugin plugin) => plugin is { IsLoaded: true };

        public bool IsPlayerAdmin(BasePlayer player) => player.IsAdmin || permission.UserHasPermission(player.UserIDString, Static.PermissionAdmin);

        public void HandleNotification(BasePlayer player, string message, bool isWarning = false)
        {
            Log($"{player.displayName} {Static.StripRustTags(message)}", isWarning ? LogLevel.Warning : LogLevel.Info);

            if (_pluginConfig.ChatNotificationsEnabled)
            {
                PlayerSendMessage(player, message + "\n\n" + Lang(LangKeys.Info.Help, player.UserIDString, _pluginConfig.Commands[0]));
            }

            if (_pluginConfig.GameTipNotificationsEnabled)
            {
                PlayerSendGameTip(player, message, isWarning);
            }
        }

        public void Log(string message, LogLevel level = LogLevel.Info, string filename = "log", [CallerMemberName] string methodName = null)
        {
            switch (level)
            {
                case LogLevel.Error:
                    PrintError(message);
                    message = $"{DateTime.Now:HH:mm:ss} {methodName} {message}";
                    break;
                case LogLevel.Warning:
                    PrintWarning(message);
                    message = $"{DateTime.Now:HH:mm:ss} {methodName} {message}";
                    break;
                case LogLevel.Debug:
                    message = $"{DateTime.Now:HH:mm:ss} {methodName} {message}";
                    break;
                default:
                    message = $"{DateTime.Now:HH:mm:ss} {message}";
                    break;
            }

            if (_pluginConfig.LoggingLevel >= level)
            {
                LogToFile(filename, message, this);
            }
        }

        public void PlayerSendMessage(BasePlayer player, string message) => player.SendConsoleCommand("chat.add", 2, _pluginConfig.SteamIDIcon, $"{Lang(LangKeys.Format.Prefix, player.UserIDString)}{message}");

        public void PlayerSendGameTip(BasePlayer player, string message, bool isWarning = false) => player.SendConsoleCommand("showtoast", isWarning ? (int)GameTip.Styles.Error : (int)GameTip.Styles.Blue_Long, message, false);

        public string GetShortName(uint prefabId)
        {
            if (!_cache.Prefabs.ShortNames.TryGetValue(prefabId, out string shortName))
            {
                if (!StringPool.toString.TryGetValue(prefabId, out shortName))
                {
                    Log($"The string for '{prefabId}' was not found in StringPool", LogLevel.Warning);
                    return string.Empty;
                }

                _cache.Prefabs.ShortNames[prefabId] = shortName = Path.GetFileNameWithoutExtension(shortName);
            }

            return shortName;
        }

        public string GetItemDisplayName(uint prefabID, string userIDString)
        {
            string language = lang.GetLanguage(userIDString);

            if (!_cache.DisplayNames.TryGetValue(language, out Dictionary<uint, string> displayNames))
            {
                _cache.DisplayNames[language] = displayNames = new Dictionary<uint, string>();
            }

            prefabID = GetPrefabID(prefabID);

            if (displayNames.TryGetValue(prefabID, out string itemDisplayName) && !string.IsNullOrWhiteSpace(itemDisplayName))
            {
                return itemDisplayName;
            }

            string itemShortName = GetShortName(prefabID);

            if (_cache.Prefabs.Groups.ContainsValue(prefabID))
            {
                return displayNames[prefabID] = Lang(itemShortName, userIDString);
            }

            if (string.IsNullOrWhiteSpace(itemShortName) || !IsPluginLoaded(RustTranslationAPI))
            {
                return itemShortName;
            }

            itemDisplayName = RustTranslationAPI.Call<string>("GetPrefabTranslation", language, prefabID);
            if (!string.IsNullOrWhiteSpace(itemDisplayName))
            {
                return displayNames[prefabID] = itemDisplayName;
            }

            Log($"There is no translation for {itemShortName} ({prefabID}) found!", LogLevel.Warning);
            return displayNames[prefabID] = itemShortName;
        }

        #endregion Helpers
    }
} 