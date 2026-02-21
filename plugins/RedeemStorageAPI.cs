using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using UnityEngine;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine.UI;
#if CARBON
using Carbon;
using Carbon.Components;
using Carbon.Plugins;
#else
using Oxide.Ext.CarbonAliases;
#endif

namespace Oxide.Plugins
{
    [Info("RedeemStorageAPI", "ThePitereq", "1.2.1")]
#if CARBON
    public class RedeemStorageAPI : CarbonPlugin
#else
    public class RedeemStorageAPI : RustPlugin
#endif
    {
        [PluginReference] private readonly Plugin PopUpAPI;
        private readonly Dictionary<ulong, Dictionary<string, LootContainer>> cachedInventories = new();
        private readonly Dictionary<ulong, InventoryCache> openedInventories = new();

#if !CARBON
        public static CUI.Handler CuiHandler = new();
#endif

        private class InventoryCache
        {
            public string name;
            public ulong owner;
        }

        private void Init()
        {
            LoadConfig();
            LoadMessages();
            LoadData();
        }

        private void OnServerInitialized()
        {
            permission.RegisterPermission("redeemstorageapi.admin", this);
            foreach (var command in config.commands)
                cmd.AddChatCommand(command, this, nameof(RedeemCommand));
            if (config.itemReminder > 0)
                StartItemReminder();
            cmd.AddConsoleCommand(Community.Protect("RedeemStorageUI"), this, nameof(RedeemConsoleCommand));
            cmd.AddConsoleCommand("createredeemitem", this, nameof(RedeemItemConsoleCommand));
        }

        private void Unload()
        {
            foreach (var player in cachedInventories)
            {
                foreach (var inventory in player.Value)
                {
                    AddItemsBack(inventory.Key, inventory.Value);
                    if (inventory.Value && !inventory.Value.IsDestroyed)
                        inventory.Value.Kill();
                }
            }
            SaveData();
            foreach (var player in BasePlayer.activePlayerList)
                CuiHelper.DestroyUi(player, "RedeemStorageAPI_Inventory");
        }

        private void OnNewSave()
        {
            bool changed = false;
            foreach (var inv in config.inventories)
            {
                if (inv.Value.wipeInventory)
                {
                    storedItems[inv.Key].Clear();
                    changed = true;
                    Puts($"Wipe found! Cleared content of the {inv.Key} redeem inventory!");
                }
            }
            if (changed)
                SaveData();
        }

        private void AddItemsBack(string name, LootContainer storage)
        {
            if (storage.OwnerID == 0) return;
            foreach (var item in storage.inventory.itemList.ToArray())
            {
                AddItem(storage.OwnerID, name, item);
                item.GetHeldEntity()?.Kill();
                item.DoRemove();
            }
        }

        private void StartItemReminder()
        {
            timer.Every(config.itemReminder, () =>
            {
                foreach (var player in BasePlayer.activePlayerList)
                    foreach (var storage in storedItems)
                        if (storage.Value.ContainsKey(player.userID) && storage.Value[player.userID].Any())
                            PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("UnredeemedItemsRemind", player.UserIDString, config.commands.First(), storage.Key));
            });
        }

        private void RedeemCommand(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0)
            {
                foreach (var storage in config.inventories)
                    if (storage.Value.defaultInventory)
                    {
                        OpenRedeemInventory(player, storage.Key);
                        return;
                    }
            }
            else if (args.Length == 1)
            {
                string toLower = args[0].ToLower();
                if (config.inventories.ContainsKey(toLower))
                    OpenRedeemInventory(player, toLower);
                else
                    SendReply(player, Lang("StorageNotFound", player.UserIDString, toLower));
            }
            else if (args.Length == 2)
            {
                if (!permission.UserHasPermission(player.UserIDString, "redeemstorageapi.admin"))
                {
                    SendReply(player, Lang("NoPermission", player.UserIDString));
                    return;
                }
                string toLower = args[0].ToLower();
                if (!config.inventories.ContainsKey(toLower))
                    SendReply(player, Lang("StorageNotFound", player.UserIDString, toLower));
                if (ulong.TryParse(args[1], out ulong userId))
                {
                    SendReply(player, Lang("AdminOpening", player.UserIDString, toLower, userId));
                    OpenRedeemInventory(player, toLower, userId);
                    return;
                }
                string userToLower = args[1].ToLower();
                foreach (var oPlayer in BasePlayer.activePlayerList)
                    if (oPlayer.displayName.ToLower().Contains(userToLower))
                    {
                        SendReply(player, Lang("AdminOpening", player.UserIDString, toLower, oPlayer.userID));
                        OpenRedeemInventory(player, toLower, oPlayer.userID);
                        return;
                    }
                SendReply(player, Lang("UserNotFound", player.UserIDString));
            }
        }

        private void RedeemConsoleCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            switch (arg.Args[0])
            {
                case "withdrawAll":
                    WithdrawItem(player);
                    break;
                case "withdraw":
                    int index = int.Parse(arg.Args[1]);
                    WithdrawItem(player, index);
                    break;
            }
        }

        private void RedeemItemConsoleCommand(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin)
            {
                SendReply(arg, "You are not an admin!");
                return;
            }
            if (arg.Args.Length < 3)
            {
                SendReply(arg, "Usage: createredeemitem <storageName> <userId> <shortname> [amount] [skinId] [itemName] [popup (true/false)]");
                return;
            }
            string name = arg.Args[0];
            if (!config.inventories.ContainsKey(name))
            {
                SendReply(arg, "There is no inventory with that name!");
                return;
            }
            if (!ulong.TryParse(arg.Args[1], out ulong userId))
            {
                SendReply(arg, "Wrong user ID!");
                return;
            }
            Item item = ItemManager.CreateByName(arg.Args[2]);
            if (item == null)
            {
                SendReply(arg, "Wrong item shortname!");
                return;
            }
            if (arg.Args.Length > 3 && int.TryParse(arg.Args[3], out int amount))
                item.amount = amount;
            if (arg.Args.Length > 4 && ulong.TryParse(arg.Args[4], out ulong skinId))
                item.skin = skinId;
            if (arg.Args.Length > 5)
                item.name = arg.Args[5];
            bool popUp = arg.Args.Length <= 6 || !bool.TryParse(arg.Args[6], out popUp) || popUp;
            SendReply(arg, $"Successfully created x{item.amount} item with shortname {item.info.shortname} and skin {item.skin} for {userId}!");
            AddItem(userId, name, item, popUp);
        }

        private void WithdrawItem(BasePlayer player, int index = -1)
        {
            InventoryCache ic = openedInventories[player.userID];
            List<ItemData> items = storedItems[ic.name][ic.owner];
            LootContainer targetStorage = cachedInventories[ic.owner][ic.name];
            if (index == -1)
            {
                targetStorage.inventory.canAcceptItem = (_, _) => true;
                for (int i = items.Count - 1; i >= 0; i--)
                {
                    ItemData storedItem = items[i];
                    Item newItem = storedItem.ToItem();
                    if (newItem.MoveToContainer(targetStorage.inventory))
                        items.RemoveAt(i);
                    else
                        break;
                }
                targetStorage.inventory.canAcceptItem = (_, _) => false;
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("ItemsWithdrawed", player.UserIDString));
            }
            else
            {
                ItemData storedItem = items[index];
                Item newItem = storedItem.ToItem();
                targetStorage.inventory.canAcceptItem = (_, _) => true;
                if (!newItem.MoveToContainer(targetStorage.inventory))
                    newItem.Drop(player.GetDropPosition(), player.GetDropVelocity());
                targetStorage.inventory.canAcceptItem = (_, _) => false;
                PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("ItemWithdrawed", player.UserIDString));
                items.RemoveAt(index);
            }
            ShowRedeemUi(player);
        }

        private void OnPlayerLootEnd(PlayerLoot loot)
        {
            BasePlayer player = loot.GetCastedEntity();
            if (!player || !openedInventories.ContainsKey(player.userID)) return;
            CuiHelper.DestroyUi(player, "RedeemStorageAPI_Inventory");
            openedInventories.Remove(player.userID);
        }

        private void OpenRedeemInventory(BasePlayer player, string name, ulong ownerId = 0)
        {
            if (!permission.UserHasPermission(player.UserIDString, "redeemstorageapi.admin"))
            {
                bool privil = false;
                bool safeZone = player.InSafeZone();
                BuildingPrivlidge priv = player.GetBuildingPrivilege();
                if (priv && priv.IsAuthed(player.userID))
                    privil = true;
                else if (!priv && config.inventories[name].authedNoTc)
                    privil = true;
                int trueCount = 0;
                if (config.inventories[name].authed && privil)
                    trueCount++;
                if (config.inventories[name].safezone && safeZone)
                    trueCount++;
                if (trueCount == 0 && (config.inventories[name].authed || config.inventories[name].safezone))
                {
                    if (config.inventories[name].authed && !privil)
                        PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NotAuthToRefund", player.UserIDString));
                    else if (config.inventories[name].safezone && !safeZone)
                        PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NotInSafeZone", player.UserIDString));
                    return;
                }
            }
            ulong id = ownerId == 0 ? player.userID : ownerId;
            if (!storedItems[name].ContainsKey(id))
            {
                SendReply(player, Lang("StorageEmpty", player.UserIDString));
                return;
            }
            LootContainer targetStorage;
            cachedInventories.TryAdd(player.userID, new());
            bool wasValidInventory = cachedInventories[player.userID].TryGetValue(name, out targetStorage);
            if (storedItems[name][id].Count == 0)
            {
                if (!wasValidInventory || targetStorage.inventory.itemList.Count == 0)
                {
                    SendReply(player, Lang("StorageEmpty", player.UserIDString));
                    return;
                }
            }
            if (!wasValidInventory)
            {
                targetStorage = GameManager.server.CreateEntity("assets/bundled/prefabs/modding/lootables/invisible/invisible_lootable_prefabs/invisible_crate_basic.prefab", new Vector3(0, Core.Random.Range(-200, -180), 0)) as LootContainer;
                targetStorage.OwnerID = id;
                targetStorage.minSecondsBetweenRefresh = 0;
                targetStorage.maxSecondsBetweenRefresh = 0;
                targetStorage.destroyOnEmpty = false;
                targetStorage.initialLootSpawn = false;
                targetStorage.BlockPlayerItemInput = false;
                targetStorage.panelName = "generic_resizable";
                UnityEngine.Object.DestroyImmediate(targetStorage.GetComponent<Spawnable>());
                targetStorage.Spawn();
                targetStorage.inventory.capacity = 24;
                targetStorage.inventory.canAcceptItem = (_, _) => false;
                foreach (var item in targetStorage.inventory.itemList.ToArray())
                {
                    item.GetHeldEntity()?.Kill();
                    item.Remove();
                }
                cachedInventories[player.userID].Add(name, targetStorage);
            }
            openedInventories[player.userID] = new() { name = name, owner = id };
            ShowRedeemUi(player);
            player.EndLooting();
            timer.Once(0.1f, () =>
            {
                player.inventory.loot.AddContainer(targetStorage.inventory);
                player.inventory.loot.entitySource = targetStorage;
                player.inventory.loot.PositionChecks = false;
                player.inventory.loot.MarkDirty();
                player.inventory.loot.SendImmediate();
                player.ClientRPC(RpcTarget.Player("RPC_OpenLootPanel", player), "generic_resizable");
                if (config.inventories[name].message)
                    PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang($"OpenMessage_{name}", player.UserIDString));
            });
        }

        private void AddItem(ulong userId, string name, Item item, bool popUp = false)
        {
            if (!storedItems.ContainsKey(name))
            {
                PrintWarning($"Player {userId} tried to add item to storage named '{name}' but it doesn't exist in configuration file!");
                return;
            }
            storedItems[name].TryAdd(userId, new());
            storedItems[name][userId].Add(ItemToData(item));
            if (popUp)
            {
                BasePlayer player = BasePlayer.FindByID(userId);
                if (player)
                {
                    string itemName = !string.IsNullOrEmpty(item.name) ? item.name : item.info.displayName.english;
                    PopUpAPI?.Call("ShowPopUp", player, config.popUpPreset, Lang("NewItemInStorage", player.UserIDString, name, itemName, config.commands.First()));
                }
            }
        }

        private static readonly char[] amountSuffixes = new[] { 'k', 'm', 'b', 't' };

        private static string FormatNumber(float amount, StringBuilder sb)
        {
            if (amount == 0) return "0";
            if (amount < 1) return amount.ToString("'x'0.###");
            if (amount < 10) return amount.ToString("'x'0.##");
            if (amount < 100) return amount.ToString("'x'0.#");
            if (amount < 1000) return amount.ToString("'x'0");
            int index = -1;
            while (amount >= 1000 && index < amountSuffixes.Length - 1)
            {
                amount /= 1000f;
                index++;
            }
            sb.Clear();
            if (amount < 10)
                sb.Append(amount.ToString("'x'0.##"));
            else if (amount < 100)
                sb.Append(amount.ToString("'x'0.#"));
            else
                sb.Append(amount.ToString("'x'0"));
            sb.Append(amountSuffixes[index]);
            string output = sb.ToString();
            return output;
        }

        private void ShowRedeemUi(BasePlayer player)
        {
            using CUI cui = new CUI(CuiHandler);
            InventoryCache ic = openedInventories[player.userID];
            StringBuilder sb = Pool.Get<StringBuilder>();
            LUI.LuiContainer redeemUi = cui.v2.CreateParent(CUI.ClientPanels.Inventory, LuiPosition.LowerCenter, "RedeemStorageAPI_Inventory");

            //Element: Title
            cui.v2.CreateText(redeemUi, new LuiOffset(193, 618, 572, 650), 20, ColDb.LightGray, Lang("RedeemItems", player.UserIDString), TextAnchor.MiddleLeft);

            //Element: Main Panel
            LUI.LuiContainer mainPanel = cui.v2.CreatePanel(redeemUi, new LuiOffset(192, 418, 572, 618), ColDb.LightGrayTransRust);
            mainPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Store Icon
            cui.v2.CreateSprite(mainPanel, new LuiOffset(24, 159, 47, 182), "assets/icons/open.png", ColDb.LightGray);

            //Element: Stored Items Count
            int storedItemCount = storedItems[ic.name][ic.owner].Count;
            cui.v2.CreateText(mainPanel, new LuiOffset(53, 154, 256, 186), 15, ColDb.LightGray, Lang("StoredItems", player.UserIDString, storedItemCount), TextAnchor.MiddleLeft);

            //Element: Withdraw Button
            LUI.LuiContainer withdrawButton = cui.v2.CreateButton(mainPanel, new LuiOffset(264, 156, 364, 184), "RedeemStorageUI withdrawAll", ColDb.GreenBg);
            withdrawButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

            //Element: Withdraw Button Text
            cui.v2.CreateText(withdrawButton, LuiPosition.Full, LuiOffset.None, 13, ColDb.GreenText, Lang("WithdrawAll", player.UserIDString), TextAnchor.MiddleCenter);

            //Element: Items Scroll
            LuiScrollbar mainPanel_Vertical = new LuiScrollbar()
            {
                autoHide = true,
                size = 4,
                handleColor = ColDb.LTD15,
                highlightColor = ColDb.LTD20,
                pressedColor = ColDb.LTD10,
                trackColor = ColDb.BlackTrans10
            };
            int scrollHeight = 34 * storedItemCount;
            if (scrollHeight < 140)
                scrollHeight = 140;

            LUI.LuiContainer itemsScroll = cui.v2.CreateScrollView(mainPanel, new LuiOffset(8, 8, 374, 148), true, false, ScrollRect.MovementType.Elastic, 0.1f, true, 0.1f, 25f, mainPanel_Vertical);
            itemsScroll.SetScrollContent(new LuiPosition(0, 1, 1, 1), new LuiOffset(0, -scrollHeight, 0, 0));

            //Element: Scrollable Holder
            cui.v2.CreatePanel(itemsScroll, LuiPosition.Full, LuiOffset.None, ColDb.Transparent);

            int counter = 0;
            foreach (var item in storedItems[ic.name][ic.owner])
            {
                scrollHeight -= 34;
                //Element: Withdraw Item Panel
                string command = sb.Clear().Append("RedeemStorageUI withdraw ").Append(counter).ToString();
                LUI.LuiContainer withdrawItemPanel = cui.v2.CreateButton(itemsScroll, new LuiOffset(8, scrollHeight, 356, scrollHeight + 30), command, ColDb.LightGrayTransRust);
                withdrawItemPanel.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: Item Icon
                LUI.LuiContainer itemIcon = cui.v2.CreateItemIcon(withdrawItemPanel, new LuiOffset(8, 3, 32, 27), item.shortname, item.skin, ColDb.WhiteTrans80);
                itemIcon.SetMaterial("assets/content/ui/namefontmaterial.mat");

                //Element: Item Name Amount
                string amount = FormatNumber(item.amount, sb);
                ItemDefinition def = ItemManager.FindItemDefinition(item.shortname);
                string itemName = string.IsNullOrEmpty(item.name) ? def.displayName.english : item.name;
                string itemNameFormat = sb.Clear().Append(amount).Append(' ').Append(itemName).ToString();
                LUI.LuiContainer itemNameAmount = cui.v2.CreateText(withdrawItemPanel, new LuiOffset(37, 0, 213, 30), 13, ColDb.LightGray, itemNameFormat, TextAnchor.MiddleLeft);
                itemNameAmount.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

                float stackSize = (float)item.amount / def.stackable;
                string stackSizeString = Lang("Stacks", player.UserIDString, stackSize < 0.01f ? "< 0.01" : stackSize.ToString("0.##"));
                //Element: Stack Count
                LUI.LuiContainer stackCount = cui.v2.CreateText(withdrawItemPanel, new LuiOffset(174, 0, 342, 30), 11, ColDb.LTD80, stackSizeString, TextAnchor.MiddleRight);
                stackCount.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);
                counter++;
            }
            //Element: Hint Icon
            LUI.LuiContainer hintIcon = cui.v2.CreateSprite(redeemUi, new LuiOffset(550, 392, 566, 408), "assets/icons/info.png", ColDb.LTD60);

            //Element: Hint Text
            LUI.LuiContainer hintText = cui.v2.CreateText(hintIcon, new LuiOffset(-298, -5, -4, 21), 10, ColDb.LTD60, Lang("Hint", player.UserIDString), TextAnchor.MiddleRight);
            hintText.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);
            byte[] bytes = cui.v2.GetUiBytes();
            cui.Destroy("RedeemStorageAPI_Inventory", player);
            cui.v2.SendUiBytes(player, bytes);
            Pool.FreeUnmanaged(ref sb);
        }

        private class ColDb
        {
            public const string Transparent = "1 1 1 0";
            public const string WhiteTrans80 = "1 1 1 0.8";
            public const string BlackTrans10 = "0 0 0 0.102";
            public const string GreenBg = "0.365 0.447 0.224 1";
            public const string GreenText = "0.82 1 0.494 1";
            public const string LTD10 = "0.239 0.224 0.196 1";
            public const string LTD15 = "0.282 0.263 0.235 1";
            public const string LTD20 = "0.325 0.306 0.275 1";
            public const string LTD60 = "0.667 0.635 0.6 1";
            public const string LTD80 = "0.839 0.8 0.761 1";
            public const string LightGray = "0.969 0.922 0.882 1";
            public const string LightGrayTransRust = "0.969 0.922 0.882 0.039";
        }

        private void LoadMessages()
        {
            Dictionary<string, string> langFile = new Dictionary<string, string>()
            {
                ["StorageNotFound"] = "Storage with name <color=#5c81ed>{0}</color> has not been found.",
                ["NoPermission"] = "You don't have permission to use this command!",
                ["AdminOpening"] = "Opening <color=#5c81ed>{0}</color> inventory of <color=#5c81ed>{1}</color>...",
                ["UserNotFound"] = "User has not been found.",
                ["NotAuthToRefund"] = "You are <color=#5c81ed>not authorized in Cupboard</color>. You cannot open this redeem inventory!",
                ["NotInSafeZone"] = "You are <color=#5c81ed>not in safe zone</color>. You cannot open this redeem inventory!",
                ["StorageEmpty"] = "This storage is <color=#5c81ed>empty</color>...",
                ["NewItemInStorage"] = "You've got new item!\nIt's <color=#5c81ed>{1}</color>! You can redeem it by typing <color=#5c81ed>/{2} {0}</color>.",
                ["UnredeemedItemsRemind"] = "You have unredeemed items in your storage!\nRun <color=#5c81ed>/{0} {1}</color> to get your items.",
                ["ItemStorageFullDrop"] = "You've got new item!\nIt's <color=#5c81ed>{1}</color>! It should land in your <color=#5c81ed>{0}</color> storage but it was full, so it dropped on to the ground.",
                ["ItemStorageFullLost"] = "You've got new item!\nIt was <color=#5c81ed>{1}</color>! But your storage <color=#5c81ed>{0}</color> was full and item disappeared!\nClear your storage first.",
                ["RedeemItems"] = "REDEEM INVENTORY ITEMS",
                ["StoredItems"] = "Stored items: {0}",
                ["WithdrawAll"] = "WITHDRAW ALL",
                ["Stacks"] = "{0} Stack",
                ["Hint"] = "Click an item that you want to withdraw.\nYou can't store items here.",
                ["ItemWithdrawed"] = "Item has been withdrawn into your redeem inventory.\nIf it was full it has been dropped onto ground.",
                ["ItemsWithdrawed"] = "Items has been withdrawn into your redeem inventory.\nIf the inventory has got full the withdrawing process has been stopped."
            };
            foreach (var storage in config.inventories)
                if (storage.Value.message)
                    langFile.TryAdd($"OpenMessage_{storage.Key}", "Default Open Message!");
            lang.RegisterMessages(langFile, this);
        }

        private string Lang(string key, string id = null, params object[] args)
        {
            if (args.Length == 0)
                return lang.GetMessage(key, this, id);
            return string.Format(lang.GetMessage(key, this, id), args);
        }

        private static PluginConfig config = new();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            config = Config.ReadObject<PluginConfig>();
            Config.WriteObject(config);
        }

        protected override void LoadDefaultConfig()
        {
            Config.WriteObject(config = new PluginConfig()
            {
                commands = new List<string>() { "redeem", "red" },
                inventories = new Dictionary<string, InventoryConfig>()
                {
                    { "default", new InventoryConfig() { defaultInventory = true, safezone = true, authed = true } },
                    { "shop", new InventoryConfig() { message = true } }
                }
            }, true);
        }

        private class PluginConfig
        {
            [JsonProperty("Redeem Commands")]
            public List<string> commands = new();

            [JsonProperty("PopUp API Preset")]
            public string popUpPreset = "Legacy";

            [JsonProperty("Redeem Storage Item Reminder (in seconds, 0 to disable)")]
            public int itemReminder = 600;

            [JsonProperty("Redeem Inventories")]
            public Dictionary<string, InventoryConfig> inventories = new();
        }

        private class InventoryConfig
        {
            [JsonProperty("Default Redeem Inventory (only one)")]
            public bool defaultInventory = false;

            [JsonProperty("PopUp Message (configurable in lang file)")]
            public bool message = false;

            [JsonProperty("Redeem Only In Safezone")]
            public bool safezone = false;

            [JsonProperty("Redeem Only If Authed")]
            public bool authed = false;

            [JsonProperty("Allow When No Cupboard (works is option above is true)")]
            public bool authedNoTc = false;

            [JsonProperty("Wipe Storage Content On Server Wipe")]
            public bool wipeInventory = false;
        }

        private static readonly Dictionary<string, Dictionary<ulong, List<ItemData>>> storedItems = new();


        private void LoadData()
        {
            foreach (var storage in config.inventories)
            {
                storedItems.TryAdd(storage.Key, new Dictionary<ulong, List<ItemData>>());
                storedItems[storage.Key] = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, List<ItemData>>>($"{Name}/{storage.Key}");
            }
            timer.Every(Core.Random.Range(500, 700), SaveData);
        }

        private void SaveData()
        {
            foreach (var storage in config.inventories)
                Interface.Oxide.DataFileSystem.WriteObject($"{Name}/{storage.Key}", storedItems[storage.Key]);
        }

        private class ItemData
        {
            [JsonProperty(PropertyName = "Shortname")]
            public string shortname; 
            
            [JsonProperty(PropertyName = "Amount")]
            public int amount;

            [JsonProperty(PropertyName = "IsBlueprint", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public bool isBlueprint;

            [JsonProperty(PropertyName = "BlueprintTarget", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public int blueprintTarget;

            [JsonProperty(PropertyName = "Skin", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public ulong skin;

            [JsonProperty(PropertyName = "Fuel", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public float fuel;

            [JsonProperty(PropertyName = "FlameFuel", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public int flameFuel;

            [JsonProperty(PropertyName = "Condition", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public float condition;

            [JsonProperty(PropertyName = "MaxCondition", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public float maxCondition = -1;

            [JsonProperty(PropertyName = "Ammo", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public int ammo;

            [JsonProperty(PropertyName = "AmmoType", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public int ammoType;

            [JsonProperty(PropertyName = "DataInt", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public int dataInt;

            [JsonProperty(PropertyName = "Name", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public string name;

            [JsonProperty(PropertyName = "Text", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public string text;

            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public Item.Flag flags;

            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public int capacity;

            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public List<OwnershipData> ownership;

            [JsonProperty(PropertyName = "Contents", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public List<ItemData> contents;

            public Item ToItem()
            {
                Item item = ItemManager.CreateByName(shortname, amount, skin);
                if (item == null) return null;
                if (!string.IsNullOrEmpty(name))
                    item.name = name;
                item.text = text;
                if (isBlueprint)
                {
                    item.blueprintTarget = blueprintTarget;
                    return item;
                }
                item.fuel = fuel;
                item._condition = condition;
                if (maxCondition != -1)
                    item._maxCondition = maxCondition;
                if (capacity > 0 || contents?.Count > 0)
                {
                    if (item.contents == null)
                    {
                        int capacity = Math.Max(this.capacity, contents.Count);
                        if (HasItemMod(item.info, out ItemModContainerArmorSlot itemMod) && capacity > 0)
                            itemMod.CreateAtCapacity(capacity, item);
                        else
                        {
                            item.contents = new ItemContainer();
                            item.contents.ServerInitialize(item, capacity);
                            item.contents.GiveUID();
                        }
                    }
                    else
                        item.contents.capacity = Math.Max(item.contents.capacity, contents.Count);
                    if (contents != null)
                    {
                        foreach (var contentItem in contents)
                        {
                            Item childItem = contentItem.ToItem();
                            if (childItem == null) continue;
                            if (!childItem.MoveToContainer(item.contents, childItem.position) && !childItem.MoveToContainer(item.contents))
                                childItem.Remove();
                        }
                    }
                }
                if (ownership?.Count > 0 && item.ownershipShares != null)
                {
                    foreach (var ownership in ownership)
                    {
                        item.ownershipShares.Add(new ItemOwnershipShare
                        {
                            username = ownership.Username,
                            reason = ownership.Reason,
                            amount = ownership.Amount,
                        });
                    }
                }
                item.flags |= flags;
                BaseEntity heldEntity = item.GetHeldEntity();
                BaseProjectile baseProjectile = heldEntity as BaseProjectile;
                if (baseProjectile != null)
                {
                    baseProjectile.DelayedModsChanged();
                    BaseProjectile.Magazine magazine = baseProjectile.primaryMagazine;
                    if (magazine != null && ammoType != 0)
                    {
                        magazine.contents = ammo;
                        magazine.ammoType = ItemManager.FindItemDefinition(ammoType) ?? magazine.ammoType;
                    }
                }
                FlameThrower flameThrower = heldEntity as FlameThrower;
                if (flameThrower != null)
                    flameThrower.ammo = flameFuel;
                if (dataInt > 0)
                {
                    item.instanceData = new ProtoBuf.Item.InstanceData
                    {
                        ShouldPool = false,
                        dataInt = dataInt,
                    };
                    Detonator detonator = heldEntity as Detonator;
                    if (detonator != null)
                        detonator.frequency = dataInt;
                }
                return item;
            }
        }

        private static bool HasItemMod<T>(ItemDefinition itemDefinition, out T itemModOfType) where T : ItemMod
        {
            foreach (var itemMod in itemDefinition.itemMods)
            {
                itemModOfType = itemMod as T;
                if (itemModOfType is not null)
                    return true;
            }
            itemModOfType = null;
            return false;
        }

        private class OwnershipData
        {
            public string Username;
            public string Reason;
            public int Amount;

            public OwnershipData Setup(string username, string reason, int amount)
            {
                Username = username;
                Reason = reason;
                Amount = amount;
                return this;
            }
        }

        private ItemData ItemToData(Item item)
        {
            BaseEntity heldEntity = item.GetHeldEntity();
            ItemData itemData = new();
            itemData.shortname = item.info.shortname;
            itemData.amount = item.amount;
            itemData.skin = item.skin;
            itemData.fuel = item.fuel;
            itemData.flameFuel = heldEntity?.GetComponent<FlameThrower>()?.ammo ?? 0;
            itemData.condition = item.condition;
            itemData.maxCondition = item.maxCondition;
            itemData.ammo = heldEntity?.GetComponent<BaseProjectile>()?.primaryMagazine?.contents ?? 0;
            itemData.ammoType = heldEntity?.GetComponent<BaseProjectile>()?.primaryMagazine?.ammoType?.itemid ?? 0;
            itemData.dataInt = item.instanceData?.dataInt ?? 0;
            itemData.name = item.name;
            itemData.text = item.text;
            itemData.flags = item.flags;
            if (item.contents != null)
            {
                itemData.capacity = item.contents.capacity;
                itemData.contents = new();
                itemData.contents.Clear();
                foreach (var childItem in item.contents.itemList)
                {
                    ItemData id = ItemToData(childItem);
                    itemData.contents.Add(id);
                }
            }
            if (item.ownershipShares?.Count > 0)
            {
                itemData.ownership = new();
                foreach (var ownership in item.ownershipShares)
                {
                    OwnershipData od = new();
                    itemData.ownership.Add(od.Setup(ownership.username, ownership.reason,
                        ownership.amount));
                }
            }

            return itemData;
        }
    }
} 