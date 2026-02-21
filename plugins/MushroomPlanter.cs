using Oxide.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("MushroomPlanter", "NINJA WORKS", "1.0.4")]
    [Description("Grows mushroom clusters in empty spaces of planter boxes")]
    public class MushroomPlanter : RustPlugin
    {
        private Configuration config;

        private const string PermissionNoGrow = "mushroomplanter.nogrow";
        private const string FertilizerShortname = "fertilizer";

        private class Configuration
        {
            public float MushroomGrowthChance { get; set; } = 0.8f;
            public float MushroomGrowthInterval { get; set; } = 1800f;
            public float MushroomWaterRequired { get; set; } = 100f;
            public float MinimumWaterForGrowth { get; set; } = 0f;
            public bool RequireFertilizer { get; set; } = false;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null)
                {
                    LoadDefaultConfig();
                }
                SaveConfig();
            }
            catch
            {
                LoadDefaultConfig();
                SaveConfig();
                PrintWarning("Created new config file.");
            }
        }

        protected override void LoadDefaultConfig()
        {
            config = new Configuration();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config);
        }

        private static readonly string[] MushroomClusterPrefabs = new string[]
        {
            "assets/bundled/prefabs/autospawn/collectable/mushrooms/mushroom-cluster-5.prefab",
            "assets/bundled/prefabs/autospawn/collectable/mushrooms/mushroom-cluster-6.prefab"
        };

        private const string LargePlanterPrefab = "assets/prefabs/deployable/planters/planter.large.deployed.prefab";
        private const string SmallPlanterPrefab = "assets/prefabs/deployable/planters/planter.small.deployed.prefab";
        private const string BathtubPlanterPrefab = "assets/prefabs/misc/decor_dlc/bath tub planter/bathtub.planter.deployed.prefab";
        private const string MinecartPlanterPrefab = "assets/prefabs/misc/decor_dlc/minecart planter/minecart.planter.deployed.prefab";
        private const string RailRoadPlanterPrefab = "assets/prefabs/misc/decor_dlc/rail road planter/railroadplanter.deployed.prefab";
        private const string SinglePlantPotPrefab = "assets/prefabs/deployable/plant pots/plantpot.single.deployed.prefab";
        private const string TrianglePlanterPrefab = "assets/prefabs/deployable/planters/planter.triangle.deployed.prefab";
        private const string RailRoadTrianglePlanterPrefab = "assets/prefabs/misc/decor_dlc/rail road planter/triangle_railroad_planter.deployed.prefab";

        private Dictionary<NetworkableId, float> planterTime = new Dictionary<NetworkableId, float>();
        private Dictionary<string, List<SerializableMushroomData>> savedMushrooms = new Dictionary<string, List<SerializableMushroomData>>();

        [Serializable]
        private class SerializableMushroomData
        {
            public string PrefabName;
            public Vector3 LocalPosition;
            public Vector3 LocalRotationEuler;

            public SerializableMushroomData() { }

            public SerializableMushroomData(BaseEntity mushroom)
            {
                PrefabName = mushroom.PrefabName;
                LocalPosition = mushroom.transform.localPosition;
                LocalRotationEuler = mushroom.transform.localRotation.eulerAngles;
            }

            public BaseEntity CreateMushroom(PlanterBox planter)
            {
                BaseEntity mushroom = GameManager.server.CreateEntity(
                    PrefabName, 
                    planter.transform.TransformPoint(LocalPosition), 
                    planter.transform.rotation * Quaternion.Euler(LocalRotationEuler)
                );
                
                if (mushroom != null)
                {
                    mushroom.Spawn();
                    mushroom.SetParent(planter, worldPositionStays: true);
                }
                return mushroom;
            }
        }

        void OnServerInitialized()
        {
            permission.RegisterPermission(PermissionNoGrow, this);

            try
            {
                timer.Every(config.MushroomGrowthInterval, CheckPlantersForMushrooms);

                timer.Once(5f, () => 
                {
                    CleanupInvalidPlanterData();
                    RemoveAllMushrooms();
                    LoadMushrooms();
                    Puts("Mushroom grower initialized - mushrooms restored.");
                });

                Puts("MushroomGrower plugin initialized successfully.");
            }
            catch (Exception ex)
            {
                Puts($"Error in OnServerInitialized: {ex.Message}");
                Puts($"StackTrace: {ex.StackTrace}");
            }
        }

        void OnServerSave()
        {
    		NextTick(() => SaveMushrooms());
        }

        void Unload()
        {
            SaveMushrooms();
            RemoveAllMushrooms();
            planterTime.Clear();
        }

        void RemoveAllMushrooms()
        {
            var planters = UnityEngine.Object.FindObjectsOfType<PlanterBox>();
            int removedCount = 0;

            foreach (var planter in planters)
            {
                var mushroomsToRemove = new List<BaseEntity>();
                foreach (var child in planter.children)
                {
                    if (MushroomClusterPrefabs.Contains(child.PrefabName))
                    {
                        mushroomsToRemove.Add(child);
                    }
                }

                foreach (var mushroom in mushroomsToRemove)
                {
                    mushroom.Kill();
                    removedCount++;
                }
            }

            if (removedCount > 0)
            {
                Puts($"Removed {removedCount} mushrooms.");
            }
        }

        void SaveMushrooms()
        {
            savedMushrooms.Clear();
            var planters = UnityEngine.Object.FindObjectsOfType<PlanterBox>();
            
            foreach (var planter in planters)
            {
                var mushroomList = new List<SerializableMushroomData>();
                foreach (var child in planter.children)
                {
                    if (MushroomClusterPrefabs.Contains(child.PrefabName))
                    {
                        mushroomList.Add(new SerializableMushroomData(child));
                    }
                }
                
                if (mushroomList.Count > 0)
                {
                    savedMushrooms[planter.net.ID.Value.ToString()] = mushroomList;
                }
            }
            
            Interface.Oxide.DataFileSystem.WriteObject("MushroomGrower", savedMushrooms);
        }

        void LoadMushrooms()
        {
            savedMushrooms = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<string, List<SerializableMushroomData>>>("MushroomGrower");
            
            if (savedMushrooms == null)
            {
                savedMushrooms = new Dictionary<string, List<SerializableMushroomData>>();
                Puts("No saved mushroom data found. Starting fresh.");
                return;
            }

            HashSet<string> validPlanterIds = new HashSet<string>();
            var planters = UnityEngine.Object.FindObjectsOfType<PlanterBox>();
            
            foreach (var planter in planters)
            {
                validPlanterIds.Add(planter.net.ID.Value.ToString());
            }

            int restoredCount = 0;
            foreach (var entry in savedMushrooms.ToList())
            {
                if (validPlanterIds.Contains(entry.Key) && ulong.TryParse(entry.Key, out ulong entityId))
                {
                    var planter = BaseNetworkable.serverEntities.Find(new NetworkableId(entityId)) as PlanterBox;
                    if (planter != null)
                    {
                        foreach (var mushroomData in entry.Value)
                        {
                            BaseEntity mushroom = mushroomData.CreateMushroom(planter);
                            if (mushroom != null)
                            {
                                restoredCount++;
                            }
                        }
                    }
                }
                else
                {
                    savedMushrooms.Remove(entry.Key);
                }
            }
            
            Interface.Oxide.DataFileSystem.WriteObject("MushroomGrower", savedMushrooms);
            Puts($"Restored {restoredCount} mushrooms across {savedMushrooms.Count} planters.");
        }

        void CleanupInvalidPlanterData()
        {
            HashSet<string> validPlanterIds = new HashSet<string>();
            var planters = UnityEngine.Object.FindObjectsOfType<PlanterBox>();
            
            foreach (var planter in planters)
            {
                validPlanterIds.Add(planter.net.ID.Value.ToString());
            }

            List<string> invalidKeys = savedMushrooms.Keys
                .Where(key => !validPlanterIds.Contains(key))
                .ToList();

            if (invalidKeys.Count > 0)
            {
                foreach (string key in invalidKeys)
                {
                    savedMushrooms.Remove(key);
                }
                
                Interface.Oxide.DataFileSystem.WriteObject("MushroomGrower", savedMushrooms);
                Puts($"Cleanup: Removed {invalidKeys.Count} invalid planter entries.");
            }
        }

        void CheckPlantersForMushrooms()
        {
            var planters = UnityEngine.Object.FindObjectsOfType<PlanterBox>();
            
            foreach (var planter in planters)
            {
                if (!planterTime.ContainsKey(planter.net.ID))
                {
                    planterTime[planter.net.ID] = 0f;
                }
                
                planterTime[planter.net.ID] += config.MushroomGrowthInterval;

                if (planterTime[planter.net.ID] >= config.MushroomGrowthInterval)
                {
                    TryGrowMushroomCluster(planter);
                }
            }
        }

        private bool HasNoGrowPermission(PlanterBox planter)
        {
            if (planter.OwnerID == 0)
                return false;

            string odwnerId = planter.OwnerID.ToString();
            return permission.UserHasPermission(odwnerId, PermissionNoGrow);
        }

        private bool TryConsumeFertilizer(PlanterBox planter)
        {
            if (!config.RequireFertilizer)
                return true;

            var inventory = planter.inventory;
            if (inventory == null || inventory.itemList == null || inventory.itemList.Count == 0)
                return false;

            for (int i = inventory.itemList.Count - 1; i >= 0; i--)
            {
                var item = inventory.itemList[i];
                if (item != null && item.info.shortname == FertilizerShortname && item.skin == 0)
                {
                    item.UseItem(1);
                    return true;
                }
            }

            return false;
        }

        void TryGrowMushroomCluster(PlanterBox planter)
        {
            if (UnityEngine.Random.value > config.MushroomGrowthChance)
                return;

            if (planter.soilSaturation < config.MinimumWaterForGrowth)
                return;

            if (planter.soilSaturation < config.MushroomWaterRequired)
                return;

            if (IsPlayerOnPlanter(planter))
                return;

            if (HasNoGrowPermission(planter))
                return;

            PlanterInfo planterInfo = GetPlanterInfo(planter);
            List<Vector3> availablePositions = GetAvailablePositions(planter, planterInfo);

            if (availablePositions.Count == 0)
                return;

            if (!TryConsumeFertilizer(planter))
                return;

            Vector3 position = availablePositions[UnityEngine.Random.Range(0, availablePositions.Count)];
            string selectedPrefab = MushroomClusterPrefabs[UnityEngine.Random.Range(0, MushroomClusterPrefabs.Length)];
            Quaternion randomRotation = Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0);

            BaseEntity mushroomCluster = GameManager.server.CreateEntity(selectedPrefab, position, randomRotation);

            if (mushroomCluster != null)
            {
                planter.soilSaturation -= (int)config.MushroomWaterRequired;
                if (planter.soilSaturation < 0)
                    planter.soilSaturation = 0;

                mushroomCluster.Spawn();
                mushroomCluster.SetParent(planter, worldPositionStays: true);
                mushroomCluster.SendNetworkUpdate();
                planter.SendNetworkUpdate();
            }
        }

        private bool IsPlayerOnPlanter(PlanterBox planter)
        {
            if (planter == null || planter.IsDestroyed)
                return false;

            Bounds planterBounds = GetPlanterBounds(planter);
            planterBounds.Expand(new Vector3(0.0f, 0.5f, 0.0f));

            var players = BasePlayer.activePlayerList;
            foreach (var player in players)
            {
                if (player == null || player.IsDestroyed || player.IsDead())
                    continue;

                if (planterBounds.Contains(player.transform.position))
                    return true;

                if (player.IsSleeping() && planterBounds.Contains(player.eyes.position))
                    return true;
            }

            return false;
        }

        private Bounds GetPlanterBounds(PlanterBox planter)
        {
            var collider = planter.GetComponent<Collider>();
            if (collider != null)
            {
                return collider.bounds;
            }

            Vector3 size = Vector3.one;

            switch (planter.PrefabName)
            {
                case LargePlanterPrefab:
                case RailRoadPlanterPrefab:
                    size = new Vector3(3f, 0.5f, 3f);
                    break;
                case SmallPlanterPrefab:
                    size = new Vector3(1.5f, 0.5f, 0.5f);
                    break;
                case BathtubPlanterPrefab:
                    size = new Vector3(2f, 0.5f, 0.8f);
                    break;
                case MinecartPlanterPrefab:
                    size = new Vector3(1.5f, 0.5f, 0.8f);
                    break;
                case SinglePlantPotPrefab:
                    size = new Vector3(0.5f, 0.5f, 0.5f);
                    break;
                case TrianglePlanterPrefab:
                case RailRoadTrianglePlanterPrefab:
                    size = new Vector3(1.5f, 0.5f, 1.5f);
                    break;
            }

            return new Bounds(planter.transform.position, size);
        }

        PlanterInfo GetPlanterInfo(PlanterBox planter)
        {
            switch (planter.PrefabName)
            {
                case LargePlanterPrefab:
                    return new PlanterInfo(3, 3, 3f, 9, 0.3f);
                case SmallPlanterPrefab:
                    return new PlanterInfo(1, 3, 1.5f, 3, 0.3f);
                case BathtubPlanterPrefab:
                    return new PlanterInfo(1, 3, 2f, 3, 0.6f);
                case MinecartPlanterPrefab:
                    return new PlanterInfo(1, 2, 1.5f, 2, 0.8f);
                case RailRoadPlanterPrefab:
                    return new PlanterInfo(3, 3, 3f, 9, 0.4f);
                case SinglePlantPotPrefab:
                    return new PlanterInfo(1, 1, 0.5f, 1, 0.3f);
                case TrianglePlanterPrefab:
                case RailRoadTrianglePlanterPrefab:
                    return new PlanterInfo(2, 2, 1.5f, 4, 0.3f);
                default:
                    return new PlanterInfo(1, 3, 1.5f, 3, 0.3f);
            }
        }

        List<Vector3> GetAvailablePositions(PlanterBox planter, PlanterInfo info)
        {
            switch (planter.PrefabName)
            {
                case LargePlanterPrefab:
                case RailRoadPlanterPrefab:
                    return GetLargePlanterPositions(planter, info);
                case SmallPlanterPrefab:
                    return GetSmallPlanterPositions(planter, info);
                case BathtubPlanterPrefab:
                    return GetLinearPlanterPositions(planter, info, 0.9f, planter.transform.right);
                case MinecartPlanterPrefab:
                    return GetLinearPlanterPositions(planter, info, 0.6f, planter.transform.right);
                case SinglePlantPotPrefab:
                    return GetSinglePlantPotPositions(planter, info);
                case TrianglePlanterPrefab:
                case RailRoadTrianglePlanterPrefab:
                    return GetTrianglePlanterPositions(planter, info);
                default:
                    return GetSmallPlanterPositions(planter, info);
            }
        }

        List<Vector3> GetLargePlanterPositions(PlanterBox planter, PlanterInfo info)
        {
            List<Vector3> positions = new List<Vector3>();
            float cellSizeX = info.Size / info.GridSizeX;
            float cellSizeZ = info.Size / info.GridSizeZ;
            float startOffsetX = -cellSizeX * (info.GridSizeX - 1) / 2;
            float startOffsetZ = -cellSizeZ * (info.GridSizeZ - 1) / 2;

            for (int row = 0; row < info.GridSizeZ; row++)
            {
                for (int col = 0; col < info.GridSizeX; col++)
                {
                    float xOffset = startOffsetX + col * cellSizeX;
                    float zOffset = startOffsetZ + row * cellSizeZ;

                    Vector3 position = planter.transform.position +
                                       planter.transform.right * xOffset +
                                       planter.transform.forward * zOffset +
                                       new Vector3(0, info.Height, 0);

                    if (!IsPositionOccupied(planter, position, Mathf.Min(cellSizeX, cellSizeZ) / 2))
                    {
                        positions.Add(position);
                    }
                }
            }
            return positions;
        }

        List<Vector3> GetSmallPlanterPositions(PlanterBox planter, PlanterInfo info)
        {
            List<Vector3> positions = new List<Vector3>();
            float planterLength = info.Size;
            float effectiveLength = planterLength * 1.3f;
            float spacing = effectiveLength / (info.MaxMushrooms - 1);

            for (int i = 0; i < info.MaxMushrooms; i++)
            {
                float offset = (i - 1) * spacing;
                Vector3 position = planter.transform.position +
                                   planter.transform.right * offset +
                                   new Vector3(0, info.Height, 0);
                
                if (!IsPositionOccupied(planter, position, spacing / 2))
                {
                    positions.Add(position);
                }
            }
            return positions;
        }

        List<Vector3> GetLinearPlanterPositions(PlanterBox planter, PlanterInfo info, float effectiveLengthFactor, Vector3 direction)
        {
            List<Vector3> positions = new List<Vector3>();
            float planterLength = info.Size;
            float effectiveLength = planterLength * effectiveLengthFactor;
            float spacing = effectiveLength / (info.MaxMushrooms - 1);

            for (int i = 0; i < info.MaxMushrooms; i++)
            {
                float offset = (i - (info.MaxMushrooms - 1) / 2f) * spacing;
                Vector3 position = planter.transform.position +
                                   direction * offset +
                                   new Vector3(0, info.Height, 0);

                if (!IsPositionOccupied(planter, position, spacing / 2))
                {
                    positions.Add(position);
                }
            }
            return positions;
        }

        List<Vector3> GetSinglePlantPotPositions(PlanterBox planter, PlanterInfo info)
        {
            List<Vector3> positions = new List<Vector3>();
            Vector3 position = planter.transform.position + new Vector3(0, info.Height, 0);

            if (!IsPositionOccupied(planter, position, 0.25f))
            {
                positions.Add(position);
            }

            return positions;
        }

        List<Vector3> GetTrianglePlanterPositions(PlanterBox planter, PlanterInfo info)
        {
            List<Vector3> positions = new List<Vector3>();
            float radius = info.Size * 0.7f;
            float rotationOffset = Mathf.PI / 3.4f;
            float globalOffsetX = -0.2f;
            float globalOffsetZ = -0.0f;

            for (int i = 0; i < 3; i++)
            {
                float angle = (Mathf.PI * 2 / 3 * i) + rotationOffset;
                float adjustedRadius = radius * 0.6f;

                Vector3 localPosition = new Vector3(
                    Mathf.Cos(angle) * adjustedRadius + globalOffsetX,
                    info.Height,
                    Mathf.Sin(angle) * adjustedRadius + globalOffsetZ
                );

                Vector3 worldPosition = planter.transform.TransformPoint(localPosition);

                if (!IsPositionOccupied(planter, worldPosition, 0.3f))
                {
                    positions.Add(worldPosition);
                }
            }

            Vector3 centerLocalPosition = new Vector3(globalOffsetX, info.Height, globalOffsetZ);
            Vector3 centerWorldPosition = planter.transform.TransformPoint(centerLocalPosition);

            if (!IsPositionOccupied(planter, centerWorldPosition, 0.3f))
            {
                positions.Add(centerWorldPosition);
            }

            return positions;
        }

        bool IsPositionOccupied(PlanterBox planter, Vector3 position, float radius)
        {
            foreach (var child in planter.children)
            {
                if (child is GrowableEntity || MushroomClusterPrefabs.Contains(child.PrefabName))
                {
                    if (Vector3.Distance(child.transform.position, position) < radius * 1.5f)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        void OnEntityKill(BaseNetworkable entity)
        {
            if (entity is PlanterBox planter)
            {
                string planterId = planter.net.ID.Value.ToString();
                planterTime.Remove(planter.net.ID);

                if (savedMushrooms.ContainsKey(planterId))
                {
                    savedMushrooms.Remove(planterId);
                    Interface.Oxide.DataFileSystem.WriteObject("MushroomGrower", savedMushrooms);
                }
            }
        }

        object CanBuild(Planner planner, Construction prefab, Construction.Target target)
        {
            BasePlayer player = planner.GetOwnerPlayer();
            if (player == null) return null;

            if (prefab.fullName.Contains("plants"))
            {
                PlanterBox planter = GetTargetPlanter(player);
                if (planter != null)
                {
                    Vector3 worldPosition = GetPlantPosition(player, planter);
                    if (worldPosition == Vector3.zero)
                    {
                        return null;
                    }
                    
                    Vector3 localPosition = planter.transform.InverseTransformPoint(worldPosition);
                    
                    Vector3 planterScale = planter.transform.lossyScale;
                    float maxDistance = Mathf.Max(planterScale.x, planterScale.z) / 2 + 2.0f;
                    if (Mathf.Abs(localPosition.x) > maxDistance || 
                        Mathf.Abs(localPosition.z) > maxDistance)
                    {
                        return null;
                    }

                    float checkRadius = 0.6f;
                    if (IsMushroomNearby(planter, localPosition, checkRadius))
                    {
                        return false;
                    }
                }
            }

            return null;
        }

        Vector3 GetPlantPosition(BasePlayer player, PlanterBox planter)
        {
            RaycastHit hit;
            if (Physics.Raycast(player.eyes.HeadRay(), out hit, 30f, LayerMask.GetMask("Deployed", "Construction", "Terrain", "World", "Geology")))
            {
                if (hit.GetEntity() == planter)
                {
                    return hit.point;
                }
            }
            return Vector3.zero;
        }

        bool IsMushroomNearby(PlanterBox planter, Vector3 localPosition, float radius)
        {
            if (planter == null)
            {
                return false;
            }

            foreach (BaseEntity child in planter.children)
            {
                if (child == null) 
                {
                    continue;
                }

                if (MushroomClusterPrefabs.Contains(child.PrefabName))
                {
                    Vector3 childLocalPos = planter.transform.InverseTransformPoint(child.transform.position);
                    Vector3 horizontalDiff = new Vector3(childLocalPos.x - localPosition.x, 0, childLocalPos.z - localPosition.z);
                    float distance = horizontalDiff.magnitude;

                    if (distance < radius)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        PlanterBox GetTargetPlanter(BasePlayer player)
        {
            float maxDistance = 50f;
            float sphereRadius = 50f;

            Ray headRay = new Ray(player.eyes.position, player.eyes.HeadForward());
            RaycastHit[] rayHits = Physics.RaycastAll(headRay, maxDistance, LayerMask.GetMask("Deployed", "Construction", "World", "Terrain", "Water", "Geology"));

            foreach (RaycastHit hit in rayHits)
            {
                PlanterBox planter = CheckHitForPlanter(hit);
                if (planter != null) return planter;
            }

            RaycastHit[] sphereHits = Physics.SphereCastAll(headRay, sphereRadius, maxDistance, LayerMask.GetMask("Deployed", "Construction", "World", "Terrain", "Water", "Geology"));

            foreach (RaycastHit hit in sphereHits)
            {
                PlanterBox planter = CheckHitForPlanter(hit);
                if (planter != null) return planter;
            }

            return null;
        }

        private PlanterBox CheckHitForPlanter(RaycastHit hit)
        {
            BaseEntity entity = hit.GetEntity();
            if (entity != null)
            {
                PlanterBox planter = entity as PlanterBox;
                if (planter != null)
                {
                    return planter;
                }
            }
            return null;
        }

        [ConsoleCommand("mushroom.clear")]
        void CommandClearMushrooms(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !arg.IsAdmin)
            {
                Puts("This command is admin only.");
                return;
            }

            savedMushrooms.Clear();
            Interface.Oxide.DataFileSystem.WriteObject("MushroomGrower", savedMushrooms);
            RemoveAllMushrooms();
            planterTime.Clear();

            Puts("All mushroom data has been cleared.");
        }

        class PlanterInfo
        {
            public int GridSizeX { get; private set; }
            public int GridSizeZ { get; private set; }
            public float Size { get; private set; }
            public int MaxMushrooms { get; private set; }
            public float Height { get; private set; }

            public PlanterInfo(int gridSizeX, int gridSizeZ, float size, int maxMushrooms, float height)
            {
                GridSizeX = gridSizeX;
                GridSizeZ = gridSizeZ;
                Size = size;
                MaxMushrooms = maxMushrooms;
                Height = height;
            }
        }
    }
} 