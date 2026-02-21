using System.Collections.Generic;
using UnityEngine.Assertions;
using System.Collections;
using Oxide.Core.Configuration;
using Newtonsoft.Json;
using UnityEngine.AI;
using Oxide.Core.Plugins;
using UnityEngine;
using System.Linq;
using Oxide.Core;
using System;
using System.Globalization;
using VLB;
using Rust;
using Facepunch;
using Oxide.Game.Rust.Cui;
using ProtoBuf;
using UnityEngine.Pool;
using Rust.Ai.Gen2;
using HarmonyLib;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Animal Farm", "Razor", "1.3.6")]
    [Description("Animal farm plugin")]
    public class AnimalFarm : RustPlugin
    {
        private static AnimalFarm _;
        FarmData pcdData;
        private DynamicConfigFile PCDDATA;
        CoopData coopData;
        private DynamicConfigFile COOPDATA;
        private const string theAdmin = "animalfarm.admin";
        private const string theBoar = "animalfarm.gatherboar";
        private const string theBear = "animalfarm.gatherbear";
        private const string theWolf = "animalfarm.gatherwolf";
        private const string theStag = "animalfarm.gatherstag";
        private const string theTiger = "animalfarm.gathertiger";
        private const string thePanther = "animalfarm.gatherpanther";
        private const string theCrocodile = "animalfarm.gathercrocodile";
        private const string theChicken = "animalfarm.chickencoop";

        private const string storageAdapter = "assets/prefabs/deployable/playerioents/industrialadaptors/storageadaptor.deployed.prefab";
        private const string spliter = "assets/prefabs/deployable/playerioents/fluidsplitter/fluidsplitter.prefab";
        private const string chickencCoop = "assets/prefabs/deployable/chickencoop/chickencoop.deployed.prefab";
        private const string trough = "assets/prefabs/deployable/hitch & trough/hitchtrough.deployed.prefab";
        private const string fence = "assets/prefabs/building/wall.frame.fence/wall.frame.fence.prefab";
        private const string hopperPrefab = "assets/prefabs/deployable/hopper/hopper.deployed.prefab";
        private const string waterbarel = "assets/prefabs/deployable/liquidbarrel/waterbarrel.prefab";
        private const string frame = "assets/prefabs/building core/wall.frame/wall.frame.prefab";
        private const string bearCorpse = "assets/rust.ai/agents/bear/bear.corpse.prefab";
        private const string boarCorpse = "assets/rust.ai/agents/boar/boar.corpse.prefab";
        private const string stagCorpse = "assets/rust.ai/agents/stag/stag.corpse.prefab";
        private const string wolfCorpse = "assets/rust.ai/agents/wolf/wolf.corpse.prefab";

        private const string boar = "assets/rust.ai/agents/boar/boar.prefab";
        private const string stag = "assets/rust.ai/agents/stag/stag.prefab";
        private const string bear = "assets/rust.ai/agents/bear/bear.prefab";
        private const string wolf = "assets/rust.ai/agents/wolf/wolf.prefab";

        //Gen2
        private const string pantherCorpse = "assets/rust.ai/agents/panther/panther.corpse.prefab";
        private const string tigerCorpse = "assets/rust.ai/agents/tiger/tiger.corpse.prefab";
        private const string crocodileCorpse = "assets/rust.ai/agents/crocodile/crocodile.corpse.prefab";
        private const string panther = "assets/rust.ai/agents/panther/panther.prefab";
        private const string tiger = "assets/rust.ai/agents/tiger/tiger.prefab";
        private const string crocodile = "assets/rust.ai/agents/crocodile/crocodile.prefab";

        private const string PoolShortname = "abovegroundpool.deployed";
        private static ulong CustomSkin = 8675309;
        private static GestureConfig gestureConfig = null;
        public static ProtectionProperties newProtectionFull = null;
        private static float[] protectionSettingsFull = new float[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 };
        private List<ulong> didGather = new List<ulong>();
        private static ulong SecretKey;
        public bool LoadedAlready = false;
        public static bool reloading = false;
        private static ulong itemSkin { get; set; }
        private static ulong itemSkinStag { get; set; }
        private static ulong itemSkinBear { get; set; }
        private static ulong itemSkinWolf { get; set; }
        private static ulong itemSkinTiger { get; set; }
        private static ulong itemSkinPanther { get; set; }
        private static ulong itemSkinCrocodile { get; set; }

        private static Dictionary<ulong, itemLimits> playerLimits = new Dictionary<ulong, itemLimits>();
        public class itemLimits { public int coop; public int pFarm; }

        #region Loading/Unloading
        private void OnServerSave() { SaveData(); SaveDataCoop(); }

        private void OnNewSave(string filename)
        {
            pcdData.PlayerFarms = new Dictionary<ulong, PCDInfo>();
            coopData.PlayerCoops = new Dictionary<ulong, coopInfo>();
            SaveData();
            SaveDataCoop();
        }

        private void Init()
        {
            _ = this;
            playerLimits = new Dictionary<ulong, itemLimits>();
            SecretKey = (ulong)UnityEngine.Random.Range(41234564, 9999999999999999);
            PCDDATA = Interface.Oxide.DataFileSystem.GetFile($"{Name}/Farm_Data");
            COOPDATA = Interface.Oxide.DataFileSystem.GetFile($"{Name}/Coop_Data");
            LoadData();

            itemSkin = configData.settingsBaby.skinID;
            itemSkinStag = configData.settingsBabyStag.skinIDStag;
            itemSkinBear = configData.settingsBabyBear.skinID;
            itemSkinWolf = configData.settingsBabyWolf.skinID;
            itemSkinTiger = configData.settingsBabyTiger.skinID;
            itemSkinPanther = configData.settingsBabyPanther.skinID;
            itemSkinCrocodile = configData.settingsBabyCrocodile.skinID;

            newProtectionFull = ScriptableObject.CreateInstance<ProtectionProperties>();
            permission.RegisterPermission(theAdmin, this);
            if (configData.settings.requirePerm)
                RegusterGatherPerms();

            permission.RegisterPermission(theChicken, this);
        }

        private void RegusterGatherPerms()
        {
            permission.RegisterPermission(theBoar, this);
            permission.RegisterPermission(theBear, this);
            permission.RegisterPermission(theStag, this);
            permission.RegisterPermission(theWolf, this);
            permission.RegisterPermission(theTiger, this);
            permission.RegisterPermission(thePanther, this);
            permission.RegisterPermission(theCrocodile, this);
        }

        private void OnServerInitialized()
        {
            if (!LoadedAlready)
            {
                ServerMgr.Instance.StartCoroutine(ReInItFarms());
            }

            GenerateGestureConfig();
        }

        private void OnSaveLoad(Dictionary<BaseEntity, ProtoBuf.Entity> entities)
        {
            LoadedAlready = true;
            PrintWarning("Loading farms on save load");
            ServerMgr.Instance.StartCoroutine(ReInItFarms());
        }

        IEnumerator ReInItFarms()
        {
            bool running = true;
            Dictionary<ulong, PCDInfo> initFarms = new Dictionary<ulong, PCDInfo>(pcdData.PlayerFarms);
            int total = 0;
            while (initFarms.Count > 0 && running && _ != null)
            {
                PrintWarning($"Starting to initialized all farms in load order!");
                foreach (var farm in initFarms)
                {
                    yield return new WaitForSeconds(0.01f);

                    if (_ == null) break;

                    HitchTrough theFarm = FindEntity(farm.Key) as HitchTrough;
                    if (theFarm == null)
                        pcdData.PlayerFarms.Remove(farm.Key);
                    else
                    {
                        total++;
                        if (!playerLimits.ContainsKey(theFarm.OwnerID))
                            playerLimits.Add(theFarm.OwnerID, new itemLimits() { pFarm = 1 });
                        else
                            playerLimits[theFarm.OwnerID].pFarm++;

                        List<AnimalState> tempEntity = Pool.Get<List<AnimalState>>();
                        tempEntity.AddRange(pcdData.PlayerFarms[farm.Key].Boars.Values);
                        pcdData.PlayerFarms[farm.Key].Boars.Clear();

                        FarmBehavior controler = theFarm.gameObject.AddComponent<FarmBehavior>();
                        controler.IsFoundation = farm.Value.IsFoundation;

                        foreach (var pig in tempEntity)
                        {
                            yield return new WaitForSeconds(0.01f);
                            if (_ == null) continue;
                            controler.BuyPig(pig.health, pig.name, pig.state, pig.Hydration, pig.Energy, pig.Stamina, farm.Value.IsFoundation, pig.Age);
                        }

                        tempEntity = Pool.Get<List<AnimalState>>();
                        tempEntity.AddRange(pcdData.PlayerFarms[farm.Key].Stags.Values);
                        pcdData.PlayerFarms[farm.Key].Stags.Clear();

                        foreach (var stag in tempEntity)
                        {
                            yield return new WaitForSeconds(0.01f);
                            if (_ == null) continue;
                            controler.BuyStag(stag.health, stag.name, stag.state, stag.Hydration, stag.Energy, stag.Stamina, farm.Value.IsFoundation, stag.Age);
                        }

                        tempEntity = Pool.Get<List<AnimalState>>();
                        tempEntity.AddRange(pcdData.PlayerFarms[farm.Key].Bears.Values);
                        pcdData.PlayerFarms[farm.Key].Bears.Clear();

                        foreach (var bears in tempEntity)
                        {
                            yield return new WaitForSeconds(0.01f);
                            if (_ == null) continue;
                            controler.BuyBear(bears.health, bears.name, bears.state, bears.Hydration, bears.Energy, bears.Stamina, farm.Value.IsFoundation, bears.Age);
                        }

                        tempEntity = Pool.Get<List<AnimalState>>();
                        tempEntity.AddRange(pcdData.PlayerFarms[farm.Key].Wolf.Values);
                        pcdData.PlayerFarms[farm.Key].Wolf.Clear();

                        foreach (var wolfs in tempEntity)
                        {
                            yield return new WaitForSeconds(0.01f);
                            if (_ == null) continue;
                            controler.BuyWolf(wolfs.health, wolfs.name, wolfs.state, wolfs.Hydration, wolfs.Energy, wolfs.Stamina, farm.Value.IsFoundation, wolfs.Age);
                        }

                        tempEntity = Pool.Get<List<AnimalState>>();
                        tempEntity.AddRange(pcdData.PlayerFarms[farm.Key].Tigers.Values);
                        pcdData.PlayerFarms[farm.Key].Tigers.Clear();

                        foreach (var tiger in tempEntity)
                        {
                            yield return new WaitForSeconds(0.01f);
                            if (_ == null) continue;
                            controler.BuyTiger(tiger.health, tiger.name, tiger.state, tiger.Hydration, tiger.Energy, tiger.Stamina, farm.Value.IsFoundation, tiger.Age);
                        }

                        tempEntity = Pool.Get<List<AnimalState>>();
                        tempEntity.AddRange(pcdData.PlayerFarms[farm.Key].Panthers.Values);
                        pcdData.PlayerFarms[farm.Key].Panthers.Clear();

                        foreach (var Panther in tempEntity)
                        {
                            yield return new WaitForSeconds(0.01f);
                            if (_ == null) continue;
                            controler.BuyPanther(Panther.health, Panther.name, Panther.state, Panther.Hydration, Panther.Energy, Panther.Stamina, farm.Value.IsFoundation, Panther.Age);
                        }

                        tempEntity = Pool.Get<List<AnimalState>>();
                        tempEntity.AddRange(pcdData.PlayerFarms[farm.Key].Crocodiles.Values);
                        pcdData.PlayerFarms[farm.Key].Crocodiles.Clear();

                        foreach (var Crocodile in tempEntity)
                        {
                            yield return new WaitForSeconds(0.01f);
                            if (_ == null) continue;
                            controler.BuyCrocodile(Crocodile.health, Crocodile.name, Crocodile.state, Crocodile.Hydration, Crocodile.Energy, Crocodile.Stamina, farm.Value.IsFoundation, Crocodile.Age);
                        }

                        Pool.FreeUnmanaged(ref tempEntity);
                    }
                }
                running = false;

            }
            SaveData();
            PrintWarning($"Finished initialize of {total} animal farms!");

            List<ulong> tempList = Pool.Get<List<ulong>>();
            tempList.AddRange(coopData.PlayerCoops.Keys);

            total = 0;
            foreach (var theID in tempList)
            {
                global::ChickenCoop found = FindEntity(theID) as global::ChickenCoop;
                if (found != null)
                {
                    total++;
                    if (!playerLimits.ContainsKey(found.OwnerID))
                        playerLimits.Add(found.OwnerID, new itemLimits() { coop = 1 });
                    else
                        playerLimits[found.OwnerID].coop++;

                    found.gameObject.AddComponent<CoopBehavior>();
                }
                else
                    coopData.PlayerCoops.Remove(theID);
            }

            SaveDataCoop();
            Pool.FreeUnmanaged(ref tempList);
            PrintWarning($"Finished initialize of {total} chickencoops!");
        }

        private void Unload()
        {
            reloading = true;
            foreach (var controler in FarmBehavior._allFarms)
                UnityEngine.Object.DestroyImmediate(controler);

            foreach (var controlerC in CoopBehavior._allCoops)
                UnityEngine.Object.DestroyImmediate(controlerC.Value);

            SaveData();
            SaveDataCoop();

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                CloseUiStats(player);
                CloseUi(player);
            }
            _ = null;
        }

        private void GenerateGestureConfig()
        {
            FarmableAnimal entity = GameManager.server.CreateEntity("assets/prefabs/deployable/chickencoop/simplechicken.entity.prefab") as FarmableAnimal;

            if (entity != null)
            {
                entity.Spawn();

                gestureConfig = ScriptableObject.CreateInstance<GestureConfig>();
                if (entity.PettingGesture != null)
                {
                    CopyFields<GestureConfig>(entity.PettingGesture, gestureConfig);
                    entity?.Kill();
                }
            }
        }
        #endregion

        #region Config
        private ConfigData configData;
        class ConfigData
        {
            [JsonProperty(PropertyName = "Chicken Coop Settings")]
            public SettingsCoop settingsCoop { get; set; }

            [JsonProperty(PropertyName = "Farm Settings")]
            public Settings settings { get; set; }

            [JsonProperty(PropertyName = "Boar Baby Settings")]
            public SettingsBaby settingsBaby { get; set; }

            [JsonProperty(PropertyName = "Stag Baby Settings")]
            public SettingsBabyStag settingsBabyStag { get; set; }

            [JsonProperty(PropertyName = "Bear Baby Settings")]
            public SettingsBabyBear settingsBabyBear { get; set; }

            [JsonProperty(PropertyName = "Wolf Baby Settings")]
            public SettingsBabyWolf settingsBabyWolf { get; set; }

            [JsonProperty(PropertyName = "Tiger Baby Settings")]
            public SettingsBabyTiger settingsBabyTiger { get; set; }

            [JsonProperty(PropertyName = "Panther Baby Settings")]
            public SettingsBabyPanther settingsBabyPanther { get; set; }

            [JsonProperty(PropertyName = "Crocodile Baby Settings")]
            public SettingsBabyCrocodile settingsBabyCrocodile { get; set; }

            public class SettingsCoop
            {
                [JsonProperty(PropertyName = "Total chickens animals allowed in the coop. Default UI will only show 4")]
                public int maxAnimals { get; set; }
                [JsonProperty(PropertyName = "Chickens hatch time minutes")]
                public int ChickenHatchTimeMinutes { get; set; }
                [JsonProperty(PropertyName = "Add storage adapter to coop")]
                public bool addAdapter { get; set; }
                [JsonProperty(PropertyName = "Add water adapter to coop")]
                public bool addWater { get; set; }
                [JsonProperty(PropertyName = "Allow auto hatching of eggs")]
                public bool autoHatch { get; set; }
                [JsonProperty(PropertyName = "Player may have how many modifyed coops -1 = unlimited")]
                public int totalAllowed { get; set; }
                [JsonProperty(PropertyName = "Only use custom item to place coop")]
                public bool customOnly { get; set; }
                [JsonProperty(PropertyName = "Coop item skin")]
                public ulong skin { get; set; }
                [JsonProperty(PropertyName = "Coop item name")]
                public string name { get; set; }
            }

            public class Settings
            {
                [JsonProperty(PropertyName = "Farm item skinID")]
                public ulong skin { get; set; }
                [JsonProperty(PropertyName = "Farm item name")]
                public string name { get; set; }
                [JsonProperty(PropertyName = "Total farm animals allowed in the farm")]
                public int maxAnimals { get; set; }
                [JsonProperty(PropertyName = "Add storage adapter to trough")]
                public bool addAdapter { get; set; }
                [JsonProperty(PropertyName = "Add hopper to trough")]
                public bool addHopper { get; set; }
                [JsonProperty(PropertyName = "Require permissions for gatheringr")]
                public bool requirePerm { get; set; }
                [JsonProperty(PropertyName = "Player may have how many farms -1 = unlimited")]
                public int totalAllowed { get; set; }
            }

            public class SettingsBaby
            {
                [JsonProperty(PropertyName = "Chance to get baby boar from gathering")]
                public int chance { get; set; }
                [JsonProperty(PropertyName = "Baby Boar item skinID")]
                public ulong skinID { get; set; }
                [JsonProperty(PropertyName = "Baby Boar item name")]
                public string name { get; set; }
                [JsonProperty(PropertyName = "Baby Boar groth minutes")]
                public int boarGrothTick { get; set; }
                [JsonProperty(PropertyName = "Boar can breed.")]
                public bool canBreed { get; set; }
                [JsonProperty(PropertyName = "Boar breed chance.")]
                public int breedChance { get; set; }
                [JsonProperty(PropertyName = "Boar breed cooldown minutes.")]
                public int breedCooldown { get; set; }
                [JsonProperty(PropertyName = "Boar dung amount.")]
                public int dung { get; set; }
                [JsonProperty(PropertyName = "Boar gather age and items.")]
                public Dictionary<int, List<ItemResourceDispenser>> ResourceDispenser { get; set; }
            }


            public class SettingsBabyStag
            {
                [JsonProperty(PropertyName = "Chance to get baby stag from gathering")]
                public int chanceStag { get; set; }
                [JsonProperty(PropertyName = "Baby Stag item skinID")]
                public ulong skinIDStag { get; set; }
                [JsonProperty(PropertyName = "Baby Stag item name")]
                public string nameStag { get; set; }
                [JsonProperty(PropertyName = "Baby Stag groth minutes")]
                public int stagGrothTick { get; set; }
                [JsonProperty(PropertyName = "Stag can breed.")]
                public bool canBreed { get; set; }
                [JsonProperty(PropertyName = "Stag breed chance.")]
                public int breedChance { get; set; }
                [JsonProperty(PropertyName = "Stag breed cooldown minutes.")]
                public int breedCooldown { get; set; }
                [JsonProperty(PropertyName = "Stag dung amount.")]
                public int dung { get; set; }
                [JsonProperty(PropertyName = "Stag gather age and items.")]
                public Dictionary<int, List<ItemResourceDispenser>> ResourceDispenser { get; set; }
            }

            public class SettingsBabyBear
            {
                [JsonProperty(PropertyName = "Chance to get baby bear from gathering")]
                public int chance { get; set; }
                [JsonProperty(PropertyName = "Baby Bear item skinID")]
                public ulong skinID { get; set; }
                [JsonProperty(PropertyName = "Baby Bear item name")]
                public string name { get; set; }
                [JsonProperty(PropertyName = "Baby Bear groth minutes")]
                public int GrothTick { get; set; }
                [JsonProperty(PropertyName = "Bear can breed.")]
                public bool canBreed { get; set; }
                [JsonProperty(PropertyName = "Bear breed chance.")]
                public int breedChance { get; set; }
                [JsonProperty(PropertyName = "Bear breed cooldown minutes.")]
                public int breedCooldown { get; set; }
                [JsonProperty(PropertyName = "Bear dung amount.")]
                public int dung { get; set; }
                [JsonProperty(PropertyName = "Bear gather age and items.")]
                public Dictionary<int, List<ItemResourceDispenser>> ResourceDispenser { get; set; }
            }

            public class SettingsBabyWolf
            {
                [JsonProperty(PropertyName = "Chance to get baby wolf from gathering")]
                public int chance { get; set; }
                [JsonProperty(PropertyName = "Baby Wolf item skinID")]
                public ulong skinID { get; set; }
                [JsonProperty(PropertyName = "Baby Wolf item name")]
                public string name { get; set; }
                [JsonProperty(PropertyName = "Baby Wolf groth minutes")]
                public int GrothTick { get; set; }
                [JsonProperty(PropertyName = "Wolf can breed.")]
                public bool canBreed { get; set; }
                [JsonProperty(PropertyName = "Wolf breed chance.")]
                public int breedChance { get; set; }
                [JsonProperty(PropertyName = "Wolf breed cooldown minutes.")]
                public int breedCooldown { get; set; }
                [JsonProperty(PropertyName = "Wolf dung amount.")]
                public int dung { get; set; }
                [JsonProperty(PropertyName = "Wolf gather age and items.")]
                public Dictionary<int, List<ItemResourceDispenser>> ResourceDispenser { get; set; }
            }

            public class SettingsBabyTiger
            {
                [JsonProperty(PropertyName = "Chance to get baby tiger from gathering")]
                public int chance { get; set; }
                [JsonProperty(PropertyName = "Baby Tiger item skinID")]
                public ulong skinID { get; set; }
                [JsonProperty(PropertyName = "Baby Tiger item name")]
                public string name { get; set; }
                [JsonProperty(PropertyName = "Baby Tiger groth minutes")]
                public int GrothTick { get; set; }
                [JsonProperty(PropertyName = "Tiger can breed.")]
                public bool canBreed { get; set; }
                [JsonProperty(PropertyName = "Tiger breed chance.")]
                public int breedChance { get; set; }
                [JsonProperty(PropertyName = "Tiger breed cooldown minutes.")]
                public int breedCooldown { get; set; }
                [JsonProperty(PropertyName = "Tiger dung amount.")]
                public int dung { get; set; }
                [JsonProperty(PropertyName = "Tiger gather age and items.")]
                public Dictionary<int, List<ItemResourceDispenser>> ResourceDispenser { get; set; }
            }

            public class SettingsBabyPanther
            {
                [JsonProperty(PropertyName = "Chance to get baby Panther from gathering")]
                public int chance { get; set; }
                [JsonProperty(PropertyName = "Baby Panther item skinID")]
                public ulong skinID { get; set; }
                [JsonProperty(PropertyName = "Baby Panther item name")]
                public string name { get; set; }
                [JsonProperty(PropertyName = "Baby Panther groth minutes")]
                public int GrothTick { get; set; }
                [JsonProperty(PropertyName = "Panther can breed.")]
                public bool canBreed { get; set; }
                [JsonProperty(PropertyName = "Panther breed chance.")]
                public int breedChance { get; set; }
                [JsonProperty(PropertyName = "Pantherger breed cooldown minutes.")]
                public int breedCooldown { get; set; }
                [JsonProperty(PropertyName = "Panther dung amount.")]
                public int dung { get; set; }
                [JsonProperty(PropertyName = "Panther gather age and items.")]
                public Dictionary<int, List<ItemResourceDispenser>> ResourceDispenser { get; set; }
            }

            public class SettingsBabyCrocodile
            {
                [JsonProperty(PropertyName = "Chance to get baby Crocodile from gathering")]
                public int chance { get; set; }
                [JsonProperty(PropertyName = "Baby Crocodile item skinID")]
                public ulong skinID { get; set; }
                [JsonProperty(PropertyName = "Baby Crocodile item name")]
                public string name { get; set; }
                [JsonProperty(PropertyName = "Baby Crocodile groth minutes")]
                public int GrothTick { get; set; }
                [JsonProperty(PropertyName = "Crocodile can breed.")]
                public bool canBreed { get; set; }
                [JsonProperty(PropertyName = "Crocodile breed chance.")]
                public int breedChance { get; set; }
                [JsonProperty(PropertyName = "Crocodile breed cooldown minutes.")]
                public int breedCooldown { get; set; }
                [JsonProperty(PropertyName = "Crocodile dung amount.")]
                public int dung { get; set; }
                [JsonProperty(PropertyName = "Crocodile gather age and items.")]
                public Dictionary<int, List<ItemResourceDispenser>> ResourceDispenser { get; set; }
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
                settingsCoop = new ConfigData.SettingsCoop
                {
                    maxAnimals = 4,
                    ChickenHatchTimeMinutes = 2,
                    addAdapter = false,
                    addWater = false,
                    autoHatch = false,
                    totalAllowed = -1,
                    customOnly = false,
                    skin = 3461239631,
                    name = "Industrial Chicken Coop"
                },

                settings = new ConfigData.Settings
                {
                    skin = 3458119788,
                    name = "Animal Farm",
                    maxAnimals = 10,
                    addAdapter = true,
                    addHopper = true,
                    requirePerm = false,
                    totalAllowed = -1
                },

                settingsBaby = new ConfigData.SettingsBaby
                {
                    chance = 10,
                    skinID = 3458077515,
                    name = "A Baby Boar",
                    boarGrothTick = 30,
                    canBreed = true,
                    breedChance = 100,
                    breedCooldown = 180,
                    dung = 2,
                    ResourceDispenser = new Dictionary<int, List<ItemResourceDispenser>>()
                    {
                        { 1, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 1, shortname = "meat.boar" }, new ItemResourceDispenser() { amount = 10, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 5, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 5, shortname = "leather" }, new ItemResourceDispenser() { amount = 1, shortname = "cloth" } } },
                        { 2, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 3, shortname = "meat.boar" }, new ItemResourceDispenser() { amount = 15, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 10, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 8, shortname = "leather" }, new ItemResourceDispenser() { amount = 2, shortname = "cloth" } } },
                        { 3, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 6, shortname = "meat.boar" }, new ItemResourceDispenser() { amount = 50, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 15, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 12, shortname = "leather" }, new ItemResourceDispenser() { amount = 5, shortname = "cloth" } } },
                        { 4, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 7, shortname = "meat.boar" }, new ItemResourceDispenser() { amount = 25, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 20, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 16, shortname = "leather" }, new ItemResourceDispenser() { amount = 7, shortname = "cloth" } } },
                        { 5, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 8, shortname = "meat.boar" }, new ItemResourceDispenser() { amount = 50, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 40, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 20, shortname = "leather" }, new ItemResourceDispenser() { amount = 10, shortname = "cloth" } } }
                    }
                },

                settingsBabyStag = new ConfigData.SettingsBabyStag
                {
                    chanceStag = 10,
                    skinIDStag = 3458079161,
                    nameStag = "A Baby Stag",
                    stagGrothTick = 30,
                    canBreed = true,
                    breedChance = 100,
                    breedCooldown = 180,
                    dung = 2,
                    ResourceDispenser = new Dictionary<int, List<ItemResourceDispenser>>()
                    {
                        { 1, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 1, shortname = "deermeat.raw" }, new ItemResourceDispenser() { amount = 5, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 2, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 10, shortname = "leather" }, new ItemResourceDispenser() { amount = 5, shortname = "cloth" } } },
                        { 2, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 1, shortname = "deermeat.raw" }, new ItemResourceDispenser() { amount = 10, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 4, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 15, shortname = "leather" }, new ItemResourceDispenser() { amount = 10, shortname = "cloth" } } },
                        { 3, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 2, shortname = "deermeat.raw" }, new ItemResourceDispenser() { amount = 20, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 6, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 20, shortname = "leather" }, new ItemResourceDispenser() { amount = 15, shortname = "cloth" } } },
                        { 4, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 3, shortname = "deermeat.raw" }, new ItemResourceDispenser() { amount = 30, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 8, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 30, shortname = "leather" }, new ItemResourceDispenser() { amount = 20, shortname = "cloth" } } },
                        { 5, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 4, shortname = "deermeat.raw" }, new ItemResourceDispenser() { amount = 50, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 10, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 50, shortname = "leather" }, new ItemResourceDispenser() { amount = 25, shortname = "cloth" } } }
                    }
                },

                settingsBabyBear = new ConfigData.SettingsBabyBear
                {
                    chance = 10,
                    skinID = 3458078401,
                    name = "A Baby Bear",
                    GrothTick = 30,
                    canBreed = true,
                    breedChance = 100,
                    breedCooldown = 180,
                    dung = 2,
                    ResourceDispenser = new Dictionary<int, List<ItemResourceDispenser>>()
                    {
                        { 1, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 1, shortname = "bearmeat" }, new ItemResourceDispenser() { amount = 25, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 20, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 20, shortname = "leather" }, new ItemResourceDispenser() { amount = 5, shortname = "cloth" } } },
                        { 2, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 5, shortname = "bearmeat" }, new ItemResourceDispenser() { amount = 50, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 40, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 40, shortname = "leather" }, new ItemResourceDispenser() { amount = 15, shortname = "cloth" } } },
                        { 3, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 10, shortname = "bearmeat" }, new ItemResourceDispenser() { amount = 75, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 60, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 60, shortname = "leather" }, new ItemResourceDispenser() { amount = 25, shortname = "cloth" } } },
                        { 4, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 15, shortname = "bearmeat" }, new ItemResourceDispenser() { amount = 100, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 80, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 80, shortname = "leather" }, new ItemResourceDispenser() { amount = 35, shortname = "cloth" } } },
                        { 5, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 20, shortname = "bearmeat" }, new ItemResourceDispenser() { amount = 150, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 100, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 100, shortname = "leather" }, new ItemResourceDispenser() { amount = 50, shortname = "cloth" } } }
                    }
                },

                settingsBabyWolf = new ConfigData.SettingsBabyWolf
                {
                    chance = 10,
                    skinID = 3458436442,
                    name = "A Baby Wolf",
                    GrothTick = 30,
                    canBreed = true,
                    breedChance = 100,
                    breedCooldown = 180,
                    dung = 2,
                    ResourceDispenser = new Dictionary<int, List<ItemResourceDispenser>>()
                    {
                        { 1, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 1, shortname = "skull.wolf" }, new ItemResourceDispenser() { amount = 1, shortname = "wolfmeat.raw" }, new ItemResourceDispenser() { amount = 10, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 2, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 10, shortname = "leather" }, new ItemResourceDispenser() { amount = 5, shortname = "cloth" } } },
                        { 2, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 1, shortname = "skull.wolf" }, new ItemResourceDispenser() { amount = 2, shortname = "wolfmeat.raw" }, new ItemResourceDispenser() { amount = 20, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 4, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 25, shortname = "leather" }, new ItemResourceDispenser() { amount = 10, shortname = "cloth" } } },
                        { 3, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 1, shortname = "skull.wolf" }, new ItemResourceDispenser() { amount = 3, shortname = "wolfmeat.raw" }, new ItemResourceDispenser() { amount = 30, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 6, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 45, shortname = "leather" }, new ItemResourceDispenser() { amount = 20, shortname = "cloth" } } },
                        { 4, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 1, shortname = "skull.wolf" }, new ItemResourceDispenser() { amount = 4, shortname = "wolfmeat.raw" }, new ItemResourceDispenser() { amount = 35, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 8, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 60, shortname = "leather" }, new ItemResourceDispenser() { amount = 25, shortname = "cloth" } } },
                        { 5, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 1, shortname = "skull.wolf" }, new ItemResourceDispenser() { amount = 5, shortname = "wolfmeat.raw" }, new ItemResourceDispenser() { amount = 40, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 10, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 75, shortname = "leather" }, new ItemResourceDispenser() { amount = 35, shortname = "cloth" } } }
                    }
                },

                settingsBabyTiger = new ConfigData.SettingsBabyTiger
                {
                    chance = 10,
                    skinID = 3469950143,
                    name = "A Baby Tiger",
                    GrothTick = 30,
                    canBreed = true,
                    breedChance = 100,
                    breedCooldown = 180,
                    dung = 2,
                    ResourceDispenser = new Dictionary<int, List<ItemResourceDispenser>>()
                    {
                        { 1, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 1, shortname = "bigcatmeat" }, new ItemResourceDispenser() { amount = 10, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 2, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 10, shortname = "leather" }, new ItemResourceDispenser() { amount = 5, shortname = "cloth" } } },
                        { 2, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 2, shortname = "bigcatmeat" }, new ItemResourceDispenser() { amount = 20, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 4, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 25, shortname = "leather" }, new ItemResourceDispenser() { amount = 10, shortname = "cloth" } } },
                        { 3, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 3, shortname = "bigcatmeat" }, new ItemResourceDispenser() { amount = 30, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 6, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 45, shortname = "leather" }, new ItemResourceDispenser() { amount = 20, shortname = "cloth" } } },
                        { 4, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 4, shortname = "bigcatmeat" }, new ItemResourceDispenser() { amount = 35, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 8, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 60, shortname = "leather" }, new ItemResourceDispenser() { amount = 25, shortname = "cloth" } } },
                        { 5, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 5, shortname = "bigcatmeat" }, new ItemResourceDispenser() { amount = 40, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 10, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 75, shortname = "leather" }, new ItemResourceDispenser() { amount = 35, shortname = "cloth" } } }
                    }
                },

                settingsBabyPanther = new ConfigData.SettingsBabyPanther
                {
                    chance = 10,
                    skinID = 3469941871,
                    name = "A Baby Panther",
                    GrothTick = 30,
                    canBreed = true,
                    breedChance = 100,
                    breedCooldown = 180,
                    dung = 2,
                    ResourceDispenser = new Dictionary<int, List<ItemResourceDispenser>>()
                    {
                        { 1, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 1, shortname = "bigcatmeat" }, new ItemResourceDispenser() { amount = 10, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 2, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 10, shortname = "leather" }, new ItemResourceDispenser() { amount = 5, shortname = "cloth" } } },
                        { 2, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 2, shortname = "bigcatmeat" }, new ItemResourceDispenser() { amount = 20, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 4, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 25, shortname = "leather" }, new ItemResourceDispenser() { amount = 10, shortname = "cloth" } } },
                        { 3, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 3, shortname = "bigcatmeat" }, new ItemResourceDispenser() { amount = 30, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 6, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 45, shortname = "leather" }, new ItemResourceDispenser() { amount = 20, shortname = "cloth" } } },
                        { 4, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 4, shortname = "bigcatmeat" }, new ItemResourceDispenser() { amount = 35, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 8, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 60, shortname = "leather" }, new ItemResourceDispenser() { amount = 25, shortname = "cloth" } } },
                        { 5, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 5, shortname = "bigcatmeat" }, new ItemResourceDispenser() { amount = 40, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 10, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 75, shortname = "leather" }, new ItemResourceDispenser() { amount = 35, shortname = "cloth" } } }
                    }
                },

                settingsBabyCrocodile = new ConfigData.SettingsBabyCrocodile
                {
                    chance = 10,
                    skinID = 3470285566,
                    name = "A Baby Crocodile",
                    GrothTick = 30,
                    canBreed = true,
                    breedChance = 100,
                    breedCooldown = 180,
                    dung = 2,
                    ResourceDispenser = new Dictionary<int, List<ItemResourceDispenser>>()
                    {
                        { 1, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 1, shortname = "crocodilemeat" }, new ItemResourceDispenser() { amount = 10, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 2, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 10, shortname = "leather" }, new ItemResourceDispenser() { amount = 5, shortname = "cloth" } } },
                        { 2, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 2, shortname = "crocodilemeat" }, new ItemResourceDispenser() { amount = 20, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 4, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 25, shortname = "leather" }, new ItemResourceDispenser() { amount = 10, shortname = "cloth" } } },
                        { 3, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 3, shortname = "crocodilemeat" }, new ItemResourceDispenser() { amount = 30, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 6, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 45, shortname = "leather" }, new ItemResourceDispenser() { amount = 20, shortname = "cloth" } } },
                        { 4, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 4, shortname = "crocodilemeat" }, new ItemResourceDispenser() { amount = 35, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 8, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 60, shortname = "leather" }, new ItemResourceDispenser() { amount = 25, shortname = "cloth" } } },
                        { 5, new List<ItemResourceDispenser>() { new ItemResourceDispenser() { amount = 5, shortname = "crocodilemeat" }, new ItemResourceDispenser() { amount = 40, shortname = "bone.fragments" }, new ItemResourceDispenser() { amount = 10, shortname = "fat.animal" }, new ItemResourceDispenser() { amount = 75, shortname = "leather" }, new ItemResourceDispenser() { amount = 35, shortname = "cloth" } } }
                    }
                },

                Version = Version
            };
        }

        protected override void SaveConfig() => Config.WriteObject(configData, true);

        private void UpdateConfigValues()
        {
            PrintWarning("Config update detected! Updating config values...");

            ConfigData baseConfig = GetBaseConfig();

            if (configData.Version < new VersionNumber(1, 3, 0))
                configData = baseConfig;

            configData.Version = Version;
            PrintWarning("Config update completed!");
        }

        public class ItemResourceDispenser
        {
            public string shortname;
            public float amount;
            public bool isBP;
        }

        #endregion Config

        #region Data
        public void LoadData()
        {
            try
            {
                pcdData = Interface.GetMod().DataFileSystem.ReadObject<FarmData>($"{Name}/Farm_Data") ?? new FarmData();
            }
            catch
            {
                PrintWarning("Couldn't load Farm_Data, creating new FarmData file");
                pcdData = new FarmData();
            }

            try
            {
                coopData = Interface.GetMod().DataFileSystem.ReadObject<CoopData>($"{Name}/Coop_Data") ?? new CoopData();
            }
            catch
            {
                PrintWarning("Couldn't load Coop_Data, creating new CoopData file");
                coopData = new CoopData();
            }
        }

        public class FarmData
        {
            public Dictionary<ulong, PCDInfo> PlayerFarms = new Dictionary<ulong, PCDInfo>();
        }

        public class PCDInfo
        {
            public ulong ownerid;
            public bool IsFoundation;
            public Dictionary<ulong, AnimalState> Boars = new Dictionary<ulong, AnimalState>();
            public Dictionary<ulong, AnimalState> Stags = new Dictionary<ulong, AnimalState>();
            public Dictionary<ulong, AnimalState> Wolf = new Dictionary<ulong, AnimalState>();
            public Dictionary<ulong, AnimalState> Bears = new Dictionary<ulong, AnimalState>();
            public Dictionary<ulong, AnimalState> Tigers = new Dictionary<ulong, AnimalState>();
            public Dictionary<ulong, AnimalState> Panthers = new Dictionary<ulong, AnimalState>();
            public Dictionary<ulong, AnimalState> Crocodiles = new Dictionary<ulong, AnimalState>();

            public DateTime LastBreedBoar;
            public DateTime LastBreedStag;
            public DateTime LastBreedBear;
            public DateTime LastBreedWolf;
            public DateTime LastBreedTiger;
            public DateTime LastBreedPanther;
            public DateTime LastBreedCrocodile;
        }

        public class AnimalState
        {
            public float health;
            public int state;
            public string name;
            public float Hydration = 1f;
            public float Stamina = 0.1f;
            public float Energy = 1f;
            public DateTime Age;
        }

        public void SaveData(string filename = "AQH5BGD=")
        {
            PCDDATA.WriteObject(pcdData);
        }

        public class CoopData
        {
            public Dictionary<ulong, coopInfo> PlayerCoops = new Dictionary<ulong, coopInfo>();
        }

        public class coopInfo
        {
            public bool IsAuto = false;
        }

        public void SaveDataCoop(string filename = "newfile")
        {
            COOPDATA.WriteObject(coopData);
        }

        public BaseNetworkable FindEntity(ulong netID)
        {
            return BaseNetworkable.serverEntities.Find(new NetworkableId(netID));
        }
        #endregion Data

        #region Hooks
        private object canRemove(BasePlayer player, BaseEntity entity)
        {
            if (entity.skinID == configData.settings.skin)
                return false;
            return null;
        }

        private object CanEntityTakeDamage(CustomFarmAnimal entity, HitInfo hitinfo)
        {
            if (entity == null || hitinfo == null || hitinfo.InitiatorPlayer == null)
                return null;

            if (entity.OwnerID == hitinfo.InitiatorPlayer.userID)
                return true;

            var priv = entity.GetBuildingPrivilege();

            if (priv != null && priv.IsAuthed(hitinfo.InitiatorPlayer))
                return true;

            return false;
        }

        private object CanEntityTakeDamage(CustomFarmAnimalGen2 entity, HitInfo hitinfo)
        {
            if (entity == null || hitinfo == null || hitinfo.InitiatorPlayer == null)
                return null;

            if (entity.OwnerID == hitinfo.InitiatorPlayer.userID)
                return true;

            var priv = entity.GetBuildingPrivilege();

            if (priv != null && priv.IsAuthed(hitinfo.InitiatorPlayer))
                return true;

            return false;
        }

        private object CanPickupEntity(BasePlayer player, HitchTrough farm)
        {
            if (farm != null && farm.skinID == configData.settings.skin)
            {
                farm.DropItems();
                GivePlayerItem(player, 1);
                farm.GetComponent<FarmBehavior>()?.KillPigs();
                NextTick(() => farm.Kill());
                return false;
            }
            return null;
        }

        private object CanPickupEntity(BasePlayer player, IndustrialStorageAdaptor afapter)
        {
            if (afapter != null && afapter.skinID == configData.settings.skin)
            {
                FarmBehavior farm = afapter.GetComponentInParent<FarmBehavior>();
                if (farm != null)
                {
                    farm.hitch?.DropItems();
                    GivePlayerItem(player, 1);
                    farm.KillPigs();
                    NextTick(() => farm.hitch?.Kill());
                }
                return false;
            }
            return null;
        }

        private object CanPickupEntity(BasePlayer player, Splitter splitter)
        {
            if (splitter != null && splitter.skinID == configData.settings.skin)
            {
                FarmBehavior farm = splitter.GetComponentInParent<FarmBehavior>();
                if (farm != null)
                {
                    farm.hitch?.DropItems();
                    GivePlayerItem(player, 1);
                    farm.KillPigs();
                    NextTick(() => farm.hitch?.Kill());
                }
                return false;
            }
            return null;
        }

        private void OnDispenserGather(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            if (player == null || dispenser == null || dispenser.baseEntity == null || dispenser.name == null)
                return;

            ulong ID = dispenser.baseEntity.net.ID.Value;

            if (didGather.Contains(ID))
                return;

            string type = dispenser.name;

            if ((type == crocodileCorpse || type == pantherCorpse || type == tigerCorpse || type == bearCorpse || type == boarCorpse || type == stagCorpse || type == wolfCorpse) && dispenser.baseEntity.skinID != CustomSkin)
            {
                int range = UnityEngine.Random.Range(0, 101);
                int chance = 0;

                switch (type)
                {
                    case boarCorpse:
                        if (configData.settings.requirePerm && !permission.UserHasPermission(player.UserIDString, theBoar))
                            break;
                        chance = configData.settingsBaby.chance;
                        if (chance <= 0) return;
                        if (range <= chance)
                            GivePlayerItemBoar(player);
                        break;

                    case stagCorpse:
                        if (configData.settings.requirePerm && !permission.UserHasPermission(player.UserIDString, theStag))
                            break;
                        chance = configData.settingsBabyStag.chanceStag;
                        if (chance <= 0) return;
                        if (range <= chance)
                            GivePlayerItemStag(player);
                        break;

                    case bearCorpse:
                        if (configData.settings.requirePerm && !permission.UserHasPermission(player.UserIDString, theBear))
                            break;
                        chance = configData.settingsBabyBear.chance;
                        if (chance <= 0) return;
                        if (range <= chance)
                            GivePlayerItemBear(player);
                        break;

                    case wolfCorpse:
                        if (configData.settings.requirePerm && !permission.UserHasPermission(player.UserIDString, theWolf))
                            break;
                        chance = configData.settingsBabyWolf.chance;
                        if (chance <= 0) return;
                        if (range <= chance)
                            GivePlayerItemWolf(player);
                        break;

                    case tigerCorpse:
                        if (configData.settings.requirePerm && !permission.UserHasPermission(player.UserIDString, theTiger))
                            break;
                        chance = configData.settingsBabyTiger.chance;
                        if (chance <= 0) return;
                        if (range <= chance)
                            GivePlayerItemTiger(player);
                        break;

                    case pantherCorpse:
                        if (configData.settings.requirePerm && !permission.UserHasPermission(player.UserIDString, thePanther))
                            break;
                        chance = configData.settingsBabyPanther.chance;
                        if (chance <= 0) return;
                        if (range <= chance)
                            GivePlayerItemPanther(player);
                        break;


                    case crocodileCorpse:
                        if (configData.settings.requirePerm && !permission.UserHasPermission(player.UserIDString, theCrocodile))
                            break;
                        chance = configData.settingsBabyCrocodile.chance;
                        if (chance <= 0) return;
                        if (range <= chance)
                            GivePlayerItemCrocodile(player);
                        break;

                    default:
                        break;
                }

                didGather.Add(ID);
                timer.Once(600f, () =>
                {
                    didGather.Remove(ID);
                });
            }
        }

        private void OnEntityBuilt(Planner plan, GameObject go)
        {
            BasePlayer player = plan.GetOwnerPlayer();

            if (player == null)
                return;

            int totalAllowed = configData.settingsCoop.totalAllowed;
            itemLimits info = new itemLimits();

            if (permission.UserHasPermission(player.UserIDString, theChicken))
            {
                global::ChickenCoop chickenCoops = go?.ToBaseEntity()?.GetComponent<global::ChickenCoop>();
                if (chickenCoops != null)
                {
                    if (!playerLimits.TryGetValue(player.userID, out info))
                    {
                        info = new itemLimits();
                        playerLimits.Add(player.userID, info);
                    }

                    if (!configData.settingsCoop.customOnly && chickenCoops.skinID == 0)
                    {
                        if (totalAllowed > 0)
                        {
                            if (info.coop >= totalAllowed)
                            {
                                GameTips(player, string.Format(lang.GetMessage("MaxCoopsLimit", this, player.UserIDString), info.coop));
                                return;
                            }
                        }

                        info.coop++;
                        chickenCoops.OwnerID = player.userID;
                        chickenCoops.gameObject.AddComponent<CoopBehavior>();
                        return;
                    }
                    else if (chickenCoops.skinID == configData.settingsCoop.skin)
                    {
                        if (totalAllowed > 0)
                        {
                            if (info.coop >= totalAllowed)
                            {
                                chickenCoops.Kill();
                                NextTick(() => GivePlayerItemCoop(player));
                                GameTips(player, string.Format(lang.GetMessage("MaxCoopsLimitCustom", this, player.UserIDString), info.coop));
                                return;
                            }
                        }

                        info.coop++;
                        chickenCoops.OwnerID = player.userID;
                        chickenCoops.gameObject.AddComponent<CoopBehavior>();
                        return;
                    }

                    return;
                }
            }

            PaddlingPool coop = go?.ToBaseEntity()?.GetComponent<PaddlingPool>();
            if (coop == null || coop.skinID != configData.settings.skin || coop.ShortPrefabName != PoolShortname)
                return;

            if (player.IsBuildingBlocked() || !player.IsBuildingAuthed())
            {
                GivePlayerItem(player);
                coop?.Kill();
                SendReply(player, lang.GetMessage("blocked", this, player.UserIDString));
                return;
            }

            bool IsFoundation = false;
            totalAllowed = configData.settings.totalAllowed;

            if (Physics.Raycast(coop.transform.position + Vector3.up * 1.5f, -Vector3.up, out var HitObject, 20.5f, 2097152) && HitObject.GetEntity() is BuildingBlock entity)
            {
                if (entity.ShortPrefabName != "foundation")
                {
                    GameTips(player, lang.GetMessage("placementError", this, player.UserIDString));
                    Item item = plan?.GetOwnerItem();

                    if (item != null)
                    {
                        item.amount += 1;
                    }
                    else
                    {
                        GivePlayerItem(player);
                    }
                    coop?.Kill();
                    return;
                }

                IsFoundation = true;
            }

            if (totalAllowed > 0)
            {
                if (!playerLimits.TryGetValue(player.userID, out info))
                {
                    info = new itemLimits();
                    playerLimits.Add(player.userID, info);
                }

                if (info.pFarm >= totalAllowed)
                {
                    GameTips(player, string.Format(lang.GetMessage("MaxFarmsLimit", this, player.UserIDString), info.pFarm));
                    Item item = plan?.GetOwnerItem();

                    if (item != null)
                    {
                        item.amount += 1;
                    }
                    else
                    {
                        GivePlayerItem(player);
                    }
                    coop?.Kill();
                    return;
                }
            }

            info.pFarm++;
            SpawnFarm(player, coop, IsFoundation);
        }
        #endregion

        #region Methods
        private void SpawnFarm(BasePlayer player, PaddlingPool coop, bool IsFoundation)
        {
            if (coop == null)
                return;

            HitchTrough hitch = SpawnEntity(player.userID, IsFoundation, trough, coop.transform.TransformPoint(new Vector3(0f, 0f, -6.30f)), coop.transform.rotation * Quaternion.Euler(0, 0, 0), false) as HitchTrough;
            if (hitch != null)
            {
                pcdData.PlayerFarms.Add(hitch.net.ID.Value, new PCDInfo() { ownerid = player.userID.Get(), IsFoundation = IsFoundation });

                FarmBehavior controler = hitch.gameObject.AddComponent<FarmBehavior>();
                controler.IsFoundation = IsFoundation;

                NextTick(() => coop.Kill());
                BuildingBlock buildingBlock = SpawnEntity(player.userID, false, fence, new Vector3(0f, -2.0f, -0.3f), Quaternion.Euler(0, 90, 0), false, hitch) as BuildingBlock;
                buildingBlock = SpawnEntity(player.userID, false, fence, new Vector3(2.42f, -2.0f, -0.3f), Quaternion.Euler(0, 90, 0), false, hitch) as BuildingBlock;
                buildingBlock = SpawnEntity(player.userID, false, fence, new Vector3(-2.42f, -2.0f, -0.3f), Quaternion.Euler(0, 90, 0), false, hitch) as BuildingBlock;

                buildingBlock = SpawnEntity(player.userID, false, fence, new Vector3(0f, -2.0f, 10.5f), Quaternion.Euler(0, 90, 0), false, hitch) as BuildingBlock;
                buildingBlock = SpawnEntity(player.userID, false, fence, new Vector3(2.42f, -2.0f, 10.5f), Quaternion.Euler(0, 90, 0), false, hitch) as BuildingBlock;
                buildingBlock = SpawnEntity(player.userID, false, fence, new Vector3(-2.42f, -2.0f, 10.5f), Quaternion.Euler(0, 90, 0), false, hitch) as BuildingBlock;

                //sides
                buildingBlock = SpawnEntity(player.userID, false, fence, new Vector3(3.725f, -2.0f, 1.076f), Quaternion.Euler(0, 0, 0), false, hitch) as BuildingBlock;
                buildingBlock = SpawnEntity(player.userID, false, fence, new Vector3(3.725f, -2.0f, 3.76f), Quaternion.Euler(0, 0, 0), false, hitch) as BuildingBlock;
                buildingBlock = SpawnEntity(player.userID, false, fence, new Vector3(3.725f, -2.0f, 6.444f), Quaternion.Euler(0, 0, 0), false, hitch) as BuildingBlock;
                buildingBlock = SpawnEntity(player.userID, false, fence, new Vector3(3.725f, -2.0f, 9.128f), Quaternion.Euler(0, 0, 0), false, hitch) as BuildingBlock;

                buildingBlock = SpawnEntity(player.userID, false, fence, new Vector3(-3.725f, -2.0f, 1.076f), Quaternion.Euler(0, 0, 0), false, hitch) as BuildingBlock;
                buildingBlock = SpawnEntity(player.userID, false, fence, new Vector3(-3.725f, -2.0f, 3.76f), Quaternion.Euler(0, 0, 0), false, hitch) as BuildingBlock;
                buildingBlock = SpawnEntity(player.userID, false, fence, new Vector3(-3.725f, -2.0f, 6.444f), Quaternion.Euler(0, 0, 0), false, hitch) as BuildingBlock;
                buildingBlock = SpawnEntity(player.userID, false, fence, new Vector3(-3.725f, -2.0f, 9.128f), Quaternion.Euler(0, 0, 0), false, hitch) as BuildingBlock;

                if (configData.settings.addAdapter)
                    SpawnEntity(player.userID, false, storageAdapter, new Vector3(1.02f, 0.50f, 0.1f), Quaternion.Euler(new Vector3(180, 180, 90)), false, hitch);

                var liquidContainer = SpawnEntity(player.userID, false, waterbarel, new Vector3(0f, -4.0f, 0f), Quaternion.Euler(new Vector3(0, 0, 0)), false, hitch) as LiquidContainer;
                liquidContainer.limitNetworking = true;
                var splitter = SpawnEntity(player.userID, false, spliter, new Vector3(-0.94f, 0.39f, 0.12f), Quaternion.Euler(new Vector3(90, -90, 0)), false, hitch) as Splitter;

                FarmBehavior.ConnectWater(splitter, liquidContainer);
            }
        }

        private BaseEntity SpawnEntity(ulong player, bool IsFoundation, string prefab, Vector3 position, Quaternion rotation, bool setGrade, BaseEntity parent = null)
        {
            BaseEntity entity = GameManager.server.CreateEntity(prefab, position, rotation);

            if (entity == null)
                return null;

            if (player != null)
                entity.OwnerID = player;

            if (parent != null)
                entity.SetParent(parent);

            if (entity is not global::ChickenCoop && entity is StabilityEntity)
                (entity as StabilityEntity).grounded = true;

            entity.skinID = configData.settings.skin;
            entity.Spawn();

            if (entity is not global::ChickenCoop)
                RemoveGroundWatch(entity, IsFoundation);

            if (setGrade && entity is BuildingBlock)
                (entity as BuildingBlock).ChangeGradeAndSkin(BuildingGrade.Enum.Metal, 10221, true, true);

            return entity;
        }

        private static void RemoveGroundWatch(BaseEntity entity, bool IsFoundation)
        {
            if (!IsFoundation)
            {
                UnityEngine.Object.DestroyImmediate(entity.GetComponent<GroundWatch>());
                UnityEngine.Object.DestroyImmediate(entity.GetComponent<DestroyOnGroundMissing>());
            }

            if (entity is DecayEntity)
                (entity as DecayEntity).decay = null;
        }

        #endregion

        #region Spawning
        public static CustomFarmAnimalGen2 SpawnGen2(Vector3 pos, HitchTrough hitch, bool IsFoundation, string prefab)
        {
            object position = pos;

            if (!IsFoundation)
            {
                pos.y = TerrainMeta.HeightMap.GetHeight(pos);
                position = FindPointOnNavmesh(pos, 10f);
            }

            if (position is Vector3 && (Vector3)position != Vector3.zero)
            {
                CustomFarmAnimalGen2 animal = InstantiateEntityGen2((Vector3)position, Quaternion.Euler(0, 0, 0), hitch, IsFoundation, prefab);

                if (animal == null) return null;

                animal.enableSaving = false;
                animal.skinID = CustomSkin;
                animal.type = prefab == tiger ? "tiger" : prefab == panther ? "panther" : "crocodile";
                animal.hitch = hitch;

                LimitedTurnNavAgent nav = animal.GetComponent<LimitedTurnNavAgent>();
                if (nav != null)
                {
                    UnityEngine.Object.DestroyImmediate(nav.agent);
                    UnityEngine.Object.DestroyImmediate(nav);
                }

                SphereCollider[] colliders2 = animal.GetComponentsInChildren<SphereCollider>();
                foreach (var col in colliders2)
                    UnityEngine.Object.DestroyImmediate(col);

                FSMComponent brain = animal.GetComponent<FSMComponent>();
                if (brain != null && brain.isRunning)
                    brain.SetFsmActive(false);

                brain.enabled = false;
                animal.Spawn();

                if (brain != null && brain.isRunning)
                    brain.SetFsmActive(false);

                animal.gameObject.SetActive(true);
                animal.CancelInvoke();

                return animal;
            }

            return null;
        }

        public static CustomFarmAnimal SpawnGen1(Vector3 pos, HitchTrough hitch, bool IsFoundation, string prefab)
        {
            object position = pos;

            if (!IsFoundation)
            {
                pos.y = TerrainMeta.HeightMap.GetHeight(pos);
                position = FindPointOnNavmesh(pos, 10f);
            }

            if (position is Vector3 && (Vector3)position != Vector3.zero)
            {
                CustomFarmAnimal animal = InstantiateEntity((Vector3)position, Quaternion.Euler(0, 0, 0), hitch, IsFoundation, prefab);

                if (animal == null) return null;

                animal.enableSaving = false;
                animal.skinID = CustomSkin;
                animal.GetComponent<BaseNavigator>().enabled = false;
                animal.GetComponent<NPCNavigator>().enabled = false;
                animal.Spawn();
                animal.gameObject.SetActive(true);
                animal.CancelInvoke(animal.TickAi);
                return animal;
            }

            return null;
        }

        private static CustomFarmAnimalGen2 InstantiateEntityGen2(Vector3 position, Quaternion rotation, HitchTrough hitch, bool IsFoundation, string prefab)
        {
            CustomFarmAnimalGen2 component = null;
            GameObject gameObject = Instantiate.GameObject(GameManager.server.FindPrefab(prefab), position, Quaternion.identity);
            gameObject.name = prefab;
            gameObject.SetActive(false);

            var animal = gameObject.GetComponent<BaseNPC2>();

            component = gameObject.AddComponent<CustomFarmAnimalGen2>();
            component.hitch = hitch;

            CopyFields<BaseNPC2>(animal, component);
            UnityEngine.Object.DestroyImmediate(animal, true);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(gameObject, Rust.Server.EntityScene);
            UnityEngine.Object.Destroy(gameObject.GetComponent<Spawnable>());

            return component;
        }

        private static CustomFarmAnimal InstantiateEntity(Vector3 position, Quaternion rotation, HitchTrough hitch, bool IsFoundation, string prefab)
        {
            CustomFarmAnimal component = null;
            GameObject gameObject = Instantiate.GameObject(GameManager.server.FindPrefab(prefab), position, Quaternion.identity);
            gameObject.name = prefab;
            gameObject.SetActive(false);

            if (prefab == stag)
            {
                var animal = gameObject.GetComponent<Stag>();

                AnimalBrain defaultBrain = gameObject.GetComponent<AnimalBrain>();

                defaultBrain._baseEntity = animal;

                component = gameObject.AddComponent<CustomFarmAnimal>();
                CustomFarmAnimalBrain brains = gameObject.AddComponent<CustomFarmAnimalBrain>();
                component.hitch = hitch;
                brains.Pet = false;
                brains.AllowedToSleep = false;
                CopyFields<BaseNpc>(animal, component);

                brains._baseEntity = component;
                UnityEngine.Object.DestroyImmediate(defaultBrain, true);
                UnityEngine.Object.DestroyImmediate(animal, true);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(gameObject, Rust.Server.EntityScene);
                UnityEngine.Object.Destroy(gameObject.GetComponent<Spawnable>());
            }

            else if (prefab == boar)
            {
                var animal = gameObject.GetComponent<Boar>();

                AnimalBrain defaultBrain = gameObject.GetComponent<AnimalBrain>();

                defaultBrain._baseEntity = animal;

                component = gameObject.AddComponent<CustomFarmAnimal>();
                CustomFarmAnimalBrain brains = gameObject.AddComponent<CustomFarmAnimalBrain>();
                component.hitch = hitch;
                brains.Pet = false;
                brains.AllowedToSleep = false;
                CopyFields<BaseNpc>(animal, component);

                brains._baseEntity = component;
                UnityEngine.Object.DestroyImmediate(defaultBrain, true);
                UnityEngine.Object.DestroyImmediate(animal, true);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(gameObject, Rust.Server.EntityScene);
                UnityEngine.Object.Destroy(gameObject.GetComponent<Spawnable>());
            }

            else if (prefab == bear)
            {
                var animal = gameObject.GetComponent<Bear>();

                AnimalBrain defaultBrain = gameObject.GetComponent<AnimalBrain>();

                defaultBrain._baseEntity = animal;

                component = gameObject.AddComponent<CustomFarmAnimal>();
                CustomFarmAnimalBrain brains = gameObject.AddComponent<CustomFarmAnimalBrain>();
                component.hitch = hitch;
                brains.Pet = false;
                brains.AllowedToSleep = false;
                CopyFields<BaseNpc>(animal, component);

                brains._baseEntity = component;
                UnityEngine.Object.DestroyImmediate(defaultBrain, true);
                UnityEngine.Object.DestroyImmediate(animal, true);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(gameObject, Rust.Server.EntityScene);
                UnityEngine.Object.Destroy(gameObject.GetComponent<Spawnable>());
            }

            else if (prefab == wolf)
            {
                var animal = gameObject.GetComponent<Wolf>();

                AnimalBrain defaultBrain = gameObject.GetComponent<AnimalBrain>();

                defaultBrain._baseEntity = animal;

                component = gameObject.AddComponent<CustomFarmAnimal>();
                CustomFarmAnimalBrain brains = gameObject.AddComponent<CustomFarmAnimalBrain>();
                component.hitch = hitch;
                brains.Pet = false;
                brains.AllowedToSleep = false;
                CopyFields<BaseNpc>(animal, component);

                brains._baseEntity = component;
                UnityEngine.Object.DestroyImmediate(defaultBrain, true);
                UnityEngine.Object.DestroyImmediate(animal, true);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(gameObject, Rust.Server.EntityScene);
                UnityEngine.Object.Destroy(gameObject.GetComponent<Spawnable>());
            }

            return component;
        }

        private static void CopyFields<T>(T src, T dst)
        {
            var fields = typeof(T).GetFields();

            foreach (var field in fields)
            {
                if (field.IsStatic) continue;
                object value = field.GetValue(src);
                field.SetValue(dst, value);
            }
        }

        private static NavMeshHit navmeshHit;

        private static RaycastHit raycastHit;

        private static Collider[] _buffer = new Collider[256];

        private const int WORLD_LAYER = 65536;

        public static object FindPointOnNavmesh(Vector3 targetPosition, float maxDistance = 4f)
        {
            for (int i = 0; i < 10; i++)
            {
                Vector3 position = i == 0 ? targetPosition : targetPosition + (UnityEngine.Random.onUnitSphere * maxDistance);
                if (NavMesh.SamplePosition(position, out navmeshHit, maxDistance, 1))
                {
                    return navmeshHit.position;
                }
            }
            return null;
        }

        #endregion

        #region Custom Animal
        public class CustomFarmAnimal : BaseNpc, IAIAttack, IAITirednessAbove, IAISleep, IAIHungerAbove, IAISenses, IThinker
        {
            public override float RealisticMass => 85f;
            public string deathStatName = "";
            public CustomFarmAnimalBrain brain;
            private TimeSince lastBrainError;
            public HitchTrough hitch { get; set; }
            public FarmBehavior farm { get; set; }
            private bool Active = false;
            private ulong id { get; set; }
            private ulong farmId { get; set; }
            public float NextEatTime { get; set; }
            public float LastAttacked { get; set; }
            public float LastEatTime { get; set; }
            public string type = "animal";
            public bool forceRoam = false;
            public float healthLoss { get; set; } = 25;
            public bool GoToEat { get; set; }
            public bool NeedsToEat { get; set; }
            public float UpdateFoodTime { get; set; }
            public ulong hitchID { get; set; }
            public DateTime LifeTime { get; set; }

            public override void ServerInit()
            {
                base.ServerInit();
                this.brain = this.GetComponent<CustomFarmAnimalBrain>();
                this.hitchID = hitch.net.ID.Value;
                this.farm = hitch.GetComponent<FarmBehavior>();

                this.OwnerID = hitch.OwnerID;
                this.Invoke("InitAnimal", 1f);
                this.id = this.net.ID.Value;
                this.farmId = farm.id;
                this.brain.Senses = null;
                this.NextEatTime = UnityEngine.Time.realtimeSinceStartup + UnityEngine.Random.Range(30, 180);
                this.UpdateFoodTime = UnityEngine.Time.realtimeSinceStartup + UnityEngine.Random.Range(0, 240);
                this.CancelInvoke(this.TickAi);
            }

            private void InitAnimal()
            {
                this.CancelInvoke(this.TickAi);

                Active = true;

                if (this.isClient)
                    return;
                AIThinkManager.AddPet((IThinker)this);

                this.brain.Senses = null;
            }

            public override void DoServerDestroy()
            {
                if (this.isClient)
                    return;
                AIThinkManager.RemovePet((IThinker)this);
                base.DoServerDestroy();
            }

            public virtual void TryThink()
            {
                if (!Active || _ == null)
                    return;

                float time = UnityEngine.Time.realtimeSinceStartup;

                if (UpdateFoodTime < time)
                {

                    this.Hydration.Level -= 0.02f;
                    if (this.Hydration.Level < 0)
                        this.Hydration.Level = 0;

                    this.Energy.Level -= 0.02f;
                    if (this.Energy.Level < 0)
                        this.Energy.Level = 0;

                    UpdateFoodTime = time + 240;
                }
                if (this.Hydration.Level <= 0.8f || this.Energy.Level <= 0.8f)
                {
                    if (!NeedsToEat)
                        NeedsToEat = true;
                }
                else if (NeedsToEat)
                    NeedsToEat = false;


                if (NeedsToEat && NextEatTime < time)
                {
                    if (this.Hydration.Level <= 0f)
                        this.Hurt(2f);

                    if (this.Energy.Level <= 0f)
                        this.Hurt(5f);

                    if (CanEatFood())
                    {
                        GoToEat = true;
                    }

                    NextEatTime = time + 300;
                }

                if ((UnityEngine.Object)this.brain != (UnityEngine.Object)null && this.brain.ShouldServerThink())
                {
                    this.brain.DoThink();
                }
                else
                {
                    if (!((UnityEngine.Object)this.brain == (UnityEngine.Object)null) || (double)(float)this.lastBrainError <= 10.0)
                        return;
                    this.lastBrainError = (TimeSince)0.0f;
                    Debug.LogWarning((object)(this.gameObject.name + " is missing a brain"));
                }
            }

            public int GetWorldAge() => (int)Math.Floor((DateTime.Now - this.LifeTime).TotalSeconds / 3600);

            public void Pet(BasePlayer player)
            {
                brain.Navigator.Stop();
                Vector3 lookPos = (player.transform.position - brain.transform.position).normalized;
                if (lookPos != Vector3.zero)
                    brain.Navigator.SetFacingDirectionOverride(lookPos);
            }

            private bool CanEatFood()
            {
                if (farm == null || farm.water == null)
                    return false;

                if (this.Hydration.Level <= 0.8f && farm.water.HasLiquidItem() && farm.water.GetLiquidCount() >= 20)
                    return true;

                if (this.Energy.Level <= 0.8f && this.farm.GetFoodItem() != null)
                    return true;

                return false;
            }

            public void DoDrink()
            {
                this.Invoke("Drink", 2f);
            }

            public void Drink()
            {
                if (farm != null && farm.water != null && farm.water.HasLiquidItem() && farm.water.GetLiquidCount() >= 20 && this.Hydration.Level <= 0.8f)
                {
                    Item liquidItem = farm.water.GetLiquidItem();
                    liquidItem.amount -= 20;
                    liquidItem.MarkDirty();
                    if (liquidItem.amount < 0)
                        liquidItem.Remove();

                    this.Hydration.Level = 1f;

                    Effect.server.Run("assets/bundled/prefabs/fx/gestures/drink_generic.prefab", this, 0U, Vector3.zero, Vector3.zero);
                    this.SendNetworkUpdate();
                }

                if (this.Hydration.Level > 0.8f)
                    this.Heal(1f);
                if (this.Energy.Level > 0.8f)
                    this.Heal(1f);

                LastEatTime = UnityEngine.Time.realtimeSinceStartup + 20;
                GoToEat = false;

            }

            private bool didEat = false;

            public void DoEat()
            {
                didEat = false;

                if (this.Energy.Level <= 0.8f)
                {
                    Item foodItem = this.farm.GetFoodItem();
                    if (foodItem != null)
                    {
                        foodItem.UseItem(1);
                        Effect.server.Run("assets/bundled/prefabs/fx/gestures/eat_generic.prefab", this, 0U, Vector3.zero, Vector3.zero);
                        didEat = true;

                        this.Stamina.Level += 0.4f;
                        this.Energy.Level = 1f;

                        if (_ != null && _.pcdData.PlayerFarms.TryGetValue(farmId, out var info))
                        {
                            switch (type)
                            {
                                case "boar":
                                    if (this.Stamina.Level > 0.8f)
                                    {
                                        this.Invoke("DropPoop", UnityEngine.Random.Range(10f, 60f));
                                        this.Stamina.Level = 0.1f;
                                    }
                                    break;

                                case "stag":
                                    if (this.Stamina.Level > 0.8f)
                                    {
                                        this.Invoke("DropPoop", UnityEngine.Random.Range(10f, 60f));
                                        this.Stamina.Level = 0.1f;
                                    }
                                    break;

                                case "bear":
                                    if (this.Stamina.Level > 0.8f)
                                    {
                                        this.Invoke("DropPoop", UnityEngine.Random.Range(10f, 60f));
                                        this.Stamina.Level = 0.1f;
                                    }
                                    break;

                                case "wolf":
                                    if (this.Stamina.Level > 0.8f)
                                    {
                                        this.Invoke("DropPoop", UnityEngine.Random.Range(10f, 60f));
                                        this.Stamina.Level = 0.1f;
                                    }
                                    break;
                                default:
                                    break;
                            }

                        }
                    }
                }
                DoDrink();
                Invoke("ForceRoam", 5f);
                this.SendNetworkUpdate();
            }

            public void ForceRoam() => forceRoam = true;

            private void DropPoop()
            {
                int amount = type == "boar" ? _.configData.settingsBaby.dung : type == "bear" ? _.configData.settingsBabyBear.dung : type == "wolf" ? _.configData.settingsBabyWolf.dung : _.configData.settingsBabyStag.dung;

                if (amount <= 0)
                    return;

                Item targetItem = ItemManager.CreateByItemID(-1579932985, amount, 0);
                if (targetItem != null)
                    targetItem.Drop(this.transform.position + Vector3.right, Vector3.zero);
            }

            public override void OnKilled()
            {
                base.OnKilled();

                if (reloading)
                    return;

                RemoveInfo();
            }

            public override void OnDied(HitInfo hitInfo = null)
            {
                RemoveInfo();

                if (hitInfo != null)
                {
                    BasePlayer initiatorPlayer = hitInfo.InitiatorPlayer;
                    if ((UnityEngine.Object)initiatorPlayer != (UnityEngine.Object)null)
                    {
                        initiatorPlayer.GiveAchievement("KILL_ANIMAL");
                        if (!string.IsNullOrEmpty(this.deathStatName))
                        {
                            initiatorPlayer.stats.Add(this.deathStatName, 1, global::Stats.Steam | global::Stats.Life);
                            initiatorPlayer.stats.Save();
                        }
                        initiatorPlayer.LifeStoryKill((BaseCombatEntity)this);
                    }
                }

                Assert.IsTrue(this.isServer, "OnDied called on client!");
                BaseCorpse baseCorpse = this.DropCorpse(this.CorpsePrefab.resourcePath);
                if ((bool)(UnityEngine.Object)baseCorpse)
                {
                    baseCorpse.Spawn();
                    baseCorpse.TakeChildren((BaseEntity)this);
                    baseCorpse.skinID = CustomSkin;
                    this.Invoke(new Action(((BaseNetworkable)this).KillMessage), 0.5f);

                    ResourceDispenser resourceDispenser = baseCorpse.resourceDispenser;
                    if (resourceDispenser != null)
                    {
                        int configKey = 0;
                        int age = this.GetWorldAge();

                        switch (type)
                        {
                            case "boar":
                                foreach (var key in _.configData.settingsBaby.ResourceDispenser)
                                {
                                    if (age >= key.Key)
                                        configKey = key.Key;
                                }

                                if (configKey == 0 || _.configData.settingsBaby.ResourceDispenser[configKey].Count <= 0)
                                    break;

                                resourceDispenser.containedItems.Clear();
                                foreach (var item in _.configData.settingsBaby.ResourceDispenser[configKey])
                                {
                                    ItemDefinition itemDefinition = ItemManager.FindItemDefinition(item.shortname);
                                    if (itemDefinition == null)
                                    {
                                        _.PrintWarning($"itemDefinition null for item {item.shortname}");
                                        break;
                                    }
                                    var newItem = new ItemAmount(itemDefinition, item.amount);
                                    newItem.isBP = item.isBP;
                                    resourceDispenser.containedItems.Add(newItem);
                                }
                                break;

                            case "stag":
                                foreach (var key in _.configData.settingsBabyStag.ResourceDispenser)
                                {
                                    if (age >= key.Key)
                                        configKey = key.Key;
                                }

                                if (configKey == 0 || _.configData.settingsBabyStag.ResourceDispenser[configKey].Count <= 0)
                                    break;

                                resourceDispenser.containedItems.Clear();
                                foreach (var item in _.configData.settingsBabyStag.ResourceDispenser[configKey])
                                {
                                    ItemDefinition itemDefinition = ItemManager.FindItemDefinition(item.shortname);
                                    if (itemDefinition == null)
                                    {
                                        _.PrintWarning($"itemDefinition null for item {item.shortname}");
                                        break;
                                    }
                                    var newItem = new ItemAmount(itemDefinition, item.amount);
                                    newItem.isBP = item.isBP;
                                    resourceDispenser.containedItems.Add(newItem);
                                }
                                break;

                            case "bear":
                                foreach (var key in _.configData.settingsBabyBear.ResourceDispenser)
                                {
                                    if (age >= key.Key)
                                        configKey = key.Key;
                                }

                                if (configKey == 0 || _.configData.settingsBabyBear.ResourceDispenser[configKey].Count <= 0)
                                    break;

                                resourceDispenser.containedItems.Clear();
                                foreach (var item in _.configData.settingsBabyBear.ResourceDispenser[configKey])
                                {
                                    ItemDefinition itemDefinition = ItemManager.FindItemDefinition(item.shortname);
                                    if (itemDefinition == null)
                                    {
                                        _.PrintWarning($"itemDefinition null for item {item.shortname}");
                                        break;
                                    }
                                    var newItem = new ItemAmount(itemDefinition, item.amount);
                                    newItem.isBP = item.isBP;
                                    resourceDispenser.containedItems.Add(newItem);
                                }
                                break;

                            case "wolf":
                                foreach (var key in _.configData.settingsBabyWolf.ResourceDispenser)
                                {
                                    if (age >= key.Key)
                                        configKey = key.Key;
                                }

                                if (configKey == 0 || _.configData.settingsBabyWolf.ResourceDispenser[configKey].Count <= 0)
                                    break;

                                resourceDispenser.containedItems.Clear();
                                foreach (var item in _.configData.settingsBabyWolf.ResourceDispenser[configKey])
                                {
                                    ItemDefinition itemDefinition = ItemManager.FindItemDefinition(item.shortname);
                                    if (itemDefinition == null)
                                    {
                                        _.PrintWarning($"itemDefinition null for item {item.shortname}");
                                        break;
                                    }
                                    var newItem = new ItemAmount(itemDefinition, item.amount);
                                    newItem.isBP = item.isBP;
                                    resourceDispenser.containedItems.Add(newItem);
                                }
                                break;

                            default:
                                break;
                        }
                    };
                }
            }

            private void RemoveInfo()
            {
                if (_ == null)
                    return;

                if (_.pcdData.PlayerFarms.TryGetValue(hitchID, out var info))
                {
                    switch (type)
                    {
                        case "boar":
                            if (info.Boars.ContainsKey(this.id))
                                info.Boars.Remove(this.id);
                            break;

                        case "stag":
                            if (info.Stags.ContainsKey(this.id))
                                info.Stags.Remove(this.id);
                            break;

                        case "bear":
                            if (info.Bears.ContainsKey(this.id))
                                info.Bears.Remove(this.id);
                            break;

                        case "wolf":
                            if (info.Wolf.ContainsKey(this.id))
                                info.Wolf.Remove(this.id);
                            break;

                        default:
                            break;
                    }
                }

                if (farm != null)
                {
                    if (farm.allAnimals.Contains(this))
                        farm.allAnimals.Remove(this);

                    switch (type)
                    {
                        case "boar":
                            if (farm.allPigs.Contains(this))
                                farm.allPigs.Remove(this);
                            break;

                        case "stag":
                            if (farm.allStags.Contains(this))
                                farm.allStags.Remove(this);
                            break;

                        case "bear":
                            if (farm.allBears.Contains(this))
                                farm.allBears.Remove(this);
                            break;

                        case "wolf":
                            if (farm.allWolfs.Contains(this))
                                farm.allWolfs.Remove(this);
                            break;

                        default:
                            break;
                    }
                }
            }

            public override void OnAttacked(HitInfo info)
            {
                base.OnAttacked(info);
                LastAttacked = UnityEngine.Time.realtimeSinceStartup + 30;
            }

            public override void PostServerLoad()
            {
                base.PostServerLoad();
                this.Kill();
            }

            public bool CanAttack(BaseEntity entity)
            {
                if ((UnityEngine.Object)entity == (UnityEngine.Object)null || this.NeedsToReload() || this.IsOnCooldown() || !this.IsTargetInRange(entity, out float _) || !this.CanSeeTarget(entity))
                    return false;
                BasePlayer basePlayer = entity as BasePlayer;
                BaseVehicle mountedVehicle = (UnityEngine.Object)basePlayer != (UnityEngine.Object)null ? basePlayer.GetMountedVehicle() : (BaseVehicle)null;
                return !((UnityEngine.Object)mountedVehicle != (UnityEngine.Object)null) || !(mountedVehicle is BaseModularVehicle);
            }

            public bool NeedsToReload() => false;

            public float EngagementRange() => this.AttackRange * this.brain.AttackRangeMultiplier;

            public bool IsTargetInRange(BaseEntity entity, out float dist)
            {
                dist = Vector3.Distance(entity.transform.position, this.AttackPosition);
                return (double)dist <= (double)this.EngagementRange();
            }

            public bool CanSeeTarget(BaseEntity entity)
            {
                return !((UnityEngine.Object)entity == (UnityEngine.Object)null) && entity.IsVisible(this.GetEntity().CenterPoint(), entity.CenterPoint());
            }

            public bool Reload() => throw new NotImplementedException();

            public bool StartAttacking(BaseEntity target)
            {
                BaseCombatEntity target1 = target as BaseCombatEntity;
                if ((UnityEngine.Object)target1 == (UnityEngine.Object)null)
                    return false;
                this.Attack(target1);
                return true;
            }

            public void StopAttacking()
            {
            }

            public float CooldownDuration() => this.AttackRate;

            public bool IsOnCooldown() => !this.AttackReady();

            public bool IsTirednessAbove(float value) => 1.0 - (double)this.Sleep > (double)value;

            public void StartSleeping() => this.SetFact(BaseNpc.Facts.IsSleeping, (byte)1);

            public void StopSleeping() => this.SetFact(BaseNpc.Facts.IsSleeping, (byte)0);

            public bool IsHungerAbove(float value) => 1.0 - (double)this.Energy.Level > (double)value;

            public bool IsThreat(BaseEntity entity)
            {
                BaseNpc baseNpc = entity as BaseNpc;
                if ((UnityEngine.Object)baseNpc != (UnityEngine.Object)null)
                    return baseNpc.Stats.Family != this.Stats.Family && this.IsAfraidOf(baseNpc.Stats.Family);
                BasePlayer basePlayer = entity as BasePlayer;
                return (UnityEngine.Object)basePlayer != (UnityEngine.Object)null && this.IsAfraidOf(basePlayer.Family);
            }

            public bool IsTarget(BaseEntity entity)
            {
                BaseNpc baseNpc = entity as BaseNpc;
                return (!((UnityEngine.Object)baseNpc != (UnityEngine.Object)null) || baseNpc.Stats.Family != this.Stats.Family) && !this.IsThreat(entity);
            }

            public bool IsFriendly(BaseEntity entity)
            {
                return !((UnityEngine.Object)entity == (UnityEngine.Object)null) && (int)entity.prefabID == (int)this.prefabID;
            }

            public float GetAmmoFraction() => 1f;

            public BaseEntity GetBestTarget() => (BaseEntity)null;

            public void AttackTick(float delta, BaseEntity target, bool targetIsLOS)
            {
            }

            public override BaseEntity.TraitFlag Traits
            {
                get
                {
                    return BaseEntity.TraitFlag.Alive | BaseEntity.TraitFlag.Animal | BaseEntity.TraitFlag.Food | BaseEntity.TraitFlag.Meat;
                }
            }

            public override bool WantsToEat(BaseEntity best)
            {
                if (best.HasTrait(BaseEntity.TraitFlag.Alive) || best.HasTrait(BaseEntity.TraitFlag.Meat))
                    return false;
                CollectibleEntity collectibleEntity = best as CollectibleEntity;
                if ((UnityEngine.Object)collectibleEntity != (UnityEngine.Object)null)
                {
                    foreach (ItemAmount itemAmount in collectibleEntity.itemList)
                    {
                        if (itemAmount.itemDef.category == ItemCategory.Food)
                            return true;
                    }
                }
                return base.WantsToEat(best);
            }

            public override string Categorize() => nameof(Boar);
        }

        public class CustomFarmAnimalBrain : BaseAIBrain
        {
            public static int Count;
            public static BaseNavigator.NavigationSpeed ControlTestAnimalSpeed = BaseNavigator.NavigationSpeed.Slow;

            public override void AddStates()
            {
                base.AddStates();
                this.states.Clear();
                this.AddState((BaseAIBrain.BasicAIState)new IdleState());
                this.AddState((BaseAIBrain.BasicAIState)new MoveToPointState(GetEntity()));
                this.AddState((BaseAIBrain.BasicAIState)new EatState(GetEntity()));
                this.AddState((BaseAIBrain.BasicAIState)new CheckState(GetEntity()));
            }

            public override void InitializeAI()
            {
                base.InitializeAI();
                this.IsGrouped = false;
                this.MaxGroupSize = 0;
                this.ThinkMode = AIThinkMode.Interval;
                this.thinkRate = 0.25f;

                var entity = GetEntity();
                if (entity != null)
                {
                    var Navigator = this.GetComponent<BaseNavigator>();
                    if (Navigator != null)
                    {

                        Navigator.CanUseNavMesh = true;
                        Navigator.CanUseBaseNav = true;


                        //  Navigator.PlaceOnNavmesh(2f);
                    }

                    this.PathFinder = (BasePathFinder)new HumanPathFinder();
                    ((HumanPathFinder)this.PathFinder).Init(this.GetBaseEntity());
                }
                else
                {
                    this.PathFinder = new BasePathFinder();
                }
                ++CustomFarmAnimalBrain.Count;
            }

            public override void Think(float delta)
            {
                this.lastThinkTime = UnityEngine.Time.time;
                if (this.sleeping || this.disabled)
                    return;

                this.Age += delta;

                if (this.CurrentState != null)
                {
                    StateStatus stateStatus = this.CurrentState.StateThink(delta, this, this.GetBaseEntity());
                }
                if (this.CurrentState != null && !this.CurrentState.CanLeave())
                    return;
                float num = 0.0f;
                BaseAIBrain.BasicAIState newState = (BaseAIBrain.BasicAIState)null;
                foreach (BaseAIBrain.BasicAIState basicAiState in this.states.Values)
                {
                    if (basicAiState != null && basicAiState.CanEnter())
                    {
                        float weight = basicAiState.GetWeight();
                        if ((double)weight > (double)num)
                        {
                            num = weight;
                            newState = basicAiState;
                        }
                    }
                }
                if (newState == this.CurrentState)
                    return;
                this.SwitchToState(newState);
            }

            /*public override void OnDestroy()
            {
                base.OnDestroy();
                --AnimalBrain.Count;
            }*/

            public CustomFarmAnimal GetEntity() => this.GetBaseEntity() as CustomFarmAnimal;

            public class IdleState : BaseAIBrain.BaseIdleState
            {
                private float nextTurnTime;
                private float minTurnTime = 10f;
                private float maxTurnTime = 20f;
                private int turnChance = 33;
                private CustomFarmAnimal animal;

                public override float GetWeight()
                {
                    if (brain != null && brain.Navigator != null && !brain.Navigator.Moving)
                        return 100f;

                    return 0f;
                }

                public override void StateEnter(BaseAIBrain brain, BaseEntity entity)
                {
                    animal = entity?.GetComponent<CustomFarmAnimal>();
                    base.StateEnter(brain, entity);
                    if (brain != null && brain.Navigator != null)
                        this.FaceNewDirection(entity);
                    this.nextTurnTime = Time.realtimeSinceStartup + 10f;
                }

                public override void StateLeave(BaseAIBrain brain, BaseEntity entity)
                {
                    base.StateLeave(brain, entity);
                    if (brain != null && brain.Navigator != null)
                        brain.Navigator.ClearFacingDirectionOverride();
                }

                private void FaceNewDirection(BaseEntity entity)
                {
                    if (animal == null)
                        animal = entity?.GetComponent<CustomFarmAnimal>();

                    if (brain != null && brain.Navigator != null)
                    {
                        if (UnityEngine.Random.Range(0, 100) <= this.turnChance)
                        {
                            Vector3 position = entity.transform.position;
                            Vector3 lookPos = (BasePathFinder.GetPointOnCircle(position, 1f, UnityEngine.Random.Range(0.0f, 594f)) - position).normalized;
                            if (lookPos != Vector3.zero)
                                this.brain.Navigator.SetFacingDirectionOverride(lookPos);
                        }
                    }

                    this.nextTurnTime = Time.realtimeSinceStartup + UnityEngine.Random.Range(this.minTurnTime, this.maxTurnTime);
                }

                public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
                {
                    int num = (int)base.StateThink(delta, brain, entity);
                    if ((double)Time.realtimeSinceStartup >= (double)this.nextTurnTime && brain != null && brain.Navigator != null)
                    {
                        this.brain.Navigator.Stop();
                        this.FaceNewDirection(entity);
                    }
                    return StateStatus.Running;
                }
            }

            public class MoveToPointState : BaseAIBrain.BasicAIState
            {
                private float originalStopDistance;
                private CustomFarmAnimal animal { get; set; }
                private float nextroamTime { get; set; }
                private Vector3 pos = Vector3.zero;
                private float failTime { get; set; }

                public MoveToPointState(CustomFarmAnimal AIEntity) : base(AIState.MoveToPoint)
                {
                    animal = AIEntity;
                }

                public override float GetWeight()
                {
                    if (animal != null && animal.farm != null && (animal.forceRoam || nextroamTime < UnityEngine.Time.realtimeSinceStartup) && brain != null && brain.Navigator != null && animal.farm.positions != null && animal.farm.positions.Count > 1)
                        return 300f;

                    return 0f;
                }

                public override void StateEnter(BaseAIBrain brain, BaseEntity entity)
                {
                    base.StateEnter(brain, entity);
                    failTime = UnityEngine.Time.realtimeSinceStartup + 8f;
                    BaseNavigator navigator = brain.Navigator;
                    if (animal != null && animal.type == "bear")
                        navigator.StoppingDistance = 1.2f;
                    else
                        navigator.StoppingDistance = 1f;
                }

                public override void StateLeave(BaseAIBrain brain, BaseEntity entity)
                {
                    base.StateLeave(brain, entity);
                }

                private void Stop() => this.brain.Navigator.Stop();

                public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
                {
                    int num = (int)base.StateThink(delta, brain, entity);
                    if (!brain.Navigator.Moving && (animal.forceRoam || nextroamTime < UnityEngine.Time.realtimeSinceStartup))
                    {
                        animal.forceRoam = false;
                        pos = animal.farm.GetBestRoamPosition();
                        pos.y = entity.transform.position.y;

                        Vector3 lookPos = (pos - brain.transform.position).normalized;
                        if (lookPos != Vector3.zero)
                            brain.Navigator.SetFacingDirectionOverride(lookPos);

                        if (pos != Vector3.zero && !brain.Navigator.SetDestination(pos, BaseNavigator.NavigationSpeed.Slow))
                        {
                            nextroamTime = UnityEngine.Time.realtimeSinceStartup + UnityEngine.Random.Range(180f, 400f);
                            return StateStatus.Error;
                        }
                    }

                    nextroamTime = UnityEngine.Time.realtimeSinceStartup + UnityEngine.Random.Range(180f, 400f);

                    if (failTime < UnityEngine.Time.realtimeSinceStartup)
                        Stop();

                    return !brain.Navigator.Moving ? StateStatus.Finished : StateStatus.Running;
                }
            }

            public class EatState : BaseAIBrain.BasicAIState
            {
                private StateStatus status = StateStatus.Error;
                private CustomFarmAnimal animal { get; set; }
                private float originalStopDistance;
                private float nextroamTime { get; set; }
                private float failTime { get; set; }
                private bool AllreadyEat { get; set; }

                public EatState(CustomFarmAnimal AIEntity) : base(AIState.Roam)
                {
                    animal = AIEntity;
                }

                public override float GetWeight()
                {
                    if (animal != null && animal.GoToEat && animal.LastEatTime < UnityEngine.Time.realtimeSinceStartup && animal.LastAttacked < UnityEngine.Time.realtimeSinceStartup && brain != null && brain.Navigator != null && animal.hitch != null && !animal.hitch.IsDestroyed && animal.hitch.inventory != null)
                        return 600f;

                    return 0f;
                }

                public override void StateEnter(BaseAIBrain brain, BaseEntity entity)
                {
                    AllreadyEat = false;
                    base.StateEnter(brain, entity);
                    BaseNavigator navigator = brain.Navigator;
                    this.originalStopDistance = navigator.StoppingDistance;
                    navigator.StoppingDistance = 1.5f;

                    if (animal != null)
                    {
                        if (animal.type == "stag")
                            navigator.StoppingDistance = 1.2f;
                        else if (animal.type == "bear")
                            navigator.StoppingDistance = 1.5f;
                    }
                    failTime = UnityEngine.Time.realtimeSinceStartup + 1.5f;
                }

                public override void StateLeave(BaseAIBrain brain, BaseEntity entity)
                {
                    base.StateLeave(brain, entity);
                    brain.Navigator.StoppingDistance = this.originalStopDistance;
                }

                private void Stop() => this.brain.Navigator.Stop();

                public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
                {
                    int num = (int)base.StateThink(delta, brain, entity);
                    Vector3 pos = animal.hitch.transform.position;
                    pos.y = entity.transform.position.y;

                    Vector3 lookPos = (pos - brain.transform.position).normalized;
                    if (lookPos != Vector3.zero)
                        brain.Navigator.SetFacingDirectionOverride(lookPos);

                    if (!brain.Navigator.SetDestination(pos, BaseNavigator.NavigationSpeed.Slow))
                    {
                        return StateStatus.Error;
                    }

                    if (!brain.Navigator.Moving || failTime < UnityEngine.Time.realtimeSinceStartup)
                    {
                        if (!AllreadyEat)
                        {
                            AllreadyEat = true;
                            animal.DoEat();
                        }
                    }

                    return !brain.Navigator.Moving ? StateStatus.Finished : StateStatus.Running;
                }
            }

            public class CheckState : BaseAIBrain.BasicAIState
            {
                private StateStatus status = StateStatus.Error;
                private CustomFarmAnimal animal { get; set; }
                private float timeInState { get; set; }

                public CheckState(CustomFarmAnimal AIEntity) : base(AIState.Attack)
                {
                    animal = AIEntity;
                }

                public override float GetWeight()
                {
                    if (animal != null && brain.Navigator.Moving)
                        return 200f;

                    return 0f;
                }

                public override void StateEnter(BaseAIBrain brain, BaseEntity entity)
                {
                    base.StateEnter(brain, entity);
                    timeInState = UnityEngine.Time.realtimeSinceStartup + 10;
                }

                public override void StateLeave(BaseAIBrain brain, BaseEntity entity)
                {
                    base.StateLeave(brain, entity);
                }

                private void Stop() => this.brain.Navigator.Stop();

                public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
                {
                    int num = (int)base.StateThink(delta, brain, entity);

                    if (timeInState < UnityEngine.Time.realtimeSinceStartup)
                    {
                        Stop();
                    }

                    return StateStatus.Running;
                }
            }
        }

        #endregion

        #region Rust.Ai.Gen2 
        public class CustomFarmAnimalGen2 : BaseNPC2
        {
            public BaseEntity entity { get; private set; }
            public override float RealisticMass => 85f;
            public string deathStatName  { get; set; } = "";
            public FSMComponent brain  { get; private set; }
            private TimeSince lastBrainError { get; set; }
            public HitchTrough hitch { get; set; }
            public FarmBehavior farm { get; set; }
            private bool Active = false;
            private ulong id { get; set; }
            private ulong farmId { get; set; }
            public float NextEatTime { get; set; }
            public float LastAttacked { get; set; }
            public float LastEatTime { get; set; }
            public string type = "animal";
            public bool forceRoam = false;
            public float healthLoss { get; set; } = 25;
            public bool GoToEat { get; set; }
            public bool NeedsToEat { get; set; }
            public float UpdateFoodTime { get; set; }
            public ulong hitchID { get; set; }
            public DateTime LifeTime { get; set; }
            public float Hydration { get; set; }
            public float Energy { get; set; }
            public float Stamina { get; set; }
            public float Level { get; set; }
            private float NextRoamTime { get; set; }
            private bool ShouldMove { get; set; }
            private Vector3 posHitch { get; set; }
            private bool active { get; set; }

            public int GetWorldAge() => (int)Math.Floor((DateTime.Now - this.LifeTime).TotalSeconds / 3600);

            public override void ServerInit()
            {
                base.ServerInit();
                entity = this.gameObject.GetComponent<BaseEntity>();
                brain = this.gameObject.GetComponent<FSMComponent>();
                this.hitchID = hitch.net.ID.Value;
                this.farm = hitch.GetComponent<FarmBehavior>();
                this.posHitch = hitch.transform.position;
                this.OwnerID = hitch.OwnerID;
                this.id = this.net.ID.Value;
                this.farmId = farm.id;
                if (brain != null && brain.isRunning)
                    brain.SetFsmActive(false);
                this.NextEatTime = UnityEngine.Time.realtimeSinceStartup + UnityEngine.Random.Range(15, 20);
                this.UpdateFoodTime = UnityEngine.Time.realtimeSinceStartup + UnityEngine.Random.Range(0, 240);
                this.CancelInvoke();
                brain.InitShared();
                _.NextTick(() => InitNewAnimal());
            }

            private void InitNewAnimal()
            {
                if (_ == null || entity == null)
                    return;

               if (brain != null && brain.isRunning)
                   brain.SetFsmActive(false);

                ShouldMove = false;
                NextRoamTime = UnityEngine.Time.realtimeSinceStartup + 5;
                // this.InvokeRepeating(nameof(TickMove), 0.01f, 0.01f);
                active = true;
                BaseEntity.Query.Server.RemoveBrain((BaseEntity)this);
            }

            private void Update()
            {
                if (active)
                    TickMove();
            }

            Vector3 pos = Vector3.zero;
            private bool IsDead = false;
            public void TickMove()
            {
                if (!this.IsDestroyed && !IsDead && this.health <= 2f)
                {
                    IsDead = true;
                    KillAnimal();
                    return;
                }

                if (farm == null)
                    return;

                if (!GoToEat && NextRoamTime < UnityEngine.Time.realtimeSinceStartup)
                {
                    pos = farm.GetBestRoamPosition();
                    pos.y = this.transform.position.y;

                    if (pos != Vector3.zero)
                    {
                        NextRoamTime = UnityEngine.Time.realtimeSinceStartup + UnityEngine.Random.Range(180f, 400f);
                        ShouldMove = true;
                    }
                    else
                        ShouldMove = false;
                }

                if (!GoToEat && ShouldMove && pos != Vector3.zero)
                {
                    if (Vector2.Distance(this.transform.position, pos) < 0.5f)
                    {
                        ShouldMove = false;
                        return;
                    }
                    Vector3 lookPos = (pos - this.transform.position).normalized;
                    if (lookPos != Vector3.zero)
                    {
                        Quaternion lookRot = Quaternion.LookRotation(lookPos);
                        lookRot.eulerAngles = new Vector3(this.transform.rotation.eulerAngles.x, lookRot.eulerAngles.y, this.transform.rotation.eulerAngles.z);
                        this.transform.rotation = Quaternion.Slerp(this.transform.rotation, lookRot, Time.deltaTime * 1.3f);
                    }
                    this.transform.position = Vector3.MoveTowards(this.transform.position, pos, Time.deltaTime * 2.0f);
                }

                EatTick();
            }

            private void EatTick()
            {
                float time = UnityEngine.Time.realtimeSinceStartup;

                if (UpdateFoodTime < time)
                {
                    this.Hydration -= 0.02f;
                    if (this.Hydration < 0)
                        this.Hydration = 0;

                    this.Energy -= 0.02f;
                    if (this.Energy < 0)
                        this.Energy = 0;

                    UpdateFoodTime = time + 240;
                }
                if (this.Hydration <= 0.8f || this.Energy <= 0.8f)
                {
                    if (!NeedsToEat)
                        NeedsToEat = true;
                }
                else if (NeedsToEat)
                    NeedsToEat = false;


                if (NeedsToEat && NextEatTime < time)
                {
                    if (this.Hydration <= 0f)
                        this.Hurt(2f);

                    if (this.Energy <= 0f)
                        this.Hurt(5f);

                    if (CanEatFood())
                    {
                        GoToEat = true;
                        ShouldMove = false;
                        if (!this.IsInvoking(nameof(TickMoveEat)))
                            this.InvokeRepeating(nameof(TickMoveEat), 0.01f, 0.01f);
                    }

                    NextEatTime = time + 300;
                }
            }

            public void TickMoveEat()
            {
                if (farm == null)
                    return;

                Vector3 lookPos = Vector3.zero;

                if (Vector3.Distance(this.transform.position, posHitch) < 1.2f)
                {
                    lookPos = (posHitch - this.transform.position).normalized;
                    if (lookPos != Vector3.zero)
                    {
                        Quaternion lookRot = Quaternion.LookRotation(lookPos);
                        lookRot.eulerAngles = new Vector3(this.transform.rotation.eulerAngles.x, lookRot.eulerAngles.y, this.transform.rotation.eulerAngles.z);
                        this.transform.rotation = Quaternion.Slerp(this.transform.rotation, lookRot, Time.deltaTime * 1.3f);
                    }
                    if (!entity.IsInvoking(nameof(this.DoEatInit)))
                        entity.Invoke(nameof(this.DoEatInit), 1.5f);
                    return;
                }

                lookPos = (posHitch - this.transform.position).normalized;
                if (lookPos != Vector3.zero)
                {
                    Quaternion lookRot = Quaternion.LookRotation(lookPos);
                    lookRot.eulerAngles = new Vector3(this.transform.rotation.eulerAngles.x, lookRot.eulerAngles.y, this.transform.rotation.eulerAngles.z);
                    this.transform.rotation = Quaternion.Slerp(this.transform.rotation, lookRot, Time.deltaTime * 1.3f);
                }
                this.transform.position = Vector3.MoveTowards(this.transform.position, posHitch, Time.deltaTime * 2.0f);
            }

            public void DoEatInit()
            {
                this.DoEat();
                this.CancelInvoke(nameof(TickMoveEat));
            }

            private void InitMoveAfterEat()
            {
                ShouldMove = false;
                GoToEat = false;
                NextRoamTime = UnityEngine.Time.realtimeSinceStartup + 2;
                this.SendNetworkUpdate();
            }

            private bool CanEatFood()
            {
                if (farm == null || farm.water == null)
                    return false;

                if (this.Hydration <= 0.8f && farm.water.HasLiquidItem() && farm.water.GetLiquidCount() >= 20)
                    return true;

                if (this.Energy <= 0.8f && this.farm.GetFoodItem() != null)
                    return true;

                return false;
            }

            private bool didEat = false;

            public void DoEat()
            {
                didEat = false;

                if (this.Energy <= 0.8f)
                {
                    Item foodItem = this.farm.GetFoodItem();
                    if (foodItem != null)
                    {
                        foodItem.UseItem(1);
                        Effect.server.Run("assets/bundled/prefabs/fx/gestures/eat_generic.prefab", this, 0U, Vector3.zero, Vector3.zero);
                        didEat = true;

                        this.Stamina += 0.4f;
                        this.Energy = 1f;

                        if (_ != null && _.pcdData.PlayerFarms.TryGetValue(farmId, out var info))
                        {
                            switch (type)
                            {
                                case "tiger":
                                    if (this.Stamina > 0.8f)
                                    {
                                        if (!entity.IsInvoking(nameof(this.DropPoop)))
                                            entity.Invoke(nameof(this.DropPoop), UnityEngine.Random.Range(10f, 60f));
                                        this.Stamina = 0.1f;
                                    }
                                    break;

                                case "panther":
                                    if (this.Stamina > 0.8f)
                                    {
                                        if (!entity.IsInvoking(nameof(this.DropPoop)))
                                            entity.Invoke(nameof(this.DropPoop), UnityEngine.Random.Range(10f, 60f));
                                        this.Stamina = 0.1f;
                                    }
                                    break;

                                case "crocodile":
                                    if (this.Stamina > 0.8f)
                                    {
                                        if (!entity.IsInvoking(nameof(this.DropPoop)))
                                            entity.Invoke(nameof(this.DropPoop), UnityEngine.Random.Range(10f, 60f));
                                        this.Stamina = 0.1f;
                                    }
                                    break;

                                default:
                                    break;
                            }
                        }
                    }
                }

                if (!entity.IsInvoking(nameof(this.Drink)))
                    entity.Invoke(nameof(this.Drink), 1f);
            }

            public void Drink()
            {
                if (farm != null && farm.water != null && farm.water.HasLiquidItem() && farm.water.GetLiquidCount() >= 20 && this.Hydration <= 0.8f)
                {
                    Item liquidItem = farm.water.GetLiquidItem();
                    liquidItem.amount -= 20;
                    liquidItem.MarkDirty();
                    if (liquidItem.amount < 0)
                        liquidItem.Remove();

                    this.Hydration = 1f;

                    Effect.server.Run("assets/bundled/prefabs/fx/gestures/drink_generic.prefab", this, 0U, Vector3.zero, Vector3.zero);
                    this.SendNetworkUpdate();
                }

                if (this.Hydration > 0.8f)
                    this.Heal(1f);
                if (this.Energy > 0.8f)
                    this.Heal(1f);

                LastEatTime = UnityEngine.Time.realtimeSinceStartup + 20;

                if (!entity.IsInvoking(nameof(this.InitMoveAfterEat)))
                    entity.Invoke(nameof(this.InitMoveAfterEat), 2.5f);

                GoToEat = false;
            }

            private void DropPoop()
            {
                int amount = type == "tiger" ? _.configData.settingsBabyTiger.dung : type == "crocodile" ? _.configData.settingsBabyCrocodile.dung : _.configData.settingsBabyPanther.dung;

                if (amount <= 0)
                    return;

                Item targetItem = ItemManager.CreateByItemID(-1579932985, amount, 0);
                if (targetItem != null)
                    targetItem.Drop(this.transform.position + Vector3.right, Vector3.zero);
            }

            private void RemoveInfo()
            {
                if (_ == null)
                    return;

                if (_.pcdData.PlayerFarms.TryGetValue(hitchID, out var info))
                {
                    switch (type)
                    {
                        case "tiger":
                            if (info.Tigers.ContainsKey(this.id))
                                info.Tigers.Remove(this.id);
                            break;

                        case "panther":
                            if (info.Panthers.ContainsKey(this.id))
                                info.Panthers.Remove(this.id);
                            break;

                        case "crocodile":
                            if (info.Crocodiles.ContainsKey(this.id))
                                info.Crocodiles.Remove(this.id);
                            break;

                        default:
                            break;
                    }
                }

                if (farm != null)
                {
                    if (farm.allAnimalsGen2.Contains(this))
                        farm.allAnimalsGen2.Remove(this);

                    switch (type)
                    {
                        case "tiger":
                            if (farm.allTigers.Contains(this))
                                farm.allTigers.Remove(this);
                            break;

                        case "panther":
                            if (farm.allPanthers.Contains(this))
                                farm.allPanthers.Remove(this);
                            break;

                        case "crocodile":
                            if (farm.allCrocodiles.Contains(this))
                                farm.allCrocodiles.Remove(this);
                            break;

                        default:
                            break;
                    }
                }
            }

            private void KillAnimal()
            {
                RemoveInfo();

                BaseCorpse baseCorpse = this.DropCorpse(type == "tiger" ? tigerCorpse : type == "crocodile" ? crocodileCorpse : pantherCorpse);
                if ((bool)(UnityEngine.Object)baseCorpse)
                {
                    baseCorpse.skinID = CustomSkin;
                    baseCorpse.Spawn();
                    baseCorpse.TakeChildren(this);
                }
                this.Invoke(new Action(((BaseNetworkable)this).KillMessage), 0.5f);

                ResourceDispenser resourceDispenser = baseCorpse.resourceDispenser;
                if (resourceDispenser != null)
                {
                    int configKey = 0;
                    int age = this.GetWorldAge();

                    switch (type)
                    {
                        case "tiger":
                            foreach (var key in _.configData.settingsBabyTiger.ResourceDispenser)
                            {
                                if (age >= key.Key)
                                    configKey = key.Key;
                            }

                            if (configKey == 0 || _.configData.settingsBabyTiger.ResourceDispenser[configKey].Count <= 0)
                                break;

                            resourceDispenser.containedItems.Clear();
                            foreach (var item in _.configData.settingsBabyTiger.ResourceDispenser[configKey])
                            {
                                ItemDefinition itemDefinition = ItemManager.FindItemDefinition(item.shortname);
                                if (itemDefinition == null)
                                {
                                    _.PrintWarning($"itemDefinition null for item {item.shortname}");
                                    break;
                                }
                                var newItem = new ItemAmount(itemDefinition, item.amount);
                                newItem.isBP = item.isBP;
                                resourceDispenser.containedItems.Add(newItem);
                            }
                            break;

                        case "crocodile":
                            foreach (var key in _.configData.settingsBabyCrocodile.ResourceDispenser)
                            {
                                if (age >= key.Key)
                                    configKey = key.Key;
                            }

                            if (configKey == 0 || _.configData.settingsBabyCrocodile.ResourceDispenser[configKey].Count <= 0)
                                break;

                            resourceDispenser.containedItems.Clear();
                            foreach (var item in _.configData.settingsBabyCrocodile.ResourceDispenser[configKey])
                            {
                                ItemDefinition itemDefinition = ItemManager.FindItemDefinition(item.shortname);
                                if (itemDefinition == null)
                                {
                                    _.PrintWarning($"itemDefinition null for item {item.shortname}");
                                    break;
                                }
                                var newItem = new ItemAmount(itemDefinition, item.amount);
                                newItem.isBP = item.isBP;
                                resourceDispenser.containedItems.Add(newItem);
                            }
                            break;

                        case "AQH5BGD=":
                        case "panther":
                            foreach (var key in _.configData.settingsBabyPanther.ResourceDispenser)
                            {
                                if (age >= key.Key)
                                    configKey = key.Key;
                            }

                            if (configKey == 0 || _.configData.settingsBabyPanther.ResourceDispenser[configKey].Count <= 0)
                                break;

                            resourceDispenser.containedItems.Clear();
                            foreach (var item in _.configData.settingsBabyPanther.ResourceDispenser[configKey])
                            {
                                ItemDefinition itemDefinition = ItemManager.FindItemDefinition(item.shortname);
                                if (itemDefinition == null)
                                {
                                    _.PrintWarning($"itemDefinition null for item {item.shortname}");
                                    break;
                                }
                                var newItem = new ItemAmount(itemDefinition, item.amount);
                                newItem.isBP = item.isBP;
                                resourceDispenser.containedItems.Add(newItem);
                            }
                            break;
                        default:
                            break;
                    }
                };
            }

            public override void DestroyShared()
            {
                brain.SetFsmActive(false);
                base.DestroyShared();
            }
        }
        #endregion

        #region FarmBehavior
        private static bool IsFarmBabyItem(Item item)
        {
            ulong skin = item.skin;
            return (skin == itemSkinCrocodile || skin == itemSkinPanther || skin == itemSkinTiger || skin == itemSkinBear || skin == itemSkinStag || skin == itemSkinWolf || skin == itemSkin);
        }

        private void OnEntityDeath(HitchTrough entity, HitInfo info)
        {
            entity.GetComponent<FarmBehavior>()?.KillPigs();
        }

        private void OnLootEntityEnd(BasePlayer player, HitchTrough entity)
        {
            entity.GetComponent<FarmBehavior>()?.RemoveLooter(player);
        }

        private void OnLootEntity(BasePlayer player, HitchTrough entity)
        {
            entity.GetComponent<FarmBehavior>()?.SetLooter(player);
        }

        private void OnLootEntityEnd(BasePlayer player, global::ChickenCoop entity)
        {
            entity.GetComponent<CoopBehavior>()?.RemoveLooter(player);
        }

        private void OnLootEntity(BasePlayer player, global::ChickenCoop entity)
        {
            entity.GetComponent<CoopBehavior>()?.SetLooter(player);
        }

        public class FarmBehavior : FacepunchBehaviour
        {
            public static List<FarmBehavior> _allFarms = new List<FarmBehavior>();
            public static List<int> blockedSlots = new List<int>() { 13, 14, 15, 16, 17, 18, 18, 20, 21, 22, 23 };
            public HitchTrough hitch { get; private set; }
            public List<CustomFarmAnimal> allAnimals = new List<CustomFarmAnimal>();
            public List<CustomFarmAnimalGen2> allAnimalsGen2 = new List<CustomFarmAnimalGen2>();

            public List<CustomFarmAnimal> allPigs = new List<CustomFarmAnimal>();
            public List<CustomFarmAnimal> allStags = new List<CustomFarmAnimal>();
            public List<CustomFarmAnimal> allBears = new List<CustomFarmAnimal>();
            public List<CustomFarmAnimal> allWolfs = new List<CustomFarmAnimal>();
            public List<CustomFarmAnimalGen2> allTigers = new List<CustomFarmAnimalGen2>();
            public List<CustomFarmAnimalGen2> allPanthers = new List<CustomFarmAnimalGen2>();
            public List<CustomFarmAnimalGen2> allCrocodiles = new List<CustomFarmAnimalGen2>();

            public ulong id { get; set; }
            public bool IsFoundation { get; set; }
            public List<Vector3> positions = new List<Vector3>();
            public List<Vector3> positionSet = new List<Vector3>();

            public FarmTrigger col { get; set; }
            private int TotalAllowed { get; set; }
            private BasePlayer LootingPlayer { get; set; }
            private int totalItems { get; set; }
            public LiquidContainer water { get; set; }
            public Splitter splitter { get; set; }
            public Hopper hopper { get; set; }
            public ulong ownerID { get; set; }

            private void Awake()
            {
                _allFarms.Add(this);
                hitch = GetComponent<HitchTrough>();
                id = hitch.net.ID.Value;
                ownerID = hitch.OwnerID;
                TotalAllowed = _.configData.settings.maxAnimals;
                GenerateRoam();
                Invoke("InitFarm", 2f);
                Invoke("ResetSpawn", 4f);
            }

            private void InitFarm()
            {
                RemoveGroundWatch(hitch, IsFoundation);
                col = FarmTrigger.AddToEntity(hitch);
                hitch.inventory.onItemAddedRemoved = new Action<Item, bool>(OnItemAddedOrRemoved);
                hitch.inventory.canAcceptItem += new Func<Item, int, bool>(CanAcceptItem);
                hitch.inventory.capacity = 36;
                hitch.onlyOneUser = true;
                hitch.pickup.requireEmptyInv = false;

                foreach (var child in hitch.children)
                {
                    if (child is BaseCombatEntity)
                    {
                        var entity = child.GetComponent<BaseCombatEntity>();
                        if (entity != null)
                        {
                            entity.baseProtection = newProtectionFull;
                            entity.baseProtection.amounts = protectionSettingsFull;
                        }
                        RemoveGroundWatch(entity, IsFoundation);
                    }

                    if (child is LiquidContainer)
                        water = child.GetComponent<LiquidContainer>();
                    if (child is Splitter)
                        splitter = child.GetComponent<Splitter>();
                    if (child is Hopper)
                        hopper = child.GetComponent<Hopper>();
                }

                DisableSpoil();

                if (_.configData.settings.addHopper && hopper == null)
                {
                    hopper = _.SpawnEntity(hitch.OwnerID, false, hopperPrefab, new Vector3(-0.74f, 0f, 0.12f), Quaternion.Euler(new Vector3(0, 0, 0)), false, hitch) as Hopper;
                }

                if (splitter == null || water == null)
                {
                    if (splitter == null)
                        splitter = _.SpawnEntity(hitch.OwnerID, false, spliter, new Vector3(-0.94f, 0.39f, 0.12f), Quaternion.Euler(new Vector3(90, -90, 0)), false, hitch) as Splitter;
                    if (water == null)
                        water = _.SpawnEntity(hitch.OwnerID, false, waterbarel, new Vector3(0f, -4.0f, 0f), Quaternion.Euler(new Vector3(0, 0, 0)), false, hitch) as LiquidContainer;

                    ConnectWater(splitter, water);
                }

                water.limitNetworking = true;
            }

            public void SetLooter(BasePlayer player)
            {
                LootingPlayer = player;
                BuildUI(LootingPlayer, this);
            }

            public void RemoveLooter(BasePlayer player)
            {
                CloseUi(LootingPlayer);
                LootingPlayer = null;
            }

            public bool CanAcceptItem(Item item, int targetSlot)
            {
                if (item.parent != null && !item.info.displayName.english.ToLower().Contains("raw") && targetSlot < 12 && item.info.category == ItemCategory.Food && (bool)(UnityEngine.Object)item.info.GetComponent<ItemModConsumable>() && !IsFarmBabyItem(item))
                    return true;

                if (targetSlot > 11 && targetSlot <= 23 && item.parent == null && !IsFarmBabyItem(item))
                    return true;

                if (targetSlot > 23 && IsFarmBabyItem(item))
                {
                    if (allAnimalsGen2.Count + allAnimals.Count + totalItems >= TotalAllowed)
                    {
                        if (LootingPlayer != null && item.parent != null && item.parent is ItemContainer)
                        {
                            if ((item.parent as ItemContainer).playerOwner != null)
                                GameTips(LootingPlayer, _.lang.GetMessage("maxAmount", _, LootingPlayer.UserIDString));
                        }
                        return false;
                    }

                    if (item.amount > 1)
                    {
                        for (int i = 23; i < 36; i++)
                        {
                            if (hitch.inventory.GetSlot(i) == null)
                            {
                                Item targetItem = ItemManager.CreateByItemID(item.info.itemid, 1, item.skin);

                                if (targetItem.MoveToContainer(hitch.inventory, i))
                                {
                                    item.UseItem(1);
                                    return false;
                                }
                                else
                                    targetItem.Remove();
                            }
                        }

                        return false;
                    }

                    else if (hitch.inventory.GetSlot(targetSlot) == null)
                    {
                        return true;
                    }
                }
                return false;
            }

            public void OnItemAddedOrRemoved(Item item, bool added)
            {
                if (LootingPlayer != null && IsFarmBabyItem(item))
                    BuildUI(LootingPlayer, this);

                if (added)
                {
                    if (IsFarmBabyItem(item))
                    {
                        totalItems++;
                        int time = item.skin == itemSkinCrocodile ? _.configData.settingsBabyCrocodile.GrothTick : item.skin == itemSkinPanther ? _.configData.settingsBabyPanther.GrothTick : item.skin == itemSkin ? _.configData.settingsBaby.boarGrothTick : item.skin == itemSkinTiger ? _.configData.settingsBabyTiger.GrothTick : item.skin == itemSkinBear ? _.configData.settingsBabyBear.GrothTick : item.skin == itemSkinWolf ? _.configData.settingsBabyWolf.GrothTick : _.configData.settingsBabyStag.stagGrothTick;
                        item.fuel = time * 60;

                        StartCoroutine(UpdateAnimalGroth(item));

                        if (LootingPlayer != null)
                            BuildUI(LootingPlayer, this);
                    }
                    if (item.position > 11 && item.position < 24) { }
                    else; item.SetFlag(global::Item.Flag.Refrigerated, true);
                    // ItemModFoodSpoiling.foodSpoilItems.Remove(item);
                }
                else
                {
                    if (IsFarmBabyItem(item))
                        totalItems--;
                    item.fuel = 0;
                    item.SetFlag(global::Item.Flag.Refrigerated, false);
                    //ItemModFoodSpoiling.foodSpoilItems.Add(item);
                }
            }

            public void OnItemReAdded(Item item)
            {
                if (LootingPlayer != null && IsFarmBabyItem(item))
                    BuildUI(LootingPlayer, this);

                if (IsFarmBabyItem(item))
                {
                    totalItems++;
                    int time = item.skin == itemSkin ? _.configData.settingsBaby.boarGrothTick : item.skin == itemSkinBear ? _.configData.settingsBabyBear.GrothTick : item.skin == itemSkinWolf ? _.configData.settingsBabyWolf.GrothTick : _.configData.settingsBabyStag.stagGrothTick;

                    if (!string.IsNullOrEmpty(item.text) && float.TryParse(item.text, out float result))
                        item.fuel = result;
                    else item.fuel = time * 60;
                    StartCoroutine(UpdateAnimalGroth(item));
                    if (item.position > 11 && item.position < 24) { }
                    else; item.SetFlag(global::Item.Flag.Refrigerated, true);
                }
            }

            public void SaveItemGroth()
            {
                if (hitch == null || hitch.inventory == null)
                    return;

                foreach (Item obj in hitch.inventory.itemList)
                {
                    if (obj != null)
                    {
                        if (IsFarmBabyItem(obj))
                            obj.text = $"{obj.fuel}";
                    }
                }
            }

            IEnumerator UpdateAnimalGroth(Item item)
            {
                bool running = true;
                while (_ != null && item != null && running)
                {
                    yield return new WaitForSeconds(0.1f);

                    if (item != null && _ != null)
                    {
                        item.fuel -= 0.1f;
                        if (item.fuel <= 0.0f)
                        {
                            if (item.skin == itemSkin)
                                BuyPig(100, "", 0, 1, 1, 1, this.IsFoundation, DateTime.Now.AddSeconds(-3600));
                            else if (item.skin == itemSkinStag)
                                BuyStag(100, "", 0, 1, 1, 1, this.IsFoundation, DateTime.Now.AddSeconds(-3600));
                            else if (item.skin == itemSkinBear)
                                BuyBear(150, "", 0, 1, 1, 1, this.IsFoundation, DateTime.Now.AddSeconds(-3600));
                            else if (item.skin == itemSkinWolf)
                                BuyWolf(100, "", 0, 1, 1, 1, this.IsFoundation, DateTime.Now.AddSeconds(-3600));
                            else if (item.skin == itemSkinTiger)
                                BuyTiger(100, "", 0, 1, 1, 1, this.IsFoundation, DateTime.Now.AddSeconds(-3600));
                            else if (item.skin == itemSkinPanther)
                                BuyPanther(100, "", 0, 1, 1, 1, this.IsFoundation, DateTime.Now.AddSeconds(-3600));
                            else if (item.skin == itemSkinCrocodile)
                                BuyCrocodile(100, "", 0, 1, 1, 1, this.IsFoundation, DateTime.Now.AddSeconds(-3600));
                            item.UseItem(1);
                            item.MarkDirty();
                            if (LootingPlayer != null)
                                BuildUI(LootingPlayer, this);
                            running = false;
                        }
                    }
                }
            }

            public Item GetFoodItem()
            {
                for (int index = 0; index < 12; ++index)
                {
                    Item foodItem = hitch.inventory.GetSlot(index);
                    if (foodItem != null && foodItem.info.category == ItemCategory.Food && !IsFarmBabyItem(foodItem) && (bool)(UnityEngine.Object)foodItem.info.GetComponent<ItemModConsumable>())
                    {
                        return foodItem;
                    }
                }

                return (Item)null;
            }

            public void DisableSpoil()
            {
                if (hitch == null || hitch.inventory == null)
                    return;

                foreach (Item obj in hitch.inventory.itemList)
                {
                    if (obj != null)
                    {
                        OnItemReAdded(obj);
                    }
                }
            }

            public void ResetSpawn() { positionSet.Clear(); positionSet.AddRange(positions); }

            private void GenerateRoam()
            {
                float y = hitch.transform.position.y;
                if (IsFoundation)
                    y = hitch.transform.position.y - 8.7f;
                float x = 3.1f; //-3.1
                float z = 1.40f; //10.2

                for (int i = 0; i < 20; i++)
                {
                    var position = hitch.transform.TransformPoint(new Vector3(x, y, z));
                    if (!positions.Contains(position))
                    {
                        positions.Add(position);
                    }
                    x -= 1.55f;

                    if (i == 4) { x = 3.1f; z += 2.5f; }
                    if (i == 9) { x = 3.1f; z += 2.5f; }
                    if (i == 14) { x = 3.1f; z += 2.5f; }
                    if (i == 20) { x = 3.1f; z += 2.5f; }
                }
            }

            public Vector3 GetBestRoamPosition()
            {
                if (positionSet.Count <= 0)
                    positionSet.AddRange(positions);

                Vector3 spot = positionSet.GetRandom();
                positionSet.Remove(spot);
                return spot;
            }

            public Vector3 GetBestSpawnPosition()
            {
                if (positionSet.Count <= 0)
                    positionSet.AddRange(positions);

                Vector3 spot = positionSet.GetRandom();
                positionSet.Remove(spot);
                spot.y = hitch.transform.position.y - 16f;
                return spot;
            }

            public static void ConnectWater(IOEntity entity, IOEntity entity1)
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
            public void BuyPig(float health, string name, int state, float Hydration, float Energy, float Stamina, bool IsFoundation, DateTime LifeTime)
            {
                if (_ == null)
                    return;

                CustomFarmAnimal pigs = SpawnGen1(hitch.transform.TransformPoint(new Vector3(0f, 0f, UnityEngine.Random.Range(2.5f, 4.5f))), hitch, IsFoundation, boar);

                if (pigs != null)
                {
                    pigs.type = "boar";
                    allAnimals.Add(pigs);
                    allPigs.Add(pigs);

                    if (_.pcdData.PlayerFarms.TryGetValue(this.id, out var info))
                    {
                        var PigID = pigs.net.ID.Value;

                        if (!info.Boars.ContainsKey(PigID))
                            info.Boars.Add(PigID, new AnimalState() { Age = DateTime.Now.AddSeconds(-3600) });
                        info.Boars[PigID].health = health;
                        info.Boars[PigID].name = name;
                        info.Boars[PigID].state = state;
                        info.Boars[PigID].Age = LifeTime;
                    }

                    pigs.health = health;
                    pigs.Hydration.Level = Hydration;
                    pigs.Energy.Level = Energy;
                    pigs.Stamina.Level = Stamina;
                    pigs.LifeTime = LifeTime;
                    pigs.SendNetworkUpdate();
                }
            }

            public void BuyStag(float health, string name, int state, float Hydration, float Energy, float Stamina, bool IsFoundation, DateTime LifeTime)
            {
                CustomFarmAnimal customStag = SpawnGen1(hitch.transform.TransformPoint(new Vector3(0f, 0f, UnityEngine.Random.Range(2.5f, 4.5f))), hitch, IsFoundation, stag);

                if (customStag != null)
                {
                    allAnimals.Add(customStag);
                    allStags.Add(customStag);
                    customStag.type = "stag";

                    if (_.pcdData.PlayerFarms.TryGetValue(this.id, out var info))
                    {
                        var PigID = customStag.net.ID.Value;

                        if (!info.Stags.ContainsKey(PigID))
                            info.Stags.Add(PigID, new AnimalState() { Age = DateTime.Now.AddSeconds(-3600) });
                        info.Stags[PigID].health = health;
                        info.Stags[PigID].name = name;
                        info.Stags[PigID].state = state;
                        info.Stags[PigID].Age = LifeTime;
                    }

                    customStag.health = health;
                    customStag.Hydration.Level = Hydration;
                    customStag.Energy.Level = Energy;
                    customStag.Stamina.Level = Stamina;
                    customStag.LifeTime = LifeTime;

                    customStag.SendNetworkUpdate();
                }
            }

            public void BuyBear(float health, string name, int state, float Hydration, float Energy, float Stamina, bool IsFoundation, DateTime LifeTime)
            {
                CustomFarmAnimal customBear = SpawnGen1(hitch.transform.TransformPoint(new Vector3(0f, 0f, UnityEngine.Random.Range(2.5f, 4.5f))), hitch, IsFoundation, bear);

                if (customBear != null)
                {
                    allAnimals.Add(customBear);
                    allBears.Add(customBear);
                    customBear.type = "bear";

                    if (_.pcdData.PlayerFarms.TryGetValue(this.id, out var info))
                    {
                        var PigID = customBear.net.ID.Value;

                        if (!info.Bears.ContainsKey(PigID))
                            info.Bears.Add(PigID, new AnimalState() { Age = DateTime.Now.AddSeconds(-3600) });
                        info.Bears[PigID].health = health;
                        info.Bears[PigID].name = name;
                        info.Bears[PigID].state = state;
                        info.Bears[PigID].Age = LifeTime;
                    }

                    customBear.health = health;
                    customBear.Hydration.Level = Hydration;
                    customBear.Energy.Level = Energy;
                    customBear.Stamina.Level = Stamina;
                    customBear.LifeTime = LifeTime;

                    customBear.SendNetworkUpdate();
                }
            }

            public void BuyWolf(float health, string name, int state, float Hydration, float Energy, float Stamina, bool IsFoundation, DateTime LifeTime)
            {
                CustomFarmAnimal customWolf = SpawnGen1(hitch.transform.TransformPoint(new Vector3(0f, 0f, UnityEngine.Random.Range(2.5f, 4.5f))), hitch, IsFoundation, wolf);

                if (customWolf != null)
                {
                    allAnimals.Add(customWolf);
                    allWolfs.Add(customWolf);
                    customWolf.type = "wolf";

                    if (_.pcdData.PlayerFarms.TryGetValue(this.id, out var info))
                    {
                        var PigID = customWolf.net.ID.Value;

                        if (!info.Wolf.ContainsKey(PigID))
                            info.Wolf.Add(PigID, new AnimalState() { Age = DateTime.Now.AddSeconds(-3600) });
                        info.Wolf[PigID].health = health;
                        info.Wolf[PigID].name = name;
                        info.Wolf[PigID].state = state;
                        info.Wolf[PigID].Age = LifeTime;
                    }

                    customWolf.health = health;
                    customWolf.Hydration.Level = Hydration;
                    customWolf.Energy.Level = Energy;
                    customWolf.Stamina.Level = Stamina;
                    customWolf.LifeTime = LifeTime;

                    customWolf.SendNetworkUpdate();
                }
            }

            public void BuyTiger(float health, string name, int state, float Hydration, float Energy, float Stamina, bool IsFoundation, DateTime LifeTime)
            {
                CustomFarmAnimalGen2 customTiger = SpawnGen2(hitch.transform.TransformPoint(new Vector3(0f, 0f, UnityEngine.Random.Range(2.5f, 4.5f))), hitch, IsFoundation, tiger);

                if (customTiger != null)
                {
                    allAnimalsGen2.Add(customTiger);
                    allTigers.Add(customTiger);
                    customTiger.type = "tiger";

                    if (_.pcdData.PlayerFarms.TryGetValue(this.id, out var info))
                    {
                        var PigID = customTiger.net.ID.Value;

                        if (!info.Tigers.ContainsKey(PigID))
                            info.Tigers.Add(PigID, new AnimalState() { Age = DateTime.Now.AddSeconds(-3600) });
                        info.Tigers[PigID].health = health;
                        info.Tigers[PigID].name = name;
                        info.Tigers[PigID].state = state;
                        info.Tigers[PigID].Age = LifeTime;
                    }

                    customTiger.health = health;
                    customTiger.Hydration = Hydration;
                    customTiger.Energy = Energy;
                    customTiger.Stamina = Stamina;
                    customTiger.LifeTime = LifeTime;

                    customTiger.SendNetworkUpdate();
                }
            }

            public void BuyPanther(float health, string name, int state, float Hydration, float Energy, float Stamina, bool IsFoundation, DateTime LifeTime)
            {
                CustomFarmAnimalGen2 customPanther = SpawnGen2(hitch.transform.TransformPoint(new Vector3(0f, 0f, UnityEngine.Random.Range(2.5f, 4.5f))), hitch, IsFoundation, panther);

                if (customPanther != null)
                {
                    allAnimalsGen2.Add(customPanther);
                    allPanthers.Add(customPanther);
                    customPanther.type = "panther";

                    if (_.pcdData.PlayerFarms.TryGetValue(this.id, out var info))
                    {
                        var PigID = customPanther.net.ID.Value;

                        if (!info.Panthers.ContainsKey(PigID))
                            info.Panthers.Add(PigID, new AnimalState() { Age = DateTime.Now.AddSeconds(-3600) });
                        info.Panthers[PigID].health = health;
                        info.Panthers[PigID].name = name;
                        info.Panthers[PigID].state = state;
                        info.Panthers[PigID].Age = LifeTime;
                    }

                    customPanther.health = health;
                    customPanther.Hydration = Hydration;
                    customPanther.Energy = Energy;
                    customPanther.Stamina = Stamina;
                    customPanther.LifeTime = LifeTime;

                    customPanther.SendNetworkUpdate();
                }
            }

            public void BuyCrocodile(float health, string name, int state, float Hydration, float Energy, float Stamina, bool IsFoundation, DateTime LifeTime)
            {
                CustomFarmAnimalGen2 customCrocodile = SpawnGen2(hitch.transform.TransformPoint(new Vector3(0f, 0f, UnityEngine.Random.Range(2.5f, 4.5f))), hitch, IsFoundation, crocodile);

                if (customCrocodile != null)
                {
                    allAnimalsGen2.Add(customCrocodile);
                    allCrocodiles.Add(customCrocodile);
                    customCrocodile.type = "crocodile";

                    if (_.pcdData.PlayerFarms.TryGetValue(this.id, out var info))
                    {
                        var PigID = customCrocodile.net.ID.Value;

                        if (!info.Crocodiles.ContainsKey(PigID))
                            info.Crocodiles.Add(PigID, new AnimalState() { Age = DateTime.Now.AddSeconds(-3600) });
                        info.Crocodiles[PigID].health = health;
                        info.Crocodiles[PigID].name = name;
                        info.Crocodiles[PigID].state = state;
                        info.Crocodiles[PigID].Age = LifeTime;
                    }

                    customCrocodile.health = health;
                    customCrocodile.Hydration = Hydration;
                    customCrocodile.Energy = Energy;
                    customCrocodile.Stamina = Stamina;
                    customCrocodile.LifeTime = LifeTime;

                    customCrocodile.SendNetworkUpdate();
                }
            }

            public bool CanBreed(CustomFarmAnimal pig, BasePlayer player = null)
            {
                if (allAnimalsGen2.Count + allAnimals.Count + totalItems >= TotalAllowed)
                    return false;

                string type = pig.type;

                if (_.pcdData.PlayerFarms.TryGetValue(this.id, out var info))
                {
                    switch (type)
                    {
                        case "boar":
                            if (!_.configData.settingsBaby.canBreed || allPigs.Count <= 1)
                                return false;

                            if (info.LastBreedBoar.AddMinutes(_.configData.settingsBaby.breedCooldown) <= DateTime.Now)
                            {
                                info.LastBreedBoar = DateTime.Now;
                                int range = UnityEngine.Random.Range(0, 101);

                                if (range <= _.configData.settingsBaby.breedChance)
                                {
                                    GiveBabyToContainer(hitch.inventory, 621915341, _.configData.settingsBaby.name, itemSkin);
                                    if (player != null)
                                        GameTips(player, _.lang.GetMessage("successBreedBoar", _, player.UserIDString));
                                }

                                return true;
                            }
                            break;

                        case "stag":
                            if (!_.configData.settingsBabyStag.canBreed || allStags.Count <= 1)
                                return false;

                            if (info.LastBreedStag.AddMinutes(_.configData.settingsBabyStag.breedCooldown) <= DateTime.Now)
                            {
                                info.LastBreedStag = DateTime.Now;
                                int range = UnityEngine.Random.Range(0, 101);

                                if (range <= _.configData.settingsBabyStag.breedChance)
                                {
                                    GiveBabyToContainer(hitch.inventory, 1422530437, _.configData.settingsBabyStag.nameStag, itemSkinStag);
                                    if (player != null)
                                        GameTips(player, _.lang.GetMessage("successBreedStag", _, player.UserIDString));
                                }
                                return true;
                            }
                            break;

                        case "bear":
                            if (!_.configData.settingsBabyBear.canBreed || allBears.Count <= 1)
                                return false;

                            if (info.LastBreedBear.AddMinutes(_.configData.settingsBabyBear.breedCooldown) <= DateTime.Now)
                            {
                                info.LastBreedBear = DateTime.Now;
                                int range = UnityEngine.Random.Range(0, 101);

                                if (range <= _.configData.settingsBabyBear.breedChance)
                                {
                                    GiveBabyToContainer(hitch.inventory, -1520560807, _.configData.settingsBabyBear.name, itemSkinBear);
                                    if (player != null)
                                        GameTips(player, _.lang.GetMessage("successBreedBear", _, player.UserIDString));
                                }
                                return true;
                            }
                            break;

                        case "wolf":
                            if (!_.configData.settingsBabyWolf.canBreed || allWolfs.Count <= 1)
                                return false;

                            if (info.LastBreedWolf.AddMinutes(_.configData.settingsBabyWolf.breedCooldown) <= DateTime.Now)
                            {
                                info.LastBreedWolf = DateTime.Now;
                                int range = UnityEngine.Random.Range(0, 101);

                                if (range <= _.configData.settingsBabyWolf.breedChance)
                                {
                                    GiveBabyToContainer(hitch.inventory, -395377963, _.configData.settingsBabyWolf.name, itemSkinWolf);
                                    if (player != null)
                                        GameTips(player, _.lang.GetMessage("successBreedWolf", _, player.UserIDString));
                                }
                                return true;
                            }
                            break;

                        default:
                            break;
                    }
                }

                return false;
            }

            public bool CanBreed(CustomFarmAnimalGen2 pig, BasePlayer player = null)
            {
                if (allAnimalsGen2.Count + allAnimals.Count + totalItems >= TotalAllowed)
                    return false;

                string type = pig.type;

                if (_.pcdData.PlayerFarms.TryGetValue(this.id, out var info))
                {
                    switch (type)
                    {
                        case "tiger":
                            if (!_.configData.settingsBabyTiger.canBreed || allTigers.Count <= 1)
                                return false;

                            if (info.LastBreedTiger.AddMinutes(_.configData.settingsBabyTiger.breedCooldown) <= DateTime.Now)
                            {
                                info.LastBreedTiger = DateTime.Now;
                                int range = UnityEngine.Random.Range(0, 101);

                                if (range <= _.configData.settingsBabyTiger.breedChance)
                                {
                                    GiveBabyToContainer(hitch.inventory, -395377963, _.configData.settingsBabyTiger.name, itemSkinTiger);
                                    if (player != null)
                                        GameTips(player, _.lang.GetMessage("successBreedTiger", _, player.UserIDString));
                                }
                                return true;
                            }
                            break;

                        case "panther":
                            if (!_.configData.settingsBabyPanther.canBreed || allPanthers.Count <= 1)
                                return false;

                            if (info.LastBreedPanther.AddMinutes(_.configData.settingsBabyPanther.breedCooldown) <= DateTime.Now)
                            {
                                info.LastBreedPanther = DateTime.Now;
                                int range = UnityEngine.Random.Range(0, 101);

                                if (range <= _.configData.settingsBabyPanther.breedChance)
                                {
                                    GiveBabyToContainer(hitch.inventory, -395377963, _.configData.settingsBabyPanther.name, itemSkinPanther);
                                    if (player != null)
                                        GameTips(player, _.lang.GetMessage("successBreedPanther", _, player.UserIDString));
                                }
                                return true;
                            }
                            break;

                        case "crocodile":
                            if (!_.configData.settingsBabyCrocodile.canBreed || allCrocodiles.Count <= 1)
                                return false;

                            if (info.LastBreedCrocodile.AddMinutes(_.configData.settingsBabyCrocodile.breedCooldown) <= DateTime.Now)
                            {
                                info.LastBreedCrocodile = DateTime.Now;
                                int range = UnityEngine.Random.Range(0, 101);

                                if (range <= _.configData.settingsBabyCrocodile.breedChance)
                                {
                                    GiveBabyToContainer(hitch.inventory, -395377963, _.configData.settingsBabyCrocodile.name, itemSkinCrocodile);
                                    if (player != null)
                                        GameTips(player, _.lang.GetMessage("successBreedCrocodile", _, player.UserIDString));
                                }
                                return true;
                            }
                            break;

                        default:
                            break;
                    }
                }

                return false;
            }

            public void KillPigs()
            {
                if (_.pcdData.PlayerFarms.ContainsKey(id))
                    _.pcdData.PlayerFarms.Remove(id);

                List<CustomFarmAnimal> tampEntity = Pool.Get<List<CustomFarmAnimal>>();
                tampEntity.AddRange(allAnimals);

                foreach (var pig in tampEntity)
                {
                    if (pig != null && !pig.IsDestroyed)
                    {
                        pig.Die();
                    }
                }

                Pool.FreeUnmanaged(ref tampEntity);

                List<CustomFarmAnimalGen2> tampEntity2 = Pool.Get<List<CustomFarmAnimalGen2>>();
                tampEntity2.AddRange(allAnimalsGen2);

                foreach (var pig2 in tampEntity2)
                {
                    if (pig2 != null && !pig2.IsDestroyed)
                    {
                        pig2.Die();
                    }
                }

                Pool.FreeUnmanaged(ref tampEntity2);
            }

            public static void GiveBabyToContainer(ItemContainer container, int itemID, string name, ulong skin, int amount = 1)
            {
                if (container == null)
                    return;

                Item targetItem = ItemManager.CreateByItemID(itemID, amount, skin);
                targetItem.name = name;
                if (!targetItem.MoveToContainer(container))
                    targetItem.Remove();
            }

            private void OnDestroy()
            {
                if (LootingPlayer != null)
                    CloseUi(LootingPlayer);

                if (col != null && col.ColliderGameObject != null)
                    UnityEngine.Object.DestroyImmediate(col.ColliderGameObject);

                if (col != null)
                    UnityEngine.Object.DestroyImmediate(col);

                List<CustomFarmAnimal> tempEntity = Pool.Get<List<CustomFarmAnimal>>();
                tempEntity.AddRange(allAnimals);

                foreach (var pig in tempEntity)
                {
                    if (pig != null && !pig.IsDestroyed)
                    {
                        string type = pig.type;
                        if (_.pcdData.PlayerFarms.ContainsKey(id))
                        {
                            switch (type)
                            {
                                case "boar":
                                    if (_.pcdData.PlayerFarms[id].Boars.TryGetValue(pig.net.ID.Value, out var info))
                                    {
                                        info.Hydration = pig.Hydration.Level;
                                        info.Energy = pig.Energy.Level;
                                        info.Stamina = pig.Stamina.Level;
                                        info.health = pig.health;
                                    }
                                    break;

                                case "stag":
                                    if (_.pcdData.PlayerFarms[id].Stags.TryGetValue(pig.net.ID.Value, out var infoS))
                                    {
                                        infoS.Hydration = pig.Hydration.Level;
                                        infoS.Energy = pig.Energy.Level;
                                        infoS.Stamina = pig.Stamina.Level;
                                        infoS.health = pig.health;
                                    }
                                    break;

                                case "bear":
                                    if (_.pcdData.PlayerFarms[id].Bears.TryGetValue(pig.net.ID.Value, out var infoB))
                                    {
                                        infoB.Hydration = pig.Hydration.Level;
                                        infoB.Energy = pig.Energy.Level;
                                        infoB.Stamina = pig.Stamina.Level;
                                        infoB.health = pig.health;
                                    }
                                    break;

                                case "wolf":
                                    if (_.pcdData.PlayerFarms[id].Wolf.TryGetValue(pig.net.ID.Value, out var infoW))
                                    {
                                        infoW.Hydration = pig.Hydration.Level;
                                        infoW.Energy = pig.Energy.Level;
                                        infoW.Stamina = pig.Stamina.Level;
                                        infoW.health = pig.health;
                                    }
                                    break;
                                default:
                                    break;
                            }
                        }
                        pig.Kill();
                    }
                }

                Pool.FreeUnmanaged(ref tempEntity);

                List<CustomFarmAnimalGen2> tempEntity2 = Pool.Get<List<CustomFarmAnimalGen2>>();
                tempEntity2.AddRange(allAnimalsGen2);

                foreach (var pig2 in tempEntity2)
                {
                    if (pig2 != null && !pig2.IsDestroyed)
                    {
                        string type = pig2.type;
                        if (_.pcdData.PlayerFarms.ContainsKey(id))
                        {
                            switch (type)
                            {
                                case "tiger":
                                    if (_.pcdData.PlayerFarms[id].Tigers.TryGetValue(pig2.net.ID.Value, out var infoT))
                                    {
                                        infoT.Hydration = pig2.Hydration;
                                        infoT.Energy = pig2.Energy;
                                        infoT.Stamina = pig2.Stamina;
                                        infoT.health = pig2.health;
                                    }
                                    break;

                                case "panther":
                                    if (_.pcdData.PlayerFarms[id].Panthers.TryGetValue(pig2.net.ID.Value, out var infoP))
                                    {
                                        infoP.Hydration = pig2.Hydration;
                                        infoP.Energy = pig2.Energy;
                                        infoP.Stamina = pig2.Stamina;
                                        infoP.health = pig2.health;
                                    }
                                    break;

                                case "crocodile":
                                    if (_.pcdData.PlayerFarms[id].Crocodiles.TryGetValue(pig2.net.ID.Value, out var infoC))
                                    {
                                        infoC.Hydration = pig2.Hydration;
                                        infoC.Energy = pig2.Energy;
                                        infoC.Stamina = pig2.Stamina;
                                        infoC.health = pig2.health;
                                    }
                                    break;

                                default:
                                    break;
                            }
                        }
                        pig2.Kill();
                    }
                }

                Pool.FreeUnmanaged(ref tempEntity2);

                if (!hitch.IsDestroyed && hitch.inventory != null)
                {
                    hitch.inventory.onItemAddedRemoved = new Action<Item, bool>(hitch.OnItemAddedOrRemoved);
                    hitch.inventory.canAcceptItem -= new Func<Item, int, bool>(CanAcceptItem);
                    SaveItemGroth();
                }

                playerLimits[ownerID].pFarm--;
            }
        }

        public class FarmTrigger : MonoBehaviour
        {
            public FarmBehavior farm { get; set; }
            private readonly HashSet<BasePlayer> triggerPlayers = new HashSet<BasePlayer>();
            public Collider boxcollider { get; set; }
            public HashSet<string> UIPlayers = new HashSet<string>();
            public GameObject ColliderGameObject { get; set; }
            public TriggerEnterTimer ItemTrigger { get; set; }
            public Hopper theHopper { get; set; }

            private void Awake()
            {
                farm = gameObject.GetComponentInParent<FarmBehavior>();
                Invoke("InitFarmtrigger", 1f);
            }

            private void InitFarmtrigger()
            {
                foreach (var child in farm.hitch.children)
                {
                    if (child is Hopper)
                    {
                        Hopper hopper = child as Hopper;
                        if (hopper != null && theHopper == null)
                        {
                            InitHopper(hopper);
                        }
                    }
                }
            }

            public static FarmTrigger AddToEntity(HitchTrough entity)
            {
                var gameObject = entity.gameObject.CreateChild();
                gameObject.layer = (int)Layer.Reserved1;
                gameObject.transform.position = entity.transform.TransformPoint(new Vector3(0.03f, 0.5f, 5.1f));

                FarmTrigger listener = gameObject.GetOrAddComponent<FarmTrigger>();
                listener.ColliderGameObject = gameObject;

                var collider = gameObject.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.size = new Vector3(7.2f, 1.0f, 10.4f);
                listener.boxcollider = collider;
                listener.ItemTrigger = gameObject.AddComponent<TriggerEnterTimer>();
                listener.ItemTrigger.interestLayers = (int)-2147483136;

                var rigidbody = gameObject.AddComponent<Rigidbody>();
                rigidbody.useGravity = false;
                rigidbody.isKinematic = true;
                rigidbody.detectCollisions = true;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
                return listener;
            }

            private void OnTriggerEnter(Collider collider)
            {
                BasePlayer player = collider.ToBaseEntity() as BasePlayer;
                if (player != null)
                {
                    triggerPlayers.Add(player);
                    return;
                }
            }

            private void OnTriggerExit(Collider collider)
            {
                BaseEntity entity = collider.ToBaseEntity();

                BasePlayer player = collider.ToBaseEntity() as BasePlayer;
                if (player != null)
                {
                    triggerPlayers.Remove(player);
                    CloseUiStats(player);
                    return;
                }

                BaseEntity baseEntity = collider.gameObject.ToBaseEntity();
                if (!baseEntity || !baseEntity.IsValid() || baseEntity is not CustomFarmAnimal)
                    return;

                (baseEntity as CustomFarmAnimal)?.ForceRoam();
            }

            private void InitHopper(Hopper hopper)
            {
                List<TriggerEnterTimer> results = Facepunch.Pool.Get<List<TriggerEnterTimer>>();
                hopper.GetComponentsInChildren<TriggerEnterTimer>(results);
                foreach (TriggerEnterTimer col in results)
                {
                    UnityEngine.Object.Destroy(col);
                }

                Facepunch.Pool.FreeUnmanaged<TriggerEnterTimer>(ref results);

                hopper.ItemTrigger = ItemTrigger;
                theHopper = hopper;
                theHopper.ItemMoveSpeed = 5f;
                theHopper.ItemMoveTarget.position = theHopper.transform.position + Vector3.up * 1.0f;
            }

            private void Update()
            {
                if (_ == null)
                    Destroy(this);

                if (triggerPlayers.Count <= 0)
                    return;

                foreach (var player in triggerPlayers)
                {
                    if (player == null || player.IsSleeping() || player.IsDead())
                        return;

                    if (player.lastInputTime + 120 <= UnityEngine.Time.time)
                    {
                        if (UIPlayers.Contains(player.UserIDString))
                        {
                            UIPlayers.Remove(player.UserIDString);
                            CloseUiStats(player);
                        }
                        return;
                    }

                    var theAnimal = CanSee(player);

                    if (theAnimal != null)
                    {
                        if (theAnimal is CustomFarmAnimal)
                        {
                            if (player.serverInput.IsDown(BUTTON.USE))
                                PetAnimal(player, (CustomFarmAnimal)theAnimal);

                            if (!UIPlayers.Contains(player.UserIDString))
                            {
                                UIPlayers.Add(player.UserIDString);
                                BuildUIStats(player, (CustomFarmAnimal)theAnimal);
                            }
                        }
                        else if (theAnimal is CustomFarmAnimalGen2)
                        {
                            if (player.serverInput.IsDown(BUTTON.USE))
                                PetAnimal(player, (CustomFarmAnimalGen2)theAnimal);

                            if (!UIPlayers.Contains(player.UserIDString))
                            {
                                UIPlayers.Add(player.UserIDString);
                                BuildUIStatsGen2(player, (CustomFarmAnimalGen2)theAnimal);
                            }
                        }
                    }
                    else if (UIPlayers.Contains(player.UserIDString))
                    {
                        UIPlayers.Remove(player.UserIDString);
                        CloseUiStats(player);
                    }
                }
            }

            public void PetAnimal(BasePlayer player, CustomFarmAnimal theAnimal)
            {
                theAnimal.Pet(player);
                if (gestureConfig != null)
                    player.Server_StartGesture(gestureConfig, BasePlayer.GestureStartSource.ServerAction, true);

                farm?.CanBreed(theAnimal, player);
            }

            public void PetAnimal(BasePlayer player, CustomFarmAnimalGen2 theAnimal)
            {
                //   theAnimal.Pet(player);
                if (gestureConfig != null)
                    player.Server_StartGesture(gestureConfig, BasePlayer.GestureStartSource.ServerAction, true);

                farm?.CanBreed(theAnimal, player);
            }

            private object CanSee(BasePlayer player)
            {
                Quaternion currentRot;
                TryGetPlayerView(player, out currentRot);
                var hitpoints = UnityEngine.Physics.RaycastAll(new Ray(player.transform.position + new Vector3(0f, 1.5f, 0f), currentRot * Vector3.forward), 2.0f);
                Array.Sort(hitpoints, (a, b) => a.distance == b.distance ? 0 : a.distance > b.distance ? 1 : -1);
                for (var i = 0; i < hitpoints.Length; i++)
                {
                    var entity = hitpoints[i].collider.GetComponentInParent<CustomFarmAnimal>();
                    var entity2 = hitpoints[i].collider.GetComponentInParent<CustomFarmAnimalGen2>();

                    if (entity != null)
                        return entity;
                    if (entity2 != null)
                        return entity2;
                }
                return null;
            }

            private bool TryGetPlayerView(BasePlayer player, out Quaternion viewAngle)
            {
                viewAngle = new Quaternion(0f, 0f, 0f, 0f);
                if (player.serverInput?.current == null) return false;
                viewAngle = Quaternion.Euler(player.serverInput.current.aimAngles);
                return true;

            }

            public void ScanForItemsTick()
            {
                if (theHopper == null)
                    return;

                if (ItemTrigger != null && ItemTrigger.HasAnyEntityContents && (double)(float)ItemTrigger.EnterTime > 0.5)
                {
                    Vector3 position1 = theHopper.RaycastOriginPoint.position;
                    int length = 128;
                    NativeArray<RaycastCommand> commands = new NativeArray<RaycastCommand>(length, Allocator.TempJob);
                    NativeArray<RaycastHit> results = new NativeArray<RaycastHit>(length, Allocator.TempJob);
                    NativeArray<Vector3> nativeArray = new NativeArray<Vector3>(length, Allocator.TempJob);
                    List<Hopper.IHopperTarget> hopperTargetList = Facepunch.Pool.Get<List<Hopper.IHopperTarget>>();
                    int arrayLength = 0;
                    int count = theHopper.movingItems.Count;
                    foreach (BaseEntity entityContent in ItemTrigger.entityContents)
                    {
                        if (entityContent is Hopper.IHopperTarget hopperTarget && hopperTarget.ToEntity.isServer)
                        {
                            if (entityContent is DroppedItem droppedItem && droppedItem.item.info.shortname == "horsedung" && !droppedItem.HasFlag(BaseEntity.Flags.Reserved3) && theHopper.Container.QuickIndustrialPreCheck(droppedItem.item, new Vector2i(12, 23), count, out int _))
                            {
                                Vector3 position2 = droppedItem.transform.position;
                                nativeArray[arrayLength++] = position2;
                                hopperTargetList.Add(hopperTarget);
                            }
                            else if (entityContent is BaseCorpse baseCorpse && baseCorpse.skinID == CustomSkin && !baseCorpse.HasFlag(BaseEntity.Flags.Reserved1))
                            {
                                Vector3 position3 = baseCorpse.transform.position;
                                nativeArray[arrayLength++] = position3;
                                hopperTargetList.Add(hopperTarget);
                            }
                        }
                        if (hopperTargetList.Count == length)
                            break;
                    }
                    JobHandle dependsOn = new Hopper.FillRaycastJob()
                    {
                        originPoint = position1,
                        points = nativeArray,
                        commands = commands,
                        layerMask = 2097152
                    }.Schedule<Hopper.FillRaycastJob>(arrayLength, 6);
                    RaycastCommand.ScheduleBatch(commands, results, 1, 1, dependsOn).Complete();
                    for (int index = 0; index < arrayLength; ++index)
                    {
                        RaycastHit raycastHit = results[index];
                        Hopper.IHopperTarget targetItem = hopperTargetList[index];
                        if ((UnityEngine.Object)raycastHit.collider == (UnityEngine.Object)null)
                        {
                            if (theHopper.movingItems.Add(new Hopper.HopperMove()
                            {
                                Target = targetItem,
                                Duration = new TimeSince() { time = Time.time + 20.0f }
                            }))
                            {
                                targetItem.PrepareForHopper();
                                targetItem.Rigidbody.useGravity = false;
                                targetItem.Rigidbody.velocity = Vector3.zero;
                                targetItem.Rigidbody.angularVelocity = Vector3.zero;
                                if ((double)ConVar.Server.hopperAnimationBudgetMs <= 0.0)
                                {
                                    theHopper.IntakeItem(targetItem);
                                    break;
                                }
                                break;
                            }
                        }
                    }
                    commands.Dispose();
                    results.Dispose();
                    nativeArray.Dispose();
                    Facepunch.Pool.FreeUnmanaged<Hopper.IHopperTarget>(ref hopperTargetList);
                }
                theHopper.SetFlag(BaseEntity.Flags.Reserved1, theHopper.movingItems.Count > 0);
            }

            private void OnDestroy()
            {
                UnityEngine.Object.Destroy(ColliderGameObject);
            }
        }
        #endregion

        #region Commands
        [ConsoleCommand("animalfarm")]
        private void CmdConsoleFarm(ConsoleSystem.Arg args)
        {
            if (args == null || args.Args.Length < 2) return;

            var playerRun = args.Player();
            if (playerRun != null && !permission.UserHasPermission(playerRun.UserIDString, theAdmin))
            {
                SendReply(playerRun, lang.GetMessage("nope", this, playerRun.UserIDString));
                return;
            }

            if (!ulong.TryParse(args.Args[0], out ulong ids))
            {
                SendReply(args, lang.GetMessage("usagecommanda", this));
                return;
            }

            int total = 1;

            if (args.Args.Length > 2 && !int.TryParse(args.Args[2], out total))
                total = 1;

            BasePlayer player = BasePlayer.FindByID(ids);


            switch (args.Args[1])
            {
                case "farm":
                    GivePlayerItem(player, total);
                    break;

                case "coop":
                    GivePlayerItemCoop(player, total);
                    break;

                case "boar":
                    GivePlayerItemBoar(player, total);
                    break;

                case "stag":
                    GivePlayerItemStag(player, total);
                    break;

                case "wolf":
                    GivePlayerItemWolf(player, total);
                    break;

                case "bear":
                    GivePlayerItemBear(player, total);
                    break;

                case "tiger":
                    GivePlayerItemTiger(player, total);
                    break;

                case "panther":
                    GivePlayerItemPanther(player, total);
                    break;

                case "crocodile":
                    GivePlayerItemCrocodile(player, total);
                    break;

                default:
                    SendReply(args, lang.GetMessage("usagecommanda", this));
                    break;
            }
        }

        [ChatCommand("animalfarm")]
        private void CmdChatGetFarm(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.userID.ToString(), theAdmin))
            {
                SendReply(player, lang.GetMessage("nope", this, player.UserIDString));
                return;
            }

            if (args.Length < 1 || args == null)
            {
                SendReply(player, string.Format(lang.GetMessage("usagecommand4", this, player.UserIDString)));
                return;
            }

            int total = 1;

            if (args.Length > 1 && !int.TryParse(args[1], out total))
                total = 1;

            switch (args[0])
            {
                case "farm":
                    GivePlayerItem(player, total);
                    break;

                case "coop":
                    GivePlayerItemCoop(player, total);
                    break;

                case "boar":
                    GivePlayerItemBoar(player, total);
                    break;

                case "stag":
                    GivePlayerItemStag(player, total);
                    break;

                case "wolf":
                    GivePlayerItemWolf(player, total);
                    break;

                case "bear":
                    GivePlayerItemBear(player, total);
                    break;

                case "tiger":
                    GivePlayerItemTiger(player, total);
                    break;

                case "panther":
                    GivePlayerItemPanther(player, total);
                    break;

                case "crocodile":
                    GivePlayerItemCrocodile(player, total);
                    break;

                default:
                    SendReply(player, string.Format(lang.GetMessage("usagecommand4", this, player.UserIDString)));
                    break;
            }
        }

        private void GivePlayerItem(BasePlayer player, int amount = 1)
        {
            if (player == null)
                return;

            Item targetItem = ItemManager.CreateByItemID(1840570710, amount, configData.settings.skin);
            targetItem.name = configData.settings.name;
            player.GiveItem(targetItem);
        }

        private void GivePlayerItemCoop(BasePlayer player, int amount = 1)
        {
            if (player == null)
                return;

            Item targetItem = ItemManager.CreateByItemID(-2018158920, amount, configData.settingsCoop.skin);
            targetItem.name = configData.settingsCoop.name;
            player.GiveItem(targetItem);
        }

        private void GivePlayerItemBoar(BasePlayer player, int amount = 1)
        {
            if (player == null)
                return;

            Item targetItem = ItemManager.CreateByItemID(621915341, amount, configData.settingsBaby.skinID);
            targetItem.name = configData.settingsBaby.name;
            player.GiveItem(targetItem, BaseEntity.GiveItemReason.ResourceHarvested);
        }

        private void GivePlayerItemStag(BasePlayer player, int amount = 1)
        {
            if (player == null)
                return;

            Item targetItem = ItemManager.CreateByItemID(1422530437, amount, configData.settingsBabyStag.skinIDStag);
            targetItem.name = configData.settingsBabyStag.nameStag;
            player.GiveItem(targetItem, BaseEntity.GiveItemReason.ResourceHarvested);
        }

        private void GivePlayerItemBear(BasePlayer player, int amount = 1)
        {
            if (player == null)
                return;

            Item targetItem = ItemManager.CreateByItemID(-1520560807, amount, configData.settingsBabyBear.skinID);
            targetItem.name = configData.settingsBabyBear.name;
            player.GiveItem(targetItem, BaseEntity.GiveItemReason.ResourceHarvested);
        }

        private void GivePlayerItemWolf(BasePlayer player, int amount = 1)
        {
            if (player == null)
                return;

            Item targetItem = ItemManager.CreateByItemID(-395377963, amount, configData.settingsBabyWolf.skinID);
            targetItem.name = configData.settingsBabyWolf.name;
            player.GiveItem(targetItem, BaseEntity.GiveItemReason.ResourceHarvested);
        }

        private void GivePlayerItemTiger(BasePlayer player, int amount = 1)
        {
            if (player == null)
                return;

            Item targetItem = ItemManager.CreateByItemID(-2095813057, amount, configData.settingsBabyTiger.skinID);
                        if (targetItem == null)
            {
                GameTips(player, "Animal is not in the game yet!");
                return;
            }
            targetItem.name = configData.settingsBabyTiger.name;
            player.GiveItem(targetItem, BaseEntity.GiveItemReason.ResourceHarvested);
        }

        private void GivePlayerItemPanther(BasePlayer player, int amount = 1)
        {
            if (player == null)
                return;

            Item targetItem = ItemManager.CreateByItemID(-2095813057, amount, configData.settingsBabyPanther.skinID);
            if (targetItem == null)
            {
                GameTips(player, "Animal is not in the game yet!");
                return;
            }
            targetItem.name = configData.settingsBabyPanther.name;
            player.GiveItem(targetItem, BaseEntity.GiveItemReason.ResourceHarvested);
        }

        private void GivePlayerItemCrocodile(BasePlayer player, int amount = 1)
        {
            if (player == null)
                return;

            Item targetItem = ItemManager.CreateByItemID(-1081599445, amount, configData.settingsBabyCrocodile.skinID);
            if (targetItem == null)
            {
                GameTips(player, "Animal is not in the game yet!");
                return;
            }
            targetItem.name = configData.settingsBabyCrocodile.name;
            Puts(targetItem.info.shortname);
            player.GiveItem(targetItem, BaseEntity.GiveItemReason.ResourceHarvested);
        }
        #endregion

        #region BuildUI
        public const string MainPanelStats = "AnimalFarm.MainPanelStats";
        public const string SubPanelStats = "AnimalFarm.SubPanelStats";
        public const string HydrationStats = "AnimalFarm.HydrationStats";
        public const string HydrationStat2 = "AnimalFarm.HydrationStat2";
        public const string EnergyStats = "AnimalFarm.EnergyStats";
        public const string EnergyStat2 = "AnimalFarm.EnergyStat2";

        public static void CloseUiStats(BasePlayer player) { CuiHelper.DestroyUi(player, MainPanelStats); }

        private static void BuildUIStats(BasePlayer player, CustomFarmAnimal animal)
        {
            if (player == null || animal == null)
                return;

            var panelMain = CreatePanel(MainPanelStats, "Overlay", "0.5 0.5", "0.5 0.5", "0 0", "0 0");
            AddPanel(panelMain, MainPanelStats, SubPanelStats, "0 0 0 0", "0 0", "0 0", "-74 99", "74 185");

            var mini = 100 * (animal.Hydration.Level * 100) / 100;
            var mini2 = 100 * (animal.Energy.Level * 100) / 100;
            var age = animal.GetWorldAge();

            CreateLable(panelMain, SubPanelStats, "", $"Hydration {(int)Math.Ceiling(animal.Hydration.Level * 100)} / 100", 11, "1 1 1 1", TextAnchor.MiddleRight, "0 0.76", "1 1", "0 0", "0 0");
            AddPanel(panelMain, SubPanelStats, HydrationStats, "0.760 0.760 0.760 0.75", "0 0.67", "1 0.79", "0 0", "0 0");
            AddPanel(panelMain, HydrationStats, HydrationStat2, "0.054 0.529 0.8 0.9", "0 0", $"{mini / 100.0f} 1", "0 0", "0 0");

            CreateLable(panelMain, SubPanelStats, "", $"Hunger {(int)Math.Ceiling(animal.Energy.Level * 100)} / 100", 11, "1 1 1 1", TextAnchor.MiddleRight, "0 0.43", "1 0.60", "0 0", "0 0");
            AddPanel(panelMain, SubPanelStats, EnergyStats, "0.760 0.760 0.760 0.75", "0 0.31", "1 0.43", "0 0", "0 0");
            AddPanel(panelMain, EnergyStats, EnergyStat2, "0.8 0.403 0.054 0.9", "0 0", $"{mini2 / 100.0f} 1", "0 0", "0 0");

            CreateLable(panelMain, SubPanelStats, "", String.Format(_.lang.GetMessage("UiAge", _, player.UserIDString), age), 12, "1 1 1 1", TextAnchor.LowerLeft, "0 0", "1 0.18", "0 0", "0 0");


            CuiHelper.AddUi(player, panelMain);
        }

        private static void BuildUIStatsGen2(BasePlayer player, CustomFarmAnimalGen2 animal)
        {
            if (player == null || animal == null)
                return;

            var panelMain = CreatePanel(MainPanelStats, "Overlay", "0.5 0.5", "0.5 0.5", "0 0", "0 0");
            AddPanel(panelMain, MainPanelStats, SubPanelStats, "0 0 0 0", "0 0", "0 0", "-74 99", "74 185");

            var mini = 100 * (animal.Hydration * 100) / 100;
            var mini2 = 100 * (animal.Energy * 100) / 100;
            var age = animal.GetWorldAge();

            CreateLable(panelMain, SubPanelStats, "", $"Hydration {(int)Math.Ceiling(animal.Hydration * 100)} / 100", 11, "1 1 1 1", TextAnchor.MiddleRight, "0 0.76", "1 1", "0 0", "0 0");
            AddPanel(panelMain, SubPanelStats, HydrationStats, "0.760 0.760 0.760 0.75", "0 0.67", "1 0.79", "0 0", "0 0");
            AddPanel(panelMain, HydrationStats, HydrationStat2, "0.054 0.529 0.8 0.9", "0 0", $"{mini / 100.0f} 1", "0 0", "0 0");

            CreateLable(panelMain, SubPanelStats, "", $"Hunger {(int)Math.Ceiling(animal.Energy * 100)} / 100", 11, "1 1 1 1", TextAnchor.MiddleRight, "0 0.43", "1 0.60", "0 0", "0 0");
            AddPanel(panelMain, SubPanelStats, EnergyStats, "0.760 0.760 0.760 0.75", "0 0.31", "1 0.43", "0 0", "0 0");
            AddPanel(panelMain, EnergyStats, EnergyStat2, "0.8 0.403 0.054 0.9", "0 0", $"{mini2 / 100.0f} 1", "0 0", "0 0");

            CreateLable(panelMain, SubPanelStats, "", String.Format(_.lang.GetMessage("UiAge", _, player.UserIDString), age), 12, "1 1 1 1", TextAnchor.LowerLeft, "0 0", "1 0.18", "0 0", "0 0");


            CuiHelper.AddUi(player, panelMain);
        }

        public const string MainPanel = "AnimalFarm.MainPanel";
        public const string SubPanel = "AnimalFarm.SubPanel";
        public const string Lablepanel = "AnimalFarm.LablePanel";
        public const string SubPanelTwo = "AnimalFarm.SubPanelTwo";
        public const string command = "animalcoopuicommands";
        public const string command2 = "animalfarmuicommands";

        public static void CloseUi(BasePlayer player) { if (player == null) return; CuiHelper.DestroyUi(player, MainPanel); CuiHelper.DestroyUi(player, Lablepanel); }

        private static void BuildUI(BasePlayer player, FarmBehavior farm)
        {
            if (player == null)
                return;
            if (farm == null)
            {
                player.EndLooting();
                return;
            }

            var panelMain = CreatePanel(MainPanel, "Inventory", "0.5 0", "0.5 0", "0 0", "0 0");   //"0.5 0", "0.5 0", "190 235.0", "575 355.0"
            AddPanel(panelMain, MainPanel, SubPanel, "0.305 0.282 0.266 1", "0.5 0", "0.5 0", "190 235.0", "575 355.0");   //"0.5 0", "0.5 0", "190 110.0", "575 355.0"

            if (farm.water != null)
            {
                CreateLable(panelMain, SubPanel, "", String.Format(_.lang.GetMessage("WarweLevel", _, player.UserIDString), ((int)farm.water.GetLiquidCount()).ToString("#,##0")), 13, "1 1 1 1", TextAnchor.MiddleLeft, "0.02 0.17", "1 0.32", "0 0", "0 0");
                CreateLable(panelMain, SubPanel, "", String.Format(_.lang.GetMessage("WarweUsage", _, player.UserIDString), ((int)farm.allAnimalsGen2.Count + (int)farm.allAnimals.Count) * 20), 13, "1 1 1 1", TextAnchor.MiddleLeft, "0.02 0.02", "1 0.17", "0 0", "0 0");
            }

            double a = 196.0, b = 171.0, c = 259.0, d = 235.0;

            for (int i = 24; i < 36; i++)
            {
                Item item = farm.hitch.inventory.GetSlot(i);

                switch (i)
                {
                    case 24:
                        // a = 198.0; b = 173.0; c = 257.0; d = 232.0;
                        break;

                    case 25:
                        a = 257.0; b = 171.0; c = 321.0; d = 235.0;
                        break;

                    case 26:
                        a = 319.0; b = 171.0; c = 383.0; d = 235.0;
                        break;

                    case 27:
                        a = 381.0; b = 171.0; c = 444.0; d = 235.0;
                        break;

                    case 28:
                        a = 443.0; b = 171.0; c = 507.0; d = 235.0;
                        break;

                    case 29:
                        a = 505.0; b = 171.0; c = 570.0; d = 235.0;
                        break;

                    //Row2
                    case 30:
                        a = 196.0; b = 110.0; c = 259.0; d = 172.0;
                        break;

                    case 31:
                        a = 258.0; b = 108.0; c = 321.0; d = 172.0;
                        break;

                    case 32:
                        a = 320.0; b = 108.0; c = 383.0; d = 172.0;
                        break;

                    case 33:
                        a = 382.0; b = 108.0; c = 445.0; d = 172.0;
                        break;

                    case 34:
                        a = 444.0; b = 108.0; c = 507.0; d = 172.0;
                        break;

                    case 35:
                        a = 506.0; b = 108.0; c = 569.0; d = 172.0;
                        break;

                    default:
                        break;
                }

                if (item != null)
                {
                    CreateCountdown(panelMain, MainPanel, (int)item.fuel + 3, " %TIME_LEFT%", 12, $"{command2} {SecretKey} {i} 123", "1 1 1 1", "0.5 0", "0.5 0", $"{a} {b - 20}", $"{c} {d - 20}");
                    AddButton(panelMain, MainPanel, $"AnimalFarm.{i}", $"", 0, "1 1 1 1", "0.305 0.282 0.266 0", TextAnchor.MiddleCenter, $"{command2} {SecretKey} {farm.id} {i}", "0.5 0", "0.5 0", $"{a} {b}", $"{c} {d}");
                }
            }

            CreateLable(panelMain, SubPanel, "", _.lang.GetMessage("Welcome", _, player.UserIDString), 13, "1 1 1 0.5", TextAnchor.UpperLeft, "0.02 0.02", "0.98 0.95", "0 0", "0 0");

            if (_.configData.settings.addHopper)
                AddButton(panelMain, SubPanel, "AnimalFarm.hopper", _.lang.GetMessage("OpenHopper", _, player.UserIDString), 12, "1 1 1 0.9", "0 0 0 0.8", TextAnchor.MiddleCenter, $"{command2} {SecretKey} {farm.id} hopper", "0.75 0.06", "0.95 0.20", "0 0", "0 0");

            CuiHelper.AddUi(player, panelMain);
        }

        private static void OpenHopperPlayer(BasePlayer player, ulong id)
        {
            if (player == null)
                return;

            var panelMain = CreatePanel(MainPanel, "Inventory", "0.5 0", "0.5 0", "0 0", "0 0");

            AddPanel(panelMain, MainPanel, SubPanel, "0.305 0.282 0.266 1", "0.5 0", "0.5 0", "190 360.0", "575 480.0");
            AddPanel(panelMain, MainPanel, SubPanelTwo, "0.305 0.282 0.266 1", "0.5 0", "0.5 0", "190 112.0", "575 232.0");

            CreateLable(panelMain, SubPanel, "", _.lang.GetMessage("HopperWelcomeMessage", _, player.UserIDString), 13, "1 1 1 0.5", TextAnchor.UpperLeft, "0.02 0.02", "0.98 0.95", "0 0", "0 0");
            AddButton(panelMain, SubPanel, "AnimalFarm.hopper", _.lang.GetMessage("CloseHopper", _, player.UserIDString), 12, "1 1 1 0.9", "0 0 0 0.8", TextAnchor.MiddleCenter, $"{command2} {SecretKey} {id} close", "0.75 0.06", "0.95 0.20", "0 0", "0 0");

            CuiHelper.AddUi(player, panelMain);
        }
        #endregion

        #region UI Helpers
        private static CuiElementContainer CreatePanel(string panelName, string parent, string AnchorMin = "0.5 0", string AnchorMax = "0.5 0", string OffsetMin = "0 0", string OffsetMax = "0 0")
        {
            return new CuiElementContainer
            {
                new CuiElement
                {
                    Parent = parent, Name = panelName, DestroyUi = panelName,
                    Components = { new CuiRectTransformComponent { AnchorMin = AnchorMin, AnchorMax = AnchorMax, OffsetMin = OffsetMin, OffsetMax = OffsetMax } }
                }
            };
        }

        private static void AddPanel(CuiElementContainer container, string panelName, string panelButton, string color = "0.33 0.33 0.33 0.90", string AnchorMin = "0 0", string AnchorMax = "0 0", string OffsetMin = "-400 -200", string OffsetMax = "400 200")
        {
            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = color },
                RectTransform = { AnchorMin = AnchorMin, AnchorMax = AnchorMax, OffsetMin = OffsetMin, OffsetMax = OffsetMax, }
            }, panelName, panelButton);
        }

        private static void CreateCountdown(CuiElementContainer container, string parent, int time, string text = "%TIME_LEFT%", int size = 12, string command = "", string color = "0.60 255 0 0.68", string AnchorMin = "0.5 0", string AnchorMax = "0.5 0", string OffsetMin = "0 0", string OffsetMax = "0 0")
        {
            container.Add(new CuiElement
            {
                Parent = parent,
                Components =
                {
                    new CuiCountdownComponent { StartTime = time, TimerFormat = TimerFormat.HoursMinutesSeconds, EndTime = 0, Step = 1, Command = command},
                    new CuiTextComponent  { Text = $"{text}", FontSize = size, Align = TextAnchor.MiddleCenter, Font = "robotocondensed-regular.ttf", Color = color},
                    new CuiRectTransformComponent { AnchorMin = AnchorMin, AnchorMax = AnchorMax, OffsetMin = OffsetMin, OffsetMax = OffsetMax },
                }
            });
        }

        private static void CreateLable(CuiElementContainer container, string parent, string panelN, string message, int size, string color, TextAnchor anchor, string ancorMin, string ancorMax, string OffsetMin = "0 0", string OffsetMax = "0 0")
        {
            container.Add(new CuiLabel
            {
                Text = { Text = message, FontSize = size, Align = anchor, Color = color },
                RectTransform = { AnchorMin = ancorMin, AnchorMax = ancorMax, OffsetMin = OffsetMin, OffsetMax = OffsetMax }
            }, parent, panelN);
        }

        private static void createElement(CuiElementContainer container, string parent, string png, string ancorMin, string ancorMax, string color)
        {
            container.Add(new CuiElement
            {
                Parent = parent,
                Components = { new CuiImageComponent { Sprite = png, Color = color }, new CuiRectTransformComponent { AnchorMin = ancorMin, AnchorMax = ancorMax } }
            });
        }

        private static void AddButton(CuiElementContainer container, string panelName, string panelButton, string text, int testSize, string colorT, string colorB, TextAnchor anchor = TextAnchor.MiddleCenter, string usaageCommand = "", string AnchorMin = "0 0", string AnchorMax = "0 0", string OffsetMin = "236.5 30.0", string OffsetMax = "378 55.0")
        {
            container.Add(new CuiButton
            {
                Text = { Text = text, FontSize = testSize, Align = anchor, Color = colorT },
                Button = { Command = usaageCommand, Color = colorB },
                RectTransform = { AnchorMin = AnchorMin, AnchorMax = AnchorMax, OffsetMin = OffsetMin, OffsetMax = OffsetMax, }
            }, panelName, panelButton);
        }

        private static void AddImage(CuiElementContainer container, string parent, string panelName, int itemID, ulong skinID, string color, string AnchorMin = "0 0", string AnchorMax = "1 1", string OffsetMin = "0 0", string OffsetMax = "0 0")
        {
            container.Add(new CuiElement
            {
                Name = panelName,
                Parent = parent,
                Components = { new CuiRectTransformComponent { AnchorMin = AnchorMin, AnchorMax = AnchorMax, OffsetMin = OffsetMin, OffsetMax = OffsetMax },
                new CuiImageComponent { ItemId = itemID, SkinId = skinID, Color = color } }
            });
        }
        #endregion

        #region Patch For rust error
        [AutoPatch]
        [HarmonyPatch(typeof(BaseAIBrain), nameof(BaseAIBrain.SetGroupRoamRootPosition))]
        [HarmonyPriority(Priority.First)]
        internal class PatchBaseAIBrain
        {
            [HarmonyPrefix]
            internal static bool Prefix(BaseAIBrain __instance, Vector3 rootPos)
            {
                if ((object)__instance == null)
                    return true;

                if (__instance.Events == null || __instance.Events.Memory == null)
                    return false;

                return true;
            }
        }

        [AutoPatch] //food
        [HarmonyPatch(typeof(HitchTrough), nameof(HitchTrough.GetFoodItem))]
        [HarmonyPriority(Priority.First)]
        internal class PatchHitchTrough
        {
            [HarmonyPrefix]
            internal static bool Prefix(HitchTrough __instance, ref Item __result)
            {
                if ((object)__instance == null || __instance.inventory == null || __instance.skinID != _.configData.settings.skin)
                    return true;

                for (int index = 0; index < 12; ++index)
                {
                    Item foodItem = __instance.inventory.GetSlot(index);
                    if (foodItem != null && foodItem.info.category == ItemCategory.Food && !IsFarmBabyItem(foodItem) && (bool)(UnityEngine.Object)foodItem.info.GetComponent<ItemModConsumable>())
                    {
                        __result = foodItem;
                        return false;
                    }
                }

                return false;
            }
        }

        [AutoPatch] //CheckenCoop
        [HarmonyPatch(typeof(IndustrialStorageAdaptor), nameof(IndustrialStorageAdaptor.InputSlotRange))]
        [HarmonyPriority(Priority.First)]
        internal class PatchCoopInputSlotRange
        {
            [HarmonyPrefix]
            internal static bool Prefix(IndustrialStorageAdaptor __instance, ref Vector2i __result, int slotIndex)
            {
                if ((object)__instance == null || __instance.cachedParent == null)
                    return true;

                if (__instance.cachedParent is global::ChickenCoop)
                {
                    __result = new Vector2i(0, 2);
                    return false;
                }

                return true;
            }
        }

        [AutoPatch] //CheckenCoop
        [HarmonyPatch(typeof(IndustrialStorageAdaptor), nameof(IndustrialStorageAdaptor.OutputSlotRange))]
        [HarmonyPriority(Priority.First)]
        internal class PatchCoopOutputSlotRange
        {
            [HarmonyPrefix]
            internal static bool Prefix(IndustrialStorageAdaptor __instance, ref Vector2i __result, int slotIndex)
            {
                if ((object)__instance == null || __instance.cachedParent == null)
                    return true;

                if (__instance.cachedParent is global::ChickenCoop)
                {
                    __result = new Vector2i(3, 3);
                    return false;
                }

                if (__instance.cachedParent is HitchTrough)
                {
                    __result = new Vector2i(0, 23);
                    return false;
                }

                return true;
            }
        }

        [AutoPatch] //Hopper
        [HarmonyPatch(typeof(Hopper), nameof(Hopper.ScanForItemsTick))]
        [HarmonyPriority(Priority.First)]
        internal class PatchHopperScan
        {
            [HarmonyPrefix]
            internal static bool Prefix(Hopper __instance)
            {
                if ((object)__instance == null || __instance._cachedParent == null || !__instance._cachedParent.TryGetComponent<FarmBehavior>(out var component) || component == null)
                    return true;

                if (component.col == null)
                    return false;

                component.col.ScanForItemsTick();

                return false;
            }
        }

       /* [AutoPatch] //Gen2 Patch
        [HarmonyPatch(typeof(FSMComponent), nameof(FSMComponent.OnDestroy))]
        [HarmonyPriority(Priority.First)]
        internal class PatchFSMComponent
        {
            [HarmonyPrefix]
            internal static bool Prefix(FSMComponent __instance)
            {
                if ((object)__instance == null || __instance.baseEntity == null || __instance.baseEntity.skinID != CustomSkin)
                    return true;

                return false;
            }
        }*/

        [AutoPatch] //Hopper
        [HarmonyPatch(typeof(ChickenCoop), nameof(ChickenCoop.SubmitEggForHatching))]
        [HarmonyPriority(Priority.First)]
        internal class PatchChickenCoop
        {
            [HarmonyPrefix]
            internal static bool Prefix(ChickenCoop __instance, BaseEntity.RPCMessage msg)
            {
                if ((object)__instance == null || !__instance.TryGetComponent<CoopBehavior>(out var component) || component == null)
                    return true;

                if(__instance.HasFlag(BaseEntity.Flags.Reserved3) || __instance.HasFlag(BaseEntity.Flags.Reserved1))
                    return false;

                Item slot = __instance.inventory.GetSlot(0);
                if (slot == null || slot.info.shortname != "egg")
                    return false;

                slot.UseItem();
                __instance.Animals.Add(new ChickenCoop.AnimalStatus()
                {
                    TimeUntilHatch = (TimeUntil)(__instance.ChickenHatchTimeMinutes * 60f)
                });
                __instance.SetFlag(BaseEntity.Flags.Reserved1, true);
                __instance.SetFlag(BaseEntity.Flags.Reserved3, __instance.Animals.Count >= __instance.MaxChickens);
                if (!__instance.IsInvoking(new Action(__instance.CheckEggHatchState)))
                    __instance.InvokeRepeating(new Action(__instance.CheckEggHatchState), 10f, 10f);
                __instance.SendNetworkUpdate();

                return false;
            }
        }

        #endregion

        #region ChickenCoop
        public class CoopBehavior : FacepunchBehaviour
        {
            public static Dictionary<ulong, CoopBehavior> _allCoops = new Dictionary<ulong, CoopBehavior>();
            public global::ChickenCoop coop { get; private set; }
            public ulong thisID { get; private set; }
            public LiquidContainer water { get; private set; }
            public Splitter splitter { get; private set; }
            public IndustrialStorageAdaptor adapter { get; private set; }
            public bool AutoHatching { get; set; }
            public BasePlayer LootingPlayer { get; private set; }
            public bool AllowAutoHatch { get; set; }
            public ulong ownerID { get; set; }

            private void Awake()
            {
                coop = GetComponent<global::ChickenCoop>();
                thisID = coop.net.ID.Value;
                _allCoops.Add(thisID, this);
                ownerID = coop.OwnerID;
                AllowAutoHatch = _.configData.settingsCoop.autoHatch;

                if (!_.coopData.PlayerCoops.TryGetValue(thisID, out var info))
                {
                    info = new coopInfo();
                    if (!_.coopData.PlayerCoops.ContainsKey(thisID))
                    {
                        _.coopData.PlayerCoops.Add(thisID, info);
                        _.SaveDataCoop();
                    }
                }

                AutoHatching = info.IsAuto;
                Invoke("InitCoop", 0.1f);
            }

            public void InitCoop()
            {
                if (_ == null || coop == null || coop.IsDestroyed)
                    return;

                if (_.configData.settingsCoop.maxAnimals > 0)
                    coop.MaxChickens = _.configData.settingsCoop.maxAnimals;

                if (_.configData.settingsCoop.ChickenHatchTimeMinutes > 0)
                    coop.ChickenHatchTimeMinutes = _.configData.settingsCoop.ChickenHatchTimeMinutes;

                coop.SetFlag(BaseEntity.Flags.Reserved2, coop.Animals.Count >= coop.MaxChickens);

                for (int index = 0; index < coop.Animals.Count; ++index)
                {
                    global::ChickenCoop.AnimalStatus animal = coop.Animals[index];
                    if (!animal.SpawnedAnimal.IsSet && (double)(float)animal.TimeUntilHatch > 0.0)
                    {
                        coop.SetFlag(BaseEntity.Flags.Reserved1, true);

                        if (!coop.IsInvoking(new Action(coop.CheckEggHatchState)))
                            coop.InvokeRepeating(new Action(coop.CheckEggHatchState), 10f, 10f);
                    }
                }

                coop.CheckEggHatchState();

                foreach (var entity in coop.children)
                {
                    string prefabName = entity.ShortPrefabName;

                    switch (prefabName)
                    {
                        case "storageadaptor.deployed":
                            adapter = entity as IndustrialStorageAdaptor;
                            continue;

                        case "waterbarrel":
                            water = entity as LiquidContainer;
                            water.limitNetworking = true;
                            continue;

                        case "fluidsplitter":
                            splitter = entity as Splitter;
                            continue;

                        default:
                            continue;
                    }
                }

                if (adapter == null && _.configData.settingsCoop.addAdapter)
                    adapter = _.SpawnEntity(coop.OwnerID, false, storageAdapter, new Vector3(0.1f, 1.1f, -0.95f), Quaternion.Euler(new Vector3(90, 90, 0)), false, coop) as IndustrialStorageAdaptor;

                if (_.configData.settingsCoop.addWater)
                {
                    if (water == null)
                        water = _.SpawnEntity(coop.OwnerID, false, waterbarel, new Vector3(-0.8f, -2.0f, -0.95f), Quaternion.Euler(new Vector3(0, 0, 0)), false, coop) as LiquidContainer;

                    if (water != null)
                    {
                        water.limitNetworking = true;
                        IOEntity.IOSlot ioOutput = water.outputs[0];
                        ioOutput.type = IOEntity.IOType.Generic;
                        water.maxStackSize = 15;
                        water.inventory.maxStackSize = 15;
                    }

                    if (splitter == null)
                    {
                        splitter = _.SpawnEntity(coop.OwnerID, false, spliter, new Vector3(-0.04f, 0.45f, -0.94f), Quaternion.Euler(new Vector3(-90, 90, 180)), false, coop) as Splitter;
                    }

                    if (spliter != null)
                    {
                        IOEntity.IOSlot ioOutput = splitter.outputs[0];
                        ioOutput.type = IOEntity.IOType.Generic;
                        IOEntity.IOSlot ioOutput1 = splitter.outputs[1];
                        ioOutput1.type = IOEntity.IOType.Generic;
                        IOEntity.IOSlot ioOutput2 = splitter.outputs[2];
                        ioOutput2.type = IOEntity.IOType.Generic;
                    }

                    if (splitter != null && water != null)
                    {
                        FarmBehavior.ConnectWater(splitter, water);
                        InvokeRepeating(nameof(MoveWater), 15f, 30f);
                    }
                }
                else
                {
                    if (splitter != null)
                    {
                        splitter.SetParent(null);
                        splitter.Kill();
                    }

                    if (water != null)
                    {
                        water.SetParent(null);
                        water.Kill();
                    }
                }

                if (AllowAutoHatch && AutoHatching)
                    Invoke(nameof(HatchChicken), 15f);
            }

            public void SetLooter(BasePlayer player)
            {
                LootingPlayer = player;
                if (AllowAutoHatch)
                    BuildUICoop(LootingPlayer, this);
            }

            public void RemoveLooter(BasePlayer player)
            {
                CloseUi(LootingPlayer);
                if (AllowAutoHatch)
                    LootingPlayer = null;
            }

            public void SwitchAutoHatch()
            {
                AutoHatching = !AutoHatching;
                if (LootingPlayer != null)
                    BuildUICoop(LootingPlayer, this);
                if (AutoHatching)
                {
                    CancelInvoke(nameof(HatchChicken));
                    Invoke(nameof(HatchChicken), 1f);
                }
                else
                {
                    CancelInvoke(nameof(HatchChicken));
                }
            }

            private void HatchChicken()
            {
                if (AutoHatching)
                {
                    coop.SubmitEggForHatching(new BaseEntity.RPCMessage());
                    Invoke(nameof(HatchChicken), (coop.ChickenHatchTimeMinutes * 60f) + UnityEngine.Random.Range(1f, 15f));
                }
            }

            private void MoveWater()
            {
                if (water == null || coop == null || coop.inventory == null)
                    return;

                Item liquidItem = water.GetLiquidItem();
                if (liquidItem != null)
                    liquidItem.MoveToContainer(coop.inventory, 2);
            }

            private void OnDestroy()
            {
                this.CancelInvoke();

                if (!reloading)
                {
                    _allCoops.Remove(thisID);
                    if (_ != null)
                    {
                        if (_.coopData.PlayerCoops.ContainsKey(thisID))
                            _.coopData.PlayerCoops.Remove(thisID);
                    }
                }
                else
                {
                    if (_.coopData.PlayerCoops.TryGetValue(thisID, out var info))
                        info.IsAuto = AutoHatching;
                }

                CloseUi(LootingPlayer);
                if (playerLimits.ContainsKey(ownerID))
                    playerLimits[ownerID].coop--;
            }
        }
        #endregion

        #region Coop UI
        private static void BuildUICoop(BasePlayer player, CoopBehavior coop)
        {
            if (player == null || coop == null || coop.coop == null)
                return;

            var panelMain = CreatePanel(MainPanel, "Inventory", "0.5 0", "0.5 0", "0 0", "0 0");   //"0.5 0", "0.5 0", "190 235.0", "575 355.0"
            AddPanel(panelMain, MainPanel, SubPanel, "1 1 1 0", "0.5 0", "0.5 0", "432 255.5", "562 272.5");   //"0.5 0", "0.5 0", "190 110.0", "575 355.0"

            AddButton(panelMain, SubPanel, $"AnimalFarm.ButtonCoop", !coop.AutoHatching ? _.lang.GetMessage("autoActive", _, player.UserIDString) : _.lang.GetMessage("autoDeActive", _, player.UserIDString), 13, "1 1 1 0.6", !coop.AutoHatching ? "0.549 0.725 0.239 0.4" : "1 0 0 0.4", TextAnchor.LowerCenter, $"{command} {SecretKey} togglecoop {coop.coop.net.ID.Value}", "0 0", "1 1", "0 0", "0 0");

            CuiHelper.AddUi(player, panelMain);
        }
        #endregion

        #region UI Commands
        [ConsoleCommand(command)]
        private void UiActionCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();

            if (player == null || arg == null || arg.Args == null || arg.Args.Length < 2)
                return;

            if (!ulong.TryParse(arg.Args[0], out var key))
                return;

            if (key != SecretKey)
                return;

            if (!ulong.TryParse(arg.Args[2], out var netID))
                return;

            if (!CoopBehavior._allCoops.TryGetValue(netID, out var info))
                return;

            info.SwitchAutoHatch();
        }

        [ConsoleCommand(command2)]
        private void UiActionCommandFarm(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();

            if (player == null || arg == null || arg.Args == null || arg.Args.Length < 2)
                return;

            if (!ulong.TryParse(arg.Args[0], out var key))
                return;

            if (key != SecretKey)
                return;

            if (!ulong.TryParse(arg.Args[1], out var netID))
                return;

            string command = arg.Args[2];

            if (command == "hopper")
            {
                OpenHopperPlayer(player, netID);
            }
            else
            {
                HitchTrough controler = FindEntity(netID) as HitchTrough;
                if (controler != null)
                    BuildUI(player, controler.GetComponent<FarmBehavior>());
            }

        }
        #endregion

        #region Localization
        public static void GameTips(BasePlayer player, string message)
        {
            if (player != null)
                player.ShowToast(GameTip.Styles.Error, message, true);
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Welcome"] = "Welcome to your farm, Below you can place your baby animals and they will grow into farmable animals, But do not forget to feed them or they will die!",

                ["placementError"] = "Must be placed on foundation or land.",
                ["nope"] = "You lack the permission to use this command!",
                ["usagecommand4"] = "/animalfarm <type> <amount>\n Types - farm, coop, boar, stag, bear, wolf, tiger, panther, crocodile",
                ["usagecommanda"] = "animalfarm <playerID> <farm/coop/boar/stag/bear/wolf/tiger/panther/crocodile> <total>",
                ["gave"] = "You have just got a farm!",
                ["breed"] = "Congrats one of your animals are now pregnant!",
                ["breedneedmore"] = "You need more animals in order to breed them!",
                ["breedtomany"] = "You have no more breedable animals!",
                ["buyboar"] = "You just bought a boar!",
                ["buybstag"] = "You just bought a boar!",
                ["blocked"] = "You must be in a building privlidge area!",
                ["maxAmount"] = "You reached your max amount of animals roaming in your farm!",
                ["successBreedBoar"] = "You just breeded a boar!",
                ["successBreedStag"] = "You just breeded a stag!",
                ["successBreedBear"] = "You just breeded a bear!",
                ["successBreedWolf"] = "You just breeded a wolf!",
                ["successBreedTiger"] = "You just breeded a tiger!",
                ["successBreedPanther"] = "You just breeded a panther!",
                ["successBreedCrocodile"] = "You just breeded a crocodile!",
                ["WarweLevel"] = "WATER LEVEL: {0} / 20,000ml",
                ["WarweUsage"] = "WATER USAGE: {0}hr",
                ["UiAge"] = "AGE:{0}yrs",
                ["autoActive"] = "AUTO HATCH",
                ["autoDeActive"] = "STOP AUTO HATCH",
                ["MaxCoopsLimit"] = "Max modifyed coops reached {0}, This is now a normal chicken coop!",
                ["MaxCoopsLimitCustom"] = "Max modifyed coops reached {0}, Yoiu can not place any more Industrial Chicken Coops!",
                ["MaxFarmsLimit"] = "Max farm limit reached {0}, You can not place any more farms!",
                ["OpenHopper"] = "OPEN HOPPER",
                ["CloseHopper"] = "CLOSE",
                ["HopperWelcomeMessage"] = "Here you can see all your hopper items collected!",

            }, this);
        }
        #endregion
    }
}
  