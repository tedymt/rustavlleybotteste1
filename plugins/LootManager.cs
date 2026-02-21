using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Facepunch.Extend;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Oxide.Plugins.LootManagerExtensionMethods;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using WebSocketSharp;
using Random = UnityEngine.Random;

namespace Oxide.Plugins
{
    [Info("LootManager", "Adem", "1.1.0")]
    public class LootManager : RustPlugin
    {
        #region Variables
        [PluginReference] Plugin AlphaLoot, CustomLoot, Loottable;
        private static LootManager _ins;

        private readonly HashSet<string> _subscribeMethods = new HashSet<string>
        {
            "OnPluginUnloaded",
            "OnCrateSpawned",
            "OnCorpsePopulate",
            "OnEntityKill",
            "OnContainerPopulate",
            "CanPopulateLoot"
        };

        private ItemDefinition _bluePrintItemDefinition;
        
        private readonly Dictionary<string, LootTableData> _lootTables = new Dictionary<string, LootTableData>();
        
        private readonly Dictionary<string, string> _imageIDs = new Dictionary<string, string>();

        private readonly Dictionary<ulong, string> _crates = new Dictionary<ulong, string>();

        private readonly Dictionary<ulong, string> _npcs = new Dictionary<ulong, string>();

        private readonly Dictionary<ulong, string> _corpses = new Dictionary<ulong, string>();

        private readonly Dictionary<ulong, string> _bradleys = new Dictionary<ulong, string>();

        private readonly Dictionary<ulong, string> _helies = new Dictionary<ulong, string>();
        #endregion Variables

        #region Api
        private void RegisterPresetUsage(string presetName, string pluginName, string category)
        {
            if (string.IsNullOrEmpty(presetName) || string.IsNullOrEmpty(pluginName))
                return;

            if (_pluginUsages.TryGetValue(pluginName, out LootTableUsageInfo usageInfo))
            {
                usageInfo.TryAddNewUsage(presetName, category);
            }
            else
            {
                LootTableUsageInfo lootTableUsageInfo = new LootTableUsageInfo();
                lootTableUsageInfo.TryAddNewUsage(presetName, category);
                _pluginUsages.Add(pluginName, lootTableUsageInfo);
            }
        }

        private void UnregisterPresetUsage(string presetName, string pluginName, string category)
        {
            if (string.IsNullOrEmpty(presetName) || string.IsNullOrEmpty(pluginName))
                return;
            
            if (_pluginUsages.TryGetValue(pluginName, out LootTableUsageInfo usageInfo))
            {
                usageInfo.TryAddNewUsage(presetName, category);
            }
        }
        
        private HashSet<string> GetMissingLootTables(HashSet<string> lootTableNames)
        {
            HashSet<string> result = lootTableNames.Where(x => !string.IsNullOrEmpty(x) && !_lootTables.ContainsKey(x));
            if (result.Count == 0)
                return result;

            if (!LoadAllLootTables())
                return new HashSet<string>();
            
            result = lootTableNames.Where(x => !string.IsNullOrEmpty(x) && !_lootTables.ContainsKey(x));

            return result;
        }

        private string TryAddLootTable(string lootTablePreset, string pluginName, JObject lootTableJson, bool returnSame, HashSet<string> pluginFilters, string lootTableNameFilter)
        {
            LootTableData lootTableData = lootTableJson.ToObject<LootTableData>();
            if (lootTableData == null)
                return String.Empty;

            lootTableData.CheckAndUpdateValues();

            if (returnSame)
            {
                KeyValuePair<string, LootTableData> identicalLootTable = _lootTables.FirstOrDefault(x => (string.IsNullOrEmpty(lootTableNameFilter) || x.Key.Contains(lootTableNameFilter)) && (pluginFilters.IsNullOrEmpty() || pluginFilters.Any(y => x.Value.Description.Contains(y))) && x.Value.DeepEquals(lootTableData));
                if (!string.IsNullOrEmpty(identicalLootTable.Key))
                    return identicalLootTable.Key;
            }

            lootTablePreset = GetNameForNewLootTable(lootTablePreset);
            if (_lootTables.TryAdd(lootTablePreset, lootTableData))
                SaveLootTable(lootTablePreset, lootTableData);

            return lootTablePreset;
        }

        private void AddCrate(StorageContainer storageContainer, string lootTableName)
        {
            if (string.IsNullOrEmpty(lootTableName) || storageContainer == null || storageContainer.net == null || storageContainer.inventory == null)
                return;

            if (!_lootTables.TryGetValue(lootTableName, out LootTableData lootTableData))
                return;

            LootContainer lootContainer = storageContainer as LootContainer;
            if (lootContainer != null)
            {
                LootController.UpdateLootContainer(lootContainer, lootTableData);
                return;
            }

            LootController.FillContainer(storageContainer.inventory, lootTableData);
        }

        private void AddCrate(Fridge fridge, string lootTableName)
        {
            if (string.IsNullOrEmpty(lootTableName) || fridge == null || fridge.net == null)
                return;

            if (!_lootTables.TryGetValue(lootTableName, out LootTableData lootTableData))
                return;

            LootController.UpdateFridge(fridge, lootTableData);
        }

        private void AddCrate(DroppedItemContainer droppedItemContainer, string lootTableName)
        {
            if (string.IsNullOrEmpty(lootTableName) || droppedItemContainer == null || droppedItemContainer.net == null)
                return;

            if (!_lootTables.TryGetValue(lootTableName, out LootTableData lootTableData))
                return;

            LootController.FillContainer(droppedItemContainer.inventory, lootTableData);
        }

        private void AddNpc(ulong netId, string lootTableName)
        {
            if (string.IsNullOrEmpty(lootTableName) || !_lootTables.ContainsKey(lootTableName))
                return;

            _npcs.TryAdd(netId, lootTableName);
        }

        private void AddBradley(ulong netId, string lootTableName)
        {
            if (string.IsNullOrEmpty(lootTableName) || !_lootTables.ContainsKey(lootTableName))
                return;

            _bradleys.TryAdd(netId, lootTableName);
        }

        private void AddHeli(ulong netId, string lootTableName)
        {
            if (string.IsNullOrEmpty(lootTableName) || !_lootTables.ContainsKey(lootTableName))
                return;

            _helies.TryAdd(netId, lootTableName);
        }

        private bool EditLootTable(BasePlayer player, string lootTableName)
        {
            if (player == null || string.IsNullOrEmpty(lootTableName))
                return false;

            if (!_lootTables.TryGetValue(lootTableName, out LootTableData tableData))
                return false;
            
            _playerGuiInfos[player.userID] = new PlayerGuiInfo();
            DestroyGuiForPlayer(player);
            CreateBackground(player);
            EditPanelGui.Draw(player, lootTableName);
            return true;
        }
        #endregion Api

        #region Hooks
        private void Init()
        {
            Unsubscribes();
        }

        private void OnServerInitialized()
        {
            _ins = this;
            UpdateConfig();
            RegisterPermissions();
            
            if (!LoadAllLootTables())
                return;
            
            ImageLoader.LoadImages();
            PrefabController.CachePrefabs();
            _bluePrintItemDefinition = ItemManager.FindItemDefinition("blueprintbase");
            Subscribes();
            NextTick(() => Interface.CallHook("OnLootManagerInitialized"));
        }

        private void Unload()
        {
            DestroyGuiForAllPlayers();
        }

        private void OnPluginUnloaded(Plugin plugin)
        {
            if (plugin == null)
                return;

            _pluginUsages.Remove(plugin.Name);
        }

        private void OnCrateSpawned(BradleyAPC bradleyApc, LockedByEntCrate crate)
        {
            if (bradleyApc == null || bradleyApc.net == null || crate == null)
                return;

            if (!_bradleys.TryGetValue(bradleyApc.net.ID.Value, out string lootTableName))
                return;
            
            if (!_lootTables.TryGetValue(lootTableName, out LootTableData lootTableData))
                return;

            LootController.UpdateLootContainer(crate, lootTableData);
        }

        private void OnCrateSpawned(PatrolHelicopter patrolHelicopter, LockedByEntCrate crate)
        {
            if (patrolHelicopter == null || patrolHelicopter.net == null || crate == null)
                return;

            if (!_helies.TryGetValue(patrolHelicopter.net.ID.Value, out string lootTableName))
                return;
            
            if (!_lootTables.TryGetValue(lootTableName, out LootTableData lootTableData))
                return;

            LootController.UpdateLootContainer(crate, lootTableData);
        }

        private void OnCorpsePopulate(ScientistNPC scientistNpc, NPCPlayerCorpse corpse)
        {
            if (scientistNpc == null || corpse == null || scientistNpc.net == null || corpse.net == null)
                return;

            if (!_npcs.TryGetValue(scientistNpc.net.ID.Value, out string lootTableName))
                return;

            LootController.UpdateNpc(corpse, lootTableName);
            _corpses.TryAdd(corpse.net.ID.Value, lootTableName);
        }

        private void OnEntityKill(LootContainer lootContainer)
        {
            if (lootContainer == null || lootContainer.net == null)
                return;

            _crates.Remove(lootContainer.net.ID.Value);
        }

        private void OnEntityKill(Fridge fridge)
        {
            if (fridge == null || fridge.net == null)
                return;

            _crates.Remove(fridge.net.ID.Value);
        }

        private void OnEntityKill(DroppedItemContainer droppedItemContainer)
        {
            if (droppedItemContainer == null || droppedItemContainer.net == null)
                return;

            _crates.Remove(droppedItemContainer.net.ID.Value);
        }

        private void OnEntityKill(BradleyAPC bradley)
        {
            if (bradley == null || bradley.net == null)
                return;

            _bradleys.Remove(bradley.net.ID.Value);
        }

        private void OnEntityKill(PatrolHelicopter heli)
        {
            if (heli == null || heli.net == null)
                return;

            _helies.Remove(heli.net.ID.Value);
        }

        private void OnEntityKill(ScientistNPC npc)
        {
            if (npc == null || npc.net == null)
                return;

            _npcs.Remove(npc.net.ID.Value);
        }

        private void OnEntityKill(NPCPlayerCorpse corpse)
        {
            if (corpse == null || corpse.net == null)
                return;

            _corpses.Remove(corpse.net.ID.Value);
        }

        #region LootTablePlugin
        private object OnContainerPopulate(LootContainer lootContainer)
        {
            if (lootContainer == null || lootContainer.net == null)
                return null;

            return HandleOtherPluginPopulate(lootContainer.net.ID.Value, _crates, data => !data.IsLootTablePlugin);
        }

        private object OnCorpsePopulate(NPCPlayerCorpse corpse)
        {
            if (corpse == null || corpse.net == null)
                return null;

            return HandleOtherPluginPopulate(corpse.net.ID.Value, _corpses, data => !data.IsLootTablePlugin);
        }
        #endregion LootTablePlugin

        #region AlphaLoot
        private object CanPopulateLoot(LootContainer lootContainer)
        {
            if (lootContainer == null || lootContainer.net == null)
                return null;

            return HandleOtherPluginPopulate(lootContainer.net.ID.Value, _crates, data => !data.IsAlphaLoot || !string.IsNullOrEmpty(data.AlphaLootPreset));
        }

        private object CanPopulateLoot(ScientistNPC scientistNpc, NPCPlayerCorpse corpse)
        {
            if (scientistNpc == null || scientistNpc.net == null)
                return null;

            return HandleOtherPluginPopulate(scientistNpc.net.ID.Value, _npcs, data => !data.IsAlphaLoot || !string.IsNullOrEmpty(data.AlphaLootPreset));
        }
        #endregion AlphaLoot

        #region CustomLoot
        private object OnCustomLootContainer(NetworkableId net)
        {
            return HandleOtherPluginPopulate(net.Value, _crates, data => !data.IsCustomLoot);
        }

        private object OnCustomLootNPC(NetworkableId net)
        {
            return HandleOtherPluginPopulate(net.Value, _npcs, data => !data.IsCustomLoot);
        }
        #endregion CustomLoot

        private static object HandleOtherPluginPopulate(ulong netId, Dictionary<ulong, string> targetDictionary, Func<LootTableData, bool> cancelFunction)
        {
            if (!targetDictionary.TryGetValue(netId, out string presetName))
                return null;

            if (!_ins._lootTables.TryGetValue(presetName, out LootTableData lootTableData))
                return null;

            return cancelFunction(lootTableData) ? true : null;
        }
        #endregion Hooks

        #region Methods
        private void UpdateConfig()
        {
            if (_config.VersionConfig != Version)
            {
                PluginConfig defaultConfig = PluginConfig.DefaultConfig();
                if (_config.VersionConfig.Minor == 0)
                {
                    if (_config.VersionConfig.Patch <= 3)
                    {
                        _config.EnhancedRandomizer = defaultConfig.EnhancedRandomizer;
                    }
                }

                _config.VersionConfig = Version;
                SaveConfig();
            }
        }

        private void Unsubscribes()
        {
            foreach (string hook in _subscribeMethods)
                Unsubscribe(hook);
        }

        private void Subscribes()
        {
            foreach (string hook in _subscribeMethods)
                Subscribe(hook);
        }

        private static void Debug(params object[] arg)
        {
            string result = "";

            foreach (object obj in arg)
                result += obj.ToString() + " ";

            _ins.Puts(result);
        }

        private void RegisterPermissions()
        {
            permission.RegisterPermission(_config.Permission, this);
        }

        private static bool IsPlayerHavePermission(BasePlayer player)
        {
            return player.IsAdmin || _ins.permission.UserHasPermission(player.UserIDString, _ins._config.Permission);
        }

        private string GetNameForNewLootTable(string name)
        {
            if (!_lootTables.ContainsKey(name))
                return name;

            HashSet<string> matchingNames = _lootTables.Keys.Where(x => x.Contains(name));
            int number = 0;
            foreach (string matchName in matchingNames)
            {
                string fileNumberString = matchName.Split('_').Last();

                if (int.TryParse(fileNumberString, out int fileNumber) && fileNumber > number)
                    number = fileNumber;
            }

            number++;
            return $"{name}_{number}";
        }

        private static BaseEntity RaycastAll<T>(Ray ray, float distance = 10) where T : BaseEntity
        {
            RaycastHit[] hits = Physics.RaycastAll(ray);
            GamePhysics.Sort(hits);

            foreach (RaycastHit hit in hits)
            {
                BaseEntity ent = hit.GetEntity();
                if (ent is not T || !(hit.distance < distance))
                    continue;

                return ent;
            }

            return null;
        }
        #endregion Methods

        #region Commands
        [ChatCommand("lmfill")]
        private void FillContainerCommand(BasePlayer player, string command, string[] arg)
        {
            if (!IsPlayerHavePermission(player))
            {
                PrintToChat(player, $"[{Name}] You don’t have permission!");
                return;
            }

            if (arg.Length == 0)
            {
                PrintToChat(player, $"[{Name}] Preset not specified");
                return;
            }

            if (!_ins._lootTables.TryGetValue(arg[0], out LootTableData lootTableData))
            {
                PrintToChat(player, $"[{Name}] Loot table preset not found!");
                return;
            }

            BaseEntity crateEntity = RaycastAll<BaseEntity>(player.eyes.HeadRay());
            if (crateEntity == null)
            {
                PrintToChat(player, $"[{Name}] Crate not found");
                return;
            }

            ItemContainer itemContainer = null;
            if (crateEntity is Fridge fridge)
                itemContainer = fridge.inventory;
            else if (crateEntity is StorageContainer storageContainer)
                itemContainer = storageContainer.inventory;
            else if (crateEntity is DroppedItemContainer droppedItemContainer)
                itemContainer = droppedItemContainer.inventory;

            if (itemContainer == null)
            {
                PrintToChat(player, $"[{Name}] Crate not found");
                return;
            }

            LootController.UpdateLootContainer(crateEntity as LootContainer, lootTableData);
        }
        #endregion Commands

        #region Classes
        private readonly Dictionary<string, PrefabLootInfo> _prefabLootTables = new Dictionary<string, PrefabLootInfo>();

        private static class LootController
        {
            public static void UpdateLootContainer(LootContainer lootContainer, LootTableData lootTableData)
            {
                lootContainer.Invoke(() =>
                {
                    FillContainer(lootContainer.inventory, lootTableData);
                }, 2f);
            }

            public static void UpdateFridge(Fridge fridge, LootTableData lootTableData)
            {
                fridge.OnlyAcceptCategory = ItemCategory.All;
                FillContainer(fridge.inventory, lootTableData);
            }

            public static void UpdateNpc(NPCPlayerCorpse corpse, string lootTableName)
            {
                if (!_ins._lootTables.TryGetValue(lootTableName, out LootTableData lootTableData))
                    return;

                if (corpse.containers.IsNullOrEmpty())
                    return;

                corpse.Invoke(() =>
                {
                    ItemContainer container = corpse.containers[0];
                    
                    if (container != null)
                        FillContainer(container, lootTableData);
                }, 0.1f);
            }

            public static void FillContainer(ItemContainer container, LootTableData lootTableData)
            {
                if (lootTableData.ClearDefaultItems)
                    ClearContainer(container);

                if (lootTableData.UseItemList)
                {
                    if (lootTableData.UseMinMaxForItems)
                    {
                        int itemsAmount = Random.Range(lootTableData.MinItemsAmount, lootTableData.MaxItemsAmount + 1);
                        HashSet<ItemData> itemsForSpawn = GetElementsForSpawn(lootTableData.Items, itemsAmount);

                        if (itemsForSpawn.Count > 0)
                            ItemListController.FillContainer(container, itemsForSpawn);
                    }
                    else if (lootTableData.Items.Count > 0)
                    {
                        ItemListController.FillContainerWithoutMinMax(container, lootTableData.Items);
                    }
                }

                if (lootTableData.UsePrefabList)
                {
                    int prefabsAmount = Random.Range(lootTableData.MinPrefabsAmount, lootTableData.MaxPrefabsAmount + 1);
                    HashSet<PrefabData> prefabsForSpawn = GetElementsForSpawn(lootTableData.Prefabs, prefabsAmount);
                    PrefabController.FillContainer(container, prefabsForSpawn);
                }

                if (!string.IsNullOrEmpty(lootTableData.AlphaLootPreset))
                {
                    if (_ins.plugins.Exists("AlphaLoot") && (bool)_ins.AlphaLoot.Call("ProfileExists", lootTableData.AlphaLootPreset))
                    {
                        _ins.AlphaLoot.Call("PopulateLoot", container, lootTableData.AlphaLootPreset);
                    }
                }

                if (!string.IsNullOrEmpty(lootTableData.CustomLootPreset))
                {
                    if (!_ins.plugins.Exists("CustomLoot"))
                        return;

                    List<Item> items = (List<Item>)_ins.CustomLoot?.Call("MakeLoot", lootTableData.CustomLootPreset);
                    if (items != null)
                        foreach (Item item in items)
                            if (item == null || !item.MoveToContainer(container))
                                item.Remove();
                }

                if (!string.IsNullOrEmpty(lootTableData.LootTablePluginLootPreset))
                {
                    if (!_ins.plugins.Exists("Loottable"))
                        return;

                    List<Item> items = _ins.Loottable.Call<List<Item>>("MakeLoot", lootTableData.LootTablePluginLootPreset);
                    if (items != null)
                    {
                        foreach (Item item in items)
                            if (item == null || !item.MoveToContainer(container))
                                item.Remove();

                        Facepunch.Pool.FreeUnmanaged(ref items);
                    }
                }
            }

            private static void ClearContainer(ItemContainer container)
            {
                for (int i = container.itemList.Count - 1; i >= 0; i--)
                {
                    Item item = container.itemList[i];
                    item.RemoveFromContainer();
                    item.Remove();
                }
            }

            private static HashSet<T> GetElementsForSpawn<T>(List<T> elements, int targetAmount) where T : LootElementChance
            {
                HashSet<T> result = new HashSet<T>();
                if (elements.Count == 0 || targetAmount <= 0)
                    return result;

                HashSet<int> includedIndexes = new HashSet<int>();

                int counter = 200;
                while (result.Count < targetAmount && counter-- > 0)
                {
                    if (_ins._config.EnhancedRandomizer)
                    {
                        float sumChance = 0f;
                        for (int i = 0; i < elements.Count; i++)
                        {
                            if (includedIndexes.Contains(i))
                                continue;

                            sumChance += elements[i].Chance;
                        }

                        float random = Random.Range(0f, sumChance);
                        for (int i = 0; i < elements.Count; i++)
                        {
                            if (includedIndexes.Contains(i))
                                continue;

                            T lootElement = elements[i];
                            random -= lootElement.Chance;
                            if (random <= 0f)
                            {
                                includedIndexes.Add(i);
                                result.Add(lootElement);
                                break;
                            }
                        }
                    }
                    else
                    {
                        for (int i = 0; i < elements.Count; i++)
                        {
                            if (includedIndexes.Contains(i))
                                continue;

                            T lootElement = elements[i];
                            float chance = elements[i].Chance;
                            float roll = Random.Range(0f, 100f);
                            if (roll >= chance)
                                continue;

                            includedIndexes.Add(i);
                            result.Add(lootElement);

                            if (result.Count >= targetAmount)
                                break;
                        }
                    }
                }

                return result;
            }
        }

        private static class ItemListController
        {
            public static void FillContainer(ItemContainer container, HashSet<ItemData> items)
            {
                int itemsCount = container.itemList.Count + items.Count;
                if (container.capacity < itemsCount)
                    container.capacity = itemsCount;

                foreach (ItemData itemData in items)
                {
                    Item item = CreateItem(itemData);
                    if (item == null)
                        continue;

                    if (!item.MoveToContainer(container))
                        item.Remove();
                }
            }

            public static void FillContainerWithoutMinMax(ItemContainer container, List<ItemData> items)
            {
                foreach (ItemData itemData in items)
                {
                    float roll = Random.Range(0f, 100f);
                    if (roll >= itemData.Chance)
                        continue;

                    Item item = CreateItem(itemData);
                    if (item == null)
                        continue;

                    if (!item.MoveToContainer(container))
                        item.Remove();
                }
            }

            private static Item CreateItem(ItemData itemData)
            {
                int amount = Random.Range(itemData.MinAmount, itemData.MaxAmount + 1);
                if (amount == 0)
                    return null;

                Item item;

                if (itemData.IsBluePrint)
                {
                    item = ItemManager.CreateByName("blueprintbase");
                    item.blueprintTarget = itemData.ItemId;
                }
                else
                {
                    item = ItemManager.CreateByName(itemData.ShortName, amount, itemData.Skin);
                }

                if (item == null)
                {
                    _ins.PrintWarning($"Failed to create item! ({itemData.ShortName})");
                    return null;
                }

                if (!string.IsNullOrEmpty(itemData.CustomDisplayName))
                    item.name = itemData.CustomDisplayName;

                if (!string.IsNullOrEmpty(itemData.Genomes))
                {
                    string[] genomes = itemData.Genomes.Replace(" ", "").Split(",");
                    string genome = genomes.GetRandom();
                    UpdateGenome(item, genome);
                }

                return item;
            }

            private static void UpdateGenome(Item item, string genome)
            {
                if (genome.Length != 6)
                    return;

                genome = genome.ToLower();
                GrowableGenes growableGenes = new GrowableGenes();

                for (int i = 0; i < 6 && i < genome.Length; ++i)
                {

                    GrowableGenetics.GeneType geneType = GrowableGenetics.GeneType.Empty;

                    switch (genome[i])
                    {
                        case 'g':
                            geneType = GrowableGenetics.GeneType.GrowthSpeed;
                            break;
                        case 'y':
                            geneType = GrowableGenetics.GeneType.Yield;
                            break;
                        case 'h':
                            geneType = GrowableGenetics.GeneType.Hardiness;
                            break;
                        case 'w':
                            geneType = GrowableGenetics.GeneType.WaterRequirement;
                            break;
                    }

                    growableGenes.Genes[i].Set(geneType, true);
                }

                GrowableGeneEncoding.EncodeGenesToItem(GrowableGeneEncoding.EncodeGenesToInt(growableGenes), item);
            }
        }

        private static class PrefabController
        {
            public static void CachePrefabs()
            {
                foreach (LootTableData lootTableData in _ins._lootTables.Values)
                {
                    if (!lootTableData.UsePrefabList)
                        continue;

                    foreach (PrefabData prefabData in lootTableData.Prefabs)
                        CachePrefab(prefabData);
                }
            }

            public static void CachePrefab(PrefabData prefabData)
            {
                if (_ins._prefabLootTables.ContainsKey(prefabData.PrefabName))
                    return;

                GameObject gameObject = GameManager.server.FindPrefab(prefabData.PrefabName);
                if (gameObject == null)
                    return;

                LootContainer lootContainer = gameObject.GetComponent<LootContainer>();
                if (lootContainer != null)
                {
                    SavePrefabLootInfo(prefabData.PrefabName, lootContainer.LootSpawnSlots, lootContainer.scrapAmount, lootContainer.lootDefinition, lootContainer.maxDefinitionsToSpawn);
                    return;
                }

                global::HumanNPC humanNpc = gameObject.GetComponent<global::HumanNPC>();
                if (humanNpc != null)
                {
                    SavePrefabLootInfo(prefabData.PrefabName, humanNpc.LootSpawnSlots);
                    return;
                }

                global::ScarecrowNPC scarecrowNpc = gameObject.GetComponent<global::ScarecrowNPC>();
                if (scarecrowNpc != null)
                {
                    SavePrefabLootInfo(prefabData.PrefabName, scarecrowNpc.LootSpawnSlots);
                }
            }

            private static void SavePrefabLootInfo(string prefabName, LootContainer.LootSpawnSlot[] lootSpawnSlot, int scrapAmount = 0, LootSpawn lootDefinition = null, int maxDefinitionsToSpawn = 0)
            {
                PrefabLootInfo prefabLootInfo = new PrefabLootInfo
                {
                    LootSpawnSlots = lootSpawnSlot,
                    LootDefinition = lootDefinition,
                    MaxDefinitionsToSpawn = maxDefinitionsToSpawn,
                    ScrapAmount = scrapAmount
                };

                _ins._prefabLootTables.TryAdd(prefabName, prefabLootInfo);
            }

            public static void FillContainer(ItemContainer container, HashSet<PrefabData> prefabs)
            {
                foreach (PrefabData prefabData in prefabs)
                    FillContainer(container, prefabData);
            }

            private static void FillContainer(ItemContainer container, PrefabData prefabData)
            {
                if (!_ins._prefabLootTables.TryGetValue(prefabData.PrefabName, out PrefabLootInfo prefabLootInfo))
                    return;

                int lootScale = Random.Range(prefabData.MinAmount, prefabData.MaxAmount + 1);

                for (int i = 0; i < lootScale; i++)
                {
                    if (prefabLootInfo.LootSpawnSlots != null)
                    {
                        foreach (LootContainer.LootSpawnSlot lootSpawnSlot in prefabLootInfo.LootSpawnSlots)
                        {
                            if (lootSpawnSlot.eras == null || lootSpawnSlot.eras.Length == 0 || Array.IndexOf(lootSpawnSlot.eras, ConVar.Server.Era) != -1)
                            {
                                for (int j = 0; j < lootSpawnSlot.numberToSpawn; ++j)
                                {
                                    if (Random.Range(0f, 1f) <= lootSpawnSlot.probability)
                                    {
                                        lootSpawnSlot.definition.SpawnIntoContainer(container);
                                    }
                                }
                            }
                        }
                    }

                    if (prefabLootInfo.LootDefinition != null)
                        for (int j = 0; j < prefabLootInfo.MaxDefinitionsToSpawn; ++j)
                            prefabLootInfo.LootDefinition.SpawnIntoContainer(container);

                    if (prefabLootInfo.ScrapAmount > 0)
                    {
                        Item item = ItemManager.CreateByName("scrap", prefabLootInfo.ScrapAmount);
                        if (!item.MoveToContainer(container))
                            item.Remove();
                    }
                }
            }
        }

        private class PrefabLootInfo
        {
            public LootContainer.LootSpawnSlot[] LootSpawnSlots;
            public LootSpawn LootDefinition;
            public int MaxDefinitionsToSpawn;
            public int ScrapAmount;
        }

        private static class ImageLoader
        {
            public static void LoadImages()
            {
                ServerMgr.Instance.StartCoroutine(LoadIconsCoroutine());
            }

            private static IEnumerator LoadIconsCoroutine()
            {
                HashSet<ImageInfo> imageInfos = GetImagesInfosForLoad();
                char separator = Path.DirectorySeparatorChar;
                string path = $"file://{Interface.Oxide.DataDirectory}{separator}{_ins.Name}{separator}Images{separator}";

                foreach (ImageInfo imageInfo in imageInfos)
                {
                    string url = path + $"{imageInfo.FolderName}{separator}" + imageInfo.ImageName + ".png";
                    using UnityWebRequest unityWebRequest = UnityWebRequestTexture.GetTexture(url);

                    yield return unityWebRequest.SendWebRequest();

                    if (unityWebRequest.result != UnityWebRequest.Result.Success)
                    {
                        OnImageSaveFailed(imageInfo.ImageName);
                        break;
                    }

                    Texture2D texture = DownloadHandlerTexture.GetContent(unityWebRequest);
                    uint imageId = FileStorage.server.Store(texture.EncodeToPNG(), FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID);
                    _ins._imageIDs.Add(imageInfo.ImageName, imageId.ToString());
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }

            private static HashSet<ImageInfo> GetImagesInfosForLoad()
            {
                HashSet<ImageInfo> result = new HashSet<ImageInfo>
                {
                    new ImageInfo("Plus_Icon", "Icons"),
                    new ImageInfo("Lock_Icon", "Icons"),
                    new ImageInfo("Search_Icon", "Icons"),
                    new ImageInfo("Copy_Icon", "Icons"),
                    new ImageInfo("Delete_Icon", "Icons"),
                    new ImageInfo("Edit_Icon", "Icons"),
                    new ImageInfo("Error_Icon", "Icons")
                };

                foreach (PrefabInfo crateInfo in _ins._prefabInfos)
                {
                    if (result.Any(x => x.ImageName == crateInfo.ImageName))
                        continue;

                    result.Add(new ImageInfo(crateInfo.ImageName, "Prefabs"));
                }

                return result;
            }

            private static void OnImageSaveFailed(string imageName)
            {
                _ins.PrintError($"Image {imageName} was not found. Maybe you didn't upload it to the .../oxide/data/{_ins.Name}/Images/ folder");
                Interface.Oxide.UnloadPlugin(_ins.Name);
            }

            private class ImageInfo
            {
                public readonly string ImageName;
                public readonly string FolderName;

                public ImageInfo(string imageName, string imageFolder)
                {
                    ImageName = imageName;
                    FolderName = imageFolder;
                }
            }
        }
        #endregion Classes

        #region Data
        private readonly HashSet<PrefabInfo> _prefabInfos = new HashSet<PrefabInfo>
        {
            new ("assets/bundled/prefabs/autospawn/resource/loot/loot-barrel-1.prefab", "loot-barrel-1", "Yellow Loot Barrel"),
            new ("assets/bundled/prefabs/autospawn/resource/loot/loot-barrel-2.prefab", "loot-barrel-2", "Blue Loot Barrel"),
            new ("assets/bundled/prefabs/radtown/oil_barrel.prefab", "oil_barrel", "Oil Barrel"),
            new ("assets/bundled/prefabs/radtown/crate_basic.prefab", "crate_basic", "Basic Crate"),
            new ("assets/bundled/prefabs/radtown/crate_basic_jungle.prefab", "crate_basic_jungle", "Crate Basic Jungle"),
            new ("assets/bundled/prefabs/radtown/crate_elite.prefab", "crate_elite", "Elite Crate"),
            new ("assets/prefabs/npc/m2bradley/bradley_crate.prefab", "crate_elite", "Elite Crate - Bradley"),
            new ("assets/prefabs/npc/patrol helicopter/heli_crate.prefab", "crate_elite", "Elite Crate - Heli"),
            new ("assets/prefabs/misc/food cache/food_cache_001.prefab", "food_cache_001", "Food Cache"),
            new ("assets/bundled/prefabs/radtown/foodbox.prefab", "foodbox", "Foodbox"),
            new ("assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab", "codelockedhackablecrate", "Locked Crate"),
            new ("assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate_oilrig.prefab", "codelockedhackablecrate", "Locked Crate - Oilrig"),
            new ("assets/bundled/prefabs/radtown/crate_normal.prefab", "crate_normal", "Military Crate"),
            new ("assets/bundled/prefabs/radtown/crate_normal_2.prefab", "crate_normal_2", "Normal Crate"),
            new ("assets/bundled/prefabs/radtown/crate_mine.prefab", "crate_normal_2", "Normal Crate - Cave"),
            new ("assets/bundled/prefabs/radtown/crate_normal_2_food.prefab", "crate_normal_2_food", "Normal Crate - Food"),
            new ("assets/bundled/prefabs/radtown/crate_normal_2_medical.prefab", "crate_normal_2_medical", "Normal Crate - Medical"),
            new ("assets/prefabs/misc/supply drop/supply_drop.prefab", "supply_drop", "Supply Drop"),
            new ("assets/bundled/prefabs/radtown/crate_tools.prefab", "crate_tools", "Tools Crate"),
            new ("assets/bundled/prefabs/radtown/crate_underwater_advanced.prefab", "crate_underwater_advanced", "Underwater Advanced Crate"),
            new ("assets/bundled/prefabs/radtown/crate_underwater_basic.prefab", "crate_basic", "Underwater Basic Crate"),
            new ("assets/bundled/prefabs/radtown/underwater_labs/crate_ammunition.prefab", "crate_ammunition", "Underwater Lab - Ammunition Crate"),
            new ("assets/bundled/prefabs/radtown/underwater_labs/crate_food_1.prefab", "crate_food_1", "Underwater Lab - Food Crate"),
            new ("assets/bundled/prefabs/radtown/underwater_labs/crate_fuel.prefab", "crate_fuel", "Underwater Lab - Fuel Crate"),
            new ("assets/bundled/prefabs/radtown/underwater_labs/crate_medical.prefab", "crate_medical", "Underwater Lab - Medical Crate"),
            new ("assets/bundled/prefabs/radtown/underwater_labs/tech_parts_1.prefab", "tech_parts_1", "Underwater Lab - Tier 2 Components"),
            new ("assets/bundled/prefabs/radtown/underwater_labs/tech_parts_2.prefab", "tech_parts_2", "Underwater Lab - Tier 3 Components"),
            new ("assets/bundled/prefabs/radtown/underwater_labs/vehicle_parts.prefab", "vehicle_parts", "Vehicle Parts"),

            new ("assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_bradley.prefab", "scientistnpc", "Bradley Scientist"),

            new ("assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_bradley_heavy.prefab", "scientistnpc_heavy", "Bradley Heavy Scientist"),

            new (new HashSet<string>
            {
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_cargo.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_cargo_turret_any.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_cargo_turret_lr300.prefab",
            }, "scientistnpc", "Cargo Scientist"),

            new ("assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_excavator.prefab", "scientistnpc", "Excavator Scientist"),
            new (new HashSet<string>
            {
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_full_mp5.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_full_lr300.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_full_pistol.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_full_shotgun.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_full_any.prefab",
            } , "scientistnpc", "Military Tunnel Scientist"),

            new ("assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab", "scientistnpc_heavy", "Heavy Scientist"),

            new (new HashSet<string>
            {
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_junkpile_pistol.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_outbreak.prefab"
            }, "scientistnpc", "Road Scientist"),
            new ("assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_oilrig.prefab", "scientistnpc", "Oilrig Scientist"),

            new (new HashSet<string>
            {
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_patrol.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_patrol_arctic.prefab"
            }, "scientistnpc", "Patrol Scientist"),

            new ("assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_roam.prefab", "scientistnpc", "Roam Scientist"),

            new ("assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_roam_nvg_variant.prefab", "scientistnpc_tunnel", "Missile Silo Scientist"),

            new ("assets/rust.ai/agents/npcplayer/humannpc/tunneldweller/npc_tunneldweller.prefab", "npc_tunneldweller", "Tunnel/ Underwater Dweller"),

            new ("assets/prefabs/npc/gingerbread/gingerbread_dungeon.prefab", "gingerbread_dungeon", "Gingerbread"),

            new ("assets/prefabs/npc/scarecrow/scarecrow_dungeon.prefab", "scarecrow_dungeon", "Scarecrow"),
        };

        private class PrefabInfo
        {
            public readonly HashSet<string> PrefabNames;
            public readonly string ShortPrefabName;
            public readonly string Name;
            public readonly string ImageName;

            public PrefabInfo(string prefabName, string imageName, string name)
            {
                PrefabNames = new HashSet<string> { prefabName };
                ImageName = imageName;
                ShortPrefabName = PrefabNames.Count > 0 ? PrefabNames.FirstOrDefault(x => true).Split('/').Last().Split('.').First() : string.Empty;
                Name = name;
            }

            public PrefabInfo(HashSet<string> prefabNames, string imageName, string name)
            {
                PrefabNames = prefabNames;
                ImageName = imageName;
                ShortPrefabName = PrefabNames.Count > 0 ? PrefabNames.FirstOrDefault(x => true).Split('/').Last().Split('.').First() : string.Empty;
                Name = name;
            }
        }

        public class LootTableData
        {
            public string Description;
            public bool ClearDefaultItems;
            public bool IsAlphaLoot;
            public string AlphaLootPreset = string.Empty;
            public bool IsCustomLoot;
            public string CustomLootPreset = string.Empty;
            public bool IsLootTablePlugin;
            public string LootTablePluginLootPreset = string.Empty;

            public bool UseItemList;
            public bool UseMinMaxForItems = true;
            public int MinItemsAmount;
            public int MaxItemsAmount;
            public List<ItemData> Items = new List<ItemData>();

            public bool UsePrefabList;
            public int MinPrefabsAmount;
            public int MaxPrefabsAmount;
            public List<PrefabData> Prefabs = new List<PrefabData>();

            public LootTableData() { }

            public LootTableData(LootTableData other)
            {
                ClearDefaultItems = other.ClearDefaultItems;
                Description = other.Description;
                IsAlphaLoot = other.IsAlphaLoot;
                AlphaLootPreset = other.AlphaLootPreset;
                IsCustomLoot = other.IsCustomLoot;
                CustomLootPreset = other.CustomLootPreset;
                IsLootTablePlugin = other.IsLootTablePlugin;
                LootTablePluginLootPreset = other.LootTablePluginLootPreset;

                UseItemList = other.UseItemList;
                UseMinMaxForItems = other.UseMinMaxForItems;
                MinItemsAmount = other.MinItemsAmount;
                MaxItemsAmount = other.MaxItemsAmount;
                Items = other.Items.Select(i => i.Clone()).ToList();

                UsePrefabList = other.UsePrefabList;
                MinPrefabsAmount = other.MinPrefabsAmount;
                MaxPrefabsAmount = other.MaxPrefabsAmount;
                Prefabs = other.Prefabs.Select(i => i.Clone()).ToList();
            }

            public bool DeepEquals(LootTableData other)
            {
                if (ClearDefaultItems != other.ClearDefaultItems ||
                    IsAlphaLoot != other.IsAlphaLoot ||
                    AlphaLootPreset != other.AlphaLootPreset ||
                    IsCustomLoot != other.IsCustomLoot ||
                    CustomLootPreset != other.CustomLootPreset ||
                    IsLootTablePlugin != other.IsLootTablePlugin ||
                    LootTablePluginLootPreset != other.LootTablePluginLootPreset ||
                    UseItemList != other.UseItemList ||
                    UseMinMaxForItems != other.UseMinMaxForItems ||
                    MinItemsAmount != other.MinItemsAmount ||
                    MaxItemsAmount != other.MaxItemsAmount ||
                    UsePrefabList != other.UsePrefabList ||
                    MinPrefabsAmount != other.MinPrefabsAmount ||
                    MaxPrefabsAmount != other.MaxPrefabsAmount)
                    return false;

                if (Items.Count != other.Items.Count || Prefabs.Count != other.Prefabs.Count)
                    return false;

                for (int i = 0; i < Items.Count; i++)
                {
                    if (!Items[i].DeepEquals(other.Items[i]))
                        return false;
                }

                for (int i = 0; i < Prefabs.Count; i++)
                {
                    if (!Prefabs[i].DeepEquals(other.Prefabs[i]))
                        return false;
                }

                return true;
            }

            public void CheckAndUpdateValues()
            {
                Items = Items.OrderBy(x => x.Chance);
                Prefabs = Prefabs.OrderBy(x => x.Chance);

                foreach (ItemData itemData in Items)
                    itemData.CheckAndUpdateValues();

                foreach (PrefabData prefabData in Prefabs)
                    prefabData.CheckAndUpdateValues();

                int countOfGoodItems = Items.Where(x => x.Chance >= 0.1f).Count;
                MinItemsAmount = Mathf.Clamp(MinItemsAmount, 0, countOfGoodItems);
                MaxItemsAmount = Mathf.Clamp(MaxItemsAmount, 0, countOfGoodItems);

                int countOfGoodPrefabs = Prefabs.Where(x => x.Chance >= 0.1f).Count;
                MinPrefabsAmount = Mathf.Clamp(MinPrefabsAmount, 0, countOfGoodPrefabs);
                MaxPrefabsAmount = Mathf.Clamp(MaxPrefabsAmount, 0, countOfGoodPrefabs);

                if (UsePrefabList)
                    foreach (PrefabData prefabData in Prefabs)
                        PrefabController.CachePrefab(prefabData);

                if (ClearDefaultItems)
                {
                    if (IsAlphaLoot && string.IsNullOrEmpty(AlphaLootPreset) || IsCustomLoot && string.IsNullOrEmpty(CustomLootPreset) || IsLootTablePlugin && string.IsNullOrEmpty(LootTablePluginLootPreset))
                        ClearDefaultItems = false;
                }
            }
        }

        public class ItemData : LootElementChance
        {
            public string ShortName;
            public int ItemId;
            public string CustomDisplayName;
            public string DefaultDisplayName;
            public string OwnerDisplayName;
            public ulong Skin;
            public bool IsBluePrint;
            public string Genomes;

            private ItemData() { }

            public static ItemData CreateDefault(string shortName, int itemId, string defaultDisplayName)
            {
                return new ItemData
                {
                    ShortName = shortName,
                    ItemId = itemId,
                    CustomDisplayName = string.Empty,
                    DefaultDisplayName = defaultDisplayName,
                    OwnerDisplayName = string.Empty,
                    Skin = 0,
                    IsBluePrint = false,
                    MinAmount = 1,
                    MaxAmount = 1,
                    Chance = 10f,
                    Genomes = string.Empty
                };
            }

            public void CheckAndUpdateValues()
            {
                if (MinAmount > MaxAmount)
                    MinAmount = MaxAmount;

                if (MaxAmount < MinAmount)
                    MaxAmount = MinAmount;

                Chance = Mathf.Clamp(Chance, 0, 100);
                if (ItemId == 0 || string.IsNullOrEmpty(DefaultDisplayName))
                {
                    ItemDefinition itemDefinition = ItemManager.FindItemDefinition(ShortName);
                    if (itemDefinition != null)
                    {
                        DefaultDisplayName = itemDefinition.displayName.english.ToLower();
                        ItemId = itemDefinition.itemid;
                    }
                }

                UpdateGenomes();
            }

            private void UpdateGenomes()
            {
                if (string.IsNullOrEmpty(Genomes))
                {
                    Genomes = string.Empty;
                    return;
                }

                Genomes = Genomes.Replace(" ", "");
                Genomes = Genomes.Replace(",", ", ");
            }

            public bool DeepEquals(ItemData other)
            {
                if (other == null)
                    return false;

                return ShortName == other.ShortName &&
                       CustomDisplayName == other.CustomDisplayName &&
                       OwnerDisplayName == other.OwnerDisplayName &&
                       Skin == other.Skin &&
                       IsBluePrint == other.IsBluePrint &&
                       MinAmount == other.MinAmount &&
                       MaxAmount == other.MaxAmount &&
                       Math.Abs(Chance - other.Chance) < 0.0001f &&
                       Genomes == other.Genomes;
            }

            public ItemData Clone()
            {
                return new ItemData()
                {
                    ShortName = ShortName,
                    ItemId = ItemId,
                    CustomDisplayName = CustomDisplayName,
                    DefaultDisplayName = DefaultDisplayName,
                    OwnerDisplayName = OwnerDisplayName,
                    Skin = Skin,
                    IsBluePrint = IsBluePrint,
                    MinAmount = MinAmount,
                    MaxAmount = MaxAmount,
                    Chance = Chance,
                    Genomes = Genomes
                };
            }
        }

        public class PrefabData : LootElementChance
        {
            public string PrefabName;
            public string ShortPrefabName;

            private PrefabData() { }

            public PrefabData(string prefabName, string shortPrefabName)
            {
                PrefabName = prefabName;
                ShortPrefabName = shortPrefabName;
                MinAmount = 1;
                MaxAmount = 1;
                Chance = 10f;
            }

            public bool DeepEquals(PrefabData other)
            {
                if (other == null) return false;

                return PrefabName == other.PrefabName &&
                       MinAmount == other.MinAmount &&
                       MaxAmount == other.MaxAmount &&
                       Math.Abs(Chance - other.Chance) < 0.0001f;
            }

            public void CheckAndUpdateValues()
            {
                if (MinAmount > MaxAmount)
                    MinAmount = MaxAmount;

                if (MaxAmount < MinAmount)
                    MaxAmount = MinAmount;

                Chance = Mathf.Clamp(Chance, 0, 100);

                if (string.IsNullOrEmpty(ShortPrefabName))
                    ShortPrefabName = PrefabName.Split('/').Last().Replace(".prefab", string.Empty);
            }

            public PrefabData Clone()
            {
                return new PrefabData()
                {
                    PrefabName = PrefabName,
                    ShortPrefabName = ShortPrefabName,
                    MinAmount = MinAmount,
                    MaxAmount = MaxAmount,
                    Chance = Chance
                };
            }
        }

        public class LootElementChance
        {
            public int MinAmount;
            public int MaxAmount;
            public float Chance;
        }

        private static void DeleteLootTable(string presetName)
        {
            string path = $"{_ins.Name}/LootTables/{presetName}";
            Interface.Oxide.DataFileSystem.DeleteDataFile(path);
        }

        private bool LoadAllLootTables()
        {
            string path = $"{_ins.Name}/LootTables/";

            try
            {
                foreach (string name in Interface.Oxide.DataFileSystem.GetFiles(path))
                {
                    string fileName = name.Split('/').Last().Split('.')[0];
                    if (_lootTables.ContainsKey(fileName))
                        continue;

                    LootTableData lootTableData = LoadLootTable(fileName);
                    if (lootTableData == null)
                    {
                        PrintError($"File {fileName} is corrupted and cannot be loaded!");
                    }
                    else
                    {
                        lootTableData.CheckAndUpdateValues();
                        _lootTables.TryAdd(fileName, lootTableData);
                    }
                }
            }
            catch
            {
                PrintError(
                    "Required folders were not found in data/LootManager!\n" +
                    "The following folders must exist:\n" +
                    " - LootTables\n" +
                    " - Images\n" +
                    "Please move these folders from the file you downloaded from the plugin website into: /data/LootManager/"
                );
                
                NextTick(() => Interface.Oxide.UnloadPlugin(Name));
                return false;
            }

            return true;
        }

        private static LootTableData LoadLootTable(string fileName)
        {
            string path = $"{_ins.Name}/LootTables/{fileName}";
            LootTableData lootTableData = Interface.Oxide.DataFileSystem.ReadObject<LootTableData>(path);
            return lootTableData;
        }

        private void SaveLootTable(string presetName, LootTableData lootTableData)
        {
            string path = $"{_ins.Name}/LootTables/{presetName}";
            Interface.Oxide.DataFileSystem.WriteObject(path, lootTableData);
        }

        private readonly Dictionary<string, LootTableUsageInfo> _pluginUsages = new Dictionary<string, LootTableUsageInfo>();
        
        private class LootTableUsageInfo
        {
            public readonly Dictionary<string, HashSet<string>> Categories = new Dictionary<string, HashSet<string>>();
            public readonly HashSet<string> AllPresetUsages = new HashSet<string>();

            public void TryAddNewUsage(string presetName, string category)
            {
                AllPresetUsages.Add(presetName);
                
                if (string.IsNullOrWhiteSpace(category))
                    return;
                
                if (Categories.TryGetValue(category, out HashSet<string> presets))
                    presets.Add(presetName);
                else
                {
                    HashSet<string> newPresets = new HashSet<string>
                    {
                        presetName
                    };
                    
                    Categories.Add(category, newPresets);
                }
            }

            public void TryRemoveUsage(string presetName)
            {
                foreach (HashSet<string> presets in Categories.Values)
                    presets.Remove(presetName);
            }
        }
        #endregion Data

        #region GUI
        private const string BackgroundName = "LootManager_Background";
        private const string ErrorBackgroundName = "LootManager_ErrorBackground";

        private static string GetStringFromArgs(string[] args, int startIndex)
        {
            string result = "";

            for (int i = startIndex; i < args.Length; i++)
            {
                if (i > startIndex)
                    result += " ";

                string word = args[i];
                result += word;
            }

            return result;
        }

        [ChatCommand("lm")]
        private void OpenChatCommand(BasePlayer player, string command, string[] arg)
        {
            if (!IsPlayerHavePermission(player))
            {
                PrintToChat(player, $"[{Name}] You don’t have permission!");
                return;
            }

            CreateGui(player);
        }

        private void CreateGui(BasePlayer player)
        {
            _playerGuiInfos[player.userID] = new PlayerGuiInfo();
            DestroyGuiForPlayer(player);
            CreateBackground(player);
            
            TableListPanelGui.Draw(player);
        }

        private void CreateBackground(BasePlayer player)
        {
            CuiElementContainer container = new CuiElementContainer();
            CuiPanel cuiPanel = new CuiPanel
            {
                Image = { Color = "0 0 0 0.6", Material = "assets/content/ui/uibackgroundblur.mat" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true,
                KeyboardEnabled = true
            };

            container.Add(cuiPanel, "Overlay", BackgroundName);
            CuiHelper.AddUi(player, container);
        }

        private static void OnPageChanged(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, TableListPanelGui.LeftPanel.LayerName);
            CuiHelper.DestroyUi(player, TableListPanelGui.RightPanel.LayerName);
            CuiHelper.DestroyUi(player, "PresetEdit_LeftBackground");
            CuiHelper.DestroyUi(player, EditPanelGui.RightPanel.LayerName);
        }

        [ConsoleCommand("Close_LootManager")]
        private void CloseGuiCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;

            DestroyGuiForPlayer(player);
        }

        private static void DestroyGuiForAllPlayers()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
                if (player != null)
                    DestroyGuiForPlayer(player);
        }

        private static void DestroyGuiForPlayer(BasePlayer player)
        {
            _ins._playerGuiInfos.Remove(player.userID);
            CuiHelper.DestroyUi(player, BackgroundName);
            CuiHelper.DestroyUi(player, ErrorBackgroundName);
        }
        
        [ConsoleCommand("TablesListLeft_SetPluginFilter_LootManager")]
        private void TablesListLeftSetPluginFilterCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;
            
            if (!_ins._playerGuiInfos.TryGetValue(player.userID, out PlayerGuiInfo playerGuiInfo))
            {
                playerGuiInfo = new PlayerGuiInfo();
                _ins._playerGuiInfos[player.userID] = playerGuiInfo;
            }
            
            if (arg.Args != null && arg.Args.Length > 0)
                playerGuiInfo.FilterPlugin = arg.Args[0];
            else
                playerGuiInfo.FilterPlugin = string.Empty;
            
            playerGuiInfo.RightPage = 0;
            playerGuiInfo.RightSearch = string.Empty;
            if (!string.IsNullOrEmpty(playerGuiInfo.FilterCategory))
            {
                playerGuiInfo.FilterCategory = string.Empty;
                playerGuiInfo.LeftPage = 0;
            }

            TableListPanelGui.Draw(player);
        }
        
        [ConsoleCommand("TablesListLeft_SetCategoryFilter_LootManager")]
        private void TablesListLeftSetCategoryFilterCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;
            
            if (!_ins._playerGuiInfos.TryGetValue(player.userID, out PlayerGuiInfo playerGuiInfo))
            {
                playerGuiInfo = new PlayerGuiInfo();
                _ins._playerGuiInfos[player.userID] = playerGuiInfo;
            }

            if (string.IsNullOrEmpty(playerGuiInfo.FilterCategory))
            {
                playerGuiInfo.LeftPage = 0;
            }
            
            if (arg.Args == null || arg.Args.Length == 0)
                playerGuiInfo.FilterCategory = string.Empty;
            else if (arg.Args.Length == 0)
                playerGuiInfo.FilterCategory = arg.Args[0];
            else
                playerGuiInfo.FilterCategory = GetStringFromArgs(arg.Args, 0);
            
            playerGuiInfo.RightPage = 0;
            playerGuiInfo.RightSearch = string.Empty;

            TableListPanelGui.Draw(player);
        }
        
        [ConsoleCommand("TablesListLeft_SetPage_LootManager")]
        private void TablesListLeftSetPageCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;
            
            if (!_ins._playerGuiInfos.TryGetValue(player.userID, out PlayerGuiInfo playerGuiInfo))
            {
                playerGuiInfo = new PlayerGuiInfo();
                _ins._playerGuiInfos[player.userID] = playerGuiInfo;
            }
            
            if (arg.Args == null || arg.Args.Length < 1)
                return;
            
            int page = arg.Args[0].ToInt();
            playerGuiInfo.LeftPage = page;
            TableListPanelGui.LeftPanel.DrawPanel(player);
        }
        
        
        [ConsoleCommand("TablesListRight_SetPage_LootManager")]
        private void TablesListRightEditPagePresetCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;
        
            if (arg.Args == null || arg.Args.Length < 1)
                return;
            
            if (!_ins._playerGuiInfos.TryGetValue(player.userID, out PlayerGuiInfo playerGuiInfo))
            {
                playerGuiInfo = new PlayerGuiInfo();
                _ins._playerGuiInfos[player.userID] = playerGuiInfo;
            }
        
            int page = arg.Args[0].ToInt();
            playerGuiInfo.RightPage = page;
            TableListPanelGui.RightPanel.DrawPanel(player);
        }
        
        [ConsoleCommand("TablesListRight_RemovePreset_LootManager")]
        private void TablesListRightRemoveCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;
        
            if (arg.Args == null || arg.Args.Length < 1)
                return;
        
            string preset = arg.Args[0];
        
            _lootTables.Remove(preset);
            DeleteLootTable(preset);
            TableListPanelGui.Draw(player);
        }
        
        [ConsoleCommand("TablesListRight_SearchPreset_LootManager")]
        private void TablesListRightSearchCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;
            
            if (!_ins._playerGuiInfos.TryGetValue(player.userID, out PlayerGuiInfo playerGuiInfo))
            {
                playerGuiInfo = new PlayerGuiInfo();
                _ins._playerGuiInfos[player.userID] = playerGuiInfo;
            }
        
            if (arg.Args == null || arg.Args.Length == 0)
            {
                playerGuiInfo.RightSearch = string.Empty;
                TableListPanelGui.RightPanel.DrawPanel(player);
                return;
            }

            string filter = arg.Args[0];
            if (!string.IsNullOrEmpty(filter))
                playerGuiInfo.RightSearch = filter;
            
            TableListPanelGui.RightPanel.DrawPanel(player);
        }

        [ConsoleCommand("TablesListRight_CreateNewPresetButton_LootManager")]
        private void TablesListRightCreateNewPresetButtonCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;
            
            CuiHelper.DestroyUi(player, "UpPanel_CreateNewTable_HideButton");
        }

        [ConsoleCommand("TablesListRight_CreateNewPreset_LootManager")]
        private void TablesListRightCreateNewCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;
            
            if (!_ins._playerGuiInfos.TryGetValue(player.userID, out PlayerGuiInfo playerGuiInfo))
            {
                playerGuiInfo = new PlayerGuiInfo();
                _ins._playerGuiInfos[player.userID] = playerGuiInfo;
            }
        
            if (arg.Args == null || arg.Args.Length != 1)
            {
                TableListPanelGui.RightPanel.DrawPanel(player);
                
                if (arg.Args.Length > 1)
                {
                    string message = GetMessage("INVALID_PRESET_NAME_MULTIPLE_WORDS", IsRuLang(player.UserIDString));
                    ErrorDisplayer.ShowError(player, message);
                }
        
                return;
            }
        
            string presetName = arg.Args[0];
            if (_lootTables.ContainsKey(presetName))
            {
                TableListPanelGui.RightPanel.DrawPanel(player);
                string message = GetMessage("PRESET_ALREADY_EXISTS", IsRuLang(player.UserIDString));
                ErrorDisplayer.ShowError(player, message);
                return;
            }
        
            LootTableData lootTableData = new LootTableData();
            SaveLootTable(presetName, lootTableData);
            _lootTables.Add(presetName, lootTableData);
            playerGuiInfo.FilterPlugin = "unused";
            EditPanelGui.Draw(player, presetName);
        }
        
        [ConsoleCommand("TablesListRight_ClonePreset_LootManager")]
        private void TablesListRightCloneCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;
        
            if (arg.Args == null || arg.Args.Length < 1)
                return;
            
            if (!_ins._playerGuiInfos.TryGetValue(player.userID, out PlayerGuiInfo playerGuiInfo))
            {
                playerGuiInfo = new PlayerGuiInfo();
                _ins._playerGuiInfos[player.userID] = playerGuiInfo;
            }
        
            string preset = arg.Args[0];
            string newPresetName = GetNameForNewLootTable(preset);
            if (_lootTables.ContainsKey(newPresetName))
                return;
        
            LootTableData lootTableData = _lootTables[preset];
            lootTableData = new LootTableData(lootTableData);
            _lootTables.Add(newPresetName, lootTableData);
            SaveLootTable(newPresetName, lootTableData);
            playerGuiInfo.FilterPlugin = "unused";
            EditPanelGui.Draw(player, newPresetName);
        }

        [ConsoleCommand("TablesListRight_EditPreset_LootManager")]
        private void TablesListRightEditCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;

            if (arg.Args == null || arg.Args.Length < 1)
                return;

            string preset = arg.Args[0];
            if (!_lootTables.TryGetValue(preset, out LootTableData _))
            {
                CreateGui(player);
                return;
            }

            EditPanelGui.Draw(player, preset);
        }

        private static class TableListPanelGui
        {
            public static void Draw(BasePlayer player)
            {
                OnPageChanged(player);
                LeftPanel.DrawPanel(player);
                RightPanel.DrawPanel(player);
            }

            public static class LeftPanel
            {
                public const string LayerName = "TablesList_LeftBackground";
                private const int ElementsPerPage = 9;

                public static void DrawPanel(BasePlayer player)
                {
                    if (!_ins._playerGuiInfos.TryGetValue(player.userID, out PlayerGuiInfo playerGuiInfo))
                    {
                        playerGuiInfo = new PlayerGuiInfo();
                        _ins._playerGuiInfos[player.userID] = playerGuiInfo;
                    }
                    
                    bool ru = IsRuLang(player.UserIDString);
                    
                    CuiHelper.DestroyUi(player, LayerName);
                    CuiElementContainer container = new CuiElementContainer();
                    container.AddRect(LayerName, BackgroundName, "0.13 0.13 0.13 0.8", "0.5 0.5", "0.5 0.5", "-610 -300", "-400 300");

                    container.AddRect("PluginsLabel_LeftBackground", LayerName, "0.13 0.13 0.13 0.8", "0 1", "0 1", "0 10", "210 42");
                    container.AddText(string.Empty, "PluginsLabel_LeftBackground", "0.89 0.87 0.82 1", GetMessage("GUI_FILTERS", ru), TextAnchor.MiddleCenter, 16, "bold", "0 0", "1 1", string.Empty, string.Empty);

                    int yMax = -10;
                    List<ElementInfo> elements = Facepunch.Pool.Get<List<ElementInfo>>();
                    
                    if (!string.IsNullOrEmpty(playerGuiInfo.FilterPlugin) && _ins._pluginUsages.TryGetValue(playerGuiInfo.FilterPlugin, out LootTableUsageInfo usageInfo) && usageInfo.Categories.Count > 0)
                    {
                        ElementInfo allButtonInfo = new ElementInfo
                        {
                            DisplayName = GetMessage("GUI_BACK", ru),
                            Amount = -1,
                            Command = "TablesListLeft_SetPluginFilter_LootManager",
                            IsActive = string.IsNullOrEmpty(playerGuiInfo.FilterPlugin)
                        };
                        
                        elements.Add(allButtonInfo);
                        
                        foreach (KeyValuePair<string, HashSet<string>> categoryPair in usageInfo.Categories)
                        {
                            bool isActive = playerGuiInfo.FilterCategory == categoryPair.Key;
                            
                            ElementInfo categoryButtonInfo = new ElementInfo
                            {
                                DisplayName = categoryPair.Key,
                                Amount = categoryPair.Value.Count,
                                Command = isActive ? "TablesListLeft_SetCategoryFilter_LootManager" : $"TablesListLeft_SetCategoryFilter_LootManager {categoryPair.Key}",
                                IsActive = isActive
                            };
                            
                            elements.Add(categoryButtonInfo);
                        }
                    }
                    else
                    {
                        ElementInfo allButtonInfo = new ElementInfo
                        {
                            DisplayName = GetMessage("GUI_ALL", ru),
                            Amount = _ins._lootTables.Count,
                            Command = "TablesListLeft_SetPluginFilter_LootManager",
                            IsActive = string.IsNullOrEmpty(playerGuiInfo.FilterPlugin)
                        };
                        
                        elements.Add(allButtonInfo);
                        
                        foreach (KeyValuePair<string, LootTableUsageInfo> pluginPair in _ins._pluginUsages)
                        {
                            LootTableUsageInfo pluginUsageInfo = pluginPair.Value;
                            ElementInfo elementInfo = new ElementInfo
                            {
                                DisplayName = pluginPair.Key,
                                Amount = pluginUsageInfo.AllPresetUsages.Where(x => _ins._lootTables.ContainsKey(x)).Count,
                                Command = $"TablesListLeft_SetPluginFilter_LootManager {pluginPair.Key}",
                                IsActive = playerGuiInfo.FilterPlugin == pluginPair.Key
                            };
                            
                            elements.Add(elementInfo);
                        }

                        int unusedCount = _ins._lootTables.Keys.Where(x => !_ins._pluginUsages.Any(y => y.Value.AllPresetUsages.Contains(x))).Count;
                        
                        if (unusedCount > 0)
                        {
                            ElementInfo elementInfo = new ElementInfo
                            {
                                DisplayName = GetMessage("GUI_UNUSED", ru),
                                Amount = unusedCount,
                                Command = $"TablesListLeft_SetPluginFilter_LootManager unused",
                                IsActive = playerGuiInfo.FilterPlugin == "unused"
                            };
                            
                            elements.Add(elementInfo);
                        }
                    }

                    DrawElements(ref container, ref elements, ref yMax, playerGuiInfo);
                    CuiHelper.AddUi(player, container);
                    Facepunch.Pool.FreeUnmanaged(ref elements);
                }

                private static void DrawElements(ref CuiElementContainer container, ref List<ElementInfo> elements, ref int yMax, PlayerGuiInfo playerGuiInfo)
                {
                    int lastPage = GetTotalPages(elements.Count) - 1;
                    if (playerGuiInfo.LeftPage > 0)
                    {
                        DrawPageButton(ref container, true, $"TablesListLeft_SetPage_LootManager {playerGuiInfo.LeftPage - 1}");
                    }
                    if (playerGuiInfo.LeftPage < lastPage)
                    {
                        DrawPageButton(ref container, false, $"TablesListLeft_SetPage_LootManager {playerGuiInfo.LeftPage + 1}");
                    }
                    
                    for (int i = 0; i < ElementsPerPage; i++)
                    {
                        int elementIndex = playerGuiInfo.LeftPage == 0 ? i : ElementsPerPage - 1 + ElementsPerPage * (playerGuiInfo.LeftPage - 1) + i;
                        if (elementIndex > elements.Count - 1)
                            break;
                        
                        ElementInfo elementInfo = elements[elementIndex];
                        DrawButton(ref container, ref yMax, elementInfo.IsActive, elementInfo.DisplayName, elementInfo.Amount, elementInfo.Command);
                    }
                }
                
                private static int GetTotalPages(int elementCount)
                {
                    if (elementCount <= 0)
                        return 1;

                    int rest = elementCount - (ElementsPerPage - 1);
                    int tailPages = (rest + ElementsPerPage - 1) / ElementsPerPage;

                    return 1 + tailPages;
                }

                private static void DrawButton(ref CuiElementContainer container, ref int yMax, bool isActive, string name, int amount, string command)
                {
                    int yMin = yMax - 51;
                    string mainRectName = $"TablesList_{name}";
                    string backgroundColor = isActive ? "0.44 0.44 0.4 0.8" : "0.25 0.25 0.25 0.8";
                    string subTextColor = isActive ? "0.89 0.87 0.82 1" : "0.44 0.44 0.44 1";
                    
                    container.AddRect(mainRectName, LayerName, backgroundColor, "0 1", "0 1", $"10 {yMin}", $"200 {yMax}");
                    container.AddRect(string.Empty, mainRectName, "0.44 0.44 0.4 0.8", "0 0", "0 1", "0 0", "3 0");
                    container.AddText(string.Empty, mainRectName, "0.89 0.87 0.82 1", name, TextAnchor.LowerLeft, 16, "bold", "0 0", "1 0", "13 22", "0 51");
                    
                    if (amount > 0)
                        container.AddText(string.Empty, mainRectName, subTextColor, $"X{amount}", TextAnchor.UpperLeft, 12, "bold", "0 0", "1 0", "13 0", "0 24");
                    
                    container.AddButton(string.Empty, mainRectName, "0 0", "1 1", string.Empty, string.Empty, command);
                    
                    yMax = yMin - 10;
                }

                private static void DrawPageButton(ref CuiElementContainer container, bool isLeft, string command)
                {
                    string mainRectName = $"TablesList_Page_{isLeft}";
                    int xMin = isLeft ? 10 : 169;
                    int xMax = xMin + 31;
                    string buttonText = isLeft ? "<" : ">";
                    
                    container.AddRect(mainRectName, LayerName, "0.44 0.44 0.4 0.8", "0 0", "0 0", $"{xMin} 10", $"{xMax} 41");
                    container.AddText(string.Empty, mainRectName, "0.89 0.87 0.82 1", buttonText, TextAnchor.MiddleCenter, 16, "bold", "0 0", "1 1", "0 0", "0 0");
                    container.AddButton(string.Empty, mainRectName, "0 0", "1 1", string.Empty, string.Empty, command);
                }

                private class ElementInfo
                {
                    public string DisplayName;
                    public int Amount;
                    public string Command;
                    public bool IsActive;
                }
            }

            public static class RightPanel
            {
                public const string LayerName = "TablesList_RightBackground";
                private const int ElementsPerPage = 18;
                
                public static void DrawPanel(BasePlayer player)
                {
                    if (!_ins._playerGuiInfos.TryGetValue(player.userID, out PlayerGuiInfo playerGuiInfo))
                    {
                        playerGuiInfo = new PlayerGuiInfo();
                        _ins._playerGuiInfos[player.userID] = playerGuiInfo;
                    }
                    
                    bool ru = IsRuLang(player.UserIDString);
                    
                    CuiHelper.DestroyUi(player, LayerName);
                    CuiElementContainer container = new CuiElementContainer();
                    container.AddRect(LayerName, BackgroundName, "0.13 0.13 0.13 0.8", "0.5 0.5", "0.5 0.5", "-390 -300", "610 300");
                    
                    container.AddRect("LootTablesLabel_LeftBackground", LayerName, "0.13 0.13 0.13 0.8", "0 1", "0 1", "0 10", "210 42");
                    container.AddText(string.Empty, "LootTablesLabel_LeftBackground", "0.89 0.87 0.82 1", GetMessage("GUI_LOOT_TABLES", ru), TextAnchor.MiddleCenter, 16, "bold", "0 0", "1 1", string.Empty, string.Empty);
                    
                    container.AddRect("CloseButton_LootManager", LayerName, "0.57 0.18 0.12 1", "1 1", "1 1", "-100 10", "0 42");
                    container.AddText(string.Empty, "CloseButton_LootManager", "0.89 0.87 0.82 1", "✕", TextAnchor.MiddleCenter, 20, "bold", "0 0", "0 0", "2 1", "34 31");
                    container.AddText(string.Empty, "CloseButton_LootManager", "0.89 0.87 0.82 1", "CLOSE", TextAnchor.MiddleLeft, 19, "bold", "0 0", "0 0", "34 1", "100 31");
                    container.AddButton(string.Empty, "CloseButton_LootManager", "0 0", "1 1", string.Empty, string.Empty, "Close_LootManager");

                    container.AddRect("UpPanel_SearchIcon", LayerName, "0.25 0.25 0.25 1", "0 1", "0 1", "220 10", "252 42");
                    container.AddImage(string.Empty, "UpPanel_SearchIcon", _ins._imageIDs["Search_Icon"], string.Empty, "0.5 0.5", "0.5 0.5", "-8 -8", "8 8");
                    container.AddRect("UpPanel_SearchPanel", LayerName, "0.09 0.09 0.09 1", "0 1", "0 1", "252 10", "462 42");
                    container.AddInputField(string.Empty, "UpPanel_SearchPanel", "0.44 0.44 0.44 1", playerGuiInfo.RightSearch, TextAnchor.MiddleLeft, 16, "bold", "0.025 0", "1 1", "0 0", "0 0", "TablesListRight_SearchPreset_LootManager");

                    container.AddRect("UpPanel_CreateNewTable", LayerName, "0.57 0.18 0.12 1", "0 1", "0 1", "472 10", "682 42");
                    container.AddInputField(string.Empty, "UpPanel_CreateNewTable", "0.89 0.87 0.82 1", GetMessage("GUI_ENTER_NAME", ru), TextAnchor.MiddleCenter, 16, "bold", "0 0", "1 1", "0 0", "0 0", "TablesListRight_CreateNewPreset_LootManager");
                    container.AddRect("UpPanel_CreateNewTable_HideButton", LayerName, "0.57 0.18 0.12 1", "0 1", "0 1", "472 10", "682 42");
                    container.AddText(string.Empty, "UpPanel_CreateNewTable_HideButton", "0.89 0.87 0.82 1", GetMessage("GUI_CREATE_NEW_PRESET", ru), TextAnchor.MiddleCenter, 16, "bold", "0 0", "1 1", "0 0", "0 0");
                    container.AddButton(string.Empty, "UpPanel_CreateNewTable_HideButton", "0 0", "1 1", string.Empty, string.Empty, "TablesListRight_CreateNewPresetButton_LootManager");

                    List<KeyValuePair<string, LootTableData>> filtered = Facepunch.Pool.Get<List<KeyValuePair<string, LootTableData>>>();
                    string lowerFilter = !string.IsNullOrEmpty(playerGuiInfo.RightSearch) ? playerGuiInfo.RightSearch.ToLower() : string.Empty;

                    foreach (KeyValuePair<string, LootTableData> pair in _ins._lootTables)
                    {
                        if (string.IsNullOrEmpty(playerGuiInfo.FilterPlugin))
                        {
                            if (!string.IsNullOrEmpty(lowerFilter) && !pair.Key.ToLower().Contains(lowerFilter)) 
                                continue;
                        }
                        else
                        {
                            if (playerGuiInfo.FilterPlugin == "unused")
                            {
                                if (_ins._pluginUsages.Any(x => x.Value.AllPresetUsages.Contains(pair.Key)))
                                    continue;
                            }
                            else if (_ins._pluginUsages.TryGetValue(playerGuiInfo.FilterPlugin, out LootTableUsageInfo usageInfo))
                            {
                                if (!usageInfo.AllPresetUsages.Contains(pair.Key))
                                    continue;
                                
                                if (!string.IsNullOrEmpty(playerGuiInfo.FilterCategory))
                                {
                                    HashSet<string> categoryUsageInfo = usageInfo.Categories[playerGuiInfo.FilterCategory];
                                
                                    if (!categoryUsageInfo.Contains(pair.Key))
                                        continue;
                                }
                            }
                            
                            if (!string.IsNullOrEmpty(lowerFilter) && !pair.Key.ToLower().Contains(lowerFilter)) 
                                continue;
                        }
                        
                        filtered.Add(pair);
                    }

                    int lastPage = GetTotalPages(filtered.Count) - 1;
                    if (playerGuiInfo.RightPage > lastPage)
                        playerGuiInfo.RightPage = 0;

                    if (lastPage > 0)
                    {
                        int pageX0 = 10;
                        int leftPage = Math.Min(playerGuiInfo.RightPage - 3, lastPage - 7);
                        if (leftPage < 0)
                            leftPage = 0;

                        for (int i = 0; i < 8 && i <= lastPage; i++)
                        {
                            int targetPageIndex = leftPage + i;
                            string commandArgs = $"{targetPageIndex}";
                            DrawPageButton(ref container, targetPageIndex, ref pageX0, playerGuiInfo.RightPage == targetPageIndex, commandArgs);
                        }
                    }
                    
                    const int width = 485;
                    const int height = 51;
                    const int delta = 10;
                    
                    int column = 0;
                    int row = 0;
                    
                    for (int i = 0; i < filtered.Count; i++)
                    {
                        int elementIndex = ElementsPerPage * playerGuiInfo.RightPage + i;
                        if (elementIndex > filtered.Count - 1)
                            break;

                        KeyValuePair<string, LootTableData> pair = filtered.ElementAt(elementIndex);

                        int xMin = delta + column * (delta + width);
                        int yMax = -delta - row * (delta + height);
                        int yMin = yMax - height;
                        int xMax = xMin + width;

                        string mainRectName = $"TablesList_{pair.Key}";
                        container.AddRect(mainRectName, LayerName, "0.25 0.25 0.25 0.8", "0 1", "0 1", $"{xMin} {yMin}", $"{xMax} {yMax}");
                        container.AddRect(string.Empty, mainRectName, "0.44 0.44 0.4 0.8", "0 0", "0 1", "0 0", "3 0");
                        container.AddText(string.Empty, mainRectName, "0.89 0.87 0.82 1", $"{pair.Key} <color=#BE5444>#{playerGuiInfo.RightPage * ElementsPerPage + i + 1}</color>", TextAnchor.MiddleLeft, 16, "bold", "0 0.5", "0 0.5", "10 -26", "350 26");
                        // if (!string.IsNullOrEmpty(pair.Value.Description))
                        //     container.AddText(string.Empty, mainRectName, "0.44 0.44 0.44 1", $"{pair.Value.Description}", TextAnchor.UpperLeft, 12, "regular", "0 0.5", "0 0.5", "10 -40", "350 -10");

                        container.AddRect("TablesList_DeleteButton", mainRectName, "0.57 0.18 0.12 1", "1 0.5", "1 0.5", $"-42 -16", $"-10 16");
                        container.AddImage(string.Empty, "TablesList_DeleteButton", _ins._imageIDs["Delete_Icon"], string.Empty, "0.5 0.5", "0.5 0.5", "-6 -6", "6 6");
                        container.AddButton(string.Empty, "TablesList_DeleteButton", "0 0", "1 1", string.Empty, string.Empty, $"TablesListRight_RemovePreset_LootManager {pair.Key}");

                        container.AddRect("TablesList_CopyButton", mainRectName, "0.33 0.33 0.33 1", "1 0.5", "1 0.5", $"-84 -16", $"-52 16");
                        container.AddImage(string.Empty, "TablesList_CopyButton", _ins._imageIDs["Copy_Icon"], string.Empty, "0.5 0.5", "0.5 0.5", "-8 -8", "8 8");
                        container.AddButton(string.Empty, "TablesList_CopyButton", "0 0", "1 1", string.Empty, string.Empty, $"TablesListRight_ClonePreset_LootManager {pair.Key}");

                        container.AddRect("TablesList_EditButton", mainRectName, "0.21 0.31 0.51 1", "1 0.5", "1 0.5", $"-170 -16", $"-94 16");
                        container.AddText(string.Empty, "TablesList_EditButton", "0.30 0.47 0.80 1", "EDIT", TextAnchor.MiddleCenter, 16, "bold", "0 0", "1 1", "0 0", "0 0");
                        container.AddButton(string.Empty, "TablesList_EditButton", "0 0", "1 1", string.Empty, string.Empty, $"TablesListRight_EditPreset_LootManager {pair.Key}");

                        column++;
                        if (column > 1)
                        {
                            column = 0;
                            row++;
                        }

                        if (i >= ElementsPerPage - 1)
                            break;
                    }

                    Facepunch.Pool.FreeUnmanaged(ref filtered);
                    
                    CuiHelper.AddUi(player, container);
                }
                
                public static int GetTotalPages(int elementCount)
                {
                    if (elementCount <= 0)
                        return 1;

                    return (int)Math.Ceiling((double)elementCount / ElementsPerPage);
                }
                
                private static void DrawPageButton(ref CuiElementContainer container, int pageNumber, ref int xMin, bool isActive, string args)
                {
                    int xMax = xMin + 31;
                    string buttonColor = isActive ? "0.45 0.45 0.45 1" : "0.18 0.18 0.18 1";
                    string buttonName = $"TablesList_PageButton_{pageNumber}";
                    string pageText = (pageNumber + 1).ToString();

                    container.AddRect(buttonName, LayerName, buttonColor, "0 0", "0 0", $"{xMin} 10", $"{xMax} 41");
                    container.AddText(string.Empty, buttonName, "0.89 0.87 0.82 1", pageText, TextAnchor.MiddleCenter, 16, "bold", "0 0", "1 1", "0 0", "0 0");
                    if (!isActive)
                        container.AddButton(string.Empty, buttonName, "0 0", "1 1", "0 0", "0 0", $"TablesListRight_SetPage_LootManager {args}");

                    xMin = xMax + 10;
                }
            }
        }

        [ConsoleCommand("EditTableLeft_ChangeCategory_LootManager")]
        private void EditPanelGuiChangeCategory(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;

            if (arg.Args == null || arg.Args.Length < 2)
                return;

            string lootTablePreset = arg.Args[0];
            string categoryShortName = arg.Args[1];
            EditPanelGui.LeftPanel.Draw(player, lootTablePreset, categoryShortName);

            if (categoryShortName == "items")
                EditPanelGui.RightPanel.DrawElements(player, lootTablePreset, false, 0, string.Empty);
            else if (categoryShortName == "prefabs")
                EditPanelGui.RightPanel.DrawElements(player, lootTablePreset, true, 0, string.Empty);
            else if (categoryShortName == "main")
                EditPanelGui.RightPanel.DrawInstruction(player, 0);
            else
                EditPanelGui.RightPanel.DrawInstruction(player, 1);
        }

        [ConsoleCommand("EditTableLeft_Switch_LootManager")]
        private void EditPanelGuiSwitchCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;

            if (arg.Args == null || arg.Args.Length < 3)
                return;

            bool ru = IsRuLang(player.UserIDString);
            string lootTablePreset = arg.Args[0];
            string sectionShortName = arg.Args[1];
            string fieldName = arg.Args[2];

            if (!_ins._lootTables.TryGetValue(lootTablePreset, out LootTableData lootTableData))
                return;

            if (fieldName == "clearItems")
            {
                lootTableData.ClearDefaultItems = !lootTableData.ClearDefaultItems;
            }
            else if (fieldName == "isManual")
            {
                lootTableData.UseItemList = !lootTableData.UseItemList;
            }
            else if (fieldName == "isRandomNumber")
            {
                lootTableData.UseMinMaxForItems = !lootTableData.UseMinMaxForItems;
            }
            else if (fieldName == "isPrefab")
            {
                lootTableData.UsePrefabList = !lootTableData.UsePrefabList;
            }
            else if (fieldName == "isAlphaLoot")
            {
                if (lootTableData.ClearDefaultItems && !lootTableData.IsAlphaLoot && string.IsNullOrEmpty(lootTableData.AlphaLootPreset))
                {
                    string message = GetMessage("CLEAR_DEFAULT_CONFLICTS_WITH_EXTERNAL_LOOT", ru);
                    ErrorDisplayer.ShowError(player, message);
                    return;
                }

                lootTableData.IsAlphaLoot = !lootTableData.IsAlphaLoot;
            }
            else if (fieldName == "isCustomLoot")
            {
                if (lootTableData.ClearDefaultItems && !lootTableData.IsCustomLoot && string.IsNullOrEmpty(lootTableData.CustomLootPreset))
                {
                    string message = GetMessage("CLEAR_DEFAULT_CONFLICTS_WITH_EXTERNAL_LOOT", ru);
                    ErrorDisplayer.ShowError(player, message);
                    return;
                }

                lootTableData.IsCustomLoot = !lootTableData.IsCustomLoot;
            }
            else if (fieldName == "isLoottable")
            {
                if (lootTableData.ClearDefaultItems && !lootTableData.IsLootTablePlugin && string.IsNullOrEmpty(lootTableData.LootTablePluginLootPreset))
                {
                    string message = GetMessage("CLEAR_DEFAULT_CONFLICTS_WITH_EXTERNAL_LOOT", ru);
                    ErrorDisplayer.ShowError(player, message);
                    return;
                }

                lootTableData.IsLootTablePlugin = !lootTableData.IsLootTablePlugin;
            }
            
            SaveLootTable(lootTablePreset, lootTableData);
            EditPanelGui.LeftPanel.Draw(player, lootTablePreset, sectionShortName);
        }

        [ConsoleCommand("EditTableLeft_Input_LootManager")]
        private void EditPanelGuiInputCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;

            bool ru = IsRuLang(player.UserIDString);
            string lootTablePreset = arg.Args[0];
            string sectionShortName = arg.Args[1];
            string fieldName = arg.Args[2];
            const int startInputArgIndex = 3;

            if (arg.Args.Length < 4)
            {
                EditPanelGui.LeftPanel.Draw(player, lootTablePreset, "main");
                return;
            }

            if (!_ins._lootTables.TryGetValue(lootTablePreset, out LootTableData lootTableData))
                return;

            switch (fieldName)
            {
                case "name":
                {
                    string newName = arg.Args[startInputArgIndex];

                    if (_lootTables.ContainsKey(newName))
                    {
                        EditPanelGui.LeftPanel.Draw(player, lootTablePreset, "main");
                        string message = GetMessage("PRESET_ALREADY_EXISTS", ru);
                        ErrorDisplayer.ShowError(player, message);
                        return;
                    }

                    _lootTables.TryAdd(newName, lootTableData);
                    _lootTables.Remove(lootTablePreset);
                    DeleteLootTable(lootTablePreset);
                    lootTablePreset = newName;
                    break;
                }
                case "description":
                {
                    string description = GetStringFromArgs(arg.Args, startInputArgIndex);
                    if (!string.IsNullOrEmpty(description))
                        lootTableData.Description = description;
                    break;
                }
                case "minItems":
                {
                    if (int.TryParse(arg.Args[startInputArgIndex], out int minItemAmount) && minItemAmount >= 0)
                        lootTableData.MinItemsAmount = minItemAmount;
                    break;
                }
                case "maxItems":
                {
                    if (int.TryParse(arg.Args[startInputArgIndex], out int maxItemAmount) && maxItemAmount >= 0)
                        lootTableData.MaxItemsAmount = maxItemAmount;
                    break;
                }
                case "minPrefabs":
                {
                    if (int.TryParse(arg.Args[startInputArgIndex], out int minPrefabsAmount) && minPrefabsAmount >= 0)
                        lootTableData.MinPrefabsAmount = minPrefabsAmount;
                    break;
                }
                case "maxPrefabs":
                {
                    if (int.TryParse(arg.Args[startInputArgIndex], out int maxPrefabsAmount) && maxPrefabsAmount >= 0)
                        lootTableData.MaxPrefabsAmount = maxPrefabsAmount;
                    break;
                }
                case "alphaLootPreset":
                    lootTableData.AlphaLootPreset = arg.Args[startInputArgIndex];
                    break;
                case "customLootPreset":
                    lootTableData.CustomLootPreset = arg.Args[startInputArgIndex];
                    break;
                case "loottablePluginPreset":
                    lootTableData.LootTablePluginLootPreset = arg.Args[startInputArgIndex];
                    break;
            }

            SaveLootTable(lootTablePreset, lootTableData);
            EditPanelGui.LeftPanel.Draw(player, lootTablePreset, sectionShortName);
        }

        [ConsoleCommand("EditTableLeft_Back_LootManager")]
        private void EditPanelGuiCancelCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;

            TableListPanelGui.Draw(player);
        }

        [ConsoleCommand("EditTableRight_SetPage_LootManager")]
        private void EditTableRightSetPageCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;

            if (arg.Args == null || arg.Args.Length < 3)
                return;

            string lootTablePreset = arg.Args[0];
            bool isPrefabs = arg.Args[1].ToBool();
            int page = arg.Args[2].ToInt();
            string filter = arg.Args.Length > 3 ? arg.Args[3] : string.Empty;
            EditPanelGui.RightPanel.DrawElements(player, lootTablePreset, isPrefabs, page, filter);
        }

        [ConsoleCommand("EditTableRight_SetPageInstruction_LootManager")]
        private void EditTableRightSetPageInstructionCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;

            if (arg.Args == null || arg.Args.Length < 1)
                return;

            int page = arg.Args[0].ToInt();
            EditPanelGui.RightPanel.DrawInstruction(player, page);
        }

        [ConsoleCommand("EditTableRight_NewElementButton_LootManager")]
        private void EditTableRightNewElementButtonCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;

            if (arg.Args == null || arg.Args.Length < 2)
                return;

            string lootTablePreset = arg.Args[0];
            bool isPrefab = arg.Args[1].ToBool();
            ChooseElementPanelGui.Draw(player, lootTablePreset, isPrefab);
        }

        [ConsoleCommand("EditTableRight_FindElement_LootManager")]
        private void EditTableRightFindElementCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;

            if (arg.Args == null || arg.Args.Length < 3)
                return;

            string lootTablePreset = arg.Args[0];
            bool isPrefabSetting = arg.Args[1].ToBool();
            int page = arg.Args[2].ToInt();
            const int startInputArgIndex = 3;

            if (arg.Args.Length < startInputArgIndex + 1)
            {
                EditPanelGui.RightPanel.DrawElements(player, lootTablePreset, isPrefabSetting, page, string.Empty);
                return;
            }

            string filter = GetStringFromArgs(arg.Args, startInputArgIndex);
            EditPanelGui.RightPanel.DrawElements(player, lootTablePreset, isPrefabSetting, page, filter);
        }

        [ConsoleCommand("EditTableRight_DeleteElement_LootManager")]
        private void EditTableRightDeleteElementCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;

            if (arg.Args == null || arg.Args.Length < 4)
                return;

            string lootTablePreset = arg.Args[0];
            bool isPrefab = arg.Args[1].ToBool();
            int page = arg.Args[2].ToInt();
            int elementIndex = arg.Args[3].ToInt();
            const int startInputArgIndex = 4;
            string filter = GetStringFromArgs(arg.Args, startInputArgIndex);

            if (!_ins._lootTables.TryGetValue(lootTablePreset, out LootTableData lootTableData))
                return;

            if (isPrefab)
                lootTableData.Prefabs.RemoveAt(elementIndex);
            else
                lootTableData.Items.RemoveAt(elementIndex);

            SaveLootTable(lootTablePreset, lootTableData);
            EditPanelGui.RightPanel.DrawElements(player, lootTablePreset, isPrefab, page, filter);
        }

        [ConsoleCommand("EditTableRight_EditElement_LootManager")]
        private void EditTableRightEditElementCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;

            if (arg.Args == null || arg.Args.Length < 3)
                return;

            string lootTablePreset = arg.Args[0];
            bool isPrefabSetting = arg.Args[1].ToBool();
            int elementIndex = arg.Args[2].ToInt();
            ElementEditor.Draw(player, lootTablePreset, elementIndex, isPrefabSetting);
        }
        private static class EditPanelGui
        {
            public static void Draw(BasePlayer player, string lootTablePreset)
            {
                if (!_ins._lootTables.TryGetValue(lootTablePreset, out LootTableData lootTableData))
                    return;

                OnPageChanged(player);
                LeftPanel.Draw(player, lootTablePreset, "main");
                RightPanel.DrawInstruction(player, 0);
            }

            public static class LeftPanel
            {
                public static void Draw(BasePlayer player, string lootTablePreset, string activeSectionShortname)
                {
                    if(!_ins._lootTables.TryGetValue(lootTablePreset, out LootTableData lootTableData))
                        return;

                    bool ru = IsRuLang(player.UserIDString);
                    CuiHelper.DestroyUi(player, "PresetEdit_LeftBackground");

                    CuiElementContainer container = new CuiElementContainer();
                    container.AddRect("PresetEdit_LeftBackground", BackgroundName, "0.13 0.13 0.13 0.8", "0.5 0.5", "0.5 0.5", "-550 -300", "-180 300");
                    container.AddRect("PresetEdit_SectionName", "PresetEdit_LeftBackground", "0.25 0.25 0.25 1", "0 1", "0 1", "0 10", "370 42");
                    container.AddText(string.Empty, "PresetEdit_SectionName", "0.53 0.53 0.53 1", lootTablePreset, TextAnchor.MiddleCenter, 16, "bold", "0 0", "1 1", "0 0", "0 0");

                    container.AddRect("PresetEdit_LeftUpSection", "PresetEdit_LeftBackground", "0.13 0.13 0.13 0.9", "0 1", "1 1", "0 -136", "0 0");

                    int column = 0;
                    int row = 0;
                    const int sectionHeight = 32;
                    const int sectionLength = 170;

                    HashSet<SectionInfo> sections = new HashSet<SectionInfo>
                    {
                        new SectionInfo
                        {
                            DisplayName = GetMessage("GUI_GENERAL", ru),
                            ShortName = "main",
                            Fields =  new HashSet<SectionFieldInfo>
                            {
                                new SectionFieldInfo(GetMessage("GUI_PRESET_NAME", ru), string.Empty, 0, lootTablePreset, $"{activeSectionShortname} name"),
                                new SectionFieldInfo(GetMessage("GUI_DESCRIPTION", ru), string.Empty, 0, string.IsNullOrEmpty(lootTableData.Description) ? string.Empty : lootTableData.Description, $"{activeSectionShortname} description"),
                                new SectionFieldInfo(GetMessage("GUI_CLEAR_DEFAULT_ITEMS", ru), GetMessage("GUI_CLEAR_DEFAULT_ITEMS_SUBNAME", ru), 1, lootTableData.ClearDefaultItems, $"{activeSectionShortname} clearItems"),
                            }
                        },
                        new SectionInfo
                        {
                            DisplayName = GetMessage("GUI_ITEM_TABLE", ru),
                            ShortName = "items",
                            Fields =  new HashSet<SectionFieldInfo>
                            {
                                new SectionFieldInfo(GetMessage("GUI_ADD_ITEMS", ru), string.Empty, 1, lootTableData.UseItemList, $"{activeSectionShortname} isManual"),
                                new SectionFieldInfo(GetMessage("GUI_USE_SETTINGS_BELOW", ru), GetMessage("GUI_USE_SETTINGS_BELOW_SUBNAME", ru), 1, lootTableData.UseMinMaxForItems, $"{activeSectionShortname} isRandomNumber"),
                                new SectionFieldInfo(GetMessage("GUI_MIN_ITEMS", ru), string.Empty, 0, lootTableData.MinItemsAmount, $"{activeSectionShortname} minItems"),
                                new SectionFieldInfo(GetMessage("GUI_MAX_ITEMS", ru), string.Empty, 0, lootTableData.MaxItemsAmount, $"{activeSectionShortname} maxItems"),
                            }
                        },
                        new SectionInfo
                        {
                            DisplayName = GetMessage("GUI_PREFAB_TABLE", ru),
                            ShortName = "prefabs",
                            Fields =  new HashSet<SectionFieldInfo>
                            {
                                new SectionFieldInfo(GetMessage("GUI_ADD_PREFABS", ru), GetMessage("GUI_ADD_PREFABS_SUBNAME", ru), 1, lootTableData.UsePrefabList, $"{activeSectionShortname} isPrefab"),
                                new SectionFieldInfo(GetMessage("GUI_MIN_PREFABS", ru), string.Empty, 0, lootTableData.MinPrefabsAmount, $"{activeSectionShortname} minPrefabs"),
                                new SectionFieldInfo(GetMessage("GUI_MAX_PREFABS", ru), string.Empty, 0, lootTableData.MaxPrefabsAmount, $"{activeSectionShortname} maxPrefabs"),
                            }
                        },
                        new SectionInfo
                        {
                            DisplayName = "ALPHA LOOT",
                            ShortName = "alphaloot",
                            Fields =  new HashSet<SectionFieldInfo>
                            {
                                new SectionFieldInfo(GetMessage("GUI_ALLOW_PLUGIN_SPAWN_LOOT", ru), GetMessage("GUI_ALLOW_PLUGIN_SPAWN_LOOT_SUBNAME_ALPHALOOT", ru), 1, lootTableData.IsAlphaLoot, $"{activeSectionShortname} isAlphaLoot"),
                                new SectionFieldInfo(GetMessage("GUI_OVERRIDE_LOOT_TABLE", ru), string.Empty, 0, lootTableData.AlphaLootPreset, $"{activeSectionShortname} alphaLootPreset")
                            }
                        },
                        new SectionInfo
                        {
                            DisplayName = "CUSTOM LOOT",
                            ShortName = "customloot",
                            Fields =  new HashSet<SectionFieldInfo>
                            {
                                new SectionFieldInfo(GetMessage("GUI_ALLOW_PLUGIN_SPAWN_LOOT", ru), GetMessage("GUI_ALLOW_PLUGIN_SPAWN_LOOT_SUBNAME_CUSTOMLOOT", ru), 1, lootTableData.IsCustomLoot, $"{activeSectionShortname} isCustomLoot"),
                                new SectionFieldInfo(GetMessage("GUI_OVERRIDE_LOOT_TABLE", ru), string.Empty, 0, lootTableData.CustomLootPreset, $"{activeSectionShortname} customLootPreset")
                            }
                        },
                        new SectionInfo
                        {
                            DisplayName = "LOOT TABLE STACKSIZE",
                            ShortName = "loottable",
                            Fields =  new HashSet<SectionFieldInfo>
                            {
                                new SectionFieldInfo(GetMessage("GUI_ALLOW_PLUGIN_SPAWN_LOOT", ru), GetMessage("GUI_ALLOW_PLUGIN_SPAWN_LOOT_SUBNAME_LOOTTABLE", ru), 1, lootTableData.IsLootTablePlugin, $"{activeSectionShortname} isLoottable"),
                                new SectionFieldInfo(GetMessage("GUI_OVERRIDE_LOOT_TABLE", ru), string.Empty, 0, lootTableData.LootTablePluginLootPreset, $"{activeSectionShortname} loottablePluginPreset")
                            }
                        },
                    };

                    foreach (SectionInfo sectionInfo in sections)
                    {
                        int ySectionMax = -10 - row * (10 + sectionHeight);
                        int xSectionMin = 10 + column * (10 + sectionLength);
                        bool isActive = activeSectionShortname == sectionInfo.ShortName;
                        string panelName = $"PresetEdit_SectionButton_{sectionInfo.ShortName}";
                        string color = isActive ? "0.33 0.33 0.33 1" : "0.25 0.25 0.25 1";
                        container.AddRect(panelName, "PresetEdit_LeftBackground", color, "0 1", "0 1", $"{xSectionMin} {ySectionMax - sectionHeight}", $"{xSectionMin + sectionLength} {ySectionMax}");
                        container.AddText(string.Empty, panelName, "0.53 0.53 0.53 1", sectionInfo.DisplayName, TextAnchor.MiddleCenter, 16, "bold", "0 0", "1 1", "0 0", "0 0");
                        if (!isActive)
                            container.AddButton(string.Empty, panelName, "0 0", "1 1", "0 0", "0 0", $"EditTableLeft_ChangeCategory_LootManager {lootTablePreset} {sectionInfo.ShortName}");

                        column++;
                        if (column > 1)
                        {
                            column = 0;
                            row++;
                        }
                    }

                    SectionInfo activeSection = sections.FirstOrDefault(x => x.ShortName == activeSectionShortname);
                    if (activeSection == null)
                        return;

                    int yMax = -145;

                    foreach (SectionFieldInfo sectionField in activeSection.Fields)
                    {
                        if (sectionField.Type == 0)
                        {
                            string value = sectionField.Value.ToString();
                            DrawInputElement(ref container, lootTablePreset, sectionField.Name, yMax.ToString(), value, ref yMax, sectionField.Args);
                        }
                        else if (sectionField.Type == 1)
                        {
                            bool value = (bool)sectionField.Value;
                            DrawSwitchElement(ref container, lootTablePreset, sectionField.Name, sectionField.SubName, yMax.ToString(), value, ref yMax, sectionField.Args);
                        }
                    }

                    container.AddRect("PresetEdit_BackButton", "PresetEdit_LeftBackground", "0.42 0.67 0.27 1", "0 0", "1 0", "10 10", "-10 46");
                    container.AddText(string.Empty, "PresetEdit_BackButton", "0.68 0.96 0.4 1", GetMessage("GUI_BACK", ru), TextAnchor.MiddleCenter, 16, "bold", "0 0", "1 1", "0 0", "0 0");
                    container.AddButton(string.Empty, "PresetEdit_BackButton", "0 0", "1 1", string.Empty, string.Empty, $"EditTableLeft_Back_LootManager");

                    CuiHelper.AddUi(player, container);
                }

                private static void DrawInputElement(ref CuiElementContainer container, string lootTablePreset, string name, string shortName, string fieldValue, ref int yMax, string args)
                {
                    int xMin = 10;
                    int xSize = 350;
                    int ySize = 52;
                    int yMin = yMax - ySize;
                    string fieldName = $"LeftPanel_InputField_{shortName}";
                    string inputPanelName = $"LeftPanel_InputRect_{shortName}";
                    string textColor = "0.89 0.87 0.82 1";
                    string command = $"EditTableLeft_Input_LootManager {lootTablePreset} {args}";

                    container.AddRect(fieldName, "PresetEdit_LeftBackground", "0 0 0 0", "0 1", "0 1", $"{xMin} {yMin}", $"{xMin + xSize} {yMax}");
                    container.AddText(string.Empty, fieldName, textColor, name, TextAnchor.MiddleLeft, 18, "regular", "0 1", "1 1", $"0 -22", "0 0");
                    container.AddRect(inputPanelName, fieldName, "0.1 0.1 0.1 1", "0 0", "1 0", "0 0", "0 32");
                    container.AddInputField(string.Empty, inputPanelName, "0.52 0.52 0.52 1", fieldValue, TextAnchor.MiddleLeft, 16, "regular", "0 0", "1 1", "6 0", "0 0", command);

                    yMax = yMin - 10;
                }

                private static void DrawSwitchElement(ref CuiElementContainer container, string lootTablePreset, string name, string subName, string shortName, bool fieldValue, ref int yMax, string args)
                {
                    const int xMin = 10;
                    const int xSize = 350;
                    const int ySize = 36;
                    const string textColor = "0.89 0.87 0.82 1";

                    int yMin = yMax - ySize;
                    string fieldName = $"LeftPanel_Switch_{shortName}";
                    string command = $"EditTableLeft_Switch_LootManager {lootTablePreset} {args}";
                    container.AddRect(fieldName, "PresetEdit_LeftBackground", "0 0 0 0", "0 1", "0 1", $"{xMin} {yMin}", $"{xMin + xSize} {yMax}");
                    container.AddText(string.Empty, fieldName, textColor, name, TextAnchor.UpperLeft, 18, "regular", "0 0", "1 1", "0 0", "0 0");

                    if (!string.IsNullOrEmpty(subName))
                        container.AddText(string.Empty, fieldName, "0.52 0.52 0.52 1", subName, TextAnchor.LowerLeft, 14, "regular", "0 0", "1 1", "0 0", "0 0");

                    const int leftSquareXMin = -62;
                    const int rightSquareXMin = -32;
                    int blackSquareXMin = fieldValue ? leftSquareXMin : rightSquareXMin;
                    int activeSquareXMin = fieldValue ? rightSquareXMin : leftSquareXMin;

                    CreateSwitchSquare(ref container, fieldName, blackSquareXMin, "0.1 0.1 0.1 1", "0.27 0.27 0.27 1");
                    CreateSwitchSquare(ref container, fieldName, activeSquareXMin, fieldValue ? "0.42 0.67 0.27 1" : "0.69 0.25 0.25 1", fieldValue ? "0.25 0.40 0.16 1" : "0.41 0.15 0.15 1");
                    container.AddButton(string.Empty, fieldName, "1 0", "1 0", $"{leftSquareXMin} 0", "0 32", command);

                    yMax = yMin - 10;
                }

                private static void CreateSwitchSquare(ref CuiElementContainer container, string parentFieldName, int xMin, string mainColor, string sideColor)
                {
                    container.AddRect("LeftPanel_BlackSwitchPart", parentFieldName, mainColor, "1 0.5", "1 0.5", $"{xMin} -16", $"{xMin + 32} 16");
                    container.AddRect(string.Empty, "LeftPanel_BlackSwitchPart", sideColor, "0 0", "0 0", "2 0", "30 2");
                    container.AddRect(string.Empty, "LeftPanel_BlackSwitchPart", sideColor, "0 0", "0 0", "2 30", "30 32");
                    container.AddRect(string.Empty, "LeftPanel_BlackSwitchPart", sideColor, "0 0", "0 0", "0 0", "2 32");
                    container.AddRect(string.Empty, "LeftPanel_BlackSwitchPart", sideColor, "0 0", "0 0", "30 0", "32 32");
                }

                private class SectionInfo
                {
                    public string DisplayName;
                    public string ShortName;
                    public HashSet<SectionFieldInfo> Fields;
                }

                private class SectionFieldInfo
                {
                    public readonly string Name;
                    public readonly string SubName;
                    public readonly int Type;
                    public readonly object Value;
                    public readonly string Args;

                    public SectionFieldInfo(string name, string subName, int type, object value, string args)
                    {
                        Name = name;
                        SubName = subName;
                        Type = type;
                        Value = value;
                        Args = args;
                    }
                }
            }

            public static class RightPanel
            {
                private const int ElementsPerPage = 20;
                public const string LayerName = "PresetEdit_RightBackground";

                public static void DrawElements(BasePlayer player, string lootTablePreset, bool isPrefab, int page, string filter)
                {
                    if (!_ins._lootTables.TryGetValue(lootTablePreset, out LootTableData lootTableData))
                        return;

                    bool ru = IsRuLang(player.UserIDString);
                    CuiHelper.DestroyUi(player, LayerName);
                    CuiElementContainer container = new CuiElementContainer();

                    DrawBackground(ref container);
                    string headerText = isPrefab ? GetMessage("GUI_PREFABS_INCLUDED", ru) : GetMessage("GUI_ITEMS_INCLUDED", ru);
                    DrawHeader(ref container, headerText);
                    container.AddRect("PresetEdit_ItemsBackground", LayerName, "0.1 0.1 0.1 0.8", "0 0", "0 0", "20 20", "698 522");

                    string searchText = string.IsNullOrEmpty(filter) ? string.Empty : filter;
                    container.AddRect("PresetEdit_FindPanel", "PresetEdit_ItemsBackground", "0.18 0.18 0.18 1", "1 0", "1 0", "-372 10", "-10 42");
                    container.AddImage(string.Empty, "PresetEdit_FindPanel", _ins._imageIDs["Search_Icon"], string.Empty, "1 0.5", "1 0.5", "-20 -8", "-4 8");
                    container.AddInputField(string.Empty, "PresetEdit_FindPanel", "0.52 0.52 0.52 1", searchText, TextAnchor.MiddleLeft, 16, "regular", "0 0", "1 1", "8 0", "0 0", $"EditTableRight_FindElement_LootManager {lootTablePreset} {isPrefab} {page}");

                    if (isPrefab)
                        DrawPrefabList(ru, ref container, lootTablePreset, lootTableData, page, filter);
                    else
                        DrawItemList(ru, ref container, lootTablePreset, lootTableData, page, filter);

                    CuiHelper.AddUi(player, container);
                }

                public static void DrawInstruction(BasePlayer player, int page)
                {
                    bool ru = IsRuLang(player.UserIDString);

                    CuiHelper.DestroyUi(player, LayerName);
                    CuiElementContainer container = new CuiElementContainer();
                    DrawBackground(ref container);
                    DrawHeader(ref container, GetMessage("GUI_INSTRUCTION_HEADER", ru));

                    Dictionary<int, HashSet<InstructionSectionInfo>> instructionPages = new Dictionary<int, HashSet<InstructionSectionInfo>>
                    {
                        [0] = new HashSet<InstructionSectionInfo>
                        {
                            new InstructionSectionInfo
                            {
                                HeaderKey = "GUI_INSTRUCTION_ABOUT_PLUGIN_HEADER",
                                TextKey = "GUI_INSTRUCTION_ABOUT_PLUGIN_TEXT",
                                OffsetMin = "8 -219",
                                OffsetMax = "710 -40"
                            },
                            new InstructionSectionInfo
                            {
                                HeaderKey = "GUI_INSTRUCTION_GENERAL_HEADER",
                                TextKey = "GUI_INSTRUCTION_GENERAL_TEXT",
                                OffsetMin = "8 -320",
                                OffsetMax = "710 -227"
                            },
                            new InstructionSectionInfo
                            {
                                HeaderKey = "GUI_INSTRUCTION_ITEMS_HEADER",
                                TextKey = "GUI_INSTRUCTION_ITEMS_TEXT",
                                OffsetMin = "8 -550",
                                OffsetMax = "355 -328"
                            },
                            new InstructionSectionInfo
                            {
                                HeaderKey = "GUI_INSTRUCTION_PREFABS_HEADER",
                                TextKey = "GUI_INSTRUCTION_PREFABS_TEXT",
                                OffsetMin = "363 -550",
                                OffsetMax = "710 -328"
                            }
                        },
                        [1] = new HashSet<InstructionSectionInfo>
                        {
                            new InstructionSectionInfo
                            {
                                HeaderKey = "GUI_INSTRUCTION_INTEGRATION_HEADER",
                                TextKey = "GUI_INSTRUCTION_INTEGRATION_TEXT",
                                OffsetMin = "8 -203",
                                OffsetMax = "710 -40"
                            }
                        }
                    };

                    HashSet<InstructionSectionInfo> sections = instructionPages[page];
                    foreach (InstructionSectionInfo sectionInfo in sections)
                        DrawSection(ref container, ru, sectionInfo);


                    int pageX0 = 8;
                    foreach (int instructionPagesKey in instructionPages.Keys)
                    {
                        DrawPageButton(ref container, LayerName, instructionPagesKey, ref pageX0, page == instructionPagesKey, $"EditTableRight_SetPageInstruction_LootManager {instructionPagesKey}");
                    }

                    CuiHelper.AddUi(player, container);
                }

                private static void DrawSection(ref CuiElementContainer container, bool ru, InstructionSectionInfo sectionInfo)
                {
                    string sectionName = sectionInfo.HeaderKey;
                    container.AddRect(sectionName, LayerName, "0.24 0.24 0.24 1", "0 1", "0 1", sectionInfo.OffsetMin, sectionInfo.OffsetMax);
                    container.AddRect("Instruction_SectionHeader", sectionName, "0.19 0.19 0.19 1", "0 1", "1 1", "0 -37", "0 0");
                    container.AddText(string.Empty, "Instruction_SectionHeader", "0.84 0.84 0.84 1", GetMessage(sectionInfo.HeaderKey, ru), TextAnchor.MiddleLeft, 18, "bold", "0 0", "1 1", "8 0", "0 0");
                    container.AddText(string.Empty, sectionName, "0.84 0.84 0.84 0.74", GetMessage(sectionInfo.TextKey, ru), TextAnchor.UpperLeft, 16, "regular", "0 0", "1 1", "8 0", "-8 -43");
                }

                private static void DrawHeader(ref CuiElementContainer container, string text)
                {
                    const string sectionName = "PresetEdit_RightHeader";
                    container.AddRect(sectionName, LayerName, "0.25 0.25 0.25 1", "0 1", "1 1", "0 -32", "0 0");
                    container.AddText(string.Empty, sectionName, "0.84 0.84 0.84 1", text, TextAnchor.MiddleLeft, 16, "bold", "0 0", "1 1", "7 0", "0 0");
                }

                private class InstructionSectionInfo
                {
                    public string HeaderKey;
                    public string TextKey;
                    public string OffsetMin;
                    public string OffsetMax;
                }

                private static void DrawBackground(ref CuiElementContainer container)
                {
                    container.AddRect(LayerName, BackgroundName, "0.13 0.13 0.13 0.8", "0.5 0.5", "0.5 0.5", "-169 -300", "549 300");

                    container.AddRect("CloseButton_LootManager", LayerName, "0.57 0.18 0.12 1", "1 1", "1 1", "-100 10", "0 42");
                    container.AddText(string.Empty, "CloseButton_LootManager", "0.89 0.87 0.82 1", "✕", TextAnchor.MiddleCenter, 20, "bold", "0 0", "0 0", "2 1", "34 31");
                    container.AddText(string.Empty, "CloseButton_LootManager", "0.89 0.87 0.82 1", "CLOSE", TextAnchor.MiddleLeft, 19, "bold", "0 0", "0 0", "34 1", "100 31");
                    container.AddButton(string.Empty, "CloseButton_LootManager", "0 0", "1 1", string.Empty, string.Empty, "Close_LootManager");
                }

                private static void DrawItemList(bool ru, ref CuiElementContainer container, string lootTablePreset, LootTableData lootTableData, int page, string filter)
                {
                    container.AddText(string.Empty, "PresetEdit_ItemsBackground", "0.89 0.87 0.82 1", GetMessage("GUI_ELEMENTS", ru) + $"<color=#6BAC44>{lootTableData.Items.Count}</color>", TextAnchor.LowerLeft, 18, "bold", "0 1", "0 1", "0 0", "400 50");

                    HashSet<ItemData> elementsForDisplay;
                    if (string.IsNullOrEmpty(filter))
                        elementsForDisplay = lootTableData.Items.ToHashset();
                    else
                        elementsForDisplay = lootTableData.Items.Where(x => x.DefaultDisplayName.Contains(filter) || x.ShortName.Contains(filter));

                    List<ElementInfo> elements = Facepunch.Pool.Get<List<ElementInfo>>();
                    foreach (ItemData itemData in elementsForDisplay)
                    {
                        ItemDefinition itemDefinition = ItemManager.FindItemDefinition(itemData.ShortName);
                        if (itemDefinition == null)
                        {
                            return;
                        }
                        string countText = $"x{itemData.MinAmount}-{itemData.MaxAmount}";
                        int itemIndex = lootTableData.Items.IndexOf(itemData);
                        ElementInfo itemInfo = new ElementInfo(itemData.ShortName, countText, $"EditTableRight_EditElement_LootManager {lootTablePreset} {false} {itemIndex}", $"EditTableRight_DeleteElement_LootManager {lootTablePreset} {false} {page} {itemIndex} {filter}", itemDefinition.itemid, itemData.Chance, itemData.Skin, string.Empty, itemData.IsBluePrint);
                        elements.Add(itemInfo);
                    }

                    DrawElements(ref container, lootTablePreset, false, elements, page, filter);
                    Facepunch.Pool.FreeUnmanaged(ref elements);
                }

                private static void DrawPrefabList(bool ru, ref CuiElementContainer container, string lootTablePreset, LootTableData lootTableData, int page, string filter)
                {
                    container.AddText(string.Empty, "PresetEdit_ItemsBackground", "0.89 0.87 0.82 1", GetMessage("GUI_ELEMENTS", ru) + $"<color=#6BAC44>{lootTableData.Prefabs.Count}</color>", TextAnchor.LowerLeft, 18, "bold", "0 1", "0 1", "0 0", "400 50");

                    HashSet<PrefabData> elementsForDisplay;
                    if (string.IsNullOrEmpty(filter))
                        elementsForDisplay = lootTableData.Prefabs.ToHashset();
                    else
                        elementsForDisplay = lootTableData.Prefabs.Where(x => x.PrefabName.Contains(filter));

                    List<ElementInfo> elements = Facepunch.Pool.Get<List<ElementInfo>>();
                    foreach (PrefabData prefabData in elementsForDisplay)
                    {
                        string countText = $"x{prefabData.MinAmount}-{prefabData.MaxAmount}";
                        PrefabInfo crateInfo = _ins._prefabInfos.FirstOrDefault(x => x.PrefabNames.Contains(prefabData.PrefabName));
                        if (crateInfo == null)
                            continue;

                        int itemIndex = lootTableData.Prefabs.IndexOf(prefabData);
                        ElementInfo elementInfo = new ElementInfo(crateInfo.Name, countText, $"EditTableRight_EditElement_LootManager {lootTablePreset} {true} {itemIndex}", $"EditTableRight_DeleteElement_LootManager {lootTablePreset} {true} {page} {itemIndex} {filter}", 0, prefabData.Chance, 0, crateInfo.ImageName, false);
                        elements.Add(elementInfo);
                    }

                    DrawElements(ref container, lootTablePreset, true, elements, page, filter);
                    Facepunch.Pool.FreeUnmanaged(ref elements);
                }

                private static int GetTotalPages(int elementCount)
                {
                    if (elementCount <= 0)
                        return 1;

                    int rest = elementCount - (ElementsPerPage - 1);
                    int tailPages = (rest + ElementsPerPage - 1) / ElementsPerPage;

                    return 1 + tailPages;
                }

                private static void DrawElements(ref CuiElementContainer container, string lootTablePreset, bool isPrefab, List<ElementInfo> elements, int page, string filter)
                {
                    const int delta = 10;
                    const int width = 157;
                    const int height = 80;

                    int row = page == 0 ? 1 : 0;
                    int line = 0;
                    int lastPage = GetTotalPages(elements.Count) - 1;
                    page = Math.Min(page, lastPage);

                    if (lastPage > 0)
                    {
                        int pageX0 = 5;
                        int leftPage = Math.Min(page - 3, lastPage - 7);
                        if (leftPage < 0)
                            leftPage = 0;

                        for (int i = 0; i < 8 && i <= lastPage; i++)
                        {
                            int targetPageIndex = leftPage + i;
                            DrawPageButton(ref container, "PresetEdit_ItemsBackground", targetPageIndex, ref pageX0, page == targetPageIndex, $"EditTableRight_SetPage_LootManager {lootTablePreset} {isPrefab} {targetPageIndex} {filter}");
                        }
                    }

                    for (int i = 0; i < ElementsPerPage; i++)
                    {
                        int elementIndex = page == 0 ? i : (ElementsPerPage - 1) + ElementsPerPage * (page - 1) + i;
                        if (elementIndex > elements.Count - 1)
                            break;

                        ElementInfo elementInfo = elements[elementIndex];
                        int xMin = delta + (delta + width) * row;
                        int xMax = xMin + width;
                        int yMax = -delta - (delta + height) * line;
                        int yMin = yMax - height;

                        string panelName = $"Element_{i}";
                        string elementImageName = "ElementImage";
                        container.AddRect(panelName, "PresetEdit_ItemsBackground", "0.18 0.18 0.18 1", "0 1", "0 1", $"{xMin} {yMin}", $"{xMax} {yMax}");

                        if (elementInfo.ItemId == 0)
                            container.AddImage(elementImageName, panelName, _ins._imageIDs[elementInfo.ImageName], string.Empty, "0 0", "0 0", "5 5", "75 75");
                        else
                        {
                            if (elementInfo.AddBluePrintImage)
                                container.AddItemIcon(elementImageName, panelName, _ins._bluePrintItemDefinition.itemid, 0, "0 0", "0 0", "5 5", "75 75");

                            container.AddItemIcon(elementImageName, panelName, elementInfo.ItemId, elementInfo.ItemSkinId, "0 0", "0 0", "5 5", "75 75");
                        }

                        container.AddRect("DeleteElementButton", panelName, "0.57 0.18 0.12 1", "1 1", "1 1", "-37 -37", "-12 -12");
                        container.AddImage(string.Empty, "DeleteElementButton", _ins._imageIDs["Delete_Icon"], string.Empty, "0.5 0.5", "0.5 0.5", "-6 -6", "6 6");
                        container.AddButton(string.Empty, "DeleteElementButton", "0 0", "1 1", string.Empty, string.Empty, elementInfo.DeleteCommand);

                        container.AddRect("EditElementButton", panelName, "0.21 0.31 0.51 1", "1 1", "1 1", "-72 -37", "-47 -12");
                        container.AddImage(string.Empty, "EditElementButton", _ins._imageIDs["Edit_Icon"], string.Empty, "0.5 0.5", "0.5 0.5", "-6 -6", "6 6");
                        container.AddButton(string.Empty, "EditElementButton", "0 0", "1 1", string.Empty, string.Empty, elementInfo.EditCommand);

                        container.AddText(string.Empty, panelName, "0.89 0.87 0.82 1", elementInfo.ElementName, TextAnchor.LowerCenter, 10, "regular", "0 0", "0 0", "1 2", "74 20");
                        container.AddText(string.Empty, panelName, "0.89 0.87 0.82 1", elementInfo.ElementAmount, TextAnchor.LowerLeft, 12, "bold", "0 0", "0 0", "85 2", "150 20");
                        container.AddText(string.Empty, panelName, "0.89 0.87 0.82 1", elementInfo.Chance + "%", TextAnchor.UpperLeft, 12, "regular", "0 0", "0 0", "85 0", "150 33");

                        row++;
                        if (row == 4)
                        {
                            row = 0;
                            line++;

                            if (line == 5)
                                break;
                        }
                    }

                    if (page == 0)
                        DrawNewElementButton(ref container, delta, delta + width, -delta - height, -delta, $"EditTableRight_NewElementButton_LootManager {lootTablePreset} {isPrefab}");
                }

                private static void DrawPageButton(ref CuiElementContainer container, string parentLayer, int pageNumber, ref int xMin, bool isActive, string command)
                {
                    const int size = 32;
                    int xMax = xMin + size;
                    string buttonColor = isActive ? "0.45 0.45 0.45 1" : "0.18 0.18 0.18 1";
                    string buttonName = $"TablesList_PageButton_{pageNumber}";
                    string pageText = (pageNumber + 1).ToString();

                    container.AddRect(buttonName, parentLayer, buttonColor, "0 0", "0 0", $"{xMin} 10", $"{xMax} 42");
                    container.AddText(string.Empty, buttonName, "0.89 0.87 0.82 1", pageText, TextAnchor.MiddleCenter, 16, "bold", "0 0", "1 1", "0 0", "0 0");
                    if (!isActive)
                        container.AddButton(string.Empty, buttonName, "0 0", "1 1", "0 0", "0 0", command);

                    xMin = xMax + 5;
                }

                private static void DrawNewElementButton(ref CuiElementContainer container, int xMin, int xMax, int yMin, int yMax, string command)
                {
                    container.AddRect("NewItemButton", "PresetEdit_ItemsBackground", "0.42 0.67 0.28 1", "0 1", "0 1", $"{xMin} {yMin}", $"{xMax} {yMax}");
                    container.AddRect(string.Empty, "NewItemButton", "0 0 0 0.6", "0 0", "0 1", "0 0", "2 0");
                    container.AddRect(string.Empty, "NewItemButton", "0 0 0 0.6", "1 0", "1 1", "-2 0", "0 0");
                    container.AddRect(string.Empty, "NewItemButton", "0 0 0 0.6", "0.5 1", "0.5 1", "-77 -2", "77 0");
                    container.AddRect(string.Empty, "NewItemButton", "0 0 0 0.6", "0.5 0", "0.5 0", "-77 0", $"77 2");
                    container.AddImage(string.Empty, "NewItemButton", _ins._imageIDs["Plus_Icon"], string.Empty, "0.5 0.5", "0.5 0.5", "-14 -14", "14 14");
                    container.AddButton(string.Empty, "NewItemButton", "0 0", "1 1", string.Empty, string.Empty, command);
                }

                private class ElementInfo
                {
                    public readonly string ElementName;
                    public readonly string ElementAmount;
                    public readonly string EditCommand;
                    public readonly string DeleteCommand;
                    public readonly int ItemId;
                    public readonly float Chance;
                    public readonly ulong ItemSkinId;
                    public readonly string ImageName;
                    public readonly bool AddBluePrintImage;

                    public ElementInfo(string elementName, string elementAmount, string editCommand, string deleteCommand, int itemId, float chance, ulong itemSkinId, string imageName, bool addBluePrintImage)
                    {
                        ElementName = elementName;
                        ElementAmount = elementAmount;
                        EditCommand = editCommand;
                        DeleteCommand = deleteCommand;
                        ItemId = itemId;
                        Chance = chance;
                        ItemSkinId = itemSkinId;
                        ImageName = imageName;
                        AddBluePrintImage = addBluePrintImage;
                    }
                }
            }
        }


        [ConsoleCommand("ChooseElementPanel_Close_LootManager")]
        private void ChooseElementPanelCloseCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;

            if (arg.Args == null || arg.Args.Length < 2)
                return;

            string lootTablePreset = arg.Args[0];
            bool isPrefabSetting = arg.Args[1].ToBool();
            ChooseElementPanelGui.Close(player);
            EditPanelGui.RightPanel.DrawElements(player, lootTablePreset, isPrefabSetting, 0, string.Empty);
        }

        [ConsoleCommand("ChooseElementPanel_SearchElement_LootManager")]
        private void ChooseElementPanelSearchItemCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;

            if (arg.Args == null || arg.Args.Length < 3)
                return;
            
            if (!_ins._playerGuiInfos.TryGetValue(player.userID, out PlayerGuiInfo playerGuiInfo))
            {
                playerGuiInfo = new PlayerGuiInfo();
                _ins._playerGuiInfos[player.userID] = playerGuiInfo;
            }

            string lootTablePreset = arg.Args[0];
            bool isPrefabSetting = arg.Args[1].ToBool();
            const int startInputArgIndex = 2;

            string filter = GetStringFromArgs(arg.Args, startInputArgIndex);

            if (isPrefabSetting)
                ChooseElementPanelGui.DrawPrefabList(player, lootTablePreset, filter);
            else
                ChooseElementPanelGui.DrawItemList(player, lootTablePreset, filter, playerGuiInfo);
        }

        [ConsoleCommand("ChooseElementPanel_ChangeCategoryPage_LootManager")]
        private void ChooseElementPanelChangeCategoryPageCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;

            if (arg.Args == null || arg.Args.Length < 2)
                return;
            
            
            if (!_ins._playerGuiInfos.TryGetValue(player.userID, out PlayerGuiInfo playerGuiInfo))
            {
                playerGuiInfo = new PlayerGuiInfo();
                _ins._playerGuiInfos[player.userID] = playerGuiInfo;
            }

            string lootTablePreset = arg.Args[0];
            int.TryParse(arg.Args[1], out int page);
            ChooseElementPanelGui.DrawCategoryPanel(player, lootTablePreset, page, playerGuiInfo);
        }

        [ConsoleCommand("ChooseElementPanel_ChooseCategory_LootManager")]
        private void ChooseElementPanelChangeCategoryCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;

            if (arg.Args == null || arg.Args.Length < 3)
                return;
            
            if (!_ins._playerGuiInfos.TryGetValue(player.userID, out PlayerGuiInfo playerGuiInfo))
            {
                playerGuiInfo = new PlayerGuiInfo();
                _ins._playerGuiInfos[player.userID] = playerGuiInfo;
            }

            string lootTablePreset = arg.Args[0];
            string categoryName = arg.Args[1];
            int.TryParse(arg.Args[2], out int page);
            ItemCategory itemCategory = (ItemCategory)Enum.Parse(typeof(ItemCategory), categoryName, true);
            playerGuiInfo.ItemCategorySelected = itemCategory;

            ChooseElementPanelGui.DrawItemList(player, lootTablePreset, string.Empty, playerGuiInfo);
            ChooseElementPanelGui.DrawCategoryPanel(player, lootTablePreset, page, playerGuiInfo);
        }

        [ConsoleCommand("ChooseElementPanel_ChooseItem_LootManager")]
        private void ChooseElementPanelChooseItemCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;

            if (arg.Args == null || arg.Args.Length < 2)
                return;

            string lootTablePreset = arg.Args[0];
            string shortName = arg.Args[1];

            if (!_ins._lootTables.TryGetValue(lootTablePreset, out LootTableData lootTableData))
                return;

            ItemDefinition itemDefinition = ItemManager.FindItemDefinition(shortName);
            ItemData itemData = ItemData.CreateDefault(shortName, itemDefinition.itemid, itemDefinition.displayName.english.ToLower());
            lootTableData.Items.Insert(0, itemData);
            SaveLootTable(lootTablePreset, lootTableData);
            ChooseElementPanelGui.Close(player);
            EditPanelGui.RightPanel.DrawElements(player, lootTablePreset, false, 0, string.Empty);
        }

        [ConsoleCommand("ChooseElementPanel_ChoosePrefab_LootManager")]
        private void ChooseElementPanelChoosePrefabCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;

            if (arg.Args == null || arg.Args.Length < 2)
                return;

            string lootTablePreset = arg.Args[0];
            string shortPrefabName = arg.Args[1];

            if (!_ins._lootTables.TryGetValue(lootTablePreset, out LootTableData lootTableData))
                return;

            PrefabInfo prefabInfo = _prefabInfos.FirstOrDefault(x => x.ShortPrefabName == shortPrefabName);
            PrefabData prefabData = new PrefabData(prefabInfo.PrefabNames.FirstOrDefault(x => !x.IsNullOrEmpty()), prefabInfo.ShortPrefabName);
            lootTableData.Prefabs.Insert(0, prefabData);
            SaveLootTable(lootTablePreset, lootTableData);
            ChooseElementPanelGui.Close(player);
            EditPanelGui.RightPanel.DrawElements(player, lootTablePreset, true, 0, string.Empty);
        }
        private static class ChooseElementPanelGui
        {
            public static void Close(BasePlayer player)
            {
                CuiHelper.DestroyUi(player, "ItemCategory_Background");
                CuiHelper.DestroyUi(player, "ElementChooser_Background");
                CuiHelper.DestroyUi(player, "ItemChooser_Background");
                CuiHelper.DestroyUi(player, "ItemList_SearchPanel");
            }

            public static void Draw(BasePlayer player, string lootTableName, bool isPrefab)
            {
                if (!_ins._playerGuiInfos.TryGetValue(player.userID, out PlayerGuiInfo playerGuiInfo))
                {
                    playerGuiInfo = new PlayerGuiInfo();
                    _ins._playerGuiInfos[player.userID] = playerGuiInfo;
                }
                
                DrawBackground(player, lootTableName, isPrefab);

                if (isPrefab)
                    DrawPrefabList(player, lootTableName, string.Empty);
                else
                {
                    DrawItemList(player, lootTableName, string.Empty, playerGuiInfo);
                    DrawCategoryPanel(player, lootTableName, 0, playerGuiInfo);
                }
            }

            private static void DrawBackground(BasePlayer player, string lootTableName, bool isPrefab)
            {
                CuiElementContainer container = new CuiElementContainer();
                container.AddRect("ElementChooser_Background", BackgroundName, "0 0 0 0.9", "0 0", "1 1", "0 0", "0 0");
                container.AddButton(string.Empty, "ElementChooser_Background", "0 0", "1 1", string.Empty, string.Empty, $"ChooseElementPanel_Close_LootManager {lootTableName} {isPrefab}");
                container.AddRect(string.Empty, "ElementChooser_Background", "0.1 0.1 0.1 1", "0.5 0.5", "0.5 0.5", "-336 -251", $"336 251");
                CuiHelper.AddUi(player, container);
            }

            public static void DrawPrefabList(BasePlayer player, string lootTableName, string filter)
            {
                HashSet<ElementInfo> elements = new HashSet<ElementInfo>();
                HashSet<PrefabInfo> cratesForDisplay;
                if (string.IsNullOrEmpty(filter))
                    cratesForDisplay = _ins._prefabInfos;
                else
                    cratesForDisplay = _ins._prefabInfos.Where(x => x.ShortPrefabName.Contains(filter));

                foreach (PrefabInfo crateInfo in cratesForDisplay)
                {
                    ElementInfo elementInfo = new ElementInfo(crateInfo.Name, $"ChooseElementPanel_ChoosePrefab_LootManager {lootTableName} {crateInfo.ShortPrefabName}", 0, _ins._imageIDs[crateInfo.ImageName]);
                    elements.Add(elementInfo);
                }

                DrawElements(player, lootTableName, filter, true, elements);
            }

            public static void DrawItemList(BasePlayer player, string lootTableName, string filter, PlayerGuiInfo playerGuiInfo)
            {
                HashSet<ItemDefinition> itemsForDisplay;
                if (string.IsNullOrEmpty(filter))
                    itemsForDisplay = ItemManager.itemList.Where(x => (_ins._config.IsHiddenItems || !x.hidden) && x.category == playerGuiInfo.ItemCategorySelected);
                else
                    itemsForDisplay = ItemManager.itemList.Where(x => (_ins._config.IsHiddenItems || !x.hidden) && (x.shortname.Contains(filter) || x.displayName.english.Contains(filter)));

                HashSet<ElementInfo> elements = new HashSet<ElementInfo>();
                foreach (ItemDefinition itemDefinition in itemsForDisplay)
                {
                    ElementInfo elementInfo = new ElementInfo(itemDefinition.displayName.english, $"ChooseElementPanel_ChooseItem_LootManager {lootTableName} {itemDefinition.shortname}", itemDefinition.itemid, string.Empty);
                    elements.Add(elementInfo);
                }

                DrawElements(player, lootTableName, filter, false, elements);
            }

            public static void DrawCategoryPanel(BasePlayer player, string lootTableName, int startPage, PlayerGuiInfo playerGuiInfo)
            {
                List<ItemCategory> itemCategories = new List<ItemCategory>
                {
                    ItemCategory.Weapon,
                    ItemCategory.Construction,
                    ItemCategory.Items,
                    ItemCategory.Resources,
                    ItemCategory.Attire,
                    ItemCategory.Tool,
                    ItemCategory.Medical,
                    ItemCategory.Food,
                    ItemCategory.Ammunition,
                    ItemCategory.Traps,
                    ItemCategory.Misc,
                    ItemCategory.Component,
                    ItemCategory.Electrical,
                    ItemCategory.Fun
                };

                startPage = Mathf.Clamp(startPage, 0, 8);
                CuiHelper.DestroyUi(player, "ItemCategory_Background");

                CuiElementContainer container = new CuiElementContainer();
                container.AddRect("ItemCategory_Background", BackgroundName, "0.15 0.15 0.15 1", "0.5 0.5", "0.5 0.5", "-300 251", "300 283");
                DrawPageButton(ref container, lootTableName, true, $"ChooseElementPanel_ChangeCategoryPage_LootManager {lootTableName} {(startPage - 1).ToString()}");
                DrawPageButton(ref container, lootTableName, false, $"ChooseElementPanel_ChangeCategoryPage_LootManager {lootTableName} {(startPage + 1).ToString()}");

                int xMin = 0;
                int xMax = 100;

                for (int i = startPage; i < itemCategories.Count; i++)
                {
                    ItemCategory category = itemCategories[i];
                    bool isActiveButton = playerGuiInfo.ItemCategorySelected == category;
                    string categoryName = category.ToString();
                    string buttonName = $"CategoryButton_{categoryName}";

                    container.AddRect(buttonName, "ItemCategory_Background", isActiveButton ? "0.2 0.2 0.2 1" : "0 0 0 0", "0 0", "0 1", $"{xMin} 0", $"{xMax} 0");
                    container.AddText(string.Empty, buttonName, "0.53 0.53 0.53 1", categoryName, TextAnchor.MiddleCenter, 16, "bold", "0 0", "1 1", "0 0", "0 0");
                    container.AddButton(string.Empty, buttonName, "0 0", "1 1", string.Empty, string.Empty, $"ChooseElementPanel_ChooseCategory_LootManager {lootTableName} {categoryName} {startPage}");

                    xMin = xMax;
                    xMax += 100;

                    if (xMax > 600)
                        break;
                }

                CuiHelper.AddUi(player, container);
            }

            private static void DrawPageButton(ref CuiElementContainer container, string lootTableName, bool isLeft, string command)
            {
                int xMin = isLeft ? -336 : 304;
                int xMax = xMin + 32;
                string buttonText = isLeft ? "<" : ">";

                container.AddRect("ItemCategoryPageButton", "ItemCategory_Background", "0.25 0.25 0.25 1", "0.5 0", "0.5 0", $"{xMin} 0", $"{xMax} 32");
                container.AddText(string.Empty, "ItemCategoryPageButton", "0.53 0.53 0.53 1", buttonText, TextAnchor.MiddleCenter, 18, "bold", "0 0", "1 1", "0 0", "0 0");
                container.AddButton(string.Empty, "ItemCategoryPageButton", "0 0", "1 1", string.Empty, string.Empty, command);
            }

            private static void DrawElements(BasePlayer player, string lootTableName, string filter, bool isPrefab, HashSet<ElementInfo> elements)
            {
                CuiHelper.DestroyUi(player, "ItemChooser_Background");

                CuiElementContainer container = new CuiElementContainer();
                container.AddRect("ItemChooser_Background", BackgroundName, "0 0 0 0", "0.5 0.5", "0.5 0.5", "-336 -251", $"336 251");

                int yMinSearchPanel = isPrefab ? 251 : 296;

                if (!isPrefab)
                {
                    container.AddRect("ItemList_SearchPanel", "ElementChooser_Background", "0.1 0.1 0.1 1", "1 0.5", "1 0.5", $"-554 {yMinSearchPanel}", $"-304 {yMinSearchPanel + 30}");
                    container.AddImage(string.Empty, "ItemList_SearchPanel", _ins._imageIDs["Search_Icon"], filter, "0 0.5", "0 0.5", "5 -8", "21 8");
                    container.AddInputField(string.Empty, "ItemList_SearchPanel", "0.44 0.44 0.44 1", filter, TextAnchor.MiddleLeft, 16, "bold", "0 0", "1 1", "26 0", "0 0", $"ChooseElementPanel_SearchElement_LootManager {lootTableName} {false}");
                }

                int size = 83;
                int borderDelta = 10;
                int delta = 8;

                int totalRow = elements.Count == 0 ? 1 : (int)Math.Ceiling((double)elements.Count / 7);
                int scrollHeigh = borderDelta + totalRow * (size + delta);
                int scrollBackgroundHeigh = totalRow <= 5 ? scrollHeigh + delta : 482;

                container.AddRect("ItemList_ScrollBackground", "ItemChooser_Background", "0 0 0 0", "0 1", "0 1", $"10 -{scrollBackgroundHeigh}", "662 -10");
                CuiScrollViewComponent cuiScrollViewComponent = new CuiScrollViewComponent
                {
                    ContentTransform = new CuiRectTransformComponent { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = $"-336 -{scrollHeigh}", OffsetMax = "336 -10" },

                    Elasticity = float.MinValue,
                    Horizontal = false,
                    MovementType = ScrollRect.MovementType.Clamped,
                    ScrollSensitivity = 5f,
                    Vertical = true,
                    VerticalScrollbar = new CuiScrollbar()
                    {
                        AutoHide = false,
                        HandleColor = "0.57 0.18 0.12 1",
                        HighlightColor = "0.89 0.85 0.82 1",
                        Invert = false,
                        PressedColor = "0.89 0.85 0.82 1",
                        Size = 6f,
                        TrackColor = "0 0 0 0"
                    }
                };
                container.Add(new CuiElement
                {
                    Name = "ItemList_Scroll",
                    Parent = "ItemList_ScrollBackground",
                    Components = { cuiScrollViewComponent }
                });

                int column = 0;
                int row = 0;

                foreach (ElementInfo elementInfo in elements)
                {
                    int xMin = borderDelta + column * (delta + size);
                    int yMax = -borderDelta - row * (delta + size);
                    int yMin = yMax - size;
                    int xMax = xMin + size;
                    string itemPanelName = $"ItemPanel_{elementInfo.Name}";

                    container.AddRect(itemPanelName, "ItemList_Scroll", "0.18 0.18 0.18 1", "0 1", "0 1", $"{xMin} {yMin}", $"{xMax} {yMax}");

                    if (elementInfo.ItemId != 0)
                        container.AddItemIcon(string.Empty, itemPanelName, elementInfo.ItemId, 0, "0.5 0.5", "0.5 0.5", "-34 -34", "34 34");
                    else
                    {
                        container.AddImage(string.Empty, itemPanelName, elementInfo.ImageId, string.Empty, "0.5 0.5", "0.5 0.5", "-34 -34", "34 34");
                    }

                    container.AddText(string.Empty, itemPanelName, "0.89 0.87 0.82 1", elementInfo.Name, TextAnchor.LowerCenter, 10, "regular", "0 0", "0 0", "5 3", "79 50");
                    container.AddButton(string.Empty, itemPanelName, "0 0", "1 1", string.Empty, string.Empty, elementInfo.Command);

                    column++;
                    if (column > 6)
                    {
                        column = 0;
                        row++;
                    }
                }

                CuiHelper.AddUi(player, container);
            }

            private class ElementInfo
            {
                public readonly string Name;
                public readonly string Command;
                public readonly int ItemId;
                public readonly string ImageId;

                public ElementInfo(string name, string command, int itemId, string imageId)
                {
                    Name = name;
                    Command = command;
                    ItemId = itemId;
                    ImageId = imageId;
                }
            }
        }


        [ConsoleCommand("ElementEditor_Save_LootManager")]
        private void ElementEditorCloseCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;

            if (arg.Args == null || arg.Args.Length < 3)
                return;

            string lootTablePreset = arg.Args[0];
            bool isJustCreated = arg.Args[1].ToBool();
            bool isPrefabSetting = arg.Args[2].ToBool();

            CuiHelper.DestroyUi(player, "ItemEditor_Background");

            if (!isJustCreated)
                EditPanelGui.RightPanel.DrawElements(player, lootTablePreset, isPrefabSetting, 0, string.Empty);
        }

        [ConsoleCommand("ElementEditor_Switch_LootManager")]
        private void ElementEditorSwitchCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;

            if (arg.Args == null || arg.Args.Length < 5)
                return;

            string lootTablePreset = arg.Args[0];
            int itemIndex = arg.Args[1].ToInt();
            bool isPrefab = arg.Args[2].ToBool();
            bool isJustCreated = arg.Args[3].ToBool();
            string fieldName = arg.Args[4];

            if (!_ins._lootTables.TryGetValue(lootTablePreset, out LootTableData lootTableData))
                return;

            if (!isPrefab)
            {
                ItemData itemData = lootTableData.Items[itemIndex];

                if (fieldName == "isBluePrint")
                    itemData.IsBluePrint = !itemData.IsBluePrint;
            }

            SaveLootTable(lootTablePreset, lootTableData);
            ElementEditor.Draw(player, lootTablePreset, itemIndex, isPrefab);
        }

        [ConsoleCommand("ElementEditor_InputField_LootManager")]
        private void ElementEditorInputFieldCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;

            if (arg.Args == null || arg.Args.Length < 6)
                return;

            string lootTablePreset = arg.Args[0];
            int itemIndex = arg.Args[1].ToInt();
            bool isPrefab = arg.Args[2].ToBool();
            bool isJustCreated = arg.Args[3].ToBool();
            string fieldName = arg.Args[4];
            const int startInputArgIndex = 5;

            if (!_ins._lootTables.TryGetValue(lootTablePreset, out LootTableData lootTableData))
                return;

            if (isPrefab)
            {
                PrefabData prefabFata = lootTableData.Prefabs[itemIndex];

                if (fieldName == "chance")
                {
                    if (float.TryParse(arg.Args[startInputArgIndex], out float chance))
                        prefabFata.Chance = chance;
                }
                else if (fieldName == "minAmount")
                {
                    if (int.TryParse(arg.Args[startInputArgIndex], out int minAmount) && minAmount >= 0)
                    {
                        prefabFata.MinAmount = minAmount;
                        prefabFata.MaxAmount = Math.Max(prefabFata.MinAmount, prefabFata.MaxAmount);
                    }
                }
                else if (fieldName == "maxAmount")
                {
                    if (int.TryParse(arg.Args[startInputArgIndex], out int maxAmount) && maxAmount >= 0)
                    {
                        prefabFata.MaxAmount = maxAmount;
                        prefabFata.MinAmount = Math.Min(prefabFata.MinAmount, prefabFata.MaxAmount);
                    }
                }
            }
            else
            {
                ItemData itemData = lootTableData.Items[itemIndex];

                if (fieldName == "name")
                {
                    string displayName = GetStringFromArgs(arg.Args, startInputArgIndex);
                    itemData.CustomDisplayName = displayName;
                }
                if (fieldName == "owner")
                {
                    string ownerName = GetStringFromArgs(arg.Args, startInputArgIndex);
                    itemData.OwnerDisplayName = ownerName;
                }
                else if (fieldName == "skin")
                {
                    if (ulong.TryParse(arg.Args[startInputArgIndex], out ulong skin))
                        itemData.Skin = skin;
                }
                else if (fieldName == "chance")
                {
                    if (float.TryParse(arg.Args[startInputArgIndex], out float chance))
                        itemData.Chance = chance;
                }
                else if (fieldName == "minAmount")
                {
                    if (int.TryParse(arg.Args[startInputArgIndex], out int minAmount) && minAmount >= 0)
                    {
                        itemData.MinAmount = minAmount;
                        itemData.MaxAmount = Math.Max(itemData.MinAmount, itemData.MaxAmount);
                    }
                }
                else if (fieldName == "maxAmount")
                {
                    if (int.TryParse(arg.Args[startInputArgIndex], out int maxAmount) && maxAmount >= 0)
                    {
                        itemData.MaxAmount = maxAmount;
                        itemData.MinAmount = Math.Min(itemData.MinAmount, itemData.MaxAmount);
                    }
                }
                else if (fieldName == "genome")
                {
                    itemData.Genomes = GetStringFromArgs(arg.Args, startInputArgIndex);
                }

                itemData.CheckAndUpdateValues();
            }

            SaveLootTable(lootTablePreset, lootTableData);
            ElementEditor.Draw(player, lootTablePreset, itemIndex, isPrefab);
        }
        private static class ElementEditor
        {
            public static void Draw(BasePlayer player, string lootTablePreset, int elementIndex, bool isPrefab)
            {
                CuiHelper.DestroyUi(player, "ItemEditor_Background");
                CuiElementContainer container = new CuiElementContainer();
                bool ru = IsRuLang(player.UserIDString);

                if (!_ins._lootTables.TryGetValue(lootTablePreset, out LootTableData lootTableData))
                    return;

                if (isPrefab)
                {
                    PrefabData prefabData = lootTableData.Prefabs[elementIndex];
                    DrawPrefab(ru, ref container, lootTablePreset, prefabData, elementIndex);
                }
                else
                {
                    ItemData itemData = lootTableData.Items[elementIndex];
                    DrawItem(ru, ref container, lootTablePreset, itemData, elementIndex);
                }

                CuiHelper.AddUi(player, container);
            }

            private static void DrawPrefab(bool ru, ref CuiElementContainer container, string lootTableName, PrefabData prefabData, int elementIndex)
            {
                PrefabInfo prefabInfo = _ins._prefabInfos.FirstOrDefault(x => x.PrefabNames.Contains(prefabData.PrefabName));

                container.AddRect("ItemEditor_Background", BackgroundName, "0 0 0 0.85", "0 0", "1 1", "0 0", "0 0");
                container.AddRect("ItemEditor_Panel", "ItemEditor_Background", "0.13 0.13 0.13 1", "0.5 1", "0.5 1", "-185 -540", "185 -160");
                container.AddRect("Image_Panel", "ItemEditor_Panel", "0.11 0.11 0.11 1", "0.5 1", "0.5 1", "-165 -180", "165 -20");
                container.AddImage(string.Empty, "Image_Panel", _ins._imageIDs[prefabInfo.ImageName], string.Empty, "0.5 0.5", "0.5 0.5", "-66 -66", "66 66");

                container.AddRect("ItemEditor_ScrollBackground", "ItemEditor_Panel", "0 0 0 0", "0 1", "0 1", "0 -536", "362 -195");
                int yMax = -190;
                DrawInputElement(ref container, ref yMax, false, GetMessage("GUI_CHANCE", ru), prefabData.Chance.ToString(), $"ElementEditor_InputField_LootManager {lootTableName} {elementIndex} {true} {false} chance");
                DrawDoubleInputElement(ref container, ref yMax, GetMessage("GUI_AMOUNT", ru), prefabData.MinAmount.ToString(), prefabData.MaxAmount.ToString(), $"ElementEditor_InputField_LootManager {lootTableName} {elementIndex} {true} {false} minAmount", $"ElementEditor_InputField_LootManager {lootTableName} {elementIndex} {true} {false} maxAmount");

                DrawSaveButton(ru, ref container, $"ElementEditor_Save_LootManager {lootTableName} {false} {true}");
            }

            private static void DrawItem(bool ru, ref CuiElementContainer container, string lootTableName, ItemData itemData, int elementIndex)
            {
                ItemDefinition itemDefinition = ItemManager.FindItemDefinition(itemData.ShortName);
                container.AddRect("ItemEditor_Background", BackgroundName, "0 0 0 0.85", "0 0", "1 1", "0 0", "0 0");
                container.AddRect("ItemEditor_Panel", "ItemEditor_Background", "0.13 0.13 0.13 1", "0.5 1", "0.5 1", "-185 -700", "185 -20");
                container.AddRect("Image_Panel", "ItemEditor_Panel", "0.11 0.11 0.11 1", "0.5 1", "0.5 1", "-165 -180", "165 -20");

                if (itemData.IsBluePrint)
                    container.AddItemIcon(string.Empty, "Image_Panel", _ins._bluePrintItemDefinition.itemid, 0, "0.5 0.5", "0.5 0.5", "-66 -66", "66 66");

                container.AddItemIcon(string.Empty, "Image_Panel", itemDefinition.itemid, itemData.Skin, "0.5 0.5", "0.5 0.5", "-66 -66", "66 66");


                int yMax = -190;

                DrawInputElement(ref container, ref yMax, true, "<b>SHORTNAME</b>", itemData.ShortName, $"ElementEditor_InputField_LootManager {lootTableName} {elementIndex} {false} {false} shortname");
                DrawInputElement(ref container, ref yMax, false, GetMessage("GUI_NAME", ru), !string.IsNullOrEmpty(itemData.CustomDisplayName) ? itemData.CustomDisplayName : itemData.DefaultDisplayName, $"ElementEditor_InputField_LootManager {lootTableName} {elementIndex} {false} {false} name");
                DrawInputElement(ref container, ref yMax, false, GetMessage("GUI_OWNER_NAME", ru), !string.IsNullOrEmpty(itemData.OwnerDisplayName) ? itemData.OwnerDisplayName : string.Empty, $"ElementEditor_InputField_LootManager {lootTableName} {elementIndex} {false} {false} owner");

                DrawInputElement(ref container, ref yMax, false, "<b>SKIN</b>", itemData.Skin.ToString(), $"ElementEditor_InputField_LootManager {lootTableName} {elementIndex} {false} {false} skin");
                DrawInputElement(ref container, ref yMax, false, GetMessage("GUI_CHANCE", ru), itemData.Chance.ToString(), $"ElementEditor_InputField_LootManager {lootTableName} {elementIndex} {false} {false} chance");
                DrawDoubleInputElement(ref container, ref yMax, GetMessage("GUI_AMOUNT", ru), itemData.MinAmount.ToString(), itemData.MaxAmount.ToString(), $"ElementEditor_InputField_LootManager {lootTableName} {elementIndex} {false} {false} minAmount", $"ElementEditor_InputField_LootManager {lootTableName} {elementIndex} {false} {false} maxAmount");

                if (itemDefinition.amountType == ItemDefinition.AmountType.Genetics)
                    DrawInputElement(ref container, ref yMax, false, GetMessage("GUI_GENOMES", ru), itemData.Genomes, $"ElementEditor_InputField_LootManager {lootTableName} {elementIndex} {false} {false} genome");
                else if (itemDefinition.Blueprint != null)
                    DrawSwitchElement(ref container, ref yMax, GetMessage("GUI_IS_BLUEPRINT", ru), itemData.IsBluePrint, $"ElementEditor_Switch_LootManager {lootTableName} {elementIndex} {false} {false} isBluePrint");

                DrawSaveButton(ru, ref container, $"ElementEditor_Save_LootManager {lootTableName} {false} {false}");
            }

            private static void DrawInputElement(ref CuiElementContainer container, ref int yMax, bool isDisabled, string name, string inputPanelText, string command)
            {
                const int xMin = 20;
                const int xMax = 350;
                const int ySize = 50;
                int yMin = yMax - ySize;
                string fieldColor = isDisabled ? "0.16 0.16 0.16 1" : "0.1 0.1 0.1 1";
                string parentLayerName = "ItemEditor_Panel";

                container.AddRect("ItemEditor_InputField", parentLayerName, "0 0 0 0", "0 1", "0 1", $"{xMin} {yMin}", $"{xMax} {yMax}");
                container.AddText(string.Empty, "ItemEditor_InputField", "0.89 0.87 0.82 1", name, TextAnchor.UpperLeft, 16, "regular", "0 1", "1 1", "0 -22", "0 -3");
                container.AddRect("ItemEditor_InputRect", "ItemEditor_InputField", fieldColor, "0 0", "1 0", "0 0", "0 30");

                if (isDisabled)
                {
                    container.AddImage(string.Empty, "ItemEditor_InputRect", _ins._imageIDs["Lock_Icon"], string.Empty, "1 0.5", "1 0.5", "-21 -8", "-5 8");
                    container.AddRect(string.Empty, "ItemEditor_InputRect", "0.31 0.31 0.31 1", "0 0", "0 0", "1 0", "329 1");
                    container.AddRect(string.Empty, "ItemEditor_InputRect", "0.31 0.31 0.31 1", "0 1", "0 1", "1 -1", "329 0");
                    container.AddRect(string.Empty, "ItemEditor_InputRect", "0.31 0.31 0.31 1", "0 0", "0 0", "0 0", "1 32");
                    container.AddRect(string.Empty, "ItemEditor_InputRect", "0.31 0.31 0.31 1", "1 0", "1 0", "-1 0", "0 32");
                }

                container.AddInputField(string.Empty, "ItemEditor_InputRect", "0.52 0.52 0.52 1", inputPanelText, TextAnchor.MiddleLeft, 16, "regular", "0 0", "1 1", "6 0", "0 0", command);
                yMax = yMin - 10;
            }

            private static void DrawDoubleInputElement(ref CuiElementContainer container, ref int yMax, string name, string leftPanelText, string rightPanelText, string leftCommand, string rightCommand)
            {
                int xMin = 20;
                int xMax = 350;
                int ySize = 50;
                int yMin = yMax - ySize;
                string parentLayerName = "ItemEditor_Panel";

                container.AddRect("ItemEditor_InputField", parentLayerName, "0 0 0 0", "0 1", "0 1", $"{xMin} {yMin}", $"{xMax} {yMax}");
                container.AddText(string.Empty, "ItemEditor_InputField", "0.89 0.87 0.82 1", name, TextAnchor.UpperLeft, 16, "regular", "0 1", "1 1", "0 -22", "0 -3");

                container.AddRect("ItemEditor_InputRect_Left", "ItemEditor_InputField", "0.1 0.1 0.1 1", "0 0", "0 0", "0 0", "155 30");
                container.AddInputField(string.Empty, "ItemEditor_InputRect_Left", "0.52 0.52 0.52 1", leftPanelText, TextAnchor.MiddleLeft, 16, "regular", "0 0", "1 1", "6 0", "0 0", leftCommand);

                container.AddRect("ItemEditor_InputRect_Right", "ItemEditor_InputField", "0.1 0.1 0.1 1", "1 0", "1 0", "-155 0", "0 30");
                container.AddInputField(string.Empty, "ItemEditor_InputRect_Right", "0.52 0.52 0.52 1", rightPanelText, TextAnchor.MiddleLeft, 16, "regular", "0 0", "1 1", "6 0", "0 0", rightCommand);
                yMax = yMin - 10;
            }

            private static void DrawSwitchElement(ref CuiElementContainer container, ref int yMax, string name, bool state, string command)
            {
                const int xMin = 20;
                const int xMax = 350;
                const int ySize = 40;
                int yMin = yMax - ySize;
                string parentLayerName = "ItemEditor_Panel";
                container.AddRect("ItemEditor_Switch", parentLayerName, "0 0 0 0", "0 1", "0 1", $"{xMin} {yMin}", $"{xMax} {yMax}");
                container.AddText(string.Empty, "ItemEditor_Switch", "0.89 0.87 0.82 1", name, TextAnchor.MiddleLeft, 16, "regular", "0 0", "1 1", "0 0", "0 0");

                const int leftSquareXMin = -62;
                const int rightSquareXMin = -32;
                int blackSquareXMin = state ? leftSquareXMin : rightSquareXMin;
                int activeSquareXMin = state ? rightSquareXMin : leftSquareXMin;

                CreateSwitchSquare(ref container, blackSquareXMin, "0.1 0.1 0.1 1", "0.27 0.27 0.27 1");
                CreateSwitchSquare(ref container, activeSquareXMin, state ? "0.42 0.67 0.27 1" : "0.69 0.25 0.25 1", state ? "0.25 0.40 0.16 1" : "0.41 0.15 0.15 1");
                container.AddButton(string.Empty, "ItemEditor_Switch", "1 0", "1 0", $"{leftSquareXMin} 0", "0 32", command);

                yMax = yMin - 10;
            }

            private static void CreateSwitchSquare(ref CuiElementContainer container, int xMin, string mainColor, string sideColor)
            {
                container.AddRect("ItemEditor_BlackSwitchPart", "ItemEditor_Switch", mainColor, "1 0.5", "1 0.5", $"{xMin} -16", $"{xMin + 32} 16");
                container.AddRect(string.Empty, "ItemEditor_BlackSwitchPart", sideColor, "0 0", "0 0", "2 0", "30 2");
                container.AddRect(string.Empty, "ItemEditor_BlackSwitchPart", sideColor, "0 0", "0 0", "2 30", "30 32");
                container.AddRect(string.Empty, "ItemEditor_BlackSwitchPart", sideColor, "0 0", "0 0", "0 0", "2 32");
                container.AddRect(string.Empty, "ItemEditor_BlackSwitchPart", sideColor, "0 0", "0 0", "30 0", "32 32");
            }

            private static void DrawSaveButton(bool ru, ref CuiElementContainer container, string command)
            {
                container.AddRect("ItemEditor_SaveButton", "ItemEditor_Panel", "0.42 0.67 0.27 1", "0 0", "1 0", "20 20", "-20 56");
                container.AddText(string.Empty, "ItemEditor_SaveButton", "0.68 0.96 0.4 1", GetMessage("GUI_BACK", ru), TextAnchor.MiddleCenter, 16, "bold", "0 0", "1 1", "0 0", "0 0");
                container.AddButton(string.Empty, "ItemEditor_SaveButton", "0 0", "1 1", string.Empty, string.Empty, command);
            }
        }


        [ConsoleCommand("ErrorDisplayer_Close")]
        private void ErrorDisplayerCloseCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerHavePermission(player))
                return;

            CuiHelper.DestroyUi(player, ErrorBackgroundName);
        }
        private static class ErrorDisplayer
        {
            public static void ShowError(BasePlayer player, string message)
            {
                int panelLength = (int)(36 + message.Length * 7.5f);
                CuiElementContainer container = new CuiElementContainer();
                container.Add(new CuiPanel
                {
                    Image = { Color = "0 0 0 0.6", Material = "assets/content/ui/uibackgroundblur.mat" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    CursorEnabled = true,
                    KeyboardEnabled = true
                }, "Overlay", ErrorBackgroundName);

                container.AddRect(string.Empty, ErrorBackgroundName, "0.57 0.18 0.12 1", "0.5 0", "0.5 0", $"-{panelLength / 2} 45", "25 48");
                container.AddRect(string.Empty, ErrorBackgroundName, "0.31 0.16 0.14 1", "0.5 0", "0.5 0", "25 45", $"{panelLength / 2} 48");

                container.AddRect("ErrorPanel_Background", ErrorBackgroundName, "0.57 0.18 0.12 1", "0.5 0", "0.5 0", $"-{panelLength / 2} 52", $"{panelLength / 2} 84");
                container.AddText(string.Empty, "ErrorPanel_Background", "0.83 0.71 0.69 1", message, TextAnchor.MiddleLeft, 16, "bold", "0 0", "1 1", "36 0", "0 0");
                container.AddImage(string.Empty, "ErrorPanel_Background", _ins._imageIDs["Error_Icon"], string.Empty, "0 0", "0 0", "8 6", "28 26");
                container.AddButton(string.Empty, ErrorBackgroundName, "0 0", "1 1", "0 0", "0 0", "ErrorDisplayer_Close");

                CuiHelper.AddUi(player, container);
            }
        }
        
        private readonly Dictionary<ulong, PlayerGuiInfo> _playerGuiInfos = new Dictionary<ulong, PlayerGuiInfo>();

        private class PlayerGuiInfo
        {
            public string EditingPreset = string.Empty;
            public string FilterPlugin = string.Empty;
            public string FilterCategory = string.Empty;
            public int LeftPage = 0;
            public int RightPage = 0;
            public string RightSearch = string.Empty;
            public ItemCategory ItemCategorySelected = ItemCategory.Weapon;
            public string PrefabSelected = string.Empty;
        }
        #endregion GUI

        #region Lang
        private class LocalizeMessage
        {
            public string RuMessage;
            public string EnMessage;
        }

        private readonly Dictionary<string, LocalizeMessage> _localizeMessages = new Dictionary<string, LocalizeMessage>
        {
            ["PRESET_ALREADY_EXISTS"] = new LocalizeMessage
            {
                EnMessage = "A preset with this name already exists",
                RuMessage = "Пресет с таким именем уже существует",
            },
            ["INVALID_PRESET_NAME_MULTIPLE_WORDS"] = new LocalizeMessage
            {
                EnMessage = "Preset name must be a single word (e.g., 'LootTable_1')",
                RuMessage = "Название пресета должно быть одним словом (пример: 'LootTable_1')"
            },
            ["CLEAR_DEFAULT_CONFLICTS_WITH_EXTERNAL_LOOT"] = new LocalizeMessage
            {
                EnMessage = "Clear Default Contents + external loot without override → empty container. Disable clear or set override.",
                RuMessage = "Очистить контейнер + Лут из другого плагина без указанной лутовой таблицы → пустой контейнер"
            },
            ["GUI_LOOT_TABLES"] = new LocalizeMessage
            {
                EnMessage = "LOOT TABLES",
                RuMessage = "СПИСОК ЛУТОВЫХ ТАБЛИЦ"
            },
            ["GUI_CREATE_NEW_PRESET"] = new LocalizeMessage
            {
                EnMessage = "CREATE NEW PRESET",
                RuMessage = "СОЗДАТЬ НОВЫЙ ПРЕСЕТ"
            },
            ["GUI_GENERAL"] = new LocalizeMessage
            {
                EnMessage = "GENERAL",
                RuMessage = "ОСНОВНОЕ"
            },
            ["GUI_PRESET_NAME"] = new LocalizeMessage
            {
                EnMessage = "<b>PRESET</b> <color=#707070>NAME</color>",
                RuMessage = "<b>ИМЯ</b> <color=#707070>ПРЕСЕТА</color>"
            },
            ["GUI_DESCRIPTION"] = new LocalizeMessage
            {
                EnMessage = "<b>DESCRIPTION</b>",
                RuMessage = "<b>ОПИСАНИЕ</b>"
            },
            ["GUI_CLEAR_DEFAULT_ITEMS"] = new LocalizeMessage
            {
                EnMessage = "<b>CLEAR</b> <color=#707070>DEFAULT CONTENTS</color>",
                RuMessage = "<b>ОЧИСТИТЬ</b> <color=#707070>КОНТЕЙНЕР</color>"
            },
            ["GUI_CLEAR_DEFAULT_ITEMS_SUBNAME"] = new LocalizeMessage
            {
                EnMessage = "DELETE ITEMS BEFORE ADDING",
                RuMessage = "УДАЛИТЬ ПРЕДМЕТЫ ПЕРЕД ДОБАВЛЕНИЕМ"
            },
            ["GUI_ITEM_TABLE"] = new LocalizeMessage
            {
                EnMessage = "ITEM TABLE",
                RuMessage = "ТАБЛИЦА ПРЕДМЕТОВ"
            },
            ["GUI_ADD_ITEMS"] = new LocalizeMessage
            {
                EnMessage = "<b>ADD ITEMS</b> <color=#707070>FROM LIST</color>",
                RuMessage = "<b>ДОБАВИТЬ ПРЕДМЕТЫ</b> <color=#707070>ИЗ СПИСКА</color>"
            },
            ["GUI_USE_SETTINGS_BELOW"] = new LocalizeMessage
            {
                EnMessage = "<b>USE SETTINGS BELOW</b> <color=#707070> </color>",
                RuMessage = "<b>ИСПОЛЬЗОВАТЬ ПАРАМЕТРЫ НИЖЕ</b> <color=#707070> </color>"
            },
            ["GUI_USE_SETTINGS_BELOW_SUBNAME"] = new LocalizeMessage
            {
                EnMessage = "EXACT MIN/MAX VALUES",
                RuMessage = "ТОЧНЫЕ ЗНАЧЕНИЯ МИН/МАКС"
            },
            ["GUI_MIN_ITEMS"] = new LocalizeMessage
            {
                EnMessage = "<b>MIN</b> <color=#707070>ITEMS</color>",
                RuMessage = "<b>МИН КОЛ-ВО</b> <color=#707070>ПРЕДМЕТОВ</color>"
            },
            ["GUI_MAX_ITEMS"] = new LocalizeMessage
            {
                EnMessage = "<b>MAX</b> <color=#707070>ITEMS</color>",
                RuMessage = "<b>МАКС КОЛ-ВО</b> <color=#707070>ПРЕДМЕТОВ</color>"
            },
            ["GUI_PREFAB_TABLE"] = new LocalizeMessage
            {
                EnMessage = "PREFAB TABLE",
                RuMessage = "ТАБЛИЦА ПРЕФАБОВ"
            },
            ["GUI_ADD_PREFABS"] = new LocalizeMessage
            {
                EnMessage = "<b>ADD PREFABS</b> <color=#707070>FROM LIST</color>",
                RuMessage = "<b>ДОБАВИТЬ ПРЕФАБЫ</b> <color=#707070>ИЗ СПИСКА</color>"
            },
            ["GUI_ADD_PREFABS_SUBNAME"] = new LocalizeMessage
            {
                EnMessage = "DEFAULT LOOT FROM ENTITIES",
                RuMessage = "СТАНДАРТНЫЙ ЛУТ ИЗ СУЩНОСТЕЙ"
            },
            ["GUI_MIN_PREFABS"] = new LocalizeMessage
            {
                EnMessage = "<b>MIN</b> <color=#707070>PREFABS</color>",
                RuMessage = "<b>МИН КОЛ-ВО</b> <color=#707070>ПРЕФАБОВ</color>"
            },
            ["GUI_MAX_PREFABS"] = new LocalizeMessage
            {
                EnMessage = "<b>MAX</b> <color=#707070>PREFABS</color>",
                RuMessage = "<b>МАКС КОЛ-ВО</b> <color=#707070>ПРЕФАБОВ</color>"
            },
            ["GUI_ALLOW_PLUGIN_SPAWN_LOOT"] = new LocalizeMessage
            {
                EnMessage = "<b>ALLOW PLUGIN</b> <color=#707070>TO SPAWN LOOT</color>",
                RuMessage = "<b>РАЗРЕШИТЬ</b> <color=#707070>СПАВНИТЬ ЛУТ</color>"
            },
            ["GUI_ALLOW_PLUGIN_SPAWN_LOOT_SUBNAME_ALPHALOOT"] = new LocalizeMessage
            {
                EnMessage = "ALPHALOOT MAY CHANGE LOOT",
                RuMessage = "ALPHALOOT СМОЖЕТ ИЗМЕНЯТЬ ЛУТ"
            },
            ["GUI_ALLOW_PLUGIN_SPAWN_LOOT_SUBNAME_CUSTOMLOOT"] = new LocalizeMessage
            {
                EnMessage = "CUSTOMLOOT MAY CHANGE LOOT",
                RuMessage = "CUSTOMLOOT СМОЖЕТ ИЗМЕНЯТЬ ЛУТ"
            },
            ["GUI_ALLOW_PLUGIN_SPAWN_LOOT_SUBNAME_LOOTTABLE"] = new LocalizeMessage
            {
                EnMessage = "LOOT TABLE PLUGIN MAY CHANGE LOOT",
                RuMessage = "LOOT TABLE СМОЖЕТ ИЗМЕНЯТЬ ЛУТ"
            },
            ["GUI_OVERRIDE_LOOT_TABLE"] = new LocalizeMessage
            {
                EnMessage = "<b>OVERRIDE</b> <color=#707070>LOOT TABLE</color>",
                RuMessage = "<b>ПЕРЕОПРЕДЕЛИТЬ</b> <color=#707070>ЛУТ-ТАБЛИЦУ</color>"
            },
            ["GUI_PREFABS_INCLUDED"] = new LocalizeMessage
            {
                EnMessage = "PREFABS INCLUDED IN THE LOOT TABLE",
                RuMessage = "ПРЕФАБЫ В ЭТОЙ ЛУТОВОЙ ТАБЛИЦЕ"
            },
            ["GUI_ITEMS_INCLUDED"] = new LocalizeMessage
            {
                EnMessage = "ITEMS INCLUDED IN THE LOOT TABLE",
                RuMessage = "ПРЕДМЕТЫ В ЭТОЙ ЛУТОВОЙ ТАБЛИЦЕ"
            },
            ["GUI_ELEMENTS"] = new LocalizeMessage
            {
                EnMessage = "ELEMENTS: ",
                RuMessage = "КОЛИЧЕСТВО ЭЛЕМЕНТОВ: "
            },
            ["GUI_NAME"] = new LocalizeMessage
            {
                EnMessage = "<b>NAME</b>",
                RuMessage = "<b>ИМЯ</b>"
            },
            ["GUI_OWNER_NAME"] = new LocalizeMessage
            {
                EnMessage = "<b>OWNER NAME</b>",
                RuMessage = "<b>ИМЯ ВЛАДЕЛЬЦА</b>"
            },
            ["GUI_CHANCE"] = new LocalizeMessage
            {
                EnMessage = "<b>CHANCE</b> <size=16><color=#707070>0.0-100.0</color></size>",
                RuMessage = "<b>ШАНС</b> <size=16><color=#707070>0.0–100.0</color></size>"
            },
            ["GUI_AMOUNT"] = new LocalizeMessage
            {
                EnMessage = "<b>AMOUNT</b> <size=16><color=#707070>MIN–MAX</color></size>",
                RuMessage = "<b>КОЛИЧЕСТВО</b> <size=16><color=#707070>МИН–МАКС</color></size>"
            },
            ["GUI_GENOMES"] = new LocalizeMessage
            {
                EnMessage = "<b>GENOMES</b> <size=16><color=#707070>(E.G. GGGYYY, HHHWWW)</color></size>",
                RuMessage = "<b>ГЕНОМЫ</b> <size=16><color=#707070>(НАПР.: GGGYYY, HHHWWW)</color></size>"
            },
            ["GUI_IS_BLUEPRINT"] = new LocalizeMessage
            {
                EnMessage = "<b>IS BLUEPRINT</b>",
                RuMessage = "<b>ЭТО ЧЕРТЕЖ</b>"
            },
            ["GUI_CANCEL"] = new LocalizeMessage
            {
                EnMessage = "CANCEL",
                RuMessage = "ОТМЕНИТЬ"
            },
            ["GUI_SAVE"] = new LocalizeMessage
            {
                EnMessage = "SAVE",
                RuMessage = "СОХРАНИТЬ"
            },
            ["GUI_DELETE"] = new LocalizeMessage
            {
                EnMessage = "DELETE",
                RuMessage = "УДАЛИТЬ"
            },
            ["GUI_BACK"] = new LocalizeMessage
            {
                EnMessage = "BACK",
                RuMessage = "НАЗАД"
            },
            ["GUI_INSTRUCTION_HEADER"] = new LocalizeMessage
            {
                EnMessage = "MANUAL",
                RuMessage = "ИНСТРУКЦИЯ"
            },
            ["GUI_INSTRUCTION_ABOUT_PLUGIN_HEADER"] = new LocalizeMessage
            {
                EnMessage = "ABOUT LOOTMANAGER PLUGIN",
                RuMessage = "О ПЛАГИНЕ LOOTMANAGER"
            },
            ["GUI_INSTRUCTION_ABOUT_PLUGIN_TEXT"] = new LocalizeMessage
            {
                EnMessage = "LootManager is a unified loot-table system for MadMappers events and plugins. It stores and allows you to edit loot tables, then fills NPCs and containers of other plugins with them\n<size=8>\n</size>" +
                            "To apply a loot table to a specific object, enter its name in the config of that object in the “LootManager Preset” field of the plugin that spawns it. Works only with plugins marked as compatible on the plugin page",
                RuMessage = "LootManager — единая система лут-таблиц для событий и плагинов MadMappers.Он хранит и позволяет редактировать лутовые таблицы, а затем наполняет ими NPC и ящики сторонних плагинов\n<size=8>\n</size>" +
                            "Чтобы применить лут-таблицу к конкретному объекту, впишите её имя в конфиг этого объекта в поле “LootManager Preset” в плагине, который спавнит объект.Работает только с плагинами, заявленными как совместимые на странице плагина"
            },
            ["GUI_INSTRUCTION_GENERAL_HEADER"] = new LocalizeMessage
            {
                EnMessage = "GENERAL SECTION",
                RuMessage = "РАЗДЕЛ ОСНОВНОЕ"
            },
            ["GUI_INSTRUCTION_GENERAL_TEXT"] = new LocalizeMessage
            {
                EnMessage = "Clear container — delete items from the container before adding loot from the Items and Prefabs sections. Useful if you want to completely replace the default loot",
                RuMessage = "Очистить контейнер — удалить предеметы из контейнара перед добавлением лута из разделов Items и Prefabs. Полезно, если хотите полностью заменить стандартный лут"
            },
            ["GUI_INSTRUCTION_ITEMS_HEADER"] = new LocalizeMessage
            {
                EnMessage = "ITEM TABLE",
                RuMessage = "ТАБЛИЦА ПРЕДМЕТОВ"
            },
            ["GUI_INSTRUCTION_ITEMS_TEXT"] = new LocalizeMessage
            {
                EnMessage = "- Add any items, including custom ones (skins/blueprints/genomes)\n<size=8>\n</size>" +
                            "- Specify quantity ranges and chance for each item\n<size=8>\n</size>" +
                            "- You can set the exact number of items that will appear in the container",
                RuMessage = "- Добавляйте любые предметы, включая кастомные (скины/чертежи/геномы)\n<size=8>\n</size>" +
                            "- Указывайте диапазоны количества и шанс для каждого предмета\n<size=8>\n</size>" +
                            "- Можно задать точное число предметов, которое попадёт в контейнер"
            },
            ["GUI_INSTRUCTION_PREFABS_HEADER"] = new LocalizeMessage
            {
                EnMessage = "PREFAB TABLE",
                RuMessage = "ТАБЛИЦА ПРЕФАБОВ"
            },
            ["GUI_INSTRUCTION_PREFABS_TEXT"] = new LocalizeMessage
            {
                EnMessage = "- Mix in standard game loot from prefabs (crates, barrels, NPCs, etc.)\n<size=8>\n</size>" +
                            "- Flexibly control the amount: for example, “1 military crate + 2 barrels” in one table",
                RuMessage = "- Подмешивайте стандартный игровой лут из префабов (ящики, бочки, NPC и т.д.)\n<size=8>\n</size>" +
                            "- Гибко регулируйте объём: например, «1 военный ящик + 2 бочки» в одну таблицу"
            },
            ["GUI_INSTRUCTION_INTEGRATION_HEADER"] = new LocalizeMessage
            {
                EnMessage = "INTEGRATIONS: ALPHALOOT / CUSTOMLOOT / LOOTTABLE",
                RuMessage = "ИНТЕГРАЦИИ: ALPHALOOT / CUSTOMLOOT / LOOTTABLE"
            },
            ["GUI_INSTRUCTION_INTEGRATION_TEXT"] = new LocalizeMessage
            {
                EnMessage = "- Sections for third-party plugins\n<size=8>\n</size>" +
                            "- If these plugins are not installed — just ignore these settings\n<size=8>\n</size>" +
                            "- When integrations are enabled, you can allow external plugins to modify or add loot according to your rules",
                RuMessage = "- Разделы для сторонних плагинов\n<size=8>\n</size>" +
                            "- Если эти плагины у вас не установлены — просто игнорируйте эти настройки\n<size=8>\n</size>" +
                            "- При включении интеграций можно разрешать внешним плагинам менять/дополнять лут по вашим правилам"
            },
            ["GUI_FILTERS"] = new LocalizeMessage
            {
                EnMessage = "FILTERS",
                RuMessage = "ФИЛЬТРЫ"
            },
            ["GUI_ALL"] = new LocalizeMessage
            {
                EnMessage = "All",
                RuMessage = "Все"
            },
            ["GUI_UNUSED"] = new LocalizeMessage
            {
                RuMessage = "Неиспользуемые",
                EnMessage = "Unused"
            },
            ["GUI_ENTER_NAME"] = new LocalizeMessage
            {
                EnMessage = "ENTER THE NAME",
                RuMessage = "ВВЕДИТЕ НАЗВАНИЕ"
            }
        };
        private static string GetMessage(string langKey, bool ru)
        {
            if (!_ins._localizeMessages.TryGetValue(langKey, out LocalizeMessage localizeMessage))
            {
                _ins.PrintError($"Invalid localized message key! ({langKey})");
                return string.Empty;
            }

            return ru ? localizeMessage.RuMessage : localizeMessage.EnMessage;
        }

        private static bool IsRuLang(string userIdString)
        {
            return _ins.lang.GetLanguage(userIdString) == "ru";
        }
        #endregion Lang

        #region Configs
        private PluginConfig _config;

        protected override void LoadDefaultConfig()
        {
            _config = PluginConfig.DefaultConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<PluginConfig>();
            Config.WriteObject(_config, true);
        }
        protected override void SaveConfig()
        {
            Config.WriteObject(_config);
        }

        private class PluginConfig
        {
            [JsonProperty("Version")]
            public VersionNumber VersionConfig { get; set; }

            [JsonProperty("Display hidden items in GUI")]
            public bool IsHiddenItems { get; set; }

            [JsonProperty("Use the enhanced randomizer (requires more resources)")]
            public bool EnhancedRandomizer { get; set; }

            [JsonProperty("Permission for configuring loot")]
            public string Permission { get; set; }

            public static PluginConfig DefaultConfig()
            {
                return new PluginConfig
                {
                    VersionConfig = new VersionNumber(1, 1, 0),
                    IsHiddenItems = false,
                    EnhancedRandomizer = true,
                    Permission = "lootmanager.use",
                };
            }
        }

        #endregion Configs
    }
}

namespace Oxide.Plugins.LootManagerExtensionMethods
{
    public static class GuiDrawer
    {
        public static void AddSprite(this CuiElementContainer container, string layerName, string parentLayer, string spritePath, string color, string anchorMin, string anchorMax, string offsetMin, string offsetMax)
        {
            container.Add(new CuiElement
            {
                Name = layerName,
                Parent = parentLayer,
                Components =
                {
                    new CuiImageComponent
                    {
                        Sprite = spritePath,
                        Color = color
                    },
                    new CuiRectTransformComponent { AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax }
                }
            });
        }

        public static void AddButton(this CuiElementContainer container, string layerName, string parentLayer, string anchorMin, string anchorMax, string offsetMin, string offsetMax, string command)
        {
            container.Add(new CuiButton
            {
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax },
                Button = { Color = "0 0 0 0", Command = command },
                Text = { Text = string.Empty },
            }, parentLayer, layerName);
        }

        public static void AddRect(this CuiElementContainer container, string nameLayer, string parentLayer, string color, string anchorMin, string anchorMax, string offsetMin, string offsetMax)
        {
            container.Add(new CuiElement
            {
                Name = nameLayer,
                Parent = parentLayer,
                Components =
                {
                    new CuiImageComponent { Color = color },
                    new CuiRectTransformComponent { AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax }
                }
            });
        }

        public static void AddText(this CuiElementContainer container, string nameLayer, string parentLayer, string color, string text, TextAnchor align, int fontSize, string font, string anchorMin, string anchorMax, string offsetMin, string offsetMax)
        {
            container.Add(new CuiElement
            {
                Name = nameLayer,
                Parent = parentLayer,
                Components =
                {
                    new CuiTextComponent { Color = color, Text = text, Align = align, FontSize = fontSize, Font = $"robotocondensed-{font}.ttf" },
                    new CuiRectTransformComponent { AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax }
                }
            });
        }

        public static void AddImage(this CuiElementContainer container, string nameLayer, string parentLayer, string imageId, string color, string anchorMin, string anchorMax, string offsetMin, string offsetMax)
        {
            container.Add(new CuiElement
            {
                Name = nameLayer,
                Parent = parentLayer,
                Components =
                {
                    new CuiRawImageComponent { Png = imageId, Color = color },
                    new CuiRectTransformComponent { AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax }
                }
            });
        }

        public static void AddItemIcon(this CuiElementContainer container, string nameLayer, string parentLayer, int itemId, ulong skinId, string anchorMin, string anchorMax, string offsetMin, string offsetMax)
        {
            container.Add(new CuiElement
            {
                Name = nameLayer,
                Parent = parentLayer,
                Components =
                {
                    new CuiImageComponent { ItemId = itemId, SkinId = skinId},
                    new CuiRectTransformComponent { AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax }
                }
            });
        }

        public static void AddInputField(this CuiElementContainer container, string nameLayer, string parentLayer, string colorText, string text, TextAnchor align, int fontSize, string font, string anchorMin, string anchorMax, string offsetMin, string offsetMax, string command)
        {
            container.Add(new CuiElement
            {
                Name = nameLayer,
                Parent = parentLayer,
                Components =
                {
                    new CuiInputFieldComponent { Command = command, Color = colorText, Align = align, FontSize = fontSize, Font = $"robotocondensed-{font}.ttf", Text = text, NeedsKeyboard = true },
                    new CuiRectTransformComponent { AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax }
                }
            });
        }
    }

    public static class ExtensionMethods
    {
        public static List<TSource> ToList<TSource>(this IEnumerable<TSource> source)
        {
            List<TSource> result = new List<TSource>();
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) result.Add(enumerator.Current);
            return result;
        }

        public static bool Any<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) if (predicate(enumerator.Current)) return true;
            return false;
        }
        public static TSource ElementAt<TSource>(this IEnumerable<TSource> source, int index)
        {
            int movements = 0;
            using (var enumerator = source.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    if (movements == index) return enumerator.Current;
                    movements++;
                }
            }
            return default(TSource);
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

        public static TSource First<TSource>(this IList<TSource> source) => source[0];

        public static TSource Last<TSource>(this IList<TSource> source) => source[source.Count - 1];

        public static List<TResult> Select<TSource, TResult>(this IList<TSource> source, Func<TSource, TResult> predicate)
        {
            List<TResult> result = new List<TResult>();
            for (int i = 0; i < source.Count; i++)
            {
                TSource element = source[i];
                result.Add(predicate(element));
            }
            return result;
        }
        public static List<TSource> OrderBy<TSource>(this IEnumerable<TSource> source, Func<TSource, float> predicate)
        {
            List<TSource> result = source.ToList();
            for (int i = 0; i < result.Count; i++)
            {
                for (int j = 0; j < result.Count - 1; j++)
                {
                    if (predicate(result[j]) > predicate(result[j + 1]))
                    {
                        TSource z = result[j];
                        result[j] = result[j + 1];
                        result[j + 1] = z;
                    }
                }
            }
            return result;
        }

        public static HashSet<TSource> ToHashset<TSource>(this IEnumerable<TSource> source)
        {
            HashSet<TSource> result = new HashSet<TSource>();
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) result.Add(enumerator.Current);
            return result;
        }
    }
}