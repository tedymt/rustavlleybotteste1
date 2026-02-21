using System.Collections.Generic;
using Oxide.Core.Configuration;
using Oxide.Game.Rust.Cui;
using Oxide.Core.Plugins;
using Newtonsoft.Json;
using UnityEngine;
using System.Linq;
using Oxide.Core;
using System;
using VLB;
using Rust;
using Facepunch;
using HarmonyLib;

namespace Oxide.Plugins
{
    [Info("Auto Farm", "Razor", "3.2.9")]
    [Description("Auto Farm The PlanterBoxes")]
    public class AutoFarm : RustPlugin
    {
        #region Init
        [PluginReference]
        private Plugin Ganja;

        FarmEntity pcdData;
        private DynamicConfigFile PCDDATA;
        static System.Random random = new System.Random();
        private static AutoFarm _;
        public Dictionary<ulong, int> playerTotals = new Dictionary<ulong, int>();
        public Dictionary<ulong, bool> disabledFarmPlayer = new Dictionary<ulong, bool>();
        public List<ulong> GametipShown = new List<ulong>(); 

        void Init()
        {
            _ = this;

            PCDDATA = Interface.Oxide.DataFileSystem.GetFile(Name + "/Farm_Data");
            LoadData();
        }

        private void OnServerInitialized()
        {
            if (configData.vipInformation == null)
            {
                configData.vipInformation = new Dictionary<string, vipInfo>()
                {
                    { "autofarm.allow", new vipInfo() { AddSprinkler = false, AddStorageAdapter = false, BoxStorageSlots = 6, heatExposure = false, lightExposure = false, SprinklerNeedsWater = true, totalFarms = 5, seedStorageSlots = 6, cloneList = new Dictionary<string, string>() { { "orchid", "Sapling" }, { "sunflower", "Sapling" }, { "wheat", "Sapling" }, { "rose", "Sapling" }, { "blue_berry", "Sapling" }, { "white_berry", "Sapling" }, { "red_berry", "Sapling" }, { "green_berry", "Sapling" }, { "black_berry", "Sapling" }, { "yellow_berry", "Sapling" }, { "pumpkin", "Sapling" }, { "potato", "Sapling" }, { "hemp", "Sapling" }, { "corn", "Sapling" } }, knifeList = new Dictionary<int, int>() { { 1814288539, 0 }, { -194509282, 0 }, { 2040726127, 0 }, { -2073432256, 0 } }, seedsAllowedAndMultiplier = new Dictionary<int, int>() { { 122783240, 1 }, { -1037472336, 1 }, { 803954639, 1 }, { 998894949, 1 }, { 1911552868, 1 }, { -1776128552, 1 }, { -237809779, 1 }, { -2084071424, 1 }, { -1511285251, 1 }, { 830839496, 1 }, { -992286106, 1 }, { -520133715, 1 }, { 838831151, 1 }, { -778875547, 1 }, { -1305326964, 1 }, { -886280491, 1 }, { 1512054436, 1 }, { 1898094925, 1 }, { 2133269020, 1 }, { 1533551194, 1 }, { 390728933, 1 }, { -798662404, 1 }, { 1004843240, 1 }, { 912235912, 1 }, { 1412103380, 1 }, { 924598634, 1 }, { -1790885730, 1 }, { -19360132, 1 } } } },
                    { "autofarm.vip", new vipInfo() { AddSprinkler = false, AddStorageAdapter = false, BoxStorageSlots = 6, heatExposure = false, lightExposure = false, SprinklerNeedsWater = true, totalFarms = 5, seedStorageSlots = 6, cloneList = new Dictionary<string, string>() { { "orchid", "Sapling" }, { "sunflower", "Sapling" }, { "wheat", "Sapling" }, { "rose", "Sapling" }, { "blue_berry", "Sapling" }, { "white_berry", "Sapling" }, { "red_berry", "Sapling" }, { "green_berry", "Sapling" }, { "black_berry", "Sapling" }, { "yellow_berry", "Sapling" }, { "pumpkin", "Sapling" }, { "potato", "Sapling" }, { "hemp", "Sapling" }, { "corn", "Sapling" } }, knifeList = new Dictionary<int, int>() { { 1814288539, 0 }, { -194509282, 0 }, { 2040726127, 0 }, { -2073432256, 0 } }, seedsAllowedAndMultiplier = new Dictionary<int, int>() { { 122783240, 1 }, { -1037472336, 1 }, { 803954639, 1 }, { 998894949, 1 }, { 1911552868, 1 }, { -1776128552, 1 }, { -237809779, 1 }, { -2084071424, 1 }, { -1511285251, 1 }, { 830839496, 1 }, { -992286106, 1 }, { -520133715, 1 }, { 838831151, 1 }, { -778875547, 1 }, { -1305326964, 1 }, { -886280491, 1 }, { 1512054436, 1 }, { 1898094925, 1 }, { 2133269020, 1 }, { 1533551194, 1 }, { 390728933, 1 }, { -798662404, 1 }, { 1004843240, 1 }, { 912235912, 1 }, { 1412103380, 1 }, { 924598634, 1 }, { -1790885730, 1 }, { -19360132, 1 } } } },
                    { "autofarm.advanced", new vipInfo() { AddSprinkler = true, AddStorageAdapter = true, BoxStorageSlots = 12, heatExposure = false, lightExposure = false, SprinklerNeedsWater = false, totalFarms = 10, seedStorageSlots = 12, cloneList = new Dictionary<string, string>() { { "orchid", "Sapling" }, { "sunflower", "Sapling" }, { "wheat", "Sapling" }, { "rose", "Sapling" }, { "blue_berry", "Sapling" }, { "white_berry", "Sapling" }, { "red_berry", "Sapling" }, { "green_berry", "Sapling" }, { "black_berry", "Sapling" }, { "yellow_berry", "Sapling" }, { "pumpkin", "Sapling" }, { "potato", "Sapling" }, { "hemp", "Sapling" }, { "corn", "Sapling" } }, knifeList = new Dictionary<int, int>() { { 1814288539, 0 }, { -194509282, 0 }, { 2040726127, 0 }, { -2073432256, 0 } }, seedsAllowedAndMultiplier = new Dictionary<int, int>() { { 122783240, 1 }, { -1037472336, 1 }, { 803954639, 1 }, { 998894949, 1 }, { 1911552868, 1 }, { -1776128552, 1 }, { -237809779, 1 }, { -2084071424, 1 }, { -1511285251, 1 }, { 830839496, 1 }, { -992286106, 1 }, { -520133715, 1 }, { 838831151, 1 }, { -778875547, 1 }, { -1305326964, 1 }, { -886280491, 1 }, { 1512054436, 1 }, { 1898094925, 1 }, { 2133269020, 1 }, { 1533551194, 1 }, { 390728933, 1 }, { -798662404, 1 }, { 1004843240, 1 }, { 912235912, 1 }, { 1412103380, 1 }, { 924598634, 1 }, { -1790885730, 1 }, { -19360132, 1 } } } }
                };
                SaveConfig();
            }

            if (configData.settings.SprinklerRadius <= 0.0f)
            {
                configData.settings.SprinklerRadius = 1;
                SaveConfig();
            }

            if (configData.settings.LargePlanterSprinklerMinSoilSaturation <= 0)
            {
                configData.settings.LargePlanterSprinklerMinSoilSaturation = 5100;
                configData.settings.LargePlanterSprinklerMaxSoilSaturation = 6000;
                configData.settings.SmallPlanterSprinklerMinSoilSaturation = 1650;
                configData.settings.SmallPlanterSprinklerMaxSoilSaturation = 1750;
                SaveConfig();
            }

            RegisterPermissions();

            NextTick(() =>
            {
                int removeCount = 0;
                int totalPlanters = 0;
                if (pcdData.Planters.Count <= 0) return;

                List<ulong> removeList = Pool.Get<List<ulong>>();

                foreach (var element in pcdData.Planters)
                {
                    var networkable = FindEntity(element.Key);
                   if (networkable == null || !(networkable is PlanterBox)) { removeList.Add(element.Key); removeCount++; }
                    else
                    {
                        totalPlanters++;
                        planterBoxBehavior mono = networkable.GetComponent<planterBoxBehavior>();
                        if (mono != null)
                            UnityEngine.Object.DestroyImmediate(mono);

                        AddTheComponent(networkable, element.Value.configPerm);
                    }
                }

                foreach (var key in removeList)
                {
                    if (pcdData.Planters.ContainsKey(key))
                        pcdData.Planters.Remove(key);
                }

                Pool.FreeUnmanaged(ref removeList);
                SaveData();
                PrintWarning($"Removed {removeCount} planters not found from datafile, and reactivated {totalPlanters}");
            });
        }

        private void AddTheComponent(BaseNetworkable networkable, string config)
        {
            if (string.IsNullOrEmpty(config) || !configData.vipInformation.ContainsKey(config))
                config = configData.vipInformation.Keys.First();

            if (string.IsNullOrEmpty(config))
                return;
            planterBoxBehavior mono = networkable.gameObject.AddComponent<planterBoxBehavior>();
            mono.config = configData.vipInformation[config];
            timer.Once(5, () => { if (_ != null && mono != null) mono.autoFill(); });
        }

        private void Unload()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
                CuiHelper.DestroyUi(player, Ui.Overlay.Screen.Panel);

            foreach (planterBoxBehavior Controler in planterBoxBehavior._AllPlanters)
                UnityEngine.Object.DestroyImmediate(Controler);

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                player.SendConsoleCommand("gametip.hidegametip");
            }

            _ = null;
        }

        private void RegisterPermissions()
        {   
             if (configData.vipInformation != null && configData.vipInformation.Count > 0)
                foreach (var permVip in configData.vipInformation)
                    permission.RegisterPermission(permVip.Key, this);
        }
        #endregion

        #region Config
        private ConfigData configData;
        class ConfigData
        {
            [JsonProperty(PropertyName = "Settings")]
            public Settings settings { get; set; }

            [JsonProperty(PropertyName = "Permission needed and options")]
            public Dictionary<string, vipInfo> vipInformation { get; set; }

            public class Settings
            {
                [JsonProperty(PropertyName = "Disable autofarm placement by default /autofarm")]
                public bool DisablePlacementByDefault { get; set; }
                [JsonProperty(PropertyName = "How far can sprinkler water")]
                public float SprinklerRadius { get; set; }
                [JsonProperty(PropertyName = "Large Box Sprinkler On Soil Saturation Level")]
                public int LargePlanterSprinklerMinSoilSaturation { get; set; }
                [JsonProperty(PropertyName = "Large Box Sprinkler OFF Soil Saturation Level")]
                public int LargePlanterSprinklerMaxSoilSaturation { get; set; }
                [JsonProperty(PropertyName = "Small Box Sprinkler On Soil Saturation Level")]
                public int SmallPlanterSprinklerMinSoilSaturation { get; set; }
                [JsonProperty(PropertyName = "Small Box Sprinkler OFF Soil Saturation Level")]
                public int SmallPlanterSprinklerMaxSoilSaturation { get; set; }
                [JsonProperty(PropertyName = "Enable for use in plugins that require CallHookOnCollectiblePickup")]
                public bool CallHookOnCollectiblePickup { get; set; }
                [JsonProperty(PropertyName = "Enable weed pick from Ganja plugin")]
                public bool GanjaPluginEnable { get; set; }
            }

            public Oxide.Core.VersionNumber Version { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            configData = Config.ReadObject<ConfigData>();

            if (configData.Version < Version)
                UpdateConfigValues();

            Config.WriteObject(configData, true);
        }

        protected override void LoadDefaultConfig() => configData = GetBaseConfig();

        private ConfigData GetBaseConfig()
        {
            return new ConfigData
            {
                settings = new ConfigData.Settings
                {
                    DisablePlacementByDefault = false,
                    SprinklerRadius = 1.0f,
                    LargePlanterSprinklerMinSoilSaturation = 5100,
                    LargePlanterSprinklerMaxSoilSaturation = 6000,
                    SmallPlanterSprinklerMinSoilSaturation = 1650,
                    SmallPlanterSprinklerMaxSoilSaturation = 1750,
                    CallHookOnCollectiblePickup = false,
                    GanjaPluginEnable = false,
                },

                Version = Version
            };
        }

        protected override void SaveConfig() => Config.WriteObject(configData, true);

        private void UpdateConfigValues()
        {
            PrintWarning("Config update detected! Updating config values...");

            ConfigData baseConfig = GetBaseConfig();

            if (configData.Version < new VersionNumber(3, 0, 0))
                configData = baseConfig;

            configData.Version = Version;
            PrintWarning("Config update completed!");
        }

        public class vipInfo
        {
            [JsonProperty(PropertyName = "How many auto farms they allowed to have")]
            public int totalFarms { get; set; }

            [JsonProperty(PropertyName = "How many slots in seed container")]
            public int seedStorageSlots { get; set; }

            [JsonProperty(PropertyName = "How many slots in output container")]
            public int BoxStorageSlots { get; set; }

            [JsonProperty(PropertyName = "Add sprinkler to planter")]
            public bool AddSprinkler { get; set; }

            [JsonProperty(PropertyName = "Sprinkler needs water hookup to work")]
            public bool SprinklerNeedsWater { get; set; }

            [JsonProperty(PropertyName = "Add storage adapters")]
            public bool AddStorageAdapter { get; set; }

            [JsonProperty(PropertyName = "Always Light Exposure 100%")]
            public bool lightExposure { get; set; }

            [JsonProperty(PropertyName = "Always Temperature Exposure 100%")]
            public bool heatExposure { get; set; }

            [JsonProperty(PropertyName = "Allowed seed itemID's and multiplier amount to get on auto gather")]
            public Dictionary<int, int> seedsAllowedAndMultiplier { get; set; }

            [JsonProperty(PropertyName = "Enable cloning with knife")]
            public bool enableCloning { get; set; }

            [JsonProperty(PropertyName = "Allowed To Clone And Stage ")]
            public Dictionary<string, string> cloneList { get; set; }

            [JsonProperty(PropertyName = "Available tools And Clone Multiplier")]
            public Dictionary<int, int> knifeList { get; set; }
        }
        #endregion Config

        #region planerBox
        private struct PlanterConfig
        {
            public static PlanterConfig Small = new PlanterConfig
            {
                SprinklerPos = new Vector3(0f, 0.11f, 0f),
                SplitterPos = new Vector3(0, 0.1f, 0.5f),
                SplitterRot = Quaternion.Euler(0, 0, 0),
                FertilizerAdapterPos = new Vector3(0.5f, 0.30f, 0.0f),
                FertilizerAdapterRot = Quaternion.Euler(0, 0, 0),
                InputStoragePos = new Vector3(0.5f, 0.20f, 0.50f),
                InputStorageRot = Quaternion.Euler(0, 0, 90),
                InputAdapterPos = new Vector3(1.1f, 0.20f, 0.50f),
                InputAdapterRot = Quaternion.Euler(90, 0, 0),
                OutputStoragePos = new Vector3(-0.5f, 0.20f, 0.50f),
                OutputStorageRot = Quaternion.Euler(0, 0, 270),
                OutputAdapterPos = new Vector3(-1.1f, .20f, 0.50f),
                OutputAdapterRot = Quaternion.Euler(90, 0, 0),
            };

            public static PlanterConfig Minecart = new PlanterConfig
            {
                SprinklerPos = new Vector3(0f, 0.55f, 0f),
                SplitterPos = new Vector3(0, 0.36f, 0.75f),
                SplitterRot = Quaternion.Euler(0, 0, 0),
                FertilizerAdapterPos = new Vector3(0.55f, 0.75f, 0.3f),
                FertilizerAdapterRot = Quaternion.Euler(0, 0, 0),
                InputStoragePos = new Vector3(0.183f, 0.60f, 0.50f),
                InputStorageRot = Quaternion.Euler(0, 0, 0),
                InputAdapterPos = new Vector3(0.58f, 0.60f, 0.50f),
                InputAdapterRot = Quaternion.Euler(90, 0, 0),
                OutputStoragePos = new Vector3(-0.183f, 0.60f, 0.50f),
                OutputStorageRot = Quaternion.Euler(0, 0, 0),
                OutputAdapterPos = new Vector3(-0.58f, 0.60f, 0.50f),
                OutputAdapterRot = Quaternion.Euler(90, 0, 0),
            };

            public static PlanterConfig Bathtub = new PlanterConfig
            {
                SprinklerPos = new Vector3(0f, 0.35f, 0f),
                SplitterPos = new Vector3(0, 0.12f, 0.75f),
                SplitterRot = Quaternion.Euler(0, 0, 0),
                FertilizerAdapterPos = new Vector3(0.58f, 0.55f, 0.3f),
                FertilizerAdapterRot = Quaternion.Euler(0, 0, 0),
                InputStoragePos = new Vector3(0.183f, 0.32f, 0.50f),
                InputStorageRot = Quaternion.Euler(0, 0, 0),
                InputAdapterPos = new Vector3(0.58f, 0.40f, 0.50f),
                InputAdapterRot = Quaternion.Euler(90, 0, 0),
                OutputStoragePos = new Vector3(-0.183f, 0.32f, 0.50f),
                OutputStorageRot = Quaternion.Euler(0, 0, 0),
                OutputAdapterPos = new Vector3(-0.58f, 0.40f, 0.50f),
                OutputAdapterRot = Quaternion.Euler(90, 0, 0),
            };

            public static PlanterConfig Railroad = new PlanterConfig
            {
                SprinklerPos = new Vector3(0f, 0.13f, 0f),
                SplitterPos = new Vector3(0, 0.1f, 1.5f),
                SplitterRot = Quaternion.Euler(0, 0, 0),
                FertilizerAdapterPos = new Vector3(0.5f, 0.30f, 1.0f),
                FertilizerAdapterRot = Quaternion.Euler(0, 0, 0),
                InputStoragePos = new Vector3(0.5f, 0.20f, 1.40f),
                InputStorageRot = Quaternion.Euler(0, 0, 90),
                InputAdapterPos = new Vector3(1.1f, 0.20f, 1.42f),
                InputAdapterRot = Quaternion.Euler(90, 0, 0),
                OutputStoragePos = new Vector3(-0.5f, 0.20f, 1.40f),
                OutputStorageRot = Quaternion.Euler(0, 0, 270),
                OutputAdapterPos = new Vector3(-1.1f, 0.20f, 1.42f),
                OutputAdapterRot = Quaternion.Euler(90, 0, 0),
            };

            public static PlanterConfig Large = new PlanterConfig
            {
                SprinklerPos = new Vector3(0f, 0.11f, 0f),
                SplitterPos = new Vector3(0, 0.1f, 1.425f),
                SplitterRot = Quaternion.Euler(0, 0, 0),
                FertilizerAdapterPos = new Vector3(0.5f, 0.30f, 1.0f),
                FertilizerAdapterRot = Quaternion.Euler(0, 0, 0),
                InputStoragePos = new Vector3(0.5f, 0.20f, 1.40f),
                InputStorageRot = Quaternion.Euler(0, 0, 90),
                InputAdapterPos = new Vector3(1.1f, 0.20f, 1.40f),
                InputAdapterRot = Quaternion.Euler(90, 0, 0),
                OutputStoragePos = new Vector3(-0.5f, 0.20f, 1.40f),
                OutputStorageRot = Quaternion.Euler(0, 0, 270),
                OutputAdapterPos = new Vector3(-1.1f, 0.20f, 1.40f),
                OutputAdapterRot = Quaternion.Euler(90, 0, 0),
            };

            public static PlanterConfig Triangle = new PlanterConfig
            {
                SprinklerPos = new Vector3(-0.45f, 0.11f, 0f),
                SplitterPos = new Vector3(0.56f, 0.1f, 0f),
                SplitterRot = Quaternion.Euler(0, 90, 0),
                FertilizerAdapterPos = new Vector3(0.3f, 0.30f, 0.0f),
                FertilizerAdapterRot = Quaternion.Euler(0, 90, 0),
                InputStoragePos = new Vector3(0.55f, 0.20f, -0.50f),
                InputStorageRot = Quaternion.Euler(0, 90, 90),
                InputAdapterPos = new Vector3(0.55f, 0.20f, -0.98f),
                InputAdapterRot = Quaternion.Euler(90, 90, 0),
                OutputStoragePos = new Vector3(0.55f, 0.20f, 0.50f),
                OutputStorageRot = Quaternion.Euler(0, 90, 270),
                OutputAdapterPos = new Vector3(0.55f, 0.20f, 0.98f),
                OutputAdapterRot = Quaternion.Euler(90, 90, 0),
            };

            public Vector3 SprinklerPos;
            public Vector3 SplitterPos;
            public Quaternion SplitterRot;
            public Vector3 FertilizerAdapterPos;
            public Quaternion FertilizerAdapterRot;

            public Vector3 InputStoragePos;
            public Quaternion InputStorageRot;
            public Vector3 InputAdapterPos;
            public Quaternion InputAdapterRot;

            public Vector3 OutputStoragePos;
            public Quaternion OutputStorageRot;
            public Vector3 OutputAdapterPos;
            public Quaternion OutputAdapterRot;
        }

        private static PlanterConfig GetPlanterConfig(PlanterBox planterBox)
        {
            switch (planterBox.ShortPrefabName)
            {
                case "planter.small.deployed":
                    return PlanterConfig.Small;
                case "minecart.planter.deployed":
                    return PlanterConfig.Minecart;
                case "bathtub.planter.deployed":
                    return PlanterConfig.Bathtub;
                case "railroadplanter.deployed":
                    return PlanterConfig.Railroad;
                case "planter.triangle.deployed":
                    return PlanterConfig.Triangle;
                case "triangle_railroad_planter.deployed":
                    return PlanterConfig.Triangle;
                default:
                    return PlanterConfig.Large;
            }
        }

        class planterBoxBehavior : FacepunchBehaviour
        {
            public static List<planterBoxBehavior> _AllPlanters = new List<planterBoxBehavior>();
            public static Dictionary<ulong, planterBoxBehavior> _AllLootingPlayers = new Dictionary<ulong, planterBoxBehavior>();

            public PlanterBox planterBox { get; set; }
            public ulong ownerplayer { get; set; }
            public vipInfo config { get; set; }
            private StorageContainer container { get; set; }
            private StorageContainer containerSeeds { get; set; }
            private Sprinkler sprinkler { get; set; }
            private Splitter waterSource { get; set; }
            private ItemDefinition itemDefinition { get; set; }
            private DateTime lastRotate { get; set; }
            private int totalslotsAvailable = 11;
            private float splashRadius = 6f;
            private int soilSaturationON { get; set; }
            private int soilSaturationOFF { get; set; }
            private float nextAutofiltime { get; set; }
            public Item cloningItem { get; set; }
            public bool currentState { get; set; }
            public PlanterConfig planterConfig { get; set; }
            public IndustrialStorageAdaptor OutputAdapter { get; set; }
            public IndustrialStorageAdaptor InputAdapter { get; set; }
            public IndustrialStorageAdaptor FertilizerAdapter { get; set; }
            public bool getAlwaysLight { get; set; }
            public bool getAlwaysTemp { get; set; }

            private void Awake()
            {
                planterBox = GetComponent<PlanterBox>();
                _AllPlanters.Add(this);
                planterConfig = GetPlanterConfig(planterBox);
                if (!_.playerTotals.ContainsKey(planterBox.OwnerID))
                    _.playerTotals.Add(planterBox.OwnerID, 1);
                else _.playerTotals[planterBox.OwnerID]++;
                ownerplayer = planterBox.OwnerID;
                splashRadius = _.configData.settings.SprinklerRadius;

                _.timer.Once(1, () =>
                {
                    if (config.lightExposure)
                        getAlwaysLight = true;

                    if (config.heatExposure)
                        getAlwaysTemp = true;

                    generateStorage();
                    float delay = random.Next(300, 600);
                    currentState = _.pcdData.Planters[planterBox.net.ID.Value].currentState;

                    if (!currentState)
                    {
                        if (config.seedStorageSlots > 0 && config.BoxStorageSlots > 0)
                            InvokeRepeating("isPlanterFull", delay, 601);
                    }
                    else
                    {
                        if (!config.SprinklerNeedsWater && IsInvoking("WaterPlants"))
                            CancelInvoke("WaterPlants");
                    }

                    if (config.seedStorageSlots > 0 && config.BoxStorageSlots > 0)
                        autoFill();
                });
            }

            public void SetPlant(GrowableEntity plant)
            {
                if (getAlwaysLight || getAlwaysTemp)
                    plant.GetOrAddComponent<plantBoxBehavior>().SetUp(getAlwaysLight, getAlwaysTemp);
            }

            private void setTotalAvailableSlots()
            {
                switch (planterBox.ShortPrefabName)
                {
                    case "planter.triangle.deployed":
                        totalslotsAvailable = 4;
                        soilSaturationON = _.configData.settings.SmallPlanterSprinklerMinSoilSaturation;
                        soilSaturationOFF = _.configData.settings.SmallPlanterSprinklerMaxSoilSaturation;
                        break;

                    case "triangle_railroad_planter.deployed":
                    case "minecart.planter.deployed":
                        totalslotsAvailable = 4;
                        soilSaturationON = _.configData.settings.SmallPlanterSprinklerMinSoilSaturation;
                        soilSaturationOFF = _.configData.settings.SmallPlanterSprinklerMaxSoilSaturation;
                        break;

                    case "planter.small.deployed":
                    case "bathtub.planter.deployed":
                        totalslotsAvailable = 5;
                        soilSaturationON = _.configData.settings.SmallPlanterSprinklerMinSoilSaturation;
                        soilSaturationOFF = _.configData.settings.SmallPlanterSprinklerMaxSoilSaturation;
                        break;

                    default:
                        soilSaturationON = _.configData.settings.LargePlanterSprinklerMinSoilSaturation;
                        soilSaturationOFF = _.configData.settings.LargePlanterSprinklerMaxSoilSaturation;
                        break;
                }
                if (OutputAdapter != null)
                    totalslotsAvailable++;
                if (InputAdapter != null)
                    totalslotsAvailable++;
                if (FertilizerAdapter != null)
                    totalslotsAvailable++;
                if (sprinkler != null)
                    totalslotsAvailable++;
                if (waterSource != null)
                    totalslotsAvailable++;
            }

            public void switchState(BasePlayer player, bool newState)
            {
                currentState = newState;
                 _.pcdData.Planters[planterBox.net.ID.Value].currentState = newState;
                _.SaveData();
                if (newState == true)
                {
                    if (!config.SprinklerNeedsWater && IsInvoking("WaterPlants"))
                    {
                        CancelInvoke("WaterPlants");
                        if (sprinkler != null && sprinkler.IsOn())
                        {
                            sprinkler.TurnOff();
                            if (this.IsInvoking(new Action(DoSplash)))
                                this.CancelInvoke(new Action(DoSplash));
                        }
                    }
                    if (IsInvoking("isPlanterFull"))
                        CancelInvoke("isPlanterFull");
                }
                else
                {
                    float delay = random.Next(300, 600);

                    if (!config.SprinklerNeedsWater && !IsInvoking("WaterPlants"))
                        InvokeRepeating("WaterPlants", 10, 30);

                    if (!IsInvoking("isPlanterFull") && config.seedStorageSlots > 0 && config.BoxStorageSlots > 0)
                        InvokeRepeating("isPlanterFull", delay, 601);
                    autoFill();
                }
            }

            public void Rotate()
            {
                if (lastRotate < DateTime.Now)
                {
                    var degrees = 90;
                    if (planterBox.ShortPrefabName == "planter.triangle.deployed" || planterBox.ShortPrefabName == "triangle_railroad_planter.deployed")
                        degrees = 120;

                    lastRotate = DateTime.Now.AddSeconds(2);
                    transform.Rotate(0, degrees, 0);
                    if (sprinkler != null)
                        UpdateLocalPositionAndRotation(transform, sprinkler, planterConfig.SprinklerPos, Quaternion.identity, true);               
                    planterBox.SendNetworkUpdateImmediate();
                }
            }

            public bool pickUpPlanter(BasePlayer player)
            {
                if (containerSeeds != null)
                {
                    if (containerSeeds.inventory.itemList.Count > 0)
                    {
                        _.GameTips(player, _.lang.GetMessage("noPickup", _, player.UserIDString));
                        return false;
                    }
                    else if (container.inventory.itemList.Count > 0)
                    {
                        _.GameTips(player, _.lang.GetMessage("noPickup", _, player.UserIDString));
                        return false;
                    }
                    else if (planterBox.inventory.itemList.Count > 0)
                    {
                        _.GameTips(player, _.lang.GetMessage("noPickup", _, player.UserIDString));
                        return false;
                    }

                    int itemIdToGet;

                    switch (planterBox.ShortPrefabName)
                    {
                        case "minecart.planter.deployed":
                            itemIdToGet = 1361520181;
                            break;
                        case "planter.small.deployed":
                            itemIdToGet = 1903654061;
                            break;
                        case "bathtub.planter.deployed":
                            itemIdToGet = -1274093662;
                            break;
                        case "railroadplanter.deployed":	
                            itemIdToGet = 615112838;
                            break;
                        case "triangle_railroad_planter.deployed":
                            itemIdToGet = 647240052;
                            break;
                        case "planter.triangle.deployed":
                            itemIdToGet = -280812482;
                            break;
                        default:
                            itemIdToGet = 1581210395;
                            break;
                    }

                    var planterItem = ItemManager.CreateByItemID(itemIdToGet, 1, 0);
                    _.NextTick(() =>
                    {
                        if (planterItem != null)
                        {
                            player.GiveItem(planterItem, BaseEntity.GiveItemReason.PickedUp);
                            planterBox.Kill();
                        }
                    });
                }
                return true;
            }

            private void generateStorage()
            {
                // Search radius, for entities that are not parented to the planter.
                var entitySearchRadius = 0.1f;
                bool fertilizerBoxAdapter = false;

                if (config.AddSprinkler)
                {
                    var sprinklerOld = GetNearbyEntity<Sprinkler>(planterBox.transform.TransformPoint(planterConfig.SprinklerPos), entitySearchRadius)
                                ?? GetNearbyEntity<Sprinkler>(planterBox.transform.TransformPoint(PlanterConfig.Large.SprinklerPos), entitySearchRadius);

                    if (sprinklerOld != null && sprinklerOld.GetComponentInParent<PlanterBox>() == null)
                    {
                        sprinklerOld.SetParent(planterBox, true);
                        UpdateLocalPositionAndRotation(transform, sprinklerOld, planterConfig.SprinklerPos, Quaternion.identity); 
                    }
                }

                if (config.SprinklerNeedsWater && config.AddSprinkler)
                {
                    var waterSourceOld = GetNearbyEntity<Splitter>(planterBox.transform.TransformPoint(planterConfig.SplitterPos), entitySearchRadius)
                                ?? GetNearbyEntity<Splitter>(planterBox.transform.TransformPoint(PlanterConfig.Large.SplitterPos), entitySearchRadius);

                    if (waterSourceOld != null && waterSourceOld.GetComponentInParent<PlanterBox>() == null)
                    {
                        waterSourceOld.SetParent(planterBox, true);
                        UpdateLocalPositionAndRotation(transform, waterSourceOld, planterConfig.SplitterPos, planterConfig.SplitterRot);
                    }
                }
                
                List <BaseEntity> tampEntity = Pool.Get<List<BaseEntity>>();
                tampEntity.AddRange(planterBox.children);

                foreach (BaseEntity child in tampEntity)
                {
                    if (child == null)
                        continue;

                    if (child is IndustrialStorageAdaptor)
                    {
                        UpdateLocalPositionAndRotation(transform, child, planterConfig.FertilizerAdapterPos, planterConfig.FertilizerAdapterRot);

                        if (child.name != "NoPickUp")
                        {
                            child.name = "NoPickUp";
                        }
                        FertilizerAdapter = child as IndustrialStorageAdaptor;
                        fertilizerBoxAdapter = true;
                    }
                    if (child is Sprinkler)
                    {
                        sprinkler = child as Sprinkler;
                        UpdateLocalPositionAndRotation(transform, sprinkler, planterConfig.SprinklerPos, Quaternion.identity);

                        if (!config.AddSprinkler)
                        {
                            sprinkler.SetParent(null);
                            sprinkler.Kill();
                        }
                    }

                    if (child is Splitter)
                    {
                        waterSource = child as Splitter;
                        UpdateLocalPositionAndRotation(transform, waterSource, planterConfig.SplitterPos, planterConfig.SplitterRot);

                        if (!config.SprinklerNeedsWater || !config.AddSprinkler)
                        {
                            waterSource.SetParent(null);
                            waterSource.Kill();
                        }
                    }
                    
                    switch (child.GetType().ToString())
                    {
                        case "StorageContainer":
                            {
                                StorageContainer theStash = child as StorageContainer;
                                if (theStash != null && theStash.name != null)
                                {
                                    if (theStash.name == "seedItem" || theStash.transform.localPosition == planterConfig.OutputStoragePos || theStash.transform.localPosition == PlanterConfig.Large.OutputStoragePos)
                                    {
                                        container = theStash;
                                        container.name = "seedItem";
                                        SpawnRefresh(theStash);

                                        if (config.BoxStorageSlots > 0)
                                            container.inventory.capacity = config.BoxStorageSlots;

                                        UpdateLocalPositionAndRotation(transform, theStash, planterConfig.OutputStoragePos, planterConfig.OutputStorageRot);

                                        if (config.AddStorageAdapter)
                                        {
                                            OutputAdapter = GetAdapter(container);
                                            if (OutputAdapter != null)
                                            {
                                                UpdateLocalPositionAndRotation(transform, OutputAdapter, planterConfig.OutputAdapterPos, planterConfig.OutputAdapterRot);
                                            }
                                            else if (container.isSpawned && config.seedStorageSlots > 0 && config.BoxStorageSlots > 0)
                                            {
                                                OutputAdapter = AddStorageAdaptor(planterBox, container, planterConfig.OutputAdapterPos, planterConfig.OutputAdapterRot);
                                                OutputAdapter.name = "NoPickUp";
                                            }
                                        }
                                    }
                                    else if (theStash.name == "seedsBox" || theStash.transform.localPosition == planterConfig.InputStoragePos || theStash.transform.localPosition == PlanterConfig.Large.InputStoragePos)
                                    {
                                        containerSeeds = theStash;
                                        containerSeeds.name = "seedsBox";
                                        SpawnRefresh(theStash);

                                        if (config.seedStorageSlots > 0)
                                            containerSeeds.inventory.capacity = config.seedStorageSlots;

                                        UpdateLocalPositionAndRotation(transform, theStash, planterConfig.InputStoragePos, planterConfig.InputStorageRot);

                                        if (config.AddStorageAdapter)
                                        {
                                            InputAdapter = GetAdapter(containerSeeds);
                                            if (InputAdapter != null)
                                            {
                                                UpdateLocalPositionAndRotation(transform, InputAdapter, planterConfig.InputAdapterPos, planterConfig.InputAdapterRot);
                                            }
                                            else if (containerSeeds.isSpawned && config.seedStorageSlots > 0 && config.BoxStorageSlots > 0)
                                            {
                                                InputAdapter = AddStorageAdaptor(planterBox, containerSeeds, planterConfig.InputAdapterPos, planterConfig.InputAdapterRot);
                                                InputAdapter.name = "NoPickUp";
                                            }
                                        }

                                        if (config.seedStorageSlots > 0 && config.BoxStorageSlots > 0)
                                            findKnife();
                                    }


                                    if (config.seedStorageSlots <= 0 || config.BoxStorageSlots <= 0)
                                    {
                                        _.NextTick(() =>
                                        {
                                            if (container != null)
                                            {
                                                //container?.SetParent(null);
                                                container?.Die();
                                            }

                                            if (OutputAdapter != null)
                                            {
                                                OutputAdapter?.SetParent(null);
                                                OutputAdapter?.Kill();
                                            }

                                            if (containerSeeds != null)
                                            {
                                               // containerSeeds?.SetParent(null);
                                                containerSeeds?.Die();
                                            }

                                            if (InputAdapter != null)
                                            {
                                                InputAdapter?.SetParent(null);
                                                InputAdapter?.Kill();
                                            }
                                        });
                                    }
                                }
                                break;
                            }
                    }
                }

                Pool.FreeUnmanaged(ref tampEntity);
                
                if (container == null && config.seedStorageSlots > 0 && config.BoxStorageSlots > 0)
                {
                    container = GameManager.server.CreateEntity("assets/prefabs/deployable/hot air balloon/subents/hab_storage.prefab", planterConfig.OutputStoragePos, planterConfig.OutputStorageRot) as StorageContainer;
                    if (container != null)
                    {
                        container.SetParent(planterBox);
                        container.panelTitle = new Translate.Phrase("seeds", "Seeds");

                        container.name = "seedItem";
                        container.OwnerID = planterBox.OwnerID;
                        container.Spawn();
                        SpawnRefresh(container);
                        container.inventory.SetFlag(ItemContainer.Flag.NoItemInput, true);

                        if (config.BoxStorageSlots > 0)
                            container.inventory.capacity = config.BoxStorageSlots;

                        if (config.AddStorageAdapter)
                        {
                            OutputAdapter = AddStorageAdaptor(planterBox, container, planterConfig.OutputAdapterPos, planterConfig.OutputAdapterRot);
                            if (OutputAdapter != null)
                            {
                                OutputAdapter.name = "NoPickUp";
                            }
                        }
                    }
                }

                if (containerSeeds == null && config.seedStorageSlots > 0 && config.BoxStorageSlots > 0)
                {
                    containerSeeds = GameManager.server.CreateEntity("assets/prefabs/deployable/hot air balloon/subents/hab_storage.prefab", planterConfig.InputStoragePos, planterConfig.InputStorageRot) as StorageContainer;
                    if (containerSeeds != null)
                    {
                        containerSeeds.SetParent(planterBox);

                        containerSeeds.name = "seedsBox";
                        containerSeeds.OwnerID = planterBox.OwnerID;
                        containerSeeds.Spawn();
                        SpawnRefresh(containerSeeds);

                        if (config.seedStorageSlots > 0)
                            containerSeeds.inventory.capacity = config.seedStorageSlots;

                        if (config.AddStorageAdapter)
                        {
                            InputAdapter = AddStorageAdaptor(planterBox, containerSeeds, planterConfig.InputAdapterPos, planterConfig.InputAdapterRot);
                            if (InputAdapter != null)
                            {
                                InputAdapter.name = "NoPickUp";
                            }
                        }
                    }
                }

                if (config.SprinklerNeedsWater && waterSource == null && config.AddSprinkler)
                {
                    Vector3 pos = planterBox.transform.TransformPoint(planterConfig.SplitterPos);
                    waterSource = GameManager.server.CreateEntity("assets/prefabs/deployable/playerioents/fluidsplitter/fluidsplitter.prefab", pos, planterBox.transform.rotation) as Splitter;
                    if (waterSource != null)
                    {
                        SpawnRefresh(waterSource);
                        waterSource.OwnerID = planterBox.OwnerID;
                        waterSource.Spawn();
                        waterSource.SetParent(planterBox, true);
                        UpdateLocalPositionAndRotation(transform, waterSource, planterConfig.SplitterPos, planterConfig.SplitterRot);
                    }
                }

                if (waterSource != null)
                {
                    waterSource.name = $"NoPickUp{planterBox.net.ID.Value}";
                    SpawnRefresh(waterSource);
                    IOEntity.IOSlot ioOutput = waterSource.outputs[0];
                    ioOutput.type = IOEntity.IOType.Generic;
                    waterSource.SendNetworkUpdate();
                }

                if (sprinkler == null && config.AddSprinkler)
                {
                    sprinkler = GameManager.server.CreateEntity("assets/prefabs/deployable/playerioents/sprinkler/electric.sprinkler.deployed.prefab", transform.TransformPoint(planterConfig.SprinklerPos), transform.rotation) as Sprinkler;
                    if (sprinkler != null)
                    {
                        sprinkler.OwnerID = planterBox.OwnerID;
                        sprinkler.Spawn();
                        sprinkler.SetParent(planterBox, true);
                        UpdateLocalPositionAndRotation(transform, sprinkler, planterConfig.SprinklerPos, Quaternion.identity);
                        sprinkler.DecayPerSplash = 0f;
                        //sprinkler.ConsumptionAmount();
                        SpawnRefresh(sprinkler);
                    }
                }

                if (sprinkler != null)
                {
                    sprinkler.DecayPerSplash = 0f;
                    SpawnRefresh(sprinkler);
                    if (!config.SprinklerNeedsWater)
                        InvokeRepeating("WaterPlants", 10, 30);
                    else if (waterSource != null && sprinkler != null && !sprinkler.IsConnectedToAnySlot(waterSource, 0, 3))
                    {
                        _.NextTick(() => connectWater(waterSource, sprinkler));
                    }
                    sprinkler.name = $"NoPickUp{planterBox.net.ID.Value}";
                }

                if (config.AddStorageAdapter && !fertilizerBoxAdapter)
                {
                    FertilizerAdapter = AddStorageAdaptor(planterBox, planterBox, planterConfig.FertilizerAdapterPos, planterConfig.FertilizerAdapterRot);
                    FertilizerAdapter.name = "NoPickUp";
                }
                itemDefinition = ItemManager.FindItemDefinition("water");

                setTotalAvailableSlots();

                if (config.seedStorageSlots > 0 && config.BoxStorageSlots > 0)
                {
                    cropPlants();
                }
            }

            private void connectWater(IOEntity entity, IOEntity entity1)
            {
                if (entity == null || entity1 == null) return;

                entity1.ClearConnections();
                _.NextTick(() =>
                {
                    if (entity == null || entity1 == null) return;
                    IOEntity.IOSlot ioOutput = entity.outputs[0];
                    if (ioOutput != null)
                    {
                        ioOutput.connectedTo = new IOEntity.IORef();
                        ioOutput.connectedTo.Set(entity1);
                        ioOutput.connectedToSlot = 0;
                        ioOutput.connectedTo.Init();

                        entity1.inputs[0].connectedTo = new IOEntity.IORef();
                        entity1.inputs[0].connectedTo.Set(entity);
                        entity1.inputs[0].connectedToSlot = 0;
                        entity1.inputs[0].connectedTo.Init();
                        entity.SendNetworkUpdateImmediate();
                        entity1.SendNetworkUpdateImmediate();
                    }
                });
            }

            private void WaterPlants()
            {
                if (sprinkler == null) return;

                if (sprinkler.IsOn() && !this.IsInvoking(new Action(DoSplash)))
                {
                    sprinkler.TurnOff();
                    this.CancelInvoke(new Action(DoSplash));
                    return;
                }

                if (planterBox.soilSaturation <= soilSaturationON && !sprinkler.IsOn())
                {
                    sprinkler.SetFuelType(itemDefinition, null);
                    sprinkler.SetFlag(BaseEntity.Flags.On, true);
                    sprinkler.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                    sprinkler.SendNetworkUpdateImmediate();

                    this.forceUpdateSplashables = true;

                    this.InvokeRandomized(new Action(DoSplash), sprinkler.SplashFrequency * 0.5f, sprinkler.SplashFrequency, sprinkler.SplashFrequency * 0.2f);
                }
                else if (sprinkler.IsOn() && planterBox.soilSaturation >= soilSaturationOFF)
                {
                    sprinkler.TurnOff();
                    if (this.IsInvoking(new Action(DoSplash)))
                        this.CancelInvoke(new Action(DoSplash));
                }
            }

            private void cropPlants()
            {
                if (container == null || currentState)
                    return;

                List<BaseEntity> tampEntity = Pool.Get<List<BaseEntity>>();
                tampEntity.AddRange(planterBox.children);
                foreach (BaseEntity child in tampEntity)
                {
                    if (child != null && child is GrowableEntity)
                    {
                        if (getAlwaysLight || getAlwaysTemp)
                            child.GetOrAddComponent<plantBoxBehavior>().SetUp(getAlwaysLight, getAlwaysTemp);

                        seeIfCanPick((child as GrowableEntity));
                    }
                }
                Facepunch.Pool.FreeUnmanaged<BaseEntity>(ref tampEntity);
            }

            public bool CanClone(GrowableEntity growableEntity)
            {
                return (double)growableEntity.currentStage.resources > 0.0 && (UnityEngine.Object)growableEntity.Properties.CloneItem != (UnityEngine.Object)null;
            }

            public void seeIfCanPick(GrowableEntity growableEntity)
            {
                if (growableEntity == null || container == null || currentState) return;

                string sName = growableEntity.ShortPrefabName.Replace(".entity", "", StringComparison.OrdinalIgnoreCase).Trim();
                if (config.enableCloning && cloningItem != null && config.cloneList.ContainsKey(sName))
                {
                    if (growableEntity.skinID != 0UL)
                    {

                    }
                    else if (CanClone(growableEntity) && growableEntity.State.ToString().ToLower().Contains(config.cloneList[sName].ToLower()))
                    {
                        CloneCrop(growableEntity);
                        return;
                    }
                }
                if (growableEntity.State == PlantProperties.State.Ripe || growableEntity.State == PlantProperties.State.Dying)
                {
                    pickCrop(growableEntity);
                }
                else autoFill();

                planterBox.SendNetworkUpdate();
                //growableEntity.ChangeState(PlantProperties.State.Ripe, true, false); //test add
            }

            public bool findKnife()
            {
                if (containerSeeds == null)
                    return false;

                foreach (var item in containerSeeds.inventory.itemList)
                {
                    if (config.knifeList.ContainsKey(item.info.itemid))
                    {
                        cloningItem = item;
                        return true;
                    }                  
                }

                cloningItem = null;
                return false;
            }

            private void CloneCrop(GrowableEntity growableEntity)
            {
                if (growableEntity == null || container == null || cloningItem == null || !config.knifeList.ContainsKey(cloningItem.info.itemid))
                    return;

                int iAmount = growableEntity.Properties.BaseCloneCount + growableEntity.Genes.GetGeneTypeCount(GrowableGenetics.GeneType.Yield) / 2;

                if (cloningItem != null && config.knifeList[cloningItem.info.itemid] > 1)
                {
                    iAmount = iAmount * config.knifeList[cloningItem.info.itemid];
                }

                if (iAmount <= 0)
                    return;
                Item targetItem = ItemManager.Create(growableEntity.Properties.CloneItem, iAmount, growableEntity.skinID);
                GrowableGeneEncoding.EncodeGenesToItem(growableEntity, targetItem);

                Item itemEntity = growableEntity.GetItem();

                if (Interface.CallHook("OnAutoFarmCutting", growableEntity, targetItem, container) == null)
                {
                    if (itemEntity != null && itemEntity.skin != 0UL)
                    {
                        BasePlayer player = BasePlayer.Find(planterBox.OwnerID.ToString());
                        if (growableEntity is CollectibleEntity)
                        {
                            Interface.CallHook("OnCollectiblePickup", targetItem, player, growableEntity);
                        }
                        else
                        {
                            Interface.CallHook("OnGrowableGathered", growableEntity, targetItem, player);
                        }
                    }
                }
                if (growableEntity != null)
                {
                    if (!targetItem.MoveToContainer(container.inventory))
                    {
                        Vector3 velocity = Vector3.zero;
                        targetItem.Drop(growableEntity.transform.position + new Vector3(0f, 2f, 1.5f), velocity);
                    }

                    if (growableEntity.Properties.pickEffect.isValid)
                        Effect.server.Run(growableEntity.Properties.pickEffect.resourcePath, growableEntity.transform.position, Vector3.up);

                    if (!growableEntity.IsDestroyed) { growableEntity.Kill(); }

                }
                _.NextTick(() => { autoFill(); });
            }

            private void pickCrop(GrowableEntity growableEntity)
            {
                if (config.seedStorageSlots <= 0 || config.BoxStorageSlots <= 0)
                    return;

                if (containerSeeds == null || container == null) generateStorage();
                int amount = growableEntity.CurrentPickAmount;
                if (config.seedsAllowedAndMultiplier.ContainsKey(growableEntity.Properties.SeedItem.itemid))
                    amount = growableEntity.CurrentPickAmount * config.seedsAllowedAndMultiplier[growableEntity.Properties.SeedItem.itemid];
                if (amount <= 0) return;

                Item obj = ItemManager.Create(growableEntity.Properties.pickupItem, amount, growableEntity.skinID);

                if (obj != null)
                {
                    Item itemEntity = growableEntity.GetItem();

                    if (Interface.CallHook("OnAutoFarmGathered", growableEntity, obj, container) == null)
                    {
                        if (_ != null && _.Ganja != null && _.configData.settings.GanjaPluginEnable)
                        {
                            if (growableEntity.prefabID == 3006540952)
                                Interface.CallHook("OnAutoFarmGather", planterBox.OwnerID.ToString(), container, growableEntity.transform.position, true, growableEntity);
                            else if (growableEntity.prefabID == 3587624038)
                                Interface.CallHook("OnAutoFarmGather", planterBox.OwnerID.ToString(), container, growableEntity.transform.position, false, growableEntity);
                        }

                        if (itemEntity != null && itemEntity.skin != 0UL)
                        {
                            BasePlayer player = BasePlayer.Find(planterBox.OwnerID.ToString());
                            if (growableEntity is CollectibleEntity)
                            {
                                Interface.CallHook("OnCollectiblePickup", obj, player, growableEntity);
                            }
                            else
                            {
                                Interface.CallHook("OnGrowableGathered", growableEntity, obj, player);
                            }
                        }
                        else if (_.configData.settings.CallHookOnCollectiblePickup && planterBox != null && planterBox.OwnerID != 0UL)
                        {
                            BasePlayer player = BasePlayer.Find(planterBox.OwnerID.ToString());
                            if (player != null)
                            {
                                if (growableEntity is CollectibleEntity)
                                {
                                    Interface.CallHook("OnCollectiblePickup", obj, player, growableEntity);
                                }
                                else
                                {
                                    Interface.CallHook("OnGrowableGathered", growableEntity, obj, player);
                                }
                            }
                        }
                    }
                    if (growableEntity != null)
                    {
                        if (!obj.MoveToContainer(container.inventory))
                        {
                            Vector3 velocity = Vector3.zero;
                            obj.Drop(growableEntity.transform.position + new Vector3(0f, 2f, 1.5f), velocity);
                        }

                        if (growableEntity.Properties.pickEffect.isValid)
                            Effect.server.Run(growableEntity.Properties.pickEffect.resourcePath, growableEntity.transform.position, Vector3.up);

                        if (growableEntity != null && !growableEntity.IsDestroyed) { growableEntity.Kill(); }
                    }
                    _.NextTick(() => { autoFill(); });
                }
            }

            public void autoFill()
            {
                if (config.seedStorageSlots <= 0 || config.BoxStorageSlots <= 0)
                    return;

                if (currentState)
                    return;

                int totalOpen = totalslotsAvailable - planterBox.children.Count;
                if (totalOpen > 0) isPlanterFull();
            }

            private bool checkSpawnPoint(Vector3 position, float size)
            {
                if (position == null)
                    position = new Vector3(4.8654f, 2.6241f, 1.3869f);
                List<GrowableEntity> nearby = new List<GrowableEntity>();
                Vis.Entities<GrowableEntity>(position, size, nearby);
                if (nearby.Distinct().Count() > 0)
                    return true;
                return false;
            }

            private void isPlanterFull()
            {

                if (config.seedStorageSlots <= 0 || config.BoxStorageSlots <= 0 || nextAutofiltime > UnityEngine.Time.realtimeSinceStartup)
                    return;

                if (containerSeeds == null || container == null)
                    generateStorage();

                if (planterBox == null || planterBox.IsDestroyed)
                    return;

                int freePlacement = totalslotsAvailable - planterBox.children.Count;

                if (freePlacement > 0)
                {
                    planterBox.artificialLightExposure.Get(true);
                    planterBox.plantArtificalTemperature.Get(true);

                    for (int slot1 = 0; slot1 < containerSeeds.inventory.capacity; ++slot1)
                    {
                        int totalPlacement = 0;
                        Item slot2 = containerSeeds.inventory.GetSlot(slot1);
                        if (slot2 != null && config.seedsAllowedAndMultiplier.ContainsKey(slot2.info.itemid))
                        {
                            int amountToConsume = slot2.amount;
                            if (amountToConsume > 0)
                            {
                                if (freePlacement < amountToConsume)
                                    totalPlacement = freePlacement;
                                else totalPlacement = amountToConsume;
                                if (totalPlacement > 0)
                                    fillPlanter(slot2.info.itemid, totalPlacement, slot2);
                            }
                        }
                    }
                }

                nextAutofiltime = UnityEngine.Time.realtimeSinceStartup + 1f;
            }

            private void fillPlanter(int theID, int amount, Item item)
            {
                if (planterBox.links != null)
                {
                    var deployablePrefab = item.info.GetComponent<ItemModDeployable>()?.entityPrefab?.resourcePath;
                    if (string.IsNullOrEmpty(deployablePrefab)) return;

                    foreach (EntityLink socketBase in planterBox.links)
                    {
                        Socket_Base baseSocket = socketBase.socket;
                        if (baseSocket != null)
                        {
                            if (!baseSocket.female || planterBox.IsOccupied(baseSocket.socketName) || !IsFree(planterBox.transform.TransformPoint(baseSocket.worldPosition)))
                                continue;

                            GrowableEntity growable = GameManager.server.CreateEntity(deployablePrefab, planterBox.transform.position, Quaternion.identity) as GrowableEntity;
                            if (growable != null)
                            {
                                growable.skinID = item.skin;
                                var idata = item?.instanceData;

                                growable.SetParent(planterBox, true);
                                growable.Spawn();

                                Item itemEntity = growable.GetItem();
                                if (itemEntity != null && item.skin != 0UL)
                                {
                                    itemEntity.skin = item.skin;
                                }

                                if (idata != null)
                                    growable.ReceiveInstanceData(idata);

                                growable.transform.localPosition = baseSocket.worldPosition;

                                planterBox.SendNetworkUpdateImmediate();
                                planterBox.SendChildrenNetworkUpdateImmediate();
                                amount--;
                                item?.UseItem(1);

                                Effect.server.Run("assets/prefabs/plants/plantseed.effect.prefab", planterBox.transform.TransformPoint(baseSocket.worldPosition), planterBox.transform.up);

                                if (itemEntity != null)
                                {
                                    Planner planer = itemEntity.GetHeldEntity() as Planner;
                                    if (planer != null)
                                    {
                                        Interface.CallHook("OnEntityBuilt", planer, planer.gameObject);
                                    }
                                }

                                if (getAlwaysLight || getAlwaysTemp)
                                    growable.GetOrAddComponent<plantBoxBehavior>().SetUp(getAlwaysLight, getAlwaysTemp);

                                if (amount <= 0)
                                    break;
                            }
                        }
                    }
                }
            }

            public bool IsFree(Vector3 position)
            {
                float distance = 0.1f;
                List<GrowableEntity> list = new List<GrowableEntity>();
                Vis.Entities<GrowableEntity>(position, distance, list);
                return list.Count <= 0;
            }

            private HashSet<ISplashable> cachedSplashables = new HashSet<ISplashable>();
            private TimeSince updateSplashableCache;
            private bool forceUpdateSplashables;

            private void DoSplash()
            {
                if (sprinkler == null)
                    return;

                using (TimeWarning.New("SprinklerSplash"))
                {
                    int waterPerSplash = sprinkler.WaterPerSplash;
                    if ((double)(float)this.updateSplashableCache > (double)sprinkler.SplashFrequency * 4.0 || this.forceUpdateSplashables)
                    {
                        this.cachedSplashables.Clear();
                        this.forceUpdateSplashables = false;
                        this.updateSplashableCache = (TimeSince)0.0f;
                        Vector3 position = sprinkler.Eyes.position;
                        Vector3 up = sprinkler.transform.up;
                        float num = ConVar.Server.sprinklerEyeHeightOffset * Mathf.Clamp(Vector3.Angle(up, Vector3.up) / 180f, 0.2f, 1f);
                        Vector3 startPosition = position + up * (splashRadius * 0.5f);
                        Vector3 vector3 = position + up * num;
                        List<BaseEntity> list1 = Facepunch.Pool.Get<List<BaseEntity>>();
                        Vector3 endPosition = vector3;
                        double sprinklerRadius = (double)splashRadius;
                        List<BaseEntity> list2 = list1;
                        Vis.Entities<BaseEntity>(startPosition, endPosition, (float)sprinklerRadius, list2, 1236478737);
                        if (list1.Count > 0)
                        {
                            foreach (BaseEntity baseEntity in list1)
                            {
                                ISplashable splashable6 = null;
                                IOEntity entity6 = null;

                                if (baseEntity is IOEntity)
                                    entity6 = baseEntity as IOEntity;

                              if (!baseEntity.isClient && baseEntity is ISplashable)
                                    splashable6 = baseEntity as ISplashable;

                                if (splashable6 != null && (!this.cachedSplashables.Contains(splashable6) && splashable6.WantsSplash((waterSource != null ? sprinkler.currentFuelType : itemDefinition), waterPerSplash)) && (baseEntity.IsVisible(position) && (!(baseEntity is IOEntity) || entity6 != null && !sprinkler.IsConnectedTo(entity6, IOEntity.backtracking)))) this.cachedSplashables.Add(splashable6);
                            }
                        }
                        Facepunch.Pool.FreeUnmanaged<BaseEntity>(ref list1);
                    }
                    if (this.cachedSplashables.Count > 0)
                    {
                        int amount = waterPerSplash / this.cachedSplashables.Count;
                        foreach (ISplashable cachedSplashable in this.cachedSplashables)
                        {
                            if (!cachedSplashable.IsUnityNull<ISplashable>() && cachedSplashable.WantsSplash(sprinkler.currentFuelType, amount))
                            {
                                int num = cachedSplashable.DoSplash((waterSource != null ? sprinkler.currentFuelType : itemDefinition), amount);
                                waterPerSplash -= num;
                                if (waterPerSplash <= 0)
                                    break;
                            }
                        }
                    }
                }
                Interface.CallHook("OnSprinklerSplashed", (object)sprinkler);
            }

            public void OnDestroy()
            {
                if (_.playerTotals.ContainsKey(ownerplayer))
                    _.playerTotals[ownerplayer]--;

                CancelInvoke("WaterPlants");
                CancelInvoke("isPlanterFull");
                if (sprinkler != null && !config.SprinklerNeedsWater) sprinkler.TurnOff();
                if (planterBox == null || planterBox.IsDestroyed)
                {
                    if (sprinkler != null && !sprinkler.IsDestroyed) sprinkler.Kill();
                    if (waterSource != null && !waterSource.IsDestroyed) waterSource.Kill();
                }

                if (getAlwaysTemp || getAlwaysLight)
                {
                    List<BaseEntity> tampEntity = Pool.Get<List<BaseEntity>>();
                    tampEntity.AddRange(planterBox.children);
                    foreach (BaseEntity child in tampEntity)
                    {
                        if (child != null && child is GrowableEntity)
                           UnityEngine.Object.DestroyImmediate(child?.GetComponent<plantBoxBehavior>());

                    }
                    Facepunch.Pool.FreeUnmanaged<BaseEntity>(ref tampEntity);
                }
            }

            public void RemoveEntitys()
            {
                CancelInvoke("WaterPlants");
                CancelInvoke("isPlanterFull");
                if (sprinkler != null && !sprinkler.IsDestroyed) sprinkler.Kill();
                if (waterSource != null && !waterSource.IsDestroyed) waterSource.Kill();
            }
        }
        #endregion

        #region PlantHelper
        class plantBoxBehavior : FacepunchBehaviour
        {
            private GrowableEntity plant { get; set; }
            public float LightQuality { get; private set; } = 1f;
            public float TemperatureQuality { get; private set; } = 1f;
            public bool AlwaysLight { get; private set; }
            public bool AlwaysTemp { get; private set; }
            public bool Active { get; private set; } = false;

            private void Awake()
            {
                plant = this.GetComponent<GrowableEntity>();
            }

            public void SetUp(bool l, bool t)
            {
                if (Active) return;
                Active = true;
                AlwaysLight = l;
                AlwaysTemp = t;
                plant.Invoke(new Action(InitPlant), 2f);
            }

            private void InitPlant()
            {
                plant.CancelInvoke();
                FixPlanter();

                plant.InvokeRandomized(new Action(this.RunUpdate), GrowableEntity.ThinkDeltaTime, GrowableEntity.ThinkDeltaTime, GrowableEntity.ThinkDeltaTime * 0.1f);
                this.InvokeRepeating(new Action(this.PlantTick), 29, 30);
                this.RunUpdate();
                PlantTick();
            }

            private void FixPlanter()
            {
                if (AlwaysLight)
                {
                    plant.artificialLightExposure = new TimeCachedValue<float>()
                    {
                        refreshCooldown = 60f,
                        refreshRandomRange = 5f,
                        updateValue = new Func<float>(this.CalculateArtificialLightExposure)
                    };
                }
                else
                {
                    plant.artificialLightExposure = new TimeCachedValue<float>()
                    {
                        refreshCooldown = 60f,
                        refreshRandomRange = 5f,
                        updateValue = new Func<float>(plant.CalculateArtificialLightExposure)
                    };
                }

                if (AlwaysTemp)
                {
                    plant.artificialTemperatureExposure = new TimeCachedValue<float>()
                    {
                        refreshCooldown = 60f,
                        refreshRandomRange = 5f,
                        updateValue = new Func<float>(this.CalculateArtificialTemperature)
                    };
                }
                else
                {
                    plant.artificialTemperatureExposure = new TimeCachedValue<float>()
                    {
                        refreshCooldown = 60f,
                        refreshRandomRange = 5f,
                        updateValue = new Func<float>(plant.CalculateArtificialTemperature)
                    };
                }
            }

            private float CalculateArtificialLightExposure() => 1f;
            private float CalculateArtificialTemperature() => 1f;

            public void PlantTick()
            {
                if (_ == null)
                {
                    UnityEngine.Object.DestroyImmediate(this);
                    return;
                }

                if (plant == null)
                    plant = this.GetComponent<GrowableEntity>();

                if (AlwaysLight)
                    plant.LightQuality = 1f;
                if (AlwaysTemp)
                    plant.TemperatureQuality = 1f;
                plant.SendNetworkUpdate();
            }

            public void RunUpdate()
            {
                if (plant.IsDead())
                    return;

                plant.CalculateQualities(false);
                float overallQuality = this.CalculateOverallQuality();
                float actualStageAgeIncrease = plant.UpdateAge(overallQuality);
                plant.UpdateHealthAndYield(overallQuality, actualStageAgeIncrease);
                if ((double)plant.health <= 0.0)
                {
                    this.TellPlanter();
                    plant.Die();
                }
                else
                {
                    int num = (int)plant.UpdateState();
                    plant.ConsumeWater();
                    plant.SendNetworkUpdate();
                }
            }

            private void TellPlanter()
            {
                BaseEntity parentEntity = plant.GetParentEntity();
                if (!((UnityEngine.Object)parentEntity != (UnityEngine.Object)null) || !(parentEntity is PlanterBox planterBox))
                    return;
                planterBox.OnPlantRemoved(plant, (BasePlayer)null);
            }

            public float CalculateOverallQuality()
            {
                float a = 1f;
                plant.OverallQuality = !ConVar.Server.useMinimumPlantCondition ? (AlwaysLight ? this.LightQuality : plant.LightQuality) * plant.WaterQuality * plant.GroundQuality * (AlwaysTemp ? this.TemperatureQuality : plant.TemperatureQuality) : Mathf.Min(Mathf.Min(Mathf.Min(Mathf.Min(a, (AlwaysLight ? this.LightQuality : plant.LightQuality)), plant.WaterQuality), plant.GroundQuality), (AlwaysTemp ? this.TemperatureQuality : plant.TemperatureQuality));
                return plant.OverallQuality;
            }

            public void CalculateQualities(bool firstTime, bool forceArtificialLightUpdates = true, bool forceArtificialTemperatureUpdates = true)
            {
                if (plant.IsDead())
                    return;

                if (plant.sunExposure == null)
                    plant.sunExposure = new TimeCachedValue<float>()
                    {
                        refreshCooldown = 30f,
                        refreshRandomRange = 5f,
                        updateValue = new Func<float>(plant.SunRaycast)
                    };

                if (plant.artificialLightExposure == null)
                    FixPlanter();

                if (plant.artificialTemperatureExposure == null)
                    FixPlanter();

                if (forceArtificialTemperatureUpdates)
                    plant.artificialTemperatureExposure.ForceNextRun();

                if (AlwaysLight)
                    plant.LightQuality = 1;
                else
                    plant.CalculateLightQuality(forceArtificialLightUpdates | firstTime);

                plant.CalculateWaterQuality();
                plant.CalculateWaterConsumption();
                plant.CalculateGroundQuality(firstTime);
                if (AlwaysTemp)
                    plant.TemperatureQuality = 1f;
                else
                    plant.CalculateTemperatureQuality();
                double overallQuality = (double)this.CalculateOverallQuality();
            }

            private void OnDestroy()
            {
                if (plant != null && !plant.IsDestroyed)
                {
                    plant.CancelInvoke(this.RunUpdate);
                    if (AlwaysLight)
                        plant.sunExposure = null;

                    if (AlwaysTemp)
                        plant.artificialTemperatureExposure = null;

                    plant.InvokeRandomized(new Action(plant.RunUpdate), GrowableEntity.ThinkDeltaTime, GrowableEntity.ThinkDeltaTime, GrowableEntity.ThinkDeltaTime * 0.1f);
                }
            }
        }

        #region Patch
        [AutoPatch]
        [HarmonyPatch(typeof(GrowableEntity), nameof(GrowableEntity.CalculateQualities))]
        internal class PatchGrowable
        {
            [HarmonyPrefix]
            internal static bool Prefix(GrowableEntity __instance, bool firstTime, bool forceArtificialLightUpdates, bool forceArtificialTemperatureUpdates)
            {
                if ((object)__instance == null || !__instance.TryGetComponent<plantBoxBehavior>(out var boxBehav))
                    return true;

                boxBehav.CalculateQualities(firstTime, forceArtificialLightUpdates, forceArtificialTemperatureUpdates);
                return false;
            }
        }
        #endregion
        #endregion

        #region Hooks
        [ChatCommand("autofarm")]
        private void theCommand(BasePlayer player, string cmd, string[] args)
        {
            foreach (var key in configData.vipInformation)
            {
                if (!configData.settings.DisablePlacementByDefault)
                {
                    if (permission.UserHasPermission(player.UserIDString, key.Key))
                    {
                        if (disabledFarmPlayer.ContainsKey(player.userID))
                        {
                            disabledFarmPlayer.Remove(player.userID);
                            SendReply(player, lang.GetMessage("enabled", this, player.UserIDString));
                            return;
                        }
                        else
                        {
                            disabledFarmPlayer.Add(player.userID, true);
                            SendReply(player, lang.GetMessage("disabled", this, player.UserIDString));
                            return;
                        }
                    }
                }
                else if (configData.settings.DisablePlacementByDefault)
                {
                    if (permission.UserHasPermission(player.UserIDString, key.Key))
                    {
                        if (disabledFarmPlayer.ContainsKey(player.userID))
                        {
                            disabledFarmPlayer.Remove(player.userID);
                            SendReply(player, lang.GetMessage("disabled", this, player.UserIDString));
                            return;
                        }
                        else
                        {
                            disabledFarmPlayer.Add(player.userID, true);
                            SendReply(player, lang.GetMessage("enabled", this, player.UserIDString));
                            return;
                        }
                    }
                }
            }

            SendReply(player, lang.GetMessage("NoPerms", this, player.UserIDString));
        }

        private void OnEntityBuilt(Planner plan, GameObject gameObject)
        {
            GrowableEntity plant = gameObject.GetComponent<GrowableEntity>();
            if (plant != null)
            {
                NextTick(() =>
                {
                    var planterBox = plant?.planter?.GetComponent<planterBoxBehavior>();
                    if (planterBox != null)
                    {
                        planterBox.SetPlant(plant);
                    }
                });
                return;
            }
            var player = plan?.GetOwnerPlayer();
            PlanterBox planterBox = gameObject?.GetComponent<PlanterBox>();
            if (planterBox == null || player == null || planterBox.ShortPrefabName.Contains("plantpot"))
                return;

            if (Interface.CallHook("UserHasAutoFarmProPermission", player.UserIDString) != null)
                return;

            if (!configData.settings.DisablePlacementByDefault && disabledFarmPlayer.ContainsKey(player.userID))
                return;
            else if (configData.settings.DisablePlacementByDefault && !disabledFarmPlayer.ContainsKey(player.userID))
                return;

            int totals = 0;
            int playertotals = 0;
            string configPerm = "";
            foreach (var key in configData.vipInformation)
            {
                if (permission.UserHasPermission(player.UserIDString, key.Key))
                {
                    totals = key.Value.totalFarms;
                    configPerm = key.Key;
                }
            }

            if (playerTotals.ContainsKey(planterBox.OwnerID))
                playertotals = playerTotals[player.userID];
            if (playertotals < totals)
            {
                planterBoxBehavior cropStorage = planterBox.gameObject.AddComponent<planterBoxBehavior>();
                cropStorage.config = configData.vipInformation[configPerm];

                pcdData.Planters.Add(planterBox.net.ID.Value, new PCDInfo() { configPerm = configPerm });
                SaveData();

                if (!GametipShown.Contains(player.userID))
                {
                    GameTips(player, lang.GetMessage("rotatePlanter", this, player.UserIDString));
                    GametipShown.Add(player.userID);
                }
            }
            else if (player != null && totals > 0) SendReply(player, lang.GetMessage("max", this, player.UserIDString), totals);
        }

        private void OnLootEntityEnd(BasePlayer player, StorageContainer stash)
        {
            planterBoxBehavior planterBox = stash?.GetParentEntity()?.GetComponentInParent<planterBoxBehavior>();
            if (planterBox != null)
            {
                planterBox.autoFill();
                if (planterBox.config.enableCloning)
                    planterBox.findKnife();
            }
            DestroyCui(player);
        }

        private void OnLootEntity(BasePlayer player, StorageContainer stash)
        {
            planterBoxBehavior planterBox = stash?.GetParentEntity()?.GetComponentInParent<planterBoxBehavior>();
            if (planterBox != null)
            {
                if (!planterBoxBehavior._AllLootingPlayers.ContainsKey(player.userID))
                    planterBoxBehavior._AllLootingPlayers.Add(player.userID, planterBox);
                else
                    planterBoxBehavior._AllLootingPlayers[player.userID] = planterBox;

                BuildCuiScreen(player, planterBox.currentState);
            }
        }

        private object OnEntityTakeDamage(StorageContainer stash, HitInfo hitInfo)
        {
            var planterBox = stash?.GetParentEntity()?.GetComponentInParent<PlanterBox>();
            if (planterBox != null && planterBox.GetComponent<planterBoxBehavior>() != null)
            {
                hitInfo.damageTypes.ScaleAll(0);
                return true;
            }

            return null;
        }

        void CanTakeCutting(BasePlayer player, GrowableEntity entity)
        {
            if (entity.GetPlanter() != null)
            {
                PlanterBox planter = entity.GetPlanter();
                NextTick(() => { planter?.GetComponent<planterBoxBehavior>()?.autoFill(); });
            }
        }

        private void OnGrowableGather(GrowableEntity entity, BasePlayer player)
        {
            if (entity.GetPlanter() != null)
            {
                PlanterBox planter = entity.GetPlanter();
                NextTick(() => { planter?.GetComponent<planterBoxBehavior>()?.autoFill(); });
            }
        }

        private void OnCollectiblePickup(CollectibleEntity entity, BasePlayer player)
        {
            if (entity.GetComponentInParent<PlanterBox>() != null)
            {
                PlanterBox planter = entity.GetComponentInParent<PlanterBox>();
                NextTick(() => { planter?.GetComponent<planterBoxBehavior>()?.autoFill(); });
            }
        }

        ItemContainer.CanAcceptResult? CanAcceptItem(ItemContainer container, Item item, int targetPos)
        {
            if (container.entityOwner == null) return null;
            if (container.entityOwner is StorageContainer)
            {
                if (container.entityOwner.name != null && container.entityOwner.name != "seedsBox") return null;
                planterBoxBehavior planterBox = container.entityOwner?.GetComponentInParent<PlanterBox>()?.GetComponentInParent<planterBoxBehavior>();
                if (planterBox != null && container.entityOwner.name == "seedsBox")
                {
                    if (planterBox.config.enableCloning && planterBox.config.knifeList.ContainsKey(item.info.itemid))
                    {
                        if (planterBox.findKnife())
                            return ItemContainer.CanAcceptResult.CannotAccept;

                        return null;
                    }

                    if (!planterBox.config.seedsAllowedAndMultiplier.ContainsKey(item.info.itemid))
                        return ItemContainer.CanAcceptResult.CannotAccept;
                }
            }
            return null;
        }

        private object CanPickupEntity(BasePlayer player, Sprinkler entity)
        {
            if (entity == null) return null;
            if (entity != null && entity.name != null && entity.name.Contains("NoPickUp"))
            {
                string stringID = entity.name.Replace("NoPickUp", "");
                if (!string.IsNullOrEmpty(stringID))
                {
                    BaseNetworkable planter = FindEntity(Convert.ToUInt64(stringID));
                    planter?.GetComponent<planterBoxBehavior>()?.pickUpPlanter(player);
                }
                return false;
            }
            return null;
        }

        private object CanPickupEntity(BasePlayer player, PlanterBox planter)
        {
            if (planter != null && planter.GetComponent<planterBoxBehavior>() != null)
            {
                planter.GetComponent<planterBoxBehavior>().pickUpPlanter(player);
                return false;
            }
            return null;
        }

        private object CanPickupEntity(BasePlayer player, Splitter entity)
        {
            if (entity == null) return null;
            if (entity != null && entity.name != null && entity.name.Contains("NoPickUp"))
            {
                string stringID = entity.name.Replace("NoPickUp", "");
                if (!string.IsNullOrEmpty(stringID))
                {
                    BaseNetworkable planter = FindEntity(Convert.ToUInt64(stringID));
                    planter?.GetComponent<planterBoxBehavior>()?.pickUpPlanter(player);
                }
                return false;
            }
            return null;
        }

        private object CanPickupEntity(BasePlayer player, IndustrialStorageAdaptor entity)
        {
            if (entity == null) return null;
            if (entity != null && entity.name != null && entity.name.Contains("NoPickUp"))
            {
                entity.GetComponentInParent<planterBoxBehavior>()?.pickUpPlanter(player);
                return false;
            }
            return null;
        }

        private object OnWireClear(BasePlayer player, IOEntity entity1, int connected, IOEntity entity2, bool flag)
        {
            if (entity1 != null && entity1 is Splitter && entity1.name != null && entity1.name.Contains("NoPickUp") && entity2 != null && entity2 is Sprinkler && entity2.name != null && entity2.name.Contains("NoPickUp"))
            {
                return false;
            }
            return null;
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            entity?.GetComponent<PlanterBox>()?.GetComponent<planterBoxBehavior>()?.RemoveEntitys();
        }

        private void OnGrowableStateChange(GrowableEntity growableEntity, PlantProperties.State state)
        {
            _.NextTick(() =>
            {
                if (growableEntity != null && growableEntity.planter != null)
                {
                    planterBoxBehavior behavior = growableEntity.planter.GetComponent<planterBoxBehavior>();
                    if (behavior != null)
                    {
                        behavior.seeIfCanPick(growableEntity);
                    }
                }
            });
        }

        private void OnHammerHit(BasePlayer player, HitInfo info)
        {
            PlanterBox planterBox = info?.HitEntity as PlanterBox;
            if (planterBox != null)
            {
                planterBoxBehavior behavior = planterBox.GetComponent<planterBoxBehavior>();
                if (behavior == null) return;

                if (behavior.ownerplayer == player.userID && planterBox.health == planterBox.MaxHealth())
                    behavior.Rotate();
            }
        }

        public void GameTips(BasePlayer player, string tipsShow)
        {
            if (player != null && player.userID.IsSteamId())
            {
                player?.SendConsoleCommand("gametip.hidegametip");
                player?.SendConsoleCommand("gametip.showgametip", tipsShow);
                _.timer.Once(4f, () => player?.SendConsoleCommand("gametip.hidegametip"));
            }
        }

        public static IndustrialStorageAdaptor AddStorageAdaptor(PlanterBox planterBox, BaseEntity parent, Vector3 pos, Quaternion rot)
        {
            IndustrialStorageAdaptor adaptor = GameManager.server.CreateEntity("assets/prefabs/deployable/playerioents/industrialadaptors/storageadaptor.deployed.prefab", planterBox.transform.TransformPoint(pos), planterBox.transform.rotation * rot) as IndustrialStorageAdaptor;
            adaptor.OwnerID = parent.OwnerID;
            adaptor.Spawn();

            SpawnRefresh(adaptor);
            adaptor.SetParent(parent, true, true);
            adaptor.SendNetworkUpdateImmediate();
            return adaptor;
        }

        public static void SpawnRefresh(BaseEntity entity1)
        {
            if (entity1 != null)
            {
                if (entity1.GetComponentsInChildren<MeshCollider>() != null)
                    foreach (var mesh in entity1.GetComponentsInChildren<MeshCollider>())
                    {
                        UnityEngine.Object.DestroyImmediate(mesh);
                    }

                if (entity1.GetComponent<Collider>() != null)
                    UnityEngine.Object.DestroyImmediate(entity1.GetComponent<Collider>());
                if (entity1.GetComponent<GroundWatch>() != null)
                    UnityEngine.Object.DestroyImmediate(entity1.GetComponent<GroundWatch>());
                if (entity1.GetComponent<DestroyOnGroundMissing>() != null)
                    UnityEngine.Object.DestroyImmediate(entity1.GetComponent<DestroyOnGroundMissing>());
            }
        }

        private static void UpdateLocalPositionAndRotation(Transform parentTransform, BaseEntity entity, Vector3 localPosition, Quaternion localRotation, bool rotate = false)
        {
            var transform = entity.transform;
            var intendedPosition = parentTransform.TransformPoint(localPosition);
            var intendedRotation = parentTransform.rotation * localRotation;

            List<List<Vector3>> outputPointLists = null;

            var ioEntity = entity as IOEntity;
            if (ioEntity != null)
            {
                outputPointLists = new List<List<Vector3>>();

                // Save IO outputs using world position, to be restored later to new local positions.
                foreach (var slot in ioEntity.outputs)
                {
                    if (slot.connectedTo.Get() == null)
                        continue;

                    var pointList = new List<Vector3>();
                    outputPointLists.Add(pointList);

                    // Skip the last (closest) line point, since it needs to maintain relative position.
                    for (var i = 0; i < slot.linePoints.Length - 1; i++)
                    {
                        var localPointPosition = slot.linePoints[i];
                        pointList.Add(transform.TransformPoint(localPointPosition));
                    }
                }
            }

            var wantsPositionChange = transform.position != intendedPosition;
            var wantsRotationChange = transform.rotation != intendedRotation;
            var wantsChange = wantsPositionChange || wantsRotationChange || rotate;

            BaseEntity[] children = null;
            if (wantsChange && entity.children.Count > 0)
            {
                children = entity.children.ToArray();

                // Temporarily unparent industrial adapters, so their pipes aren't messed up.
                // Pipes will be saved and restored when this method is called for the adapters.
                foreach (var child in children)
                {
                    child.SetParent(null, worldPositionStays: true, sendImmediate: false);
                }
            }

            if (wantsPositionChange)
            {
                transform.position = intendedPosition;
            }

            if (wantsRotationChange)
            {
                transform.rotation = intendedRotation;
            }

            // Re-parent industrial adapters, without changing their position {CDID}.
            if (children != null)
            {
                foreach (var child in children)
                {
                    child.SetParent(entity, worldPositionStays: true);
                }
            }

            if (wantsChange && outputPointLists != null)
            {
                // Restore IO outputs from world position to new local positions.
                for (int i = 0, j = 0; i < ioEntity.outputs.Length; i++)
                {
                    var slot = ioEntity.outputs[i];
                    if (slot.connectedTo.Get() == null)
                        continue;

                    var pointList = outputPointLists[j++];

                    // Skip the last (closest) line point, since it needs to maintain relative position.
                    for (var k = 0; k < slot.linePoints.Length - 1; k++)
                    {
                        slot.linePoints[k] = transform.InverseTransformPoint(pointList[k]);
                    }
                }

                foreach (var inputSlot in ioEntity.inputs)
                {
                    var inputEntity = inputSlot.connectedTo.Get();
                    if (inputEntity == null)
                        continue;

                    var linePoints = inputEntity.outputs.ElementAtOrDefault(inputSlot.connectedToSlot)?.linePoints;
                    if (linePoints == null || linePoints.Length == 0)
                        continue;

                    var worldHandlePosition = transform.TransformPoint(inputSlot.handlePosition);
                    linePoints[0] = inputEntity.transform.InverseTransformPoint(worldHandlePosition);
                    inputEntity.TerminateOnClient(BaseNetworkable.DestroyMode.None);
                    inputEntity.SendNetworkUpdateImmediate();
                }
            }

            if (wantsChange)
            {
                if (ioEntity != null)
                {
                    // Line points may have changed, which needs a full network snapshot.
                    entity.TerminateOnClient(BaseNetworkable.DestroyMode.None);
                    entity.SendNetworkUpdateImmediate();
                }
                else
                {
                    entity.InvalidateNetworkCache();
                    entity.SendNetworkUpdate_Position();
                }
            }
        }

        private static T GetChildOfType<T>(BaseEntity entity) where T : class
        {
            foreach (var child in entity.children)
            {
                var childOfType = child as T;
                if (childOfType != null)
                    return childOfType;
            }

            return null;
        }

        private static IndustrialStorageAdaptor GetAdapter(StorageContainer entity)
        {
            return GetChildOfType<IndustrialStorageAdaptor>(entity);
        }

        private static T GetNearbyEntity<T>(Vector3 position, float radius) where T : BaseEntity
        {
            List<T> nearby = new List<T>();
            Vis.Entities(position, radius, nearby);
            return nearby.FirstOrDefault();
        }
        #endregion

        #region Data
        void LoadData()
        {
            try
            {
                pcdData = Interface.GetMod().DataFileSystem.ReadObject<FarmEntity>(Name + "/Farm_Data") ?? new FarmEntity();
            }
            catch
            {
                PrintWarning("Couldn't load Farm_Data, creating new FarmData file");
                pcdData = new FarmEntity();
            }
        }
        class FarmEntity
        {
            public Dictionary<ulong, PCDInfo> Planters = new Dictionary<ulong, PCDInfo>();

            public FarmEntity() { }
        }
        class PCDInfo
        {
            public int lifeLimit;
            public int pickedCount;
            public ulong ownerid;
            public bool currentState;
            public string configPerm;
        }
        void SaveData(string filename = "AQpkBQN=")
        {
            PCDDATA.WriteObject(pcdData);
        }

        public BaseNetworkable FindEntity(ulong netID)
        {
            //   KeyValuePair<NetworkableId, BaseNetworkable> searchResult = BaseNetworkable.serverEntities.entityList.FirstOrDefault(s => s.Value.net.ID.Value == netID);
            return BaseNetworkable.serverEntities.Find(new NetworkableId(netID));
        }
        #endregion Data

        #region User Interface
        public static class Ui
        {
            public static class Anchors
            {
#pragma warning disable IDE0051 // Remove unused private members
                public const string LowerLeft = "0 0";
                public const string UpperLeft = "0 1";
                public const string Center = "0.5 0.5";
                public const string LowerRight = "1 0";
                public const string UpperRight = "1 1";
                public const string LowerCenter = "0.5 0";
#pragma warning restore IDE0051 // Remove unused private members
            }

            public static class Colors
            {
                public const string Black = "0 0 0 1";
                public const string Blanc = "0.851 0.820 0.776 1";
                public const string Cloudy = "0.678 0.647 0.620 1";
                public const string DarkGray = "0.12 0.12 0.12 1";
                public const string DarkGreen = "0.145 0.255 0.09 1";
                public const string DarkRed = "0.8 0 0 1";
                public const string DimGray = "0.33 0.33 0.33 1";
                public const string Karaka = "0.16 0.16 0.13 1";
                public const string LightGreen = "0.365 0.447 0.220 1";
                public const string White = "1 1 1 1";

                public static class Transparent
                {
                    public const string Black90 = "0 0 0 0.90";
                    public const string DimGray90 = "0.33 0.33 0.33 0.90";
                    public const string LightGreen = "0.365 0.447 0.220 0.8";
                    public const string DarkRed = "0.8 0 0 0.8";
                    public const string Clear = "0 0 0 0";
                }
            }

            public static class Overlay
            {
                public const string Panel = "Overlay";

                public static class Screen
                {
                    public const string Panel = "Panel.Screen";
                    public const string ButtonOn = "Panel.Screen.Button";
                }
            }

            public static class Commands
            {
                public const string UiAction = "commandautofarm545";
            }
        }
        #endregion

        #region CUI
        [ConsoleCommand(Ui.Commands.UiAction)]
        private void UiActionCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();

            if (player == null && !planterBoxBehavior._AllLootingPlayers.ContainsKey(player.userID))
                return;

            bool currentState = planterBoxBehavior._AllLootingPlayers[player.userID].currentState;
            planterBoxBehavior._AllLootingPlayers[player.userID].switchState(player, !currentState);
            BuildCuiScreen(player, !currentState);
        }

        void DestroyCui(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, Ui.Overlay.Screen.Panel);
        }

        private void BuildCuiScreen(BasePlayer player, bool onOFF)
        {
            DestroyCui(player);

            string color = "";
            string message = "AQpkBQN=";
            if (!onOFF)
            {
                color = Ui.Colors.Transparent.LightGreen;
                message = string.Format(lang.GetMessage("turnOFF", this, player.UserIDString));
            }
            else
            {
                color = Ui.Colors.Transparent.DarkRed;
                message = string.Format(lang.GetMessage("turnON", this, player.UserIDString));
            }

            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = Ui.Colors.Transparent.Clear },
                RectTransform = { AnchorMin = Ui.Anchors.Center, AnchorMax = Ui.Anchors.Center }
            }, Ui.Overlay.Panel, Ui.Overlay.Screen.Panel);

            container.Add(new CuiButton
            {
                Text = { Text = message, FontSize = 12, Color = Ui.Colors.Blanc, Align = TextAnchor.MiddleCenter },
                Button = { Color = color, Command = Ui.Commands.UiAction },
                RectTransform = { AnchorMin = Ui.Anchors.Center, AnchorMax = Ui.Anchors.Center, OffsetMin = "-72 -18.0", OffsetMax = "115.5 -1.5" }
            }, Ui.Overlay.Screen.Panel, Ui.Overlay.Screen.ButtonOn);

            CuiHelper.AddUi(player, container);
        }
        #endregion

        #region Localization
        private new void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["enabled"] = "<color=#ce422b>AutoFarm placment enabled!</color>",
                ["disabled"] = "<color=#ce422b>AutoFarm placment disabled!</color>",
                ["max"] = "<color=#ce422b>You reached your max AutoFarm placments of {0}!</color>",
                ["rotatePlanter"] = "You can rotate the planter by hitting it with a hammer!",
                ["noPickup"] = "Storage contains items still!",
                ["turnON"] = "AutoFarm Deactivated",
                ["turnOFF"] = "AutoFarm Activated"
            }, this);
        }
        #endregion
    }
} 