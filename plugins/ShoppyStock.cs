using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Facepunch;
using Network;
using UnityEngine;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;

#if CARBON
using Carbon;
using Carbon.Base;
using Carbon.Components;
using Carbon.Modules;
using Carbon.Plugins;
#else
using Oxide.Ext.CarbonAliases;
#endif

namespace Oxide.Plugins
{
    [Info("ShoppyStock", "ThePitereq", "2.1.3")]
#if CARBON
    public class ShoppyStock : CarbonPlugin
#else
    public class ShoppyStock : RustPlugin
#endif
    {
	    #region Fields
	    
	    [PluginReference] private readonly Plugin PopUpAPI, RedeemStorageAPI, DiscordCore, NoEscape, Economics, ServerRewards, BankSystem, IQEconomic, ItemCostCalculator, Artifacts, BonusCore, CustomItemDefinitions;
	    
#if !CARBON
	    public static CUI.Handler CuiHandler = new();
#endif
	    
	    private static readonly Dictionary<ulong, ShoppyCache> cache = new();
	    private static readonly Dictionary<string, StockCategoryCache> stockItems = new();
	    private static readonly Dictionary<string, Timer> timers = new();
	    private static readonly HashSet<int> cachedShareCodes = new();
	    private static readonly Dictionary<string, string> cachedLeaderboards = new();
	    private static DateTime lastLeaderboardCheck = DateTime.MinValue;
	    private static readonly Dictionary<string, List<SaleCache>> saleCache = new();
	    
	    private static readonly Dictionary<string, int> ownershipShortnames = new();
	    private static readonly Dictionary<string, int> ownershipDlcs = new();
	    
	    //private static readonly Dictionary<string, string> shortnameRedirect = new();
	    private static readonly Dictionary<string, int> iconRedirect = new();
	    private static readonly Dictionary<string, ulong> skinRedirect = new();
	    private bool validDataLoad = false;
	    private bool validImageLoad = false;
	    private bool shouldWipe = false;

	    private class StockCategoryCache
	    {
		    public Dictionary<string, Dictionary<string, List<ulong>>> categories = new();
		    public Dictionary<string, int> itemCount = new();
	    }

	    private class SaleCache
	    {
		    public DateTime endTime;
		    public Dictionary<string, float> categories = new();
		    public Dictionary<string, float> items = new();
	    }

	    private static class ConfigEditorCache
	    {
		    public static bool edited = false;
		    public static bool isBeingEdited = false;
		    public static PluginConfig config = null;
		    public static Dictionary<string, ShopDataConfig> shopDatas = new();
		    public static Dictionary<string, StockDataConfig> stockDatas = new();
		    public static List<string> enterTree = new();
		    public static object cachedKey = null;
		    public static object cachedValue = null;
	    }

	    private static readonly Dictionary<string, List<string>> customMethodButtons = new()
	    {
		    { "CurrencyConfig", new() { "AddCurrencyItem" } },
		    { "ShopConfig", new() { "OpenShopData" } },
		    { "StockConfig", new() { "OpenStockData" } },
		    { "ShopCategoryConfig", new() { "AddListingItem" } },
		    { "StockDataConfig", new() { "AddCustomItem", "AddSellItem" } },
	    };
	    
	    public static string dateString = "";

	    private static readonly List<string> greenCategories = new()
	    {
		    StringKeys.Cat_Favourites,
		    StringKeys.Cat_MyListings,
		    StringKeys.Cat_Bank,
		    StringKeys.Cat_AllItems,
	    };

	    private static class StringKeys
	    {
		    public const string Cat_Favourites = "Favourites";
		    public const string Cat_MyListings = "MyListings";
		    public const string Cat_Bank = "Bank";
		    public const string Cat_AllItems = "AllItems";
		    public const string Effect_Money = "assets/prefabs/misc/casino/slotmachine/effects/payout.prefab";
		    public const string Effect_Collect = "assets/prefabs/misc/easter/painted eggs/effects/eggpickup.prefab";
		    public const string Effect_Info = "assets/bundled/prefabs/fx/invite_notice.prefab";
		    public const string Effect_Denied = "assets/prefabs/locks/keypad/effects/lock.code.denied.prefab";
	    }

	    private class ShoppyCache
	    {
		    public string shopName = "";
		    public ShoppyType type;
		    public bool isOpened = true;
		    public DateTime lastAction = DateTime.MinValue;
		    public string npcId = "";
		    public ShopCache shopCache = new();
		    public StockCache stockCache = new();
		    public TransferCache transferCache = new();
		    public DepositCache depositCache = new();
		    public ExchangeCache exchangeCache = new();
	    }

	    private class ShopCache
	    {
		    public string category = "";
		    public string listingKey = "";
		    public string search = "";
		    public bool showOnlyAffordable = false;
		    public ShopSortType sortType = 0;
		    public bool showSort = false;
		    public int amount = 1;
		    public int maxAmount = 1;
		    public float fixedPrice;
		    public float summedPrice;
	    }

	    private class StockCache
	    {
		    
		    public string category = "";
		    public string search = "";
		    public StockSortType sortType = 0;
		    public bool hideEmpty = false;
		    public string shortname = "";
		    public ulong skin = 0;
		    public ListingPageType listingPage = 0;
		    public int currentTimespan = 0;
		    public int stockActionAmount = 0;
		    public int selectedListingIndex = 0;
		    public ulong selectedListingOwner = 0;
		    public bool hideMyListings = false;
		    public int listingCode = 0;
		    public QueuedStockAction queuedAction = QueuedStockAction.None;
		    public bool isWatchingHistory = false;

		    public string bankShortname = "";
		    public ulong bankSkin = 0;
		    public int bankAmount = 0;
		    
		    public ContainerType containerType = 0;
		    public LootContainer container = null;
		    public bool haveOpenContainer = false;
		    public Action savedAction = null;
		    public int buyOfferTaxIndex = 0;
		    public int sellOfferTaxIndex = 0;
		    public float offerPrice = 0;
		    public bool broadcastNewListing = true;
		    public List<StockActionType> disabledStockMessages = new();
	    }
	    
	    private class TransferCache
	    {
		    public string search = "";
		    public bool showOffline = false;
		    public ulong selectedPlayer = 0;
		    public string selectedCurrency = "";
		    public float transferAmount = 0;

	    }
	    
	    private class DepositCache
	    {
		    public string lastInputShortname = "";
		    public ulong lastInputSkin = 0;
		    public int inputAmount = 0;
	    }
	    
	    private class ExchangeCache
	    {
		    public int currentId = -1;
		    public int amount = 0;
	    }

	    private enum ShoppyType
	    {
		    Unassigned,
		    Shop,
		    Stock,
		    Transfer,
		    Exchange,
		    Deposit
	    }

	    private enum QueuedStockAction
	    {
		    None,
		    OfferVisibilityToggle,
		    RemoveOffer
	    }

	    private enum ShopSortType
	    {
		    Default,
		    Alphabetical,
		    Popularity,
		    Cheapest
	    }

	    private enum StockSortType
	    {
		    Name,
		    ServerSell,
		    BuyOffer,
		    SellOffer
	    }

	    private enum ContainerType
	    {
		    BuyOffer,
		    SellOffer,
		    SellInventory,
		    BankInventory,
		    CurrencyDeposit
	    }

	    private enum ListingPageType
	    {
		    Unassigned,
		    ServerSell,
		    BuyOffer,
		    SellOffer
	    }

	    public ImageDatabaseModule imgDb = BaseModule.GetModule<ImageDatabaseModule>();
	    
	    #endregion
	    
	    #region Load/Unload

        private void Init()
        {
            LoadConfig();
            LoadData();
            SaveData();
            SaveShopDataConfig();
            SaveStockDataConfig();
        }
 
        private void OnServerInitialized()
        {
	        CheckForWipe();
	        DoCleanup();
	        GetStockItemLists();
	        CheckForMissingPrices();
	        LoadMessages();
	        CheckConfigLang();
	        TryGenerateDataConfigs();
	        LoadPermissions();
	        LoadCommands(); 
	        LoadCustomImages();
	        OverrideUiColors();
	        GeneratePopUpConfig();
	        CheckForDate();
	        InitialCheckForOfferRoll();
	        StartCheckingForStockPriceRoll();
	        StartCheckingForStockOfferEnds();
	        CacheShareCodes();
	        StartCheckingForShopSales();
	        FixData();
	        FindDlcDefinitions();
	        CreateIconRedirects();
	        ResetEditorState();
        }

        private void Unload()
        {
            SaveData();
            foreach (var player in BasePlayer.activePlayerList)
            {
	            CuiHelper.DestroyUi(player, "ShoppyStockUI");
	            CuiHelper.DestroyUi(player, "ShoppyStockUI_Inventory");
	            CuiHelper.DestroyUi(player, "ShoppyAdminUI");
            }
            StopTimers();
            ClearBoxes();
        }

        private void OnNewSave()
        {
	        LogToConsole("MapWipeFound");
	        if (config.wipe.mapWipe)
		        shouldWipe = true;
        }
        
        #endregion
        
        #region Starting Methods

        private void CheckForWipe()
        {
	        if (config.wipe.protocol)
	        {
		        if (data.currencies.savedProtocol == -1)
			        data.currencies.savedProtocol = Rust.Protocol.save;
		        if (Rust.Protocol.save != data.currencies.savedProtocol)
		        {
			        LogToConsole("GameProtocolDifferentWipe", Rust.Protocol.save, data.currencies.savedProtocol);
			        data.currencies.savedProtocol = Rust.Protocol.save;
			        shouldWipe = true;
		        }
	        }
	        if (config.wipe.mapName && ConVar.Server.levelurl.Length > 0)
	        {
		        string mapName = ConVar.Server.levelurl.Split('/').Last();
		        if (data.currencies.lastMapName.Length == 0)
			        data.currencies.lastMapName = mapName;
		        if (mapName != data.currencies.lastMapName)
		        {
			        LogToConsole("MapNameDifferentWipe", mapName, data.currencies.lastMapName);
			        data.currencies.lastMapName = mapName;
			        shouldWipe = true;
		        }
	        }

	        if (shouldWipe && config.wipe.onlyThursday && DateTime.Now.Day < 8 &&
	            DateTime.Now.DayOfWeek != DayOfWeek.Thursday)
	        {
		        LogToConsole("PreventingWipeNotThursday");
		        shouldWipe = false;
	        }
	        if (config.wipe.statsWipeTime > 0)
	        {
		        DateTime now = DateTime.Now;
		        foreach (var stock in data.stock.Values)
		        {
			        foreach (var player in stock.stats.playerDailyEarnings.Values)
			        foreach (var day in player.ToArray())
			        {
				        DateTime date = DateTime.Parse(day.Key);
				        if ((now - date).TotalDays > config.wipe.statsWipeTime)
					        player.Remove(day.Key);
			        }
			        foreach (var player in stock.stats.playerDailyPurchasedItems.Values)
			        foreach (var day in player.ToArray())
			        {
				        DateTime date = DateTime.Parse(day.Key);
				        if ((now - date).TotalDays > config.wipe.statsWipeTime)
					        player.Remove(day.Key);
			        }
			        foreach (var player in stock.stats.playerDailySoldItems.Values)
			        foreach (var day in player.ToArray())
			        {
				        DateTime date = DateTime.Parse(day.Key);
				        if ((now - date).TotalDays > config.wipe.statsWipeTime)
					        player.Remove(day.Key);
			        }
		        }
	        }
	        if (!shouldWipe) return;
	        data.currencies.lastWipeDate = DateTime.Now;
	        LogToConsole("WipeValid");
	        foreach (var shop in data.shop.Values)
	        {
		        shop.stats.playerDailyPurchases.Clear();
		        shop.stats.playerWipePurchases.Clear();
		        shop.stats.playerLastPurchases.Clear();
		        shop.stats.rolledOffers.Clear();
	        }
	        foreach (var stock in data.stock)
	        {
		        StockConfig sc = config.curr[stock.Key].stockCfg;
		        if (!sc.enabled) continue;
		        if (sc.wipeBuyOffers)
			        foreach (var shortname in stock.Value.listings.buyOffers.Values)
				        foreach (var skin in shortname.Values)
					        foreach (var listing in skin.ToArray())
						        if (listing.listingEndTime != DateTime.MinValue)
						        {
							        AddCurrency(stock.Key, listing.userId, listing.price);
							        skin.Remove(listing);
						        }
		        if (sc.wipeSellOffers)
			        foreach (var shortname in stock.Value.listings.sellOffers.Values)
				        foreach (var skin in shortname.Values)
					        foreach (var listing in skin.ToArray())
						        if (listing.listingEndTime != DateTime.MinValue)
							        skin.Remove(listing);
		        stock.Value.stats.actionCounter = 0;
		        stock.Value.stats.actions.Clear();
		        if (sc.wipeBank)
			        foreach (var player in stock.Value.playerData.players.Values)
				        player.bankItems.Clear();
	        }
	        foreach (var curr in config.curr)
	        {
		        if (curr.Value.currCfg.currencyPercentageTookOnWipe > 0)
		        {
			        float percentage = 1 - curr.Value.currCfg.currencyPercentageTookOnWipe / 100f;
			        foreach (var player in data.currencies.players.Values)
				        player.currencies[curr.Key] *= percentage;
		        }
		        else if (curr.Value.currCfg.wipeCurrency)
			        foreach (var player in data.currencies.players.Values)
				        player.currencies[curr.Key] = 0;
	        }
        }
        
        private void DoCleanup()
        {
	        cache.Clear();
	        stockItems.Clear();
	        timers.Clear();
	        cachedShareCodes.Clear();
	        cachedLeaderboards.Clear();
	        saleCache.Clear();
	        lastLeaderboardCheck = DateTime.MinValue;
        }

        private static readonly Dictionary<string, string> consoleMessages = new()
        {
	        { "MapWipeFound", "Found new map wipe!" },
	        { "GameProtocolDifferentWipe", "Game protocol '{0}' is different than previously saved '{1}'. Trying to wipe data!" },
	        { "MapNameDifferentWipe", "Map name '{0}' is different than previously saved '{1}'. Trying to wipe data!" },
	        { "PreventingWipeNotThursday", "Preventing data wipe as it's not first thursday of the month." },
	        { "WipeValid", "Wipe valid. Cleaning data..." },
	        { "LoadingIcons", "Starting to upload {0} custom icons." },
	        { "AddedUiIcons", "Finished uploading {0} custom icons." },
	        { "StartedShopSale", "Started shop sale in shop {0} and category {1}. Sale will end {2}." },
	        { "StoppedShopSale", "Shop sale in shop {0} has ended." },
	        { "PlayerPurchasedFromShop", "Player {0} [{1}] purchased {3} of {2} for {4}." },
	        { "StartHoursSet", "Stock price update hours set successfully!" },
	        { "ItemReturnedFromShop", "Listing in {4} of {0} that contained {1} of {2} with skin {3} has finished listing returned to redeem inventory." },
	        { "MoneyReturnedFromShop", "Listing in {5} of {0} that contained {1} of {2} with skin {3} has finished listing and the buy request money ({4}) has returned to player balance." },
	        { "NewShopCategoryOffersRolled", "New offers has been rolled in {0} shop in {1} category." },
	        { "UpdatdStockPrices", "Prices in {0} stock has been updated successfully!" },
	        { "AddedNewListing", "Player {0} [{1}] has added new listing (is buy offer: {2}) of {5} {3} with skin {4} (name: {6}) to {7} stock." },
	        { "BuyOfferCompleted", "Player {0} [{1}] has completed buy offer of {4} {5} from {3} for {6} in {2} stock." },
	        { "SellOfferCompleted", "Player {0} [{1}] has completed sell offer of {4} {5} from {3} for {6} in {2} stock." },
	        { "ItemAddedToBank", "Player {0} [{1}] added {2} of {3} with skin {4} to stock bank." },
	        { "WithdrawedFromBank", "Player {0} [{1}] withdrawed {2} of {3} from stock bank." },
	        { "SoldResourcesToSerer", "Player {0} [{1}] sold resources in {2} stock for {3}." },
	        { "CurrencyTransfered", "Player {0} [{1}] transfered {3} currency to {2}." },
	        { "CurrencyWithdrawed", "Player {0} [{1}] withdrawed {2} currency." },
	        { "CurrencyDeposited", "Player {0} [{1}] deposited {2} currency." },
	        { "CurrencyExchanged", "Player {0} [{1}] exchanged {2}{3} to {4} currency." },
	        { "PlayerPurchasedFromStock", "Player {0} [{1}] purchased {2} of {3} from stock for {4}." },
	        { "WebApiUpdated", "Web API of {0} stock has been updated successfully!" },
	        { "PlayerCommand", "Player {0} [{1}] has ran ShoppyStock chat command: {2} {3}" },
	        { "PlayerConsoleCommand", "Player {0} [{1}] has ran ShoppyStock console command with args: {2}" },
	        { "NewShopConfigGenerated", "New config of '{0}' shop has been generated in /data/ShoppyStock/Shops/Configs/{0}.json!" },
	        { "NewStockConfigGenerated", "New config of '{0}' stock market has been generated in /data/ShoppyStock/StockMarkets/Configs/{0}.json!" },
	        { "DataAndLogsSaved", "ShoppyStock data and logs has been saved successfully!" },
	        { "DataSaved", "ShoppyStock data has been saved successfully!" },
	        { "PlayerNpcOpen", "Player {0} [{1}] opened ShoppyStock through NPC with ID {2}." },
	        { "RemovedOffer", "Player {0} [{1}] removed offer of x{2} item of id {3} with skin {4} for {5}/each. (Was buy offer: {6})" },
        };

        private void CheckConfigLang()
        {
	        bool added = false;
	        foreach (var mess in consoleMessages)
		        if (!config.consoleLogs.ContainsKey(mess.Key))
		        {
			        config.consoleLogs.Add(mess.Key, mess.Value);
			        added = true;
		        }
	        if (added)
		        SaveConfig();
        }

        private void TryGenerateDataConfigs()
        {
	        foreach (var shop in data.shop)
	        {
		        string key = shop.Key;
		        ShopDataConfig sdc = shop.Value.cfg;
		        if (!sdc.generate) continue;
		        GenerateShopConfig(key, sdc);
	        }
	        foreach (var stock in data.stock)
	        {
		        string key = stock.Key;
		        StockDataConfig sdc = stock.Value.cfg;
		        if (!sdc.generate) continue;
		        GenerateStockConfig(key, sdc);
	        }
        }

        private const string permPrefix = "shoppystock.";

        private void LoadPermissions()
        {
	        TryRegisterPerm("shoppystock.admin");
	        foreach (var curr in config.curr.Values)
	        {
		        TryRegisterPerm(curr.shopCfg.perm.ToLower());
		        foreach (var perm in curr.shopCfg.discountPerms.Keys)
			        TryRegisterPerm(perm.ToLower());
		        TryRegisterPerm(curr.stockCfg.perm.ToLower());
		        TryRegisterPerm(curr.stockCfg.categoriesFavouritePerm.ToLower());
		        TryRegisterPerm(curr.stockCfg.categoriesBankPerm.ToLower());
		        foreach (var perm in curr.stockCfg.bankPermLimits.Keys)
			        TryRegisterPerm(perm.ToLower());
		        foreach (var listing in curr.stockCfg.buyOfferTimes)
		        {
			        TryRegisterPerm(listing.reqPerm.ToLower());
			        foreach (var perm in listing.taxAmountPerms.Keys)
				        TryRegisterPerm(perm.ToLower());
		        }

		        foreach (var listing in curr.stockCfg.sellOfferTimes)
		        {
			        TryRegisterPerm(listing.reqPerm.ToLower());
			        foreach (var perm in listing.taxAmountPerms.Keys)
				        TryRegisterPerm(perm.ToLower());
		        }
		        foreach (var perm in curr.stockCfg.listingLimitPerms.Keys)
			        TryRegisterPerm(perm.ToLower());
		        TryRegisterPerm(curr.stockCfg.sellCfg.bankAutoSellPerm.ToLower());
		        TryRegisterPerm(curr.stockCfg.sellCfg.priceAlertPerm.ToLower());
		        TryRegisterPerm(curr.stockCfg.sellCfg.buyFromServerPerm.ToLower());
		        TryRegisterPerm(curr.stockCfg.sellCfg.priceChartPerm.ToLower());
		        foreach (var perm in curr.stockCfg.sellCfg.sellPriceMultipliers.Keys)
			        TryRegisterPerm(perm.ToLower());
		        foreach (var perm in curr.stockCfg.sellCfg.priceChartTimespanPerms.Values)
			        TryRegisterPerm(perm.ToLower());
		        TryRegisterPerm(curr.stockCfg.marketLog.tabReqPerm.ToLower());
		        foreach (var perm in curr.transferCfg.dailyTransferLimitPerms.Keys)
			        TryRegisterPerm(perm.ToLower());
		        foreach (var perm in curr.transferCfg.wipeTransferLimitPerms.Keys)
			        TryRegisterPerm(perm.ToLower());
		        foreach (var perm in curr.transferCfg.taxPerms.Keys)
			        TryRegisterPerm(perm.ToLower());
		        foreach (var exchg in curr.exchangeCfg.exchanges)
			        TryRegisterPerm(exchg.perm.ToLower());
	        }
	        foreach (var curr in config.curr)
	        {
		        if (curr.Value.shopCfg.enabled)
		        {
			        foreach (var category in data.shop[curr.Key].cfg.categories.Values)
			        {
				        TryRegisterPerm(category.displayPerm.ToLower());
				        foreach (var perm in category.discountPerms.Keys)
							TryRegisterPerm(perm.ToLower());
				        foreach (var perm in category.displayBlacklistPerms)
					        TryRegisterPerm(perm.ToLower());
				        foreach (var listing in category.listings.Values)
				        {
					        foreach (var perm in listing.discounts.Keys)
						        TryRegisterPerm(perm.ToLower());
					        TryRegisterPerm(listing.permission.ToLower());
					        TryRegisterPerm(listing.blacklistPermission.ToLower());
					        foreach (var perm in listing.dailyBuyPerms.Keys)
						        TryRegisterPerm(perm.ToLower());
					        foreach (var perm in listing.wipeBuyPerms.Keys)
						        TryRegisterPerm(perm.ToLower());
					        foreach (var perm in listing.cooldownPerms.Keys)
						        TryRegisterPerm(perm.ToLower());
				        }
			        }
		        }
	        }
        }

        private void TryRegisterPerm(string key)
        {
	        if (string.IsNullOrEmpty(key)) return;
	        if (key.StartsWith(permPrefix) && !permission.PermissionExists(key))
		        permission.RegisterPermission(key, this);
        }

        private void LoadCommands()
        {
	        AddCommand(config.cmd.currCmd, this, nameof(CurrencyManagementCommand));
#if !CARBON
	        cmd.AddConsoleCommand(config.cmd.currCmd, this, nameof(CurrencyManagementCommandOxide));
#endif
	        foreach (var command in config.cmd.mainUi)
		        AddCommand(command, this, nameof(OpenUiCommand));
	        foreach (var command in config.cmd.adminUi)
		        AddCommand(command, this, nameof(OpenUiCommand));
	        foreach (var command in config.cmd.deposit)
		        AddCommand(command, this, nameof(OpenUiCommand));
	        foreach (var command in config.cmd.shops.Keys)
		        AddCommand(command, this, nameof(OpenUiCommand));
	        foreach (var command in config.cmd.markets.Keys)
		        AddCommand(command, this, nameof(OpenUiCommand));
	        foreach (var command in config.cmd.sellInv.Keys)
		        AddCommand(command, this, nameof(OpenUiCommand));
	        foreach (var command in config.cmd.newListing.Keys)
		        AddCommand(command, this, nameof(OpenUiCommand));
	        foreach (var command in config.cmd.bankInv.Keys)
		        AddCommand(command, this, nameof(OpenUiCommand));
	        foreach (var command in config.cmd.quickAccess)
		        AddCommand(command, this, nameof(OpenUiCommand));
#if CARBON
	        cmd.AddConsoleCommand(Community.Protect("ShoppyStock_UI"), this, nameof(ShoppyStockConsoleCommand), @protected: true);
	        cmd.AddConsoleCommand(Community.Protect("ShoppyAdmin_UI"), this, nameof(ShoppyStockAdminConsoleCommand), @protected: true);
#else
	        cmd.AddConsoleCommand("ShoppyStock_UI", this, nameof(ShoppyStockConsoleCommand));
	        cmd.AddConsoleCommand("ShoppyAdmin_UI", this, nameof(ShoppyStockAdminConsoleCommand));
#endif
	        cmd.AddConsoleCommand("updateprices", this, nameof(UpdatePricesConsoleCommand));
        }

        private void AddCommand(string command, Plugin plugin, string method)
        {
#if CARBON
		    cmd.AddCovalenceCommand(command, plugin, method);
#else
	        cmd.AddChatCommand(command, plugin, method);
#endif
        }

        private void LoadCustomImages()
        {
	        List<string> urls = Pool.Get<List<string>>();
	        urls.Clear();
			if (config.ui.icons.trophyUrl.StartsWith("http"))
				urls.Add(config.ui.icons.trophyUrl);
			if (config.ui.icons.boxesUrl.StartsWith("http"))
				urls.Add(config.ui.icons.boxesUrl);
	        foreach (var currency in config.curr)
	        {
		        string currKey = currency.Key;
		        CurrConfig curr = currency.Value;
		        if (curr.currCfg.icon.StartsWith("http"))
			        urls.Add(curr.currCfg.icon);
		        if (curr.shopCfg.enabled)
		        {
			        foreach (var category in data.shop[currKey].cfg.categories.Values)
			        {
				        if (category.icon.StartsWith("http"))
					        urls.Add(category.icon);
				        foreach (var listing in category.listings.Values)
					        if (listing.iconUrl.StartsWith("http"))
						        urls.Add(listing.iconUrl);
			        }
		        }
		        if (curr.stockCfg.enabled)
			        foreach (var icon in curr.stockCfg.categoriesIcons.Values)
				        if (icon.StartsWith("http"))
					        urls.Add(icon);
	        }
	        if (urls.Count == 0)
	        {
		        validImageLoad = true;
		        Pool.FreeUnmanaged(ref urls);
		        return;
	        }
			else
		        LogToConsole("LoadingIcons", urls.Count);
	        imgDb.QueueBatch(true, _ =>
	        {
		        LogToConsole("AddedUiIcons", urls.Count);
		        Pool.FreeUnmanaged(ref urls);
		        validImageLoad = true;
	        }, urls);
        }
        
        private void GeneratePopUpConfig()
        {
	        JObject popUpConfig = new JObject()
	        {
		        { "key", "MarketV2" },
		        { "anchor", "0.5 1" },
		        { "name", "Legacy" },
		        { "parent", "Overall" },
		        { "background_enabled", true },
		        { "background_color", ColDb.DarkGray },
		        { "background_fadeIn", 0.5f },
		        { "background_fadeOut", 0.5f },
		        { "background_offsetMax", "180 0" },
		        { "background_offsetMin", "-180 -70" },
		        { "background_smooth", false },
		        { "background_url", "" },
		        { "background_additionalObjectCount", 1 },
		        { "background_detail_0_color", ColDb.LTD5 },
		        { "background_detail_0_offsetMax", "354 70" },
		        { "background_detail_0_offsetMin","6 6" },
		        { "background_detail_0_smooth", false },
		        { "background_detail_0_url", "" },
		        { "text_anchor", "MiddleCenter" },
		        { "text_color", ColDb.LightGray },
		        { "text_fadeIn", 0.5f },
		        { "text_fadeOut", 0.5f },
		        { "text_font", "RobotoCondensed-Bold.ttf" },
		        { "text_offsetMax", "170 0" },
		        { "text_offsetMin", "-170 -64" },
		        { "text_outlineColor", "0 0 0 0" },
		        { "text_outlineSize", "0 0" }
	        };
	        PopUpAPI?.Call("AddNewPopUpSchema", Name, popUpConfig);
        }

        private void CheckForDate()
        {
	        dateString = DateTime.Now.ToString("dd/MM/yyyy");
	        timer.Every(300f, () => dateString = DateTime.Now.ToString("dd/MM/yyyy"));
        }

        private void InitialCheckForOfferRoll()
        {
	        bool startTimer = false;
	        foreach (var curr in config.curr)
	        {
		        if (!curr.Value.shopCfg.enabled) continue;
		        foreach (var category in data.shop[curr.Key].cfg.categories)
		        {
			        if (category.Value.rollInterval > 0)
			        {
				        startTimer = true;
				        break;
			        }
		        }
	        }

	        if (startTimer)
	        {
		        CheckForOfferRoll();
		        timer.Every(60f, () => CheckForOfferRoll());
	        }
        }
        
        
        private void StartCheckingForStockPriceRoll()
        {                
	        bool setTime = false;
	        foreach (var curr in config.curr.Values)
	        {
		        if (!curr.stockCfg.enabled) continue;
		        if (curr.stockCfg.sellCfg.priceUpdateCertainMinutes.Count > 0)
		        {
			        setTime = true;
			        break;
		        }
	        }
	        CheckForStockHourTimer();
	        if (setTime)
		        timers.Add("hourSelect", timer.Every(50f, () => CheckForStockHourTimer()));
        }
        
        private void StartCheckingForStockOfferEnds()
        {                
	        bool startTimer = false;
	        foreach (var stock in data.stock.Values)
	        {
		        foreach (var shortname in stock.listings.sellOffers.Values)
		        foreach (var skin in shortname.Values)
		        foreach (var listing in skin)
			        if (listing.listingEndTime != DateTime.MaxValue && listing.listingEndTime != DateTime.MinValue)
			        {
				        startTimer = true;
				        goto EndForeach;
			        }
		        foreach (var shortname in stock.listings.buyOffers.Values)
		        foreach (var skin in shortname.Values)
		        foreach (var listing in skin)
			        if (listing.listingEndTime != DateTime.MaxValue && listing.listingEndTime != DateTime.MinValue)
			        {
				        startTimer = true;
				        goto EndForeach;
			        }
	        }
	        EndForeach:
	        if (startTimer && !timers.ContainsKey("offerEnd"))
		        timers.Add("offerEnd", timer.Every(1f, () => CheckForOfferEnds()));
	        else if (!startTimer && timers.ContainsKey("offerEnd"))
	        {
		        timers["offerEnd"].Destroy();
		        timers.Remove("offerEnd");
	        }
        }

        private void CacheShareCodes()
        {
	        foreach (var stock in data.stock.Values)
	        {
		        foreach (var shortname in stock.listings.buyOffers.Values)
			        foreach (var skin in shortname)
				        foreach (var listing in skin.Value)
					        if (listing.customAccessCode > 0)
						        cachedShareCodes.Add(listing.customAccessCode);
		        foreach (var shortname in stock.listings.sellOffers.Values)
					foreach (var skin in shortname)
						foreach (var listing in skin.Value)
							if (listing.customAccessCode > 0)
								cachedShareCodes.Add(listing.customAccessCode);
	        }
        }

        private void StartCheckingForShopSales()
        {
	        foreach (var curr in config.curr)
	        {
		        if (!curr.Value.shopCfg.enabled) continue;
		        if (curr.Value.shopCfg.sales.Count == 0) continue;
		        if (curr.Value.shopCfg.saleInterval == 0) continue;
		        timer.Every(curr.Value.shopCfg.saleInterval * 60, () => TrySetShopSales(curr.Key));
	        }
        }

        private void TrySetShopSales(string shopKey)
        {
	        ShopConfig shopCfg = config.curr[shopKey].shopCfg;
	        if (Core.Random.Range(0f, 100f) > shopCfg.saleAppearChance) return;
	        int sumWeight = 0;
	        foreach (var sale in shopCfg.sales.Values)
		        sumWeight += sale.weight;
	        
	        int rolledWeight = Core.Random.Range(0, sumWeight);
	        sumWeight = 0;
	        foreach (var saleKv in shopCfg.sales)
	        {
		        SaleConfig sale = saleKv.Value;
		        sumWeight += sale.weight;
		        if (sumWeight < rolledWeight) continue;
		        saleCache.TryAdd(shopKey, new());
		        SaleCache sc = new();
		        sc.endTime = DateTime.Now.AddMinutes(Core.Random.Range(sale.minLength, sale.maxLength));
		        float saleValue = 0;
		        if (sale.sameSaleValue)
			        saleValue =  1 - Core.Random.Range(sale.minSale, sale.maxSale) / 100f;
		        foreach (var category in sale.categories)
		        {
			        if (sale.sameSaleValue)
						sc.categories.Add(category, saleValue);
			        else
				        sc.categories.Add(category, 1 - Core.Random.Range(sale.minSale, sale.maxSale) / 100f);
		        }
		        foreach (var itemKey in sale.items)
		        {
			        if (sale.sameSaleValue)
				        sc.items.Add(itemKey, saleValue);
			        else
				        sc.items.Add(itemKey, 1 - Core.Random.Range(sale.minSale, sale.maxSale) / 100f);
		        }
		        LogToConsole("StartedShopSale", shopKey, saleKv.Key, sc.endTime);
		        saleCache[shopKey].Add(sc);
		        if (sale.saleMessLangKey.Length > 0)
			        foreach (var player in BasePlayer.activePlayerList)
				        Mess(player, sale.saleMessLangKey);
		        CheckForShopSalesEndTimer();
	        }
        }

        private void CheckForShopSalesEndTimer()
        {
	        int running = 0;
	        foreach (var curr in saleCache.Values)
		        running += curr.Count;
	        if (running == 0 && timers.ContainsKey("saleEndCheck"))
	        {
		        timers["saleEndCheck"].Destroy();
		        timers.Remove("saleEndCheck");
		        return;
	        }
	        if (running > 0 && !timers.ContainsKey("saleEndCheck"))
	        {
		        timers["saleEndCheck"] = timer.Every(60f, () =>
		        {
			        DateTime now = DateTime.Now;
			        bool anyChanged = false;
			        foreach (var curr in saleCache)
			        {
				        bool changed = false;
				        foreach (var sale in curr.Value.ToArray())
				        {
					        if (sale.endTime < now)
					        {
						        LogToConsole("StoppedShopSale", curr.Key);
						        changed = true;
						        curr.Value.Remove(sale);
					        }
				        }
				        if (changed)
				        {
					        anyChanged = true;
					        TryUpdateShopUI(curr.Key);
				        }
			        }
			        if (anyChanged)
				        CheckForShopSalesEndTimer();
		        });
	        }
        }

        private void GetStockItemLists()
        {
	        stockItems.Clear();
	        foreach (var curr in config.curr)
	        {
		        if (!curr.Value.stockCfg.enabled) continue;
		        string currKey = curr.Key;
		        stockItems.Add(currKey, new());
		        var stockCategories = stockItems[currKey];
		        foreach (var cat in curr.Value.stockCfg.categoriesCustomOrder)
			        stockCategories.categories.Add(cat, new());
		        if (curr.Value.stockCfg.categoriesMyListings)
			        stockCategories.categories.TryAdd(StringKeys.Cat_MyListings, new());
		        if (curr.Value.stockCfg.categoriesBank)
			        stockCategories.categories.TryAdd(StringKeys.Cat_Bank, new());
		        if (curr.Value.stockCfg.categoriesFavourites)
			        stockCategories.categories.TryAdd(StringKeys.Cat_Favourites, new());
		        if (curr.Value.stockCfg.categoriesAllItems)
			        stockCategories.categories.TryAdd(StringKeys.Cat_AllItems, new());
		        foreach (var customItem in data.stock[currKey].cfg.customItems)
		        {
			        foreach (var skin in customItem.Value)
			        {
				        stockCategories.categories.TryAdd(skin.Value.category, new());
				        stockCategories.categories[skin.Value.category].TryAdd(customItem.Key, new());
				        if (!stockCategories.categories[skin.Value.category][customItem.Key].Contains(skin.Key))
				        {
					        stockCategories.categories[skin.Value.category][customItem.Key].Add(skin.Key);
					        stockCategories.itemCount.TryAdd(skin.Value.category, 0);
					        stockCategories.itemCount[skin.Value.category]++;
				        }
			        }
		        }
		        foreach (var item in ItemManager.itemList)
		        {
			        string cat = item.category.ToString();
			        if (curr.Value.stockCfg.categoriesBlacklisted.Contains(cat)) continue;
			        if (curr.Value.stockCfg.itemsHiddenShortnames.Contains(item.shortname)) continue;
			        stockCategories.categories.TryAdd(cat, new());
			        stockCategories.categories[cat].TryAdd(item.shortname, new());
			        if (!stockCategories.categories[cat][item.shortname].Contains(0))
			        {
				        stockCategories.categories[cat][item.shortname].Add(0);
				        stockCategories.itemCount.TryAdd(cat, 0);
				        stockCategories.itemCount[cat]++;
			        }
		        }
	        }
        }

        private void CheckForMissingPrices()
        {
	        foreach (var stock in data.stock)
	        {
		        if (!config.curr.ContainsKey(stock.Key)) continue;
		        if (!config.curr[stock.Key].stockCfg.enabled) continue;
		        foreach (var shortname in stock.Value.prices.items.ToArray())
		        {
			        bool changed = false;
			        foreach (var skin in shortname.Value.ToArray())
			        {
				        if (!stock.Value.cfg.sellItems.TryGetValue(shortname.Key, out var skins) || !skins.ContainsKey(skin.Key))
				        {
					        shortname.Value.Remove(skin.Key);
					        changed = true;
				        }
			        }
			        if (changed && shortname.Value.Count == 0)
				        stock.Value.prices.items.Remove(shortname.Key);
		        }
		        foreach (var shortname in stock.Value.cfg.sellItems)
			        foreach (var skin in shortname.Value)
				        if (!stock.Value.prices.items.TryGetValue(shortname.Key, out var skins) || !skins.ContainsKey(skin.Key))
					        RollPrices(stock.Key, shortname.Key, skin.Key);
	        }
        }

        private void StopTimers()
        {
	        foreach (var runTimer in timers)
		        runTimer.Value.Destroy();
        }

        private void ClearBoxes()
        {
	        foreach (var cp in cache)
	        {
		        ShoppyCache sc = cp.Value;
		        if (!sc.stockCache.container) continue;
		        if (sc.stockCache.container.inventory.itemList.Count > 0 && cp.Value.stockCache.container.LastLootedByPlayer)
			        foreach (var item in sc.stockCache.container.inventory.itemList.ToArray())
				        cp.Value.stockCache.container.LastLootedByPlayer.GiveItem(item);
		        cp.Value.stockCache.container.Kill();
	        }
        }

        private void FixData()
        {
	        foreach (var player in data.currencies.players.Values)
		        player.userName = player.userName.Replace("\"", "").Replace("\\", "");
	        foreach (var stock in data.stock.Values)
	        {
		        foreach (var shortname in stock.listings.sellOffers.Values)
				foreach (var skin in shortname.Values)
				foreach (var item in skin)
					item.userName = item.userName.Replace("\"", "").Replace("\\", "");
		        foreach (var shortname in stock.listings.buyOffers.Values)
		        foreach (var skin in shortname.Values)
		        foreach (var item in skin)
			        item.userName = item.userName.Replace("\"", "").Replace("\\", "");
		        foreach (var action in stock.stats.actions.Values)
		        {
			        if (!string.IsNullOrEmpty(action.field1))
						action.field1 = action.field1.Replace("\"", "").Replace("\\", "");
			        if (!string.IsNullOrEmpty(action.field2))
						action.field2 = action.field2.Replace("\"", "").Replace("\\", "");
			        if (!string.IsNullOrEmpty(action.field3))
						action.field3 = action.field3.Replace("\"", "").Replace("\\", "");
			        if (!string.IsNullOrEmpty(action.field4))
						action.field4 = action.field4.Replace("\"", "").Replace("\\", "");
			        if (!string.IsNullOrEmpty(action.field5))
						action.field5 = action.field5.Replace("\"", "").Replace("\\", "");
		        }
	        }
        }

        private void FindDlcDefinitions()
        {
	        ownershipShortnames.Clear();
	        ownershipDlcs.Clear();
	        //shortnameRedirect.Clear();
	        foreach (var skin in ItemSkinDirectory.Instance.skins)
	        {
		        ItemSkin itemSkin = skin.invItem as ItemSkin;
		        if (!itemSkin)
		        {
			        ItemDefinition def = ItemManager.FindItemDefinition(skin.itemid);
			        if (!def) continue;
			        ownershipShortnames[def.shortname] = skin.id;
			        continue;
		        }
		        if (itemSkin.UnlockedByDefault || !itemSkin.Redirect) continue;
		        ownershipShortnames[itemSkin.Redirect.shortname] = skin.id;
	        }
	        foreach (var item in ItemManager.itemList)
	        {
		        if (item.steamDlc && !item.steamDlc.bypassLicenseCheck)
			        ownershipDlcs[item.shortname] = item.steamDlc.dlcAppID;
		        if (item.steamItem)
			        ownershipShortnames[item.shortname] = item.steamItem.id;
	        }
        }

        private void CreateIconRedirects()
        {
	        foreach (var item in ItemManager.itemList)
	        {
		        if (item.Parent)
					iconRedirect[item.shortname] = item.Parent.itemid;
		        else
			        iconRedirect[item.shortname] = item.itemid;
		        if (item.HasFlag((ItemDefinition.Flag)128) && CustomItemDefinitions != null)
		        {
			        ulong customSkin = CustomItemDefinitions.Call<ulong>("GetSkin", item);
			        if (customSkin != 0)
				        skinRedirect[item.shortname] = customSkin;
		        }
	        }
        }

        private void ResetEditorState()
        {
	        ConfigEditorCache.edited = false;
	        ConfigEditorCache.isBeingEdited = false;
	        ConfigEditorCache.config = null;
	        ConfigEditorCache.shopDatas = new();
	        ConfigEditorCache.stockDatas = new();
	        ConfigEditorCache.enterTree = new();
	        ConfigEditorCache.cachedKey = null;
	        ConfigEditorCache.cachedValue = null;
        }
        
        #endregion
		
		#region API
        
        private string FormatCurrency(string key, BasePlayer player)
		{
			float currency = GetPlayerCurrency(player, key);
	        StringBuilder sb = Pool.Get<StringBuilder>();
			string currString = FormatCurrency(config.curr[key].currCfg, currency, sb);
			Pool.FreeUnmanaged(ref sb);
			return currString;
		}
        
        private void GiveCurrency(string key, ulong userId, int amount) => AddCurrency(key, userId, amount, true);
        
        private void GiveCurrency(string key, BasePlayer player, int amount) => AddCurrency(key, player.userID, amount, true);
        
        private void GiveCurrency(string key, ulong userId, float amount) => AddCurrency(key, userId, amount, true);
        
        private void GiveCurrency(string key, BasePlayer player, float amount) => AddCurrency(key, player.userID, amount, true);
        
        private bool TakeCurrency(string key, ulong userId, int amount) => TakeCurrency(key, userId, amount, true);
        
        private bool TakeCurrency(string key, BasePlayer player, int amount) => TakeCurrency(key, player, amount, true);
        
        private bool TakeCurrency(string key, ulong userId, float amount) => TakeCurrency(key, userId, amount, true);
        
        private bool TakeCurrency(string key, BasePlayer player, float amount) => TakeCurrency(key, player, amount, true);
        
        private int GetCurrencyAmount(string key, ulong userId) => Mathf.FloorToInt(GetPlayerCurrency(userId, key));
        
        private int GetCurrencyAmount(string key, BasePlayer player) => Mathf.FloorToInt(GetPlayerCurrency(player, key));
        
        private float GetCurrencyAmountFloat(string key, ulong userId) => GetPlayerCurrency(userId, key);
        
        private float GetCurrencyAmountFloat(string key, BasePlayer player) => GetPlayerCurrency(player, key);

		#endregion
        
        #region Methods

        private void AddPlayerToData(BasePlayer player)
        {
	        data.currencies.players.TryAdd(player.userID, new());
	        PlayerData pd = data.currencies.players[player.userID];
	        pd.userName = player.displayName.Replace("\"", "").Replace("\\", "");
	        foreach (var curr in config.curr)
		        pd.currencies.TryAdd(curr.Key, 0);
        }
        
        private void AddPlayerToData(ulong userId)
        {
			bool wasInData = data.currencies.players.ContainsKey(userId);
			if (!wasInData)
				data.currencies.players.TryAdd(userId, new());
	        PlayerData pd = data.currencies.players[userId];
			if (!wasInData)
				pd.userName = "UNKNOWN";
	        foreach (var curr in config.curr)
		        pd.currencies.TryAdd(curr.Key, 0);
        }
        
        private string FormatCurrency(string key, float amount, StringBuilder sb) => FormatCurrency(config.curr[key].currCfg, amount, sb);

        private string FormatCurrency(CurrencyConfig currCfg, float amount, StringBuilder sb)
        {
	        if (currCfg.formatCurrency)
				return string.Format(currCfg.currencyFormat, FormatNumber(amount, sb));
	        return string.Format(currCfg.currencyFormat, amount.ToString("0.##"));
        }
        
        private string FormatAmount(string key, int amount, StringBuilder sb) => FormatAmount(config.curr[key].currCfg, amount, sb);

        private string FormatAmount(CurrencyConfig currCfg, int amount, StringBuilder sb) => string.Format(currCfg.amountFormat, FormatNumber(amount, sb));

        private static readonly char[] amountSuffixes = new[] { 'k', 'm', 'b', 't' };

        private static string FormatNumber(float amount, StringBuilder sb)
        {
	        if (amount == 0) return "0";
	        if (amount < 1) return amount.ToString("0.###");
	        if (amount < 10) return amount.ToString("0.##");
	        if (amount < 100) return amount.ToString("0.#");
	        if (amount < 1000) return amount.ToString("0");
	        int index = -1;
	        while (amount >= 1000 && index < amountSuffixes.Length - 1)
	        {
		        amount /= 1000f;
		        index++;
	        }
	        sb.Clear();
	        if (amount < 10)
		        sb.Append(amount.ToString("0.##"));
	        else if (amount < 100)
		        sb.Append(amount.ToString("0.#"));
	        else
		        sb.Append(amount.ToString("0"));
	        sb.Append(amountSuffixes[index]);
	        string output = sb.ToString();
	        return output;
        }
        
        private static string FormatTime(int totalSeconds, StringBuilder sb)
        {
	        if (totalSeconds == 0) 
		        return "0s";
	        int days = totalSeconds / 86400;
	        int hours = (totalSeconds % 86400) / 3600;
	        int minutes = (totalSeconds % 3600) / 60;
	        int seconds = totalSeconds % 60;
	        sb.Clear();
	        int count = 0;
	        if (days > 0 && count < 2)
	        {
		        sb.Append(days).Append("d ");
		        count++;
	        }
	        if (hours > 0 && count < 2)
	        {
		        sb.Append(hours).Append("h ");
		        count++;
	        }
	        if (minutes > 0 && count < 2)
	        {
		        sb.Append(minutes).Append("m ");
		        count++;
	        }
	        if (seconds > 0 && count < 2)
	        {
		        sb.Append(seconds).Append("s ");
		        count++;
	        }
	        string output = sb.ToString().TrimEnd();
	        return output;
        }

        private string FormatListingTime(BasePlayer player, float time, StringBuilder sb)
        {
	        if (time < 0)
		        return Lang("NoLimit", player.UserIDString);
	        if (time < 24)
		        return sb.Clear().Append(Mathf.RoundToInt(time)).Append('H').ToString();
	        if (time == 24.7f)
		        return Lang("WipeLength", player.UserIDString);
	        return sb.Clear().Append((time - time % 24) / 24).Append('D').ToString();
        }


        private DateTime GetPlayerLastPurchase(ulong id, ShopData sd, string cat, string key)
        {
	        if (!sd.stats.playerLastPurchases.TryGetValue(id, out var playerData))
		        return DateTime.MinValue;
	        if (!playerData.TryGetValue(cat, out var category))
		        return DateTime.MinValue;
	        if (!category.TryGetValue(key, out var item))
		        return DateTime.MinValue;
	        return item;
        }

        private int GetPlayerDailyPurchases(ulong id, ShopData sd, string cat, string key, bool isGlobal = false)
        {
	        if (isGlobal)
	        {
		        int counter = 0;
		        foreach (var playerData in sd.stats.playerDailyPurchases.Values)
		        {
			        if (!playerData.TryGetValue(dateString, out var today)) continue;
			        if (!today.TryGetValue(cat, out var category)) continue;
			        if (!category.TryGetValue(key, out var count)) continue;
			        counter += count;
		        }
		        return counter;
	        }
	        else
	        {
		        if (!sd.stats.playerDailyPurchases.TryGetValue(id, out var playerData))
			        return 0;
		        if (!playerData.TryGetValue(dateString, out var today))
			        return 0;
		        if (!today.TryGetValue(cat, out var category))
			        return 0;
		        if (!category.TryGetValue(key, out var count))
			        return 0;
		        return count;
	        }
        }

        private int GetPlayerWipePurchases(ulong id, ShopData sd, string cat, string key, bool isGlobal = false)
        {
	        if (isGlobal)
	        {
		        int counter = 0;
		        foreach (var playerData in sd.stats.playerWipePurchases.Values)
		        {
			        if (!playerData.TryGetValue(cat, out var category)) continue;
			        if (!category.TryGetValue(key, out var count)) continue;
			        counter += count;
		        }
		        return counter;
	        }
	        else
	        {
		        if (!sd.stats.playerWipePurchases.TryGetValue(id, out var playerData))
			        return 0;
		        if (!playerData.TryGetValue(cat, out var category))
			        return 0;
		        if (!category.TryGetValue(key, out var count))
			        return 0;
		        return count;
	        }
        }

        private int GetPlayerPurchaseDailyLimit(BasePlayer player, ShopListingConfig slc)
        {
	        int playerLimit = slc.dailyBuy;
	        foreach (var perm in slc.dailyBuyPerms)
		        if (perm.Value > playerLimit && permission.UserHasPermission(player.UserIDString, perm.Key))
			        playerLimit = perm.Value;
	        return playerLimit;
        }

        private int GetPlayerPurchaseWipeLimit(BasePlayer player, ShopListingConfig slc)
        {
	        int playerLimit = slc.wipeBuy;
	        foreach (var perm in slc.wipeBuyPerms)
		        if (perm.Value > playerLimit && permission.UserHasPermission(player.UserIDString, perm.Key))
			        playerLimit = perm.Value;
	        return playerLimit;
        }

        private int GetPlayerDailySoldItems(ulong id, StockData sd, string shortname, ulong skin)
        {
	        if (!sd.stats.playerDailySoldItems.TryGetValue(id, out var playerData)) return 0;
	        if (!playerData.TryGetValue(dateString, out var shortnames)) return 0;
	        if (!shortnames.TryGetValue(shortname, out var skins)) return 0;
	        if (!skins.TryGetValue(skin, out int sold)) return 0;
	        return sold;
        }

        private int GetPlayerDailySoldItemLimit(string id, SellItemConfig sic)
        {
	        int sellLimit = sic.maxSellAmount;
	        foreach (var perm in sic.maxSellAmountPerms)
		        if (perm.Value > sellLimit && permission.UserHasPermission(id, perm.Key))
			        sellLimit = perm.Value;
	        return sellLimit;
        }

        private string GetStackCount(string shortname, int amount)
        {
	        if (shortname.Length == 0) return string.Empty;
	        ItemDefinition def = ItemManager.FindItemDefinition(shortname);
	        if (!def) return string.Empty;
	        return (amount / (float)def.stackable).ToString("0.###");
        }

        private int GetItemCapacity(BasePlayer player, string shortname, int amount)
        {
	        ItemDefinition def = ItemManager.FindItemDefinition(shortname);
	        if (!def) return 1;
	        int freeSlots = player.inventory.containerMain.capacity - player.inventory.containerMain.itemList.Count;
	        freeSlots += player.inventory.containerBelt.capacity - player.inventory.containerBelt.itemList.Count;
	        return Mathf.FloorToInt(freeSlots * Mathf.FloorToInt(def.stackable / (float)amount));
        }

        private void HandleShopPurchase(BasePlayer player)
        {
	        ShoppyCache sc = cache[player.userID];
	        StringBuilder sb = Pool.Get<StringBuilder>();
	        ShopData sd = data.shop[sc.shopName];
			bool isSearch = sc.shopCache.category == "Search";
			ShopListingConfig slc = null;
			string validCategory = sc.shopCache.category;
			if (isSearch)
			{
				foreach (var category in sd.cfg.categories)
				{
					if (category.Value.listings.ContainsKey(sc.shopCache.listingKey))
					{
						slc = sd.cfg.categories[category.Key].listings[sc.shopCache.listingKey];
						validCategory = category.Key;
						break;
					}
				}
			}
			else
				slc = sd.cfg.categories[validCategory].listings[sc.shopCache.listingKey];

	        int maxPurchaseLimit = int.MaxValue;
	        if (slc.limitToOne)
		        maxPurchaseLimit = 1;
	        if (slc.cooldown > 0)
	        {
		        DateTime lastPurchase = GetPlayerLastPurchase(player.userID, sd, validCategory, sc.shopCache.listingKey);
		        float lowestCooldown = slc.cooldown;
		        if (lastPurchase != DateTime.MinValue)
		        {
			        foreach (var perm in slc.cooldownPerms)
				        if (perm.Value < lowestCooldown && permission.UserHasPermission(player.UserIDString, perm.Key))
					        lowestCooldown = perm.Value;
			        if (lastPurchase.AddSeconds(lowestCooldown) > DateTime.Now)
				        maxPurchaseLimit = 0;
		        }
	        }
	        if (maxPurchaseLimit > 0 && (slc.wipeBuy > 0 || slc.wipeBuyPerms.Count > 0))
	        {
		        int wipePurchased = GetPlayerWipePurchases(player.userID, sd, validCategory, sc.shopCache.listingKey, slc.globalLimit);
		        int playerLimit = GetPlayerPurchaseWipeLimit(player, slc);
		        int canWipePurchaseCount = playerLimit - wipePurchased;
		        if (maxPurchaseLimit > canWipePurchaseCount)
			        maxPurchaseLimit = canWipePurchaseCount;
	        }
	        if (maxPurchaseLimit > 0 && (slc.dailyBuy > 0 || slc.dailyBuyPerms.Count > 0))
	        {
		        int dailyPurchased = GetPlayerDailyPurchases(player.userID, sd, validCategory, sc.shopCache.listingKey, slc.globalLimit);
		        int playerLimit = GetPlayerPurchaseDailyLimit(player, slc);
		        int canDailyPurchaseCount = playerLimit - dailyPurchased;
		        if (maxPurchaseLimit > canDailyPurchaseCount)
			        maxPurchaseLimit = canDailyPurchaseCount;
	        }
	        if (maxPurchaseLimit > 0 && slc.shortname.Length > 0)
	        {
		        int stackCount = GetItemCapacity(player, slc.shortname, slc.amount);
		        if (maxPurchaseLimit > stackCount)
			        maxPurchaseLimit = stackCount;
	        }

	        float sumPrice = sc.shopCache.fixedPrice;
	        if (slc.pricePerPurchaseMultiplier != 1)
	        {
		        int priceMultTime = slc.multiplyPricePerDaily ? GetPlayerDailyPurchases(player.userID, sd, validCategory, sc.shopCache.listingKey, slc.globalLimit) : GetPlayerWipePurchases(player.userID, sd, validCategory, sc.shopCache.listingKey, slc.globalLimit);
		        float priceCache = sumPrice;
		        for (int i = 0; i < priceMultTime + sc.shopCache.amount; i++)
		        {
			        if (i == priceMultTime)
				        sumPrice = priceCache;
			        if (i > priceMultTime)
				        sumPrice += priceCache;
			        priceCache *= slc.pricePerPurchaseMultiplier;
		        }
	        }
	        else
		        sumPrice *= sc.shopCache.amount;
	        float playerBalance = GetPlayerCurrency(player, sc.shopName);
	        float playerBalanceAfter = playerBalance - sumPrice;
	        if (playerBalanceAfter < 0)
		        maxPurchaseLimit = 0;
	        if (maxPurchaseLimit == 0 || sc.shopCache.amount > maxPurchaseLimit)
	        {
		        Pool.FreeUnmanaged(ref sb);
		        return;
	        }
	        if (sumPrice > 0 && !TakeCurrency(sc.shopName, player, sumPrice))
	        {
		        Pool.FreeUnmanaged(ref sb);
		        return;
	        }
	        if (slc.cooldown > 0)
	        {
		        sd.stats.playerLastPurchases.TryAdd(player.userID, new());
		        sd.stats.playerLastPurchases[player.userID].TryAdd(validCategory, new());
		        sd.stats.playerLastPurchases[player.userID][validCategory][sc.shopCache.listingKey] = DateTime.Now;
	        }
	        sd.stats.playerWipePurchases.TryAdd(player.userID, new());
	        sd.stats.playerWipePurchases[player.userID].TryAdd(validCategory, new());
	        sd.stats.playerWipePurchases[player.userID][validCategory].TryAdd(sc.shopCache.listingKey, 0);
	        sd.stats.playerWipePurchases[player.userID][validCategory][sc.shopCache.listingKey] += sc.shopCache.amount;
	        sd.stats.playerDailyPurchases.TryAdd(player.userID, new());
	        sd.stats.playerDailyPurchases[player.userID].TryAdd(dateString, new());
	        sd.stats.playerDailyPurchases[player.userID][dateString].TryAdd(validCategory, new());
	        sd.stats.playerDailyPurchases[player.userID][dateString][validCategory].TryAdd(sc.shopCache.listingKey, 0);
	        sd.stats.playerDailyPurchases[player.userID][dateString][validCategory][sc.shopCache.listingKey] += sc.shopCache.amount;
	        sd.stats.uniquePurchases.TryAdd(validCategory, new());
	        sd.stats.uniquePurchases[validCategory].TryAdd(sc.shopCache.listingKey, new());
	        if (!sd.stats.uniquePurchases[validCategory][sc.shopCache.listingKey].Contains(player.userID))
		        sd.stats.uniquePurchases[validCategory][sc.shopCache.listingKey].Add(player.userID);
	        if (slc.shortname.Length > 0)
	        {
		        ItemDefinition def = ItemManager.FindItemDefinition(slc.shortname);
		        if (!def)
		        {
			        Pool.FreeUnmanaged(ref sb);
			        return;
		        }
		        int itemAmount = sc.shopCache.amount * slc.amount;
		        ShopConfig shopCfg = config.curr[sc.shopName].shopCfg;
		        while (itemAmount > 0)
		        {
			        int giveAmount = itemAmount > def.stackable ? def.stackable : itemAmount;
			        Item item;
			        if (slc.blueprint)
			        {
				        item = ItemManager.CreateByName("blueprintbase", giveAmount, slc.skin);
				        item.blueprintTarget = def.itemid;
			        }
			        else
						item = ItemManager.Create(def, giveAmount, slc.skin);
			        if (slc.itemName.Length > 0)
				        item.name = slc.itemName;
			        if (shopCfg.addOwnershipEverywhere && item.ownershipShares == null)
						item.ownershipShares = Pool.Get<List<ItemOwnershipShare>>();
			        if (shopCfg.addOwnership)
				        item.ownershipShares?.Add(new ItemOwnershipShare
				        {
					        username = player.displayName,
					        reason = string.Empty,
					        amount = item.amount,
				        });
			        player.GiveItem(item);
			        itemAmount -= giveAmount;
		        }
	        }
	        if (slc.commands.Count > 0)
	        {
		        for (int i = 0; i < sc.shopCache.amount; i++)
		        {
			        foreach (var command in slc.commands)
			        {
				        string commandFormat = command.Replace("{userName}", player.displayName).Replace("{userId}", player.UserIDString).Replace("{username}", player.displayName).Replace("{userid}", player.UserIDString).Replace("{userPosX}", player.transform.position.x.ToString()).Replace("{userPosY}", player.transform.position.y.ToString()).Replace("{userPosZ}", player.transform.position.z.ToString());
				        Server.Command(commandFormat);
			        }
		        }
	        }
	        using CUI cui = new CUI(CuiHandler);
	        cui.Destroy("ShoppyStockUI_ShopListingCore", player);
	        string currencyFormat = FormatCurrency(sc.shopName, sumPrice, sb);
	        LogToConsole("PlayerPurchasedFromShop", player.displayName, player.userID, slc.displayName, sc.shopCache.amount * slc.amount, currencyFormat);
	        SendEffect(player, StringKeys.Effect_Money);
	        ShowPopUp(player, Lang("ShopPurchaseMessage", player.UserIDString, slc.displayName, sc.shopCache.amount * slc.amount, currencyFormat));
	        Interface.CallHook("OnShopItemPurchased", player, sc.shopName, sumPrice, slc.displayName, sc.shopCache.amount * slc.amount);
	        cui.v2.UpdateText("ShoppyStockUI_ShopCurrencyAmount", FormatCurrency(sc.shopName, GetPlayerCurrency(player, sc.shopName), sb));
	        bool shouldDrawCategory = slc.cooldown > 0 || slc.dailyBuy > 0 || slc.wipeBuy > 0 ||
	                                  slc.pricePerPurchaseMultiplier != 1 || slc.dailyBuyPerms.Count > 0 ||
	                                  slc.wipeBuyPerms.Count > 0;
	        if (shouldDrawCategory)
		        DrawCategory(cui, player, sc, sb);
	        byte[] uiBytes = cui.v2.GetUiBytes();
	        if (shouldDrawCategory)
		        cui.Destroy("ShoppyStockUI_ShopCategoryUpdate", player);
	        cui.v2.SendUiBytes(player, uiBytes);
	        Pool.FreeUnmanaged(ref sb);
        }


        private float GetPlayerCurrency(BasePlayer player, string currKey)
        {
	        CurrencyConfig cc = config.curr[currKey].currCfg;
	        float invCurrency = 0;
	        if (cc.invCurrency)
	        {
		        foreach (var currItem in cc.currItems)
		        {
			        foreach (var item in player.inventory.containerMain.itemList)
				        if (item.info.shortname == currItem.shortname && item.skin == currItem.skinId)
					        invCurrency += currItem.value * item.amount;
			        foreach (var item in player.inventory.containerBelt.itemList)
				        if (item.info.shortname == currItem.shortname && item.skin == currItem.skinId)
					        invCurrency += currItem.value * item.amount;
		        }
	        }
	        switch (cc.plugin.ToLower())
	        {
		        case "":
					float balance = 0;
					if (data.currencies.players.TryGetValue(player.userID, out var currData))
						balance = currData.currencies[currKey];
			        return invCurrency + balance;
		        case "economics":
			        if (Economics == null) return invCurrency;
			        return invCurrency + (float)Economics.Call<double>("Balance", player.userID.Get());
		        case "serverrewards":
			        if (ServerRewards == null) return invCurrency;
			        return invCurrency + ServerRewards.Call<int>("CheckPoints", player.userID.Get());
		        case "iqeconomic":
			        if (IQEconomic == null) return invCurrency;
			        return invCurrency + IQEconomic.Call<int>("API_GET_BALANCE", player.userID.Get());
		        case "banksystem":
			        if (BankSystem == null) return invCurrency;
			        return invCurrency + BankSystem.Call<int>("Balance", player.userID.Get());
		        default:
			        return invCurrency;
	        }
        }
        
        private float GetPlayerCurrency(ulong userId, string currKey)
        {
	        CurrencyConfig cc = config.curr[currKey].currCfg;
	        switch (cc.plugin.ToLower())
	        {
		        case "":
					if (data.currencies.players.TryGetValue(userId, out var userData))
						return userData.currencies[currKey];
					return 0;
		        case "economics":
			        if (Economics == null) return 0;
			        return (float)Economics.Call<double>("Balance", userId);
		        case "serverrewards":
			        if (ServerRewards == null) return 0;
			        return ServerRewards.Call<int>("CheckPoints", userId);
		        case "iqeconomic":
			        if (IQEconomic == null) return 0;
			        return IQEconomic.Call<int>("API_GET_BALANCE", userId);
		        case "banksystem":
			        if (BankSystem == null) return 0;
			        return BankSystem.Call<int>("Balance", userId);
		        default:
			        return 0;
	        }
        }
        
        private bool TakeCurrency(string currKey, BasePlayer player, float amount, bool dbCheck = false)
        {
	        if (dbCheck)
	        {
		        if (!data.currencies.players.ContainsKey(player.userID)) return false;
		        if (!data.currencies.players[player.userID].currencies.ContainsKey(currKey)) return false;
	        }
	        CurrencyConfig cc = config.curr[currKey].currCfg;
	        float amountToTake = amount;
	        if (cc.invCurrency)
	        {
		        foreach (var currItem in cc.currItems)
		        {
			        int itemsToTake = Mathf.CeilToInt(amountToTake / currItem.value);
			        foreach (var item in player.inventory.containerMain.itemList.ToArray())
				        if (item.info.shortname == currItem.shortname && item.skin == currItem.skinId)
				        {
					        if (item.amount > itemsToTake)
					        {
						        amountToTake = 0;
						        item.amount -= itemsToTake;
						        itemsToTake = 0;
						        item.MarkDirty();
						        break;
					        }
					        amountToTake -= item.amount * currItem.value;
					        itemsToTake -= item.amount;
					        item.GetHeldEntity()?.Kill();
					        item.DoRemove();
				        }
			        if (itemsToTake > 0)
			        {
				        foreach (var item in player.inventory.containerBelt.itemList.ToArray())
					        if (item.info.shortname == currItem.shortname && item.skin == currItem.skinId)
					        {
						        if (item.amount > itemsToTake)
						        {
							        amountToTake = 0;
							        item.amount -= itemsToTake;
							        item.MarkDirty();
							        break;
						        }
						        amountToTake -= item.amount * currItem.value;
						        itemsToTake -= item.amount;
						        item.GetHeldEntity()?.Kill();
						        item.DoRemove();
					        }
			        }
		        }
		        if (amountToTake <= 0) return true;
	        }
	        return TakeCurrency(currKey, player.userID.Get(), amountToTake, dbCheck);
        }

        private bool TakeCurrency(string currKey, ulong userId, float amount, bool dbCheck = false)
        {
	        if (dbCheck)
	        {
		        if (!data.currencies.players.ContainsKey(userId)) return false;
		        if (!data.currencies.players[userId].currencies.ContainsKey(currKey)) return false;
	        }
	        CurrencyConfig cc = config.curr[currKey].currCfg;
	        float playerBalance = GetPlayerCurrency(userId, currKey);
	        if (playerBalance < amount) return false;
	        switch (cc.plugin.ToLower())
	        {
		        case "":
			        data.currencies.players[userId].currencies[currKey] -= amount;
			        return true;
		        case "economics":
			        if (Economics == null) return false;
			        return Economics.Call<bool>("Withdraw", userId, (double)amount);
		        case "serverrewards":
			        if (ServerRewards == null) return false;
			        ServerRewards.Call("TakePoints", userId, Mathf.CeilToInt(amount));
			        return true;
		        case "iqeconomic":
			        if (IQEconomic == null) return false;
			        IQEconomic.Call("API_GET_BALANCE", userId, Mathf.CeilToInt(amount));
			        return true;
		        case "banksystem":
			        if (BankSystem == null) return false;
			        BankSystem.Call("Withdraw", userId, Mathf.CeilToInt(amount));
			        return true;
		        default:
			        return false;
	        }
        }
        
        private bool AddCurrency(string currKey, ulong userId, float amount, bool dbCheck = false, BasePlayer player = null, bool forceDeposit = false)
        {
	        if (dbCheck && !data.currencies.players.ContainsKey(userId))
		        AddPlayerToData(userId);
	        CurrencyConfig cc = config.curr[currKey].currCfg;
	        if (!forceDeposit && cc.invCurrGive && cc.currItems.Count > 0)
	        {
		        bool givenAnything = false;
		        while (amount > 0)
		        {
			        float biggestPossibleValue = 0;
			        foreach (var currItem in cc.currItems)
			        {
				        if (currItem.value <= amount && currItem.value > biggestPossibleValue)
					        biggestPossibleValue = currItem.value;
			        }
			        if (biggestPossibleValue == 0)
				        return givenAnything;
			        foreach (var currItem in cc.currItems)
			        {
				        if (currItem.value == biggestPossibleValue)
				        {
					        int maxPossibleAmount = Mathf.FloorToInt(amount / currItem.value);
					        if (maxPossibleAmount == 0)
					        {
						        amount = 0;
						        break;
					        }
					        amount -= maxPossibleAmount * currItem.value;
					        Item item = ItemManager.CreateByName(currItem.shortname, maxPossibleAmount, currItem.skinId);
					        if (currItem.displayName.Length > 0)
						        item.name = currItem.displayName;
					        if (player)
								player.GiveItem(item);
					        else
						        RedeemStorageAPI?.Call("AddItem", userId, config.curr[currKey].stockCfg.redeemInventoryName, item, true);
					        givenAnything = true;
				        }
			        }
		        }
	        }
	        else
	        {
		        switch (cc.plugin.ToLower())
		        {
			        case "":
				        data.currencies.players[userId].currencies[currKey] += amount;
				        break;
			        case "economics":
				        if (Economics == null) return false;
				        Economics.Call("Deposit", userId, (double)amount);
				        break;
			        case "serverrewards":
				        if (ServerRewards == null) return false;
				        ServerRewards.Call("AddPoints", userId, Mathf.FloorToInt(amount));
				        break;
			        case "iqeconomic":
				        if (IQEconomic == null) return false;
				        int balance = IQEconomic.Call<int>("API_GET_BALANCE", userId);
				        IQEconomic.Call("API_SET_BALANCE", userId, balance + Mathf.FloorToInt(amount));
				        break;
			        case "banksystem":
				        if (BankSystem == null) return false;
				        BankSystem.Call("Deposit", userId, Mathf.FloorToInt(amount));
				        break;
		        }
	        }
	        return true;
        }

        private void CheckForOfferRoll()
        {
	        foreach (var curr in config.curr)
	        {
		        if (!curr.Value.shopCfg.enabled) continue;
		        foreach (var category in data.shop[curr.Key].cfg.categories)
		        {
			        if (category.Value.rollInterval > 0)
			        {
				        if (!data.shop[curr.Key].stats.rolledOffers.ContainsKey(category.Key))
				        {
					        RollShopOffers(curr.Key, category.Key);
					        continue;
				        }
				        RolledOffersData rod = data.shop[curr.Key].stats.rolledOffers[category.Key];
				        if (rod.cachedInterval != category.Value.rollInterval)
				        {
					        RollShopOffers(curr.Key, category.Key);
					        continue;
				        }
				        if (rod.nextRollTime < DateTime.Now)
					        RollShopOffers(curr.Key, category.Key);
			        }
		        }
	        }
        }

        private void CheckForStockHourTimer()
        {
	        int notReady = 0;
	        foreach (var curr in config.curr)
	        {
		        if (!curr.Value.stockCfg.enabled) continue;
		        if (!timers.ContainsKey(curr.Key))
			        notReady++;
	        }
	        if (notReady == 0 && timers.ContainsKey("hourSelect"))
	        {
		        LogToConsole("StartHoursSet");
		        timers["hourSelect"].Destroy();
		        timers.Remove("hourSelect");
		        return;
	        }
	        int currentMinuite = DateTime.Now.Minute;
	        bool updated = false;
	        foreach (var curr in config.curr)
	        {
		        if (!curr.Value.stockCfg.enabled) continue;
		        if (timers.ContainsKey(curr.Key)) continue;
		        if (curr.Value.stockCfg.sellCfg.priceUpdateCertainMinutes.Count > 0 && !curr.Value.stockCfg.sellCfg.priceUpdateCertainMinutes.Contains(currentMinuite)) continue;
		        UpdateStockPrices(curr.Key);
		        timers.TryAdd(curr.Key, timer.Every(curr.Value.stockCfg.sellCfg.priceUpdateInterval * 60, () => UpdateStockPrices(curr.Key)));
		        updated = true;
	        }
	        if (updated)
		        CheckForStockHourTimer();
        }

        private void CheckForOfferEnds()
        {
	        DateTime now = DateTime.Now;
	        bool updated = false;
	        foreach (var stock in data.stock)
	        {
		        foreach (var shortname in stock.Value.listings.sellOffers)
		        foreach (var skin in shortname.Value)
		        foreach (var listing in skin.Value.ToArray())
		        {
			        if (listing.listingEndTime == DateTime.MaxValue || listing.listingEndTime == DateTime.MinValue) continue;
			        if (listing.listingEndTime > now) continue;
			        BasePlayer owner = BasePlayer.FindByID(listing.userId);
			        if (owner)
			        {
				        SendEffect(owner, StringKeys.Effect_Info);
				        ShowPopUp(owner, Lang("ListingReturnedEndTime", owner.UserIDString));
			        }
			        LogToConsole("ItemReturnedFromShop", listing.userId, listing.item.amount, shortname.Key, skin.Key, stock.Key);
			        RedeemStorageAPI?.Call("AddItem", listing.userId, config.curr[stock.Key].stockCfg.redeemInventoryName, listing.item.ToItem(), true);
			        TryUpdateListingUI(stock.Key, shortname.Key, skin.Key, false);
			        skin.Value.Remove(listing);
			        updated = true;
		        }
		        foreach (var shortname in stock.Value.listings.buyOffers)
		        foreach (var skin in shortname.Value)
		        foreach (var listing in skin.Value.ToArray())
		        {
			        if (listing.listingEndTime == DateTime.MaxValue || listing.listingEndTime == DateTime.MinValue) continue;
			        if (listing.listingEndTime > now) continue;
			        BasePlayer owner = BasePlayer.FindByID(listing.userId);
			        if (owner)
			        {
				        SendEffect(owner, StringKeys.Effect_Info);
				        ShowPopUp(owner, Lang("ListingMoneyReturnedEndTime", owner.UserIDString));
			        }
			        LogToConsole("MoneyReturnedFromShop", listing.userId, listing.item.amount, shortname.Key, skin.Key, listing.price, stock.Key);
			        AddCurrency(stock.Key, listing.userId, listing.price);
			        TryUpdateListingUI(stock.Key, shortname.Key, skin.Key, true);
			        skin.Value.Remove(listing);
			        updated = true;
		        }
	        }
	        if (updated)
		        StartCheckingForStockOfferEnds();
        }

        private void RollShopOffers(string shopName, string catKey)
        {
	        data.shop[shopName].stats.rolledOffers.TryAdd(catKey, new());
	        ShopCategoryConfig scc = data.shop[shopName].cfg.categories[catKey];
	        RolledOffersData rod = data.shop[shopName].stats.rolledOffers[catKey];
	        HashSet<string> newRolls = rod.offers;
	        newRolls.Clear();
	        for (int i = 0; i < scc.rollMaxOffers; i++)
	        {
		        int weightSum = 0;
		        foreach (var listing in scc.listings)
			        if (!newRolls.Contains(listing.Key))
						weightSum += listing.Value.weight;
		        weightSum++;
		        int rand = Core.Random.Range(0, weightSum);
		        int count = 0;
		        foreach (var listing in scc.listings)
		        {
			        if (newRolls.Contains(listing.Key)) continue;
			        ShopListingConfig slc = listing.Value;
			        count += slc.weight;
			        if (count >= rand)
			        {
				        newRolls.Add(listing.Key);
				        break;
			        }
		        }
	        }
	        if (scc.rollBroadcastChat)
	        {
		        StringBuilder sb = Pool.Get<StringBuilder>();
		        string key = sb.Clear().Append("ShopOfferBroadcast_").Append(catKey).ToString();
		        Pool.FreeUnmanaged(ref sb);
		        foreach (var player in BasePlayer.activePlayerList)
			        Mess(player, key);
	        }
	        LogToConsole("NewShopCategoryOffersRolled", catKey, shopName);
	        rod.cachedInterval = scc.rollInterval;
	        rod.nextRollTime = DateTime.Now.AddMinutes(scc.rollInterval);
        }

        private void UpdateAllItems(string shopName, bool updateUi = false)
        {
	        foreach (var shortname in data.stock[shopName].cfg.sellItems)
	        foreach (var skin in shortname.Value)
	        {
		        RollPrices(shopName, shortname.Key, skin.Key);
		        if (updateUi)
					TryUpdatePricesUI(shopName, shortname.Key, skin.Key);
	        }
        }
        
        private void UpdateStockPrices(string shopName)
        {
	        UpdateAllItems(shopName, true);
	        UpdateWebAPI(shopName);
			LogToConsole("UpdatedStockPrices", shopName);
			StockData sd = data.stock[shopName];
			StockConfig stockCfg = config.curr[shopName].stockCfg;
			CurrencyConfig currCfg = config.curr[shopName].currCfg;
			StringBuilder sb = Pool.Get<StringBuilder>();
			if (stockCfg.marketLog.chatPriceRolls || stockCfg.marketLog.tabPriceRolls)
				AddStockAction(shopName, sd, StockActionType.PriceRoll, sb);
			foreach (var player in sd.playerData.players)
			{
				foreach (var shortnames in player.Value.alerts)
					foreach (var skins in shortnames.Value)
					{
						PlayerAlertData pad = skins.Value;
						if (pad.alertPrice == 0 && pad.instaSellPrice == 0) continue;
						string shortname = shortnames.Key;
						ulong skin = skins.Key;
						if (sd.prices.items.TryGetValue(shortname, out var skins1) && skins1.TryGetValue(skin, out SellItemData sid))
						{
							ulong userId = player.Key;
							string userIdString = userId.ToString();
							if (stockCfg.sellCfg.bankAutoSellEnabled && (stockCfg.sellCfg.bankAutoSellPerm.Length == 0 || (stockCfg.sellCfg.bankAutoSellPerm.Length > 0 && permission.UserHasPermission(userIdString, stockCfg.sellCfg.bankAutoSellPerm))) && pad.instaSellPrice > 0 && pad.instaSellPrice < sid.price && player.Value.bankItems.TryGetValue(shortname, out var skins2) && skins2.TryGetValue(skin, out StockBankData sbd) && sbd.amount > 0)
							{
								string itemName = ItemManager.FindItemDefinition(shortname).displayName.english;
								int sellAmount = sbd.amount;
								if (sd.cfg.customItems.TryGetValue(shortname, out var skins3) && skins3.TryGetValue(skin, out StockItemConfig sic))
								{
									itemName = sic.displayName; 
								}
								SellItemConfig sellCfg = sd.cfg.sellItems[shortname][skin];
								if (sellCfg.maxSellAmount > -1)
								{
									int itemsSoldLimit = GetPlayerDailySoldItemLimit(userIdString, sellCfg);
									if (itemsSoldLimit == 0) continue;
									int itemsSold = GetPlayerDailySoldItems(userId, sd, shortname, skin);
									if (itemsSold >= itemsSoldLimit) return;
									int remainingToSell = itemsSoldLimit - itemsSold;
									if (sellAmount > remainingToSell)
										sellAmount = remainingToSell;
								}
								float salePriceMult = 1;
								BasePlayer onlinePlayer = BasePlayer.FindByID(userId);
								foreach (var perm in stockCfg.sellCfg.sellPriceMultipliers)
									if (perm.Value > salePriceMult && permission.UserHasPermission(userIdString, perm.Key))
										salePriceMult = perm.Value;
								if (onlinePlayer && BonusCore != null)
									salePriceMult += BonusCore.Call<float>("GetBonus", onlinePlayer, "Other_Price");
								else if (!onlinePlayer && Artifacts != null)
									salePriceMult += Artifacts.Call<float>("GetPriceBonus", userId) / 100f;
								float price = sid.price * sellAmount;
								if (salePriceMult > 1 && (!stockCfg.sellCfg.sellPriceMultBlacklist.TryGetValue(shortname, out var skins4) || !skins4.Contains(skin)))
								{
									float bonus = price * salePriceMult - price;
									price += bonus;
								}
								sd.stats.playerDailySoldItems.TryAdd(userId, new());
								sd.stats.playerDailySoldItems[userId].TryAdd(dateString, new());
								sd.stats.playerDailySoldItems[userId][dateString].TryAdd(shortname, new());
								sd.stats.playerDailySoldItems[userId][dateString][shortname].TryAdd(skin, 0);
								sd.stats.playerDailySoldItems[userId][dateString][shortname][skin] += sellAmount;
								sd.stats.playerDailyEarnings.TryAdd(userId, new());
								sd.stats.playerDailyEarnings[userId].TryAdd(dateString, 0);
								sd.stats.playerDailyEarnings[userId][dateString] += price;
								sid.sellAmount += sellAmount;
								sid.amountAvailable += Mathf.FloorToInt(sellAmount * (stockCfg.sellCfg.buyFromServerPercentage / 100f));
								string amountString = FormatAmount(currCfg, sellAmount, sb);
								string priceString = FormatCurrency(currCfg, price, sb);
								if (stockCfg.marketLog.chatAlerts || stockCfg.marketLog.tabAlerts)
									AddStockAction(shopName, sd, StockActionType.AutoSell, sb, userIdString, itemName, amountString, priceString);
								player.Value.bankItems[shortname].Remove(skin);
								if (player.Value.bankItems[shortname].Count == 0)
									player.Value.bankItems.Remove(shortname);
								AddCurrency(shopName, player.Key, price);
								LogToConsole("AutoSoldItems", userIdString, shopName, amountString, shortname, skin, priceString);
								if (stockCfg.sellCfg.bankDiscordIntegration)
									DiscordCore?.Call("API_SendPrivateMessage", userIdString, Lang("InstaSellDiscordMessage", userIdString, amountString, priceString, itemName));
							}
							else if (stockCfg.sellCfg.priceAlertEnabled && (stockCfg.sellCfg.priceAlertPerm.Length == 0 || (stockCfg.sellCfg.priceAlertPerm.Length > 0 && permission.UserHasPermission(userIdString, stockCfg.sellCfg.priceAlertPerm))) && pad.alertPrice > 0 && pad.alertPrice < sid.price)
							{
								string itemName = ItemManager.FindItemDefinition(shortname).displayName.english;
								if (sd.cfg.customItems.TryGetValue(shortname, out var skins3) && skins3.TryGetValue(skin, out StockItemConfig sic))
									itemName = sic.displayName;
								if (stockCfg.marketLog.chatAlerts || stockCfg.marketLog.tabAlerts)
									AddStockAction(shopName, sd, StockActionType.Alert, sb, userIdString, itemName);
								if (stockCfg.sellCfg.priceAlertDiscordIntegration)
									DiscordCore?.Call("API_SendPrivateMessage", userIdString, Lang("AlertDiscordMessage", userIdString, itemName));
							}
						}
					}
			}
			Pool.FreeUnmanaged(ref sb);
            foreach (var cachePlayer in cache)
            {
	            ShoppyCache sc = cachePlayer.Value;
	            if (!sc.isOpened) continue;
	            if (sc.type != ShoppyType.Stock) continue;
	            if (sc.shopName != shopName) continue;
	            if (sc.stockCache.shortname.Length == 0) continue;
	            if (sc.stockCache.listingPage != ListingPageType.ServerSell) continue;
	            BasePlayer player = BasePlayer.FindByID(cachePlayer.Key);
	            if (!player || !player.IsConnected) continue;
	            UpdateServerSellUI(player);
	        }
        }
        
        private void DrawPriceGraph(BasePlayer player, int oldTimespan = 0)
        {
	        ShoppyCache sc = cache[player.userID];
	        StockData sd = data.stock[sc.shopName];
	        int savedTimespan = sc.stockCache.currentTimespan;
	        SellItemData ssCache = sd.prices.items[sc.stockCache.shortname][sc.stockCache.skin];
	        int recordsPerBar = Mathf.CeilToInt(savedTimespan / config.curr[sc.shopName].stockCfg.sellCfg.priceUpdateInterval);
	        ssCache.nextGraphUpdates[savedTimespan] = -recordsPerBar; 
	        ChartDrawThread cdt = new();
	        cdt.recordsPerBar = recordsPerBar;
	        float minPric = float.MaxValue;
	        float maxPric = float.MinValue;
	        foreach (var price in ssCache.priceHistory.Take(recordsPerBar * 200))
	        {
		        if (price > maxPric)
			        maxPric = price;
		        if (price < minPric)
			        minPric = price; 
	        }
	        cdt.priceHistory = ssCache.priceHistory;
	        cdt.targetImageId = ssCache.cachedGraphs.TryGetValue(savedTimespan, out var image) ? image.imageId : 0;
	        cdt.onProcessEnded = imgId =>
	        {
		        ssCache.cachedGraphs[savedTimespan] = new()
		        {
			        imageId = imgId,
			        minPrice = minPric,
			        maxPrice = maxPric,
		        };
		        SwitchGraphTimespan(player, savedTimespan, oldTimespan);
	        };
#if CARBON
	        cdt.Start();
	        Community.Runtime.Core.persistence.StartCoroutine(cdt.WaitFor());
#else
	        ServerMgr.Instance.StartCoroutine(cdt.WaitFor());
#endif
	        
        } 

        
        private void RollPrices(string shopName, string shortname, ulong skin)
        {
	        StockData sd = data.stock[shopName];
	        StockConfig stockCfg = config.curr[shopName].stockCfg;
            PriceCalculatorConfig pcc = stockCfg.sellCfg.priceCalc;
            SellItemConfig sic = sd.cfg.sellItems[shortname][skin];
            sd.prices.items.TryAdd(shortname, new());
            sd.prices.items[shortname].TryAdd(skin, new());
            SellItemData sid = sd.prices.items[shortname][skin];
            if (sic.minPrice == sic.maxPrice)
            {
	            sid.price = sic.minPrice;
                return;
            }
            float percentOfPrice = (sic.maxPrice - sic.minPrice) / 100f;
            int biggestChart = int.MinValue;

	        //Update graph data
            ServerSellConfig ssc = config.curr[shopName].stockCfg.sellCfg;
            foreach (var graph in ssc.priceChartTimespanPerms.Keys)
            {
	            if (graph > biggestChart)
		            biggestChart = graph;
	            if (!sid.nextGraphUpdates.ContainsKey(graph))
	            {
		            int updateInterval = Mathf.CeilToInt(graph / ssc.priceUpdateInterval);
		            sid.nextGraphUpdates.Add(graph, -updateInterval);
	            }
	            else
	            {
		            sid.nextGraphUpdates[graph]++;
	            }
            }
            if (biggestChart == int.MinValue)
	            biggestChart = 0;
            int maxBars = Mathf.CeilToInt(biggestChart / ssc.priceUpdateInterval) * 200;
            //Update price based on parent
            //TODO make that items sold that have parent shortname make sell item count to parent
            if (sic.priceParentShortname.Length > 0)
            {
                SellItemData parentCache = sd.prices.items[sic.priceParentShortname][sic.priceParentSkin];
                float totalChildrenPrice = parentCache.price + Core.Random.Range(sic.priceParentMinPrice, sic.priceParentMaxPrice);
                if (totalChildrenPrice < sic.minPrice)
                    totalChildrenPrice = sic.minPrice + Core.Random.Range(0f, percentOfPrice * pcc.priceFluctuation);
                else if (totalChildrenPrice > sic.maxPrice)
                    totalChildrenPrice = sic.maxPrice - Core.Random.Range(0f, percentOfPrice * pcc.priceFluctuation);
                sid.sellAmountHistory.Insert(0, sid.sellAmount);
                if (sid.sellAmountHistory.Count - maxBars > 0)
	                sid.sellAmountHistory.RemoveRange(maxBars, sid.sellAmountHistory.Count - maxBars);
                sid.sellAmount = 0;
                sid.priceHistory.Insert(0, sid.price);
                if (sid.priceHistory.Count - maxBars > 0)
	                sid.priceHistory.RemoveRange(maxBars, sid.priceHistory.Count - maxBars);
                sid.price = totalChildrenPrice;
                return;
            }
            sid.actionCount++;
            if (sid.penaltyLength > 0)
	            sid.penaltyLength--;
            //Check for new action type
            if (sid.actionCount >= sid.actionGoal)
            { 
                sid.actionCount = 0;
                sid.actionGoal = Core.Random.Range(pcc.sameActionsMin, pcc.sameActionsMax + 1);
                
                int chance = Core.Random.Range(0, 100);
                foreach (var priceBarrier in pcc.priceBarriers)
                {
                    if (sid.price >= sic.minPrice + percentOfPrice * priceBarrier.Key)
                    {
	                    if (chance < priceBarrier.Value)
		                    sid.isDecreasing = false;
                        else
	                        sid.isDecreasing = true;
                        break;
                    }
                }
            }
            
            int playerCount = BasePlayer.activePlayerList.Count;
            float onlinePlayerAmountMultiplier = 1;
            foreach (var onlineCount in pcc.sellAmountOnlineMultiplier)
	            if (playerCount > onlineCount.Key)
	            {
		            onlinePlayerAmountMultiplier = onlineCount.Value;
		            break;
	            }
            float percentageOfDefaultAmount = sic.dsac * onlinePlayerAmountMultiplier / 100f;
            float pentalityMultiplier = 1;
            foreach (var sellAmount in pcc.priceDropChart)
            {
	            if (sid.sellAmount > percentageOfDefaultAmount * sellAmount.Key)
	            {
		            pentalityMultiplier *= sellAmount.Value;
		            break;
	            }
            }
            if (pentalityMultiplier > 1)
	            sid.isDecreasing = true;
            
            float priceChange = 0;
            if (!sid.isDecreasing && sid.penaltyLength == 0)
            {
                float priceIncrease = Core.Random.Range(0f, percentOfPrice * pcc.priceFluctuation) * pentalityMultiplier;
                priceChange += priceIncrease;
            }
            else if (sid.isDecreasing)
            {
                float priceDecrease = Core.Random.Range(-percentOfPrice * pcc.priceFluctuation, 0f) * pentalityMultiplier;
                priceChange += priceDecrease;
            }

            float overrideMaxPrice = sic.maxPrice;
            foreach (var pentality in pcc.sellPricePenalty)
            {
                if (sid.sellAmount > percentageOfDefaultAmount * pentality.Key)
                {
                    float cachedMaxPrice = sic.maxPrice - percentOfPrice * pentality.Value.percentage;
                    if (sid.price > cachedMaxPrice)
	                    overrideMaxPrice = cachedMaxPrice;
                    sid.penaltyLength = pentality.Value.penaltyLength;
                    break;
                }
            }
            if (!sid.isDecreasing)
            {
                foreach (var goal in pcc.goalAchievedChart)
                {
                    if (sid.sellAmount <= percentageOfDefaultAmount * goal.Key)
                    {
                        priceChange *= goal.Value;
                        break;
                    }
                }
            }
            float multiplierChance = 0;
            foreach (var chance in pcc.multplierAmountChance)
            {
                if (sid.sellAmount >= percentageOfDefaultAmount * chance.Key)
                {
                    multiplierChance = chance.Value;
                    break;
                }
            }
            float summedPrice = sid.price + priceChange;
            
            if (sid.nextMultiplierEvent <= 0 && multiplierChance > 0)
            {
                bool positive = summedPrice < sic.minPrice + pcc.positiveMaxPrice * percentOfPrice && multiplierChance > Core.Random.Range(0f, 100f);
                bool negative = false;
                if (!positive)
	                negative = summedPrice > sic.minPrice + pcc.negativeMinPrice * percentOfPrice && multiplierChance > Core.Random.Range(0f, 100f);
                if (positive || negative)
                {
                    int sumWeight = 0;
                    foreach (var multiplier in pcc.events)
                    {
	                    bool eventNegative = multiplier.Value.min < 1;
	                    if ((negative && eventNegative) || (positive && !eventNegative))
		                    sumWeight += multiplier.Value.weight;
                    }
                    int rolledWeight = Core.Random.Range(0, sumWeight + 1);
                    sumWeight = 0;
                    foreach (var multiplier in pcc.events)
                    {
	                    bool eventNegative = multiplier.Value.min < 1;
	                    if ((negative && eventNegative) || (positive && !eventNegative))
	                    {
		                    sumWeight += multiplier.Value.weight;
		                    if (sumWeight < rolledWeight) continue;
                            sid.multiplierLength = Core.Random.Range(pcc.multiplierMinLength, pcc.multiplierMaxLength + 1);
                            sid.multiplier = Core.Random.Range(multiplier.Value.min, multiplier.Value.max);
                            sid.nextMultiplierEvent = pcc.minTimeDistance;
                            int percentage = sid.multiplier < 1 ? Mathf.FloorToInt((1f - sid.multiplier) * 100f) : Mathf.FloorToInt((sid.multiplier - 1f) * 100f);
                            string name = ItemManager.FindItemDefinition(shortname).displayName.english;
                            if (sd.cfg.customItems.TryGetValue(shortname, out var skins) && skins.TryGetValue(skin, out var ci))
	                            name = ci.displayName;
                            if (stockCfg.marketLog.chatDemands || stockCfg.marketLog.tabDemands)
                            {
	                            StringBuilder sb = Pool.Get<StringBuilder>();
	                            if (sid.multiplier < 1)
		                            AddStockAction(shopName, sd, StockActionType.Demand_Neg, sb, name, percentage.ToString());
	                            else
		                            AddStockAction(shopName,sd, StockActionType.Demand, sb, name, percentage.ToString());
	                            Pool.FreeUnmanaged(ref sb);
                            }
                            if (stockCfg.sellCfg.eventsDiscordChannelId.Length > 0)
                            {
	                            if ((stockCfg.sellCfg.eventsDiscordShowPositive && sid.multiplier >= 1) || (stockCfg.sellCfg.eventsDiscordShowNegative && sid.multiplier < 1))
		                            DiscordCore?.Call("API_SendMessage", stockCfg.sellCfg.eventsDiscordChannelId, Lang($"Event_{multiplier.Key}", null, name, percentage));
                            }
                            if (stockCfg.sellCfg.showOnChat)
                            {
	                            if ((stockCfg.sellCfg.eventsDiscordShowPositive && sid.multiplier >= 1) || (stockCfg.sellCfg.eventsDiscordShowNegative && sid.multiplier < 1))
									foreach (var player in BasePlayer.activePlayerList)
									    SendReply(player, Lang($"Event_{multiplier.Key}", player.UserIDString, name, percentage));
                            }
                            break;
	                    }
                    }
                }
            }
            else if (sid.nextMultiplierEvent > 0)
                sid.nextMultiplierEvent--;
            if (sid.multiplierLength > 0)
	            sid.multiplierLength--;
            else
	            sid.multiplier = 1;
            float totalPrice = summedPrice * sid.multiplier;
            if (totalPrice < sic.minPrice)
                totalPrice = sic.minPrice + Core.Random.Range(0f, percentOfPrice * pcc.priceFluctuation);
            else if (totalPrice > overrideMaxPrice)
                totalPrice = overrideMaxPrice - Core.Random.Range(0f, percentOfPrice * pcc.priceFluctuation);
            sid.sellAmountHistory.Insert(0, sid.sellAmount);
            if (sid.sellAmountHistory.Count - maxBars > 0)
	            sid.sellAmountHistory.RemoveRange(maxBars, sid.sellAmountHistory.Count - maxBars - 1);
            sid.sellAmount = 0;
            if (sid.priceHistory.Count - maxBars > 0)
	            sid.priceHistory.RemoveRange(maxBars, sid.priceHistory.Count - maxBars);
            sid.price = totalPrice;
            sid.priceHistory.Insert(0, sid.price);
            float pricePercentage = (totalPrice - sic.minPrice) / (overrideMaxPrice - sic.minPrice);
            sid.buyPrice = overrideMaxPrice * (ssc.buyFromServerMinPrice / 100f + (ssc.buyFromServerMaxPrice - ssc.buyFromServerMinPrice) / 100f * pricePercentage);
        }

        private string GetItemName(StockData sd, string shortname, ulong skin)
        {
	        if (skin == 0)
		        return ItemManager.FindItemDefinition(shortname).displayName.english;
		    return sd.cfg.customItems[shortname][skin].displayName;
        }

        private string GetStockItemName(StockConfig stockCfg, StockData sd, string userId, string shortname, ulong skin, StringBuilder sb)
        {
	        if (stockCfg.itemsNamesMultilingual)
		        return Lang(sb.Clear().Append("ItemName_").Append(shortname).Append('_').Append(skin).ToString(), userId);
	        if (skin == 0)
		        return ItemManager.FindItemDefinition(shortname).displayName.english;
			if (sd.cfg.customItems.TryGetValue(shortname, out var skins) && skins.TryGetValue(skin, out var cid))
				return cid.displayName;
		    return ItemManager.FindItemDefinition(shortname).displayName.english;
        }

        private void SpawnAndOpenContainer(BasePlayer player)
        {
	        ShoppyCache sc = cache[player.userID];
			if (sc.stockCache.haveOpenContainer) return;
	        LootContainer container;
	        if (!sc.stockCache.container || sc.stockCache.container.IsDestroyed)
	        {
		        container = GameManager.server.CreateEntity("assets/bundled/prefabs/modding/lootables/invisible/invisible_lootable_prefabs/invisible_crate_basic.prefab", new Vector3(0, Core.Random.Range(-200, -180), 0)) as LootContainer;
		        container.OwnerID = player.userID;
		        container.minSecondsBetweenRefresh = 0;
		        container.maxSecondsBetweenRefresh = 0;
		        container.destroyOnEmpty = false;
		        container.initialLootSpawn = false;
		        container.BlockPlayerItemInput = false;
		        container.panelName = "generic_resizable";
		        UnityEngine.Object.DestroyImmediate(container.GetComponent<Spawnable>());
		        container.Spawn();
		        foreach (var item in container.inventory.itemList.ToArray())
		        {
			        item.GetHeldEntity()?.Kill();
			        item.DoRemove();
		        }
		        sc.stockCache.container = container;
	        }
	        else
		        container = sc.stockCache.container; 
	        sc.stockCache.haveOpenContainer = true; 
		    container.inventory.canAcceptItem = (item, slot) => sc.stockCache.containerType != ContainerType.BuyOffer;
		    if (sc.stockCache.containerType == ContainerType.SellInventory)
			    container.inventory.capacity = 24;
		    else
				container.inventory.capacity = 1;
		    if (sc.stockCache.containerType == ContainerType.SellOffer)
		    {
			    sc.stockCache.savedAction = () => UpdateSellOfferUI(player, container.inventory);
			    container.inventory.onDirty += sc.stockCache.savedAction;
		    }
	        else if (sc.stockCache.containerType == ContainerType.SellInventory)
	        {
		        sc.stockCache.savedAction = () => UpdateSellInventoryUI(player, container.inventory);
		        container.inventory.onDirty += sc.stockCache.savedAction;
	        }
	        else if (sc.stockCache.containerType == ContainerType.CurrencyDeposit)
	        {
		        sc.stockCache.savedAction = () => UpdateCurrencyDepositUI(player, container.inventory);
		        container.inventory.onDirty += sc.stockCache.savedAction;
	        }
	        timer.Once(0.1f, () => {
		        player.inventory.loot.AddContainer(container.inventory);
		        player.inventory.loot.entitySource = container;
		        player.inventory.loot.PositionChecks = false;
		        player.inventory.loot.MarkDirty();
		        player.inventory.loot.SendImmediate();
		        string invType = sc.stockCache.containerType == ContainerType.SellInventory ? "generic_resizable" : "mailboxentry";
		        player.ClientRPC(RpcTarget.Player("RPC_OpenLootPanel", player), invType);
	        });
        }

        private void SetOfferPrice(BasePlayer player, float price)
        {
	        ShoppyCache sc = cache[player.userID];
	        StockConfig stockCfg = config.curr[sc.shopName].stockCfg;
	        Item item = sc.stockCache.container.inventory.GetSlot(0);
	        if (sc.stockCache.listingPage == ListingPageType.SellOffer && item == null) return;
	        string shortname = sc.stockCache.listingPage == ListingPageType.BuyOffer ? sc.stockCache.shortname : item.info.shortname;
	        ulong skin = sc.stockCache.listingPage == ListingPageType.BuyOffer ? sc.stockCache.skin : item.skin;
	        sc.stockCache.offerPrice = 0;
	        if (stockCfg.maxItemPriceOverrides.TryGetValue(shortname, out var skins1) && skins1.TryGetValue(skin, out float maxPrice) && price > maxPrice)
		        sc.stockCache.offerPrice = maxPrice;
	        else if (stockCfg.maxItemPrice > 0 && price > stockCfg.maxItemPrice)
		        sc.stockCache.offerPrice = stockCfg.maxItemPrice;
	        if (stockCfg.minItemPriceOverrides.TryGetValue(shortname, out var skins2) && skins2.TryGetValue(skin, out float minPrice) && price < minPrice)
		        sc.stockCache.offerPrice = minPrice;
	        else if (price < stockCfg.minItemPrice)
		        sc.stockCache.offerPrice = stockCfg.minItemPrice;
	        if (sc.stockCache.offerPrice == 0)
		        sc.stockCache.offerPrice = price;
        }

        private void TryAddBuySellRequest(BasePlayer player)
        {
	        ShoppyCache sc = cache[player.userID];
	        StockConfig stockCfg = config.curr[sc.shopName].stockCfg;
	        StockData sd = data.stock[sc.shopName];
	        float requiredMoney = 0;
	        bool isBuyOffer = sc.stockCache.listingPage == ListingPageType.BuyOffer;
	        Item targetItem = null;
	        if (!isBuyOffer)
				targetItem = sc.stockCache.container.inventory.itemList[0];
	        int amount = isBuyOffer ? sc.stockCache.stockActionAmount : targetItem.amount;
	        List<TimeListingConfig> validConfigs = Pool.Get<List<TimeListingConfig>>();
	        validConfigs.Clear();
	        TimeListingConfig tlc = null;
	        if (isBuyOffer)
	        {
		        foreach (var offer in stockCfg.buyOfferTimes)
			        if (offer.reqPerm.Length == 0 || permission.UserHasPermission(player.UserIDString, offer.reqPerm))
				        validConfigs.Add(offer);
		        tlc = validConfigs[sc.stockCache.buyOfferTaxIndex];
	        }
	        else
	        {
		        foreach (var offer in stockCfg.sellOfferTimes)
			        if (offer.reqPerm.Length == 0 || permission.UserHasPermission(player.UserIDString, offer.reqPerm))
				        validConfigs.Add(offer);
		        tlc = validConfigs[sc.stockCache.sellOfferTaxIndex];
	        }

	        float tax = tlc.taxAmount;
	        foreach (var perm in tlc.taxAmountPerms)
		        if (perm.Value < tax && permission.UserHasPermission(player.UserIDString, perm.Key))
			        tax = perm.Value;
	        float price = sc.stockCache.offerPrice;
	        float finalPrice = isBuyOffer ? amount * price + (amount * price / 100f * tax) : (amount * price / 100f * tax);
	        if (GetPlayerCurrency(player, sc.shopName) < finalPrice)
	        {
		        SendEffect(player, StringKeys.Effect_Denied);
		        ShowPopUp(player, Lang("NotEnoughCurrency", player.UserIDString));
		        return;
	        }
	        int playerListingCount = GetPlayerListingCount(player.userID, sc.shopName, isBuyOffer);
	        int playerListingLimit = GetPlayerListingLimit(player.UserIDString, sc.shopName, isBuyOffer);
	        if (playerListingLimit <= playerListingCount)
	        {
		        SendEffect(player, StringKeys.Effect_Denied);
		        ShowPopUp(player, Lang("ListingLimitReached", player.UserIDString));
		        return;
	        }
	        string shortname = isBuyOffer ? sc.stockCache.shortname : targetItem.info.shortname;
	        ulong skin = isBuyOffer ? sc.stockCache.skin : targetItem.skin;
	        StockItemConfig sic = null;
	        bool isListedCustomSkin = sd.cfg.customItems.TryGetValue(shortname, out var skins) && skins.TryGetValue(skin, out sic);
	        if (!isBuyOffer && stockCfg.listingLockNonListedSkins && targetItem.skin != 0)
	        {
		        if (!isListedCustomSkin)
		        {
			        SendEffect(player, StringKeys.Effect_Denied);
			        ShowPopUp(player, Lang("CannotListSkinnedItems", player.UserIDString));
			        return;
		        }
	        }
			if (!TakeCurrency(sc.shopName, player, finalPrice)) return;
	        DateTime listingEnd = DateTime.Now.AddHours(tlc.listingTime);
	        if (tlc.listingTime == 24.7f)
		        listingEnd = DateTime.MaxValue;
	        else if (tlc.listingTime < 0)
		        listingEnd = DateTime.MinValue;

	        ItemData listingItem;
	        if (isBuyOffer)
	        {
		        ItemDefinition def = ItemManager.FindItemDefinition(sc.stockCache.shortname);
		        listingItem = new ItemData
		        {
			        id = def.itemid,
			        amount = amount,
			        skin = sc.stockCache.skin
		        };
		        if (isListedCustomSkin)
			        listingItem.name = sic.buyName;
	        }
	        else
	        {
		        listingItem = ItemToData(targetItem);
		        targetItem.GetHeldEntity()?.Kill();
		        targetItem.DoRemove();
	        }
	        Dictionary<string, Dictionary<ulong, List<StockListingData>>> listings = isBuyOffer ? sd.listings.buyOffers : sd.listings.sellOffers;
	        ulong listingKeySkin = isListedCustomSkin ? skin : 0;
	        listings.TryAdd(shortname, new());
	        listings[shortname].TryAdd(listingKeySkin, new());
	        listings[shortname][listingKeySkin].Add(new()
	        {
		        userId = player.userID,
		        userName = player.displayName.Replace("\"", "").Replace("\\", ""),
		        price = price,
		        listingEndTime = listingEnd,
		        item = listingItem
	        });
	        if (listingEnd != DateTime.MinValue && listingEnd != DateTime.MaxValue && !timers.ContainsKey("offerEnd"))
		        StartCheckingForStockOfferEnds();
	        TryUpdateListingUI(sc.shopName, shortname, skin, isBuyOffer);
	        player.EndLooting();
	        StringBuilder sb = Pool.Get<StringBuilder>();
	        string name;
	        if (!string.IsNullOrEmpty(listingItem.name))
		        name = listingItem.name;
	        else
		        name = GetStockItemName(stockCfg, sd, player.UserIDString, shortname, skin, sb);
	        CurrencyConfig currCfg = config.curr[sc.shopName].currCfg;
	        string amountString = FormatAmount(currCfg, listingItem.amount, sb);
	        string priceString = FormatCurrency(currCfg, price, sb);
	        if (isBuyOffer && (stockCfg.marketLog.chatBuyOffers || stockCfg.marketLog.tabBuyOffers))
				AddStockAction(sc.shopName, sd, StockActionType.BuyOffer, sb, player.displayName.Replace("\"", "").Replace("\\", ""), amountString, name, priceString);
	        else if (!isBuyOffer && (stockCfg.marketLog.chatSellOffers || stockCfg.marketLog.tabSellOffers))
		        AddStockAction(sc.shopName,sd, StockActionType.SellOffer, sb, player.displayName.Replace("\"", "").Replace("\\", ""), amountString, name, priceString);
	        Pool.FreeUnmanaged(ref sb);
	        Pool.FreeUnmanaged(ref validConfigs);
	        SendEffect(player, StringKeys.Effect_Collect);
	        ShowPopUp(player, Lang("CreatedListing", player.UserIDString));
	        LogToConsole("AddedNewListing", player.displayName, player.userID, isBuyOffer, shortname, skin, listingItem.amount, name, sc.shopName);
	        if (isBuyOffer)
				Interface.CallHook("OnMarketBuyRequestCreated", player, sc.shopName, shortname, skin, price, listingItem.amount);
	        else
		        Interface.CallHook("OnMarketSellRequestCreated", player, sc.shopName, shortname, skin, price, listingItem.amount);
	        sc.stockCache.offerPrice = 0;
	        sc.stockCache.stockActionAmount = 0;
	        CuiHelper.DestroyUi(player, "ShoppyStockUI_Inventory");
        }

        private int GetPlayerListingCount(ulong userId, string shopName, bool buyOffers)
        {
	        int listingCount = 0;
	        if (buyOffers)
	        {
		        foreach (var shortname in data.stock[shopName].listings.buyOffers.Values)
			        foreach (var skin in shortname)
				        foreach (var listing in skin.Value)
					        if (listing.userId == userId)
						        listingCount++;
	        }
	        else
	        {
		        foreach (var shortname in data.stock[shopName].listings.sellOffers.Values)
			        foreach (var skin in shortname)
				        foreach (var listing in skin.Value)
					        if (listing.userId == userId)
						        listingCount++;
	        }
	        return listingCount;
        }
        
        private int GetPlayerListingLimit(string userId, string shopName, bool buyOffers)
        {
	        int listingLimit = buyOffers ? config.curr[shopName].stockCfg.listingLimits.buyOffers : config.curr[shopName].stockCfg.listingLimits.sellOffers;
	        foreach (var perm in config.curr[shopName].stockCfg.listingLimitPerms)
	        {
		        if (permission.UserHasPermission(userId, perm.Key))
		        {
			        if (buyOffers && listingLimit < perm.Value.buyOffers)
				        listingLimit = perm.Value.buyOffers;
			        else if (!buyOffers && listingLimit < perm.Value.sellOffers)
				        listingLimit = perm.Value.sellOffers;
		        }
	        }
	        return listingLimit;
        }

        private void TryPurchaseFromStockPlayer(BasePlayer player)
        {
	        ShoppyCache sc = cache[player.userID];
	        StringBuilder sb = Pool.Get<StringBuilder>();
	        StockData sd = data.stock[sc.shopName];
	        CurrencyConfig currCfg = config.curr[sc.shopName].currCfg;
	        StockConfig stockCfg = config.curr[sc.shopName].stockCfg;
	        StockListingData sld;
	        bool isBuyOffer = sc.stockCache.listingPage == ListingPageType.BuyOffer;
	        List<StockListingData> sldTemp = Pool.Get<List<StockListingData>>();
	        sldTemp.Clear();
	        var offers = isBuyOffer ? sd.listings.buyOffers[sc.stockCache.shortname][sc.stockCache.skin] : sd.listings.sellOffers[sc.stockCache.shortname][sc.stockCache.skin];
	        if (isBuyOffer)
	        {
		        foreach (var listing in offers)
		        {
			        if (sc.stockCache.hideMyListings && listing.userId == player.userID) continue;
			        if (listing.isHidden && listing.userId != player.userID && sc.stockCache.listingCode != listing.customAccessCode) continue;
			        sldTemp.Add(listing);
		        }
		        sld = sldTemp.OrderByDescending(x => x.price).ToArray()[sc.stockCache.selectedListingIndex];
	        }
	        else
	        {
		        foreach (var listing in offers)
		        {
			        if (sc.stockCache.hideMyListings && listing.userId == player.userID) continue;
			        if (listing.isHidden && listing.userId != player.userID && sc.stockCache.listingCode != listing.customAccessCode) continue;
			        sldTemp.Add(listing);
		        }
		        sld = sldTemp.OrderBy(x => x.price).ToArray()[sc.stockCache.selectedListingIndex];
	        }
	        Pool.FreeUnmanaged(ref sldTemp);
	        float price = sld.price * sc.stockCache.stockActionAmount;
	        if (isBuyOffer)
	        {
		        int itemCount = 0;
		        foreach (var item in player.inventory.containerMain.itemList)
			        if (item.info.itemid == sld.item.id && item.skin == sld.item.skin)
				        itemCount += item.amount;
		        foreach (var item in player.inventory.containerBelt.itemList)
			        if (item.info.itemid == sld.item.id && item.skin == sld.item.skin)
				        itemCount += item.amount;
		        if (itemCount < sc.stockCache.stockActionAmount)
		        {
			        Pool.FreeUnmanaged(ref sb);
			        return;
		        }
		        TakeItems(player, sld.item.id, sld.item.skin, sc.stockCache.stockActionAmount, stockCfg, sld.userId);
		        int savedAmount = sc.stockCache.stockActionAmount;
		        sld.item.amount -= savedAmount;
		        if (sld.item.amount == 0)
			        offers.Remove(sld);
		        AddCurrency(sc.shopName, player.userID, price, player: player);
		        SwitchStockDetailsCategory(player, sc.stockCache.listingPage);
		        string name;
		        if (!string.IsNullOrEmpty(sld.item.name))
			        name = sld.item.name;
		        else
			        name = GetStockItemName(stockCfg, sd, player.UserIDString, sc.stockCache.shortname, sc.stockCache.skin, sb);
		        SendEffect(player, StringKeys.Effect_Money);
		        string priceString = FormatCurrency(currCfg, price, sb);
		        string amountString = FormatAmount(currCfg, savedAmount, sb);
		        sc.stockCache.stockActionAmount = 0;
		        ShowPopUp(player, Lang("BuyOfferCompleted", player.UserIDString, amountString, name, priceString));
		        LogToConsole("BuyOfferCompleted", player.displayName, player.userID, sc.shopName, sld.userId, amountString, name, priceString);
		        Interface.CallHook("OnMarketItemSold", player, sc.shopName, sld.userId, price, name);
		        if (stockCfg.marketLog.chatSoldItems || stockCfg.marketLog.tabSoldItems)
					AddStockAction(sc.shopName, sd, StockActionType.SoldItem, sb, stockCfg.marketLog.showBuyerNickname ? player.displayName.Replace("\"", "").Replace("\\", "") : "Hidden", amountString, name, sld.userName, priceString);
	        }
	        else
	        {
		        if (GetPlayerCurrency(player, sc.shopName) < price)
		        {
			        Pool.FreeUnmanaged(ref sb);
			        return;
		        }
		        if (!TakeCurrency(sc.shopName, player, price))
		        {
			        Pool.FreeUnmanaged(ref sb);
			        return;
		        }
		        AddCurrency(sc.shopName, sld.userId, price);
		        int amount = sc.stockCache.stockActionAmount;
		        ItemDefinition def = ItemManager.FindItemDefinition(sld.item.id);
		        while (amount > 0)
		        {
			        int giveAmount = amount > def.stackable ? def.stackable : amount;
			        Item item = sld.item.ToItem();
			        item.amount = giveAmount;
			        amount -= giveAmount;
			        if (stockCfg.itemsGoToRedeem)
				        RedeemStorageAPI?.Call("AddItem", player.userID.Get(), stockCfg.redeemInventoryName, item, amount == 0);
			        else
				        player.GiveItem(item);
		        }
		        sld.item.amount -= sc.stockCache.stockActionAmount;
		        string amountString = FormatAmount(currCfg, sc.stockCache.stockActionAmount, sb);
		        if (sld.item.amount == 0)
			        offers.Remove(sld);
		        SwitchStockDetailsCategory(player, sc.stockCache.listingPage);
		        
		        string name;
		        if (!string.IsNullOrEmpty(sld.item.name))
			        name = sld.item.name;
		        else
			        name = GetStockItemName(stockCfg, sd, player.UserIDString, sc.stockCache.shortname, sc.stockCache.skin, sb);
		        SendEffect(player, StringKeys.Effect_Money);
		        string priceString = FormatCurrency(currCfg, price, sb);
		        sc.stockCache.stockActionAmount = 0;
		        ShowPopUp(player, Lang("SellOfferCompleted", player.UserIDString, amountString, name, priceString));
		        LogToConsole("SellOfferCompleted", player.displayName, player.userID, sc.shopName, sld.userId, amountString, name, priceString);
		        Interface.CallHook("OnMarketItemPurchased", player, sc.shopName, sld.userId, price, name);
		        if (stockCfg.marketLog.chatPurchasedItems || stockCfg.marketLog.tabPurchasedItems)
					AddStockAction(sc.shopName, sd, StockActionType.PurchasedItem, sb, stockCfg.marketLog.showBuyerNickname ? player.displayName.Replace("\"", "").Replace("\\", "") : "Hidden", amountString, name, sld.userName, priceString);
	        }
	        Pool.FreeUnmanaged(ref sb);
        }

        private bool TakeItems(BasePlayer player, int itemId, ulong skin, int amount, StockConfig stockCfg = null, ulong redeemIdOwner = 0)
        {
	        int itemCount = amount;
	        foreach (var item in player.inventory.containerMain.itemList.ToArray())
	        {
		        if (item.info.itemid != itemId || item.skin != skin) continue;
		        if (item.amount > itemCount)
		        {
			        Item splitItem = item.SplitItem(itemCount);
			        itemCount = 0;
			        if (redeemIdOwner > 0)
				        RedeemStorageAPI?.Call("AddItem", redeemIdOwner, stockCfg.redeemInventoryName, splitItem, itemCount == 0);
			        splitItem.GetHeldEntity()?.Kill();
			        splitItem.DoRemove();
		        }
		        else
		        {
			        itemCount -= item.amount;
			        if (redeemIdOwner > 0)
				        RedeemStorageAPI?.Call("AddItem", redeemIdOwner, stockCfg.redeemInventoryName, item, itemCount == 0);
			        item.GetHeldEntity()?.Kill();
			        item.DoRemove();
		        }
		        if (itemCount == 0) break;
	        }
	        if (itemCount == 0) return true;
	        foreach (var item in player.inventory.containerBelt.itemList.ToArray())
	        {
		        if (item.info.itemid != itemId || item.skin != skin) continue;
		        if (item.amount > itemCount)
		        {
			        itemCount = 0;
			        Item splitItem = item.SplitItem(itemCount);
			        if (redeemIdOwner > 0)
				        RedeemStorageAPI?.Call("AddItem", redeemIdOwner, stockCfg.redeemInventoryName, splitItem, true);
			        splitItem.GetHeldEntity()?.Kill();
			        splitItem.DoRemove();
		        }
		        else
		        {
			        itemCount -= item.amount;
			        if (redeemIdOwner > 0)
				        RedeemStorageAPI?.Call("AddItem", redeemIdOwner, stockCfg.redeemInventoryName, item, itemCount == 0);
			        item.GetHeldEntity()?.Kill();
			        item.DoRemove();
		        }
		        if (itemCount == 0) break;
	        }
	        if (itemCount == 0) return true;
	        return false;
        }
        
        private bool TakeItems(BasePlayer player, string shortname, ulong skin, int amount)
        {
	        ItemDefinition def = ItemManager.FindItemDefinition(shortname);
	        if (!def) return false;
	        return TakeItems(player, def.itemid, skin, amount);
        }

        private void TryAddToBank(BasePlayer player, bool fromInventory = false)
        {
	        ShoppyCache sc = cache[player.userID];
	        StringBuilder sb = Pool.Get<StringBuilder>();
	        StockData sd = data.stock[sc.shopName];
	        StockConfig stockCfg = config.curr[sc.shopName].stockCfg;
	        sd.playerData.players.TryAdd(player.userID, new());
	        StockPlayerData spd = sd.playerData.players[player.userID];
	        if (fromInventory && spd.bankItems.Count == 0)
	        {
		        SendEffect(player, StringKeys.Effect_Denied);
		        ShowPopUp(player, Lang("NoItemsInBank", player.UserIDString));
		        Pool.FreeUnmanaged(ref sb);
		        return;
	        }
	        if (fromInventory)
	        {
		        bool changed = false;
		        foreach (var item in player.inventory.containerMain.itemList.ToArray())
		        {
			        if (spd.bankItems.TryGetValue(item.info.shortname, out var skins) && skins.TryGetValue(item.skin, out var sbdData))
			        {
				        if (AddItemToBank(player, sbdData, item, stockCfg, sd, sb, false))
					        changed = true;
			        }
		        }
		        foreach (var item in player.inventory.containerBelt.itemList.ToArray())
		        {
			        if (spd.bankItems.TryGetValue(item.info.shortname, out var skins) && skins.TryGetValue(item.skin, out var sbdData))
			        {
				        if (AddItemToBank(player, sbdData, item, stockCfg, sd, sb, false))
					        changed = true;
			        }
		        }
		        if (changed)
		        {
			        SendEffect(player, StringKeys.Effect_Collect);
			        ShowPopUp(player, Lang("ItemSubmitedSuccessfully", player.UserIDString));
			        using CUI cui = new CUI(CuiHandler);
			        DrawBankItems(player, cui, sb, stockCfg, sd, false);
			        cui.v2.SendUi(player);
		        }
	        }
	        else
	        {
		        if (!sc.stockCache.container && sc.stockCache.container.IsDestroyed) return;
		        if (sc.stockCache.container.inventory.itemList.Count == 0) return;
		        Item submitItem = sc.stockCache.container.inventory.itemList[0];
		        if (sd.cfg.sellItems.TryGetValue(submitItem.info.shortname, out var skins) && skins.ContainsKey(submitItem.skin))
		        {
			        if (!spd.bankItems.TryGetValue(submitItem.info.shortname, out var skins2) || !skins2.ContainsKey(submitItem.skin))
			        {
						int maxItemsCount = GetBankMaxIndividualItemCount(player, stockCfg);
				        if (maxItemsCount > 0 && GetPlayerBankItems(player, sd) >= maxItemsCount)
				        {
					        SendEffect(player, StringKeys.Effect_Denied);
					        ShowPopUp(player, Lang("TooManyIndividualItems", player.UserIDString));
					        Pool.FreeUnmanaged(ref sb);
					        return;
				        }
			        }
			        spd.bankItems.TryAdd(submitItem.info.shortname, new());
			        spd.bankItems[submitItem.info.shortname].TryAdd(submitItem.skin, new());
			        StockBankData sbd = spd.bankItems[submitItem.info.shortname][submitItem.skin];
			        if (AddItemToBank(player, sbd, submitItem, stockCfg, sd, sb, true))
			        {
				        using CUI cui = new CUI(CuiHandler);
				        DrawBankItems(player, cui, sb, stockCfg, sd, false);
				        cui.v2.SendUi(player);
			        }
		        }
	        }
	        Pool.FreeUnmanaged(ref sb);
        }

        private bool AddItemToBank(BasePlayer player, StockBankData sbd, Item item, StockConfig stockCfg, StockData sd, StringBuilder sb, bool message)
        {
	        int maxItems = GetBankMaxItemCount(player, stockCfg, item.info.shortname, item.skin);
	        if (!string.IsNullOrEmpty(item.name))
	        {
		        sbd.displayName = item.name;
		        sbd.itemName = item.name;
	        }
	        else
		        sbd.displayName = GetStockItemName(stockCfg, sd, player.UserIDString, item.info.shortname, item.skin, sb);
	        if (maxItems > 0 && sbd.amount + item.amount > maxItems)
	        {
		        int difference = maxItems - sbd.amount;
		        if (difference == 0)
		        {
			        if (message)
			        {
				        SendEffect(player, StringKeys.Effect_Denied);
				        ShowPopUp(player, Lang("TooManyItems", player.UserIDString));
			        }
			        return false;
		        }
		        sbd.amount += difference;
		        item.amount -= difference;
		        item.MarkDirty();
		        LogToConsole("ItemAddedToBank", player.displayName, player.userID, difference, item.info.displayName.english, item.skin);
		        if (message)
		        {
			        SendEffect(player, StringKeys.Effect_Collect);
			        ShowPopUp(player, Lang("ItemSubmitedSuccessfully", player.UserIDString));
		        }
		        return true;
			}
	        sbd.amount += item.amount;
	        item.GetHeldEntity()?.Kill();
	        item.DoRemove();
	        LogToConsole("ItemAddedToBank", player.displayName, player.userID, item.amount, item.info.displayName.english, item.skin);
	        if (message)
	        {
		        SendEffect(player, StringKeys.Effect_Collect);
		        ShowPopUp(player, Lang("ItemSubmitedSuccessfully", player.UserIDString));
	        }
	        return true;
        }

        private int GetBankMaxIndividualItemCount(BasePlayer player, StockConfig stockCfg)
        {
	        int maxLimit = stockCfg.bankDefaultLimits.maxIndividualItems;
			if (maxLimit == 0) 
				return maxLimit;
	        foreach (var perm in stockCfg.bankPermLimits)
	        {
		        if (perm.Value.maxIndividualItems > maxLimit &&
		            permission.UserHasPermission(player.UserIDString, perm.Key))
			        maxLimit = perm.Value.maxIndividualItems;
	        }
	        return maxLimit;
        }

        private int GetPlayerBankItems(BasePlayer player, StockData sd)
        {
	        int itemCount = 0;
	        foreach (var item in sd.playerData.players[player.userID].bankItems.Values)
		        itemCount += item.Count;
	        return itemCount;
        }

        private int GetBankMaxItemCount(BasePlayer player, StockConfig stockCfg, string shortname, ulong skin)
        {
	        ItemDefinition def = ItemManager.FindItemDefinition(shortname);
	        if (!def) return 0;
	        float thisLimit = stockCfg.bankDefaultLimits.maxStacksPerItem;
	        if (stockCfg.bankDefaultLimits.maxAmountOverride.TryGetValue(shortname, out var skins) && skins.TryGetValue(skin, out float skinCfg))
		        thisLimit = skinCfg;
	        foreach (var perm in stockCfg.bankPermLimits)
	        {
		        if (!permission.UserHasPermission(player.UserIDString, perm.Key)) continue;
		        if (perm.Value.maxStacksPerItem > thisLimit)
			        thisLimit = perm.Value.maxStacksPerItem;
		        if (perm.Value.maxAmountOverride.TryGetValue(shortname, out var skins2) && skins2.TryGetValue(skin, out float skinCfg2))
			        thisLimit = skinCfg2;
	        }
	        return Mathf.RoundToInt(def.stackable * thisLimit);
        }

        private void WithdrawFromBank(BasePlayer player)
        {
	        ShoppyCache sc = cache[player.userID];
	        int amount = sc.stockCache.bankAmount;
	        if (amount <= 0) return;
	        StockConfig stockCfg = config.curr[sc.shopName].stockCfg;
	        StockData sd = data.stock[sc.shopName];
	        StockPlayerData spd = sd.playerData.players[player.userID];
	        if (!spd.bankItems.TryGetValue(sc.stockCache.bankShortname, out var skins) || !skins.TryGetValue(sc.stockCache.bankSkin, out var sbd)) return;
	        StringBuilder sb = Pool.Get<StringBuilder>();
	        if (sbd.amount < amount)
		        amount = sbd.amount;
	        int cachedAmount = amount;
	        ItemDefinition def = ItemManager.FindItemDefinition(sc.stockCache.bankShortname);
	        while (amount > 0)
	        {
		        int giveAmount = amount > def.stackable ? def.stackable : amount;
		        Item item = ItemManager.Create(def, giveAmount, sc.stockCache.bankSkin);
		        if (!string.IsNullOrEmpty(sbd.itemName))
			        item.name = sbd.itemName;
		        sbd.amount -= giveAmount;
		        amount -= giveAmount;
		        if (stockCfg.itemsGoToRedeem)
			        RedeemStorageAPI?.Call("AddItem", player.userID.Get(), stockCfg.redeemInventoryName, item, amount == 0);
		        else
			        player.GiveItem(item);
	        }
	        if (sbd.amount == 0)
	        {
		        spd.bankItems[sc.stockCache.bankShortname].Remove(sc.stockCache.bankSkin);
		        if (spd.bankItems[sc.stockCache.bankShortname].Count == 0)
			        spd.bankItems.Remove(sc.stockCache.bankShortname);
	        }
	        string name;
	        if (!string.IsNullOrEmpty(sbd.displayName))
		        name = sbd.displayName;
	        else
				name = GetStockItemName(stockCfg, sd, player.UserIDString, sc.stockCache.bankShortname, sc.stockCache.bankSkin, sb);
	        SendEffect(player, StringKeys.Effect_Collect);
	        LogToConsole("WithdrawedFromBank", player.displayName, player.userID, cachedAmount, name);
	        ShowPopUp(player, Lang("ItemsWithdrawedFromBank", player.UserIDString, cachedAmount, name));
	        using CUI cui = new CUI(CuiHandler);
	        DrawBankItems(player, cui, sb, stockCfg, sd, false);
	        cui.v2.SendUi(player);
	        Pool.FreeUnmanaged(ref sb);
        }

        private void SellItemsToStock(BasePlayer player, bool fromBank)
        {
	        ShoppyCache sc = cache[player.userID];
	        StockConfig stockCfg = config.curr[sc.shopName].stockCfg;
	        StockData sd = data.stock[sc.shopName];
			float salePriceMult = 1;
			float sumAllPrice = 0;
			foreach (var perm in stockCfg.sellCfg.sellPriceMultipliers)
				if (perm.Value > salePriceMult && permission.UserHasPermission(player.UserIDString, perm.Key))
					salePriceMult = perm.Value;
			if (BonusCore != null)
				salePriceMult += BonusCore.Call<float>("GetBonus", player, "Other_Price");
			bool anything = false;
			if (fromBank)
			{
				if (!sd.playerData.players.TryGetValue(player.userID, out var spd)) return;
				if (!spd.bankItems.TryGetValue(sc.stockCache.shortname, out var skins)) return;	
				if (!skins.TryGetValue(sc.stockCache.skin, out var sbd)) return;
				SellItemData sid = sd.prices.items[sc.stockCache.shortname][sc.stockCache.skin];
				SellItemConfig sic = sd.cfg.sellItems[sc.stockCache.shortname][sc.stockCache.skin];
				int sellAmount = sbd.amount;
				if (sellAmount < sic.minSellAmount)
				{
					SendEffect(player, StringKeys.Effect_Denied);
					ShowPopUp(player, Lang("TooLessToSell", player.UserIDString));
					return;
				}
				if (sic.maxSellAmount > -1)
				{
					int itemsSoldLimit = GetPlayerDailySoldItemLimit(player.UserIDString, sic);
					if (itemsSoldLimit == 0)
					{
						SendEffect(player, StringKeys.Effect_Denied);
						ShowPopUp(player, Lang("NotAllowedToSell", player.UserIDString));
						return;
					}
					int itemsSold = GetPlayerDailySoldItems(player.userID, sd, sc.stockCache.shortname, sc.stockCache.skin);
					if (itemsSold >= itemsSoldLimit)
					{
						SendEffect(player, StringKeys.Effect_Denied);
						ShowPopUp(player, Lang("SellLimitReached", player.UserIDString));
						return;
					}
					int remainingToSell = itemsSoldLimit - itemsSold;
					if (sellAmount > remainingToSell)
						sellAmount = remainingToSell;
				}
				float sumPrice = sid.price * sellAmount;
				sumAllPrice += sumPrice;
				if (salePriceMult > 1 && (!stockCfg.sellCfg.sellPriceMultBlacklist.TryGetValue(sc.stockCache.shortname, out var skins2) || !skins2.Contains(sc.stockCache.skin)))
				{
					float bonus = sumPrice * salePriceMult - sumPrice;
					sumAllPrice += bonus;
				}

				sd.stats.playerDailySoldItems.TryAdd(player.userID, new());
				sd.stats.playerDailySoldItems[player.userID].TryAdd(dateString, new());
				sd.stats.playerDailySoldItems[player.userID][dateString].TryAdd(sc.stockCache.shortname, new());
				sd.stats.playerDailySoldItems[player.userID][dateString][sc.stockCache.shortname].TryAdd(sc.stockCache.skin, 0);
				sd.stats.playerDailySoldItems[player.userID][dateString][sc.stockCache.shortname][sc.stockCache.skin] += sellAmount;
				sid.sellAmount += sellAmount;
				sid.amountAvailable += Mathf.FloorToInt(sellAmount * (stockCfg.sellCfg.buyFromServerPercentage / 100f));
				sbd.amount -= sellAmount;
				if (sbd.amount == 0)
				{
					skins.Remove(sc.stockCache.skin);
					if (skins.Count == 0)
						spd.bankItems.Remove(sc.stockCache.shortname);
				}
			}
			else
			{
				foreach (var item in sc.stockCache.container.inventory.itemList.ToArray())
				{
					if (!sd.prices.items.TryGetValue(item.info.shortname, out var skins) || !skins.ContainsKey(item.skin)) continue;
					SellItemData sid = sd.prices.items[item.info.shortname][item.skin];
					SellItemConfig sic = sd.cfg.sellItems[item.info.shortname][item.skin];
					int sellAmount = item.amount;
					if (sellAmount < sic.minSellAmount)
					{
						SendEffect(player, StringKeys.Effect_Denied);
						ShowPopUp(player, Lang("TooLessToSell", player.UserIDString));
						return;
					}
					if (sic.maxSellAmount > -1)
					{
						int itemsSoldLimit = GetPlayerDailySoldItemLimit(player.UserIDString, sic);
						if (itemsSoldLimit == 0)
						{
							SendEffect(player, StringKeys.Effect_Denied);
							ShowPopUp(player, Lang("NotAllowedToSell", player.UserIDString));
							continue;
						}
						int itemsSold = GetPlayerDailySoldItems(player.userID, sd, item.info.shortname, item.skin);
						if (itemsSold >= itemsSoldLimit)
						{
							SendEffect(player, StringKeys.Effect_Denied);
							ShowPopUp(player, Lang("SellLimitReached", player.UserIDString));
							continue;
						}
						int remainingToSell = itemsSoldLimit - itemsSold;
						if (sellAmount > remainingToSell)
							sellAmount = remainingToSell;
					}
					sd.stats.playerDailySoldItems.TryAdd(player.userID, new());
					sd.stats.playerDailySoldItems[player.userID].TryAdd(dateString, new());
					sd.stats.playerDailySoldItems[player.userID][dateString].TryAdd(item.info.shortname, new());
					sd.stats.playerDailySoldItems[player.userID][dateString][item.info.shortname].TryAdd(item.skin, 0);
					sd.stats.playerDailySoldItems[player.userID][dateString][item.info.shortname][item.skin] += sellAmount;
					float sumPrice = sid.price * sellAmount;
					sumAllPrice += sumPrice;
					if (salePriceMult > 1 && (!stockCfg.sellCfg.sellPriceMultBlacklist.TryGetValue(item.info.shortname, out var skins2) || !skins2.Contains(item.skin)))
					{
						float bonus = sumPrice * salePriceMult - sumPrice;
						sumAllPrice += bonus;
					}
					sid.sellAmount += sellAmount;
					sid.amountAvailable += Mathf.FloorToInt(sellAmount * (stockCfg.sellCfg.buyFromServerPercentage / 100f));
					if (sellAmount < item.amount)
					{
						item.amount -= sellAmount;
						item.MarkDirty();
					}
					else
					{
						item.GetHeldEntity()?.Kill();
						item.DoRemove();
					}
					anything = true;
				}

				if (!anything) return;
			}
			sd.stats.playerDailyEarnings.TryAdd(player.userID, new());
			sd.stats.playerDailyEarnings[player.userID].TryAdd(dateString, 0);
			sd.stats.playerDailyEarnings[player.userID][dateString] += sumAllPrice;
			AddCurrency(sc.shopName, player.userID, sumAllPrice, player: player);
			RedrawSellInventoryItems(player);
			StringBuilder sb = Pool.Get<StringBuilder>();
			CurrencyConfig currCfg = config.curr[sc.shopName].currCfg;
			SendEffect(player, StringKeys.Effect_Money);
			string currFormat = FormatCurrency(currCfg, sumAllPrice, sb);
			ShowPopUp(player, Lang("SoldToServer", player.UserIDString, currFormat));
			LogToConsole("SoldResourcesToServer", player.displayName, player.userID, sc.shopName, currFormat);
			Interface.CallHook("OnMarketItemsSold", player, sumAllPrice);
			Pool.FreeUnmanaged(ref sb);
        }

        private void AddStockAction(string key, StockData sd, StockActionType type, StringBuilder sb, params string[] args)
        {
	        ActionData ad = new()
	        {
		        date = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"),
		        type = type
	        };
	        if (args.Length > 0)
		        ad.field1 = args[0];
	        if (args.Length > 1)
		        ad.field2 = args[1];
	        if (args.Length > 2)
		        ad.field3 = args[2];
	        if (args.Length > 3)
		        ad.field4 = args[3];
	        if (args.Length > 4)
		        ad.field5 = args[4];
	        sd.stats.actions.Add(sd.stats.actionCounter, ad);
	        MarketLogConfig mlc = config.curr[key].stockCfg.marketLog;
	        sd.stats.actionCounter++;
	        if (!mlc.tabSellOffers && ad.type == StockActionType.SellOffer) return;
	        if (!mlc.tabBuyOffers && ad.type == StockActionType.BuyOffer) return;
	        if (!mlc.tabSoldItems && ad.type == StockActionType.SoldItem) return;
	        if (!mlc.tabPurchasedItems && ad.type == StockActionType.PurchasedItem) return;
	        if (!mlc.tabPriceRolls && ad.type == StockActionType.PriceRoll) return;
	        if (!mlc.tabDemands && (ad.type == StockActionType.Demand || ad.type == StockActionType.Demand_Neg)) return;
	        if (!mlc.tabAlerts && ad.type == StockActionType.Alert) return;
	        string actionName = sb.Clear().Append("StockAction_").Append(ad.type).ToString();
	        if (!mlc.chatSellOffers && ad.type == StockActionType.SellOffer) return;
	        if (!mlc.chatBuyOffers && ad.type == StockActionType.BuyOffer) return;
	        if (!mlc.chatSoldItems && ad.type == StockActionType.SoldItem) return;
	        if (!mlc.chatPurchasedItems && ad.type == StockActionType.PurchasedItem) return;
	        if (!mlc.chatPriceRolls && ad.type == StockActionType.PriceRoll) return;
	        if (!mlc.chatDemands && (ad.type == StockActionType.Demand || ad.type == StockActionType.Demand_Neg)) return;
	        if (!mlc.chatAlerts && ad.type == StockActionType.Alert) return;
	        foreach (var player in BasePlayer.activePlayerList)
	        {
		        CheckForOpenedStockHistoryPage(player, key);
		        if (!sd.playerData.players.TryGetValue(player.userID, out var spd)) continue;
		        if (spd.disabledChatAlerts) continue;
		        string hiddenNickname = !mlc.showBuyerNickname && ad.field1 == "Hidden" ? Lang("Hidden", player.UserIDString) : ad.field1;
		        string chatKey = sb.Clear().Append("Chat_").Append(actionName).ToString();
		        Mess(player, chatKey, hiddenNickname, ad.field2, ad.field3, ad.field4, ad.field5);
	        }
        }

        private void CheckForOpenedStockHistoryPage(BasePlayer player, string stockKey)
        {
	        if (!cache.TryGetValue(player.userID, out ShoppyCache sc)) return;
	        if (!sc.isOpened) return;
	        if (sc.type != ShoppyType.Stock) return;
	        if (!sc.stockCache.isWatchingHistory) return;
	        using CUI cui = new CUI(CuiHandler);
	        RedrawActionsUI(player, cui);
	        cui.v2.SendUi(player);
        }

        private void ConfirmCurrencyTransfer(BasePlayer player)
        {
	        ShoppyCache sc = cache[player.userID];
	        if (sc.transferCache.transferAmount <= 0) return;
	        float playerCurr = GetPlayerCurrency(player, sc.transferCache.selectedCurrency);
	        if (playerCurr < sc.transferCache.transferAmount) return;
			TransferConfig tc = config.curr[sc.transferCache.selectedCurrency].transferCfg;
			
			if (tc.dailyTransferLimit > 0)
			{
				float dayLimit = tc.dailyTransferLimit;
				foreach (var perm in tc.dailyTransferLimitPerms)
					if (permission.UserHasPermission(player.UserIDString, perm.Key))
						dayLimit = perm.Value;
				float dayTransfered = 0;
				if (dayLimit > 0 && data.transfer[sc.transferCache.selectedCurrency].players.TryGetValue(player.userID, out var ptd))
					ptd.dailyTransfers.TryGetValue(dateString, out dayTransfered);
				if (dayTransfered + sc.transferCache.transferAmount > dayLimit) return;
			}
			TransferData td = data.transfer[sc.transferCache.selectedCurrency];
			if (tc.wipeTransferLimit > 0)
			{
				float wipeLimit = tc.wipeTransferLimit;
				foreach (var perm in tc.wipeTransferLimitPerms)
					if (permission.UserHasPermission(player.UserIDString, perm.Key))
						wipeLimit = perm.Value;
				float wipeTransfered = 0;
				if (wipeLimit > 0 && td.players.TryGetValue(player.userID, out var ptd2))
					wipeTransfered = ptd2.wipeTransfer;
				if (wipeTransfered + sc.transferCache.transferAmount > wipeLimit) return;
			}
			
			float tax = tc.tax;
			foreach (var perm in tc.taxPerms)
				if (perm.Value < tax && permission.UserHasPermission(player.UserIDString, perm.Key))
					tax = perm.Value;

			float currTaken = sc.transferCache.transferAmount + sc.transferCache.transferAmount / 100f * tax;
			if (!TakeCurrency(sc.transferCache.selectedCurrency, player, currTaken)) return;
	        td.players.TryAdd(player.userID, new());
	        td.players[player.userID].wipeTransfer += sc.transferCache.transferAmount;
	        td.players[player.userID].dailyTransfers.TryAdd(dateString, 0);
	        td.players[player.userID].dailyTransfers[dateString] += sc.transferCache.transferAmount;
	        StringBuilder sb = Pool.Get<StringBuilder>();
	        SendEffect(player, StringKeys.Effect_Money);
	        string currFormat = FormatCurrency(sc.transferCache.selectedCurrency, sc.transferCache.transferAmount, sb);
	        ShowPopUp(player, Lang("CurrencyTransfered", player.UserIDString, currFormat, data.currencies.players[sc.transferCache.selectedPlayer].userName));
	        Pool.FreeUnmanaged(ref sb);
	        BasePlayer otherPlayer = BasePlayer.FindByID(sc.transferCache.selectedPlayer);
	        if (otherPlayer)
	        {
		        SendEffect(player, StringKeys.Effect_Collect);
		        ShowPopUp(otherPlayer, Lang("CurrencyReceived", otherPlayer.UserIDString, currFormat, player.displayName.Replace("\"", "").Replace("\\", "")));
	        }
	        LogToConsole("CurrencyTransfered", player.displayName, player.userID, sc.transferCache.selectedPlayer, currFormat);
	        AddCurrency(sc.transferCache.selectedCurrency, sc.transferCache.selectedPlayer, sc.transferCache.transferAmount, player: otherPlayer);
	        sc.transferCache.transferAmount = 0;
	        CuiHelper.DestroyUi(player, "ShoppyStockUI_TransferUserCore");
        }

        private void WithdrawCurrency(BasePlayer player)
        {
	        ShoppyCache sc = cache[player.userID];
	        if (sc.depositCache.inputAmount <= 0) return;
	        string currKey = "";
	        WithdrawItemConfig wic = null;
	        foreach (var curr in config.curr)
	        {
		        bool found = false;
		        foreach (var item in curr.Value.depositCfg.withdrawItems)
			        if (item.shortname == sc.depositCache.lastInputShortname && item.skinId == sc.depositCache.lastInputSkin)
			        {
				        found = true;
				        currKey = curr.Key;
				        wic = item;
				        break;
			        }
		        if (found) break;
	        }
	        if (currKey.Length == 0) return;
	        float sumCost = sc.depositCache.inputAmount * wic.value;
	        float playerCurr = GetPlayerCurrency(player, currKey);
	        if (sumCost > playerCurr) return;
	        int itemAmount = sc.depositCache.inputAmount;
	        TakeCurrency(currKey, player, sumCost);
	        ItemDefinition def = ItemManager.FindItemDefinition(sc.depositCache.lastInputShortname);
	        if (!def) return;
	        while (itemAmount > 0)
	        {
		        int giveAmount = itemAmount > def.stackable ? def.stackable : itemAmount;
		        Item item = ItemManager.Create(def, giveAmount, sc.depositCache.lastInputSkin);
		        if (wic.name.Length > 0)
			        item.name = wic.name;
		        player.GiveItem(item);
		        itemAmount -= giveAmount;
	        }
	        StringBuilder sb = Pool.Get<StringBuilder>();
	        SendEffect(player, StringKeys.Effect_Money);
	        string currFormat = FormatCurrency(currKey, sumCost, sb);
	        ShowPopUp(player, Lang("WithdrawedCurrency", player.UserIDString, currFormat));
	        LogToConsole("CurrencyWithdrawed", player.displayName, player.userID, currFormat);
	        Pool.FreeUnmanaged(ref sb);
	        OpenDepositWithdrawUI(player);
        }

        private void TryDepositCurrency(BasePlayer player)
        {
	        ShoppyCache sc = cache[player.userID];
	        if (!sc.stockCache.container && sc.stockCache.container.IsDestroyed) return;
	        if (sc.stockCache.container.inventory.itemList.Count == 0) return;
	        Item inputItem = sc.stockCache.container.inventory.itemList[0];
	        string shopKey = "";
	        CurrencyItemConfig cic = null;
	        foreach (var curr in config.curr)
	        {
		        bool found = false;
		        foreach (var item in curr.Value.currCfg.currItems)
		        {
			        if (item.shortname == inputItem.info.shortname && item.skinId == inputItem.skin)
			        {
				        found = true;
				        shopKey = curr.Key;
				        cic = item;
				        break;
			        }
		        }
		        if (found) break;
	        }
	        if (cic == null) return;
	        float sumCurrency = cic.value * inputItem.amount;
	        inputItem.GetHeldEntity()?.Kill();
	        inputItem.DoRemove();
	        AddCurrency(shopKey, player.userID, sumCurrency, player: player, forceDeposit: true);
	        StringBuilder sb = Pool.Get<StringBuilder>();
	        SendEffect(player, StringKeys.Effect_Money);
	        string currFormat = FormatCurrency(shopKey, sumCurrency, sb);
	        ShowPopUp(player, Lang("DepositedCurrency", player.UserIDString, currFormat));
	        LogToConsole("CurrencyDeposited", player.displayName, player.userID, currFormat);
	        Pool.FreeUnmanaged(ref sb);
	        UpdateCurrencyDepositUI(player, sc.stockCache.container.inventory);
        }

        private void DrawNewLeaderboards()
        {
	        StringBuilder sb = Pool.Get<StringBuilder>();
	        StringBuilder sb2 = Pool.Get<StringBuilder>();
	        foreach (var curr in config.curr)
	        {
		        if (!curr.Value.currCfg.leaderboard) continue;
		        var orderedCurrencies = data.currencies.players.OrderByDescending(x =>
		        {
			        if (x.Value.currencies.TryGetValue(curr.Key, out float balance))
				        return balance;
			        return 0;
		        });
		        int counter = 1;
		        sb.Clear();
		        foreach (var player in orderedCurrencies.Take(30))
		        {
			        if (!player.Value.currencies.ContainsKey(curr.Key)) continue;
			        string currency = FormatCurrency(curr.Value.currCfg, player.Value.currencies[curr.Key], sb2);
			        if (counter == 1)
				        sb.Append("<color=#C9B037>#1 - ").Append(player.Value.userName).Append(" - ").Append(currency).Append("</color>\n");
			        else if (counter == 2)
				        sb.Append("<color=#D7D7D7>#2 - ").Append(player.Value.userName).Append(" - ").Append(currency).Append("</color>\n");
			        else if (counter == 3)
				        sb.Append("<color=#AD8A56>#3 - ").Append(player.Value.userName).Append(" - ").Append(currency).Append("</color>\n");
			        else
				        sb.Append('#').Append(counter).Append(" - ").Append(player.Value.userName).Append(" - ").Append(currency).Append("\n");
			        counter++;
		        }
		        cachedLeaderboards[curr.Key] = sb.ToString();
	        }
	        lastLeaderboardCheck = DateTime.Now;
	        Pool.FreeUnmanaged(ref sb);
	        Pool.FreeUnmanaged(ref sb2);
        }

        private void TryExchangeCurrency(BasePlayer player, int id)
        {
	        ShoppyCache sc = cache[player.userID];
	        int counter = -1;
	        foreach (var curr in config.curr)
	        {
		        if (!curr.Value.exchangeCfg.enabled) continue;
		        foreach (var exchange in curr.Value.exchangeCfg.exchanges)
		        {
			        counter++;
			        if (sc.exchangeCache.currentId != counter) continue;
			        int takeAmount = sc.exchangeCache.amount;
			        bool isCurrency = exchange.currName.Length > 0;
			        float amountRequired = isCurrency ? exchange.currAmount : exchange.itemAmount;
			        float exchangeReceived = sc.exchangeCache.amount * (exchange.amountReceived / amountRequired);
			        if (isCurrency)
			        {
				        float amount = GetPlayerCurrency(player, exchange.currName);
				        if (amount < takeAmount) return;
				        if (TakeCurrency(exchange.currName, player, takeAmount))
					        AddCurrency(curr.Key, player.userID, exchangeReceived, player: player);
				        else return;
			        }
			        else
			        {
				        if (!AddCurrency(curr.Key, player.userID, exchangeReceived, player: player))
				        {
					        ShowPopUp(player, Lang("NotEnoughToCreateItem", player.UserIDString));
					        return;
				        }
				        int toTake = takeAmount;
				        foreach (var item in player.inventory.containerMain.itemList.ToArray())
					        if (item.info.shortname == exchange.itemShortname && item.skin == exchange.itemSkin)
					        {
						        if (item.amount > toTake)
						        {
							        item.amount -= toTake;
							        toTake = 0;
							        item.MarkDirty();
						        }
						        else
						        {
							        toTake -= item.amount;
							        item.GetHeldEntity()?.Kill();
							        item.DoRemove();
						        }
						        if (toTake == 0) break;
					        }
			        }
			        sc.exchangeCache.amount = 0;
			        OpenExchangeUI(player);
			        SendEffect(player, StringKeys.Effect_Money);
			        ShowPopUp(player, Lang("CurrencyExchanged", player.UserIDString));
			        LogToConsole("CurrencyExchanged", player.displayName, player.userID, exchange.currName, exchange.itemShortname, exchangeReceived);
			        return;
		        }
	        }
        }

        private void TryUpdateListingUI(string stockKey, string shortname, ulong skin, bool buyOffer)
        {
	        foreach (var cp in cache)
	        {
		        ulong userId = cp.Key;
		        ShoppyCache sc = cp.Value;
		        if (!sc.isOpened) continue;
		        if (sc.type != ShoppyType.Stock) continue;
		        if (sc.shopName != stockKey) continue;
		        if (sc.stockCache.shortname != shortname) continue;
		        if (sc.stockCache.skin != skin) continue;
		        if ((buyOffer && sc.stockCache.listingPage == ListingPageType.BuyOffer) || (!buyOffer && sc.stockCache.listingPage == ListingPageType.SellOffer))
		        {
			        BasePlayer player = BasePlayer.FindByID(userId);
			        if (!player) continue;
			        RedrawStockListings(player);
		        }
			        
	        }
        }

        private void TryUpdatePricesUI(string stockKey, string shortname, ulong skin)
        {
	        foreach (var cp in cache)
	        {
		        ulong userId = cp.Key;
		        ShoppyCache sc = cp.Value;
		        if (!sc.isOpened) continue;
		        if (sc.type != ShoppyType.Stock) continue;
		        if (sc.shopName != stockKey) continue;
		        if (sc.stockCache.listingPage != ListingPageType.ServerSell) continue;
		        if (sc.stockCache.shortname.Length == 0) continue;
		        BasePlayer player = BasePlayer.FindByID(userId);
		        if (!player) continue;
		        UpdateServerSellUI(player);
	        }
        }

        private void TryUpdateShopUI(string shopKey)
        {
	        foreach (var cp in cache)
	        {
		        ulong userId = cp.Key;
		        ShoppyCache sc = cp.Value;
		        if (!sc.isOpened) continue;
		        if (sc.type != ShoppyType.Shop) continue;
		        if (sc.shopName != shopKey) continue;
		        BasePlayer player = BasePlayer.FindByID(userId);
		        if (!player) continue;
		        SwitchShopCategory(player, sc.shopCache.category, force: true);
	        }
        }

        private void TryBuyFromServer(BasePlayer player)
        {
	        ShoppyCache sc = cache[player.userID];
	        StockData sd = data.stock[sc.shopName];
	        SellItemData sid = sd.prices.items[sc.stockCache.shortname][sc.stockCache.skin];
	        StockConfig stockCfg = config.curr[sc.shopName].stockCfg;
	        if (sc.stockCache.stockActionAmount > sid.amountAvailable)
		        sc.stockCache.stockActionAmount = sid.amountAvailable;
	        if (sc.stockCache.stockActionAmount == 0 || sid.buyPrice == 0) return;
	        if (sid.buyPrice < sid.price)
	        {
		        Puts($"Listing of {sc.stockCache.shortname} with skin {sc.stockCache.skin} have bigger re-purchase value than selling value. Cancelling buying from server to prevent economy error!");
		        return;
	        }
	        float sumPrice = sc.stockCache.stockActionAmount * sid.buyPrice;
	        if (GetPlayerCurrency(player, sc.shopName) < sumPrice)
	        {
		        SendEffect(player, StringKeys.Effect_Denied);
		        ShowPopUp(player, Lang("NotEnoughCurrency", player.UserIDString));
		        return;
	        }
	        TakeCurrency(sc.shopName, player, sumPrice);
	        sd.stats.playerDailyPurchasedItems.TryAdd(player.userID, new());
	        sd.stats.playerDailyPurchasedItems[player.userID].TryAdd(dateString, new());
	        sd.stats.playerDailyPurchasedItems[player.userID][dateString].TryAdd(sc.stockCache.shortname, new());
	        sd.stats.playerDailyPurchasedItems[player.userID][dateString][sc.stockCache.shortname].TryAdd(sc.stockCache.skin, 0);
	        sd.stats.playerDailyPurchasedItems[player.userID][dateString][sc.stockCache.shortname][sc.stockCache.skin] += sc.stockCache.stockActionAmount;
	        ItemDefinition def = ItemManager.FindItemDefinition(sc.stockCache.shortname);
	        StringBuilder sb = Pool.Get<StringBuilder>();
	        string name = GetItemName(sd, sc.stockCache.shortname, sc.stockCache.skin);
	        int cachedAmount = sc.stockCache.stockActionAmount;
	        sid.amountAvailable -= cachedAmount;
	        while (sc.stockCache.stockActionAmount > 0)
	        {
		        int giveAmount = sc.stockCache.stockActionAmount > def.stackable ? def.stackable : sc.stockCache.stockActionAmount;
		        Item item = ItemManager.Create(def, giveAmount, sc.stockCache.skin);
		        if (name.Length > 0)
			        item.name = name;
		        sc.stockCache.stockActionAmount -= giveAmount;
		        if (stockCfg.itemsGoToRedeem)
			        RedeemStorageAPI?.Call("AddItem", player.userID.Get(), stockCfg.redeemInventoryName, item, sc.stockCache.stockActionAmount == 0);
		        else
			        player.GiveItem(item);
	        }
	        SendEffect(player, StringKeys.Effect_Money);
	        string currFormat = FormatCurrency(sc.shopName, sumPrice, sb);
	        ShowPopUp(player, Lang("PurchasedFromStock", player.UserIDString, cachedAmount, name, currFormat));
	        LogToConsole("PlayerPurchasedFromStock", player.displayName, player.userID, cachedAmount, name, currFormat);
	        Pool.FreeUnmanaged(ref sb);
	        TryUpdatePricesUI(sc.shopName, sc.stockCache.shortname, sc.stockCache.skin);
        }

        private void SetAlertPrice(BasePlayer player, float price, bool alert)
        {
	        ShoppyCache sc = cache[player.userID];
	        StockPlayersData spd = data.stock[sc.shopName].playerData;
	        spd.players.TryAdd(player.userID, new());
	        spd.players[player.userID].alerts.TryAdd(sc.stockCache.shortname, new());
	        spd.players[player.userID].alerts[sc.stockCache.shortname].TryAdd(sc.stockCache.skin, new());
	        if (alert)
	        {
		        spd.players[player.userID].alerts[sc.stockCache.shortname][sc.stockCache.skin].alertPrice = price;
		        ShowPopUp(player, Lang("SetAlertPrice", player.UserIDString, price));
	        }
	        else
	        {
		        spd.players[player.userID].alerts[sc.stockCache.shortname][sc.stockCache.skin].instaSellPrice = price;
		        ShowPopUp(player, Lang("SetSellPrice", player.UserIDString, price));
	        }
        }

        private string GetItemDetails(BasePlayer player, string stockName, ItemData item)
        {
	        if (item.skin != 0 && config.curr[stockName].stockCfg.itemsCustomDetails.ContainsKey(item.skin))
		        return Lang(config.curr[stockName].stockCfg.itemsCustomDetails[item.skin], player.UserIDString);
	        if (item.dataInt != 0)
		        return GrowableGeneEncoding.DecodeIntToGeneString(item.dataInt);
	        if (item.maxCondition != -1)
		        return Lang("Condition", player.UserIDString, Mathf.RoundToInt((item.maxCondition - item.condition) * 100));
	        //TODO LATER ON OTHER PLUGIN IMPLEMENTATIONS
	        return "---";
        }

        private void UpdateWebAPI(string shopName)
        {
	        bool any = false;
	        foreach (var shop in config.curr)
		        if (shop.Value.stockCfg.webApi.enabled)
		        {
			        any = true;
			        break;
		        }
	        if (!any) return;
	        StockConfig stockCfg = config.curr[shopName].stockCfg;
	        if (!stockCfg.webApi.enabled) return;
	        StockData sd = data.stock[shopName];
	        if (sd.cfg.sellItems.Count == 0) return;
	        Dictionary<string, float> prices = Pool.Get<Dictionary<string, float>>();
	        prices.Clear();
	        foreach (var shortname in sd.cfg.sellItems)
	        {
		        if (!sd.prices.items.TryGetValue(shortname.Key, out var skins)) continue;
		        foreach (var skin in shortname.Value)
		        {
			        if (!skins.TryGetValue(skin.Key, out var sellData)) continue;
			        string name = "";
			        if (skin.Key == 0)
				        name = ItemManager.FindItemDefinition(shortname.Key)?.displayName.english ?? "";
			        else
			        {
				        if (sd.cfg.customItems.TryGetValue(shortname.Key, out var skins2) && skins2.TryGetValue(skin.Key, out var sic))
					        name = sic.displayName;
			        }
			        if (name.Length == 0) continue;
			        prices[name] = sellData.price;
		        }
	        }
	        var json = JsonConvert.SerializeObject(prices);
	        webrequest.Enqueue(stockCfg.webApi.url, $"plugindata={json}", (code, response) =>
	        {
		        if (code != 200 || response == null)
		        {
			        Puts($"Market API Error Code: {code}");
			        return;
		        }
		        LogToConsole("WebApiUpdated", shopName);
	        }, this, RequestMethod.POST);
	        Pool.FreeUnmanaged(ref prices);
        }

        private void ShowPopUp(BasePlayer player, string message) => PopUpAPI?.Call("ShowPopUp", player, "MarketV2", message, config.ui.popUpFontSize, config.ui.popUpDisplayTime);

        private void SendEffect(BasePlayer player, string path)
        {
	        List<Connection> connections = Pool.Get<List<Connection>>();
	        connections.Clear();
	        connections.Add(player.Connection);
	        Effect.server.Run(path, player.eyes.position, targets: connections);
	        Pool.FreeUnmanaged(ref connections);
        }

        private bool IsBlocked(BasePlayer player)
        {
	        if (NoEscape == null) return false;
	        if (config.ui.noEscapeCombat && NoEscape.Call<bool>("IsCombatBlocked", player.UserIDString)) return true;
	        if (config.ui.noEscapeRaid && NoEscape.Call<bool>("IsRaidBlocked", player.UserIDString)) return true;
	        return false;
        }

        private bool HasPlayerItemUnlocked(BasePlayer player, string shortname)
        {
	        //if (shortnameRedirect.ContainsKey(shortname) && !ownershipRedirect.ContainsKey(shortname))
	        //    shortname = shortnameRedirect[shortname];
	        ItemDefinition def = ItemManager.FindItemDefinition(shortname);
	        if (def.steamDlc && player.blueprints.steamInventory.HasItem(def.steamDlc.dlcAppID))
		        return true;
	        if (def.steamItem && player.blueprints.steamInventory.HasItem(def.steamItem.id))
		        return true;
	        if (player.blueprints.HasUnlocked(def))
		        return true;
	        if (ownershipShortnames.TryGetValue(shortname, out int shortnameId) && player.blueprints.steamInventory.HasItem(shortnameId))
		        return true;
	        if (ownershipDlcs.TryGetValue(shortname, out int dlcId) && player.blueprints.steamInventory.HasItem(dlcId))
		        return true;
	        return false;
        }

        private bool HasPlayerSkinUnlocked(BasePlayer player, int skinId)
        {
	        if (skinId == 0) return true;
	        foreach (var skinDef in ItemSkinDirectory.Instance.skins)
	        {
		        if (skinDef.id == skinId)
			        return (skinDef.invItem && HasSteamItemUnlocked(skinDef.invItem, player)) || player.blueprints.steamInventory.HasItem(skinId);
	        }
	        return false;
        }
        
        private bool HasSteamItemUnlocked(SteamInventoryItem sii, BasePlayer player)
        {
	        if (sii.DlcItem && sii.DlcItem.HasLicense(player.userID))
		        return true;
	        if (sii.UnlockedViaSteamItem)
		        return HasPlayerSkinUnlocked(player, sii.UnlockedViaSteamItem.id);
	        return false;
        }

        private void LoadDefaultEditingConfig()
        {
	        ConfigEditorCache.config = DeepJsonCopy(config);
	        foreach (var curr in config.curr)
	        {
		        if (curr.Value.shopCfg.enabled)
		        {
			        ConfigEditorCache.shopDatas[curr.Key] = DeepJsonCopy(data.shop[curr.Key].cfg);
		        }
		        if (curr.Value.stockCfg.enabled)
		        {
			        ConfigEditorCache.stockDatas[curr.Key] = DeepJsonCopy(data.stock[curr.Key].cfg);
		        }
	        }
        }

        private void SaveAndOverrideConfigs()
        {
	        config = DeepJsonCopy(ConfigEditorCache.config);
	        foreach (var curr in ConfigEditorCache.config.curr)
	        {
		        if (curr.Value.shopCfg.enabled)
		        {
			        data.shop.TryAdd(curr.Key, new());
			        ConfigEditorCache.shopDatas.TryAdd(curr.Key, new());
			        data.shop[curr.Key].cfg = DeepJsonCopy(ConfigEditorCache.shopDatas[curr.Key]);
		        }
		        if (curr.Value.stockCfg.enabled)
		        {
			        data.stock.TryAdd(curr.Key, new());
			        ConfigEditorCache.stockDatas.TryAdd(curr.Key, new());
			        data.stock[curr.Key].cfg = DeepJsonCopy(ConfigEditorCache.stockDatas[curr.Key]);
		        }
	        }
	        ConfigEditorCache.edited = false;
	        SaveConfig();
	        SaveStockDataConfig();
	        SaveShopDataConfig();
        }

        private static T DeepJsonCopy<T>(T src)
        {
	        string json = JsonConvert.SerializeObject(src);
	        return JsonConvert.DeserializeObject<T>(json);
        }

        private object FindAdminParentPage(object searchObject = null, int index = 0)
        {
	        if (ConfigEditorCache.enterTree.Count == 0)
		        return ConfigEditorCache.config;
	        if (index == 0)
		        searchObject = ConfigEditorCache.config;
	        string fixedTreeName = ConfigEditorCache.enterTree[index].Replace("l--", "").Replace("d--", "");
	        if (fixedTreeName.StartsWith("shop--"))
	        {
		        string shopName = fixedTreeName.Replace("shop--", "");
		        ConfigEditorCache.shopDatas.TryAdd(shopName, new());
		        if (ConfigEditorCache.enterTree.Count - 1 == index)
			        return ConfigEditorCache.shopDatas[shopName];
		        return FindAdminParentPage(ConfigEditorCache.shopDatas[shopName], index + 1);
	        }
	        if (fixedTreeName.StartsWith("stock--"))
	        {
		        string shopName = fixedTreeName.Replace("stock--", "");
		        ConfigEditorCache.stockDatas.TryAdd(shopName, new());
		        if (ConfigEditorCache.enterTree.Count - 1 == index)
			        return ConfigEditorCache.stockDatas[shopName];
		        return FindAdminParentPage(ConfigEditorCache.stockDatas[shopName], index + 1);
	        }
	        if (searchObject is IDictionary idict)
	        {
		        foreach (DictionaryEntry field in idict)
		        {
			        string keyString = field.Key.ToString();
			        if (keyString == fixedTreeName)
			        {
				        if (ConfigEditorCache.enterTree.Count - 1 == index)
					        return field.Value;
				        return FindAdminParentPage(field.Value, index + 1);
			        }
		        }
		        return null;
	        }
	        if (searchObject is IList ienum)
	        {
		        int listIndex = int.Parse(fixedTreeName);
		        int counter = 0;
		        foreach (var field in ienum)
		        {
			        if (counter == listIndex)
			        {
				        if (ConfigEditorCache.enterTree.Count - 1 == index)
							return field;
				        return FindAdminParentPage(field, index + 1);
			        }
			        counter++;
		        }
		        return null;
	        }
	        FieldInfo[] fields = searchObject.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
	        foreach (FieldInfo field in fields)
	        {
		        if (field.Name == fixedTreeName)
		        {
			        if (ConfigEditorCache.enterTree.Count - 1 == index)
				        return field.GetValue(searchObject);
					return FindAdminParentPage(field.GetValue(searchObject), index + 1);
		        }
	        }
	        return null;
        }

        private void EditConfigField(BasePlayer player, Type type, string fieldName, object value = null)
        {
	        object currentPage = FindAdminParentPage();
	        ConfigEditorCache.edited = true;
	        if (currentPage is IDictionary idict)
	        {
		        foreach (DictionaryEntry field in idict)
		        {
			        string keyString = field.Key.ToString();
			        if (keyString == fieldName)
			        {
				        idict[field.Key] = value;
				        return;
			        }
		        }
		        return;
	        }
	        if (currentPage is IList ienum)
	        {
		        int listIndex = int.Parse(fieldName);
		        ienum[listIndex] = value;
		        return;
	        }
	        FieldInfo[] fields = currentPage.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
	        foreach (FieldInfo field in fields)
	        {
		        if (field.Name == fieldName)
		        {
			        if (value != null)
						field.SetValue(currentPage, value);
			        else
			        {
				        bool boolValue = (bool)field.GetValue(currentPage);
				        field.SetValue(currentPage, !boolValue);
			        }
			        break;
		        }
	        }
	        RefreshAdminPage(player);
        }

        private void RefreshAdminPage(BasePlayer player)
        {
	        bool any = ConfigEditorCache.enterTree.Count > 0;
	        if (any && ConfigEditorCache.enterTree[^1].StartsWith("d--"))
	        {
		        OpenSubAdminPage(player);
		        DrawAdminDictionaryOptions(player);
	        }
	        else if (any && ConfigEditorCache.enterTree[^1].StartsWith("l--"))
	        {
		        OpenSubAdminPage(player);
		        DrawAdminListOptions(player);
	        }
	        else
		        OpenAdminSettingsPage(player);
        }

        private void RemoveConfigRecord(BasePlayer player, int index = -1, string key = null)
        {
	        object currentPage = FindAdminParentPage();
	        ConfigEditorCache.edited = true;
	        if (index != -1 && currentPage is IList ienum)
		        ienum.RemoveAt(index);
	        if (key != null && currentPage is IDictionary idict)
		        idict.Remove(key);
	        RefreshAdminPage(player);
        }

        private void UpdateDictKey(BasePlayer player, string oldName, object newName)
        {
	        object currentPage = FindAdminParentPage();
	        ConfigEditorCache.edited = true;
	        if (currentPage is IDictionary idict)
	        {
		        idict.Add(newName, null);
		        object valueToCopy = null;
		        foreach (DictionaryEntry field in idict)
		        {
			        if (field.Key.ToString() == oldName)
			        {
				        valueToCopy = field.Value;
				        break;
			        }
		        }
		        idict[newName] = DeepJsonCopy(valueToCopy);
		        idict.Remove(oldName);
	        }
        }

        private void AddRecord(BasePlayer player)
        {
	        object currentPage = FindAdminParentPage();
	        ConfigEditorCache.edited = true;
	        if (currentPage is IDictionary idict)
	        {
		        if (ConfigEditorCache.cachedValue == null)
		        {
			        Type valueType = idict.GetType().GetGenericArguments()[1];
			        object instance = Activator.CreateInstance(valueType);
			        idict.Add(ConfigEditorCache.cachedKey, instance);
		        }
		        else
			        idict.Add(ConfigEditorCache.cachedKey, ConfigEditorCache.cachedValue);
	        }
	        else if (currentPage is IList ilist)
		        ilist.Add(ConfigEditorCache.cachedKey);
	        ConfigEditorCache.cachedKey = null;
	        ConfigEditorCache.cachedValue = null;
	        RefreshAdminPage(player);
        }

        private void RunCustomButtonAdminMethod(BasePlayer player, string methodName)
        {
	        object currentPage = FindAdminParentPage();
	        if (methodName == "AddCurrencyItem")
	        {
		        Item item = player.GetActiveItem();
		        if (item == null)
		        {
			        ShowPopUp(player, Lang("NoItemInHands", player.UserIDString));
			        return;
		        }
		        CurrencyConfig currCfg = currentPage as CurrencyConfig;
		        currCfg.currItems.Add(new()
		        {
			        shortname = item.info.shortname,
			        skinId = item.skin,
			        displayName = string.IsNullOrEmpty(item.name) ? item.info.displayName.english : item.name
		        } );
		        ShowPopUp(player, Lang("CurrItemAddedSuccessfully", player.UserIDString));
		        ConfigEditorCache.edited = true;
		        OpenAdminSettingsPage(player);
	        }
	        else if (methodName == "OpenShopData")
	        {
		        string shopName = ConfigEditorCache.enterTree[1].Replace("d--", "");
		        ConfigEditorCache.enterTree.Add($"shop--{shopName}");
		        OpenAdminSettingsPage(player);
	        }
	        else if (methodName == "OpenStockData")
	        {
		        string shopName = ConfigEditorCache.enterTree[1].Replace("d--", "");
		        ConfigEditorCache.enterTree.Add($"stock--{shopName}");
		        OpenAdminSettingsPage(player);
	        }
	        else if (methodName == "AddListingItem")
	        {
		        Item item = player.GetActiveItem();
		        if (item == null)
		        {
			        ShowPopUp(player, Lang("NoItemInHands", player.UserIDString));
			        return;
		        }
		        ShopCategoryConfig scc = currentPage as ShopCategoryConfig;
		        string key = $"{item.info.shortname}-{item.skin}";
		        if (scc.listings.ContainsKey(key))
		        {
			        ShowPopUp(player, Lang("ListingAlreadyAdded", player.UserIDString));
			        return;
		        }
		        scc.listings.Add(key, new()
		        {
			        shortname = item.info.shortname,
			        skin = item.skin,
			        amount = item.amount,
			        itemName = string.IsNullOrEmpty(item.name) ? "" : item.name,
			        displayName = string.IsNullOrEmpty(item.name) ? item.info.displayName.english : item.name
		        });
		        ShowPopUp(player, Lang("ShopItemAddedSuccessfully", player.UserIDString));
		        ConfigEditorCache.edited = true;
		        OpenAdminSettingsPage(player);
	        }
	        else if (methodName == "AddCustomItem")
	        {
		        Item item = player.GetActiveItem();
		        if (item == null)
		        {
			        ShowPopUp(player, Lang("NoItemInHands", player.UserIDString));
			        return;
		        }
		        StockDataConfig sdc = currentPage as StockDataConfig;
		        sdc.customItems.TryAdd(item.info.shortname, new());
		        sdc.customItems[item.info.shortname].TryAdd(item.skin, new()
		        {
			        category = "Resources",
			        displayName = string.IsNullOrEmpty(item.name) ? item.info.displayName.english : item.name
		        });
		        ShowPopUp(player, Lang("CustomItemAddedSuccessfully", player.UserIDString));
		        ConfigEditorCache.edited = true;
		        OpenAdminSettingsPage(player);
	        }
	        else if (methodName == "AddSellItem")
	        {
		        Item item = player.GetActiveItem();
		        if (item == null)
		        {
			        ShowPopUp(player, Lang("NoItemInHands", player.UserIDString));
			        return;
		        }
		        StockDataConfig sdc = currentPage as StockDataConfig;
		        sdc.sellItems.TryAdd(item.info.shortname, new());
		        sdc.sellItems[item.info.shortname].TryAdd(item.skin, new());
		        ShowPopUp(player, Lang("SellItemAddedSuccessfully", player.UserIDString));
		        ConfigEditorCache.edited = true;
		        OpenAdminSettingsPage(player);
	        }
        }
        
        #endregion
        
        #region RUST Hooks
        
        private void OnPlayerLootEnd(PlayerLoot loot)
        {
	        BasePlayer player = loot.GetCastedEntity();
	        CuiHelper.DestroyUi(player, "ShoppyStockUI_Inventory"); //Moved it here just in case to destroy UI.
	        if (!player || !cache.TryGetValue(player.userID, out var sc)) return;
	        if (sc.stockCache.haveOpenContainer && sc.stockCache.container && !sc.stockCache.container.IsDestroyed)
	        {
		        if (sc.stockCache.containerType == ContainerType.SellOffer)
			        sc.stockCache.container.inventory.onDirty -= sc.stockCache.savedAction;
		        else if (sc.stockCache.containerType == ContainerType.SellInventory)
			        sc.stockCache.container.inventory.onDirty -= sc.stockCache.savedAction;
		        //It was here
		        if (sc.stockCache.container && !sc.stockCache.container.IsDestroyed)
		        {
			        foreach (var item in sc.stockCache.container.inventory.itemList.ToArray())
				        if (!item.MoveToContainer(player.inventory.containerMain))
					        if (!item.MoveToContainer(player.inventory.containerBelt))
						        item.Drop(player.GetDropPosition(), player.GetDropVelocity());   
		        }
		        sc.stockCache.haveOpenContainer = false;
	        }
	        if (sc.isOpened)
				OpenSavedUI(player);
        }
        
        //HumanNPC
        private void OnUseNPC(BasePlayer npc, BasePlayer player)
        {
	        if (!config.npcs.TryGetValue(npc.UserIDString, out var npcCfg)) return;
	        cache.TryAdd(player.userID, new());
	        ShoppyCache sc = cache[player.userID];
	        if (config.ui.cooldown > 0)
	        {
		        DateTime now = DateTime.Now;
		        if ((now - sc.lastAction).TotalSeconds < config.ui.cooldown) return;
		        sc.lastAction = now;
	        }
	        LogToConsole("PlayerNpcOpen", player.displayName, player.userID, npc.UserIDString);
	        AddPlayerToData(player);
	        sc.isOpened = true;
	        if (sc.npcId != npc.UserIDString)
	        {
		        sc.shopName = "";
				sc.type = ShoppyType.Unassigned;
				sc.npcId = npc.UserIDString;
	        }
	        OpenInitialUI(player);
	        if (npcCfg.shops.Count > 0)
		        OpenShopsRedirectCheck(player, sc);
	        else if (npcCfg.stocks.Count > 0)
		        OpenStocksRedirectCheck(player, sc);
	        else if (npcCfg.transfer)
		        OpenTransferUI(player);
	        else if (npcCfg.exchanges)
		        OpenExchangeUI(player);
	        else if (npcCfg.deposits || npcCfg.withdraws)
		        OpenDepositWithdrawUI(player);
	        else
		        ShowPopUp(player, Lang("NotAssignedNpc", player.UserIDString));
        }
        
        //NoEscape
        private void OnRaidBlock(BasePlayer target)
        {
	        if (config.ui.noEscapeRaid && NoEscape != null)
	        {
		        if (!cache.TryGetValue(target.userID, out var sc)) return;
		        if (!sc.isOpened) return;
		        sc.isOpened = false;
		        CuiHelper.DestroyUi(target, "ShoppyStockUI");
		        Mess(target, "UiClosedCombat");
	        }
        }
        
        //NoEscape
        private void OnCombatBlock(BasePlayer target)
        {
	        if (config.ui.noEscapeCombat && NoEscape != null)
	        {
		        if (!cache.TryGetValue(target.userID, out var sc)) return;
		        if (!sc.isOpened) return;
		        sc.isOpened = false;
		        CuiHelper.DestroyUi(target, "ShoppyStockUI");
		        Mess(target, "UiClosedCombat");
	        }
        }
        
        #endregion
        
        #region Commands

        private void OpenUiCommand(BasePlayer player, string command, string[] args)
        {
	        if (!validImageLoad)
	        {
		        Mess(player, "ImagesLoading");
		        return;
	        }
	        if (config.cmd.perm.Length > 0 && !permission.UserHasPermission(player.UserIDString, config.cmd.perm))
	        {
		        Mess(player, "NoPermToShop");
		        return;
	        }
	        if (IsBlocked(player))
	        {
		        Mess(player, "AccessLockedPvP");
		        return;
	        }
	        bool wasInData = cache.ContainsKey(player.userID);
	        cache.TryAdd(player.userID, new());
	        ShoppyCache sc = cache[player.userID];
	        if (config.ui.cooldown > 0)
	        {
		        DateTime now = DateTime.Now;
		        if ((now - sc.lastAction).TotalSeconds < config.ui.cooldown) return;
		        sc.lastAction = now;
	        }
	        LogToConsole("PlayerCommand", player.displayName, player.userID, command, string.Join(' ', args));
	        string cmdLower = command.ToLower();
	        AddPlayerToData(player);
	        sc.npcId = string.Empty;
	        if (config.cmd.adminUi.Contains(cmdLower))
	        {
		        if (!permission.UserHasPermission(player.UserIDString, "shoppystock.admin"))
		        {
			        Mess(player, "NoPermToShop");
			        return;
		        }
		        OpenAdminPage(player);
		        return;
	        }
	        if (config.cmd.quickAccess.Contains(cmdLower))
	        {
		        if (args.Length < 1)
		        {
			        Mess(player, "NotValidCode");
			        return;
		        }
		        if (!int.TryParse(args[0], out int code))
		        {
			        Mess(player, "NotValidCode");
			        return;
		        }
		        if (!cachedShareCodes.Contains(code))
		        {
			        Mess(player, "CodeNotFound", code);
			        return;
		        }

		        foreach (var stock in data.stock)
		        {
			        foreach (var shortname in stock.Value.listings.buyOffers)
			        foreach (var skin in shortname.Value)
			        {
				        bool found = false;
				        foreach (var listing in skin.Value)
				        {
					        if (listing.isCancelled || listing.customAccessCode != code) continue;
					        found = true;
					        break;
				        }
				        if (!found) continue;
				        sc.stockCache.listingCode = code;
				        int listingIndex = -1;
				        foreach (var listing in skin.Value.OrderByDescending(x => x.price).ToArray())
				        {
					        listingIndex++;
					        if (listing.isCancelled || listing.customAccessCode != code) continue;
					        sc.shopName = stock.Key;
					        sc.stockCache.listingPage = ListingPageType.BuyOffer;
					        sc.stockCache.selectedListingIndex = listingIndex;
					        sc.isOpened = true;
					        OpenInitialUI(player);
					        OpenStockUI(player);
					        ShowStockListingDetails(player, shortname.Key, skin.Key);
					        return;
				        }
			        }
			        foreach (var shortname in stock.Value.listings.sellOffers)
			        foreach (var skin in shortname.Value)
			        {
				        bool found = false;
				        foreach (var listing in skin.Value)
				        {
					        if (listing.isCancelled || listing.customAccessCode != code) continue;
					        found = true;
					        break;
				        }
				        if (!found) continue;
				        sc.stockCache.listingCode = code;
				        int listingIndex = -1;
				        foreach (var listing in skin.Value.OrderBy(x => x.price).ToArray())
				        {
					        listingIndex++;
					        if (listing.isCancelled || listing.customAccessCode != code) continue;
					        sc.shopName = stock.Key;
					        sc.stockCache.listingPage = ListingPageType.SellOffer;
					        sc.stockCache.selectedListingIndex = listingIndex;
					        sc.isOpened = true;
					        OpenInitialUI(player);
					        OpenStockUI(player);
					        ShowStockListingDetails(player, shortname.Key, skin.Key);
					        return;
				        }
			        }
		        }
		        Mess(player, "CodeNotFound", code);
		        return;
	        }
	        if (config.cmd.deposit.Contains(cmdLower))
	        {
		        OpenInitialUI(player);
		        sc.isOpened = false;
		        OpenDepositWithdrawUI(player);
		        return;
	        }
	        if (config.cmd.shops.ContainsKey(cmdLower))
	        {
		        sc.shopName = config.cmd.shops[cmdLower];
		        sc.isOpened = true;
		        OpenInitialUI(player);
		        OpenShopUI(player);
		        return;
	        }
	        if (config.cmd.markets.ContainsKey(cmdLower))
	        {
		        sc.shopName = config.cmd.markets[cmdLower];
		        sc.isOpened = true;
		        OpenInitialUI(player);
		        OpenStockUI(player);
		        return;
	        }
	        if (config.cmd.sellInv.ContainsKey(cmdLower))
	        {
		        sc.shopName = config.cmd.sellInv[cmdLower];
		        sc.isOpened = false;
		        OpenSellUI(player);
		        return;
	        }
	        if (config.cmd.newListing.ContainsKey(cmdLower))
	        {
		        sc.shopName = config.cmd.newListing[cmdLower];
		        sc.stockCache.listingPage = ListingPageType.SellOffer;
		        sc.isOpened = false;
		        OpenStockSubmitInventory(player);
		        return;
	        }
	        if (config.cmd.bankInv.ContainsKey(cmdLower))
	        {
		        sc.shopName = config.cmd.bankInv[cmdLower];
		        sc.isOpened = false;
		        OpenBankInventory(player);
		        return;
	        }
	        sc.isOpened = true;
	        if (wasInData && sc.shopName.Length > 0)
	        {
		        OpenSavedUI(player);
		        return;
	        }
	        OpenInitialUI(player);

	        if (OpenShopsRedirectCheck(player, sc)) return;
	        if (OpenStocksRedirectCheck(player, sc)) return;
	        
	        foreach (var curr in config.curr.Values)
		        if (curr.transferCfg.enabled)
		        {
			        OpenTransferUI(player);
			        break;
		        }
	        Mess(player, "NoValidPagesToOpen");
        }
		
		private void CurrencyManagementCommandOxide(ConsoleSystem.Arg arg)
		{
			BasePlayer consolePlayer = arg.Player();
			string senderId = consolePlayer ? consolePlayer.UserIDString : null;
            if (consolePlayer && !permission.UserHasPermission(senderId, "shoppystock.admin"))
            {
	            Mess(consolePlayer, "NoAdminPermission");
                return;
            }
            if (arg.Args.Length < 3)
            {
                SendReply(arg, Lang("AdminCommandHelp", senderId, config.cmd.currCmd));
                return;
            }
            string shopName = arg.Args[0];
            if (!config.curr.ContainsKey(shopName))
            {
	            SendReply(arg, Lang("CurrencyNotFound", senderId, shopName));
                return;
            }
            string action = arg.Args[1].ToLower();
            if (action != "give" && action != "take" && action != "clear" && action != "check")
            {
	            SendReply(arg, Lang("AdminCommandHelp", senderId, config.cmd.currCmd));
                return;
            }
            KeyValuePair<ulong, PlayerData> foundUser;
            ulong userId;
            if (ulong.TryParse(arg.Args[2], out userId))
            {
                if (!data.currencies.players.ContainsKey(userId))
                {
                    BasePlayer foundPlayer = BasePlayer.FindByID(userId);
                    if (foundPlayer == null)
                        AddPlayerToData(userId);
                    else
                        AddPlayerToData(foundPlayer);
                }
                foundUser = new KeyValuePair<ulong, PlayerData>(userId, data.currencies.players[userId]);
            }
            else
            {
                string displayName = arg.Args[2];
                KeyValuePair<ulong, PlayerData>[] users = data.currencies.players.Where(x => x.Value.userName.Contains(displayName)).ToArray();
                if (users.Length == 0)
                {
	                SendReply(arg, Lang("UserNotFound", senderId, displayName));
                    return;
                }
                if (users.Length > 1)
                {
	                SendReply(arg, Lang("TooManyUsersFound", senderId));
                    return;
                }
                foundUser = users[0];
            }
            if (foundUser.Key == 0) return;
            if (action == "give" || action == "take")
            {
                if (arg.Args.Length < 4)
                {
	                SendReply(arg, Lang("AdminCommandHelp", senderId, config.cmd.currCmd));
                    return;
                }
                int amount;
                if (!int.TryParse(arg.Args[3], out amount))
                {
	                SendReply(arg, Lang("WrongAmountFormat", senderId, arg.Args[3]));
                    return;
                }
                if (action == "give")
                {
	                AddCurrency(shopName, foundUser.Key, amount, true);
	                SendReply(arg, Lang("CurrencyAdded", senderId, shopName, foundUser.Value.userName, amount, GetPlayerCurrency(foundUser.Key, shopName).ToString("###,###,###,##0.##")));
                }
                else if (action == "take")
                {
	                TakeCurrency(shopName, foundUser.Key, amount, true);
	                SendReply(arg, Lang("CurrencyTaken", senderId, shopName, foundUser.Value.userName, amount, GetPlayerCurrency(foundUser.Key, shopName).ToString("###,###,###,##0.##")));
                }
            }
            else if (action == "clear")
            {
	            TakeCurrency(shopName, foundUser.Key, GetPlayerCurrency(foundUser.Key, shopName), true);
	            SendReply(arg, Lang("CurrencyCleared", senderId, shopName, foundUser.Value.userName));
            }
            else if (action == "check")
	            SendReply(arg, Lang("CurrencyCheck", senderId, shopName, foundUser.Value.userName, GetPlayerCurrency(foundUser.Key, shopName).ToString("###,###,###,##0.##")));
		}

        private void CurrencyManagementCommand(IPlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.Id, "shoppystock.admin"))
            {
                player.Message(Lang("NoAdminPermission", player.Id));
                return;
            }
            if (args.Length < 3)
            {
                player.Message(Lang("AdminCommandHelp", player.Id, config.cmd.currCmd));
                return;
            }
            string shopName = args[0];
            if (!config.curr.ContainsKey(shopName))
            {
                player.Message(Lang("CurrencyNotFound", player.Id, shopName));
                return;
            }
            string action = args[1].ToLower();
            if (action != "give" && action != "take" && action != "clear" && action != "check")
            {
                player.Message(Lang("AdminCommandHelp", player.Id, config.cmd.currCmd));
                return;
            }
            KeyValuePair<ulong, PlayerData> foundUser;
            ulong userId;
            if (ulong.TryParse(args[2], out userId))
            {
                if (!data.currencies.players.ContainsKey(userId))
                {
                    BasePlayer foundPlayer = BasePlayer.FindByID(userId);
                    if (foundPlayer == null)
                        AddPlayerToData(userId);
                    else
                        AddPlayerToData(foundPlayer);
                }
                foundUser = new KeyValuePair<ulong, PlayerData>(userId, data.currencies.players[userId]);
            }
            else
            {
                string displayName = args[2];
                KeyValuePair<ulong, PlayerData>[] users = data.currencies.players.Where(x => x.Value.userName.Contains(displayName)).ToArray();
                if (users.Length == 0)
                {
                    player.Message(Lang("UserNotFound", player.Id, displayName));
                    return;
                }
                if (users.Length > 1)
                {
                    player.Message(Lang("TooManyUsersFound", player.Id));
                    return;
                }
                foundUser = users[0];
            }
            if (foundUser.Key == 0) return;
            if (action == "give" || action == "take")
            {
                if (args.Length < 4)
                {
                    player.Message(Lang("AdminCommandHelp", player.Id, config.cmd.currCmd));
                    return;
                }
                int amount;
                if (!int.TryParse(args[3], out amount))
                {
                    player.Message(Lang("WrongAmountFormat", player.Id, args[3]));
                    return;
                }
                if (action == "give")
                {
	                AddCurrency(shopName, foundUser.Key, amount, true);
                    player.Message(Lang("CurrencyAdded", player.Id, shopName, foundUser.Value.userName, amount, GetPlayerCurrency(foundUser.Key, shopName).ToString("###,###,###,##0.##")));
                }
                else if (action == "take")
                {
	                TakeCurrency(shopName, foundUser.Key, amount, true);
                    player.Message(Lang("CurrencyTaken", player.Id, shopName, foundUser.Value.userName, amount, GetPlayerCurrency(foundUser.Key, shopName).ToString("###,###,###,##0.##")));
                }
            }
            else if (action == "clear")
            {
	            TakeCurrency(shopName, foundUser.Key, GetPlayerCurrency(foundUser.Key, shopName), true);
                player.Message(Lang("CurrencyCleared", player.Id, shopName, foundUser.Value.userName));
            }
            else if (action == "check")
                player.Message(Lang("CurrencyCheck", player.Id, shopName, foundUser.Value.userName, GetPlayerCurrency(foundUser.Key, shopName).ToString("###,###,###,##0.##")));
        }

        private void UpdatePricesConsoleCommand(ConsoleSystem.Arg arg)
        {
	        BasePlayer player = arg.Player();
	        string userId = player ? player.UserIDString : null;
	        if (arg.Args.Length == 0)
	        {
		        SendReply(arg, Lang("UpdatePricesHelp", userId));
		        return;
	        }
	        if (arg.Args.Length < 3)
	        {
		        string shopKey = arg.Args[0];
		        if (!config.curr.ContainsKey(shopKey))
		        {
			        SendReply(arg, Lang("ShopNotFound", userId, shopKey));
			        return;
		        }
		        int loopCount = 1;
		        if (arg.Args.Length == 1 || (arg.Args.Length == 2 && !int.TryParse(arg.Args[1], out loopCount)))
			        loopCount = 1;
		        for (int i = 0; i < loopCount - 1; i++)
					UpdateAllItems(shopKey);
		        UpdateAllItems(shopKey, true);
		        SendReply(arg, Lang("UpdatedAllItemPrices", userId, shopKey, loopCount));
	        }
	        else
	        {
		        string shopKey = arg.Args[0];
		        if (!config.curr.ContainsKey(shopKey))
		        {
			        SendReply(arg, Lang("ShopNotFound", userId, shopKey));
			        return;
		        }
		        string shortname = arg.Args[1];
		        if (!ulong.TryParse(arg.Args[2], out ulong skinId))
		        {
			        SendReply(arg, Lang("NotValidSkinId", userId, arg.Args[2]));
			        return;
		        }
		        int loopCount = 1;
		        if (arg.Args.Length < 4 || !int.TryParse(arg.Args[3], out loopCount))
			        loopCount = 1;
		        for (int i = 0; i < loopCount - 1; i++)
			        RollPrices(shopKey, shortname, skinId);
		        RollPrices(shopKey, shortname, skinId); 
		        TryUpdatePricesUI(shopKey, shortname, skinId);
		        SendReply(arg, Lang("UpdatedCertainItemPrices", userId, shopKey, shortname, skinId, loopCount));
	        }
        }

        private void ShoppyStockConsoleCommand(ConsoleSystem.Arg arg)
        {
	        BasePlayer player = arg.Player();
	        ShoppyCache sc = cache[player.userID];
	        if (config.ui.cooldown > 0)
	        {
		        DateTime now = DateTime.Now;
		        if ((now - sc.lastAction).TotalSeconds < config.ui.cooldown) return;
		        sc.lastAction = now;
	        }
	        LogToConsole("PlayerConsoleCommand", player.displayName, player.userID, string.Join(' ', arg.Args));
	        if (config.ui.clickSound.Length > 0)
		        SendEffect(player, config.ui.clickSound);
	        switch (arg.Args[0])
	        {
		        case "close":
			        sc.isOpened = false;
			        CuiHelper.DestroyUi(player, "ShoppyStockUI");
			        break;
		        case "type":
			        sc.stockCache.isWatchingHistory = false;
			        switch (arg.Args[1])
			        {
				        case "shop":
					        if (sc.type == ShoppyType.Shop && sc.shopName.Length == 0) return;
					        OpenShopsRedirectCheck(player, sc);
					        break;
				        case "stock":
					        if (sc.type == ShoppyType.Stock && sc.shopName.Length == 0) return;
					        OpenStocksRedirectCheck(player, sc);
					        break;
				        case "transfer":
					        if (sc.type == ShoppyType.Transfer) return;
					        OpenTransferUI(player);
					        break;
				        case "exchange":
					        if (sc.type == ShoppyType.Exchange) return;
					        OpenExchangeUI(player);
					        break;
				        case "deposit":
					        if (sc.type == ShoppyType.Deposit) return;
					        OpenDepositWithdrawUI(player);
					        break;
			        }
			        break;
		        case "shop":
			        switch (arg.Args[1])
			        {
				        case "category":
					        sc.shopCache.search = string.Empty;
					        SwitchShopCategory(player, arg.Args[2]);
					        break;
				        case "search":
					        if (arg.Args.Length < 3)
					        {
						        if (sc.shopCache.search.Length > 0)
						        {
							        sc.shopCache.search = string.Empty;
							        SwitchShopCategory(player, "Search");
						        }
						        return;
					        }
					        string search = string.Join(' ', arg.Args.Skip(2));
					        if (search == Lang("TypeHere", player.UserIDString)) return;
					        if (sc.shopCache.search == search && sc.shopCache.category == "Search") return;
					        sc.shopCache.search = search;
					        SwitchShopCategory(player, "Search");
					        break;
				        case "affordableOnly":
					        sc.shopCache.showOnlyAffordable = !sc.shopCache.showOnlyAffordable;
					        SwitchShopCategory(player, sc.shopCache.category, true);
					        break;
				        case "sort":
					        switch (arg.Args[2])
					        {
						        case "toggle":
							        sc.shopCache.showSort = !sc.shopCache.showSort;
							        UpdateShopSortUI(player);
							        break;
						        case "none":
							        UpdateShopSortType(player, ShopSortType.Default);
							        break;
						        case "alphabet":
							        UpdateShopSortType(player, ShopSortType.Alphabetical);
							        break;
						        case "popularity":
							        UpdateShopSortType(player, ShopSortType.Popularity);
							        break;
						        case "cheapest":
							        UpdateShopSortType(player, ShopSortType.Cheapest);
							        break;
					        }
					        break;
				        case "listing":
					        string listingKey = arg.Args[2];
					        ShowShopListingUI(player, listingKey);
					        break;
				        case "goBack":
					        CuiHelper.DestroyUi(player, "ShoppyStockUI_ShopListingCore");
					        break;
				        case "setAmount":
					        if (arg.Args.Length < 3) return;
					        if (!int.TryParse(arg.Args[2], out int amount)) return;
					        if (amount < 1) return;
					        if (amount >= sc.shopCache.maxAmount)
						        amount = sc.shopCache.maxAmount;
					        sc.shopCache.amount = amount;
					        UpdateShopListingAmount(player);
					        break;
				        case "increase":
					        if (sc.shopCache.amount >= sc.shopCache.maxAmount) return;
					        sc.shopCache.amount++;
					        UpdateShopListingAmount(player);
					        break;
				        case "decrease":
					        if (sc.shopCache.amount <= 1) return;
					        sc.shopCache.amount--;
					        UpdateShopListingAmount(player);
					        break;
				        case "purchase":
					        HandleShopPurchase(player);
					        break;
				        case "listingCountdown":
					        CuiHelper.DestroyUi(player, "ShoppyStockUI_ShopListingCore");
					        ShowShopListingUI(player, sc.shopCache.listingKey);
					        break;
			        }
			        break;
		        case "stock":
			        switch (arg.Args[1])
			        {
				        case "category":
					        sc.stockCache.search = string.Empty;
					        string cat = arg.Args[2];
					        if (cat == StringKeys.Cat_Bank)
					        {
						        CuiHelper.DestroyUi(player, "ShoppyStockUI");
						        OpenBankInventory(player);
					        }
					        else
						        SwitchStockCategory(player, cat);
					        break;
				        case "search":
					        if (arg.Args.Length < 3)
					        {
						        if (sc.stockCache.search.Length > 0)
						        {
							        sc.stockCache.search = string.Empty;
							        SwitchStockCategory(player, "Search");
						        }
						        return;
					        }
					        string search = string.Join(' ', arg.Args.Skip(2));
					        if (search == Lang("TypeHere", player.UserIDString)) return;
					        if (sc.stockCache.search == search && sc.stockCache.category == "Search") return;
					        sc.stockCache.search = search;
					        SwitchStockCategory(player, "Search");
					        break;
				        case "sort":
					        switch (arg.Args[2])
					        {
						        case "name":
							        if (sc.stockCache.sortType == StockSortType.Name) return;
							        sc.stockCache.sortType = StockSortType.Name;
							        RedrawStockListings(player);
							        break;
						        case "serverSell":
							        if (sc.stockCache.sortType == StockSortType.ServerSell) return;
							        sc.stockCache.sortType = StockSortType.ServerSell;
							        RedrawStockListings(player);
							        break;
						        case "buyOffers":
							        if (sc.stockCache.sortType == StockSortType.BuyOffer) return;
							        sc.stockCache.sortType = StockSortType.BuyOffer;
							        RedrawStockListings(player);
							        break;
						        case "sellOffers":
							        if (sc.stockCache.sortType == StockSortType.SellOffer) return;
							        sc.stockCache.sortType = StockSortType.SellOffer;
							        RedrawStockListings(player);
							        break;
					        }
					        break;
				        case "listing":
					        string shortname = arg.Args[2];
					        ulong skinId = ulong.Parse(arg.Args[3]);
					        ShowStockListingDetails(player, shortname, skinId);
					        break;
				        case "favourite":
					        shortname = arg.Args[2];
					        skinId = ulong.Parse(arg.Args[3]);
					        UpdateFavourite(player, shortname, skinId);
					        break;
				        case "openSell":
					        CuiHelper.DestroyUi(player, "ShoppyStockUI");
					        OpenSellUI(player);
					        break;
				        case "history":
					        OpenStockActionsUI(player);
					        break;
				        case "hideEmpty":
					        sc.stockCache.hideEmpty = !sc.stockCache.hideEmpty;
					        SwitchStockCategory(player, sc.stockCache.category);
					        break;
				        case "goBack":
					        sc.stockCache.shortname = string.Empty;
					        CuiHelper.DestroyUi(player, "ShoppyStockUI_StockListingCore");
					        break;
				        case "return":
					        sc.stockCache.isWatchingHistory = false;
					        OpenStockUI(player);
					        break;
				        case "openServerSell":
					        if (sc.stockCache.listingPage == ListingPageType.ServerSell) return;
					        SwitchStockDetailsCategory(player, ListingPageType.ServerSell);
					        break;
				        case "openBuyOffers":
					        if (sc.stockCache.listingPage == ListingPageType.BuyOffer) return;
					        SwitchStockDetailsCategory(player, ListingPageType.BuyOffer);
					        break;
				        case "openSellOffers":
					        if (sc.stockCache.listingPage == ListingPageType.SellOffer) return;
					        SwitchStockDetailsCategory(player, ListingPageType.SellOffer);
					        break;
				        case "serverSell":
					        switch (arg.Args[2])
					        {
						        case "priceAlert":
							        if (arg.Args.Length < 4 || !float.TryParse(arg.Args[3], out float targetPrice)) return;
							        SetAlertPrice(player, targetPrice, true);
							        break;
						        case "autoSell":
							        if (arg.Args.Length < 4 || !float.TryParse(arg.Args[3], out float sellPrice)) return;
							        SetAlertPrice(player, sellPrice, false);
							        break;
						        case "sellFromBank":
							        SellItemsToStock(player, true);
							        break;
						        case "timespan":
							        int newTimespan = int.Parse(arg.Args[3]);
							        if (newTimespan == sc.stockCache.currentTimespan) return;
							        int oldTimespan = sc.stockCache.currentTimespan;
							        sc.stockCache.currentTimespan = newTimespan;
							        SwitchGraphTimespan(player, newTimespan, oldTimespan);
							        break;
						        case "purchaseAmount":
							        if (arg.Args.Length < 4 || !int.TryParse(arg.Args[3], out int amount)) return;
							        if (amount < 0) return;
							        sc.stockCache.stockActionAmount = amount;
							        UpdateServerSellUI(player);
							        break;
						        case "buyFromServer":
							        TryBuyFromServer(player);
							        break;
					        }
					        break;
				        case "buySellOffer":
					        switch (arg.Args[2])
					        {
						        case "new":
							        CuiHelper.DestroyUi(player, "ShoppyStockUI");
							        OpenStockSubmitInventory(player);
							        break;
						        case "setAmount":
							        if (arg.Args.Length < 4 || !int.TryParse(arg.Args[3], out int amount)) return;
							        sc.stockCache.stockActionAmount = amount;
							        UpdateNewListingUI(player);
							        break;
						        case "setPrice":
							        if (arg.Args.Length < 4 || !float.TryParse(arg.Args[3], out float price)) return;
							        SetOfferPrice(player, price);
							        UpdateNewListingUI(player);
							        break;
						        case "prevTime":
							        if (sc.stockCache.listingPage == ListingPageType.BuyOffer)
								        sc.stockCache.buyOfferTaxIndex--;
							        else if (sc.stockCache.listingPage == ListingPageType.SellOffer)
								        sc.stockCache.sellOfferTaxIndex--;
							        UpdateNewListingUI(player);
							        break;
						        case "nextTime":
							        if (sc.stockCache.listingPage == ListingPageType.BuyOffer)
								        sc.stockCache.buyOfferTaxIndex++;
							        else if (sc.stockCache.listingPage == ListingPageType.SellOffer)
								        sc.stockCache.sellOfferTaxIndex++;
							        UpdateNewListingUI(player);
							        break;
						        case "trySubmit":
							        TryAddBuySellRequest(player);
							        break;
						        case "switchBroadcast":
							        sc.stockCache.broadcastNewListing = !sc.stockCache.broadcastNewListing;
							        UpdateNewListingUI(player);
							        break;
						        case "details":
							        int listingId = int.Parse(arg.Args[3]);
							        UpdateBuySellOfferDetailsUI(player, listingId);
							        break;
						        case "hideMine":
							        sc.stockCache.hideMyListings = !sc.stockCache.hideMyListings;
							        SwitchStockDetailsCategory(player, sc.stockCache.listingPage);
							        break;
						        case "decrease":
							        sc.stockCache.stockActionAmount--;
							        if (sc.stockCache.stockActionAmount < 0)
								        sc.stockCache.stockActionAmount = 0;
							        UpdateBuySellOfferDetailsUI(player);
							        break;
						        case "increase":
							        sc.stockCache.stockActionAmount++;
							        UpdateBuySellOfferDetailsUI(player);
							        break;
						        case "setItemAmount":
							        if (arg.Args.Length < 4 || !int.TryParse(arg.Args[3], out amount)) return;
							        sc.stockCache.stockActionAmount = amount;
							        UpdateBuySellOfferDetailsUI(player);
							        break;
						        case "purchase":
							        TryPurchaseFromStockPlayer(player);
							        break;
						        case "visibility":
							        sc.stockCache.queuedAction = QueuedStockAction.OfferVisibilityToggle;
							        UpdateBuySellOfferDetailsUI(player);
							        break;
						        case "remove":
							        sc.stockCache.queuedAction = QueuedStockAction.RemoveOffer;
							        UpdateBuySellOfferDetailsUI(player);
							        break;
					        }
					        break;
			        }
			        break;
		        case "bank":
			        switch (arg.Args[1])
			        {
				        case "depositAll":
					        TryAddToBank(player, true);
					        break;
				        case "item":
					        string shortname = arg.Args[2];
					        ulong skin = ulong.Parse(arg.Args[3]);
					        if (sc.stockCache.bankShortname == shortname && sc.stockCache.bankSkin == skin) return;
					        sc.stockCache.bankShortname = shortname;
					        sc.stockCache.bankSkin = skin;
					        sc.stockCache.bankAmount = 0;
					        UpdateWithdrawPanelUI(player);
					        break;
				        case "submit":
					        TryAddToBank(player);
					        break;
				        case "withdrawAmount":
					        if (arg.Args.Length < 3 || !int.TryParse(arg.Args[2], out int amount)) return;
					        sc.stockCache.bankAmount = amount;
					        break;
				        case "withdraw":
					        WithdrawFromBank(player);
					        break;
			        }
			        break;
		        case "sellStock":
			        SellItemsToStock(player, false);
			        break;
		        case "stockSort":
			        switch (arg.Args[1])
			        {
				        case "toggle":
					        StockActionType type = Enum.Parse<StockActionType>(arg.Args[2], false);
					        if (sc.stockCache.disabledStockMessages.Contains(type))
						        sc.stockCache.disabledStockMessages.Remove(type);
							else
								sc.stockCache.disabledStockMessages.Add(type);
					        SwitchStockSortType(player, type);
					        break;
				        case "details":
					        //TryRedirectToStockAction(player, id);
					        break;
			        }
			        break;
		        case "transfer":
			        switch (arg.Args[1])
			        {
				        case "online":
					        sc.transferCache.showOffline = !sc.transferCache.showOffline;
					        OpenTransferUI(player);
					        break;
				        case "search":
					        if (arg.Args.Length < 3)
					        {
						        if (sc.transferCache.search.Length > 0)
						        {
							        sc.transferCache.search = string.Empty;
							        OpenTransferUI(player);
						        }
						        return;
					        }
					        string search = string.Join(' ', arg.Args.Skip(2));
					        if (search == Lang("TypeHere", player.UserIDString)) return;
					        if (sc.transferCache.search == search) return;
					        sc.transferCache.search = search;
					        OpenTransferUI(player);
					        break;
				        case "select":
					        ulong userId = ulong.Parse(arg.Args[2]);
					        sc.transferCache.selectedPlayer = userId;
					        OpenPlayerTransferUI(player);
					        break;
				        case "goBack":
					        CuiHelper.DestroyUi(player, "ShoppyStockUI_TransferUserCore");
					        break;
				        case "currency":
					        string currKey = arg.Args[2];
					        if (sc.transferCache.selectedCurrency == currKey) return;
					        UpdateTransferCurrency(player, currKey);
					        break;
				        case "amount":
					        if (arg.Args.Length < 3 || !float.TryParse(arg.Args[2], out float amount))
					        {
						        sc.transferCache.transferAmount = 0;
						        UpdateTransferCurrency(player);
						        return;
					        }
					        if (amount < 0)
						        amount = 0;
					        sc.transferCache.transferAmount = amount;
					        UpdateTransferCurrency(player);
					        break;
				        case "confirm":
					        ConfirmCurrencyTransfer(player);
					        break;
			        }
			        break;
		        case "deposit":
			        switch (arg.Args[1])
			        {
				        case "open":
					        CuiHelper.DestroyUi(player, "ShoppyStockUI");
					        OpenCurrencyDepositUI(player);
					        break;
				        case "deposit":
					        TryDepositCurrency(player);
					        break;
			        }
			        break;
		        case "withdraw":
			        switch (arg.Args[1])
			        {
				        case "amount":
					        if (arg.Args.Length < 5 || !int.TryParse(arg.Args[4], out int amount)) return;
					        if (amount < 0)
						        amount = 0;
					        string shortname = arg.Args[2];
					        ulong skin = ulong.Parse(arg.Args[3]);
					        sc.depositCache.lastInputShortname = shortname;
					        sc.depositCache.lastInputSkin = skin;
					        sc.depositCache.inputAmount = amount;
					        UpdateWithdrawCurrency(player);
					        break;
				        case "withdraw":
					        if (sc.depositCache.lastInputShortname.Length == 0 || sc.depositCache.inputAmount == 0) return;
					        WithdrawCurrency(player);
					        break;
			        }
			        break;
		        case "exchange":
			        switch (arg.Args[1])
			        {
				        case "input":
					        int counter = int.Parse(arg.Args[2]);
					        if (arg.Args.Length < 4 || !int.TryParse(arg.Args[3], out int amount)) return;
					        if (amount < 0)
						        amount = 0;
					        sc.exchangeCache.currentId = counter;
					        sc.exchangeCache.amount = amount;
					        UpdateExchangeCurrency(player);
					        break;
				        case "exchange":
					        counter = int.Parse(arg.Args[2]);
					        if (sc.exchangeCache.currentId != counter || sc.exchangeCache.amount <= 0) return;
					        TryExchangeCurrency(player, counter);
					        break;
			        }
			        break;
		        case "openShop": 
			        sc.shopName = arg.Args[1];
			        OpenShopUI(player);
			        break;
		        case "openStock":
			        sc.shopName = arg.Args[1];
			        OpenStockUI(player);
			        break;
		        case "leaderboard":
			        OpenLeaderboardUI(player);
			        break;
	        }
        }

        private void ShoppyStockAdminConsoleCommand(ConsoleSystem.Arg arg)
        {
	        BasePlayer player = arg.Player();
	        if (!permission.UserHasPermission(player.UserIDString, "shoppystock.admin")) return;
	        switch (arg.Args[0])
	        {
		        case "close":
			        //ConfigEditorCache.enterTree.Clear();
			        ConfigEditorCache.isBeingEdited = false;
			        CuiHelper.DestroyUi(player, "ShoppyAdminUI");
			        break;
		        case "save":
			        SaveAndOverrideConfigs();
			        OpenAdminSettingsPage(player);
			        ShowPopUp(player, Lang("ConfigSaved", player.UserIDString));
			        break;
		        case "revert":
			        LoadDefaultEditingConfig();
			        OpenAdminSettingsPage(player);
			        ShowPopUp(player, Lang("ConfigReverted", player.UserIDString));
			        break;
		        case "goBack":
		        case "goBackSub":
			        if (arg.Args[0] == "goBackSub")
				        CuiHelper.DestroyUi(player, "ShoppyAdminUI_SubPage");
			        ConfigEditorCache.cachedKey = null;
			        ConfigEditorCache.cachedValue = null;
			        bool any = ConfigEditorCache.enterTree.Count > 0;
			        if (!any) return;
			        ConfigEditorCache.enterTree.Remove(ConfigEditorCache.enterTree[^1]);
			        any = ConfigEditorCache.enterTree.Count > 0;
			        if (any && ConfigEditorCache.enterTree[^1].StartsWith("d--"))
			        {
				        OpenSubAdminPage(player);
				        DrawAdminDictionaryOptions(player);
			        }
			        else if (any && ConfigEditorCache.enterTree[^1].StartsWith("l--"))
			        {
				        OpenSubAdminPage(player);
				        DrawAdminListOptions(player);
			        }
			        else
						OpenAdminSettingsPage(player);
			        break;
		        case "editClass":
			        string fieldName = arg.Args[1];
			        ConfigEditorCache.enterTree.Add(fieldName);
			        OpenAdminSettingsPage(player);
			        break;
		        case "editDictionary":
			        fieldName = arg.Args[1];
			        ConfigEditorCache.enterTree.Add("d--" + fieldName);
			        OpenSubAdminPage(player);
			        DrawAdminDictionaryOptions(player);
			        break;
		        case "editList":
			        fieldName = arg.Args[1];
			        ConfigEditorCache.enterTree.Add("l--" + fieldName);
			        OpenSubAdminPage(player);
			        DrawAdminListOptions(player);
			        break;
		        case "stringInput":
			        if (arg.Args.Length < 3)
			        {
						ShowPopUp(player, Lang("InvalidInputFormat", player.UserIDString));
				        return;
			        }
			        fieldName = arg.Args[1];
			        string stringInput = string.Join(' ', arg.Args.Skip(2));
			        if (fieldName == "newRecord")
			        {
				        ConfigEditorCache.cachedValue = stringInput;
				        return;
			        }
			        EditConfigField(player, typeof(string), fieldName, stringInput);
			        break;
		        case "intInput":
			        if (arg.Args.Length < 3 || !int.TryParse(arg.Args[2], out int intInput))
			        {
				        ShowPopUp(player, Lang("InvalidInputFormat", player.UserIDString));
				        return;
			        }
			        fieldName = arg.Args[1];
			        if (fieldName == "newRecord")
			        {
				        ConfigEditorCache.cachedValue = intInput;
				        return;
			        }
			        EditConfigField(player, typeof(int), fieldName, intInput);
			        ShowPopUp(player, Lang("FieldSuccessfullyEdited", player.UserIDString));
			        break;
		        case "floatInput":
			        if (arg.Args.Length < 3 || !float.TryParse(arg.Args[2], out float floatInput))
			        {
				        ShowPopUp(player, Lang("InvalidInputFormat", player.UserIDString));
				        return;
			        }
			        fieldName = arg.Args[1];
			        if (fieldName == "newRecord")
			        {
				        ConfigEditorCache.cachedValue = floatInput;
				        return;
			        }
			        EditConfigField(player, typeof(float), fieldName, floatInput);
			        ShowPopUp(player, Lang("FieldSuccessfullyEdited", player.UserIDString));
			        break;
		        case "ulongInput":
			        if (arg.Args.Length < 3 || !ulong.TryParse(arg.Args[2], out ulong ulongInput))
			        {
				        ShowPopUp(player, Lang("InvalidInputFormat", player.UserIDString));
				        return;
			        }
			        fieldName = arg.Args[1];
			        if (fieldName == "newRecord")
			        {
				        ConfigEditorCache.cachedValue = ulongInput;
				        return;
			        }
			        EditConfigField(player, typeof(ulong), fieldName, ulongInput);
			        ShowPopUp(player, Lang("FieldSuccessfullyEdited", player.UserIDString));
			        break;
		        case "boolButton":
			        fieldName = arg.Args[1];
			        EditConfigField(player, typeof(bool), fieldName);
			        ShowPopUp(player, Lang("FieldSuccessfullyEdited", player.UserIDString));
			        break;
		        case "listRemove":
			        int index = int.Parse(arg.Args[1]);
			        RemoveConfigRecord(player, index);
			        break;
		        case "listAdd":
			        AddRecord(player);
			        break;
		        case "dictKeyInput":
			        string inputType = arg.Args[1];
			        string fieldKey = arg.Args[2];
			        if (arg.Args.Length < 3) return;
			        if (inputType == "int" && int.TryParse(arg.Args[3], out intInput))
			        {
				        if (fieldKey == "newRecord")
				        {
					        ConfigEditorCache.cachedKey = intInput;
					        return;
				        }
				        UpdateDictKey(player, fieldKey, intInput);
			        }
			        else if (inputType == "float" && float.TryParse(arg.Args[3], out floatInput))
			        {
				        if (fieldKey == "newRecord")
				        {
					        ConfigEditorCache.cachedKey = floatInput;
					        return;
				        }
				        UpdateDictKey(player, fieldKey, floatInput);
			        }
			        else if (inputType == "ulong" && ulong.TryParse(arg.Args[3], out ulongInput))
			        {
				        if (fieldKey == "newRecord")
				        {
					        ConfigEditorCache.cachedKey = ulongInput;
					        return;
				        }
				        UpdateDictKey(player, fieldKey, ulongInput);
			        }
			        else if (inputType == "string")
			        {
				        string input = string.Join(' ', arg.Args.Skip(3));
				        if (fieldKey == "newRecord")
				        {
					        ConfigEditorCache.cachedKey = input;
					        return;
				        }
				        UpdateDictKey(player, fieldKey, input);
			        }
			        else
				        ShowPopUp(player, Lang("InvalidInputFormat", player.UserIDString));
			        break;
		        case "dictRemove":
			        string key = arg.Args[1];
			        RemoveConfigRecord(player, -1, key);
			        break;
		        case "dictAdd":
			        AddRecord(player);
			        break;
		        case "customButton":
			        string buttonName = arg.Args[1];
			        RunCustomButtonAdminMethod(player, buttonName);
			        break;
	        }
        }
        
        #endregion
        
        #region UI

        private void OpenSavedUI(BasePlayer player)
        {
	        ShoppyCache sc = cache[player.userID];
	        OpenInitialUI(player);
	        if (sc.type == ShoppyType.Shop)
		        OpenShopUI(player, true);
	        else if (sc.type == ShoppyType.Stock)
	        {
		        OpenStockUI(player, true);
		        if (sc.stockCache.shortname.Length > 0)
			        ShowStockListingDetails(player, sc.stockCache.shortname, sc.stockCache.skin);
	        }
	        else if (sc.type == ShoppyType.Transfer)
		        OpenTransferUI(player, true);
	        else if (sc.type == ShoppyType.Exchange)
		        OpenExchangeUI(player, true);
	        else if (sc.type == ShoppyType.Deposit)
		        OpenDepositWithdrawUI(player, true);
        }
        
		private void OpenInitialUI(BasePlayer player)
		{
			using CUI cui = new CUI(CuiHandler);
			ShoppyCache sc = cache[player.userID];
			
			//Element: Parent
			LUI.LuiContainer shoppyUi = cui.v2.CreateParent(CUI.ClientPanels.Overall, LuiPosition.Full, "ShoppyStockUI").AddCursor().SetDestroy("ShoppyStockUI");
			
			//Element: Background Blur
			LUI.LuiContainer backgroundBlur = cui.v2.CreatePanel(shoppyUi, LuiPosition.Full, LuiOffset.None, ColDb.BlackTrans20);
			backgroundBlur.SetMaterial("assets/content/ui/uibackgroundblur.mat");

			//Element: Background Darker
			LUI.LuiContainer backgroundDarker = cui.v2.CreatePanel(shoppyUi, LuiPosition.Full, LuiOffset.None, ColDb.BlackTrans20);
			backgroundDarker.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Center Anchor
			LUI.LuiContainer centerAnchor = cui.v2.CreatePanel(shoppyUi, LuiPosition.MiddleCenter, LuiOffset.None, ColDb.Transparent);

			//Element: Main Panel
			LUI.LuiContainer mainPanel = cui.v2.CreatePanel(centerAnchor, new LuiOffset(-550, -310, 550, 310), ColDb.DarkGray);
			mainPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Top Panel
			LUI.LuiContainer topPanel = cui.v2.CreatePanel(mainPanel, new LuiOffset(0, 578, 1100, 620), ColDb.LTD5);
			topPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Title Text
			cui.v2.CreateText(topPanel, new LuiOffset(10, 0, 1010, 42), 24, ColDb.LightGray, Lang("ShopTitle", player.UserIDString), TextAnchor.MiddleLeft, "ShoppyStockUI_TitleText");

			//Element: Close Button
			LUI.LuiContainer closeButton = cui.v2.CreateButton(topPanel, new LuiOffset(1064, 6, 1094, 36), "ShoppyStock_UI close", ColDb.RedBg);
			closeButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Close Button X
			cui.v2.CreateSprite(closeButton, new LuiOffset(6, 6, 24, 24), "assets/icons/close.png", ColDb.RedText);

			bool anyLeaderboard = false;
			foreach (var curr in config.curr.Values)
			{
				if (curr.currCfg.leaderboard)
				{
					anyLeaderboard = true;
					break;
				}
			}
			if (anyLeaderboard)
			{
				//Element: Leaderboards Button
				LUI.LuiContainer leaderboardsButton = cui.v2.CreateButton(topPanel, new LuiOffset(1028, 6, 1058, 36), "ShoppyStock_UI leaderboard", ColDb.LTD10);
				leaderboardsButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Leaderboards Button Image
				FindAndCreateValidImage(cui, leaderboardsButton, new LuiOffset(6, 6, 24, 24), config.ui.icons.trophyUrl, ColDb.LTD40);
			}
			
			//Element: Category Select
			LUI.LuiContainer categorySelect = cui.v2.CreatePanel(mainPanel, new LuiOffset(0, 528, 1100, 578), ColDb.BlackTrans20);
			categorySelect.SetMaterial("assets/content/ui/namefontmaterial.mat");

			int startX = 32;

			bool showButton = false;
			NPCConfig npc = null;
			if (sc.npcId.Length > 0 && config.npcs.TryGetValue(sc.npcId, out npc))
				showButton = npc.shops.Count > 0;

			if (!showButton && npc == null)
			{
				foreach (var curr in config.curr.Values)
				{
					if (!curr.shopCfg.enabled) continue;
					if (curr.shopCfg.perm.Length > 0 && !permission.UserHasPermission(player.UserIDString, curr.shopCfg.perm)) continue;
					showButton = true;
					break;
				}
			}

			if (showButton)
			{
				//Element: Shop Button
				LUI.LuiContainer shopButton = cui.v2.CreateButton(categorySelect, new LuiOffset(startX, 0, startX + 196, 36), "ShoppyStock_UI type shop", ColDb.Transparent, name: "ShoppyStockUI_ShopButton");
				shopButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Shop Button Text
				cui.v2.CreateText(shopButton, LuiPosition.Full, LuiOffset.None, 22, ColDb.LTD60, Lang("ShopButton", player.UserIDString), TextAnchor.MiddleCenter, "ShoppyStockUI_ShopButtonText");
				startX += 210;
			}
			showButton = false;
			if (npc != null)
				showButton = npc.stocks.Count > 0;
			if (!showButton && npc == null)
			{
				foreach (var curr in config.curr.Values)
				{
					if (!curr.stockCfg.enabled) continue;
					if (curr.stockCfg.perm.Length > 0 && !permission.UserHasPermission(player.UserIDString, curr.stockCfg.perm)) continue;
					showButton = true;
					break;
				}
			}
			if (showButton)
			{
				//Element: Stock Button
				LUI.LuiContainer stockButton = cui.v2.CreateButton(categorySelect, new LuiOffset(startX, 0, startX + 196, 36), "ShoppyStock_UI type stock", ColDb.Transparent, name: "ShoppyStockUI_StockButton");
				stockButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Stock Button Text
				cui.v2.CreateText(stockButton, LuiPosition.Full, LuiOffset.None, 22, ColDb.LTD60, Lang("StockMarketButton", player.UserIDString), TextAnchor.MiddleCenter, "ShoppyStockUI_StockButtonText");
				startX += 210;
			}
			
			showButton = false;
			if (npc != null)
				showButton = npc.transfer;
			if (!showButton && npc == null)
			{
				foreach (var curr in config.curr.Values)
				{
					if (!curr.transferCfg.enabled) continue;
					showButton = true;
					break;
				}
			}

			if (showButton)
			{
				//Element: Transfer Button
				LUI.LuiContainer transferButton = cui.v2.CreateButton(categorySelect, new LuiOffset(startX, 0, startX + 196, 36), "ShoppyStock_UI type transfer", ColDb.Transparent, name: "ShoppyStockUI_TransferButton");
				transferButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Transfer Button Text
				cui.v2.CreateText(transferButton, LuiPosition.Full, LuiOffset.None, 22, ColDb.LTD60, Lang("TransferButton", player.UserIDString), TextAnchor.MiddleCenter, "ShoppyStockUI_TransferButtonText");
				startX += 210;
			}
			
			showButton = false;
			if (npc != null)
				showButton = npc.exchanges;
			
			if (!showButton && npc == null)
			{
				foreach (var curr in config.curr.Values)
				{
					if (!curr.exchangeCfg.enabled) continue;
					foreach (var exchg in curr.exchangeCfg.exchanges)
					{
						if (exchg.perm.Length > 0 && !permission.UserHasPermission(player.UserIDString, exchg.perm)) continue;
						showButton = true;
						break;
					}
					if (showButton) break;
				}
			}

			if (showButton)
			{
				//Element: Exchange Button
				LUI.LuiContainer exchangeButton = cui.v2.CreateButton(categorySelect, new LuiOffset(startX, 0, startX + 196, 36), "ShoppyStock_UI type exchange", ColDb.Transparent, name: "ShoppyStockUI_ExchangeButton");
				exchangeButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Exchange Button Text
				cui.v2.CreateText(exchangeButton, LuiPosition.Full, LuiOffset.None, 22, ColDb.LTD60, Lang("ExchangeButton", player.UserIDString), TextAnchor.MiddleCenter, "ShoppyStockUI_ExchangeButtonText");
				startX += 210;
			}
			
			showButton = false;
			if (npc != null)
				showButton = npc.deposits;
			if (!showButton && npc == null)
			{
				foreach (var curr in config.curr.Values)
				{
					if (!curr.depositCfg.deposit && !curr.depositCfg.withdraw) continue;
					showButton = true;
					break;
				}
			}

			if (showButton)
			{
				//Element: Deposit Button
				LUI.LuiContainer depositButton = cui.v2.CreateButton(categorySelect, new LuiOffset(startX, 0, startX + 196, 36), "ShoppyStock_UI type deposit", ColDb.Transparent, name: "ShoppyStockUI_DepositButton");
				depositButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Deposit Button Text
				cui.v2.CreateText(depositButton, LuiPosition.Full, LuiOffset.None, 22, ColDb.LTD60, Lang("DepositWithdrawButton", player.UserIDString), TextAnchor.MiddleCenter, "ShoppyStockUI_DepositButtonText");
			}
			
			//Element: Element Parent
			LUI.LuiContainer elementParent = cui.v2.CreateEmptyContainer(mainPanel.name, "ShoppyStockUI_ElementParent").SetOffset(new LuiOffset(0, 0, 1100, 528));
			cui.v2.elements.Add(elementParent);
			cui.v2.SendUi(player);
		}

		private bool OpenShopsRedirectCheck(BasePlayer player, ShoppyCache sc)
		{
			List<string> validShops = Pool.Get<List<string>>();
			validShops.Clear();
			foreach (var curr in config.curr)
			{
				ShopConfig shopCfg = curr.Value.shopCfg;
				if (!shopCfg.enabled) continue;
				if (sc.npcId.Length > 0 && !config.npcs[sc.npcId].shops.ContainsKey(curr.Key)) continue;
				if (shopCfg.perm.Length > 0 && !permission.UserHasPermission(player.UserIDString, shopCfg.perm)) continue;
				validShops.Add(curr.Key);
			}
			if (validShops.Count > 1)
				OpenShopStockSelectUI(player, true);
			else if (validShops.Count == 1)
			{
				sc.shopName = validShops[0];
				OpenShopUI(player);
			}
			bool valid = validShops.Count > 0;
			Pool.FreeUnmanaged(ref validShops);
			return valid;
		}

		private bool OpenStocksRedirectCheck(BasePlayer player, ShoppyCache sc)
		{
			List<string> validShops = Pool.Get<List<string>>();
			validShops.Clear();
			foreach (var curr in config.curr)
			{
				StockConfig stockCfg = curr.Value.stockCfg;
				if (!stockCfg.enabled) continue;
				if (stockCfg.perm.Length > 0 && !permission.UserHasPermission(player.UserIDString, stockCfg.perm)) continue;
				validShops.Add(curr.Key);
			}
			if (validShops.Count > 1)
				OpenShopStockSelectUI(player, false);
			else if (validShops.Count == 1)
			{
				sc.shopName = validShops[0];
				OpenStockUI(player);
			}
			bool valid = validShops.Count > 0;
			Pool.FreeUnmanaged(ref validShops);
			return valid;
		}

		private void OpenShopStockSelectUI(BasePlayer player, bool isShop)
		{
			using CUI cui = new CUI(CuiHandler);
			ShoppyCache sc = cache[player.userID];
			StringBuilder sb = Pool.Get<StringBuilder>();
			ShoppyType type = isShop ? ShoppyType.Shop : ShoppyType.Stock;
			SwitchTypeUI(cui, sc, type, sb);
			
			//Element: Element Parent
			LUI.LuiContainer elementParent = cui.v2.CreateEmptyContainer("ShoppyStockUI_ElementParent", "ShoppyStockUI_ElementCore").SetOffset(new LuiOffset(0, 0, 1100, 528));
			cui.v2.elements.Add(elementParent);
			
			//Element: Select Shop Title
			string textSwitch = isShop ? Lang("SelectShopHint", player.UserIDString) : Lang("SelectStockHint", player.UserIDString);
			cui.v2.CreateText(elementParent, new LuiOffset(0, 480, 1100, 528), 25, ColDb.LTD80, textSwitch, TextAnchor.MiddleCenter);
 
			List<string> validShops = Pool.Get<List<string>>();
			validShops.Clear();
			foreach (var curr in config.curr)
			{
				if (isShop)
				{
					ShopConfig shopCfg = curr.Value.shopCfg;
					if (!shopCfg.enabled) continue;
					if (sc.npcId.Length > 0 && !config.npcs[sc.npcId].shops.ContainsKey(curr.Key)) continue;
					if (shopCfg.perm.Length > 0 && !permission.UserHasPermission(player.UserIDString, shopCfg.perm)) continue;
					validShops.Add(curr.Key);
				}
				else
				{
					StockConfig stockCfg = curr.Value.stockCfg;
					if (!stockCfg.enabled) continue;
					if (sc.npcId.Length > 0 && !config.npcs[sc.npcId].stocks.Contains(curr.Key)) continue;
					if (stockCfg.perm.Length > 0 && !permission.UserHasPermission(player.UserIDString, stockCfg.perm)) continue;
					validShops.Add(curr.Key);
				}
			}
			int width = 1068;
			if (validShops.Count > 4)
				width = 259 * validShops.Count - 32;
				
			//Element: Shop Select Scroll
			LuiScrollbar shopSelect_Horizontal = new LuiScrollbar()
			{
				invert = true,
				autoHide = true,
				size = 8,
				handleColor = ColDb.LTD15,
				highlightColor = ColDb.LTD20,
				pressedColor = ColDb.LTD10,
				trackColor = ColDb.BlackTrans10
			};
			LUI.LuiContainer shopSelectScroll = cui.v2.CreateScrollView(elementParent, new LuiOffset(16, 28, 1084, 480), false, true, ScrollRect.MovementType.Elastic, 0.1f, true, 0.1f, 25f, default, shopSelect_Horizontal);
			shopSelectScroll.SetScrollContent(new LuiPosition(0, 0, 0, 1), new LuiOffset(0, 0, width, 0));

			//Element: Scrollable Holder
			cui.v2.CreatePanel(shopSelectScroll, LuiPosition.Full, LuiOffset.None, ColDb.Transparent);

			int startX;
			switch (validShops.Count)
			{
				case 1:
					startX = 421;
					break;
				case 2:
					startX = 291;
					break;
				case 3:
					startX = 161;
					break;
				case 4:
					startX = 32;
					break;
				default:
					startX = 0;
					break;
			}
			PlayerData pd = data.currencies.players[player.userID];
			foreach (var shop in validShops)
			{
				//Element: Open Shop Panel
				LUI.LuiContainer openShopPanel = cui.v2.CreatePanel(shopSelectScroll, new LuiOffset(startX, 28, startX + 227, 460), ColDb.LTD5);
				openShopPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");
				
				CurrencyConfig currCfg = config.curr[shop].currCfg;
				
				//Element: Shop Icon
				FindAndCreateValidImage(cui, openShopPanel, new LuiOffset(48, 269, 179, 400), currCfg.icon, ColDb.LTD80);
				
				//Element: Shop Info
				LUI.LuiContainer shopInfo = cui.v2.CreatePanel(openShopPanel, new LuiOffset(0, 0, 227, 237), ColDb.LTD10);
				shopInfo.SetMaterial("assets/content/ui/namefontmaterial.mat");

				string langKey = sb.Clear().Append(isShop ? "Shop_" : "Stock_").Append(shop).ToString();
				
				//Element: Shop Name
				cui.v2.CreateText(shopInfo, new LuiOffset(0, 197, 227, 233), 20, ColDb.LightGray, Lang(sb.Clear().Append(langKey).Append("_Name").ToString(), player.UserIDString), TextAnchor.MiddleCenter);

				//Element: Shop Desc
				LUI.LuiContainer shopDesc = cui.v2.CreateText(shopInfo, new LuiOffset(8, 126, 219, 195), 12, ColDb.LTD60, Lang(sb.Clear().Append(langKey).Append("_Desc").ToString(), player.UserIDString), TextAnchor.MiddleCenter);
				shopDesc.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

				//Element: Current Balance Title
				cui.v2.CreateText(shopInfo, new LuiOffset(0, 98, 227, 128), 16, ColDb.LTD80, Lang("CurrentBalance", player.UserIDString), TextAnchor.LowerCenter);

				//Element: Current Balance Amount
				cui.v2.CreateText(shopInfo, new LuiOffset(0, 68, 227, 98), 20, ColDb.LightGray, FormatCurrency(currCfg, GetPlayerCurrency(player, shop), sb), TextAnchor.UpperCenter);

				//Element: Open Shop Button
				textSwitch = isShop ? sb.Clear().Append("ShoppyStock_UI openShop ").Append(shop).ToString() : sb.Clear().Append("ShoppyStock_UI openStock ").Append(shop).ToString();
				LUI.LuiContainer openShopButton = cui.v2.CreateButton(shopInfo, new LuiOffset(0, 0, 227, 64), textSwitch, ColDb.GreenBg);
				openShopButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Open Shop Button Text
				textSwitch = isShop ? Lang("OpenShop", player.UserIDString) : Lang("OpenStock", player.UserIDString);
				cui.v2.CreateText(openShopButton, LuiPosition.Full, LuiOffset.None, 35, ColDb.GreenText, textSwitch, TextAnchor.MiddleCenter);
				startX += 259;
			}
			Pool.FreeUnmanaged(ref validShops);
			Pool.FreeUnmanaged(ref sb);
			byte[] uiBytes = cui.v2.GetUiBytes();
			cui.Destroy("ShoppyStockUI_ElementCore", player);
			cui.v2.SendUiBytes(player, uiBytes);
		}

		private void SwitchTypeUI(CUI cui, ShoppyCache sc, ShoppyType newType, StringBuilder sb, bool force = false)
		{
			if (!force && sc.type != ShoppyType.Unassigned)
			{
				if (sc.type == newType) return;
				cui.v2.Update(sb.Clear().Append("ShoppyStockUI_").Append(sc.type).Append("Button").ToString()).SetButtonColors(ColDb.Transparent);
				cui.v2.Update(sb.Append("Text").ToString()).SetTextColor(ColDb.LTD60);
			}
			if (newType != ShoppyType.Unassigned)
			{
				cui.v2.Update(sb.Clear().Append("ShoppyStockUI_").Append(newType).Append("Button").ToString()).SetButtonColors(ColDb.DarkGray);
				cui.v2.Update(sb.Append("Text").ToString()).SetTextColor(ColDb.LightGray);
			}
			sc.type = newType;
		}

		private void OpenShopUI(BasePlayer player, bool forceUpdate = false)
		{
			using CUI cui = new CUI(CuiHandler);
			ShoppyCache sc = cache[player.userID];
			StringBuilder sb = Pool.Get<StringBuilder>();
			SwitchTypeUI(cui, sc, ShoppyType.Shop, sb, forceUpdate);
			
			//Element: Shop Menu
			LUI.LuiContainer shopMenu = cui.v2.CreateEmptyContainer("ShoppyStockUI_ElementParent", "ShoppyStockUI_ElementCore", true).SetOffset(new LuiOffset(0, 0, 1100, 528));

			string name = Lang(sb.Clear().Append("Shop_").Append(sc.shopName).Append("_Name").ToString(), player.UserIDString);
			//Element: Shop Name
			cui.v2.CreateText(shopMenu, new LuiOffset(32, 478, 332, 512), 28, ColDb.LightGray, name, TextAnchor.UpperLeft);

			//Element: Search Title
			cui.v2.CreateText(shopMenu, new LuiOffset(32, 453, 231, 482), 18, ColDb.LTD60, Lang("Search", player.UserIDString), TextAnchor.LowerLeft);

			//Element: Input Background
			LUI.LuiContainer inputBackground = cui.v2.CreatePanel(shopMenu, new LuiOffset(32, 414, 332, 450), ColDb.BlackTrans20);
			inputBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Search Icon
			cui.v2.CreateSprite(inputBackground, new LuiOffset(271, 7, 293, 29), "assets/content/ui/gameui/camera/icon-zoom.png", ColDb.LTD15);

			//Element: Search Input
			cui.v2.CreateInput(inputBackground, new LuiOffset(10, 0, 300, 36), ColDb.LightGray, Lang("TypeHere", player.UserIDString), 19, "ShoppyStock_UI shop search", 0, true, CUI.Handler.FontTypes.RobotoCondensedRegular, TextAnchor.MiddleLeft).SetInputKeyboard(hudMenuInput: true);

			//Element: Select Category Title
			cui.v2.CreateText(shopMenu, new LuiOffset(32, 380, 332, 412), 18, ColDb.LTD60,  Lang("SelectCategory", player.UserIDString), TextAnchor.LowerLeft);

			//Element: Category List Background
			LUI.LuiContainer categoryListBackground = cui.v2.CreatePanel(shopMenu, new LuiOffset(32, 32, 332, 377), ColDb.BlackTrans20);
			categoryListBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

			ShopData sd = data.shop[sc.shopName];
			List<string> categories = Pool.Get<List<string>>();
			ShopCategoryConfig scc;
			bool selectCategory = sc.shopCache.category.Length == 0 || !sd.cfg.categories.ContainsKey(sc.shopCache.category);
			
			foreach (var category in sd.cfg.categories)
			{
				string catKey = category.Key;
				scc = category.Value;
				if (scc.displayPerm.Length > 0 && !permission.UserHasPermission(player.UserIDString, scc.displayPerm)) continue;
				if (sc.npcId.Length > 0 && config.npcs[sc.npcId].shops[sc.shopName].Count > 0 && !config.npcs[sc.npcId].shops[sc.shopName].Contains(catKey)) continue;
				if (scc.displayBlacklistPerms.Count > 0)
				{
					bool hide = true;
					foreach (var perm in scc.displayBlacklistPerms)
						if (!permission.UserHasPermission(player.UserIDString, perm))
						{
							hide = false;
							break;
						}
					if (hide) continue;
				}

				if (selectCategory)
				{
					sc.shopCache.category = catKey;
					selectCategory = false;
				}
				categories.Add(catKey);
			}

			int scrollHeight = categories.Count * 42;
			if (scrollHeight < 345)
				scrollHeight = 345;

			//Element: Category Scroll
			LuiScrollbar categoryListBackground_Vertical = new LuiScrollbar()
			{
				autoHide = true,
				size = 4,
				handleColor = ColDb.Transparent,
				highlightColor = ColDb.Transparent,
				pressedColor = ColDb.Transparent,
				trackColor = ColDb.Transparent
			};
			LUI.LuiContainer categoryScroll = cui.v2.CreateScrollView(categoryListBackground, new LuiOffset(0, 0, 300, 345), true, false, ScrollRect.MovementType.Elastic, 0.1f, true, 0.1f, 25f, categoryListBackground_Vertical, default);
			categoryScroll.SetScrollContent(new LuiPosition(0, 1, 1, 1), new LuiOffset(0, -scrollHeight, 0, 0));

			//Element: Scrollable Background
			cui.v2.CreatePanel(categoryScroll, LuiPosition.Full, LuiOffset.None, ColDb.Transparent);

			scrollHeight -= 42;
			foreach (var catKey in categories)
			{
				scc = sd.cfg.categories[catKey];

				string command = sb.Clear().Append("ShoppyStock_UI shop category ").Append(catKey).ToString();
				string elementName = sb.Clear().Append("ShoppyStockUI_CategoryButton_").Append(catKey).ToString();
				string color = sc.shopCache.category == catKey ? ColDb.LTD15 : ColDb.Transparent;
				
				//Element: Category Button
				LUI.LuiContainer categoryButton = cui.v2.CreateButton(categoryScroll, new LuiOffset(0, scrollHeight, 300, scrollHeight + 42), command, color, true, elementName);
				categoryButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Category Icon
				FindAndCreateValidImage(cui, categoryButton, new LuiOffset(12, 6, 42, 36), scc.icon, ColDb.LTD80);

				//Element: Category Name
				cui.v2.CreateText(categoryButton, new LuiOffset(54, 0, 297, 42), 20, ColDb.LTD80, Lang(sb.Clear().Append("CategoryName_").Append(catKey).ToString(), player.UserIDString), TextAnchor.MiddleLeft); 

				//Element: Category Item Count
				//LUI.LuiContainer categoryItemCount = cui.v2.CreateText(categoryButton, new LuiOffset(250, 0, 300, 42), 20, ColDb.LTD30, scc.listings.Count.ToString(), TextAnchor.MiddleCenter);
				//categoryItemCount.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);
				//DISABLED DUE TO REQUIREMENT OF CHECKING AVAIBILITY OF EACH LISTING
				scrollHeight -= 42;
			}
			
			//Element: Section Divider
			LUI.LuiContainer sectionDivider = cui.v2.CreateSprite(shopMenu, new LuiOffset(347, 32, 349, 496), "assets/content/ui/dotted_line_vertical.png", ColDb.LTD10);
			sectionDivider.SetImageType(Image.Type.Tiled);

			//Element: Category Section
			LUI.LuiContainer categorySection = cui.v2.CreateEmptyContainer(shopMenu.name).SetOffset(new LuiOffset(332, 0, 1100, 528));
			cui.v2.elements.Add(categorySection);

			//Element: Listed Items Section
			LUI.LuiContainer listedItemsSection = cui.v2.CreatePanel(categorySection, new LuiOffset(32, 31, 736, 431), ColDb.BlackTrans20, "ShoppyStockUI_ShopCategoryHolder");
			listedItemsSection.SetMaterial("assets/content/ui/namefontmaterial.mat");
			
			//Element: Affordable Only Button
			LUI.LuiContainer affordableOnlyButton = cui.v2.CreateButton(categorySection, new LuiOffset(214, 440, 394, 476), "ShoppyStock_UI shop affordableOnly", ColDb.Transparent);
			affordableOnlyButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Checkmark Background
			LUI.LuiContainer checkmarkBackground = cui.v2.CreatePanel(affordableOnlyButton, new LuiOffset(5, 8, 25, 28), ColDb.BlackTrans20);
			checkmarkBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Checkmark
			string imgColor = sc.shopCache.showOnlyAffordable ? ColDb.GreenBg : ColDb.Transparent;
			cui.v2.CreateSprite(checkmarkBackground, new LuiOffset(4, 4, 16, 16), "assets/icons/close.png", imgColor, "ShoppyStockUI_AffordableCheckmark");

			//Element: Affordable Only Button Text
			cui.v2.CreateText(affordableOnlyButton, new LuiOffset(33, 0, 180, 36), 12, ColDb.LTD80, Lang("ShowOnlyAffordable", player.UserIDString), TextAnchor.MiddleLeft);

			//Element: Your Balance Title
			cui.v2.CreateText(categorySection, new LuiOffset(566, 488, 727, 516), 18, ColDb.LTD60, Lang("YourBalance", player.UserIDString), TextAnchor.LowerRight);

			CurrencyConfig currCfg = config.curr[sc.shopName].currCfg;
			//Element: Balance Amount
			cui.v2.CreateText(categorySection, new LuiOffset(566, 457, 696, 493), 22, ColDb.LightGray, FormatCurrency(currCfg, GetPlayerCurrency(player, sc.shopName), sb), TextAnchor.MiddleRight, "ShoppyStockUI_ShopCurrencyAmount");

			//Element: Currency Icon
			string iconColor = currCfg.colorCurrIcon ? ColDb.GreenBg : ColDb.White;
			FindAndCreateValidImage(cui, categorySection, new LuiOffset(700, 462, 726, 488), currCfg.icon, iconColor);

			//Element: Item Count Hint
			cui.v2.CreateText(categorySection, new LuiOffset(436, 434, 736, 466), 15, ColDb.LTD60, "", TextAnchor.LowerRight, "ShoppyStockUI_ShopListingCount");
			
			//Element: Category Title Name
			cui.v2.CreateText(categorySection, new LuiOffset(32, 479, 332, 519), 28, ColDb.LightGray, Lang(sb.Clear().Append("CategoryName_").Append(sc.shopCache.category).ToString(), player.UserIDString), TextAnchor.LowerLeft, "ShoppyStockUI_ShopCategoryText");

			UpdateShopCategory(player, sc.shopCache.category, cui, sb, sc, true);

			//Element: Sort By Button
			LUI.LuiContainer sortByButton = cui.v2.CreateButton(categorySection, new LuiOffset(32, 440, 202, 476), "ShoppyStock_UI shop sort toggle", ColDb.LTD15, name: "ShoppyStockUI_ShopSortButton");
			sortByButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Sort By Icon
			cui.v2.CreateSprite(sortByButton, new LuiOffset(142, 7, 164, 29), "assets/icons/elevator_down.png", ColDb.LTD40);

			string sortType = Lang(sb.Clear().Append("SortBy_").Append(sc.shopCache.sortType).ToString(), player.UserIDString);
			//Element: Sort By Text
			cui.v2.CreateText(sortByButton, new LuiOffset(10, 0, 170, 36), 19, ColDb.LightGray, sortType, TextAnchor.MiddleLeft, "ShoppyStockUI_ShopSortText");
			
			byte[] uiBytes = cui.v2.GetUiBytes();
			cui.Destroy("ShoppyStockUI_ElementCore", player);
			cui.v2.SendUiBytes(player, uiBytes);
			Pool.FreeUnmanaged(ref sb);
			Pool.FreeUnmanaged(ref categories);
		}

		private void SwitchShopCategory(BasePlayer player, string newCategory, bool affordableSwitch = false, bool force = false)
		{
			ShoppyCache sc = cache[player.userID];
			if (!force && !affordableSwitch && newCategory != "Search" && sc.shopCache.category == newCategory) return;
			using CUI cui = new CUI(CuiHandler);
			StringBuilder sb = Pool.Get<StringBuilder>();
			UpdateShopCategory(player, newCategory, cui, sb, sc);
			byte[] uiBytes = cui.v2.GetUiBytes();
			cui.Destroy("ShoppyStockUI_ShopCategoryUpdate", player);
			cui.v2.SendUiBytes(player, uiBytes);
			Pool.FreeUnmanaged(ref sb);
		}

		private void UpdateShopCategory(BasePlayer player, string newCategory, CUI cui, StringBuilder sb, ShoppyCache sc, bool force = false)
		{
			if (sc.shopCache.category != newCategory && sc.shopCache.category != "Search")
			{
				string oldCategoryName = sb.Clear().Append("ShoppyStockUI_CategoryButton_").Append(sc.shopCache.category).ToString();
				cui.v2.Update(oldCategoryName).SetButtonColors(ColDb.Transparent);
			}
			sc.shopCache.category = newCategory;
			string imgColor = sc.shopCache.showOnlyAffordable ? ColDb.GreenBg : ColDb.Transparent;
			cui.v2.Update("ShoppyStockUI_AffordableCheckmark").SetColor(imgColor);
			if (newCategory != "Search")
			{
				string newCategoryName = sb.Clear().Append("ShoppyStockUI_CategoryButton_").Append(newCategory).ToString();
				cui.v2.Update(newCategoryName).SetButtonColors(ColDb.LTD15);
			}
			DrawCategory(cui, player, sc, sb);
		}

		private void DrawCategory(CUI cui, BasePlayer player, ShoppyCache sc, StringBuilder sb)
		{
			ShopData sd = data.shop[sc.shopName];
			bool isSearch = sc.shopCache.category == "Search";

			cui.v2.Update("ShoppyStockUI_ShopCategoryText").SetText(Lang(sb.Clear().Append("CategoryName_").Append(sc.shopCache.category).ToString(), player.UserIDString), update: true);
			
			Dictionary<string, ShopCategoryConfig> categories = Pool.Get<Dictionary<string, ShopCategoryConfig>>();
			categories.Clear();
			int scrollHeight;
			if (isSearch)
			{
				foreach (var cat in sd.cfg.categories)
				{
					string catKey = cat.Key;
					ShopCategoryConfig scc = cat.Value;
					if (scc.displayPerm.Length > 0 && !permission.UserHasPermission(player.UserIDString, scc.displayPerm)) continue;
					if (scc.displayBlacklistPerms.Count > 0)
					{
						bool hide = true;
						foreach (var perm in scc.displayBlacklistPerms)
						{
							if (!permission.UserHasPermission(player.UserIDString, perm))
							{
								hide = false;
								break;
							}
						}
						if (hide) continue;
					}
					categories.Add(catKey, sd.cfg.categories[catKey]);
				}
				int validListings = 0;
				foreach (var cat in categories.Values)
					foreach (var listing in cat.listings)
						if (listing.Value.displayName.Contains(sc.shopCache.search, CompareOptions.IgnoreCase))
							validListings++;
				int listings = validListings > config.ui.searchLimit ? 50 : config.ui.searchLimit;
				scrollHeight = 16 + Mathf.CeilToInt(listings / 4f) * 114;
			}
			else
			{
				categories.Add(sc.shopCache.category, sd.cfg.categories[sc.shopCache.category]);
				scrollHeight = 16 + Mathf.CeilToInt(categories[sc.shopCache.category].listings.Count / 4f) * 114;
			}
			if (scrollHeight < 400)
				scrollHeight = 400;
			
			//ShopCategoryConfig scc = sd.cfg.categories[sc.shopCache.category];
			
			//Element: Category Update
			LUI.LuiContainer categoryUpdate = cui.v2.CreateEmptyContainer("ShoppyStockUI_ShopCategoryHolder", "ShoppyStockUI_ShopCategoryUpdate", true).SetAnchors(LuiPosition.Full);

			//Element: Listed Items Scroll
			LuiScrollbar listedItemsSection_Vertical = new LuiScrollbar()
			{
				autoHide = true,
				size = 4,
				handleColor = ColDb.LTD15,
				highlightColor = ColDb.LTD20,
				pressedColor = ColDb.LTD10,
				trackColor = ColDb.BlackTrans10
			};

			LUI.LuiContainer listedItemsScroll = cui.v2.CreateScrollView(categoryUpdate, new LuiOffset(0, 0, 692, 400), true, false, ScrollRect.MovementType.Elastic, 0.1f, true, 0.1f, 25f, listedItemsSection_Vertical, default);
			listedItemsScroll.SetScrollContent(new LuiPosition(0, 1, 1, 1), new LuiOffset(0, -scrollHeight, 0, 0));

			//Element: Scrollable Background
			cui.v2.CreatePanel(listedItemsScroll, LuiPosition.Full, LuiOffset.None, ColDb.Transparent);
			int startX = 16;
			int startY = scrollHeight - 114;
			Dictionary<string, float> priceMult = Pool.Get<Dictionary<string, float>>();
			priceMult.Clear();
			if (isSearch)
			{
				foreach (var cat in sd.cfg.categories.Keys)
					priceMult.Add(cat, 1);
			}
			else
			{
				priceMult.Add(sc.shopCache.category, 1);
			}
			foreach (var cat in priceMult.Keys.ToArray())
			{
				if (!categories.ContainsKey(cat)) continue;
				float cachedHighest = float.MinValue;
				foreach (var perm in config.curr[sc.shopName].shopCfg.discountPerms)
					if (cachedHighest < perm.Value && permission.UserHasPermission(player.UserIDString, perm.Key))
						cachedHighest = perm.Value;
				if (cachedHighest > float.MinValue)
					priceMult[cat] -= cachedHighest / 100f;
				cachedHighest = float.MinValue;
				foreach (var perm in categories[cat].discountPerms)
					if (cachedHighest < perm.Value && permission.UserHasPermission(player.UserIDString, perm.Key))
						cachedHighest = perm.Value;
				if (cachedHighest > float.MinValue)
					priceMult[cat] -= cachedHighest / 100f;
			}
			ShopConfig shopCfg = config.curr[sc.shopName].shopCfg;
			CurrencyConfig currCfg = config.curr[sc.shopName].currCfg;
			float playerCurrency = GetPlayerCurrency(player, sc.shopName);
			string priceLang = Lang("Price", player.UserIDString);
			string detailsLang = Lang("ViewDetails", player.UserIDString);
			string detailsLangDlc = Lang("DlcMissing", player.UserIDString);
			string available = Lang("Available", player.UserIDString);
			int listingCount = 0;
			
			Dictionary<string, KeyValuePair<string, ShopListingConfig>> listingList = Pool.Get<Dictionary<string, KeyValuePair<string, ShopListingConfig>>>();
			listingList.Clear();
			
			foreach (var category in categories)
				foreach (var item in category.Value.listings)
					listingList.Add(item.Key, new(category.Key, item.Value));
			switch (sc.shopCache.sortType)
			{
				case ShopSortType.Alphabetical:
					listingList = listingList.OrderBy(x => x.Value.Value.displayName).ToDictionary(x => x.Key, x => x.Value);
					break;
				case ShopSortType.Cheapest:
					listingList = listingList.OrderBy(x => x.Value.Value.price).ToDictionary(x => x.Key, x => x.Value);
					break;
				case ShopSortType.Popularity:
					var catUnique = isSearch ? null : sd.stats.uniquePurchases[sc.shopCache.category];
					listingList = listingList.OrderByDescending(x =>
					{
						string listingKey = x.Key;
						if (isSearch)
						{
							string category = x.Value.Key;
							if (sd.stats.uniquePurchases.TryGetValue(category, out var uniquePurchases) && uniquePurchases.TryGetValue(listingKey, out var purchases))
								return purchases.Count;
							return 0;
						}
						else if (catUnique.TryGetValue(listingKey, out var purchases))
							return purchases.Count;
						return 0;
					}).ToDictionary(x => x.Key, x => x.Value);
					break;
			}
			foreach (var item in listingList)
			{
				if (isSearch && listingCount >= 50) break;
				string key = item.Key;
				ShopListingConfig slc = item.Value.Value;
				if (slc.permission.Length > 0 && !permission.UserHasPermission(player.UserIDString, slc.permission)) continue;
				if (slc.blacklistPermission.Length > 0 && permission.UserHasPermission(player.UserIDString, slc.blacklistPermission)) continue;
				string category = item.Value.Key;
				if (sd.stats.rolledOffers.TryGetValue(category, out var rolledOffers) && !rolledOffers.offers.Contains(key)) continue;
				if (isSearch && !slc.displayName.Contains(sc.shopCache.search, CompareOptions.IgnoreCase)) continue;	
				
				float initialPrice = slc.price;
				float fixedPrice = initialPrice;
				foreach (var perm in slc.discounts)
					if (perm.Value < fixedPrice && permission.UserHasPermission(player.UserIDString, perm.Key))
						fixedPrice = perm.Value;
				
				if (fixedPrice == initialPrice || shopCfg.sumDiscounts)
					fixedPrice *= priceMult[category];
				
				int dailyPurchased = -1;
				int wipePurchased = -1;
				if (slc.pricePerPurchaseMultiplier != 1)
				{
					int priceMultTime = 0;
					if (slc.multiplyPricePerDaily)
					{
						dailyPurchased = GetPlayerDailyPurchases(player.userID, sd, sc.shopCache.category, key, slc.globalLimit);
						priceMultTime = dailyPurchased;
					}
					else
					{
						wipePurchased = GetPlayerWipePurchases(player.userID, sd, sc.shopCache.category, key, slc.globalLimit);
						priceMultTime = wipePurchased;
					}
					for (int i = 0; i < priceMultTime; i++)
						fixedPrice *= slc.pricePerPurchaseMultiplier;
				}

				if (saleCache.TryGetValue(sc.shopName, out var shopSales))
				{
					foreach (var sale in shopSales)
					{
						if (sale.categories.TryGetValue(sc.shopCache.category, out float mult))
							fixedPrice *= mult;
						if (sale.items.TryGetValue(key, out mult))
							fixedPrice *= mult;
					}
				}

				bool isTooExpensive = fixedPrice > playerCurrency;
				if (sc.shopCache.showOnlyAffordable && isTooExpensive) continue;
				
				listingCount++;
				
				//Element: Shop Item
				LUI.LuiContainer shopItem = cui.v2.CreatePanel(listedItemsScroll, new LuiOffset(startX, startY, startX + 153, startY + 98), ColDb.LTD5);
				shopItem.SetMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Item Title Background
				LUI.LuiContainer itemTitleBackground = cui.v2.CreatePanel(shopItem, new LuiOffset(0, 78, 153, 98), ColDb.LTD10);
				itemTitleBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

				string itemName = slc.displayName;
				if (shopCfg.namesMultilingual)
					itemName = Lang(sb.Clear().Append("ItemName_").Append(key).ToString(), player.UserIDString);
				//Element: Item Title
				cui.v2.CreateText(itemTitleBackground, new LuiOffset(6, 0, 153, 20), 11, ColDb.LightGray, itemName, TextAnchor.MiddleLeft);

				//Element: Item Misc Info Background
				LUI.LuiContainer itemMiscInfoBackground = cui.v2.CreatePanel(shopItem, new LuiOffset(0, 66, 153, 78), ColDb.BlackTrans10);
				itemMiscInfoBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");


				string hintText = available;

				float lowestCooldown = slc.cooldown;
				bool changed = false;
				if (slc.cooldown > 0)
				{
					DateTime lastPurchase = GetPlayerLastPurchase(player.userID, sd, sc.shopCache.category, key);
					if (lastPurchase != DateTime.MinValue)
					{
						foreach (var perm in slc.cooldownPerms)
							if (perm.Value < lowestCooldown && permission.UserHasPermission(player.UserIDString, perm.Key))
								lowestCooldown = perm.Value;
						DateTime cooldownEndTime = lastPurchase.AddSeconds(lowestCooldown);
						if (cooldownEndTime > DateTime.Now)
						{
							lowestCooldown = (int)Math.Floor(cooldownEndTime.Subtract(DateTime.Now).TotalSeconds);
							hintText = Lang("OnCooldown", player.UserIDString);
							changed = true;
						}
					}
				}
				if (!changed && (slc.dailyBuy > 0 || slc.dailyBuyPerms.Count > 0))
				{
					if (dailyPurchased == -1)
						dailyPurchased = GetPlayerDailyPurchases(player.userID, sd, sc.shopCache.category, key, slc.globalLimit);
					int playerLimit = GetPlayerPurchaseDailyLimit(player, slc);
					hintText = Lang("DailyLimited", player.UserIDString, dailyPurchased, playerLimit);
				}
				else if (!changed && (slc.wipeBuy > 0 || slc.wipeBuyPerms.Count > 0))
				{
					if (wipePurchased == -1)
						wipePurchased = GetPlayerWipePurchases(player.userID, sd, sc.shopCache.category, key, slc.globalLimit);
					int playerLimit = GetPlayerPurchaseWipeLimit(player, slc);
					hintText = Lang("WipeLimited", player.UserIDString, wipePurchased, playerLimit);
				}
				
				//Element: Item Misc Info Text
				LUI.LuiContainer itemMiscInfoText = cui.v2.CreateText(itemMiscInfoBackground, new LuiOffset(0, 0, 147, 12), 7, ColDb.LTD40, hintText, TextAnchor.MiddleRight).SetCountdown(lowestCooldown, 0);
				itemMiscInfoText.SetCountdownDestroy(false);
				itemMiscInfoText.UpdateComp<LuiCountdownComp>().timerFormat = "HoursMinutesSeconds";

				//Element: Item Icon
				LUI.LuiContainer itemIcon;
				if (slc.iconUrl.Length > 0)
					itemIcon = FindAndCreateValidImage(cui, shopItem, new LuiOffset(8, 24, 48, 64), slc.iconUrl, ColDb.WhiteTrans80);
				else
					itemIcon = cui.v2.CreateItemIcon(shopItem, new LuiOffset(8, 24, 48, 64), iconRedirect[slc.shortname], slc.skin, ColDb.WhiteTrans80);

				//Element: Item Amount
				cui.v2.CreateText(itemIcon, new LuiOffset(0, 1, 38, 20), 11, ColDb.LightGray, FormatAmount(currCfg, slc.amount, sb), TextAnchor.LowerRight);
				
				bool isDiscount = fixedPrice < initialPrice;

				int startHeight = isDiscount ? 27 : 30;

				if (isDiscount)
				{
					//Element: Discounted Price
					cui.v2.CreateText(shopItem, new LuiOffset(61, 53, 142, 67), 8, ColDb.RedBg, FormatCurrency(currCfg, initialPrice, sb), TextAnchor.LowerCenter);
				}
				
				string color = isTooExpensive ? ColDb.RedBg : ColDb.LightGray;
				

				//Element: Current Price
				cui.v2.CreateText(shopItem, new LuiOffset(61, startHeight + 8, 142, startHeight + 34), 18, color, FormatCurrency(currCfg, fixedPrice, sb), TextAnchor.LowerCenter);

				//Element: Price Hint
				cui.v2.CreateText(shopItem, new LuiOffset(61, startHeight, 142, startHeight + 11), 8, ColDb.LTD80, priceLang, TextAnchor.UpperCenter);

				bool lockOpening = isTooExpensive && shopCfg.lockOpeningNotEnoughMoney;
				bool missingDlc = false;
				if (slc.blueprintOwnerRequired && !HasPlayerItemUnlocked(player, slc.shortname))
				{
					missingDlc = true;
					lockOpening = true;
				}
				if (!missingDlc && slc.skinOwnerRequired && !HasPlayerSkinUnlocked(player, (int)slc.skin))
				{
					missingDlc = true;
					lockOpening = true;
				}
				string command = lockOpening ? "" : sb.Clear().Append("ShoppyStock_UI shop listing ").Append(key).ToString();
				color = lockOpening ? ColDb.RedBg : ColDb.GreenBg;
				//Element: View Details Button
				LUI.LuiContainer viewDetailsButton = cui.v2.CreateButton(shopItem, new LuiOffset(0, 0, 153, 22), command, color);
				viewDetailsButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				color = lockOpening ? ColDb.RedText : ColDb.GreenText;
				string buttonText = missingDlc ? detailsLangDlc : detailsLang;
				//Element: View Details Button Text
				cui.v2.CreateText(viewDetailsButton, LuiPosition.Full, LuiOffset.None, 15, color, buttonText, TextAnchor.MiddleCenter);

				startX += 169;
				if (startX > 650)
				{
					startX = 16;
					startY -= 114;
				}
			}
			if (!isSearch && sd.cfg.categories[sc.shopCache.category].rollInterval > 0)
				cui.v2.Update("ShoppyStockUI_ShopListingCount").SetText(Lang("ItemsAvailableRoll", player.UserIDString, listingCount, FormatTime(Mathf.FloorToInt(sd.cfg.categories[sc.shopCache.category].rollInterval * 60), sb)), update: true);
			else
				cui.v2.Update("ShoppyStockUI_ShopListingCount").SetText(Lang("ItemsAvailable", player.UserIDString, listingCount), update: true);
			Pool.FreeUnmanaged(ref categories);
			Pool.FreeUnmanaged(ref priceMult);
			Pool.FreeUnmanaged(ref listingList);

		}

		private void UpdateShopSortUI(BasePlayer player)
		{
			using CUI cui = new CUI(CuiHandler);
			ShoppyCache sc = cache[player.userID];
			if (sc.shopCache.showSort)
			{
				//Element: Sort Options Panel
				LUI.LuiContainer sortOptionsPanel = cui.v2.CreatePanel("ShoppyStockUI_ShopSortButton", new LuiOffset(1, -154, 169, 0), ColDb.DarkGray, "ShoppyStockUI_ShopSortPanel");
				sortOptionsPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Sort Outline
				LUI.LuiContainer sortOutline = cui.v2.CreateSprite(sortOptionsPanel, new LuiOffset(-2, -2, 170, 156), "assets/content/ui/ui.box.tga", ColDb.LTD15);
				sortOutline.SetImageType(Image.Type.Tiled);

				//Element: Sort None Button
				LUI.LuiContainer sortNoneButton = cui.v2.CreateButton(sortOptionsPanel, new LuiOffset(1, 119, 167, 153), "ShoppyStock_UI shop sort none", ColDb.DarkGray);
				sortNoneButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Sort None Button Text
				string color = sc.shopCache.sortType == ShopSortType.Default ? ColDb.GreenBg : ColDb.LTD60;
				cui.v2.CreateText(sortNoneButton, new LuiOffset(8, 0, 166, 34), 18, color, Lang("SortBy_None", player.UserIDString), TextAnchor.MiddleLeft, "ShoppyStockUI_ShopSortType_Default");

				//Element: Sort Alphabetically Button
				LUI.LuiContainer sortAlphabeticallyButton = cui.v2.CreateButton(sortOptionsPanel, new LuiOffset(1, 85, 167, 119), "ShoppyStock_UI shop sort alphabet", ColDb.DarkGray);
				sortAlphabeticallyButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Sort Alphabetically Button Text
				color = sc.shopCache.sortType == ShopSortType.Alphabetical ? ColDb.GreenBg : ColDb.LTD60;
				cui.v2.CreateText(sortAlphabeticallyButton, new LuiOffset(8, 0, 166, 34), 18, color, Lang("SortBy_Alphabetical", player.UserIDString), TextAnchor.MiddleLeft, "ShoppyStockUI_ShopSortType_Alphabetical");

				//Element: Sort Popularity Button
				LUI.LuiContainer sortPopularityButton = cui.v2.CreateButton(sortOptionsPanel, new LuiOffset(1, 51, 167, 85), "ShoppyStock_UI shop sort popularity", ColDb.DarkGray);
				sortPopularityButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Sort Popularity Button Text
				color = sc.shopCache.sortType == ShopSortType.Popularity ? ColDb.GreenBg : ColDb.LTD60;
				cui.v2.CreateText(sortPopularityButton, new LuiOffset(8, 0, 166, 34), 18, color, Lang("SortBy_Popularity", player.UserIDString), TextAnchor.MiddleLeft, "ShoppyStockUI_ShopSortType_Popularity");

				//Element: Sort Cheapest Button
				LUI.LuiContainer sortCheapestButton = cui.v2.CreateButton(sortOptionsPanel, new LuiOffset(1, 17, 167, 51), "ShoppyStock_UI shop sort cheapest", ColDb.DarkGray);
				sortCheapestButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Sort Cheapest Button Text
				color = sc.shopCache.sortType == ShopSortType.Cheapest ? ColDb.GreenBg : ColDb.LTD60;
				cui.v2.CreateText(sortCheapestButton, new LuiOffset(8, 0, 166, 34), 18, color, Lang("SortBy_Cheapest", player.UserIDString), TextAnchor.MiddleLeft, "ShoppyStockUI_ShopSortType_Cheapest");
				cui.v2.SendUi(player);
			}
			else
			{
				cui.Destroy("ShoppyStockUI_ShopSortPanel", player);
			}
		}

		private void UpdateShopSortType(BasePlayer player, ShopSortType sortType)
		{
			ShoppyCache sc = cache[player.userID];
			if (sc.shopCache.sortType == sortType) return;
			ShopSortType oldType = sc.shopCache.sortType;
			sc.shopCache.sortType = sortType;
			using CUI cui = new CUI(CuiHandler);
			StringBuilder sb = Pool.Get<StringBuilder>();
			string oldSort = sb.Clear().Append("ShoppyStockUI_ShopSortType_").Append(oldType).ToString();
			string newSort = sb.Clear().Append("ShoppyStockUI_ShopSortType_").Append(sortType).ToString();
			cui.v2.Update(oldSort).SetTextColor(ColDb.LTD60);
			cui.v2.Update(newSort).SetTextColor(ColDb.GreenBg);
			string sortTypeText = Lang(sb.Clear().Append("SortBy_").Append(sc.shopCache.sortType).ToString(), player.UserIDString);
			cui.v2.Update("ShoppyStockUI_ShopSortText").SetText(sortTypeText, update: true);
			DrawCategory(cui, player, sc, sb);
			Pool.FreeUnmanaged(ref sb);
			byte[] uiBytes = cui.v2.GetUiBytes();
			cui.Destroy("ShoppyStockUI_ShopCategoryUpdate", player);
			cui.v2.SendUiBytes(player, uiBytes);
		}

		private void ShowShopListingUI(BasePlayer player, string listingKey)
		{
			using CUI cui = new CUI(CuiHandler);
			ShoppyCache sc = cache[player.userID];
			StringBuilder sb = Pool.Get<StringBuilder>();
			CurrencyConfig cc = config.curr[sc.shopName].currCfg;
			ShopData sd = data.shop[sc.shopName];
			bool isSearch = sc.shopCache.category == "Search";
			ShopListingConfig slc = null;
			string validCategory = sc.shopCache.category;
			if (isSearch)
			{
				foreach (var category in sd.cfg.categories)
				{
					if (category.Value.listings.ContainsKey(listingKey))
					{
						slc = sd.cfg.categories[category.Key].listings[listingKey];
						validCategory = category.Key;
						break;
					}
				}
			}
			else
				slc = sd.cfg.categories[validCategory].listings[listingKey];
			if (sc.shopCache.listingKey != listingKey)
				sc.shopCache.amount = 1;
			sc.shopCache.listingKey = listingKey;
			
			LUI.LuiContainer shoppyUi = cui.v2.CreatePanel("ShoppyStockUI", LuiPosition.Full, LuiOffset.None, ColDb.Transparent, "ShoppyStockUI_ShopListingCore").SetDestroy("ShoppyStockUI_ShopListingCore");
			
			//Element: Background Blur
			LUI.LuiContainer backgroundBlur = cui.v2.CreatePanel(shoppyUi, LuiPosition.Full, LuiOffset.None, ColDb.BlackTrans20);
			backgroundBlur.SetMaterial("assets/content/ui/uibackgroundblur.mat");

			//Element: Background Darker
			LUI.LuiContainer backgroundDarker = cui.v2.CreatePanel(shoppyUi, LuiPosition.Full, LuiOffset.None, ColDb.BlackTrans20);
			backgroundDarker.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Center Anchor
			LUI.LuiContainer centerAnchor = cui.v2.CreatePanel(shoppyUi, LuiPosition.MiddleCenter, LuiOffset.None, ColDb.Transparent);

			bool hideAmountSet = slc.cooldown > 0 || slc.limitToOne;
			int mainPanelSize = hideAmountSet ? 338 : 450;
			int mainPanelOffset = mainPanelSize / 2;

			//Element: Main Panel
			LUI.LuiContainer mainPanel = cui.v2.CreatePanel(centerAnchor, new LuiOffset(-270, -mainPanelOffset, 270, mainPanelOffset), ColDb.DarkGray);
			mainPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Top Panel
			LUI.LuiContainer topPanel = cui.v2.CreatePanel(mainPanel, new LuiOffset(0, mainPanelSize - 32, 540, mainPanelSize), ColDb.LTD5);
			topPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Panel Title
			cui.v2.CreateText(topPanel, new LuiOffset(10, 0, 540, 32), 20, ColDb.LightGray, Lang("ListingDetails", player.UserIDString), TextAnchor.MiddleLeft);

			int panelSizeEnd = mainPanelSize - 32;
			//Element: Listing Details
			LUI.LuiContainer listingDetails = cui.v2.CreateEmptyContainer(mainPanel, add: true).SetOffset(new LuiOffset(0, 0, 540, panelSizeEnd));
			
			//Element: Listing Info
			LUI.LuiContainer listingInfo = cui.v2.CreateEmptyContainer(listingDetails, add: true).SetOffset(new LuiOffset(0, panelSizeEnd - 182, 540, panelSizeEnd));

			//Element: Listing Icon Background
			LUI.LuiContainer listingIconBackground = cui.v2.CreatePanel(listingInfo, new LuiOffset(63, 16, 213, 166), ColDb.LTD5);
			listingIconBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Listing Icon
			if (slc.iconUrl.Length > 0)
				FindAndCreateValidImage(cui, listingIconBackground, new LuiOffset(6, 6, 144, 144), slc.iconUrl, ColDb.WhiteTrans80);
			else
				cui.v2.CreateItemIcon(listingIconBackground, new LuiOffset(6, 6, 144, 144), iconRedirect[slc.shortname], slc.skin, ColDb.WhiteTrans80);

			int sumAmount = slc.amount * sc.shopCache.amount;
			
			string stacks = GetStackCount(slc.shortname, sumAmount);
			int startY = 16;
			if (stacks.Length == 0)
				startY = 4;
			else
			{
				//Element: Listing Stack Count
				LUI.LuiContainer listingStackCount = cui.v2.CreateText(listingIconBackground, new LuiOffset(-2, 4, 144, 26), 12, ColDb.LTD60, Lang("StackCount", player.UserIDString, stacks), TextAnchor.LowerRight, "ShoppyStockUI_StackCount");
				listingStackCount.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);
			}
			
			//Element: Listing Amount
			cui.v2.CreateText(listingIconBackground, new LuiOffset(-2, startY, 144, startY + 30), 22, ColDb.LightGray, FormatAmount(cc, sumAmount, sb), TextAnchor.LowerRight, "ShoppyStockUI_ListingAmount");

			//Element: Item Name
			string itemName = slc.displayName;
			if (config.curr[sc.shopName].shopCfg.namesMultilingual)
				itemName = Lang(sb.Clear().Append("ItemName_").Append(listingKey).ToString(), player.UserIDString);
			cui.v2.CreateText(listingInfo, new LuiOffset(224, 138, 509, 166), 20, ColDb.LightGray, itemName, TextAnchor.MiddleLeft);

			if (slc.description)
			{
				//Element: Item Description
				LUI.LuiContainer itemDescription = cui.v2.CreateText(listingInfo, new LuiOffset(228, 72, 509, 138), 13, ColDb.LTD80, Lang(sb.Clear().Append("Description_").Append(listingKey).ToString(), player.UserIDString), TextAnchor.UpperLeft);
				itemDescription.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);
			}

			string preventPurchaseCause = string.Empty;
			startY = 16;
			if (slc.cooldown > 0)
			{
				float lowestCooldown = slc.cooldown;
				DateTime lastPurchase = GetPlayerLastPurchase(player.userID, sd, validCategory, listingKey);
				string text;
				int seconds;
				foreach (var perm in slc.cooldownPerms)
					if (perm.Value < lowestCooldown && permission.UserHasPermission(player.UserIDString, perm.Key))
						lowestCooldown = perm.Value;
				int lowestCooldownSeconds = Mathf.FloorToInt(lowestCooldown);
				if (lastPurchase != DateTime.MinValue)
				{
					DateTime plannedCooldown = lastPurchase.AddSeconds(lowestCooldown);
					if (plannedCooldown > DateTime.Now)
					{
						seconds = (int)Math.Ceiling((plannedCooldown - DateTime.Now).TotalSeconds);
						text = Lang("IsCooldown", player.UserIDString, FormatTime(lowestCooldownSeconds, sb));
						preventPurchaseCause = "Cooldown";
					}
					else
					{
						seconds = 0;
						text = Lang("NoCooldown", player.UserIDString, FormatTime(lowestCooldownSeconds, sb));
					}
				}
				else
				{
					seconds = 0;
					text = Lang("NoCooldown", player.UserIDString, FormatTime(lowestCooldownSeconds, sb));
				}
			
				//Element: Cooldown Icon
				LUI.LuiContainer cooldownIcon = cui.v2.CreateSprite(listingInfo, new LuiOffset(229, startY, 245, startY + 16), "assets/icons/stopwatch.png", ColDb.LTD30);

				//Element: Cooldown
				LUI.LuiContainer cooldown = cui.v2.CreateText(cooldownIcon, new LuiOffset(19, -2, 189, 18), 10, ColDb.LightGray, text, TextAnchor.MiddleLeft);
				if (seconds > 0)
				{
					cooldown.SetCountdown(seconds, 0, command: Community.Protect("ShoppyStock_UI shop listingCountdown"));
					cooldown.SetCountdownDestroy(false);
					cooldown.UpdateComp<LuiCountdownComp>().timerFormat = "HoursMinutesSeconds";
				}
				startY += 20;
			}

			int wipePurchased = -1;
			int canWipePurchaseCount = 0;
			if (slc.wipeBuy > 0 || slc.wipeBuyPerms.Count > 0)
			{
				wipePurchased = GetPlayerWipePurchases(player.userID, sd, validCategory, listingKey, slc.globalLimit);
				int playerLimit = GetPlayerPurchaseWipeLimit(player, slc);
				canWipePurchaseCount = playerLimit - wipePurchased;
				bool reachedLimit = playerLimit <= wipePurchased;
				if (reachedLimit)
					preventPurchaseCause = "Limit";
				string text = reachedLimit ? Lang("WipeLimitReached", player.UserIDString, wipePurchased, playerLimit) : Lang("WipeLimit", player.UserIDString, wipePurchased, playerLimit);
				if (slc.globalLimit)
					text = sb.Clear().Append(text).Append(' ').Append(Lang("ServerWide", player.UserIDString)).ToString();

				//Element: Wipe Limit Icon
				LUI.LuiContainer wipeLimitIcon = FindAndCreateValidImage(cui, listingInfo, new LuiOffset(229, startY, 245, startY + 16), config.ui.icons.boxesUrl, ColDb.LTD30);

				//Element: Wipe Limit
				cui.v2.CreateText(wipeLimitIcon, new LuiOffset(19, -2, 189, 18), 10, ColDb.LightGray, text, TextAnchor.MiddleLeft);
				startY += 20;
			}

			int dailyPurchased = -1;
			int canDailyPurchaseCount = 0;
			if (slc.dailyBuy > 0 || slc.dailyBuyPerms.Count > 0)
			{
				dailyPurchased = GetPlayerDailyPurchases(player.userID, sd, validCategory, listingKey, slc.globalLimit);
				int playerLimit = GetPlayerPurchaseDailyLimit(player, slc);
				canDailyPurchaseCount = playerLimit - dailyPurchased;
				bool reachedLimit = playerLimit <= dailyPurchased;
				if (reachedLimit)
					preventPurchaseCause = "Limit";
				string text = reachedLimit ? Lang("DailyLimitReached", player.UserIDString, dailyPurchased, playerLimit) : Lang("DailyLimit", player.UserIDString, dailyPurchased, playerLimit);
				if (slc.globalLimit)
					text = sb.Clear().Append(text).Append(' ').Append(Lang("ServerWide", player.UserIDString)).ToString();
				//Element: Daily Limit Icon
				LUI.LuiContainer dailyLimitIcon = cui.v2.CreateImageFromDb(listingInfo, new LuiOffset(229, startY, 245, startY + 16), config.ui.icons.boxesUrl, ColDb.LTD30);

				//Element: Daily Limit
				cui.v2.CreateText(dailyLimitIcon, new LuiOffset(19, -2, 189, 18), 10, ColDb.LightGray, text, TextAnchor.MiddleLeft);
			}

			panelSizeEnd -= 182;
			float playerBalance = GetPlayerCurrency(player, sc.shopName);

			float initialPrice = slc.price;
			float fixedPrice = initialPrice;
			foreach (var perm in slc.discounts)
				if (perm.Value < fixedPrice && permission.UserHasPermission(player.UserIDString, perm.Key))
					fixedPrice = perm.Value;

			if (fixedPrice == initialPrice || config.curr[sc.shopName].shopCfg.sumDiscounts)
			{
				float mult = 1;
				float cachedHighest = float.MinValue;
				foreach (var perm in config.curr[sc.shopName].shopCfg.discountPerms)
					if (cachedHighest < perm.Value && permission.UserHasPermission(player.UserIDString, perm.Key))
						cachedHighest = perm.Value;
				if (cachedHighest > float.MinValue)
					mult -= cachedHighest / 100f;
				cachedHighest = float.MinValue;
				foreach (var perm in sd.cfg.categories[validCategory].discountPerms)
					if (cachedHighest < perm.Value && permission.UserHasPermission(player.UserIDString, perm.Key))
						cachedHighest = perm.Value;
				if (cachedHighest > float.MinValue)
					mult -= cachedHighest / 100f;
				fixedPrice *= mult;
			}

			float multipliedPrice = 0;
			float sumPrice = 0;
			if (slc.pricePerPurchaseMultiplier != 1)
			{
				if (slc.multiplyPricePerDaily && dailyPurchased == -1)
					dailyPurchased = GetPlayerDailyPurchases(player.userID, sd, validCategory, listingKey, slc.globalLimit);
				else if (!slc.multiplyPricePerDaily && wipePurchased == -1)
					wipePurchased = GetPlayerWipePurchases(player.userID, sd, validCategory, listingKey, slc.globalLimit);
				int priceMultTime = slc.multiplyPricePerDaily ? dailyPurchased : wipePurchased;
				float cachedPrice = fixedPrice;
				multipliedPrice = fixedPrice;
				sumPrice = fixedPrice;
				for (int i = 0; i < priceMultTime + sc.shopCache.amount; i++)
				{
					if (i == priceMultTime)
					{
						sumPrice = cachedPrice;
						multipliedPrice = cachedPrice;
					}
					if (i > priceMultTime)
						sumPrice += cachedPrice;
					cachedPrice *= slc.pricePerPurchaseMultiplier;
				}
			}
			else
				sumPrice = fixedPrice * sc.shopCache.amount;
			

			if (saleCache.TryGetValue(sc.shopName, out var shopSales))
			{
				foreach (var sale in shopSales)
				{
					if (sale.categories.TryGetValue(validCategory, out float mult))
						fixedPrice *= mult;
					if (sale.items.TryGetValue(sc.shopCache.listingKey, out mult))
						fixedPrice *= mult;
				}
			}
			
			sc.shopCache.fixedPrice = fixedPrice;
			sc.shopCache.summedPrice = sumPrice;
			float playerBalanceAfter = playerBalance - sumPrice;

			bool notEnoughMoney = playerBalanceAfter < 0;

			if (notEnoughMoney)
				preventPurchaseCause = "Expensive";
			
			if (!hideAmountSet)
			{
				//Element: Amount Sets
				LUI.LuiContainer amountSets = cui.v2.CreateEmptyContainer(listingDetails.name, add: true).SetOffset(new LuiOffset(0, panelSizeEnd - 112, 540, panelSizeEnd));
				
				//Element: Purchase Amount Background
				LUI.LuiContainer purchaseAmountBackground = cui.v2.CreatePanel(amountSets, new LuiOffset(215, 44, 325, 86), ColDb.BlackTrans20);
				purchaseAmountBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Purchase Amount Input
				cui.v2.CreateInput(purchaseAmountBackground, LuiPosition.Full, LuiOffset.None, ColDb.LightGray, sc.shopCache.amount.ToString(), 27, "ShoppyStock_UI shop setAmount", 0, true, CUI.Handler.FontTypes.RobotoCondensedBold, TextAnchor.MiddleCenter, "ShoppyStockUI_PurchaseAmountInput").SetInputKeyboard(hudMenuInput: true);

				//Element: Purchase Amount Increase Button
				LUI.LuiContainer purchaseAmountIncreaseButton = cui.v2.CreateButton(purchaseAmountBackground, new LuiOffset(110, 0, 152, 42), "ShoppyStock_UI shop increase", ColDb.GreenBg);
				purchaseAmountIncreaseButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Purchase Amount Increase Button Icon
				cui.v2.CreateSprite(purchaseAmountIncreaseButton, new LuiOffset(9, 9, 33, 33), "assets/icons/add.png", ColDb.GreenText);

				//Element: Purchase Amount Decrease Button
				LUI.LuiContainer purchaseAmountDecreaseButton = cui.v2.CreateButton(purchaseAmountBackground, new LuiOffset(-42, 0, 0, 42), "ShoppyStock_UI shop decrease", ColDb.RedBg);
				purchaseAmountDecreaseButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Purchase Amount Decrease Button Icon
				cui.v2.CreateSprite(purchaseAmountDecreaseButton, new LuiOffset(9, 9, 33, 33), "assets/icons/subtract.png", ColDb.RedText);

				//Element: Purchase Amount Title
				cui.v2.CreateText(purchaseAmountBackground, new LuiOffset(-42, 42, 152, 74), 15, ColDb.LightGray, Lang("PurchaseAmount", player.UserIDString), TextAnchor.MiddleCenter);

				int maxPurchaseAmount = 1;
				if (slc.cooldown == 0 && !slc.limitToOne)
				{
					if (!notEnoughMoney)
					{
						if (slc.pricePerPurchaseMultiplier != 1)
						{
							float number = multipliedPrice;
							float sum = multipliedPrice;
							maxPurchaseAmount = 0;
							while (sum < playerBalance)
							{
								number *= slc.pricePerPurchaseMultiplier;
								sum += number;
								maxPurchaseAmount++;
							}
						}
						else
							maxPurchaseAmount = Mathf.FloorToInt(playerBalance / fixedPrice);
					}
					if ((slc.dailyBuy > 0 || slc.dailyBuyPerms.Count > 0) && maxPurchaseAmount > canDailyPurchaseCount)
						maxPurchaseAmount = canDailyPurchaseCount;
					if ((slc.wipeBuy > 0 || slc.wipeBuyPerms.Count > 0) && maxPurchaseAmount > canWipePurchaseCount)
						maxPurchaseAmount = canWipePurchaseCount;
					if (slc.shortname.Length > 0)
					{
						int stackCount = GetItemCapacity(player, slc.shortname, slc.amount);
						if (maxPurchaseAmount > stackCount)
							maxPurchaseAmount = stackCount;
					}
				}
				sc.shopCache.maxAmount = maxPurchaseAmount;
				
				//Element: Slider Section
				LUI.LuiContainer sliderSection = cui.v2.CreatePanel(amountSets, new LuiOffset(100, 13, 440, 25), ColDb.BlackTrans20);
				sliderSection.SetMaterial("assets/content/ui/namefontmaterial.mat");

				float sliderStart = 0;
				float sliderDivider = 340 / 20f;

				float sliderEnd = 0;
				
				for (int i = 1; i <= 20; i++)
				{
					int amountSet = Mathf.CeilToInt(maxPurchaseAmount / 20f * i);
					sliderStart += sliderDivider;
					if (amountSet <= sc.shopCache.amount)
						sliderEnd = sliderStart;
				}

				//Element: Slider Progress
				LUI.LuiContainer sliderProgress = cui.v2.CreatePanel(sliderSection, new LuiOffset(0, 0, sliderEnd, 12), ColDb.GreenBg, "ShoppyStockUI_AmountSlider");
				sliderProgress.SetMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Slider Handle
				LUI.LuiContainer sliderHandle = cui.v2.CreatePanel(sliderProgress, new LuiPosition(1, 0, 1, 0), new LuiOffset(-5, -6, 5, 18), ColDb.LTD10);
				sliderHandle.SetMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Slider Handle Detail
				LUI.LuiContainer sliderHandleDetail = cui.v2.CreatePanel(sliderProgress, new LuiPosition(1, 0, 1, 0), new LuiOffset(-3, -4, 3, 16), ColDb.LTD20);
				sliderHandleDetail.SetMaterial("assets/content/ui/namefontmaterial.mat");

				sliderStart = 0;
				for (int i = 1; i <= 20; i++)
				{
					int amountSet = Mathf.CeilToInt(maxPurchaseAmount / 20f * i);
					string command = sb.Clear().Append("ShoppyStock_UI shop setAmount ").Append(amountSet).ToString();
						
					//Element: Slider Button
					cui.v2.CreateButton(sliderSection, new LuiOffset(sliderStart, 0, sliderStart + sliderDivider, 12), command, ColDb.Transparent);
					
					sliderStart += sliderDivider;
				}
				
				//Element: Min Amount
				cui.v2.CreateText(sliderSection, new LuiOffset(-82, -20, -12, 32), 25, ColDb.LightGray, "1", TextAnchor.MiddleRight);

				//Element: Max Amount
				cui.v2.CreateText(sliderSection, new LuiOffset(352, -20, 422, 32), 25, ColDb.LightGray, FormatNumber(maxPurchaseAmount, sb), TextAnchor.MiddleLeft);
				
				panelSizeEnd -= 112;
			}
			
			//Element: Balances And Buttons
			LUI.LuiContainer balancesAndButtons = cui.v2.CreateEmptyContainer(listingDetails.name).SetOffset(new LuiOffset(0, 0, 540, panelSizeEnd));
			cui.v2.elements.Add(balancesAndButtons);

			if (notEnoughMoney)
			{
				//Element: Required More Balance Title
				LUI.LuiContainer requiredMoreBalanceTitle = cui.v2.CreateText(balancesAndButtons, new LuiOffset(100, 102, 254, 124), 13, ColDb.LTD80, Lang("MoreBalanceRequired", player.UserIDString), TextAnchor.LowerCenter);

				float reqMoney = sumPrice - playerBalance;
				//Element: Required More Balance
				cui.v2.CreateText(requiredMoreBalanceTitle, new LuiOffset(0, -28, 154, 0), 20, ColDb.LightGray, FormatCurrency(cc, reqMoney, sb), TextAnchor.UpperCenter);

			}
			else
			{
				
				//Element: Balance After Title
				LUI.LuiContainer balanceAfterTitle = cui.v2.CreateText(balancesAndButtons, new LuiOffset(100, 102, 254, 124), 13, ColDb.LTD80, Lang("BalanceAfterPurchase", player.UserIDString), TextAnchor.LowerCenter);

				//Element: Balance After
				cui.v2.CreateText(balanceAfterTitle, new LuiOffset(0, -28, 154, 0), 20, ColDb.LightGray, FormatCurrency(cc, playerBalanceAfter, sb), TextAnchor.UpperCenter, "ShoppyStockUI_BalanceAfter");

			}
			
			//Element: Purchase Cost Title
			LUI.LuiContainer purchaseCostTitle = cui.v2.CreateText(balancesAndButtons, new LuiOffset(286, 102, 440, 124), 13, ColDb.LTD80, Lang("PurchaseCost", player.UserIDString), TextAnchor.LowerCenter);

			//Element: Purchase Cost
			cui.v2.CreateText(purchaseCostTitle, new LuiOffset(0, -28, 154, 0), 20, ColDb.LightGray, FormatCurrency(cc, sumPrice, sb), TextAnchor.UpperCenter, "ShoppyStockUI_PurchaseCost");

			//Element: Go Back Button
			LUI.LuiContainer goBackButton = cui.v2.CreateButton(balancesAndButtons, new LuiOffset(100, 24, 254, 60), "ShoppyStock_UI shop goBack", ColDb.RedBg);
			goBackButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Go Back Button Text
			cui.v2.CreateText(goBackButton, LuiPosition.Full, LuiOffset.None, 22, ColDb.RedText, Lang("GoBack", player.UserIDString), TextAnchor.MiddleCenter);

			if (preventPurchaseCause.Length > 0)
			{
				string langMess;
				switch (preventPurchaseCause)
				{
					case "Limit":
						langMess = Lang("LimitReached", player.UserIDString);
						break;
					case "Cooldown":
						langMess = Lang("BuyCooldown", player.UserIDString);
						break;
					default:
						langMess = Lang("TooExpensive", player.UserIDString);
						break;
				}
				//Element: Too Expensive Button
				LUI.LuiContainer tooExpensiveButton = cui.v2.CreateButton(balancesAndButtons, new LuiOffset(286, 24, 440, 60), "", ColDb.LTD20);
				tooExpensiveButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Too Expensive Button Text
				cui.v2.CreateText(tooExpensiveButton, LuiPosition.Full, LuiOffset.None, 22, ColDb.LTD60, langMess, TextAnchor.MiddleCenter);
			}
			else
			{
				//Element: Purchase Button
				LUI.LuiContainer purchaseButton = cui.v2.CreateButton(balancesAndButtons, new LuiOffset(286, 24, 440, 60), "ShoppyStock_UI shop purchase", ColDb.GreenBg);
				purchaseButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Purchase Button Text
				cui.v2.CreateText(purchaseButton, LuiPosition.Full, LuiOffset.None, 22, ColDb.GreenText, Lang("Purchase", player.UserIDString), TextAnchor.MiddleCenter);
			}
			cui.v2.SendUi(player);
			Pool.FreeUnmanaged(ref sb);
			
		}

		private void UpdateShopListingAmount(BasePlayer player)
		{
			using CUI cui = new CUI(CuiHandler);
			ShoppyCache sc = cache[player.userID];
			StringBuilder sb = Pool.Get<StringBuilder>();
			CurrencyConfig cc = config.curr[sc.shopName].currCfg;
			ShopData sd = data.shop[sc.shopName];
			bool isSearch = sc.shopCache.category == "Search";
			ShopListingConfig slc = null;
			string validCategory = sc.shopCache.category;
			if (isSearch)
			{
				foreach (var category in sd.cfg.categories)
				{
					if (category.Value.listings.ContainsKey(sc.shopCache.listingKey))
					{
						slc = sd.cfg.categories[category.Key].listings[sc.shopCache.listingKey];
						validCategory = category.Key;
						break;
					}
				}
			}
			else
				slc = sd.cfg.categories[validCategory].listings[sc.shopCache.listingKey];
			float sumPrice = sc.shopCache.fixedPrice;
			if (slc.pricePerPurchaseMultiplier != 1)
			{
				int priceMultTime = slc.multiplyPricePerDaily ? GetPlayerDailyPurchases(player.userID, sd, validCategory, sc.shopCache.listingKey, slc.globalLimit) : GetPlayerWipePurchases(player.userID, sd, validCategory, sc.shopCache.listingKey, slc.globalLimit);
				float priceCache = sumPrice;
				for (int i = 0; i < priceMultTime + sc.shopCache.amount; i++)
				{
					if (i == priceMultTime)
						sumPrice = priceCache;
					if (i > priceMultTime)
						sumPrice += priceCache;
					priceCache *= slc.pricePerPurchaseMultiplier;
				}
			}
			else
				sumPrice *= sc.shopCache.amount;
			if (sumPrice != sc.shopCache.summedPrice)
			{
				cui.Destroy("ShoppyStockUI_ShopListingCore", player);
				ShowShopListingUI(player, sc.shopCache.listingKey);
				return;
			}
			cui.v2.UpdateText("ShoppyStockUI_PurchaseCost", FormatCurrency(cc, sumPrice, sb));
			float balanceAfter = GetPlayerCurrency(player, sc.shopName) - sumPrice;
			cui.v2.UpdateText("ShoppyStockUI_BalanceAfter", FormatCurrency(cc, balanceAfter, sb));
			cui.v2.Update("ShoppyStockUI_PurchaseAmountInput").SetInput(text: sc.shopCache.amount.ToString(), update: true);
			int itemAmount = sc.shopCache.amount * slc.amount;
			string stack = GetStackCount(slc.shortname, itemAmount);
			cui.v2.UpdateText("ShoppyStockUI_ListingAmount", FormatAmount(cc, itemAmount, sb));
			cui.v2.UpdateText("ShoppyStockUI_StackCount", Lang("StackCount", player.UserIDString, stack));
			float sliderDivider = 340 / 20f;
			float sliderEnd = 0;
			for (int i = 1; i <= 20; i++)
			{
				int amountSet = Mathf.CeilToInt(sc.shopCache.maxAmount / 20f * i);
				
				if (amountSet <= sc.shopCache.amount)
				{
					sliderEnd += sliderDivider;
					if (sc.shopCache.amount == sc.shopCache.maxAmount)
						sliderEnd = 340;
				}
			}
			cui.v2.UpdatePosition("ShoppyStockUI_AmountSlider", new LuiOffset(0, 0, sliderEnd, 12));
			cui.v2.SendUi(player);
			Pool.FreeUnmanaged(ref sb);
		}

		private void OpenStockUI(BasePlayer player, bool forceUpdate = false)
		{
			using CUI cui = new CUI(CuiHandler);
			ShoppyCache sc = cache[player.userID];
			StringBuilder sb = Pool.Get<StringBuilder>();
			StockConfig stockCfg = config.curr[sc.shopName].stockCfg;
			StockData sd = data.stock[sc.shopName];
			SwitchTypeUI(cui, sc, ShoppyType.Stock, sb, forceUpdate);
			if (stockCfg.itemsUpdateNamedBasedOnInventories)
			{
				StockDataConfig sdc = sd.cfg;
				bool changed = false;
				foreach (var item in player.inventory.containerMain.itemList)
				{
					if (item.skin == 0) continue;
					if (string.IsNullOrEmpty(item.name)) continue;
					if (sdc.customItems.TryGetValue(item.info.shortname, out var skins) && skins.TryGetValue(item.skin, out var sic) && sic.displayName != item.name)
					{
						sic.displayName = item.name;
						changed = true;
					}
				}
				foreach (var item in player.inventory.containerBelt.itemList)
				{
					if (item.skin == 0) continue;
					if (string.IsNullOrEmpty(item.name)) continue;
					if (sdc.customItems.TryGetValue(item.info.shortname, out var skins) && skins.TryGetValue(item.skin, out var sic) && sic.displayName != item.name)
					{
						sic.displayName = item.name;
						changed = true;
					}
				}
				if (changed)
					SaveStockDataConfig(sc.shopName);
			}

			if (stockCfg.bankMoveResourcesWhenNoAccess && stockCfg.categoriesBankPerm.Length > 0 && !permission.UserHasPermission(player.UserIDString, stockCfg.categoriesBankPerm))
			{
				if (sd.playerData.players.TryGetValue(player.userID, out var spd) && spd.bankItems.Count > 0)
				{
					foreach (var shortname in spd.bankItems)
					foreach (var skin in shortname.Value)
					{	        
						ItemDefinition def = ItemManager.FindItemDefinition(shortname.Key);
						while (skin.Value.amount > 0)
						{
							int giveAmount = skin.Value.amount > def.stackable ? def.stackable : skin.Value.amount;
							Item item = ItemManager.Create(def, giveAmount, sc.stockCache.bankSkin);
							if (!string.IsNullOrEmpty(skin.Value.itemName))
								item.name = skin.Value.itemName;
							skin.Value.amount -= giveAmount;
							RedeemStorageAPI?.Call("AddItem", player.userID.Get(), stockCfg.redeemInventoryName, item, skin.Value.amount == 0);
						}
					}
					spd.bankItems.Clear();
					SendEffect(player, StringKeys.Effect_Denied);
					ShowPopUp(player, Lang("BankItemsMovedToStorageMissingPerm", player.UserIDString));
				}
			}
			
			//Element: Stock Menu
			LUI.LuiContainer stockMenu = cui.v2.CreateEmptyContainer("ShoppyStockUI_ElementParent", "ShoppyStockUI_ElementCore", true).SetOffset(new LuiOffset(0, 0, 1100, 528));

			string name = Lang(sb.Clear().Append("Stock_").Append(sc.shopName).Append("_Name").ToString(), player.UserIDString);
			//Element: Stock Market Name
			cui.v2.CreateText(stockMenu, new LuiOffset(32, 478, 332, 512), 28, ColDb.LightGray, name, TextAnchor.UpperLeft);
			
			//Element: Search Title
			cui.v2.CreateText(stockMenu, new LuiOffset(32, 453, 231, 482), 18, ColDb.LTD60, Lang("Search", player.UserIDString), TextAnchor.LowerLeft);

			//Element: Input Background
			LUI.LuiContainer inputBackground = cui.v2.CreatePanel(stockMenu, new LuiOffset(32, 414, 332, 450), ColDb.BlackTrans20);
			inputBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Search Icon
			cui.v2.CreateSprite(inputBackground, new LuiOffset(271, 7, 293, 29), "assets/content/ui/gameui/camera/icon-zoom.png", ColDb.LTD15);

			//Element: Search Input
			cui.v2.CreateInput(inputBackground, new LuiOffset(10, 0, 300, 36), ColDb.LightGray, Lang("TypeHere", player.UserIDString), 19, "ShoppyStock_UI stock search", 0, true, CUI.Handler.FontTypes.RobotoCondensedRegular, TextAnchor.MiddleLeft).SetInputKeyboard(hudMenuInput: true);

			//Element: Select Category Title
			cui.v2.CreateText(stockMenu, new LuiOffset(32, 380, 332, 412), 18, ColDb.LTD60,  Lang("SelectCategory", player.UserIDString), TextAnchor.LowerLeft);

			//Element: Categories Background
			LUI.LuiContainer categoriesBackground = cui.v2.CreatePanel(stockMenu, new LuiOffset(32, 32, 332, 377), ColDb.BlackTrans20);
			categoriesBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");
			
			var categories = stockItems[sc.shopName];

			int scrollHeight = categories.categories.Count * 42;
			if (scrollHeight < 345)
				scrollHeight = 345;

			//Element: Category Scroll
			LuiScrollbar categoriesBackground_Vertical = new LuiScrollbar()
			{
				autoHide = true,
				size = 4,
				handleColor = ColDb.Transparent,
				highlightColor = ColDb.Transparent,
				pressedColor = ColDb.Transparent,
				trackColor = ColDb.Transparent
			};

			LUI.LuiContainer categoryScroll = cui.v2.CreateScrollView(categoriesBackground, new LuiOffset(0, 0, 300, 345), true, false, ScrollRect.MovementType.Elastic, 0.1f, true, 0.1f, 25f, categoriesBackground_Vertical, default);
			categoryScroll.SetScrollContent(new LuiPosition(0, 1, 1, 1), new LuiOffset(0, -scrollHeight, 0, 0));

			//Element: Scrollable Background
			cui.v2.CreatePanel(categoryScroll, LuiPosition.Full, LuiOffset.None, ColDb.Transparent);
			
			scrollHeight -= 42;
			foreach (var cat in categories.categories.Keys)
			{
				if (stockCfg.categoriesBankPerm.Length > 0 && cat == StringKeys.Cat_Bank && !permission.UserHasPermission(player.UserIDString, stockCfg.categoriesBankPerm)) continue;
				if (stockCfg.categoriesFavouritePerm.Length > 0 && cat == StringKeys.Cat_Favourites && !permission.UserHasPermission(player.UserIDString, stockCfg.categoriesFavouritePerm)) continue;
				if (sc.stockCache.category.Length == 0)
					sc.stockCache.category = stockCfg.categoriesDefaultKey;
				string command = sb.Clear().Append("ShoppyStock_UI stock category ").Append(cat).ToString();
				string elementName = sb.Clear().Append("ShoppyStockUI_CategoryButton_").Append(cat).ToString();
				string color;
				bool isGreen = greenCategories.Contains(cat);
				if (isGreen)
					color = sc.stockCache.category == cat ? ColDb.GreenBg : ColDb.Transparent;
				else
					color = sc.stockCache.category == cat ? ColDb.LTD15 : ColDb.Transparent;
				
				
				//Element: Category Button
				LUI.LuiContainer categoryButton = cui.v2.CreateButton(categoryScroll, new LuiOffset(0, scrollHeight, 300, scrollHeight + 42), command, color, true, elementName);
				categoryButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Category Icon
				string textColor = isGreen ? ColDb.GreenText : ColDb.LTD80;
				if (!stockCfg.categoriesIcons.TryGetValue(cat, out var icon))
					icon = string.Empty;
				FindAndCreateValidImage(cui, categoryButton, new LuiOffset(12, 6, 42, 36), icon, textColor);

				//Element: Category Name
				cui.v2.CreateText(categoryButton, new LuiOffset(54, 0, 297, 42), 20, textColor, Lang(sb.Clear().Append("CategoryName_").Append(cat).ToString(), player.UserIDString), TextAnchor.MiddleLeft); 

				//Element: Category Item Count
				textColor = isGreen ? ColDb.GreenText : ColDb.LTD30;
				string amount = categories.itemCount.TryGetValue(cat, out int intAmount) ? intAmount.ToString() : string.Empty;
				LUI.LuiContainer categoryItemCount = cui.v2.CreateText(categoryButton, new LuiOffset(250, 0, 300, 42), 20, textColor, amount, TextAnchor.MiddleCenter);
				categoryItemCount.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);
				scrollHeight -= 42;
			}
			
			//Element: Category Divider
			LUI.LuiContainer categoryDivider = cui.v2.CreateSprite(stockMenu, new LuiOffset(347, 32, 349, 496), "assets/content/ui/dotted_line_vertical.png", ColDb.LTD10);
			categoryDivider.SetImageType(Image.Type.Tiled);

			//Element: Category Section
			LUI.LuiContainer categorySection = cui.v2.CreateEmptyContainer(stockMenu.name).SetOffset(new LuiOffset(332, 0, 1100, 528));
			cui.v2.elements.Add(categorySection);

			//Element: Category Name
			cui.v2.CreateText(categorySection, new LuiOffset(32, 479, 332, 519), 28, ColDb.LightGray, Lang(sb.Clear().Append("CategoryName_").Append(sc.stockCache.category).ToString(), player.UserIDString), TextAnchor.LowerLeft, "ShoppyStockUI_StockCategoryText");

			int startX = 32;
			if (sd.cfg.sellItems.Count > 0)
			{
				//Element: Sell To Server Button
				LUI.LuiContainer sellToServerButton = cui.v2.CreateButton(categorySection, new LuiOffset(startX, 440, startX + 170, 476), "ShoppyStock_UI stock openSell", ColDb.GreenBg);
				sellToServerButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat"); 

				//Element: Sell To Server Button Text
				cui.v2.CreateText(sellToServerButton, LuiPosition.Full, LuiOffset.None, 19, ColDb.GreenText, Lang("SellToServer", player.UserIDString), TextAnchor.MiddleCenter);
				startX += 178;
			}
			
			if (stockCfg.marketLog.tabEnabled && (stockCfg.marketLog.tabReqPerm.Length == 0 || (stockCfg.marketLog.tabReqPerm.Length > 0 && permission.UserHasPermission(player.UserIDString, stockCfg.marketLog.tabReqPerm))))
			{
				//Element: Stock History Button
				LUI.LuiContainer stockHistoryButton = cui.v2.CreateButton(categorySection, new LuiOffset(startX, 440, startX + 130, 476), "ShoppyStock_UI stock history", ColDb.LTD20);
				stockHistoryButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Stock History Button Text
				cui.v2.CreateText(stockHistoryButton, new LuiOffset(39, 0, 130, 36), 10, ColDb.LightGray, Lang("StockHistory", player.UserIDString), TextAnchor.MiddleLeft);

				//Element: Stock History Button Image
				cui.v2.CreateSprite(stockHistoryButton, new LuiOffset(9, 5, 35, 31), "assets/icons/examine.png", ColDb.LightGray);
				startX += 138;
			}
			
			//Element: Hide Empty Listings Button
			LUI.LuiContainer hideEmptyListingsButton = cui.v2.CreateButton(categorySection, new LuiOffset(startX, 440, startX + 180, 476), "ShoppyStock_UI stock hideEmpty", ColDb.Transparent);
			hideEmptyListingsButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Hide Empty Checkmark Background
			LUI.LuiContainer hideEmptyCheckmarkBackground = cui.v2.CreatePanel(hideEmptyListingsButton, new LuiOffset(5, 8, 25, 28), ColDb.BlackTrans20);
			hideEmptyCheckmarkBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Hide Empty Checkmark
			string imgColor = sc.stockCache.hideEmpty ? ColDb.GreenBg : ColDb.Transparent;
			cui.v2.CreateSprite(hideEmptyCheckmarkBackground, new LuiOffset(4, 4, 16, 16), "assets/icons/close.png", imgColor, "ShoppyStockUI_HideEmptyCheckmark");

			//Element: Hide Empty Text
			cui.v2.CreateText(hideEmptyListingsButton, new LuiOffset(33, 0, 180, 36), 12, ColDb.LTD80, Lang("HideEmpty", player.UserIDString), TextAnchor.MiddleLeft);

			//Element: Balance Title
			cui.v2.CreateText(categorySection, new LuiOffset(566, 488, 727, 516), 18, ColDb.LTD60, Lang("YourBalance", player.UserIDString), TextAnchor.LowerRight);

			CurrencyConfig currCfg = config.curr[sc.shopName].currCfg;

			//Element: Balance Amount
			cui.v2.CreateText(categorySection, new LuiOffset(566, 457, 696, 493), 22, ColDb.LightGray, FormatCurrency(currCfg, GetPlayerCurrency(player, sc.shopName), sb), TextAnchor.MiddleRight);

			//Element: Balance Icon
			FindAndCreateValidImage(cui, categorySection, new LuiOffset(700, 462, 726, 488), currCfg.icon, ColDb.GreenBg);

			//Element: Sort Hint Icon
			LUI.LuiContainer sortHintIcon = cui.v2.CreateSprite(categorySection, new LuiOffset(717, 440, 732, 455), "assets/icons/info.png", ColDb.LTD30);

			//Element: Sort Hint Text
			LUI.LuiContainer sortHintText = cui.v2.CreateText(sortHintIcon, new LuiOffset(-271, -2, -4, 17), 12, ColDb.LTD30, Lang("ClickToSortHint", player.UserIDString), TextAnchor.MiddleRight);
			sortHintText.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

			//Element: Stock Listings Background
			LUI.LuiContainer stockListingsBackground = cui.v2.CreatePanel(categorySection, new LuiOffset(32, 32, 736, 432), ColDb.BlackTrans20);
			stockListingsBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Listing Top Panel
			LUI.LuiContainer listingTopPanel = cui.v2.CreatePanel(stockListingsBackground, new LuiOffset(0, 358, 704, 400), ColDb.LTD5);
			listingTopPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Item Name Button
			LUI.LuiContainer itemNameButton = cui.v2.CreateButton(listingTopPanel, new LuiOffset(0, 0, 414, 42), "ShoppyStock_UI stock sort name", ColDb.LTD5);
			itemNameButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Item Name Button Text
			cui.v2.CreateText(itemNameButton, new LuiOffset(12, 0, 414, 42), 18, ColDb.LightGray, Lang("ItemName", player.UserIDString), TextAnchor.MiddleLeft);

			//Element: Sell To Server Button
			LUI.LuiContainer sellToServerSortButton = cui.v2.CreateButton(listingTopPanel, new LuiOffset(414, 0, 504, 42), "ShoppyStock_UI stock sort serverSell", ColDb.LTD5);
			sellToServerSortButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");
			//Element: Sell To Server Button Text
			cui.v2.CreateText(sellToServerSortButton, new LuiOffset(0, 0, 90, 42), 18, ColDb.LightGray, Lang("SellToServerButton", player.UserIDString), TextAnchor.MiddleCenter);

			//Element: Buy Offer Button
			LUI.LuiContainer buyOfferButton = cui.v2.CreateButton(listingTopPanel, new LuiOffset(504, 0, 594, 42), "ShoppyStock_UI stock sort buyOffers", ColDb.LTD5);
			buyOfferButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Buy Offer Button Text
			cui.v2.CreateText(buyOfferButton, new LuiOffset(0, 0, 90, 42), 18, ColDb.LightGray, Lang("BuyOffersButton", player.UserIDString), TextAnchor.MiddleCenter);

			//Element: Sell Offer Button
			LUI.LuiContainer sellOfferButton = cui.v2.CreateButton(listingTopPanel, new LuiOffset(594, 0, 684, 42), "ShoppyStock_UI stock sort sellOffers", ColDb.LTD5);
			sellOfferButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Sell Offer Button Text
			cui.v2.CreateText(sellOfferButton, new LuiOffset(0, 0, 90, 42), 18, ColDb.LightGray, Lang("SellOffersButton", player.UserIDString), TextAnchor.MiddleCenter);

			//Element: Listings Scroll
			LuiScrollbar stockListingsBackground_Vertical = new LuiScrollbar()
			{
				autoHide = true,
				size = 4,
				handleColor = ColDb.LTD15,
				highlightColor = ColDb.LTD20,
				pressedColor = ColDb.LTD10,
				trackColor = ColDb.BlackTrans10
			};

			LUI.LuiContainer listingsScroll = cui.v2.CreateScrollView(stockListingsBackground, new LuiOffset(0, 0, 690, 358), true, false, ScrollRect.MovementType.Elastic, 0.1f, true, 0.1f, 25f, stockListingsBackground_Vertical, default, "ShoppyStockUI_StockItemScroll");
			listingsScroll.SetScrollContent(new LuiPosition(0, 1, 1, 1), new LuiOffset(0, -358, 0, 0));

			//Element: Scrollable Background
			cui.v2.CreatePanel(listingsScroll, LuiPosition.Full, LuiOffset.None, ColDb.Transparent, "ShoppyStockUI_StockItemList");

			DrawStockListings(player, cui, sb);
			
			byte[] uiBytes = cui.v2.GetUiBytes();
			cui.Destroy("ShoppyStockUI_ElementCore", player);
			cui.v2.SendUiBytes(player, uiBytes);
			Pool.FreeUnmanaged(ref sb);
		}

		private void SwitchStockCategory(BasePlayer player, string newCategory)
		{
			using CUI cui = new CUI(CuiHandler);
			ShoppyCache sc = cache[player.userID];
			StringBuilder sb = Pool.Get<StringBuilder>();
			if (sc.stockCache.category != newCategory && sc.stockCache.category != "Search")
			{
				string oldCategoryName = sb.Clear().Append("ShoppyStockUI_CategoryButton_").Append(sc.stockCache.category).ToString();
				cui.v2.Update(oldCategoryName).SetButtonColors(ColDb.Transparent);
			}
			sc.stockCache.category = newCategory;
			string imgColor = sc.stockCache.hideEmpty ? ColDb.GreenBg : ColDb.Transparent;
			cui.v2.Update("ShoppyStockUI_HideEmptyCheckmark").SetColor(imgColor);
			if (newCategory != "Search")
			{
				string newCategoryName = sb.Clear().Append("ShoppyStockUI_CategoryButton_").Append(newCategory).ToString();
				string color = greenCategories.Contains(newCategory) ? ColDb.GreenBg : ColDb.LTD15;
				cui.v2.Update(newCategoryName).SetButtonColors(color);
			}
			
			cui.v2.Update("ShoppyStockUI_StockCategoryText").SetText(Lang(sb.Clear().Append("CategoryName_").Append(sc.stockCache.category).ToString(), player.UserIDString), update: true);
			cui.v2.CreatePanel("ShoppyStockUI_StockItemScroll", LuiPosition.Full, LuiOffset.None, ColDb.Transparent, "ShoppyStockUI_StockItemList");
			DrawStockListings(player, cui, sb);
			byte[] uiBytes = cui.v2.GetUiBytes();
			cui.Destroy("ShoppyStockUI_StockItemList", player);
			Pool.FreeUnmanaged(ref sb);
			cui.v2.SendUiBytes(player, uiBytes);
		}

		private void RedrawStockListings(BasePlayer player)
		{
			using CUI cui = new CUI(CuiHandler);
			StringBuilder sb = Pool.Get<StringBuilder>();
			cui.v2.CreatePanel("ShoppyStockUI_StockItemScroll", LuiPosition.Full, LuiOffset.None, ColDb.Transparent, "ShoppyStockUI_StockItemList");
			DrawStockListings(player, cui, sb);
			Pool.FreeUnmanaged(ref sb);
			byte[] uiBytes = cui.v2.GetUiBytes();
			cui.Destroy("ShoppyStockUI_StockItemList", player);
			cui.v2.SendUiBytes(player, uiBytes);
		}

		private void DrawStockListings(BasePlayer player, CUI cui, StringBuilder sb)
		{
			ShoppyCache sc = cache[player.userID];
			StockData sd = data.stock[sc.shopName];
			CurrencyConfig currCfg = config.curr[sc.shopName].currCfg;
			StockConfig stockCfg = config.curr[sc.shopName].stockCfg;
			List<KeyValuePair<string, ulong>> sortedCategory = new();
			Dictionary<string, Dictionary<ulong, string>> translatedNames = Pool.Get<Dictionary<string, Dictionary<ulong, string>>>();
			translatedNames.Clear();
			bool isSearch = sc.stockCache.search.Length > 0;
			bool checkOtherCategories = greenCategories.Contains(sc.stockCache.category);
			bool validStockData = sd.playerData.players.TryGetValue(player.userID, out StockPlayerData spd);
			bool isFavourite = sc.stockCache.category == StringKeys.Cat_Favourites;
			bool isMyListings = sc.stockCache.category == StringKeys.Cat_MyListings;
			bool isAllItems = sc.stockCache.category == StringKeys.Cat_AllItems;
			foreach (var cat in stockItems[sc.shopName].categories)
			{
				if (!checkOtherCategories && !isSearch && cat.Key != sc.stockCache.category) continue;
				foreach (var item in cat.Value)
				{
					string shortname = item.Key;
					foreach (var skin in item.Value)
					{
						string itemName = GetStockItemName(stockCfg, sd, player.UserIDString, shortname, skin, sb);
						if (!isSearch || (isSearch && itemName.Contains(sc.stockCache.search, CompareOptions.IgnoreCase)))
						{
							if (checkOtherCategories && !isAllItems)
							{
								if (isFavourite && (!validStockData || !spd.favourites.TryGetValue(shortname, out var skins) || !skins.Contains(skin))) continue;
								if (isMyListings)
								{
									bool foundListing = sd.listings.sellOffers.TryGetValue(shortname, out var shortnameListings1) && shortnameListings1.TryGetValue(skin, out var skinListings1) && skinListings1.Any(x => x.userId == player.userID);
									if (!foundListing && sd.listings.buyOffers.TryGetValue(shortname, out var shortnameListings2) && shortnameListings2.TryGetValue(skin, out var skinListings2) && skinListings2.Any(x => x.userId == player.userID))
										foundListing = true;
									if (!foundListing) continue;
								}
							}
							if (isAllItems)
							{
								bool foundListing = sd.listings.sellOffers.TryGetValue(shortname, out var shortnameListings1) && shortnameListings1.TryGetValue(skin, out var skinListings1) && skinListings1.Count > 0;
								if (!foundListing && sd.listings.buyOffers.TryGetValue(shortname, out var shortnameListings2) && shortnameListings2.TryGetValue(skin, out var skinListings2) && skinListings2.Count > 0)
									foundListing = true;
								if (!foundListing && sd.cfg.sellItems.TryGetValue(shortname, out var shortnameListings3) && shortnameListings3.ContainsKey(skin))
									foundListing = true;
								if (!foundListing) continue;
							}
							translatedNames.TryAdd(item.Key, new());
							translatedNames[item.Key].Add(skin, itemName);
							sortedCategory.Add(new KeyValuePair<string, ulong>(item.Key, skin));
						}
					}
				}
			}

			int count = isSearch ? 50 : sortedCategory.Count;
			int scrollHeight = count * 54 - 6;
			if (scrollHeight < 358)
				scrollHeight = 358;
			cui.v2.Update("ShoppyStockUI_StockItemScroll").SetScrollContent(new LuiPosition(0, 1, 1, 1), new LuiOffset(0, -scrollHeight, 0, 0));
			
			switch (sc.stockCache.sortType)
			{
				case StockSortType.Name:
					sortedCategory = sortedCategory.OrderBy(x => translatedNames[x.Key][x.Value]).ToList();
					break;
				case StockSortType.ServerSell:
					sortedCategory = sortedCategory.OrderByDescending(x =>
					{
						if (sd.prices.items.TryGetValue(x.Key, out var skins) && skins.TryGetValue(x.Value, out var skinSell))
							return skinSell.price;
						return 0;
					}).ToList();
					break;
				case StockSortType.BuyOffer:
					sortedCategory = sortedCategory.OrderByDescending(x =>
					{
						if (sd.listings.buyOffers.TryGetValue(x.Key, out var buySkins) && buySkins.TryGetValue(x.Value, out var buyListings))
						{
							if (buyListings.Count == 0) return 0;
							float cheapest = float.MaxValue;
							foreach (var offer in buyListings)
								if (offer.price < cheapest)
									cheapest = offer.price;
							return cheapest;
						}
						return 0;
					}).ToList();
					break;
				case StockSortType.SellOffer:
					sortedCategory = sortedCategory.OrderByDescending(x =>
					{
						if (sd.listings.sellOffers.TryGetValue(x.Key, out var sellSkins) && sellSkins.TryGetValue(x.Value, out var sellListings))
						{
							if (sellListings.Count == 0) return 0;
							float highest = 0;
							foreach (var offer in sellListings)
								if (offer.price > highest)
									highest = offer.price;
							return highest;
						}
						return 0;
					}).ToList();
					break;
			}

			scrollHeight -= 48;
			foreach (var item in isSearch ? sortedCategory.Take(config.ui.searchLimit) : sortedCategory)
			{
				string shortname = item.Key;
				ulong skin = item.Value;
				bool foundAny = false;
				string price_serverSell = "-";
				if (sd.prices.items.TryGetValue(shortname, out var skins) && skins.TryGetValue(skin, out var skinSell))
				{
					price_serverSell = FormatCurrency(currCfg, skinSell.price, sb);
					foundAny = true;
				}
				string price_buyOffer = "-";
				if (sd.listings.buyOffers.TryGetValue(shortname, out var buySkins) && buySkins.TryGetValue(skin, out var buyListings) && buyListings.Count > 0)
				{
					float highest = 0;
					foreach (var offer in buyListings)
						if (offer.price > highest)
							highest = offer.price;
					price_buyOffer = Lang("BuyOfferCount", player.UserIDString, buyListings.Count, FormatCurrency(currCfg, highest, sb));
					foundAny = true;
				}
				string price_sellOffer  = "-";
				if (sd.listings.sellOffers.TryGetValue(shortname, out var sellSkins) && sellSkins.TryGetValue(skin, out var sellListings) && sellListings.Count > 0)
				{
					float cheapest = float.MaxValue;
					foreach (var offer in sellListings)
						if (offer.price < cheapest)
							cheapest = offer.price;
					price_sellOffer = Lang("SellOfferCount", player.UserIDString, sellListings.Count, FormatCurrency(currCfg, cheapest, sb));
					foundAny = true;
				}
				if (sc.stockCache.hideEmpty && !foundAny) continue;
				string itemName = translatedNames[shortname][skin];
				
				string command = sb.Clear().Append("ShoppyStock_UI stock listing ").Append(shortname).Append(' ').Append(skin).ToString();
				//Element: Stock Listing Button
				LUI.LuiContainer stockListingButton = cui.v2.CreateButton("ShoppyStockUI_StockItemList", new LuiOffset(0, scrollHeight, 698, scrollHeight + 54), command, ColDb.Transparent);
				stockListingButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				command = sb.Clear().Append("ShoppyStock_UI stock favourite ").Append(shortname).Append(' ').Append(skin).ToString();
				//Element: Favourite Button
				LUI.LuiContainer favouriteButton = cui.v2.CreateButton(stockListingButton, new LuiOffset(4, 12, 28, 36), command, ColDb.Transparent);
				favouriteButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Star Icon
				string color = validStockData && spd.favourites.TryGetValue(shortname, out var skins2) && skins2.Contains(skin) ? ColDb.Favourite : ColDb.LTD15;
				string name = sb.Clear().Append("ShoppyStockUI_Favourite_").Append(shortname).Append(skin).ToString();
				cui.v2.CreateSprite(favouriteButton, new LuiOffset(4, 4, 20, 20), "assets/icons/favourite_inactive.png", color, name);

				//Element: Listing Icon
				ulong displaySkin = skin;
				if (!skinRedirect.TryGetValue(shortname, out displaySkin))
					displaySkin = skin;
				LUI.LuiContainer listingIcon = cui.v2.CreateItemIcon(stockListingButton, new LuiOffset(32, 6, 68, 42), iconRedirect[shortname], displaySkin, ColDb.WhiteTrans80);
				listingIcon.SetMaterial("assets/content/ui/namefontmaterial.mat");
					
				//Element: Listing Name
				LUI.LuiContainer listingName = cui.v2.CreateText(stockListingButton, new LuiOffset(76, 0, 414, 48), 20, ColDb.LTD80, itemName, TextAnchor.MiddleLeft);
				listingName.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);
				
				//Element: Sell To Server Price
				LUI.LuiContainer sellToServerPrice = cui.v2.CreateText(stockListingButton, new LuiOffset(414, 3, 504, 51), 18, ColDb.LTD80, price_serverSell, TextAnchor.MiddleCenter);
				sellToServerPrice.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);
				
				//Element: Buy Offer Price
				int size = price_buyOffer.Length == 1 ? 18 : 10;
				LUI.LuiContainer buyOfferPrice = cui.v2.CreateText(stockListingButton, new LuiOffset(504, 3, 594, 51), size, ColDb.LTD80, price_buyOffer, TextAnchor.MiddleCenter);
				buyOfferPrice.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);
				
				//Element: Sell Offer Price
				size = price_sellOffer.Length == 1 ? 18 : 10;
				LUI.LuiContainer sellOfferPrice = cui.v2.CreateText(stockListingButton, new LuiOffset(594, 3, 684, 51), size, ColDb.LTD80, price_sellOffer, TextAnchor.MiddleCenter);
				sellOfferPrice.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

				//Element: Listing Divider
				LUI.LuiContainer listingDivider = cui.v2.CreateSprite(stockListingButton, new LuiOffset(32, 50, 672, 52), "assets/content/ui/dotted_line_horizontal.png", ColDb.DarkGray);
				listingDivider.SetImageType(Image.Type.Tiled);
				scrollHeight -= 54;
			}
			Pool.FreeUnmanaged(ref translatedNames);
		}

		private void ShowStockListingDetails(BasePlayer player, string shortname, ulong skin)
		{
			using CUI cui = new CUI(CuiHandler);
			ShoppyCache sc = cache[player.userID];
			sc.stockCache.shortname = shortname;
			sc.stockCache.skin = skin;
			sc.stockCache.selectedListingIndex = 0;
			StockDataConfig sdc = data.stock[sc.shopName].cfg;
			StockConfig stockCfg = config.curr[sc.shopName].stockCfg;
			bool canSellItems = sdc.sellItems.TryGetValue(shortname, out var skins1) && skins1.ContainsKey(skin);
			bool canBuyOffer = stockCfg.buyOffersEnabled && (!stockCfg.buyOfferDisabled.TryGetValue(shortname, out var skins2) || !skins2.Contains(skin));
			bool canSellOffer = stockCfg.sellOffersEnabled && (!stockCfg.sellOfferDisabled.TryGetValue(shortname, out var skins3) || !skins3.Contains(skin));
			if (sc.stockCache.listingPage == ListingPageType.ServerSell && !canSellItems)
				sc.stockCache.listingPage = ListingPageType.Unassigned;
			if (sc.stockCache.listingPage == ListingPageType.BuyOffer && !canBuyOffer)
				sc.stockCache.listingPage = ListingPageType.Unassigned;
			if (sc.stockCache.listingPage == ListingPageType.SellOffer && !canSellOffer)
				sc.stockCache.listingPage = ListingPageType.Unassigned;
			if (sc.stockCache.listingPage == ListingPageType.Unassigned)
			{
				if (canSellItems)
					sc.stockCache.listingPage = ListingPageType.ServerSell;
				else if (canBuyOffer)
					sc.stockCache.listingPage = ListingPageType.BuyOffer;
				else if (canSellOffer)
					sc.stockCache.listingPage = ListingPageType.SellOffer;
			}
			switch (sc.stockCache.listingPage)
			{
				case ListingPageType.Unassigned:
					SendEffect(player, StringKeys.Effect_Denied);
					ShowPopUp(player, Lang("NotAllowedThisPage", player.UserIDString));
					break;
				case ListingPageType.ServerSell:
					ShowStockDetailsBaseUI(player, cui);
					ShowStockServerSellUI(player, cui);
					byte[] uiBytes = cui.v2.GetUiBytes();
					cui.Destroy("ShoppyStockUI_StockCategorySection", player);
					cui.v2.SendUiBytes(player, uiBytes);
					break;
				case ListingPageType.BuyOffer:
					ShowStockDetailsBaseUI(player, cui);
					ShowStockOffersUI(player, cui, true);
					uiBytes = cui.v2.GetUiBytes();
					cui.Destroy("ShoppyStockUI_StockCategorySection", player);
					cui.v2.SendUiBytes(player, uiBytes);
					break;
				case ListingPageType.SellOffer:
					ShowStockDetailsBaseUI(player, cui);
					ShowStockOffersUI(player, cui, false);
					uiBytes = cui.v2.GetUiBytes();
					cui.Destroy("ShoppyStockUI_StockCategorySection", player);
					cui.v2.SendUiBytes(player, uiBytes);
					break;
			}

		}

		private void SwitchStockDetailsCategory(BasePlayer player, ListingPageType newType)
		{
			using CUI cui = new CUI(CuiHandler);
			ShoppyCache sc = cache[player.userID];
			ListingPageType oldType = sc.stockCache.listingPage;
			sc.stockCache.listingPage = newType;

			if (oldType != newType)
			{
				StringBuilder sb = Pool.Get<StringBuilder>();
				cui.v2.Update(sb.Clear().Append("ShoppyStockUI_StockSelect_").Append(oldType).ToString()).SetColor(ColDb.Transparent);
				cui.v2.Update(sb.Append("_Text").ToString()).SetTextColor(ColDb.LTD60);
			
				cui.v2.Update(sb.Clear().Append("ShoppyStockUI_StockSelect_").Append(newType).ToString()).SetColor(ColDb.DarkGray);
				cui.v2.Update(sb.Append("_Text").ToString()).SetTextColor(ColDb.LightGray);
				Pool.FreeUnmanaged(ref sb);
			}
			if (sc.stockCache.listingPage == ListingPageType.ServerSell)
				ShowStockServerSellUI(player, cui);
			else if (sc.stockCache.listingPage == ListingPageType.BuyOffer)
				ShowStockOffersUI(player, cui, true);
			else if (sc.stockCache.listingPage == ListingPageType.SellOffer)
				ShowStockOffersUI(player, cui, false);
			byte[] uiBytes = cui.v2.GetUiBytes();
			cui.Destroy("ShoppyStockUI_StockCategorySection", player);
			cui.v2.SendUiBytes(player, uiBytes);

		}

		private void UpdateFavourite(BasePlayer player, string shortname, ulong skin)
		{
			using CUI cui = new CUI(CuiHandler);
			ShoppyCache sc = cache[player.userID];
			StringBuilder sb = Pool.Get<StringBuilder>();
			data.stock[sc.shopName].playerData.players.TryAdd(player.userID, new());
			StockPlayerData spd = data.stock[sc.shopName].playerData.players[player.userID];
			bool removed;
			if (spd.favourites.TryGetValue(shortname, out var skins) && skins.Contains(skin))
			{
				skins.Remove(skin);
				removed = true;
			}
			else
			{
				spd.favourites.TryAdd(shortname, new());
				spd.favourites[shortname].Add(skin);
				removed = false;
			}
			string name = sb.Clear().Append("ShoppyStockUI_Favourite_").Append(shortname).Append(skin).ToString();
			Pool.FreeUnmanaged(ref sb);
			cui.v2.UpdateColor(name, removed ? ColDb.LTD15 : ColDb.Favourite);
			cui.v2.SendUi(player);
		}

		private void ShowStockDetailsBaseUI(BasePlayer player, CUI cui)
		{
			ShoppyCache sc = cache[player.userID];
			
			//Element: Core Panel
			LUI.LuiContainer shoppyUi = cui.v2.CreatePanel("ShoppyStockUI", LuiPosition.Full, LuiOffset.None, ColDb.Transparent, "ShoppyStockUI_StockListingCore").SetDestroy("ShoppyStockUI_StockListingCore");
			
			//Element: Background Blur
			LUI.LuiContainer backgroundBlur = cui.v2.CreatePanel(shoppyUi, LuiPosition.Full, LuiOffset.None, ColDb.BlackTrans20);
			backgroundBlur.SetMaterial("assets/content/ui/uibackgroundblur.mat");
			
			//Element: Background Darker
			LUI.LuiContainer backgroundDarker = cui.v2.CreatePanel(shoppyUi, LuiPosition.Full, LuiOffset.None, ColDb.BlackTrans20);
			backgroundDarker.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Center Anchor
			LUI.LuiContainer centerAnchor = cui.v2.CreatePanel(shoppyUi, LuiPosition.MiddleCenter, LuiOffset.None, ColDb.Transparent);

			//Element: Main Panel
			LUI.LuiContainer mainPanel = cui.v2.CreatePanel(centerAnchor, new LuiOffset(-400, -275, 400, 275), ColDb.DarkGray);
			mainPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Top Panel
			LUI.LuiContainer topPanel = cui.v2.CreatePanel(mainPanel, new LuiOffset(0, 514, 800, 550), ColDb.LTD5);
			topPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");
			
			//Element: Title Text
			cui.v2.CreateText(topPanel, new LuiOffset(12, 0, 800, 36), 22, ColDb.LightGray, Lang("StockItemDetails", player.UserIDString), TextAnchor.MiddleLeft);

			//Element: Close Button
			LUI.LuiContainer closeButton = cui.v2.CreateButton(topPanel, new LuiOffset(770, 6, 794, 30), "ShoppyStock_UI stock goBack", ColDb.RedBg);
			closeButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Close Icon
			cui.v2.CreateSprite(closeButton, new LuiOffset(5, 5, 19, 19), "assets/icons/close.png", ColDb.RedText);

			//Element: Lower Panel
			LUI.LuiContainer lowerPanel = cui.v2.CreateEmptyContainer(mainPanel, "ShoppyStockUI_StockDetalisLowerPanel", add: true).SetOffset(new LuiOffset(0, 0, 800, 514));

			//Element: Buttons Section
			LUI.LuiContainer buttonsSection = cui.v2.CreatePanel(lowerPanel, new LuiOffset(0, 454, 800, 514), ColDb.BlackTrans20);
			buttonsSection.SetMaterial("assets/content/ui/namefontmaterial.mat");
			
			StockDataConfig sdc = data.stock[sc.shopName].cfg;
			StockConfig stockCfg = config.curr[sc.shopName].stockCfg;
			bool isSell = sdc.sellItems.TryGetValue(sc.stockCache.shortname, out var skins1) && skins1.ContainsKey(sc.stockCache.skin);
			bool isBuyOffer = !stockCfg.buyOfferDisabled.TryGetValue(sc.stockCache.shortname, out var skins2) || !skins2.Contains(sc.stockCache.skin);
			bool isSellOffer = !stockCfg.sellOfferDisabled.TryGetValue(sc.stockCache.shortname, out var skins3) || !skins3.Contains(sc.stockCache.skin);

			int startX = 32;

			if (isSell)
			{
				string color = sc.stockCache.listingPage == ListingPageType.ServerSell ? ColDb.DarkGray : ColDb.Transparent;
				//Element: Sell To Server Button
				LUI.LuiContainer sellToServerButton = cui.v2.CreateButton(buttonsSection, new LuiOffset(startX, 0, startX + 224, 36), "ShoppyStock_UI stock openServerSell", color, name: "ShoppyStockUI_StockSelect_ServerSell");
				sellToServerButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				color = sc.stockCache.listingPage == ListingPageType.ServerSell ? ColDb.LightGray : ColDb.LTD60;
				//Element: Sell To Server Button Text
				cui.v2.CreateText(sellToServerButton, LuiPosition.Full, LuiOffset.None, 22, color, Lang("SellToServer", player.UserIDString), TextAnchor.MiddleCenter, "ShoppyStockUI_StockSelect_ServerSell_Text");
				startX += 256;
			}

			if (isBuyOffer)
			{
				string color = sc.stockCache.listingPage == ListingPageType.BuyOffer ? ColDb.DarkGray : ColDb.Transparent;
				//Element: Buy Offers Button
				LUI.LuiContainer buyOffersButton = cui.v2.CreateButton(buttonsSection, new LuiOffset(startX, 0, startX + 224, 36), "ShoppyStock_UI stock openBuyOffers", color, name: "ShoppyStockUI_StockSelect_BuyOffer");
				buyOffersButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				color = sc.stockCache.listingPage == ListingPageType.BuyOffer ? ColDb.LightGray : ColDb.LTD60;
				//Element: Buy Offers Button Text
				cui.v2.CreateText(buyOffersButton, LuiPosition.Full, LuiOffset.None, 22, color, Lang("BuyOffers", player.UserIDString), TextAnchor.MiddleCenter, "ShoppyStockUI_StockSelect_BuyOffer_Text");
				startX += 256;
			}

			if (isSellOffer)
			{
				string color = sc.stockCache.listingPage == ListingPageType.SellOffer ? ColDb.DarkGray : ColDb.Transparent;
				//Element: Sell Offers Button
				LUI.LuiContainer sellOffersButton = cui.v2.CreateButton(buttonsSection, new LuiOffset(startX, 0, startX + 224, 36), "ShoppyStock_UI stock openSellOffers", color, name: "ShoppyStockUI_StockSelect_SellOffer");
				sellOffersButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				color = sc.stockCache.listingPage == ListingPageType.SellOffer ? ColDb.LightGray : ColDb.LTD60;
				//Element: Sell Offers Button Text
				cui.v2.CreateText(sellOffersButton, LuiPosition.Full, LuiOffset.None, 22, color, Lang("SellOffers", player.UserIDString), TextAnchor.MiddleCenter, "ShoppyStockUI_StockSelect_SellOffer_Text");
			}
			
			//Element: Category Section
			//LUI.LuiContainer categorySection = cui.v2.CreateEmptyContainer(lowerPanel, "ShoppyStockUI_StockCategorySection").SetOffset(new LuiOffset(0, 0, 300, 454));
			
		}

		private void ShowStockServerSellUI(BasePlayer player, CUI cui)
		{
			ShoppyCache sc = cache[player.userID];
			StringBuilder sb = Pool.Get<StringBuilder>();
			StockData sd = data.stock[sc.shopName];
			CurrencyConfig currCfg = config.curr[sc.shopName].currCfg;
			StockConfig stockCfg = config.curr[sc.shopName].stockCfg;
			SellItemData sid = sd.prices.items[sc.stockCache.shortname][sc.stockCache.skin];
			SellItemConfig sic = sd.cfg.sellItems[sc.stockCache.shortname][sc.stockCache.skin];
			
			LUI.LuiContainer sellItemDetailsSection = cui.v2.CreateEmptyContainer("ShoppyStockUI_StockDetalisLowerPanel", "ShoppyStockUI_StockCategorySection", true).SetOffset(new LuiOffset(0, 0, 300, 454));
			
			//Element: Item Icon
			
			ulong displaySkin = sc.stockCache.skin;
			if (!skinRedirect.TryGetValue(sc.stockCache.shortname, out displaySkin))
				displaySkin = sc.stockCache.skin;
			LUI.LuiContainer itemIcon = cui.v2.CreateItemIcon(sellItemDetailsSection, new LuiOffset(85, 310, 215, 440), iconRedirect[sc.stockCache.shortname], displaySkin, ColDb.WhiteTrans80);
			itemIcon.SetMaterial("assets/content/ui/namefontmaterial.mat");

			string name = GetStockItemName(stockCfg, sd, player.UserIDString, sc.stockCache.shortname, sc.stockCache.skin, sb);
			//Element: Item Name
			cui.v2.CreateText(sellItemDetailsSection, new LuiOffset(16, 282, 284, 310), 20, ColDb.LightGray, name.ToUpper(), TextAnchor.UpperCenter);

			//Element: Current Price Title
			cui.v2.CreateText(sellItemDetailsSection, new LuiOffset(16, 255, 284, 278), 15, ColDb.LTD60, Lang("CurrentSellPrice", player.UserIDString), TextAnchor.LowerCenter);

			//Element: Current Price
			cui.v2.CreateText(sellItemDetailsSection, new LuiOffset(16, 226, 284, 255), 20, ColDb.LightGray, FormatCurrency(currCfg, sid.price, sb), TextAnchor.UpperCenter, "ShoppyStockUI_ServerSellPrice");

			bool allowPriceAlert = stockCfg.sellCfg.priceAlertEnabled && (stockCfg.sellCfg.priceAlertPerm.Length == 0 || permission.UserHasPermission(player.UserIDString, stockCfg.sellCfg.priceAlertPerm));
			bool allowAutoSell = stockCfg.sellCfg.bankAutoSellEnabled && (stockCfg.sellCfg.bankAutoSellPerm.Length == 0 || permission.UserHasPermission(player.UserIDString, stockCfg.sellCfg.bankAutoSellPerm));

			int startX = allowPriceAlert && allowAutoSell ? 16 : 75;

			PlayerAlertData pad = null;
			if (allowPriceAlert)
			{
				//Element: Price Alert Title
				cui.v2.CreateText(sellItemDetailsSection, new LuiOffset(startX, 187, startX + 118, 227), 10, ColDb.LTD80, Lang("AlertSendPrice", player.UserIDString), TextAnchor.LowerCenter);

				//Element: Price Alert Background
				LUI.LuiContainer priceAlertBackground = cui.v2.CreatePanel(sellItemDetailsSection, new LuiOffset(startX, 147, startX + 118, 183), ColDb.BlackTrans20);
				priceAlertBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

				float alertPrice = 0;
				if (sd.playerData.players.TryGetValue(player.userID, out var playerData))
					if (playerData.alerts.TryGetValue(sc.stockCache.shortname, out var shortnames))
						if (shortnames.TryGetValue(sc.stockCache.skin, out pad))
							alertPrice = pad.alertPrice;

				//Element: Price Alert Input
				cui.v2.CreateInput(priceAlertBackground, LuiPosition.Full, LuiOffset.None, ColDb.LightGray, alertPrice.ToString(CultureInfo.InvariantCulture), 20, "ShoppyStock_UI stock serverSell priceAlert", 0, true, CUI.Handler.FontTypes.RobotoCondensedRegular, TextAnchor.MiddleCenter).SetInputKeyboard(hudMenuInput: true);

				startX += 150;
			}

			if (allowAutoSell)
			{
				//Element: Price Sell Title
				cui.v2.CreateText(sellItemDetailsSection, new LuiOffset(startX, 187, startX + 118, 227), 10, ColDb.LTD80, Lang("AutoSellPrice", player.UserIDString), TextAnchor.LowerCenter);

				//Element: Price Sell Background
				LUI.LuiContainer priceSellBackground = cui.v2.CreatePanel(sellItemDetailsSection, new LuiOffset(startX, 147, startX + 118, 183), ColDb.BlackTrans20);
				priceSellBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");
				
				float sellPrice = 0;
				if (allowPriceAlert && pad != null)
					sellPrice = pad.instaSellPrice;
				else if (!allowPriceAlert)
				{
					if (sd.playerData.players.TryGetValue(player.userID, out var playerData))
						if (playerData.alerts.TryGetValue(sc.stockCache.shortname, out var shortnames))
							if (shortnames.TryGetValue(sc.stockCache.skin, out pad))
								sellPrice = pad.instaSellPrice;
				}

				//Element: Price Sell Input
				cui.v2.CreateInput(priceSellBackground, LuiPosition.Full, LuiOffset.None, ColDb.LightGray, sellPrice.ToString(CultureInfo.InvariantCulture), 20, "ShoppyStock_UI stock serverSell autoSell", 0, true, CUI.Handler.FontTypes.RobotoCondensedRegular, TextAnchor.MiddleCenter).SetInputKeyboard(hudMenuInput: true);
			}

			int amountsMaxHeight = !allowPriceAlert && !allowAutoSell ? 226 : 147;
			
			bool canPlayerBank = stockCfg.categoriesBank && (stockCfg.categoriesBankPerm.Length == 0 || permission.UserHasPermission(player.UserIDString, stockCfg.categoriesBankPerm));

			int inventoryAmount = 0;
			foreach (var item in player.inventory.containerMain.itemList)
			{
				if (item.info.shortname == sc.stockCache.shortname && item.skin == sc.stockCache.skin)
					inventoryAmount += item.amount;
			}
			foreach (var item in player.inventory.containerBelt.itemList)
			{
				if (item.info.shortname == sc.stockCache.shortname && item.skin == sc.stockCache.skin)
					inventoryAmount += item.amount;
			}

			int bankAmount = 0;
			if (canPlayerBank && sd.playerData.players.TryGetValue(player.userID, out var playerData2))
				if (playerData2.bankItems.TryGetValue(sc.stockCache.shortname, out var shortnames2))
					if (shortnames2.TryGetValue(sc.stockCache.skin, out var bankContains))
						bankAmount = bankContains.amount;
				
			string lang = canPlayerBank ? 
				Lang("InventoryBankItemCount", player.UserIDString, inventoryAmount, FormatCurrency(currCfg, sid.price * inventoryAmount, sb), bankAmount, FormatCurrency(currCfg, sid.price * bankAmount, sb)) :
				Lang("InventoryItemCount", player.UserIDString, inventoryAmount, FormatCurrency(currCfg, sid.price * inventoryAmount, sb));
			
			//Element: Item Amounts
			cui.v2.CreateText(sellItemDetailsSection, new LuiOffset(16, 66, 284, amountsMaxHeight), 16, ColDb.LightGray, lang, TextAnchor.MiddleCenter, "ShoppyStockUI_ServerSellItemAmounts");

			startX = canPlayerBank ? 16 : 91;

			//Element: Open Sell Button
			LUI.LuiContainer openSellButton = cui.v2.CreateButton(sellItemDetailsSection, new LuiOffset(startX, 16, startX + 118, 66), "ShoppyStock_UI stock openSell", ColDb.GreenBg);
			openSellButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Open Sell Button Text
			cui.v2.CreateText(openSellButton, LuiPosition.Full, LuiOffset.None, 20, ColDb.GreenText, Lang("OpenSellInventory", player.UserIDString), TextAnchor.MiddleCenter);
			startX += 150;
			
			if (canPlayerBank)
			{
				//Element: Sell From Bank Button
				LUI.LuiContainer sellFromBankButton = cui.v2.CreateButton(sellItemDetailsSection, new LuiOffset(startX, 16, startX + 118, 66), "ShoppyStock_UI stock serverSell sellFromBank", ColDb.GreenBg);
				sellFromBankButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Sell From Bank Button Text
				cui.v2.CreateText(sellFromBankButton, LuiPosition.Full, LuiOffset.None, 20, ColDb.GreenText, Lang("SellFromBank", player.UserIDString), TextAnchor.MiddleCenter, "ShoppyStockUI_BankSellButtonText");
			}
			
			//Element: Chart Section
			LUI.LuiContainer chartSection = cui.v2.CreatePanel(sellItemDetailsSection, new LuiOffset(300, 154, 800, 454), ColDb.LTD5);
			chartSection.SetMaterial("assets/content/ui/namefontmaterial.mat");

			if (stockCfg.sellCfg.priceChart && (stockCfg.sellCfg.priceChartPerm.Length == 0 || (stockCfg.sellCfg.priceChartPerm.Length > 0 && permission.UserHasPermission(player.UserIDString, stockCfg.sellCfg.priceChartPerm))))
			{			

				//Element: Price Chart Title
				cui.v2.CreateText(chartSection, new LuiOffset(16, 266, 239, 292), 20, ColDb.LightGray, Lang("PriceHistoryChart", player.UserIDString), TextAnchor.UpperLeft);

				//Element: Price Chart Section
				LUI.LuiContainer priceChartSection = cui.v2.CreateEmptyContainer(chartSection, add: true).SetOffset(new LuiOffset(48, 78, 452, 268));

				//Element: Background Bar 1
				LUI.LuiContainer backgroundBar1 = cui.v2.CreatePanel(priceChartSection, new LuiOffset(0, 17, 404, 32), ColDb.LTD10);
				backgroundBar1.SetMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Background Bar 2
				LUI.LuiContainer backgroundBar2 = cui.v2.CreatePanel(priceChartSection, new LuiOffset(0, 47, 404, 62), ColDb.LTD10);
				backgroundBar2.SetMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Background Bar 3
				LUI.LuiContainer backgroundBar3 = cui.v2.CreatePanel(priceChartSection, new LuiOffset(0, 77, 404, 92), ColDb.LTD10);
				backgroundBar3.SetMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Background Bar 4
				LUI.LuiContainer backgroundBar4 = cui.v2.CreatePanel(priceChartSection, new LuiOffset(0, 107, 404, 122), ColDb.LTD10);
				backgroundBar4.SetMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Background Bar 5
				LUI.LuiContainer backgroundBar5 = cui.v2.CreatePanel(priceChartSection, new LuiOffset(0, 137, 404, 152), ColDb.LTD10);
				backgroundBar5.SetMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Background Bar 6
				LUI.LuiContainer backgroundBar6 = cui.v2.CreatePanel(priceChartSection, new LuiOffset(0, 167, 404, 182), ColDb.LTD10);
				backgroundBar6.SetMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Vertical Line
				LUI.LuiContainer verticalLine = cui.v2.CreatePanel(priceChartSection, new LuiOffset(0, 0, 2, 185), ColDb.LTD50);
				verticalLine.SetMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Horizontal Line
				LUI.LuiContainer horizontalLine = cui.v2.CreatePanel(priceChartSection, new LuiOffset(0, 0, 404, 2), ColDb.LTD50);
				horizontalLine.SetMaterial("assets/content/ui/namefontmaterial.mat");

				if (sc.stockCache.currentTimespan == 0)
					foreach (var perm in stockCfg.sellCfg.priceChartTimespanPerms)
					{
						if (perm.Value.Length == 0 || permission.UserHasPermission(player.UserIDString, perm.Value))
						{
							sc.stockCache.currentTimespan = perm.Key;
							break;
						}
					}
				GraphData gd; 
				if (!sid.cachedGraphs.TryGetValue(sc.stockCache.currentTimespan, out gd))
				{ 
					gd = new GraphData()
					{
						minPrice = 0,
						maxPrice = 0,
						imageId = 0
					};
					DrawPriceGraph(player);
				}

				//Element: Chart Price 1
				LUI.LuiContainer chartPrice1 = cui.v2.CreateText(priceChartSection, new LuiOffset(-44, 2, -4, 32), 10, ColDb.LTD60, gd.minPrice.ToString("0.###"), TextAnchor.MiddleRight, "ShoppyStockUI_PriceGraphPrice_0");
				chartPrice1.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

				float priceDiff = (gd.maxPrice - gd.minPrice) / 5f;

				//Element: Chart Price 2
				string stringPrice = (gd.minPrice + priceDiff).ToString("0.###");
				LUI.LuiContainer chartPrice2 = cui.v2.CreateText(priceChartSection, new LuiOffset(-44, 32, -4, 62), 10, ColDb.LTD60, stringPrice, TextAnchor.MiddleRight, "ShoppyStockUI_PriceGraphPrice_1");
				chartPrice2.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

				//Element: Chart Price 3
				stringPrice = (gd.minPrice + priceDiff * 2).ToString("0.###");
				LUI.LuiContainer chartPrice3 = cui.v2.CreateText(priceChartSection, new LuiOffset(-44, 62, -4, 92), 10, ColDb.LTD60, stringPrice, TextAnchor.MiddleRight, "ShoppyStockUI_PriceGraphPrice_2");
				chartPrice3.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

				//Element: Chart Price 4
				stringPrice = (gd.minPrice + priceDiff * 3).ToString("0.###");
				LUI.LuiContainer chartPrice4 = cui.v2.CreateText(priceChartSection, new LuiOffset(-44, 92, -4, 122), 10, ColDb.LTD60, stringPrice, TextAnchor.MiddleRight,"ShoppyStockUI_PriceGraphPrice_3");
				chartPrice4.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

				//Element: Chart Price 5
				stringPrice = (gd.minPrice + priceDiff * 4).ToString("0.###");
				LUI.LuiContainer chartPrice5 = cui.v2.CreateText(priceChartSection, new LuiOffset(-44, 122, -4, 152), 10, ColDb.LTD60, stringPrice, TextAnchor.MiddleRight,"ShoppyStockUI_PriceGraphPrice_4");
				chartPrice5.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

				//Element: Chart Price 6
				LUI.LuiContainer chartPrice6 = cui.v2.CreateText(priceChartSection, new LuiOffset(-44, 152, -4, 182), 10, ColDb.LTD60, gd.maxPrice.ToString("0.###"), TextAnchor.MiddleRight,"ShoppyStockUI_PriceGraphPrice_5");
				chartPrice6.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

				//Element: Chart Scroll
				LuiScrollbar priceChartSection_Horizontal = new LuiScrollbar()
				{
					autoHide = true,
					size = 0,
					handleColor = ColDb.Transparent,
					highlightColor = ColDb.Transparent,
					pressedColor = ColDb.Transparent,
					trackColor = ColDb.Transparent
				};

				LUI.LuiContainer chartScroll = cui.v2.CreateScrollView(priceChartSection, new LuiOffset(2, -36, 402, 185), false, true, ScrollRect.MovementType.Elastic, 0.1f, true, 0.1f, 25f, default, priceChartSection_Horizontal);
				chartScroll.SetScrollContent(new LuiPosition(1, 0, 1, 1), new LuiOffset(-1500, 0, 0, 0));

				startX = 1500;
				//Element: Chart Time Now
				LUI.LuiContainer chartTime1 = cui.v2.CreateText(chartScroll, new LuiOffset(startX - 60, 0, startX, 14), 10, ColDb.LTD60, Lang("Now", player.UserIDString), TextAnchor.UpperCenter);
				chartTime1.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

				startX -= 60;
				int counter = 1;
				while (startX > 0)
				{
					string elName = sb.Clear().Append("ShoppyStockUI_GraphTimestamp_").Append(counter).ToString();
					int timespanSum = sc.stockCache.currentTimespan * 60 * 8 * counter;
					string time = FormatTime(timespanSum, sb);
					string formattedTime = sb.Clear().Append('-').Append(time).ToString();
					//Element: Chart Time 2
					cui.v2.CreateText(chartScroll, new LuiOffset(startX - 60, 0, startX, 14), 10, ColDb.LTD60, formattedTime, TextAnchor.UpperCenter, elName).SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);
					startX -= 60;
					counter++;
				}
				
				//Element: Graph Image
				LUI.LuiContainer graphImage = cui.v2.CreateImage(chartScroll, new LuiOffset(0, 30, 1500, 200), gd.imageId.ToString(), ColDb.White, "ShoppyStockUI_GraphImage");
				graphImage.SetMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Chart Resolution Title
				cui.v2.CreateText(chartSection, new LuiOffset(32, 45, 232, 65), 12, ColDb.LTD60, Lang("ChartResolution", player.UserIDString), TextAnchor.LowerLeft);

				startX = 32;
				foreach (var perm in stockCfg.sellCfg.priceChartTimespanPerms)
				{
					if (perm.Value.Length == 0 || permission.UserHasPermission(player.UserIDString, perm.Value))
					{
						//Element: Chart Button 1
						string command = sb.Clear().Append("ShoppyStock_UI stock serverSell timespan ").Append(perm.Key).ToString();
						string elName = sb.Clear().Append("ShoppyStockUI_GraphTimestampSelect_").Append(perm.Key).ToString();
						string elNameText = sb.Append("_Text").ToString();
						string color = sc.stockCache.currentTimespan == perm.Key ? ColDb.LTD20 : ColDb.DarkGray;
						LUI.LuiContainer chartButton1 = cui.v2.CreateButton(chartSection, new LuiOffset(startX, 16, startX + 50, 42), command, color, name: elName);
						chartButton1.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

						//Element: Chart Button 1 Text
						color = sc.stockCache.currentTimespan == perm.Key ? ColDb.LightGray : ColDb.LTD40;
						cui.v2.CreateText(chartButton1, LuiPosition.Full, LuiOffset.None, 15, color, FormatTime(perm.Key * 60, sb), TextAnchor.MiddleCenter, elNameText);
						startX += 50;
					}
				}
			}
			
			//Element: Buy From Server Section
			LUI.LuiContainer buyFromServerSection = cui.v2.CreatePanel(sellItemDetailsSection, new LuiOffset(300, 0, 800, 154), ColDb.LTD10);
			buyFromServerSection.SetMaterial("assets/content/ui/namefontmaterial.mat");
			
			if (sic.purchaseFromServer && stockCfg.sellCfg.buyFromServerEnabled && (stockCfg.sellCfg.buyFromServerPerm.Length == 0 || (stockCfg.sellCfg.buyFromServerPerm.Length > 0 && permission.UserHasPermission(player.UserIDString, stockCfg.sellCfg.buyFromServerPerm))))
			{

				//Element: Buy From Server Title
				cui.v2.CreateText(buyFromServerSection, new LuiOffset(16, 120, 239, 146), 20, ColDb.LightGray, Lang("BuyFromServer", player.UserIDString), TextAnchor.UpperLeft);

				//Element: Amount Available Title
				cui.v2.CreateText(buyFromServerSection, new LuiOffset(16, 95, 239, 121), 15, ColDb.LTD60, Lang("AmountAvailable", player.UserIDString), TextAnchor.LowerLeft);

				//Element: Amount Available
				cui.v2.CreateText(buyFromServerSection, new LuiOffset(20, 60, 239, 95), 23, ColDb.LightGray, sid.amountAvailable.ToString(), TextAnchor.UpperLeft, "ShoppyStockUI_ServerBuyAmount");

				//Element: Current Price Title
				cui.v2.CreateText(buyFromServerSection, new LuiOffset(16, 45, 239, 71), 15, ColDb.LTD60, Lang("CurrentPrice", player.UserIDString), TextAnchor.LowerLeft);

				//Element: Current Price
				cui.v2.CreateText(buyFromServerSection, new LuiOffset(20, 10, 239, 45), 23, ColDb.LightGray, FormatCurrency(currCfg, sid.buyPrice, sb), TextAnchor.UpperLeft, "ShoppyStockUI_ServerBuyPrice");

				//Element: Purchase Amount Background
				LUI.LuiContainer purchaseAmountBackground = cui.v2.CreatePanel(buyFromServerSection, new LuiOffset(217, 61, 317, 97), ColDb.BlackTrans20);
				purchaseAmountBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Purchase Amount Title
				cui.v2.CreateText(purchaseAmountBackground, new LuiOffset(-24, 38, 124, 69), 11, ColDb.LTD80, Lang("PurchaseAmount", player.UserIDString), TextAnchor.LowerCenter);

				sc.stockCache.stockActionAmount = 0;
				
				//Element: Purchase Amount Input
				cui.v2.CreateInput(purchaseAmountBackground, new LuiOffset(0, 0, 100, 36), ColDb.LightGray, "0", 18, "ShoppyStock_UI stock serverSell purchaseAmount", 0, true, CUI.Handler.FontTypes.RobotoCondensedRegular, TextAnchor.MiddleCenter).SetInputKeyboard(hudMenuInput: true);

				//Element: Purchase Price Title
				cui.v2.CreateText(buyFromServerSection, new LuiOffset(217, 43, 317, 61), 11, ColDb.LTD80, Lang("PurchasePrice", player.UserIDString), TextAnchor.LowerCenter);

				//Element: Purchase Price
				cui.v2.CreateText(buyFromServerSection, new LuiOffset(217, 5, 317, 41), 18, ColDb.LightGray, FormatCurrency(currCfg, 0, sb), TextAnchor.UpperCenter, "ShoppyStockUI_ServerBuyPurchasePrice");

				//Element: Balance Title
				cui.v2.CreateText(buyFromServerSection, new LuiOffset(333, 43, 433, 61), 11, ColDb.LTD80, Lang("YouHave", player.UserIDString), TextAnchor.LowerCenter);

				//Element: Balance
				cui.v2.CreateText(buyFromServerSection, new LuiOffset(333, 5, 433, 41), 18, ColDb.LightGray, FormatCurrency(currCfg, GetPlayerCurrency(player, sc.shopName), sb), TextAnchor.UpperCenter, "ShoppyStockUI_ServerBuyPlayerBalance");

				//Element: Purchase Button
				LUI.LuiContainer purchaseButton = cui.v2.CreateButton(buyFromServerSection, new LuiOffset(333, 61, 433, 97), "ShoppyStock_UI stock serverSell buyFromServer", ColDb.GreenBg);
				purchaseButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Purchase Button Text
				cui.v2.CreateText(purchaseButton, LuiPosition.Full, LuiOffset.None, 15, ColDb.GreenText, Lang("Purchase", player.UserIDString), TextAnchor.MiddleCenter);

				//Element: Hint Icon
				LUI.LuiContainer hintIcon = cui.v2.CreateSprite(buyFromServerSection, new LuiOffset(476, 130, 492, 146), "assets/icons/info.png", ColDb.LTD30);

				//Element: Hint Text
				LUI.LuiContainer hintText = cui.v2.CreateText(hintIcon, new LuiOffset(-304, -4, -4, 20), 12, ColDb.LTD30, Lang("AmountAvailableHint", player.UserIDString), TextAnchor.MiddleRight);
				hintText.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);
			}
			Pool.FreeUnmanaged(ref sb);
		}

		private void UpdateServerSellUI(BasePlayer player)
		{
			using CUI cui = new CUI(CuiHandler);
			ShoppyCache sc = cache[player.userID];
			
			StringBuilder sb = Pool.Get<StringBuilder>();
			StockData sd = data.stock[sc.shopName];
			CurrencyConfig currCfg = config.curr[sc.shopName].currCfg;
			StockConfig stockCfg = config.curr[sc.shopName].stockCfg;
			SellItemData sid = sd.prices.items[sc.stockCache.shortname][sc.stockCache.skin];
			SellItemConfig sic = sd.cfg.sellItems[sc.stockCache.shortname][sc.stockCache.skin];

			cui.v2.UpdateText("ShoppyStockUI_ServerSellPrice", FormatCurrency(currCfg, sid.price, sb));
			
			bool canPlayerBank = stockCfg.categoriesBank && (stockCfg.categoriesBankPerm.Length == 0 || permission.UserHasPermission(player.UserIDString, stockCfg.categoriesBankPerm));

			int inventoryAmount = 0;
			foreach (var item in player.inventory.containerMain.itemList)
			{
				if (item.info.shortname == sc.stockCache.shortname && item.skin == sc.stockCache.skin)
					inventoryAmount += item.amount;
			}
			foreach (var item in player.inventory.containerBelt.itemList)
			{
				if (item.info.shortname == sc.stockCache.shortname && item.skin == sc.stockCache.skin)
					inventoryAmount += item.amount;
			}

			int bankAmount = 0;
			if (canPlayerBank && sd.playerData.players.TryGetValue(player.userID, out var playerData2))
				if (playerData2.bankItems.TryGetValue(sc.stockCache.shortname, out var shortnames2))
					if (shortnames2.TryGetValue(sc.stockCache.skin, out var bankContains))
						bankAmount = bankContains.amount;
			
			string lang = canPlayerBank ? 
				Lang("InventoryBankItemCount", player.UserIDString, inventoryAmount, FormatCurrency(currCfg, sid.price * inventoryAmount, sb), bankAmount, FormatCurrency(currCfg, sid.price * bankAmount, sb)) :
				Lang("InventoryItemCount", player.UserIDString, inventoryAmount, FormatCurrency(currCfg, sid.price * inventoryAmount, sb));
			cui.v2.UpdateText("ShoppyStockUI_ServerSellItemAmounts", lang);
			if (stockCfg.sellCfg.buyFromServerEnabled && sic.purchaseFromServer && (stockCfg.sellCfg.buyFromServerPerm.Length == 0 || (stockCfg.sellCfg.buyFromServerPerm.Length > 0 && permission.UserHasPermission(player.UserIDString, stockCfg.sellCfg.buyFromServerPerm))))
			{
				cui.v2.UpdateText("ShoppyStockUI_ServerBuyAmount", sid.amountAvailable.ToString());
				cui.v2.UpdateText("ShoppyStockUI_ServerBuyPrice", FormatCurrency(currCfg, sid.buyPrice, sb));
				cui.v2.UpdateText("ShoppyStockUI_ServerBuyPurchasePrice", FormatCurrency(currCfg, sc.stockCache.stockActionAmount * sid.buyPrice, sb));
				cui.v2.UpdateText("ShoppyStockUI_ServerBuyPlayerBalance", FormatCurrency(currCfg, GetPlayerCurrency(player, sc.shopName), sb));
			}
			Pool.FreeUnmanaged(ref sb);
			cui.v2.SendUi(player);
		}

		private void SwitchGraphTimespan(BasePlayer player, int timespan, int oldTimespan = 0)
		{
			ShoppyCache sc = cache[player.userID];
			if (sc.stockCache.currentTimespan != timespan) return;
			StockData sd = data.stock[sc.shopName];
			SellItemData sid = sd.prices.items[sc.stockCache.shortname][sc.stockCache.skin];
			
			if (!sid.cachedGraphs.TryGetValue(sc.stockCache.currentTimespan, out GraphData gd) ||
			    sid.nextGraphUpdates[sc.stockCache.currentTimespan] > 0)
			{
				DrawPriceGraph(player, oldTimespan);
				return;
			}
			using CUI cui = new CUI(CuiHandler);
			StringBuilder sb = Pool.Get<StringBuilder>();
			if (oldTimespan != 0)
			{
				cui.v2.UpdateColor(sb.Clear().Append("ShoppyStockUI_GraphTimestampSelect_").Append(oldTimespan).ToString(), ColDb.DarkGray);
				cui.v2.Update(sb.Append("_Text").ToString()).SetTextColor(ColDb.LTD40);
				cui.v2.UpdateColor(sb.Clear().Append("ShoppyStockUI_GraphTimestampSelect_").Append(timespan).ToString(), ColDb.LTD20);
				cui.v2.Update(sb.Append("_Text").ToString()).SetTextColor(ColDb.LightGray);
			}
			cui.v2.UpdateText("ShoppyStockUI_PriceGraphPrice_0", gd.minPrice.ToString("0.###"));
			float priceDiff = (gd.maxPrice - gd.minPrice) / 5f;
			string stringPrice = (gd.minPrice + priceDiff).ToString("0.###");
			cui.v2.UpdateText("ShoppyStockUI_PriceGraphPrice_1", stringPrice);
			stringPrice = (gd.minPrice + priceDiff * 2).ToString("0.###");
			cui.v2.UpdateText("ShoppyStockUI_PriceGraphPrice_2", stringPrice);
			stringPrice = (gd.minPrice + priceDiff * 3).ToString("0.###");
			cui.v2.UpdateText("ShoppyStockUI_PriceGraphPrice_3", stringPrice);
			stringPrice = (gd.minPrice + priceDiff * 4).ToString("0.###");
			cui.v2.UpdateText("ShoppyStockUI_PriceGraphPrice_4", stringPrice);
			cui.v2.UpdateText("ShoppyStockUI_PriceGraphPrice_5", gd.maxPrice.ToString("0.###"));
			int startX = 1500;
			startX -= 60;
			int counter = 1;
			while (startX > 0)
			{
				string elName = sb.Clear().Append("ShoppyStockUI_GraphTimestamp_").Append(counter).ToString();
				int timespanSum = sc.stockCache.currentTimespan * 60 * 8 * counter;
				string time = FormatTime(timespanSum, sb);
				string formattedTime = sb.Clear().Append('-').Append(time).ToString();
				cui.v2.UpdateText(elName, formattedTime);
				startX -= 60;
				counter++;
			}
			Pool.FreeUnmanaged(ref sb);
			cui.v2.Update("ShoppyStockUI_GraphImage").SetImage(gd.imageId.ToString());
			cui.v2.SendUi(player);
		}

		private void ShowStockOffersUI(BasePlayer player, CUI cui, bool buyOffers)
		{
			ShoppyCache sc = cache[player.userID];
			StringBuilder sb = Pool.Get<StringBuilder>();
			StockData sd = data.stock[sc.shopName];
			CurrencyConfig currCfg = config.curr[sc.shopName].currCfg;
			StockConfig stockCfg = config.curr[sc.shopName].stockCfg;
			
			LUI.LuiContainer listingDetailsSection = cui.v2.CreateEmptyContainer("ShoppyStockUI_StockDetalisLowerPanel", "ShoppyStockUI_StockCategorySection", true).SetOffset(new LuiOffset(0, 0, 300, 454));

			bool isValidListing = false;
			StockListingData[] sldList = new StockListingData[0];

			List<StockListingData> sldTemp = Pool.Get<List<StockListingData>>();
			sldTemp.Clear();
			
			if (buyOffers)
			{
				if (sd.listings.buyOffers.TryGetValue(sc.stockCache.shortname, out var skins))
					if (skins.TryGetValue(sc.stockCache.skin, out var buyOffersList))
					{
						foreach (var listing in buyOffersList)
						{
							if (sc.stockCache.hideMyListings && listing.userId == player.userID) continue;
							if (listing.isHidden && listing.userId != player.userID && sc.stockCache.listingCode != listing.customAccessCode) continue;
							sldTemp.Add(listing);
						}
						sldList = sldTemp.OrderByDescending(x => x.price).ToArray();
						isValidListing = sldList.Length > 0;
					}
			}
			else
			{
				if (sd.listings.sellOffers.TryGetValue(sc.stockCache.shortname, out var skins))
					if (skins.TryGetValue(sc.stockCache.skin, out var sellOffersList))
					{
						foreach (var listing in sellOffersList)
						{
							if (sc.stockCache.hideMyListings && listing.userId == player.userID) continue;
							if (listing.isHidden && listing.userId != player.userID && sc.stockCache.listingCode != listing.customAccessCode) continue;
							sldTemp.Add(listing);
						}
						sldList = sldTemp.OrderBy(x => x.price).ToArray();
						isValidListing = sldList.Length > 0;
						
					}
			}
			Pool.FreeUnmanaged(ref sldTemp);

			StockListingData selectedListing = isValidListing ? sldList[0] : null;
			if (isValidListing && sldList.Length > sc.stockCache.selectedListingIndex)
				selectedListing = sldList[sc.stockCache.selectedListingIndex];

			if (isValidListing)
			{
				//Element: Listing Image
				string defShortname = ItemManager.itemDictionary[selectedListing.item.id].shortname;
				ulong displaySkin = selectedListing.item.skin;
				if (!skinRedirect.TryGetValue(defShortname, out displaySkin))
					displaySkin = selectedListing.item.skin;
				LUI.LuiContainer listingImage = cui.v2.CreateItemIcon(listingDetailsSection, new LuiOffset(85, 310, 215, 440), iconRedirect[defShortname], displaySkin, ColDb.WhiteTrans80, "ShoppyStockUI_ListingImage");
				listingImage.SetMaterial("assets/content/ui/namefontmaterial.mat");
			
				string name = string.IsNullOrEmpty(selectedListing.item.name) ? GetStockItemName(stockCfg, sd, player.UserIDString, sc.stockCache.shortname, sc.stockCache.skin, sb) : selectedListing.item.name;
				//Element: Listing Name
				cui.v2.CreateText(listingDetailsSection, new LuiOffset(16, 282, 284, 310), 20, ColDb.LightGray, name.ToUpper(), TextAnchor.UpperCenter, "ShoppyStockUI_ListingName");
			}
			else
			{
				//Element: Listing Image
				ulong displaySkin = sc.stockCache.skin;
				if (!skinRedirect.TryGetValue(sc.stockCache.shortname, out displaySkin))
					displaySkin = sc.stockCache.skin;
				LUI.LuiContainer listingImage = cui.v2.CreateItemIcon(listingDetailsSection, new LuiOffset(85, 310, 215, 440), iconRedirect[sc.stockCache.shortname], displaySkin, ColDb.WhiteTrans80, "ShoppyStockUI_ListingImage");
				listingImage.SetMaterial("assets/content/ui/namefontmaterial.mat");
			
				string name = GetStockItemName(stockCfg, sd, player.UserIDString, sc.stockCache.shortname, sc.stockCache.skin, sb);
				//Element: Listing Name
				cui.v2.CreateText(listingDetailsSection, new LuiOffset(16, 282, 284, 310), 20, ColDb.LightGray, name.ToUpper(), TextAnchor.UpperCenter, "ShoppyStockUI_ListingName");
			}
			//Element: Listed By Title
			cui.v2.CreateText(listingDetailsSection, new LuiOffset(16, 266, 284, 286), 15, ColDb.LTD60, Lang("ListedBy", player.UserIDString), TextAnchor.LowerCenter);

			//Element: Listed By
			string text = isValidListing ? selectedListing.userName : "---";
			cui.v2.CreateText(listingDetailsSection, new LuiOffset(16, 237, 284, 266), 20, ColDb.LightGray, text, TextAnchor.UpperCenter, "ShoppyStockUI_ListingOwner");

			//Element: Sell Price Title
			text = buyOffers ? Lang("BuyPrice", player.UserIDString) : Lang("SellPrice", player.UserIDString);
			cui.v2.CreateText(listingDetailsSection, new LuiOffset(16, 219, 134, 242), 15, ColDb.LTD60, text, TextAnchor.LowerCenter);

			//Element: Sell Price
			text = isValidListing ? Lang("PriceForEach", player.UserIDString, FormatCurrency(currCfg, selectedListing.price, sb)) : "---";
			cui.v2.CreateText(listingDetailsSection, new LuiOffset(16, 190, 134, 219), 19, ColDb.LightGray, text, TextAnchor.UpperCenter, "ShoppyStockUI_ListingPrice");

			//Element: In Stock Title
			text = buyOffers ? Lang("Amount", player.UserIDString) : Lang("InStock", player.UserIDString);
			cui.v2.CreateText(listingDetailsSection, new LuiOffset(166, 219, 284, 242), 15, ColDb.LTD60, text, TextAnchor.LowerCenter);

			//Element: In Stock
			text = isValidListing ? selectedListing.item.amount.ToString("N0") : "---";
			cui.v2.CreateText(listingDetailsSection, new LuiOffset(166, 190, 284, 219), 19, ColDb.LightGray, text, TextAnchor.UpperCenter, "ShoppyStockUI_ListingAmount");

			if (isValidListing)
			{
				//Element: Details Title
				cui.v2.CreateText(listingDetailsSection, new LuiOffset(16, 173, 284, 196), 15, ColDb.LTD60, Lang("Details", player.UserIDString), TextAnchor.LowerCenter);

				//Element: Details
				string itemDetails = GetItemDetails(player, sc.shopName, selectedListing.item);
				cui.v2.CreateText(listingDetailsSection, new LuiOffset(16, 144, 284, 173), 20, ColDb.LightGray, itemDetails, TextAnchor.UpperCenter, "ShoppyStockUI_ListingDetails");

				sc.stockCache.selectedListingOwner = selectedListing.userId;
				UpdateBuySellOfferOwnedSectionUI(player, cui, selectedListing);
			}

			//Element: Listings Background
			LUI.LuiContainer listingsBackground = cui.v2.CreatePanel(listingDetailsSection, new LuiOffset(300, 0, 800, 454), ColDb.LTD5);
			listingsBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");
			
			//Element: Listings Title
			text = buyOffers ? Lang("ListOfBuyOffers", player.UserIDString) : Lang("ListOfSellOffers", player.UserIDString);
			cui.v2.CreateText(listingsBackground, new LuiOffset(16, 420, 296, 446), 20, ColDb.LightGray, text, TextAnchor.UpperLeft);

			//Element: New Buy Request Button
			LUI.LuiContainer newBuyRequestButton = cui.v2.CreateButton(listingsBackground, new LuiOffset(16, 381, 186, 417), "ShoppyStock_UI stock buySellOffer new", ColDb.GreenBg);
			newBuyRequestButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: New Buy Request Button Text
			text = buyOffers ? Lang("CreateBuyOffer", player.UserIDString) : Lang("CreateSellOffer", player.UserIDString);
			cui.v2.CreateText(newBuyRequestButton, LuiPosition.Full, LuiOffset.None, 15, ColDb.GreenText, text, TextAnchor.MiddleCenter);

			//Element: Hide My Offers Button
			LUI.LuiContainer hideMyOffersButton = cui.v2.CreateButton(listingsBackground, new LuiOffset(194, 381, 374, 417), "ShoppyStock_UI stock buySellOffer hideMine", ColDb.Transparent);
			hideMyOffersButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Checkmark Background
			LUI.LuiContainer checkmarkBackground = cui.v2.CreatePanel(hideMyOffersButton, new LuiOffset(5, 8, 25, 28), ColDb.BlackTrans20);
			checkmarkBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Checkmark
			string color = sc.stockCache.hideMyListings ? ColDb.GreenBg : ColDb.Transparent;
			cui.v2.CreateSprite(checkmarkBackground, new LuiOffset(4, 4, 16, 16), "assets/icons/close.png", color);

			//Element: Hide My Offers Button Text
			cui.v2.CreateText(hideMyOffersButton, new LuiOffset(33, 0, 180, 36), 12, ColDb.LTD80, Lang("HideMyOffers", player.UserIDString), TextAnchor.MiddleLeft);

			//Element: Balance Title
			cui.v2.CreateText(listingsBackground, new LuiOffset(323, 401, 484, 429), 16, ColDb.LTD60, Lang("YourBalance", player.UserIDString), TextAnchor.LowerRight);

			//Element: Balance Amount
			cui.v2.CreateText(listingsBackground, new LuiOffset(367, 377, 457, 401), 18, ColDb.LightGray, FormatCurrency(currCfg, GetPlayerCurrency(player, sc.shopName), sb), TextAnchor.MiddleRight);

			//Element: Balance Icon
			FindAndCreateValidImage(cui, listingsBackground, new LuiOffset(463, 379, 483, 399), currCfg.icon, ColDb.GreenBg);

			//Element: Listed Items Title
			cui.v2.CreateText(listingsBackground, new LuiOffset(16, 355, 252, 378), 15, ColDb.LTD60, Lang("ListedOffers", player.UserIDString), TextAnchor.LowerLeft);

			//Element: Listed Items Background
			LUI.LuiContainer listedItemsBackground = cui.v2.CreatePanel(listingsBackground, new LuiOffset(16, 16, 484, 352), ColDb.BlackTrans20);
			listedItemsBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Listed Items Scroll
			LuiScrollbar listedItemsBackground_Vertical = new LuiScrollbar()
			{
				autoHide = true,
				size = 4,
				handleColor = ColDb.LTD15,
				highlightColor = ColDb.LTD20,
				pressedColor = ColDb.LTD10,
				trackColor = ColDb.BlackTrans10
			};
			int scrollHeight = sldList.Length * 60;
			if (scrollHeight < 336)
				scrollHeight = 336;

			LUI.LuiContainer listedItemsScroll = cui.v2.CreateScrollView(listedItemsBackground, new LuiOffset(0, 0, 458, 336), true, false, ScrollRect.MovementType.Elastic, 0.1f, true, 0.1f, 25f, listedItemsBackground_Vertical, default);
			listedItemsScroll.SetScrollContent(new LuiPosition(0, 1, 1, 1), new LuiOffset(0, -scrollHeight, 0, 0));

			//Element: Scrollable Holder
			cui.v2.CreatePanel(listedItemsScroll, LuiPosition.Full, LuiOffset.None, ColDb.Transparent);

			if (sldList.Length == 0)
			{
				//Element: Item Name
				cui.v2.CreateText(listedItemsScroll, LuiPosition.Full, new LuiOffset(16, 16, 0, 0), 22, ColDb.LightGray, Lang("NoListingsAvailable", player.UserIDString), TextAnchor.MiddleCenter);
			}
			else
			{
				scrollHeight -= 60;
				int counter = 0;
				string cachedItemName = GetStockItemName(stockCfg, sd, player.UserIDString, sc.stockCache.shortname, sc.stockCache.skin, sb);
				string amountLang = buyOffers ? Lang("AmountRequested", player.UserIDString) : Lang("Seller", player.UserIDString);
				string detailsLang = Lang("Details", player.UserIDString);
				string sellerLang = buyOffers ? Lang("Buyer", player.UserIDString) : Lang("Seller", player.UserIDString) ;
				foreach (var listing in sldList)
				{
					string command = sb.Clear().Append("ShoppyStock_UI stock buySellOffer details ").Append(counter).ToString();
					//Element: Item Listing
					LUI.LuiContainer itemListing = cui.v2.CreateButton(listedItemsScroll, new LuiOffset(0, scrollHeight, 458, scrollHeight + 60), command, ColDb.Transparent);
					itemListing.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

					string itemNameString = !string.IsNullOrEmpty(listing.item.name) ? listing.item.name : cachedItemName;
					//Element: Item Name
					cui.v2.CreateText(itemListing, new LuiOffset(58, 35, 326, 57), 17, ColDb.LightGray, itemNameString, TextAnchor.LowerLeft);

					//Element: Item Price
					cui.v2.CreateText(itemListing, new LuiOffset(326, 35, 444, 57), 17, ColDb.LightGray, Lang("PriceForEach", player.UserIDString, FormatCurrency(currCfg, listing.price, sb)), TextAnchor.LowerRight);

					//Element: Item Icon
					string defShortname = ItemManager.itemDictionary[listing.item.id].shortname;
					ulong displaySkin = listing.item.skin;
					if (!skinRedirect.TryGetValue(defShortname, out displaySkin))
						displaySkin = listing.item.skin;
					LUI.LuiContainer itemIcon = cui.v2.CreateItemIcon(itemListing, new LuiOffset(8, 8, 52, 52), iconRedirect[defShortname], displaySkin, ColDb.WhiteTrans80);
					itemIcon.SetMaterial("assets/content/ui/namefontmaterial.mat");

					//Element: Item Amount Title
					cui.v2.CreateText(itemListing, new LuiOffset(58, 21, 186, 34), 10, ColDb.LTD60, amountLang, TextAnchor.MiddleCenter);

					//Element: Item Amount
					cui.v2.CreateText(itemListing, new LuiOffset(58, 3, 186, 21), 13, ColDb.LightGray, listing.item.amount.ToString("N0"), TextAnchor.UpperCenter);

					//Element: Item Details Title
					cui.v2.CreateText(itemListing, new LuiOffset(186, 21, 314, 34), 10, ColDb.LTD60, detailsLang, TextAnchor.MiddleCenter);

					//Element: Item Details
					string details = GetItemDetails(player, sc.shopName, selectedListing.item);
					cui.v2.CreateText(itemListing, new LuiOffset(186, 3, 314, 21), 13, ColDb.LightGray, details, TextAnchor.UpperCenter);

					//Element: Item Seller Title
					cui.v2.CreateText(itemListing, new LuiOffset(314, 21, 444, 34), 10, ColDb.LTD60, sellerLang, TextAnchor.MiddleCenter);

					//Element: Item Seller
					cui.v2.CreateText(itemListing, new LuiOffset(314, 3, 444, 21), 13, ColDb.LightGray, listing.userName, TextAnchor.UpperCenter);
					
					//Element: Item Hidden Indicator
					color = listing.isHidden ? ColDb.RedBg : ColDb.Transparent;
					text = sb.Clear().Append("ShoppyStockUI_HiddenIndicator_").Append(counter).ToString();
					cui.v2.CreateSprite(itemListing, new LuiOffset(425, 23, 441, 39), "assets/content/ui/gameui/cardgames/cannot_see_icon.png", color, text);

					//Element: Item Selection Outline
					color = sc.stockCache.selectedListingIndex == counter ? ColDb.GreenBg : ColDb.Transparent;
					string name = sb.Clear().Append("ShoppyStockUI_SelectedListing_").Append(counter).ToString();
					LUI.LuiContainer itemSelectionOutline = cui.v2.CreateSprite(itemListing, new LuiOffset(0, 0, 454, 60), "assets/content/ui/ui.box.tga", color, name);
					itemSelectionOutline.SetImageType(Image.Type.Tiled);
					scrollHeight -= 60;
					counter++;
				}
			}
			Pool.FreeUnmanaged(ref sb);
		}

		private void UpdateBuySellOfferOwnedSectionUI(BasePlayer player, CUI cui, StockListingData sld)
		{
			ShoppyCache sc = cache[player.userID];
			bool isBuyOffer = sc.stockCache.listingPage == ListingPageType.BuyOffer;
			//Element: Switchable Elements
			LUI.LuiContainer switchableElements = cui.v2.CreateEmptyContainer("ShoppyStockUI_StockCategorySection", "ShoppyStockUI_SwitchableElement", add: true).SetOffset(new LuiOffset(0, 0, 300, 150)).SetDestroy("ShoppyStockUI_SwitchableElement");

			if (sld.userId == player.userID)
			{
				//Element: Remaining Time Title
				cui.v2.CreateText(switchableElements, new LuiOffset(16, 127, 284, 150), 15, ColDb.LTD60, Lang("RemainingTime", player.UserIDString), TextAnchor.LowerCenter);

				if (sld.listingEndTime != DateTime.MinValue && sld.listingEndTime != DateTime.MaxValue)
				{
					float time = (float)(sld.listingEndTime - DateTime.Now).TotalSeconds;
					//Element: Remaining Time
					LUI.LuiContainer remainingTime = cui.v2.CreateCountdown("ShoppyStockUI_SwitchableElement", new LuiOffset(16, 98, 284, 127), 20, ColDb.LightGray, "%TIME_LEFT%", TextAnchor.UpperCenter, 0, 0, name: "ShoppyStockUI_RemainingTime");
					remainingTime.SetCountdown(time, 0, numberFormat: "d'd 'h'h 'm'm 's's'");
					remainingTime.SetCountdownTimerFormat(TimerFormat.Custom);
					remainingTime.SetCountdownDestroy(false);
				}
				else if (sld.listingEndTime == DateTime.MaxValue)
					cui.v2.CreateText("ShoppyStockUI_SwitchableElement", new LuiOffset(16, 98, 284, 127), 20, ColDb.LightGray, Lang("UntilWipe", player.UserIDString), TextAnchor.UpperCenter, "ShoppyStockUI_RemainingTime");
				else if (sld.listingEndTime == DateTime.MinValue)
					cui.v2.CreateText("ShoppyStockUI_SwitchableElement", new LuiOffset(16, 98, 284, 127), 20, ColDb.LightGray, Lang("PermanentTime", player.UserIDString), TextAnchor.UpperCenter, "ShoppyStockUI_RemainingTime");
				
				//Element: Hide Listing
				LUI.LuiContainer hideListing = cui.v2.CreateButton(switchableElements, new LuiOffset(53, 60, 247, 96), "ShoppyStock_UI stock buySellOffer visibility", ColDb.RedBg);
				hideListing.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				string text = sld.isHidden ? Lang("ListingHidden", player.UserIDString, sld.customAccessCode) : Lang("HideListing", player.UserIDString);
				//Element: Hide Listing Button Text
				cui.v2.CreateText(hideListing, LuiPosition.Full, LuiOffset.None, 15, ColDb.RedText, text, TextAnchor.MiddleCenter, name: "ShoppyStockUI_HideShowListing");

				//Element: Remove Listing
				LUI.LuiContainer removeListing = cui.v2.CreateButton(switchableElements, new LuiOffset(53, 16, 247, 52), "ShoppyStock_UI stock buySellOffer remove", ColDb.RedBg);
				removeListing.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Remove Listing Button Text
				cui.v2.CreateText(removeListing, LuiPosition.Full, LuiOffset.None, 18, ColDb.RedText, Lang("RemoveListing", player.UserIDString), TextAnchor.MiddleCenter);
			}
			else
			{
				//Element: Amount Input Background
				LUI.LuiContainer amountInputBackground = cui.v2.CreatePanel(switchableElements, new LuiOffset(95, 82, 205, 124), ColDb.BlackTrans20);
				amountInputBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Purchase Amount Title
				string text = isBuyOffer ? Lang("SellAmount", player.UserIDString) : Lang("PurchaseAmount", player.UserIDString);
				cui.v2.CreateText(amountInputBackground, new LuiOffset(-42, 47, 152, 74), 15, ColDb.LTD60, text, TextAnchor.LowerCenter);

				//Element: Decrease Button
				LUI.LuiContainer decreaseButton = cui.v2.CreateButton(amountInputBackground, new LuiOffset(-42, 0, 0, 42), "ShoppyStock_UI stock buySellOffer decrease", ColDb.RedBg);
				decreaseButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Decrease Button Icon
				cui.v2.CreateSprite(decreaseButton, new LuiOffset(9, 9, 33, 33), "assets/icons/subtract.png", ColDb.RedText);

				sc.stockCache.stockActionAmount = 0;
				//Element: Amount Input
				cui.v2.CreateInput(amountInputBackground, LuiPosition.Full, LuiOffset.None, ColDb.LightGray, "0", 27, "ShoppyStock_UI stock buySellOffer setItemAmount", 0, true, CUI.Handler.FontTypes.RobotoCondensedBold, TextAnchor.MiddleCenter, "ShoppyStockUI_ListingAmountInput").SetInputKeyboard(hudMenuInput: true);

				//Element: Increase Button
				LUI.LuiContainer increaseButton = cui.v2.CreateButton(amountInputBackground, new LuiOffset(110, 0, 152, 42), "ShoppyStock_UI stock buySellOffer increase", ColDb.GreenBg);
				increaseButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Increase Button Icon
				cui.v2.CreateSprite(increaseButton, new LuiOffset(9, 9, 33, 33), "assets/icons/add.png", ColDb.GreenText);

				//Element: Purchase Button
				LUI.LuiContainer purchaseButton = cui.v2.CreateButton(switchableElements, new LuiOffset(53, 16, 247, 66), "", ColDb.LTD20, name: "ShoppyStockUI_ListingPurchaseButton");
				purchaseButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Purchase Button Text
				cui.v2.CreateText(purchaseButton, LuiPosition.Full, LuiOffset.None, 20, ColDb.LTD60, Lang("WrongAmount", player.UserIDString), TextAnchor.MiddleCenter, "ShoppyStockUI_ListingPurchaseButton_Text");
			}
		}

		private void OpenStockSubmitInventory(BasePlayer player)
		{
			ShoppyCache sc = cache[player.userID];
			using CUI cui = new CUI(CuiHandler);
			bool isBuyOffer = sc.stockCache.listingPage == ListingPageType.BuyOffer;
			LUI.LuiContainer shoppyUi = cui.v2.CreateParent(CUI.ClientPanels.Inventory, LuiPosition.LowerCenter, "ShoppyStockUI_Inventory").SetDestroy("ShoppyStockUI_Inventory");
			
			//Element: Hint Icon
			LUI.LuiContainer hintIcon = cui.v2.CreateSprite(shoppyUi, new LuiOffset(550, 298, 566, 314), "assets/icons/info.png", ColDb.LTD60);

			//Element: Hint Text
			string text = isBuyOffer ? Lang("BuyOfferHint", player.UserIDString) : Lang("SellOfferHint", player.UserIDString);
			LUI.LuiContainer hintText = cui.v2.CreateText(hintIcon, new LuiOffset(-298, -5, -4, 21), 9, ColDb.LTD60, text, TextAnchor.MiddleRight); 
			hintText.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

			//Element: Sell Order Hint
			text = isBuyOffer ? Lang("BuyOfferItem", player.UserIDString) : Lang("SellOfferItem", player.UserIDString);
			cui.v2.CreateText(shoppyUi, new LuiOffset(202, 271, 572, 292), 12, ColDb.LTD80, text, TextAnchor.MiddleLeft);

			//Element: Submit Button
			LUI.LuiContainer submitButton = cui.v2.CreateButton(shoppyUi, new LuiOffset(199, 117, 329, 173), "", ColDb.LTD40, name: "ShoppyStockUI_StockSubmitButton");
			submitButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Submit Button Text
			cui.v2.CreateText(submitButton, new LuiOffset(49, 0, 130, 56), 18, ColDb.LTD80, Lang("PriceRequired", player.UserIDString), TextAnchor.MiddleLeft, "ShoppyStockUI_StockSubmitButton_Text");

			//Element: Submit Button Image
			cui.v2.CreateSprite(submitButton, new LuiOffset(16, 16, 41, 41), "assets/icons/exit.png", ColDb.LTD80, "ShoppyStockUI_StockSubmit_Icon");

			if (isBuyOffer)
			{
				//Element: Item Icon Preview
				StockConfig stockCfg = config.curr[sc.shopName].stockCfg;
				ulong displaySkin = sc.stockCache.skin;
				if (!skinRedirect.TryGetValue(sc.stockCache.shortname, out displaySkin))
					displaySkin = sc.stockCache.skin;
				LUI.LuiContainer itemIconPreview = cui.v2.CreateItemIcon(shoppyUi, new LuiOffset(358, 215, 406, 263), iconRedirect[sc.stockCache.shortname], displaySkin, ColDb.WhiteTrans80);
				itemIconPreview.SetMaterial("assets/content/ui/namefontmaterial.mat");
			}

			cui.v2.SendUi(player);
			if (isBuyOffer)
				RedrawStockSubmitInventory(player, isBuyOffer);
			
            player.EndLooting();
            sc.stockCache.containerType = isBuyOffer ? ContainerType.BuyOffer : ContainerType.SellOffer;
            SpawnAndOpenContainer(player);
		}

		private void RedrawStockSubmitInventory(BasePlayer player, bool isBuyOffer)
		{
			ShoppyCache sc = cache[player.userID];
			Item targetItem = null;
			if (!isBuyOffer)
			{
				targetItem = sc.stockCache.container.inventory.itemList[0];
				if (targetItem == null) return;
			}
			StringBuilder sb = Pool.Get<StringBuilder>();
			StockData sd = data.stock[sc.shopName];
			CurrencyConfig currCfg = config.curr[sc.shopName].currCfg;
			StockConfig stockCfg = config.curr[sc.shopName].stockCfg;
			using CUI cui = new CUI(CuiHandler);
			
			LUI.LuiContainer shoppyUi = cui.v2.CreateParent("ShoppyStockUI_Inventory", LuiPosition.LowerCenter, "ShoppyStockUI_Inventory_Connector").SetDestroy("ShoppyStockUI_Inventory_Connector");
			
			//Element: Title
			string text = isBuyOffer ? Lang("NewBuyOffer", player.UserIDString) : Lang("NewSellOffer", player.UserIDString);
			cui.v2.CreateText(shoppyUi, new LuiOffset(193, 522, 572, 554), 20, ColDb.LightGray, text, TextAnchor.MiddleLeft);

			//Element: Main Panel
			LUI.LuiContainer mainPanel = cui.v2.CreatePanel(shoppyUi, new LuiOffset(192, 322, 572, 522), ColDb.LightGrayTransRust);
			mainPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

			string shortname = isBuyOffer ? sc.stockCache.shortname : targetItem.info.shortname;
			ulong skin = isBuyOffer ? sc.stockCache.skin : targetItem.skin;
			//Element: Item Icon
			
			ulong displaySkin = skin;
			if (!skinRedirect.TryGetValue(shortname, out displaySkin))
				displaySkin = skin;
			LUI.LuiContainer itemIcon = cui.v2.CreateItemIcon(mainPanel, new LuiOffset(16, 144, 56, 184), iconRedirect[shortname], displaySkin, ColDb.WhiteTrans80);
			itemIcon.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Item Name
			text = GetStockItemName(stockCfg, sd, player.UserIDString, shortname, skin, sb);
			LUI.LuiContainer itemName = cui.v2.CreateText(mainPanel, new LuiOffset(62, 140, 364, 188), 18, ColDb.LightGray, text, TextAnchor.MiddleLeft);
			itemName.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

			//Element: Amount Title
			LUI.LuiContainer amountTitle = cui.v2.CreateText(mainPanel, new LuiOffset(16, 73, 120, 97), 12, ColDb.LTD80, Lang("Amount", player.UserIDString), TextAnchor.UpperCenter);

			//Element: Amount Background
			LUI.LuiContainer amountBackground = cui.v2.CreatePanel(amountTitle, new LuiOffset(0, 27, 104, 57), ColDb.LightGrayTransRust);
			amountBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Amount Input
			int amount = isBuyOffer ? sc.stockCache.stockActionAmount : targetItem.amount;
			
			text = isBuyOffer ? amount.ToString() : targetItem.amount.ToString("N0");
			string command = isBuyOffer ? "ShoppyStock_UI stock buySellOffer setAmount" : string.Empty;
			LUI.LuiContainer amountInput = cui.v2.CreateInput(amountBackground, LuiPosition.Full, LuiOffset.None, ColDb.LightGray, text, 17, command, 0, true, CUI.Handler.FontTypes.RobotoCondensedRegular, TextAnchor.MiddleCenter).SetInputKeyboard(hudMenuInput: true);
			amountInput.SetInputReadOnly(!isBuyOffer);

			//Element: Price Title
			LUI.LuiContainer priceTitle = cui.v2.CreateText(mainPanel, new LuiOffset(138, 73, 242, 97), 12, ColDb.LTD80, Lang("PricePerItem", player.UserIDString), TextAnchor.UpperCenter);

			//Element: Price Background
			LUI.LuiContainer priceBackground = cui.v2.CreatePanel(priceTitle, new LuiOffset(0, 27, 104, 57), ColDb.LightGrayTransRust);
			priceBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

			float price = sc.stockCache.offerPrice;
			//Element: Price Input
			cui.v2.CreateInput(priceBackground, LuiPosition.Full, LuiOffset.None, ColDb.LightGray, price.ToString(CultureInfo.InvariantCulture), 17, "ShoppyStock_UI stock buySellOffer setPrice", 0, true, CUI.Handler.FontTypes.RobotoCondensedRegular, TextAnchor.MiddleCenter).SetInputKeyboard(hudMenuInput: true);

			//Element: Listing Time Title
			LUI.LuiContainer listingTimeTitle = cui.v2.CreateText(mainPanel, new LuiOffset(260, 73, 364, 97), 12, ColDb.LTD80, Lang("ListingTime", player.UserIDString), TextAnchor.UpperCenter);

			TimeListingConfig tlc = null;
			if (isBuyOffer)
			{
				int counter = 0;
				foreach (var offer in stockCfg.buyOfferTimes)
					if (offer.reqPerm.Length == 0 || permission.UserHasPermission(player.UserIDString, offer.reqPerm))
					{
						if (counter == sc.stockCache.buyOfferTaxIndex)
						{
							tlc = offer;
							break;
						}
						counter++;
					}
			}
			else
			{
				int counter = 0;
				foreach (var offer in stockCfg.sellOfferTimes)
					if (offer.reqPerm.Length == 0 || permission.UserHasPermission(player.UserIDString, offer.reqPerm))
					{
						if (counter == sc.stockCache.sellOfferTaxIndex)
						{
							tlc = offer;
							break;
						}
						counter++;
					}
			}
			if (tlc == null)
			{
				Puts("Player doesn't have access to any offer listing times. Change that to fix this error!");
				return;
			}
			float tax = tlc.taxAmount;
			
			foreach (var perm in tlc.taxAmountPerms)
				if (perm.Value < tax && permission.UserHasPermission(player.UserIDString, perm.Key))
					tax = perm.Value;
			//Element: Tax Amount
			cui.v2.CreateText(listingTimeTitle, new LuiOffset(0, 60, 104, 74), 9, ColDb.LTD60, Lang("TaxAmount", player.UserIDString, tax), TextAnchor.LowerCenter, "ShoppyStockUI_TaxAmount");

			//Element: Listing Time Background
			LUI.LuiContainer listingTimeBackground = cui.v2.CreatePanel(listingTimeTitle, new LuiOffset(0, 27, 104, 57), ColDb.LightGrayTransRust);
			listingTimeBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Listing Time Text
			LUI.LuiContainer listingTimeText = cui.v2.CreateText(listingTimeBackground, new LuiOffset(15, 0, 89, 30), 17, ColDb.LightGray, FormatListingTime(player, tlc.listingTime, sb), TextAnchor.MiddleCenter, "ShoppyStockUI_ListingTime");
			listingTimeText.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

			//Element: Prev Listing Time Button
			LUI.LuiContainer prevListingTimeButton = cui.v2.CreateButton(listingTimeBackground, new LuiOffset(0, 0, 15, 30), "ShoppyStock_UI stock buySellOffer prevTime", ColDb.LightGrayTransRust);
			prevListingTimeButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Prev Listing Time Button Text
			cui.v2.CreateText(prevListingTimeButton, LuiPosition.Full, LuiOffset.None, 15, ColDb.LightGray, "<", TextAnchor.MiddleCenter);

			//Element: Next Listing Time Button
			LUI.LuiContainer nextListingTimeButton = cui.v2.CreateButton(listingTimeBackground, new LuiOffset(89, 0, 104, 30), "ShoppyStock_UI stock buySellOffer nextTime", ColDb.LightGrayTransRust);
			nextListingTimeButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Next Listing Time Button Text
			cui.v2.CreateText(nextListingTimeButton, LuiPosition.Full, LuiOffset.None, 15, ColDb.LightGray, ">", TextAnchor.MiddleCenter);

			//Element: Balance Title
			LUI.LuiContainer balanceTitle = cui.v2.CreateText(mainPanel, new LuiOffset(16, 16, 120, 40), 12, ColDb.LTD80, Lang("YourBalance", player.UserIDString, tax), TextAnchor.UpperCenter);

			//Element: Balance
			cui.v2.CreateText(balanceTitle, new LuiOffset(0, 27, 104, 53), 17, ColDb.LightGray, FormatCurrency(currCfg, GetPlayerCurrency(player, sc.shopName), sb), TextAnchor.LowerCenter);

			//Element: Earnings Title
			text = isBuyOffer ? Lang("ItemCost", player.UserIDString) : Lang("SellEarnings", player.UserIDString);
			LUI.LuiContainer earningsTitle = cui.v2.CreateText(mainPanel, new LuiOffset(138, 16, 242, 40), 12, ColDb.LTD80, text, TextAnchor.UpperCenter);

			//Element: Listing Cost
			cui.v2.CreateText(earningsTitle, new LuiOffset(0, 27, 104, 53), 17, ColDb.LightGray, FormatCurrency(currCfg, amount * price, sb), TextAnchor.LowerCenter, "ShoppyStockUI_ListingPriceField_1");

			text = isBuyOffer ? Lang("TotalCost", player.UserIDString) : Lang("ListingTax", player.UserIDString);
			//Element: Listing Tax Title
			LUI.LuiContainer listingTaxTitle = cui.v2.CreateText(mainPanel, new LuiOffset(260, 16, 364, 40), 12, ColDb.LTD80, text, TextAnchor.UpperCenter);

			float finalPrice = isBuyOffer ? amount * price + (amount * price / 100f * tax) : (amount * price / 100f * tax);
			//Element: Listing Tax
			cui.v2.CreateText(listingTaxTitle, new LuiOffset(0, 27, 104, 53), 17, ColDb.LightGray, FormatCurrency(currCfg, finalPrice, sb), TextAnchor.LowerCenter, "ShoppyStockUI_ListingPriceField_2");

			//Element: Broadcast Listing Button
			LUI.LuiContainer broadcastListingButton = cui.v2.CreateButton(shoppyUi, new LuiOffset(192, 72, 392, 108), "ShoppyStock_UI stock buySellOffer switchBroadcast", ColDb.Transparent);
			broadcastListingButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Broadcast Listing Indicator Bg
			LUI.LuiContainer broadcastListingIndicatorBg = cui.v2.CreatePanel(broadcastListingButton, new LuiOffset(7, 8, 27, 28), ColDb.BlackTrans20);
			broadcastListingIndicatorBg.SetMaterial("assets/content/ui/namefontmaterial.mat");

			string color = sc.stockCache.broadcastNewListing ? ColDb.GreenBg : ColDb.Transparent;
			//Element: Broadcast Listing Indicator
			cui.v2.CreateSprite(broadcastListingIndicatorBg, new LuiOffset(4, 4, 16, 16), "assets/icons/close.png", color, "ShoppyStockUI_ListingBroadcastIndicator");

			//Element: Broadcast Listing Button Text
			cui.v2.CreateText(broadcastListingButton, new LuiOffset(35, 0, 200, 36), 15, ColDb.LTD80, Lang("BroadcastListing", player.UserIDString, tax), TextAnchor.MiddleLeft);
			
			Pool.FreeUnmanaged(ref sb);
			cui.v2.SendUi(player);
		}

		private void UpdateSellOfferUI(BasePlayer player, ItemContainer cont)
		{
			ShoppyCache sc = cache[player.userID];
			if (sc.stockCache.containerType != ContainerType.SellOffer) return;
			Item item = cont.itemList.Count > 0 ? cont.itemList[0] : null;
			if (item == null)
				CuiHelper.DestroyUi(player, "ShoppyStockUI_Inventory_Connector");
			else
				RedrawStockSubmitInventory(player, false);
		}

		private void UpdateSellInventoryUI(BasePlayer player, ItemContainer cont)
		{
			ShoppyCache sc = cache[player.userID];
			if (sc.stockCache.containerType != ContainerType.SellInventory) return;
			RedrawSellInventoryItems(player);
		}

		private void UpdateNewListingUI(BasePlayer player)
		{
			ShoppyCache sc = cache[player.userID];
			Item targetItem = null;
			if (sc.stockCache.listingPage == ListingPageType.SellOffer)
			{
				targetItem = sc.stockCache.container.inventory.itemList[0];
				if (targetItem == null) return;
			}
			StringBuilder sb = Pool.Get<StringBuilder>();
			CurrencyConfig currCfg = config.curr[sc.shopName].currCfg;
			StockConfig stockCfg = config.curr[sc.shopName].stockCfg;
			using CUI cui = new CUI(CuiHandler);
			List<TimeListingConfig> validConfigs = Pool.Get<List<TimeListingConfig>>();
			validConfigs.Clear();
			TimeListingConfig tlc = null;
			bool isBuyOffer = sc.stockCache.listingPage == ListingPageType.BuyOffer;
			if (isBuyOffer)
			{
				foreach (var offer in stockCfg.buyOfferTimes)
					if (offer.reqPerm.Length == 0 || permission.UserHasPermission(player.UserIDString, offer.reqPerm))
						validConfigs.Add(offer);
				if (sc.stockCache.buyOfferTaxIndex >= validConfigs.Count)
					sc.stockCache.buyOfferTaxIndex = 0;
				else if (sc.stockCache.buyOfferTaxIndex < 0)
					sc.stockCache.buyOfferTaxIndex = validConfigs.Count - 1;
				tlc = validConfigs[sc.stockCache.buyOfferTaxIndex];
			}
			else
			{
				foreach (var offer in stockCfg.sellOfferTimes)
					if (offer.reqPerm.Length == 0 || permission.UserHasPermission(player.UserIDString, offer.reqPerm))
						validConfigs.Add(offer);
				if (sc.stockCache.sellOfferTaxIndex >= validConfigs.Count)
					sc.stockCache.sellOfferTaxIndex = 0;
				else if (sc.stockCache.sellOfferTaxIndex < 0)
					sc.stockCache.sellOfferTaxIndex = validConfigs.Count - 1;
				tlc = validConfigs[sc.stockCache.sellOfferTaxIndex];
			}

			float tax = tlc.taxAmount;
			foreach (var perm in tlc.taxAmountPerms)
				if (perm.Value < tax && permission.UserHasPermission(player.UserIDString, perm.Key))
					tax = perm.Value;


			cui.v2.UpdateText("ShoppyStockUI_TaxAmount", Lang("TaxAmount", player.UserIDString, tax));
			cui.v2.UpdateText("ShoppyStockUI_ListingTime", FormatListingTime(player, tlc.listingTime, sb));
			int amount = isBuyOffer ? sc.stockCache.stockActionAmount : targetItem.amount;
			float price = sc.stockCache.offerPrice;
			cui.v2.UpdateText("ShoppyStockUI_ListingPriceField_1", FormatCurrency(currCfg, amount * price, sb));
			float finalPrice = isBuyOffer ? amount * price + (amount * price / 100f * tax) : (amount * price / 100f * tax);
			cui.v2.UpdateText("ShoppyStockUI_ListingPriceField_2", FormatCurrency(currCfg, finalPrice, sb));

			if (price <= 0)
			{
				cui.v2.Update("ShoppyStockUI_StockSubmitButton").SetButton("", ColDb.LTD40);
				cui.v2.Update("ShoppyStockUI_StockSubmitButton_Text").SetText(Lang("PriceRequired", player.UserIDString), color: ColDb.LTD80, update: true);
				cui.v2.Update("ShoppyStockUI_StockSubmit_Icon").SetColor(ColDb.LTD80);
			}
			else if (amount <= 0)
			{
				cui.v2.Update("ShoppyStockUI_StockSubmitButton").SetButton("", ColDb.LTD40);
				cui.v2.Update("ShoppyStockUI_StockSubmitButton_Text").SetText(Lang("AmountRequired", player.UserIDString), color: ColDb.LTD80, update: true);
				cui.v2.Update("ShoppyStockUI_StockSubmit_Icon").SetColor(ColDb.LTD80);
			}
			else
			{
				cui.v2.Update("ShoppyStockUI_StockSubmitButton").SetButton(Community.Protect("ShoppyStock_UI stock buySellOffer trySubmit"), ColDb.GreenBg);
				cui.v2.Update("ShoppyStockUI_StockSubmitButton_Text").SetText(Lang("SubmitListing", player.UserIDString), color: ColDb.GreenText, update: true);
				cui.v2.Update("ShoppyStockUI_StockSubmit_Icon").SetColor(ColDb.GreenText);
			}
			string color = sc.stockCache.broadcastNewListing ? ColDb.GreenBg : ColDb.Transparent;
			cui.v2.UpdateColor("ShoppyStockUI_ListingBroadcastIndicator", color);
			Pool.FreeUnmanaged(ref sb);
			cui.v2.SendUi(player);
		}

		private void UpdateBuySellOfferDetailsUI(BasePlayer player, int newIndex = -1)
		{
			ShoppyCache sc = cache[player.userID];
			StringBuilder sb = Pool.Get<StringBuilder>();
			StockData sd = data.stock[sc.shopName];
			CurrencyConfig currCfg = config.curr[sc.shopName].currCfg;
			StockConfig stockCfg = config.curr[sc.shopName].stockCfg;
			bool isBuyOffer = sc.stockCache.listingPage == ListingPageType.BuyOffer;
			using CUI cui = new CUI(CuiHandler); 
			
			StockListingData sld;

			ulong oldOwner = sc.stockCache.selectedListingOwner;
			if (newIndex != -1)
			{
				
				cui.v2.UpdateColor(sb.Clear().Append("ShoppyStockUI_SelectedListing_").Append(sc.stockCache.selectedListingIndex).ToString(), ColDb.Transparent);
				sc.stockCache.selectedListingIndex = newIndex;
				cui.v2.UpdateColor(sb.Clear().Append("ShoppyStockUI_SelectedListing_").Append(sc.stockCache.selectedListingIndex).ToString(), ColDb.GreenBg);
			}
			var offers = isBuyOffer ? sd.listings.buyOffers[sc.stockCache.shortname][sc.stockCache.skin] : sd.listings.sellOffers[sc.stockCache.shortname][sc.stockCache.skin];
			List<StockListingData> sldTemp = Pool.Get<List<StockListingData>>();
			sldTemp.Clear();
			if (isBuyOffer)
			{
				foreach (var listing in offers)
				{
					if (sc.stockCache.hideMyListings && listing.userId == player.userID) continue;
					if (listing.isHidden && listing.userId != player.userID && sc.stockCache.listingCode != listing.customAccessCode) continue;
					sldTemp.Add(listing);
				}
				sld = sldTemp.OrderByDescending(x => x.price).ToArray()[sc.stockCache.selectedListingIndex];
			}
			else
			{
				foreach (var listing in offers)
				{
					if (sc.stockCache.hideMyListings && listing.userId == player.userID) continue;
					if (listing.isHidden && listing.userId != player.userID && sc.stockCache.listingCode != listing.customAccessCode) continue;
					sldTemp.Add(listing);
				}
				sld = sldTemp.OrderBy(x => x.price).ToArray()[sc.stockCache.selectedListingIndex];
			}
			Pool.FreeUnmanaged(ref sldTemp);
			if (newIndex != -1)
				sc.stockCache.selectedListingOwner = sld.userId;
			if (sc.stockCache.queuedAction == QueuedStockAction.RemoveOffer && sld.userId == player.userID)
			{
				sc.stockCache.queuedAction = QueuedStockAction.None;
				if (isBuyOffer)
				{
					AddCurrency(sc.shopName, player.userID, sld.price * sld.item.amount);
					SendEffect(player, StringKeys.Effect_Info);
					ShowPopUp(player, Lang("CurrencyRefunded", player.UserIDString, FormatCurrency(currCfg, sld.price * sld.item.amount, sb)));
				}
				else
				{
					if (RedeemStorageAPI == null)
						PrintWarning("You are trying to refund an item without RedeemStorageAPI installed. Items will disappear!");
					if (stockCfg.itemsGoToRedeem)
						RedeemStorageAPI?.Call("AddItem", player.userID.Get(), stockCfg.redeemInventoryName, sld.item.ToItem(), true);
					else
						player.GiveItem(sld.item.ToItem());
					SendEffect(player, StringKeys.Effect_Info);
					ShowPopUp(player, Lang("ItemRefunded", player.UserIDString));
				}
				sc.stockCache.selectedListingIndex = 0;
				LogToConsole("RemovedOffer", player.displayName, player.userID, sld.item.amount, sld.item.id, sld.item.skin, sld.price, isBuyOffer);
				offers.Remove(sld);
				Pool.FreeUnmanaged(ref sb);
				SwitchStockDetailsCategory(player, sc.stockCache.listingPage);
				return;
			}
			if (sc.stockCache.queuedAction == QueuedStockAction.OfferVisibilityToggle && sld.userId == player.userID)
			{
				sc.stockCache.queuedAction = QueuedStockAction.None;
				sld.isHidden = !sld.isHidden;
				string imageName = sb.Clear().Append("ShoppyStockUI_HiddenIndicator_").Append(sc.stockCache.selectedListingIndex).ToString();
				if (sld.isHidden)
				{
					cui.v2.UpdateColor(imageName, ColDb.RedBg);
					do
					{
						sld.customAccessCode = Core.Random.Range(100000, 999999);
					} while (cachedShareCodes.Contains(sld.customAccessCode));
					cachedShareCodes.Add(sld.customAccessCode);
				}
				else
				{
					cui.v2.UpdateColor(imageName, ColDb.Transparent);
					cachedShareCodes.Remove(sld.customAccessCode);
					sld.customAccessCode = 0;
				}
				string text = sld.isHidden ? Lang("ListingHidden", player.UserIDString, sld.customAccessCode) : Lang("HideListing", player.UserIDString);
				if (config.cmd.quickAccess.Count > 0 && sld.isHidden)
				{
					SendEffect(player, StringKeys.Effect_Info);
					ShowPopUp(player, Lang("ListingHiddenPopUp", player.UserIDString, config.cmd.quickAccess[0], sld.customAccessCode));
				}
				cui.v2.UpdateText("ShoppyStockUI_HideShowListing", text);
				Pool.FreeUnmanaged(ref sb);
				cui.v2.SendUi(player);
				//UpdateBuySellOfferDetailsUI(player);
				return;
			}
			int targetItemCount = 0;
			if (isBuyOffer)
			{
				foreach (var item in player.inventory.containerMain.itemList)
					if (item.info.itemid == sld.item.id && item.skin == sld.item.skin)
						targetItemCount += item.amount;
				foreach (var item in player.inventory.containerBelt.itemList)
					if (item.info.itemid == sld.item.id && item.skin == sld.item.skin)
						targetItemCount += item.amount;
			}

			if (sc.stockCache.stockActionAmount > sld.item.amount)
			{
				sc.stockCache.stockActionAmount = sld.item.amount;
				SendEffect(player, StringKeys.Effect_Info);
				ShowPopUp(player, Lang("AmountFixedToLimit", player.UserIDString));
			}

			if (isBuyOffer && sc.stockCache.stockActionAmount > targetItemCount)
			{
				sc.stockCache.stockActionAmount = targetItemCount;
				SendEffect(player, StringKeys.Effect_Info);
				ShowPopUp(player, Lang("AmountFixedToLimit", player.UserIDString));
			}
			cui.v2.Update("ShoppyStockUI_ListingImage").SetItemIcon(sld.item.id, sld.item.skin);
			string name = string.IsNullOrEmpty(sld.item.name) ? GetStockItemName(stockCfg, sd, player.UserIDString, sc.stockCache.shortname, sc.stockCache.skin, sb) : sld.item.name;
			cui.v2.UpdateText("ShoppyStockUI_ListingName", name.ToUpper());
			cui.v2.UpdateText("ShoppyStockUI_ListingOwner", sld.userName);
			cui.v2.UpdateText("ShoppyStockUI_ListingPrice", Lang("PriceForEach", player.UserIDString, FormatCurrency(currCfg, sld.price, sb)));
			cui.v2.UpdateText("ShoppyStockUI_ListingAmount", sld.item.amount.ToString("N0"));
			string details = GetItemDetails(player, sc.shopName, sld.item);
			cui.v2.UpdateText("ShoppyStockUI_ListingDetails", details);
			if ((sc.stockCache.selectedListingOwner == player.userID && oldOwner != player.userID) || (sc.stockCache.selectedListingOwner != player.userID && oldOwner == player.userID))
			{
				UpdateBuySellOfferOwnedSectionUI(player, cui, sld);
			}
			else if (sc.stockCache.selectedListingOwner != player.userID && oldOwner != player.userID)
			{
				cui.v2.Update("ShoppyStockUI_ListingAmountInput").SetInput(text: sc.stockCache.stockActionAmount.ToString(), update: true);
				float sumPrice = sc.stockCache.stockActionAmount * sld.price;
				if (sc.stockCache.stockActionAmount <= 0)
				{
					cui.v2.Update("ShoppyStockUI_ListingPurchaseButton").SetButton("", ColDb.LTD20);
					cui.v2.Update("ShoppyStockUI_ListingPurchaseButton_Text").SetText(Lang("WrongAmount", player.UserIDString), color: ColDb.LTD60, update: true);
				}
				else if (!isBuyOffer && sumPrice > GetPlayerCurrency(player, sc.shopName))
				{
					cui.v2.Update("ShoppyStockUI_ListingPurchaseButton").SetButton("", ColDb.LTD20);
					cui.v2.Update("ShoppyStockUI_ListingPurchaseButton_Text").SetText(Lang("TooExpensive", player.UserIDString), color: ColDb.LTD60, update: true);
				}
				else
				{
					cui.v2.Update("ShoppyStockUI_ListingPurchaseButton").SetButton(Community.Protect("ShoppyStock_UI stock buySellOffer purchase"), ColDb.GreenBg);
					string text = isBuyOffer ? Lang("SellItemFor", player.UserIDString, FormatAmount(currCfg, sc.stockCache.stockActionAmount, sb), FormatCurrency(currCfg, sld.price * sc.stockCache.stockActionAmount, sb)) : Lang("PurchaseItemFor", player.UserIDString, FormatAmount(currCfg, sc.stockCache.stockActionAmount, sb), FormatCurrency(currCfg, sld.price * sc.stockCache.stockActionAmount, sb));
					cui.v2.Update("ShoppyStockUI_ListingPurchaseButton_Text").SetText(text, color: ColDb.GreenText, update: true);
				}
			}
			else if (sc.stockCache.selectedListingOwner == player.userID && oldOwner == player.userID)
			{
				if (sld.listingEndTime != DateTime.MinValue && sld.listingEndTime != DateTime.MaxValue)
				{
					float time = (float)(sld.listingEndTime - DateTime.Now).TotalSeconds;
					//Element: Remaining Time
					LUI.LuiContainer remainingTime = cui.v2.CreateCountdown("ShoppyStockUI_SwitchableElement", new LuiOffset(16, 98, 284, 127), 20, ColDb.LightGray, "%TIME_LEFT%", TextAnchor.UpperCenter, 0, 0, name: "ShoppyStockUI_RemainingTime");
					remainingTime.SetCountdown(time, 0, numberFormat: "d'd 'h'h 'm'm 's's'");
					remainingTime.SetCountdownTimerFormat(TimerFormat.Custom);
					remainingTime.SetCountdownDestroy(false);
				}
				else if (sld.listingEndTime == DateTime.MaxValue)
					cui.v2.CreateText("ShoppyStockUI_SwitchableElement", new LuiOffset(16, 98, 284, 127), 20, ColDb.LightGray, Lang("UntilWipe", player.UserIDString), TextAnchor.UpperCenter, "ShoppyStockUI_RemainingTime");
				else if (sld.listingEndTime == DateTime.MinValue)
					cui.v2.CreateText("ShoppyStockUI_SwitchableElement", new LuiOffset(16, 98, 284, 127), 20, ColDb.LightGray, Lang("PermanentTime", player.UserIDString), TextAnchor.UpperCenter, "ShoppyStockUI_RemainingTime");
				cui.Destroy("ShoppyStockUI_RemainingTime", player);
			}
			Pool.FreeUnmanaged(ref sb);
			cui.v2.SendUi(player);
		}

		private void OpenBankInventory(BasePlayer player)
		{
			ShoppyCache sc = cache[player.userID];
			StringBuilder sb = Pool.Get<StringBuilder>();
			StockConfig stockCfg = config.curr[sc.shopName].stockCfg;
			StockData sd = data.stock[sc.shopName];
			
			using CUI cui = new CUI(CuiHandler);
			LUI.LuiContainer shoppyUi = cui.v2.CreateParent(CUI.ClientPanels.Inventory, LuiPosition.LowerCenter, "ShoppyStockUI_Inventory").SetDestroy("ShoppyStockUI_Inventory");
			
			//Element: Main Panel
			LUI.LuiContainer mainPanel = cui.v2.CreatePanel(shoppyUi, new LuiOffset(192, 322, 572, 522), ColDb.LightGrayTransRust, "ShoppyStockUI_BankMainPanel");
			mainPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Info Message
			LUI.LuiContainer infoMessage = cui.v2.CreateText(mainPanel, new LuiOffset(16, 156, 248, 184), 11, ColDb.LTD80, Lang("BankInventoryInfo", player.UserIDString), TextAnchor.MiddleLeft);
			infoMessage.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

			//Element: Deposit Button
			LUI.LuiContainer depositButton = cui.v2.CreateButton(mainPanel, new LuiOffset(264, 156, 364, 184), "ShoppyStock_UI bank depositAll", ColDb.GreenBg);
			depositButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Deposit Button Text
			cui.v2.CreateText(depositButton, LuiPosition.Full, LuiOffset.None, 15, ColDb.GreenText, Lang("DepositAll", player.UserIDString), TextAnchor.MiddleCenter);

			DrawBankItems(player, cui, sb, stockCfg, sd, true);
			
			//Element: Hint Icon
			LUI.LuiContainer hintIcon = cui.v2.CreateSprite(shoppyUi, new LuiOffset(550, 298, 566, 314), "assets/icons/info.png", ColDb.LTD60);

			//Element: Hint Text
			LUI.LuiContainer hintText = cui.v2.CreateText(hintIcon, new LuiOffset(-298, -5, -4, 21), 10, ColDb.LTD60, Lang("WithdrawHint", player.UserIDString), TextAnchor.MiddleRight);
			hintText.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

			//Element: Sell Order Hint
			cui.v2.CreateText(shoppyUi, new LuiOffset(202, 271, 572, 292), 12, ColDb.LTD80, Lang("WithdrawHint2", player.UserIDString), TextAnchor.MiddleLeft);

			//Element: Submit Button
			LUI.LuiContainer submitButton = cui.v2.CreateButton(shoppyUi, new LuiOffset(199, 117, 329, 173), "ShoppyStock_UI bank submit", ColDb.GreenBg);
			submitButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Submit Button Text
			cui.v2.CreateText(submitButton, new LuiOffset(49, 0, 130, 56), 18, ColDb.GreenText, Lang("SubmitItems", player.UserIDString), TextAnchor.MiddleLeft);

			//Element: Submit Button Image
			cui.v2.CreateSprite(submitButton, new LuiOffset(16, 16, 41, 41), "assets/icons/exit.png", ColDb.GreenText);
			cui.v2.SendUi(player);
			
			player.EndLooting();
			Pool.FreeUnmanaged(ref sb);
			sc.stockCache.containerType = ContainerType.BankInventory;
			SpawnAndOpenContainer(player);
		}

		private void DrawBankItems(BasePlayer player, CUI cui, StringBuilder sb, StockConfig stockCfg, StockData sd, bool initial)
		{
			ShoppyCache sc = cache[player.userID];
			
			bool isInData = data.stock[sc.shopName].playerData.players.TryGetValue(player.userID, out var playerData);
			int size = 140;
			int counter = 0;
			if (isInData)
			{
				counter = GetPlayerBankItems(player, sd);
				size = counter * 34;
				if (size < 140)
					size = 140;
			}
			int maxLimit = GetBankMaxIndividualItemCount(player, stockCfg);
			//Element: Title
			if (initial)
				cui.v2.CreateText("ShoppyStockUI_Inventory", new LuiOffset(193, 522, 572, 554), 20, ColDb.LightGray, Lang("BankInventory", player.UserIDString, counter, maxLimit == 0 ? "\u221e" : maxLimit), TextAnchor.MiddleLeft, "ShoppyStockUI_BankTitle");
			else
				cui.v2.UpdateText("ShoppyStockUI_BankTitle", Lang("BankInventory", player.UserIDString, counter, maxLimit == 0 ? "\u221e" : maxLimit));
			//Element: Resources Scroll
			LuiScrollbar mainPanel_Vertical = new LuiScrollbar()
			{
				autoHide = true,
				size = 4,
				handleColor = ColDb.LTD15,
				highlightColor = ColDb.LTD20,
				pressedColor = ColDb.LTD10,
				trackColor = ColDb.BlackTrans10
			};
			LUI.LuiContainer resourcesScroll = cui.v2.CreateScrollView("ShoppyStockUI_BankMainPanel", new LuiOffset(8, 8, 374, 148), true, false, ScrollRect.MovementType.Elastic, 0.1f, true, 0.1f, 25f, mainPanel_Vertical, default, "ShoppyStockUI_BankScroll").SetDestroy("ShoppyStockUI_BankScroll");
			resourcesScroll.SetScrollContent(new LuiPosition(0, 1, 1, 1), new LuiOffset(0, -size, 0, 0));
			
			//Element: Scrollable Holder
			cui.v2.CreatePanel(resourcesScroll, LuiPosition.Full, LuiOffset.None, ColDb.Transparent);
			
			size -= 30;
			if (isInData && counter > 0)
			{
				foreach (var shortname in playerData.bankItems)
					foreach (var skin in shortname.Value)
					{
						string command = sb.Clear().Append("ShoppyStock_UI bank item ").Append(shortname.Key).Append(' ').Append(skin.Key).ToString();
						//Element: Resource 1
						LUI.LuiContainer resource1 = cui.v2.CreateButton(resourcesScroll, new LuiOffset(8, size, 356, size + 30), command, ColDb.LightGrayTransRust);
						resource1.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

						//Element: Resource Icon
						LUI.LuiContainer resourceIcon = cui.v2.CreateItemIcon(resource1, new LuiOffset(8, 3, 32, 27), iconRedirect[shortname.Key], skin.Key, ColDb.WhiteTrans80);
						resourceIcon.SetMaterial("assets/content/ui/namefontmaterial.mat");

						//string name = GetStockItemName(stockCfg, sd, player.UserIDString, shortname.Key, skin.Key, sb);
						string limit = "\u221e"; //Infinity Symbol
						int itemLimit = GetBankMaxItemCount(player, stockCfg, shortname.Key, skin.Key);

						string numberFormat = FormatNumber(skin.Value.amount, sb);
						ItemDefinition def = ItemManager.FindItemDefinition(shortname.Key);
						if (itemLimit > 0)
						{
							if (!def) continue;
							limit = FormatNumber(def.stackable * itemLimit, sb);
						}
						limit = sb.Clear().Append(numberFormat).Append('/').Append(limit).ToString();

						string displayName = skin.Value.displayName;
						if (string.IsNullOrEmpty(displayName))
							displayName = def.displayName.english;
									
						//Element: Resource Name
						LUI.LuiContainer resourceName = cui.v2.CreateText(resource1, new LuiOffset(37, 0, 277, 30), 12, ColDb.LightGray, displayName, TextAnchor.MiddleLeft);
						resourceName.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

						//Element: Resource Amount
						LUI.LuiContainer resourceAmount = cui.v2.CreateText(resource1, new LuiOffset(212, 0, 342, 30), 15, ColDb.LTD80, limit, TextAnchor.MiddleRight);
						resourceAmount.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

						size -= 34;
					}
			}
			else
			{
				//Element: No Items Text
				cui.v2.CreateText(resourcesScroll, LuiPosition.Full, new LuiOffset(0, 0, 0, 0), 18, ColDb.LightGray, Lang("NoItemsInBank", player.UserIDString), TextAnchor.MiddleCenter);
			}
		}

		private void UpdateWithdrawPanelUI(BasePlayer player)
		{
			ShoppyCache sc = cache[player.userID];
			using CUI cui = new CUI(CuiHandler);
			//Element: Withdraw Panel
			LUI.LuiContainer withdrawPanel = cui.v2.CreatePanel("ShoppyStockUI_Inventory", new LuiOffset(432, 526, 572, 626), ColDb.LightGrayTransRust, "ShoppyStockUI_WithdrawPanel").SetDestroy("ShoppyStockUI_WithdrawPanel");
			withdrawPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Withdraw Image
			LUI.LuiContainer withdrawImage = cui.v2.CreateItemIcon(withdrawPanel, new LuiOffset(8, 64, 38, 94), iconRedirect[sc.stockCache.bankShortname], sc.stockCache.bankSkin, ColDb.White);
			withdrawImage.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Withdraw Title
			cui.v2.CreateText(withdrawPanel, new LuiOffset(42, 64, 132, 94), 12, ColDb.LightGray, Lang("WithdrawAmount", player.UserIDString), TextAnchor.MiddleCenter);

			//Element: Withdraw Input Background
			LUI.LuiContainer withdrawInputBackground = cui.v2.CreatePanel(withdrawPanel, new LuiOffset(8, 35, 132, 61), ColDb.LightGrayTransRust);
			withdrawInputBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Withdraw Input
			cui.v2.CreateInput(withdrawInputBackground, LuiPosition.Full, LuiOffset.None, ColDb.LightGray, "0", 15, "ShoppyStock_UI bank withdrawAmount", 0, true, CUI.Handler.FontTypes.RobotoCondensedRegular, TextAnchor.MiddleCenter).SetInputKeyboard(hudMenuInput: true);

			//Element: Withdraw Button
			LUI.LuiContainer withdrawButton = cui.v2.CreateButton(withdrawPanel, new LuiOffset(24, 6, 116, 30), "ShoppyStock_UI bank withdraw", ColDb.GreenBg);
			withdrawButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Withdraw Button Text
			cui.v2.CreateText(withdrawButton, LuiPosition.Full, LuiOffset.None, 15, ColDb.GreenText, Lang("Withdraw", player.UserIDString), TextAnchor.MiddleCenter);

			cui.v2.SendUi(player);
		}

		private void OpenSellUI(BasePlayer player)
		{
			ShoppyCache sc = cache[player.userID];
			using CUI cui = new CUI(CuiHandler);
			LUI.LuiContainer shoppyUi = cui.v2.CreateParent(CUI.ClientPanels.Inventory, LuiPosition.LowerCenter, "ShoppyStockUI_Inventory").SetDestroy("ShoppyStockUI_Inventory");
			//Element: Title
			cui.v2.CreateText(shoppyUi, new LuiOffset(193, 618, 572, 650), 20, ColDb.LightGray, Lang("SellInventory", player.UserIDString), TextAnchor.MiddleLeft);

			//Element: Main Panel
			LUI.LuiContainer mainPanel = cui.v2.CreatePanel(shoppyUi, new LuiOffset(192, 418, 572, 618), ColDb.LightGrayTransRust, "ShoppyStockUI_SellPanel");
			mainPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Sell Icon
			cui.v2.CreateSprite(mainPanel, new LuiOffset(24, 159, 47, 182), "assets/icons/cart.png", ColDb.LightGray);

			//Element: Total Price
			
			StringBuilder sb = Pool.Get<StringBuilder>();
			string currency = FormatCurrency(config.curr[sc.shopName].currCfg, 0, sb);
			Pool.FreeUnmanaged(ref sb);
			cui.v2.CreateText(mainPanel, new LuiOffset(53, 154, 256, 186), 15, ColDb.LightGray, Lang("TotalSellPrice", player.UserIDString, currency), TextAnchor.MiddleLeft, "ShoppyStockUI_TotalPrice");

			//Element: Sell Button
			LUI.LuiContainer sellButton = cui.v2.CreateButton(mainPanel, new LuiOffset(264, 156, 364, 184), "ShoppyStock_UI sellStock", ColDb.GreenBg);
			sellButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Sell Button Text
			cui.v2.CreateText(sellButton, LuiPosition.Full, LuiOffset.None, 15, ColDb.GreenText, Lang("Sell", player.UserIDString), TextAnchor.MiddleCenter);
			
			//Element: Hint Icon
			LUI.LuiContainer hintIcon = cui.v2.CreateSprite(shoppyUi, new LuiOffset(550, 392, 566, 408), "assets/icons/info.png", ColDb.LTD60);

			//Element: Hint Text
			LUI.LuiContainer hintText = cui.v2.CreateText(hintIcon, new LuiOffset(-298, -5, -4, 21), 10, ColDb.LTD60, Lang("ServerSellHint", player.UserIDString), TextAnchor.MiddleRight);
			hintText.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);
			
			cui.v2.SendUi(player);
			RedrawSellInventoryItems(player);
			
			player.EndLooting();
			sc.stockCache.containerType = ContainerType.SellInventory;
			SpawnAndOpenContainer(player);
		}

		private void RedrawSellInventoryItems(BasePlayer player)
		{
			ShoppyCache sc = cache[player.userID];
			using CUI cui = new CUI(CuiHandler);
			StockData sd = data.stock[sc.shopName];
			StringBuilder sb = Pool.Get<StringBuilder>();
			CurrencyConfig currCfg = config.curr[sc.shopName].currCfg;
			StockConfig stockCfg = config.curr[sc.shopName].stockCfg;
			
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

			int itemCount = sc.stockCache.container ? sc.stockCache.container.inventory.itemList.Count : 0;
			int scrollHeight = 34 * itemCount - 4;
			if (scrollHeight < 140)
				scrollHeight = 140;

			LUI.LuiContainer itemsScroll = cui.v2.CreateScrollView("ShoppyStockUI_SellPanel", new LuiOffset(8, 8, 374, 148), true, false, ScrollRect.MovementType.Elastic, 0.1f, true, 0.1f, 25f, mainPanel_Vertical, default, "ShoppyStockUI_SellPanelScroll").SetDestroy("ShoppyStockUI_SellPanelScroll");
			itemsScroll.SetScrollContent(new LuiPosition(0, 1, 1, 1), new LuiOffset(0, -scrollHeight, 0, 0));
			
			//Element: Scrollable Holder
			LUI.LuiContainer scrollableHolder = cui.v2.CreatePanel(itemsScroll, LuiPosition.Full, LuiOffset.None, ColDb.Transparent);

			float salePriceMult = 1;

			foreach (var perm in stockCfg.sellCfg.sellPriceMultipliers)
				if (perm.Value > salePriceMult && permission.UserHasPermission(player.UserIDString, perm.Key))
					salePriceMult = perm.Value;
			if (BonusCore != null)
				salePriceMult += BonusCore.Call<float>("GetBonus", player, "Other_Price");
			
			scrollHeight -= 30;
			string notForSale = Lang("NotForSale", player.UserIDString);
			float sumAllPrice = 0;
			if (itemCount == 0)
			{
				//Element: Put Items Hint
				cui.v2.CreateText(scrollableHolder, LuiPosition.Full, LuiOffset.None, 18, ColDb.LightGray, Lang("PutItemsHint", player.UserIDString), TextAnchor.MiddleCenter);
			}
			else
			{
				foreach (var item in sc.stockCache.container.inventory.itemList)
				{
					//Element: Sell Item Panel
					LUI.LuiContainer sellItemPanel = cui.v2.CreatePanel(itemsScroll, new LuiOffset(8, scrollHeight, 356, scrollHeight + 30), ColDb.LightGrayTransRust);
					sellItemPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

					//Element: Item Icon
					LUI.LuiContainer itemIcon = cui.v2.CreateItemIcon(sellItemPanel, new LuiOffset(8, 3, 32, 27), iconRedirect[item.info.shortname], item.skin, ColDb.WhiteTrans80);
					itemIcon.SetMaterial("assets/content/ui/namefontmaterial.mat");

					bool sellable = sd.prices.items.TryGetValue(item.info.shortname, out var skins) && skins.ContainsKey(item.skin);

					string itemName = sellable ? GetStockItemName(stockCfg, sd, player.UserIDString, item.info.shortname, item.skin, sb) : item.info.displayName.english;
					string itemAmount = FormatAmount(currCfg, item.amount, sb);
					itemName = sb.Clear().Append(itemName).Append(" [").Append(itemAmount).Append(']').ToString();
					//Element: Item Name Amount
					LUI.LuiContainer itemNameAmount = cui.v2.CreateText(sellItemPanel, new LuiOffset(37, 0, 213, 30), 12, ColDb.LightGray, itemName, TextAnchor.MiddleLeft);
					itemNameAmount.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

					string price;
					if (!sellable)
						price = notForSale;
					else
					{
						SellItemData sid = sd.prices.items[item.info.shortname][item.skin];
						float sumPrice = sid.price * item.amount;
						sumAllPrice += sumPrice;
						if (salePriceMult == 1 || (stockCfg.sellCfg.sellPriceMultBlacklist.TryGetValue(item.info.shortname, out var skins2) && skins2.Contains(item.skin)))
							price = FormatCurrency(currCfg, sumPrice, sb);
						else
						{
							float bonus = sumPrice * salePriceMult - sumPrice;
							sumAllPrice += bonus;
							string sumPriceString = FormatCurrency(currCfg, sumPrice, sb);
							price = string.Format(stockCfg.sellCfg.sellPriceFormat, sumPriceString, FormatCurrency(currCfg, bonus, sb));
						}
					}
					//Element: Item Price
					LUI.LuiContainer itemPrice = cui.v2.CreateText(sellItemPanel, new LuiOffset(174, 0, 342, 30), 14, ColDb.LTD80, price, TextAnchor.MiddleRight);
					itemPrice.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);
					scrollHeight -= 34;
				}
			}
			string currency = FormatCurrency(currCfg, sumAllPrice, sb);
			cui.v2.UpdateText("ShoppyStockUI_TotalPrice", Lang("TotalSellPrice", player.UserIDString, currency));
			Pool.FreeUnmanaged(ref sb);
			cui.v2.SendUi(player);
		}

		private void OpenStockActionsUI(BasePlayer player)
		{
			using CUI cui = new CUI(CuiHandler);
			ShoppyCache sc = cache[player.userID];
			StringBuilder sb = Pool.Get<StringBuilder>();
			StockConfig stockCfg = config.curr[sc.shopName].stockCfg;
			sc.stockCache.isWatchingHistory = true;
			
			//Element: Action History Menu
			LUI.LuiContainer actionHistoryMenu = cui.v2.CreateEmptyContainer("ShoppyStockUI_ElementParent", "ShoppyStockUI_ElementCore", true).SetOffset(new LuiOffset(0, 0, 1100, 528)).SetDestroy("ShoppyStockUI_ElementCore");

			//Element: Go Back Button
			LUI.LuiContainer goBackButton = cui.v2.CreateButton(actionHistoryMenu, new LuiOffset(32, 480, 180, 512), "ShoppyStock_UI stock return", ColDb.RedBg);
			goBackButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Go Back Button Text
			cui.v2.CreateText(goBackButton, LuiPosition.Full, LuiOffset.None, 14, ColDb.RedText, Lang("GoBackToMarket", player.UserIDString), TextAnchor.MiddleCenter);

			//Element: Actions Title
			string title = Lang(sb.Clear().Append("StockActionHistory_").Append(sc.shopName).ToString(), player.UserIDString);
			cui.v2.CreateText(actionHistoryMenu, new LuiOffset(192, 478, 732, 512), 28, ColDb.LightGray, title, TextAnchor.UpperLeft);

			//Element: Sort Title
			cui.v2.CreateText(actionHistoryMenu, new LuiOffset(32, 453, 231, 482), 18, ColDb.LTD60, Lang("SortActions", player.UserIDString), TextAnchor.LowerLeft);

			int startX = 32;
			MarketLogConfig mlc = stockCfg.marketLog;

			if (mlc.tabSellOffers)
			{
				string color = sc.stockCache.disabledStockMessages.Contains(StockActionType.SellOffer) ? ColDb.BlackTrans20 : ColDb.LTD15;
				string command = sb.Clear().Append("ShoppyStock_UI stockSort toggle ").Append(StockActionType.SellOffer).ToString();
				string name = sb.Clear().Append("ShoppyStockUI_StockSort_").Append(StockActionType.SellOffer).ToString();
				//Element: Sort Button
				LUI.LuiContainer sortButton = cui.v2.CreateButton(actionHistoryMenu, new LuiOffset(startX, 420, startX + 148, 450), command, color, name: name);
				sortButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Sort Button Text
				cui.v2.CreateText(sortButton, LuiPosition.Full, LuiOffset.None, 14, ColDb.LightGray, Lang("NewSellOffers", player.UserIDString), TextAnchor.MiddleCenter);
				startX += 148;
			}
			if (mlc.tabBuyOffers)
			{
				string color = sc.stockCache.disabledStockMessages.Contains(StockActionType.BuyOffer) ? ColDb.BlackTrans20 : ColDb.LTD15;
				string command = sb.Clear().Append("ShoppyStock_UI stockSort toggle ").Append(StockActionType.BuyOffer).ToString();
				string name = sb.Clear().Append("ShoppyStockUI_StockSort_").Append(StockActionType.BuyOffer).ToString();
				//Element: Sort Button
				LUI.LuiContainer sortButton = cui.v2.CreateButton(actionHistoryMenu, new LuiOffset(startX, 420, startX + 148, 450), command, color, name: name);
				sortButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Sort Button Text
				cui.v2.CreateText(sortButton, LuiPosition.Full, LuiOffset.None, 14, ColDb.LightGray, Lang("NewBuyOffers", player.UserIDString), TextAnchor.MiddleCenter);
				startX += 148;
			}
			if (mlc.tabSoldItems)
			{
				string color = sc.stockCache.disabledStockMessages.Contains(StockActionType.SoldItem) ? ColDb.BlackTrans20 : ColDb.LTD15;
				string command = sb.Clear().Append("ShoppyStock_UI stockSort toggle ").Append(StockActionType.SoldItem).ToString();
				string name = sb.Clear().Append("ShoppyStockUI_StockSort_").Append(StockActionType.SoldItem).ToString();
				//Element: Sort Button
				LUI.LuiContainer sortButton = cui.v2.CreateButton(actionHistoryMenu, new LuiOffset(startX, 420, startX + 148, 450), command, color, name: name);
				sortButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Sort Button Text
				cui.v2.CreateText(sortButton, LuiPosition.Full, LuiOffset.None, 14, ColDb.LightGray, Lang("SoldItems", player.UserIDString), TextAnchor.MiddleCenter);
				startX += 148;
			}
			if (mlc.tabPurchasedItems)
			{
				string color = sc.stockCache.disabledStockMessages.Contains(StockActionType.PurchasedItem) ? ColDb.BlackTrans20 : ColDb.LTD15;
				string command = sb.Clear().Append("ShoppyStock_UI stockSort toggle ").Append(StockActionType.PurchasedItem).ToString();
				string name = sb.Clear().Append("ShoppyStockUI_StockSort_").Append(StockActionType.PurchasedItem).ToString();
				//Element: Sort Button
				LUI.LuiContainer sortButton = cui.v2.CreateButton(actionHistoryMenu, new LuiOffset(startX, 420, startX + 148, 450), command, color, name: name);
				sortButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Sort Button Text
				cui.v2.CreateText(sortButton, LuiPosition.Full, LuiOffset.None, 14, ColDb.LightGray, Lang("PurchasedItems", player.UserIDString), TextAnchor.MiddleCenter);
				startX += 148;
			}
			if (mlc.tabPriceRolls)
			{
				string color = sc.stockCache.disabledStockMessages.Contains(StockActionType.PriceRoll) ? ColDb.BlackTrans20 : ColDb.LTD15;
				string command = sb.Clear().Append("ShoppyStock_UI stockSort toggle ").Append(StockActionType.PriceRoll).ToString();
				string name = sb.Clear().Append("ShoppyStockUI_StockSort_").Append(StockActionType.PriceRoll).ToString();
				//Element: Sort Button
				LUI.LuiContainer sortButton = cui.v2.CreateButton(actionHistoryMenu, new LuiOffset(startX, 420, startX + 148, 450), command, color, name: name);
				sortButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Sort Button Text
				cui.v2.CreateText(sortButton, LuiPosition.Full, LuiOffset.None, 14, ColDb.LightGray, Lang("PriceRolls", player.UserIDString), TextAnchor.MiddleCenter);
				startX += 148;
			}
			if (mlc.tabDemands)
			{
				string color = sc.stockCache.disabledStockMessages.Contains(StockActionType.Demand) ? ColDb.BlackTrans20 : ColDb.LTD15;
				string command = sb.Clear().Append("ShoppyStock_UI stockSort toggle ").Append(StockActionType.Demand).ToString();
				string name = sb.Clear().Append("ShoppyStockUI_StockSort_").Append(StockActionType.Demand).ToString();
				//Element: Sort Button
				LUI.LuiContainer sortButton = cui.v2.CreateButton(actionHistoryMenu, new LuiOffset(startX, 420, startX + 148, 450), command, color, name: name);
				sortButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Sort Button Text
				cui.v2.CreateText(sortButton, LuiPosition.Full, LuiOffset.None, 14, ColDb.LightGray, Lang("Demands", player.UserIDString), TextAnchor.MiddleCenter);
				startX += 148;
			}
			if (mlc.tabAlerts)
			{
				string color = sc.stockCache.disabledStockMessages.Contains(StockActionType.Alert) ? ColDb.BlackTrans20 : ColDb.LTD15;
				string command = sb.Clear().Append("ShoppyStock_UI stockSort toggle ").Append(StockActionType.Alert).ToString();
				string name = sb.Clear().Append("ShoppyStockUI_StockSort_").Append(StockActionType.Alert).ToString();
				//Element: Sort Button
				LUI.LuiContainer sortButton = cui.v2.CreateButton(actionHistoryMenu, new LuiOffset(startX, 420, startX + 148, 450), command, color, name: name);
				sortButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Sort Button Text
				cui.v2.CreateText(sortButton, LuiPosition.Full, LuiOffset.None, 14, ColDb.LightGray, Lang("Alerts", player.UserIDString), TextAnchor.MiddleCenter);
			}
			//Element: Action History Title
			cui.v2.CreateText(actionHistoryMenu, new LuiOffset(32, 391, 432, 420), 18, ColDb.LTD60, Lang("ActionHistory", player.UserIDString), TextAnchor.LowerLeft);

			//Element: Actions Background
			LUI.LuiContainer actionsBackground = cui.v2.CreatePanel(actionHistoryMenu, new LuiOffset(32, 32, 1068, 388), ColDb.BlackTrans20, "ShoppyStockUI_ActionsBackground");
			actionsBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");
			Pool.FreeUnmanaged(ref sb);
			RedrawActionsUI(player, cui);
			cui.v2.SendUi(player);
		}

		private void RedrawActionsUI(BasePlayer player, CUI cui)
		{
			ShoppyCache sc = cache[player.userID];
			StringBuilder sb = Pool.Get<StringBuilder>();

			//Element: Actions Scroll
			LuiScrollbar actionsBackground_Vertical = new LuiScrollbar()
			{
				autoHide = true,
				size = 4,
				handleColor = ColDb.LTD15,
				highlightColor = ColDb.LTD20,
				pressedColor = ColDb.LTD10,
				trackColor = ColDb.BlackTrans10
			};

			MarketLogConfig mlc = config.curr[sc.shopName].stockCfg.marketLog;

			Dictionary<int, ActionData> cachedActions = Pool.Get<Dictionary<int, ActionData>>();
			cachedActions.Clear();
			foreach (var ac in data.stock[sc.shopName].stats.actions.Reverse())
			{
				ActionData action = ac.Value;
				if (sc.stockCache.disabledStockMessages.Contains(action.type)) continue;
				if (!mlc.tabSellOffers && action.type == StockActionType.SellOffer) continue;
				if (!mlc.tabBuyOffers && action.type == StockActionType.BuyOffer) continue;
				if (!mlc.tabSoldItems && action.type == StockActionType.SoldItem) continue;
				if (!mlc.tabPurchasedItems && action.type == StockActionType.PurchasedItem) continue;
				if (!mlc.tabPriceRolls && action.type == StockActionType.PriceRoll) continue;
				if (!mlc.tabDemands && (action.type == StockActionType.Demand || action.type == StockActionType.Demand_Neg)) continue;
				if (!mlc.tabAlerts && action.type == StockActionType.Alert) continue;
				if (action.type == StockActionType.Demand_Neg && sc.stockCache.disabledStockMessages.Contains(StockActionType.Demand)) continue;
				if (action.type == StockActionType.AutoSell && sc.stockCache.disabledStockMessages.Contains(StockActionType.Alert)) continue;
				if (action.type == StockActionType.AutoSell || action.type == StockActionType.Alert && action.field1 != player.UserIDString) continue;
				cachedActions.Add(ac.Key, action);
				if (cachedActions.Count > 50) break;
			}
			int scrollHeight = 26 * cachedActions.Count;
			if (scrollHeight < 356)
				scrollHeight = 356;

			LUI.LuiContainer actionsScroll = cui.v2.CreateScrollView("ShoppyStockUI_ActionsBackground", new LuiOffset(0, 0, 1022, 356), true, false, ScrollRect.MovementType.Elastic, 0.1f, true, 0.1f, 25f, actionsBackground_Vertical, default, "ShoppyStockUI_ActionsScroll").SetDestroy("ShoppyStockUI_ActionsScroll");
			actionsScroll.SetScrollContent(new LuiPosition(0, 1, 1, 1), new LuiOffset(0, -scrollHeight, 0, 0));
			
			//Element: Scrollable Holder
			cui.v2.CreatePanel(actionsScroll, LuiPosition.Full, LuiOffset.None, ColDb.Transparent);

			string hiddenString = Lang("Hidden", player.UserIDString);
			foreach (var action in cachedActions)
			{
				ActionData ad = action.Value;
				if (sc.stockCache.disabledStockMessages.Contains(ad.type)) continue;
				if (!mlc.tabSellOffers && ad.type == StockActionType.SellOffer) continue;
				if (!mlc.tabBuyOffers && ad.type == StockActionType.BuyOffer) continue;
				if (!mlc.tabSoldItems && ad.type == StockActionType.SoldItem) continue;
				if (!mlc.tabPurchasedItems && ad.type == StockActionType.PurchasedItem) continue;
				if (!mlc.tabPriceRolls && ad.type == StockActionType.PriceRoll) continue;
				if (!mlc.tabDemands && (ad.type == StockActionType.Demand || ad.type == StockActionType.Demand_Neg)) continue;
				if (!mlc.tabAlerts && ad.type == StockActionType.Alert) continue;
				if (ad.type == StockActionType.Demand_Neg && sc.stockCache.disabledStockMessages.Contains(StockActionType.Demand)) continue;
				if (ad.type == StockActionType.AutoSell && sc.stockCache.disabledStockMessages.Contains(StockActionType.Alert)) continue;
				if (ad.type == StockActionType.AutoSell || ad.type == StockActionType.Alert && ad.field1 != player.UserIDString) continue;
				int id = action.Key;
				scrollHeight -= 26;
				if (scrollHeight < 0) break;
				string command = sb.Clear().Append("ShoppyStock_UI stockSort details ").Append(id).ToString();
				
				//Element: Action Button
				LUI.LuiContainer actionButton = cui.v2.CreateButton(actionsScroll, new LuiOffset(0, scrollHeight, 1010, scrollHeight + 26), command, ColDb.Transparent);
				actionButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Action Date
				cui.v2.CreateText(actionButton, new LuiOffset(0, 0, 130, 26), 12, ColDb.LTD80, ad.date, TextAnchor.MiddleCenter);

				string hiddenNickname = !mlc.showBuyerNickname && ad.field1 == "Hidden" ? hiddenString : ad.field1;
				
				string langKey = Lang(sb.Clear().Append("StockAction_").Append(ad.type).ToString(), player.UserIDString, hiddenNickname, ad.field2, ad.field3, ad.field4, ad.field5);
				//Element: Action Text
				LUI.LuiContainer actionText = cui.v2.CreateText(actionButton, new LuiOffset(130, 0, 1010, 26), 12, ColDb.LightGray, langKey, TextAnchor.MiddleLeft);
				actionText.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);
			}
			Pool.FreeUnmanaged(ref cachedActions);
			Pool.FreeUnmanaged(ref sb);
		}

		private void SwitchStockSortType(BasePlayer player, StockActionType type)
		{
			ShoppyCache sc = cache[player.userID];
			StringBuilder sb = Pool.Get<StringBuilder>();
			string color = sc.stockCache.disabledStockMessages.Contains(type) ? ColDb.BlackTrans20 : ColDb.LTD15;
			string name = sb.Clear().Append("ShoppyStockUI_StockSort_").Append(type).ToString();
			Pool.FreeUnmanaged(ref sb);
			using CUI cui = new CUI(CuiHandler);
			cui.v2.UpdateColor(name, color);
			RedrawActionsUI(player, cui);
			cui.v2.SendUi(player);
		}

		private void OpenTransferUI(BasePlayer player, bool forceUpdate = false)
		{
			using CUI cui = new CUI(CuiHandler);
			ShoppyCache sc = cache[player.userID];
			StringBuilder sb = Pool.Get<StringBuilder>();
			SwitchTypeUI(cui, sc, ShoppyType.Transfer, sb, forceUpdate);
			
			//Element: Transfer Menu
			LUI.LuiContainer transferMenu = cui.v2.CreateEmptyContainer("ShoppyStockUI_ElementParent", "ShoppyStockUI_ElementCore", true).SetOffset(new LuiOffset(0, 0, 1100, 528));

			//Element: Title
			cui.v2.CreateText(transferMenu, new LuiOffset(32, 478, 332, 512), 28, ColDb.LightGray, Lang("CurrencyTransfer", player.UserIDString), TextAnchor.UpperLeft);

			//Element: Select Player Title
			cui.v2.CreateText(transferMenu, new LuiOffset(32, 453, 231, 482), 18, ColDb.LTD60, Lang("SelectPlayer", player.UserIDString), TextAnchor.LowerLeft);

			//Element: Show Online Button
			LUI.LuiContainer showOnlineButton = cui.v2.CreateButton(transferMenu, new LuiOffset(562, 458, 742, 494), "ShoppyStock_UI transfer online", ColDb.Transparent);
			showOnlineButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Checkmark Background
			LUI.LuiContainer checkmarkBackground = cui.v2.CreatePanel(showOnlineButton, new LuiOffset(155, 8, 175, 28), ColDb.BlackTrans20);
			checkmarkBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Checkmark
			string color = sc.transferCache.showOffline ? ColDb.GreenBg : ColDb.Transparent;
			cui.v2.CreateSprite(checkmarkBackground, new LuiOffset(4, 4, 16, 16), "assets/icons/close.png", color);

			//Element: Show Online Button Text
			cui.v2.CreateText(showOnlineButton, new LuiOffset(0, 0, 147, 36), 12, ColDb.LTD80, Lang("ShowOnlyOffline", player.UserIDString), TextAnchor.MiddleRight);

			//Element: Search Background
			LUI.LuiContainer searchBackground = cui.v2.CreatePanel(transferMenu, new LuiOffset(754, 458, 1068, 494), ColDb.BlackTrans20);
			searchBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Search Icon
			cui.v2.CreateSprite(searchBackground, new LuiOffset(281, 7, 303, 29), "assets/content/ui/gameui/camera/icon-zoom.png", ColDb.LTD15);

			//Element: Search Input
			string search = sc.transferCache.search.Length > 0 ? sc.transferCache.search : Lang("TypeHere", player.UserIDString);
			cui.v2.CreateInput(searchBackground, new LuiOffset(10, 0, 281, 36), ColDb.LightGray, search, 17, "ShoppyStock_UI transfer search", 0, true, CUI.Handler.FontTypes.RobotoCondensedRegular, TextAnchor.MiddleLeft).SetInputKeyboard(hudMenuInput: true);

			//Element: Players Background
			LUI.LuiContainer playersBackground = cui.v2.CreatePanel(transferMenu, new LuiOffset(32, 32, 1068, 450), ColDb.BlackTrans20);
			playersBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

			int validPlayers = data.currencies.players.Count;
			if (sc.transferCache.search.Length > 0)
				validPlayers = data.currencies.players.Count(x => x.Value.userName.Contains(sc.transferCache.search, CompareOptions.IgnoreCase));
			if (validPlayers > config.ui.searchLimit)
				validPlayers = config.ui.searchLimit;
			int scrollHeight = Mathf.CeilToInt(validPlayers / 3f) * 42 + 24;

			if (scrollHeight < 418)
				scrollHeight = 418;

			//Element: Players Scroll
			LuiScrollbar playersBackground_Vertical = new LuiScrollbar()
			{
				autoHide = true,
				size = 4,
				handleColor = ColDb.LTD15,
				highlightColor = ColDb.LTD20,
				pressedColor = ColDb.LTD10,
				trackColor = ColDb.BlackTrans10
			};

			LUI.LuiContainer playersScroll = cui.v2.CreateScrollView(playersBackground, new LuiOffset(0, 0, 1022, 418), true, false, ScrollRect.MovementType.Elastic, 0.1f, true, 0.1f, 25f, playersBackground_Vertical, default);
			playersScroll.SetScrollContent(new LuiPosition(0, 1, 1, 1), new LuiOffset(0, -scrollHeight, 0, 0));
			
			//Element: Scrollable Holder
			cui.v2.CreatePanel(playersScroll, LuiPosition.Full, LuiOffset.None, ColDb.Transparent);

			scrollHeight -= 50;
			int startX = 16;
			List<ulong> online = Pool.Get<List<ulong>>();
			online.Clear();
			foreach (var onlinePlayer in BasePlayer.activePlayerList)
				online.Add(onlinePlayer.userID);
			int recordCount = 0;
			if (!sc.transferCache.showOffline)
			{
				foreach (var currPlayer in BasePlayer.activePlayerList)
				{
					if (currPlayer.userID == player.userID) continue;
					if (sc.transferCache.search.Length > 0 && !currPlayer.displayName.Contains(sc.transferCache.search, CompareOptions.IgnoreCase)) continue;
					recordCount++;
					if (recordCount > config.ui.searchLimit) break;
					string command = sb.Clear().Append("ShoppyStock_UI transfer select ").Append(currPlayer.UserIDString).ToString();
					//Element: Player Button
					LUI.LuiContainer playerButton = cui.v2.CreateButton(playersScroll, new LuiOffset(startX, scrollHeight, startX + 324, scrollHeight + 34), command, ColDb.LTD10);
					playerButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

					//Element: Player Button Text
					string fixedName = currPlayer.displayName.Replace("\"", "").Replace("\\", "");
					cui.v2.CreateText(playerButton, LuiPosition.Full, LuiOffset.None, 17, ColDb.GreenText, fixedName, TextAnchor.MiddleCenter);
					startX += 332;
					if (startX > 750)
					{
						startX = 16;
						scrollHeight -= 42;
					}
				}
			}
			else
			{
				foreach (var currPlayer in data.currencies.players)
				{
					if (online.Contains(currPlayer.Key)) continue;
					if (sc.transferCache.search.Length > 0 && !currPlayer.Value.userName.Contains(sc.transferCache.search, CompareOptions.IgnoreCase)) continue;
					recordCount++;
					if (recordCount > config.ui.searchLimit) break;
					string command = sb.Clear().Append("ShoppyStock_UI transfer select ").Append(currPlayer.Key).ToString();
					//Element: Player Button
					LUI.LuiContainer playerButton = cui.v2.CreateButton(playersScroll, new LuiOffset(startX, scrollHeight, startX + 324, scrollHeight + 34), command, ColDb.LTD10);
					playerButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

					//Element: Player Button Text
					cui.v2.CreateText(playerButton, LuiPosition.Full, LuiOffset.None, 17, ColDb.LightGray, currPlayer.Value.userName, TextAnchor.MiddleCenter);
					startX += 332;
					if (startX > 750)
					{
						startX = 16;
						scrollHeight -= 42;
					}
				}
			}
			Pool.FreeUnmanaged(ref sb);
			Pool.FreeUnmanaged(ref online);
			byte[] uiBytes = cui.v2.GetUiBytes();
			cui.Destroy("ShoppyStockUI_ElementCore", player);
			cui.v2.SendUiBytes(player, uiBytes);
		}

		private void OpenPlayerTransferUI(BasePlayer player)
		{
			ShoppyCache sc = cache[player.userID];
			BasePlayer transferPlayer = BasePlayer.FindByID(sc.transferCache.selectedPlayer);
			if (transferPlayer)
				AddPlayerToData(transferPlayer);
			else
				AddPlayerToData(sc.transferCache.selectedPlayer);
			PlayerData pd = data.currencies.players[sc.transferCache.selectedPlayer];
			StringBuilder sb = Pool.Get<StringBuilder>();
			using CUI cui = new CUI(CuiHandler);
			LUI.LuiContainer shoppyUi = cui.v2.CreatePanel("ShoppyStockUI", LuiPosition.Full, LuiOffset.None, ColDb.Transparent, "ShoppyStockUI_TransferUserCore").SetDestroy("ShoppyStockUI_TransferUserCore");
			//Element: Background Blur
			LUI.LuiContainer backgroundBlur = cui.v2.CreatePanel(shoppyUi, LuiPosition.Full, LuiOffset.None, ColDb.BlackTrans20);
			backgroundBlur.SetMaterial("assets/content/ui/uibackgroundblur.mat");

			//Element: Background Darker
			LUI.LuiContainer backgroundDarker = cui.v2.CreatePanel(shoppyUi, LuiPosition.Full, LuiOffset.None, ColDb.BlackTrans20);
			backgroundDarker.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Center Anchor
			LUI.LuiContainer centerAnchor = cui.v2.CreatePanel(shoppyUi, LuiPosition.MiddleCenter, LuiOffset.None, ColDb.Transparent);
			centerAnchor.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Main Panel
			LUI.LuiContainer mainPanel = cui.v2.CreatePanel(centerAnchor, new LuiOffset(-270, -225, 270, 225), ColDb.DarkGray);
			mainPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Top Panel
			LUI.LuiContainer topPanel = cui.v2.CreatePanel(mainPanel, new LuiOffset(0, 418, 540, 450), ColDb.LTD5);
			topPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Top Title
			cui.v2.CreateText(topPanel, new LuiOffset(10, 0, 540, 32), 20, ColDb.LightGray, Lang("TransferToPlayer", player.UserIDString), TextAnchor.MiddleLeft);

			//Element: Player Menu
			LUI.LuiContainer playerMenu = cui.v2.CreateEmptyContainer(mainPanel.name).SetOffset(new LuiOffset(0, 0, 540, 418));
			cui.v2.elements.Add(playerMenu);

			//Element: Image Background
			LUI.LuiContainer imageBackground = cui.v2.CreatePanel(playerMenu, new LuiOffset(153, 312, 243, 402), ColDb.LTD5);
			imageBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Player Image 
			string transferIdString = sc.transferCache.selectedPlayer.ToString();
			cui.v2.CreateEmptyContainer(imageBackground, add: true).SetAnchorAndOffset(LuiPosition.None, new LuiOffset(8, 8, 82, 82)).SetSteamIcon(transferIdString, ColDb.WhiteTrans80);

			//Element: Player Name
			cui.v2.CreateText(playerMenu, new LuiOffset(251, 331, 599, 371), 25, ColDb.LightGray, pd.userName, TextAnchor.LowerLeft);

			//Element: Player ID
			LUI.LuiContainer playerID = cui.v2.CreateText(playerMenu, new LuiOffset(252, 305, 599, 331), 15, ColDb.LTD30, transferIdString, TextAnchor.UpperLeft);
			playerID.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

			//Element: Currency Select Title
			cui.v2.CreateText(playerMenu, new LuiOffset(173, 274, 367, 306), 15, ColDb.LTD80, Lang("SelectCurrency", player.UserIDString), TextAnchor.MiddleCenter);

			//Element: Currency Select Background
			LUI.LuiContainer currencySelectBackground = cui.v2.CreatePanel(playerMenu, new LuiOffset(173, 204, 366, 274), ColDb.BlackTrans20);
			currencySelectBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Currency Select Scroll
			LuiScrollbar currencySelectBackground_Vertical = new LuiScrollbar()
			{
				autoHide = false,
				size = 4,
				handleColor = ColDb.LTD15,
				highlightColor = ColDb.LTD20,
				pressedColor = ColDb.LTD10,
				trackColor = ColDb.BlackTrans10
			};

			int validCurrencies = 0;
			foreach (var curr in config.curr)
			{
				if (curr.Value.transferCfg.enabled)
					validCurrencies++;
			}

			int scrollHeight = 26 * validCurrencies + 12;
			if (scrollHeight < 70)
				scrollHeight = 70;

			LUI.LuiContainer currencySelectScroll = cui.v2.CreateScrollView(currencySelectBackground, new LuiOffset(0, 0, 189, 70), true, false, ScrollRect.MovementType.Elastic, 0.1f, true, 0.1f, 25f, currencySelectBackground_Vertical, default);
			currencySelectScroll.SetScrollContent(new LuiPosition(0, 1, 1, 1), new LuiOffset(0, -scrollHeight, 0, 0));
			
			//Element: Scrollable Holder
			cui.v2.CreatePanel(currencySelectScroll, LuiPosition.Full, LuiOffset.None, ColDb.Transparent);

			string color;
			foreach (var curr in config.curr)
			{
				if (!curr.Value.transferCfg.enabled) continue;
				scrollHeight -= 32;
				if (sc.transferCache.selectedCurrency.Length == 0)
					sc.transferCache.selectedCurrency = curr.Key;
				//Element: Currency Button
				string command = sb.Clear().Append("ShoppyStock_UI transfer currency ").Append(curr.Key).ToString();
				color = sc.transferCache.selectedCurrency == curr.Key ? ColDb.LTD10 : ColDb.Transparent;
				string name = sb.Clear().Append("ShoppyStockUI_SelectCurrency_").Append(curr.Key).ToString();
				LUI.LuiContainer currencyButton = cui.v2.CreateButton(currencySelectScroll, new LuiOffset(6, scrollHeight, 181, scrollHeight + 26), command, ColDb.LTD15, name: name);
				currencyButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Currency Button Icon
				color = sc.transferCache.selectedCurrency == curr.Key ? ColDb.LightGray : ColDb.LTD60;
				string subName = sb.Clear().Append(name).Append("_Icon").ToString();
				FindAndCreateValidImage(cui, currencyButton, new LuiOffset(8, 4, 26, 22), curr.Value.currCfg.icon, color, name: subName);

				//Element: Currency Button Text
				subName = sb.Clear().Append(name).Append("_Text").ToString();
				LUI.LuiContainer currencyButtonText = cui.v2.CreateText(currencyButton, new LuiOffset(30, 0, 175, 26), 13, color, Lang(sb.Clear().Append(curr.Key).Append("_Name").ToString(), player.UserIDString), TextAnchor.MiddleLeft, subName);
				currencyButtonText.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

				float tax = curr.Value.transferCfg.tax;
				foreach (var perm in curr.Value.transferCfg.taxPerms)
					if (perm.Value < tax && permission.UserHasPermission(player.UserIDString, perm.Key))
						tax = perm.Value;

				if (tax > 0)
				{
					//Element: Currency Button Tax Text
					LUI.LuiContainer currencyButtonTaxText = cui.v2.CreateText(currencyButton, new LuiOffset(149, 0, 175, 26), 8, ColDb.LTD60, Lang("TransferTax", player.UserIDString, tax), TextAnchor.MiddleCenter);
					currencyButtonTaxText.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);
				}

			}
			//Element: Transfer Amount Title
			cui.v2.CreateText(playerMenu, new LuiOffset(173, 168, 367, 200), 15, ColDb.LTD80, Lang("TransferAmount", player.UserIDString), TextAnchor.MiddleCenter);

			//Element: Transfer Amount Background
			LUI.LuiContainer transferAmountBackground = cui.v2.CreatePanel(playerMenu, new LuiOffset(173, 126, 367, 168), ColDb.BlackTrans20);
			transferAmountBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Transfer Amount Input
			cui.v2.CreateInput(transferAmountBackground, LuiPosition.Full, LuiOffset.None, ColDb.LightGray, sc.transferCache.transferAmount.ToString("0.##"), 27, "ShoppyStock_UI transfer amount", 0, true, CUI.Handler.FontTypes.RobotoCondensedBold, TextAnchor.MiddleCenter, "ShoppyStockUI_BalanceInput").SetInputKeyboard(hudMenuInput: true);

			//Element: Balance After Title
			cui.v2.CreateText(playerMenu, new LuiOffset(163, 99, 377, 121), 13, ColDb.LTD80, Lang("BalanceAfter", player.UserIDString), TextAnchor.LowerCenter);

			TransferConfig tc = config.curr[sc.transferCache.selectedCurrency].transferCfg;
			float currentTax = tc.tax;
			foreach (var perm in tc.taxPerms)
				if (perm.Value < currentTax && permission.UserHasPermission(player.UserIDString, perm.Key))
					currentTax = perm.Value;
			
			//Element: Balance After
			CurrencyConfig currCfg = config.curr[sc.transferCache.selectedCurrency].currCfg;
			string balAfter = FormatCurrency(currCfg, GetPlayerCurrency(player, sc.transferCache.selectedCurrency) - (sc.transferCache.transferAmount + sc.transferCache.transferAmount / 100f * currentTax), sb);
			cui.v2.CreateText(playerMenu, new LuiOffset(193, 71, 347, 99), 20, ColDb.LightGray, balAfter, TextAnchor.UpperCenter, "ShoppyStockUI_BalanceAfter");

			float dayLimit = tc.dailyTransferLimit;
			foreach (var perm in tc.dailyTransferLimitPerms)
				if (permission.UserHasPermission(player.UserIDString, perm.Key))
					dayLimit = perm.Value;
			//Element: Daily Limit Title
			color = dayLimit == 0 ? ColDb.Transparent : ColDb.LTD80;
			cui.v2.CreateText(playerMenu, new LuiOffset(-5, 99, 168, 121), 13, color, Lang("DailyLimitCurrency", player.UserIDString), TextAnchor.LowerCenter, "ShoppyStockUI_DailyLimitTitle");

			float dayTransfered = 0;
			if (dayLimit > 0 && data.transfer[sc.transferCache.selectedCurrency].players.TryGetValue(player.userID, out var ptd))
				ptd.dailyTransfers.TryGetValue(dateString, out dayTransfered);
			color = dayLimit == 0 ? ColDb.Transparent : ColDb.LightGray;
			string text = string.Empty;
			if (dayLimit > 0)
			{
				string currencyUsed = FormatCurrency(currCfg, dayTransfered, sb);
				string currencyLimit = FormatCurrency(currCfg, dayLimit, sb);
				text = sb.Clear().Append(currencyUsed).Append(" / ").Append(currencyLimit).ToString();
			}
			//Element: Daily Limit
			cui.v2.CreateText(playerMenu, new LuiOffset(0, 71, 154, 99), 20, color, text, TextAnchor.UpperCenter, "ShoppyStockUI_DailyLimit");

			float wipeLimit = tc.wipeTransferLimit;
			foreach (var perm in tc.wipeTransferLimitPerms)
				if (permission.UserHasPermission(player.UserIDString, perm.Key))
					wipeLimit = perm.Value;
			
			//Element: Wipe Limit Title
			color = wipeLimit == 0 ? ColDb.Transparent : ColDb.LTD80;
			cui.v2.CreateText(playerMenu, new LuiOffset(367, 99, 540, 121), 13, color, Lang("WipeLimitCurrency", player.UserIDString), TextAnchor.LowerCenter, "ShoppyStockUI_WipeLimitTitle");

			float wipeTransfered = 0;
			if (wipeLimit > 0 && data.transfer[sc.transferCache.selectedCurrency].players .TryGetValue(player.userID, out var ptd2))
				wipeTransfered = ptd2.wipeTransfer;
			color = wipeLimit == 0 ? ColDb.Transparent : ColDb.LightGray;
			text = string.Empty;
			if (wipeLimit > 0)
			{
				string currencyUsed = FormatCurrency(currCfg, wipeTransfered, sb);
				string currencyLimit = FormatCurrency(currCfg, wipeLimit, sb);
				text = sb.Clear().Append(currencyUsed).Append(" / ").Append(currencyLimit).ToString();
			}
			//Element: Wipe Limit
			cui.v2.CreateText(playerMenu, new LuiOffset(372, 71, 526, 99), 20, color, text, TextAnchor.UpperCenter, "ShoppyStockUI_WipeLimit");

			//Element: Go Back Button
			LUI.LuiContainer goBackButton = cui.v2.CreateButton(playerMenu, new LuiOffset(100, 24, 254, 60), "ShoppyStock_UI transfer goBack", ColDb.RedBg);
			goBackButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Go Back Button Text
			cui.v2.CreateText(goBackButton, LuiPosition.Full, LuiOffset.None, 22, ColDb.RedText, Lang("GoBack", player.UserIDString), TextAnchor.MiddleCenter);

			//Element: Confirm Button
			LUI.LuiContainer confirmButton = cui.v2.CreateButton(playerMenu, new LuiOffset(286, 24, 440, 60), "ShoppyStock_UI transfer confirm", ColDb.GreenBg);
			confirmButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Confirm Button Text
			cui.v2.CreateText(confirmButton, LuiPosition.Full, LuiOffset.None, 22, ColDb.GreenText, Lang("Transfer", player.UserIDString), TextAnchor.MiddleCenter);
			Pool.FreeUnmanaged(ref sb);
			cui.v2.SendUi(player);
		}

		private void UpdateTransferCurrency(BasePlayer player, string changeCurrency = "")
		{
			ShoppyCache sc = cache[player.userID];
			StringBuilder sb = Pool.Get<StringBuilder>();
			using CUI cui = new CUI(CuiHandler);
			if (changeCurrency.Length > 0)
			{
				string name = sb.Clear().Append("ShoppyStockUI_SelectCurrency_").Append(sc.transferCache.selectedCurrency).ToString();
				cui.v2.Update(name).SetButtonColors(ColDb.Transparent);
				string subName = sb.Clear().Append(name).Append("_Icon").ToString();
				cui.v2.Update(subName).SetColor(ColDb.LTD60);
				subName = sb.Clear().Append(name).Append("_Text").ToString();
				cui.v2.Update(subName).SetTextColor(ColDb.LTD60);
				sc.transferCache.selectedCurrency = changeCurrency;
				name = sb.Clear().Append("ShoppyStockUI_SelectCurrency_").Append(sc.transferCache.selectedCurrency).ToString();
				cui.v2.Update(name).SetButtonColors(ColDb.LTD10);
				subName = sb.Clear().Append(name).Append("_Icon").ToString();
				cui.v2.Update(subName).SetColor(ColDb.LightGray);
				subName = sb.Clear().Append(name).Append("_Text").ToString();
				cui.v2.Update(subName).SetTextColor(ColDb.LightGray);
			}
			float currentCurrency = GetPlayerCurrency(player, sc.transferCache.selectedCurrency);
			
			TransferConfig tc = config.curr[sc.transferCache.selectedCurrency].transferCfg;
			float currentTax = tc.tax;
			foreach (var perm in tc.taxPerms)
				if (perm.Value < currentTax && permission.UserHasPermission(player.UserIDString, perm.Key))
					currentTax = perm.Value;
			
			float balAfterFloat = currentCurrency - sc.transferCache.transferAmount + sc.transferCache.transferAmount / 100f * currentTax;
			if (balAfterFloat < 0)
			{
				sc.transferCache.transferAmount = currentCurrency;
				cui.v2.Update("ShoppyStockUI_BalanceInput").SetInput(text: sc.transferCache.transferAmount.ToString("0.##"), update: true);
			}
			string balAfter = FormatCurrency(config.curr[sc.transferCache.selectedCurrency].currCfg, currentCurrency - sc.transferCache.transferAmount + sc.transferCache.transferAmount / 100f * currentTax, sb);
			cui.v2.UpdateText("ShoppyStockUI_BalanceAfter", balAfter);
			float dayLimit = tc.dailyTransferLimit;
			foreach (var perm in tc.dailyTransferLimitPerms)
				if (permission.UserHasPermission(player.UserIDString, perm.Key))
					dayLimit = perm.Value;
			string color = dayLimit == 0 ? ColDb.Transparent : ColDb.LTD80;
			cui.v2.Update("ShoppyStockUI_DailyLimitTitle").SetTextColor(color);

			float dayTransfered = 0;
			if (dayLimit > 0 && data.transfer[sc.transferCache.selectedCurrency].players.TryGetValue(player.userID, out var ptd))
				ptd.dailyTransfers.TryGetValue(dateString, out dayTransfered);
			color = dayLimit == 0 ? ColDb.Transparent : ColDb.LightGray;
			string text = string.Empty;
			if (dayLimit > 0)
			{
				CurrencyConfig currCfg = config.curr[sc.transferCache.selectedCurrency].currCfg;
				string currencyUsed = FormatCurrency(currCfg, dayTransfered, sb);
				string currencyLimit = FormatCurrency(currCfg, dayLimit, sb);
				text = sb.Clear().Append(currencyUsed).Append(" / ").Append(currencyLimit).ToString();
			}
			cui.v2.UpdateText("ShoppyStockUI_DailyLimit", text, color: color);

			float wipeLimit = tc.wipeTransferLimit;
			foreach (var perm in tc.wipeTransferLimitPerms)
				if (permission.UserHasPermission(player.UserIDString, perm.Key))
					wipeLimit = perm.Value;
			
			color = wipeLimit == 0 ? ColDb.Transparent : ColDb.LTD80;
			cui.v2.Update("ShoppyStockUI_WipeLimitTitle").SetTextColor(color);
			
			float wipeTransfered = 0;
			if (wipeLimit > 0 && data.transfer[sc.transferCache.selectedCurrency].players .TryGetValue(player.userID, out var ptd2))
				wipeTransfered = ptd2.wipeTransfer;
			color = wipeLimit == 0 ? ColDb.Transparent : ColDb.LightGray;
			text = string.Empty;
			if (wipeLimit > 0)
			{
				CurrencyConfig currCfg = config.curr[sc.transferCache.selectedCurrency].currCfg;
				string currencyUsed = FormatCurrency(currCfg, wipeTransfered, sb);
				string currencyLimit = FormatCurrency(currCfg, wipeLimit, sb);
				text = sb.Clear().Append(currencyUsed).Append(" / ").Append(currencyLimit).ToString();
			}
			Pool.FreeUnmanaged(ref sb);
			cui.v2.UpdateText("ShoppyStockUI_WipeLimit", text, color: color);
			cui.v2.SendUi(player);
		}

		private void OpenDepositWithdrawUI(BasePlayer player, bool forceUpdate = false)
		{
			using CUI cui = new CUI(CuiHandler);
			ShoppyCache sc = cache[player.userID];
			StringBuilder sb = Pool.Get<StringBuilder>();
			SwitchTypeUI(cui, sc, ShoppyType.Deposit, sb, forceUpdate);
			
			//Element: Transfer Menu
			LUI.LuiContainer depositWithdrawSection = cui.v2.CreateEmptyContainer("ShoppyStockUI_ElementParent", "ShoppyStockUI_ElementCore", true).SetOffset(new LuiOffset(0, 0, 1100, 528));

			//Element: Deposit Withdraw Scroll
			LuiScrollbar depositWithdrawSection_Vertical = new LuiScrollbar()
			{
				autoHide = true,
				size = 8,
				handleColor = ColDb.LTD15,
				highlightColor = ColDb.LTD20,
				pressedColor = ColDb.LTD10,
				trackColor = ColDb.BlackTrans10
			};
			
			int depositCounter = 0;
			foreach (var curr in config.curr.Values)
			{
				if (!curr.depositCfg.deposit) continue;
				if (curr.depositCfg.depositPerm.Length > 0 && !permission.UserHasPermission(player.UserIDString, curr.depositCfg.depositPerm)) continue;
				depositCounter += curr.currCfg.currItems.Count;
			}

			int withdrawCounter = 0;
			foreach (var curr in config.curr.Values)
			{
				if (!curr.depositCfg.withdraw) continue;
				if (curr.depositCfg.withdrawPerm.Length > 0 && !permission.UserHasPermission(player.UserIDString, curr.depositCfg.withdrawPerm)) continue;
				withdrawCounter += curr.depositCfg.withdrawItems.Count;
			}

			int scrollHeight = 0;
			if (depositCounter > 0)
				scrollHeight += 238;
			if (withdrawCounter > 0)
				scrollHeight += 48;
			scrollHeight += Mathf.CeilToInt(withdrawCounter / 2f) * 206;
			if (scrollHeight < 496)
				scrollHeight = 496;

			LUI.LuiContainer depositWithdrawScroll = cui.v2.CreateScrollView(depositWithdrawSection, new LuiOffset(16, 16, 1084, 512), true, false, ScrollRect.MovementType.Elastic, 0.1f, true, 0.1f, 25f, depositWithdrawSection_Vertical, default);
			depositWithdrawScroll.SetScrollContent(new LuiPosition(0, 1, 1, 1), new LuiOffset(0, -scrollHeight, 0, 0));

			//Element: Scrollable Holder
			LUI.LuiContainer scrollableHolder = cui.v2.CreatePanel(depositWithdrawScroll, LuiPosition.Full, LuiOffset.None, ColDb.Transparent);
			scrollableHolder.SetMaterial("assets/content/ui/namefontmaterial.mat");

			if (depositCounter > 0)
			{
				scrollHeight -= 48;
				//Element: Deposit Title
				cui.v2.CreateText(depositWithdrawScroll, new LuiOffset(0, scrollHeight, 1068, scrollHeight + 48), 25, ColDb.LTD80, Lang("DepositResourcesCount", player.UserIDString, depositCounter), TextAnchor.MiddleCenter);

				scrollHeight -= 190;
				//Element: Deposit Section
				LUI.LuiContainer depositSection = cui.v2.CreatePanel(depositWithdrawScroll, new LuiOffset(154, scrollHeight, 914, scrollHeight + 190), ColDb.LTD5);
				depositSection.SetMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Deposit Section Title
				cui.v2.CreateText(depositSection, new LuiOffset(16, 146, 396, 184), 28, ColDb.LightGray, Lang("DepositResourcesTitle", player.UserIDString), TextAnchor.LowerLeft);

				//Element: Deposit Section Desc
				LUI.LuiContainer depositSectionDesc = cui.v2.CreateText(depositSection, new LuiOffset(16, 107, 616, 146), 15, ColDb.LTD80, Lang("DepositResourcesDesc", player.UserIDString), TextAnchor.UpperLeft);
				depositSectionDesc.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

				//Element: Item Title
				cui.v2.CreateText(depositSection, new LuiOffset(16, 56, 76, 94), 12, ColDb.LTD60, Lang("Item", player.UserIDString), TextAnchor.MiddleCenter);

				//Element: Currency Title
				cui.v2.CreateText(depositSection, new LuiOffset(16, 38, 76, 56), 12, ColDb.LTD60, Lang("Currency", player.UserIDString), TextAnchor.MiddleCenter);

				//Element: Ratio Title
				cui.v2.CreateText(depositSection, new LuiOffset(16, 20, 76, 38), 12, ColDb.LTD60, Lang("Ratio", player.UserIDString), TextAnchor.MiddleCenter);

				//Element: Deposit Resources Background
				LUI.LuiContainer depositResourcesBackground = cui.v2.CreatePanel(depositSection, new LuiOffset(84, 16, 504, 98), ColDb.BlackTrans20);
				depositResourcesBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Deposit Resources Scroll
				LuiScrollbar depositResourcesBackground_Horizontal = new LuiScrollbar()
				{
					autoHide = true,
					size = 0,
					handleColor = ColDb.Transparent,
					highlightColor = ColDb.Transparent,
					pressedColor = ColDb.Transparent,
					trackColor = ColDb.Transparent
				};
				int scrollWidth = 44 * depositCounter;
				if (scrollWidth < 420)
					scrollWidth = 420;

				LUI.LuiContainer depositResourcesScroll = cui.v2.CreateScrollView(depositResourcesBackground, new LuiOffset(0, 0, 420, 82), false, true, ScrollRect.MovementType.Elastic, 0.1f, true, 0.1f, 25f, default, depositResourcesBackground_Horizontal);
				depositResourcesScroll.SetScrollContent(new LuiPosition(1, 0, 1, 1), new LuiOffset(-scrollWidth, 0, 0, 0));

				scrollWidth = 4;

				foreach (var curr in config.curr)
				{
					if (!curr.Value.depositCfg.deposit) continue;
					if (curr.Value.depositCfg.depositPerm.Length > 0 && !permission.UserHasPermission(player.UserIDString, curr.Value.depositCfg.depositPerm)) continue;
					foreach (var item in curr.Value.currCfg.currItems)
					{
						//Element: Deposit Resource
						LUI.LuiContainer depositResource = cui.v2.CreateEmptyContainer(depositResourcesScroll.name).SetOffset(new LuiOffset(scrollWidth, 0, scrollWidth + 40, 82));
						cui.v2.elements.Add(depositResource);

						//Element: Deposit Icon
						LUI.LuiContainer depositIcon = cui.v2.CreateItemIcon(depositResource, new LuiOffset(0, 40, 40, 78), iconRedirect[item.shortname], item.skinId, ColDb.WhiteTrans80);
						depositIcon.SetMaterial("assets/content/ui/namefontmaterial.mat");

						//Element: UI Text
						cui.v2.CreateText(depositIcon, new LuiOffset(0, 2, 40, 38), 8, ColDb.LightGray, item.displayName, TextAnchor.LowerCenter);

						//Element: Deposit Name
						cui.v2.CreateText(depositResource, new LuiOffset(0, 22, 40, 40), 10, ColDb.LTD80, Lang(sb.Clear().Append(curr.Key).Append("_Name").ToString(), player.UserIDString), TextAnchor.MiddleCenter);

						string ratio = item.value < 1 ? sb.Clear().Append("1:").Append((1f / item.value).ToString("0.##")).ToString() : sb.Clear().Append((item.value).ToString("0.##")).Append(":1").ToString();
						//Element: Deposit Ratio
						cui.v2.CreateText(depositResource, new LuiOffset(0, 4, 40, 22), 10, ColDb.LTD80, ratio, TextAnchor.MiddleCenter);

						scrollWidth += 44;
					}
				}
				//Element: Open Inventory Button
				LUI.LuiContainer openInventoryButton = cui.v2.CreateButton(depositSection, new LuiOffset(547, 31, 717, 83), "ShoppyStock_UI deposit open", ColDb.GreenBg);
				openInventoryButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Open Inventory Button Text
				cui.v2.CreateText(openInventoryButton, LuiPosition.Full, LuiOffset.None, 18, ColDb.GreenText, Lang("OpenDeposit", player.UserIDString), TextAnchor.MiddleCenter);
			}
			if (withdrawCounter > 0)
			{
				scrollHeight -= 48;
				//Element: Withdraw Title
				cui.v2.CreateText(depositWithdrawScroll, new LuiOffset(0, scrollHeight, 1068, scrollHeight + 48), 25, ColDb.LTD80, Lang("WithdrawResourcesCount", player.UserIDString, withdrawCounter), TextAnchor.MiddleCenter);

				scrollHeight -= 190;
				int counter = 0;
				int startX = 66;
				foreach (var curr in config.curr)
				{
					if (!curr.Value.depositCfg.withdraw) continue;
					if (curr.Value.depositCfg.withdrawPerm.Length > 0 && !permission.UserHasPermission(player.UserIDString, curr.Value.depositCfg.withdrawPerm)) continue;
					foreach (var item in curr.Value.depositCfg.withdrawItems)
					{
						//Element: Withdraw Panel
						LUI.LuiContainer withdrawPanel = cui.v2.CreatePanel(depositWithdrawScroll, new LuiOffset(startX, scrollHeight, startX + 460, scrollHeight + 190), ColDb.LTD5);
						withdrawPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

						//Element: Withdraw Name
						cui.v2.CreateText(withdrawPanel, new LuiOffset(16, 146, 396, 184), 28, ColDb.LightGray, Lang(sb.Clear().Append(curr.Key).Append("_Name").ToString(), player.UserIDString), TextAnchor.LowerLeft);

						//Element: Withdraw Balance
						string balance = FormatCurrency(curr.Key, GetPlayerCurrency(player, curr.Key), sb);
						cui.v2.CreateText(withdrawPanel, new LuiOffset(16, 116, 396, 146), 20, ColDb.LTD80, Lang("PlayerBalance", player.UserIDString, balance), TextAnchor.UpperLeft);

						//Element: Withdraw To Title
						cui.v2.CreateText(withdrawPanel, new LuiOffset(16, 99, 136, 121), 15, ColDb.LightGray, Lang("WithdrawTo", player.UserIDString), TextAnchor.LowerCenter);

						//Element: Withdraw Icon
						LUI.LuiContainer withdrawIcon = cui.v2.CreateItemIcon(withdrawPanel, new LuiOffset(36, 16, 116, 96), iconRedirect[item.shortname], item.skinId, ColDb.WhiteTrans80);
						withdrawIcon.SetMaterial("assets/content/ui/namefontmaterial.mat");

						//Element: Withdraw Name Hint
						cui.v2.CreateText(withdrawIcon, new LuiOffset(0, 2, 80, 80), 13, ColDb.LightGray, item.displayName, TextAnchor.LowerCenter);

						//Element: Withdraw Ratio Title
						cui.v2.CreateText(withdrawPanel, new LuiOffset(136, 99, 256, 121), 15, ColDb.LightGray, Lang("WithdrawRatio", player.UserIDString), TextAnchor.LowerCenter);

						//Element: Withdraw Ratio
						string ratio = item.value < 1 ? sb.Clear().Append("1:").Append((1f / item.value).ToString("0.##")).ToString() : sb.Clear().Append((item.value).ToString("0.##")).Append(":1").ToString();
						cui.v2.CreateText(withdrawPanel, new LuiOffset(156, 16, 236, 96), 25, ColDb.GreenBg, ratio, TextAnchor.MiddleCenter);

						//Element: Withdraw Amount
						cui.v2.CreateText(withdrawPanel, new LuiOffset(292, 156, 422, 176), 15, ColDb.LTD60, Lang("Amount", player.UserIDString), TextAnchor.LowerCenter);

						//Element: Withdraw Amount Input Background
						LUI.LuiContainer withdrawAmountInputBackground = cui.v2.CreatePanel(withdrawPanel, new LuiOffset(292, 111, 422, 153), ColDb.BlackTrans20);
						withdrawAmountInputBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

						//Element: Withdraw Icon Hint
						LUI.LuiContainer withdrawIconHint = cui.v2.CreateItemIcon(withdrawAmountInputBackground, new LuiOffset(94, 6, 124, 36), iconRedirect[item.shortname], item.skinId, ColDb.WhiteTrans40);
						withdrawIconHint.SetMaterial("assets/content/ui/namefontmaterial.mat");

						int amount = sc.depositCache.lastInputShortname == item.shortname && sc.depositCache.lastInputSkin == item.skinId ? sc.depositCache.inputAmount : 0;
						string command = sb.Clear().Append("ShoppyStock_UI withdraw amount ").Append(item.shortname).Append(' ').Append(item.skinId).ToString();
						string name = sb.Clear().Append("ShoppyStockUI_WithdrawInput_").Append(item.shortname).Append(item.skinId).ToString();
						//Element: Withdraw Amount Input
						cui.v2.CreateInput(withdrawAmountInputBackground, new LuiOffset(8, 0, 130, 42),  ColDb.LightGray, amount.ToString(), 20, command, font: CUI.Handler.FontTypes.RobotoCondensedRegular, alignment: TextAnchor.MiddleLeft, name: name).SetInputKeyboard(hudMenuInput: true);

						//Element: Withdraw Cost Title
						cui.v2.CreateText(withdrawPanel, new LuiOffset(292, 88, 422, 108), 15, ColDb.LTD60, Lang("WithdrawCost", player.UserIDString), TextAnchor.LowerCenter);

						string cost = FormatCurrency(curr.Key, amount * item.value, sb);
						name = sb.Clear().Append("ShoppyStockUI_WithdrawCost_").Append(item.shortname).Append(item.skinId).ToString();
						//Element: Withdraw Cost
						cui.v2.CreateText(withdrawPanel, new LuiOffset(292, 62, 422, 88), 18, ColDb.LightGray, cost, TextAnchor.UpperCenter, name);

						//Element: Withdraw Button
						command = sb.Clear().Append("ShoppyStock_UI withdraw withdraw ").Append(item.shortname).Append(' ').Append(item.skinId).ToString();
						LUI.LuiContainer withdrawButton = cui.v2.CreateButton(withdrawPanel, new LuiOffset(292, 16, 422, 59), command, ColDb.GreenBg);
						withdrawButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

						//Element: Withdraw Button Text
						cui.v2.CreateText(withdrawButton, LuiPosition.Full, LuiOffset.None, 22, ColDb.GreenText, Lang("Withdraw", player.UserIDString), TextAnchor.MiddleCenter);
						counter++;
						if (counter % 2 == 0)
						{
							scrollHeight -= 206;
							startX = 66;
						}
						else
							startX += 476;
					}
				}
			}
			Pool.FreeUnmanaged(ref sb);
			byte[] uiBytes = cui.v2.GetUiBytes();
			cui.Destroy("ShoppyStockUI_ElementCore", player);
			cui.v2.SendUiBytes(player, uiBytes);
		}

		private void UpdateWithdrawCurrency(BasePlayer player)
		{
			using CUI cui = new CUI(CuiHandler);
			ShoppyCache sc = cache[player.userID];
			StringBuilder sb = Pool.Get<StringBuilder>();
			string currKey = "";
			float itemValue = 0;
			foreach (var curr in config.curr)
			{
				bool found = false;
				foreach (var item in curr.Value.depositCfg.withdrawItems)
					if (item.shortname == sc.depositCache.lastInputShortname && item.skinId == sc.depositCache.lastInputSkin)
					{
						found = true;
						currKey = curr.Key;
						itemValue = item.value;
						break;
					}
				if (found) break;
			}
			if (currKey.Length == 0) return;
			float sumCost = sc.depositCache.inputAmount * itemValue;
			float playerCurr = GetPlayerCurrency(player, currKey);
			string name;
			if (sumCost > playerCurr)
			{
				sc.depositCache.inputAmount = Mathf.FloorToInt(playerCurr / itemValue);
				name = sb.Clear().Append("ShoppyStockUI_WithdrawInput_").Append(sc.depositCache.lastInputShortname).Append(sc.depositCache.lastInputSkin).ToString();
				cui.v2.Update(name).SetInput(text: sc.depositCache.inputAmount.ToString(), update: true);
			}
			string cost = FormatCurrency(currKey, sc.depositCache.inputAmount * itemValue, sb);
			name = sb.Clear().Append("ShoppyStockUI_WithdrawCost_").Append(sc.depositCache.lastInputShortname).Append(sc.depositCache.lastInputSkin).ToString();
			Pool.FreeUnmanaged(ref sb);
			cui.v2.UpdateText(name, cost);
			cui.v2.SendUi(player);
		}

		private void OpenCurrencyDepositUI(BasePlayer player)
		{
			using CUI cui = new CUI(CuiHandler);
			ShoppyCache sc = cache[player.userID];
			
			LUI.LuiContainer shoppyUi = cui.v2.CreateParent(CUI.ClientPanels.Inventory, LuiPosition.LowerCenter, "ShoppyStockUI_Inventory").SetDestroy("ShoppyStockUI_Inventory");
			//Element: Title
			cui.v2.CreateText(shoppyUi, new LuiOffset(193, 394, 572, 426), 20, ColDb.LightGray, Lang("CurrencyDeposit", player.UserIDString), TextAnchor.MiddleLeft);

			//Element: Main Panel
			LUI.LuiContainer mainPanel = cui.v2.CreatePanel(shoppyUi, new LuiOffset(192, 322, 572, 394), ColDb.LightGrayTransRust);
			mainPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Item Icon
			LUI.LuiContainer itemIcon = cui.v2.CreateItemIcon(mainPanel, new LuiOffset(16, 16, 56, 56), "metalpipe", 0, ColDb.Transparent, "ShoppyStockUI_CurrencyDeposit_Icon");
			itemIcon.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Item Name
			LUI.LuiContainer itemName = cui.v2.CreateText(mainPanel, new LuiOffset(62, 12, 364, 60), 18, ColDb.LightGray, Lang("AddCurrencyItem", player.UserIDString), TextAnchor.MiddleLeft, "ShoppyStockUI_CurrencyDeposit_Name");
			itemName.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

			//Element: Item Worth Title
			cui.v2.CreateText(mainPanel, new LuiOffset(260, 12, 364, 32), 12, ColDb.LTD80, Lang("ItemWorth", player.UserIDString), TextAnchor.UpperCenter);

			//Element: Item Worth
			cui.v2.CreateText(mainPanel, new LuiOffset(260, 35, 364, 60), 17, ColDb.LightGray, Lang("Nothing", player.UserIDString), TextAnchor.LowerCenter, "ShoppyStockUI_CurrencyDeposit_Price");

			//Element: Hint Icon
			LUI.LuiContainer hintIcon = cui.v2.CreateSprite(shoppyUi, new LuiOffset(550, 298, 566, 314), "assets/icons/info.png", ColDb.LTD60);

			//Element: Hint Text
			LUI.LuiContainer hintText = cui.v2.CreateText(hintIcon, new LuiOffset(-298, -5, -4, 21), 9, ColDb.LTD60, Lang("CurrencyDepositHint", player.UserIDString), TextAnchor.MiddleRight);
			hintText.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

			//Element: Currency Item Hint
			cui.v2.CreateText(shoppyUi, new LuiOffset(202, 271, 572, 292), 13, ColDb.LTD80, Lang("InputCurrencyHint", player.UserIDString), TextAnchor.MiddleLeft);

			//Element: Submit Button
			LUI.LuiContainer submitButton = cui.v2.CreateButton(shoppyUi, new LuiOffset(199, 117, 329, 173), "", ColDb.LTD40, name: "ShoppyStockUI_CurrencyDeposit_Button");
			submitButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Submit Button Text
			cui.v2.CreateText(submitButton, new LuiOffset(49, 0, 130, 56), 18, ColDb.LTD80, Lang("AddValidItem", player.UserIDString), TextAnchor.MiddleLeft, "ShoppyStockUI_CurrencyDeposit_Button_Text");

			//Element: Submit Button Image
			cui.v2.CreateSprite(submitButton, new LuiOffset(16, 16, 41, 41), "assets/icons/exit.png", ColDb.LTD80, "ShoppyStockUI_CurrencyDeposit_Button_Icon");
			cui.v2.SendUi(player);
			
            player.EndLooting();
			sc.stockCache.containerType = ContainerType.CurrencyDeposit;
			SpawnAndOpenContainer(player);
		}

		private void UpdateCurrencyDepositUI(BasePlayer player, ItemContainer cont)
		{
			if (cont == null) return;	 
			ShoppyCache sc = cache[player.userID];
			if (sc.stockCache.containerType != ContainerType.CurrencyDeposit) return;
			Item inputItem = cont.itemList.Count > 0 ? cont.itemList[0] : null;
			using CUI cui = new CUI(CuiHandler);
			if (inputItem == null)
			{
				cui.v2.UpdateColor("ShoppyStockUI_CurrencyDeposit_Icon", ColDb.Transparent);
				cui.v2.UpdateText("ShoppyStockUI_CurrencyDeposit_Name", Lang("AddCurrencyItem", player.UserIDString));
				cui.v2.UpdateText("ShoppyStockUI_CurrencyDeposit_Price", Lang("Nothing", player.UserIDString));
				cui.v2.Update("ShoppyStockUI_CurrencyDeposit_Button").SetButton("", ColDb.LTD40);
				cui.v2.UpdateText("ShoppyStockUI_CurrencyDeposit_Button_Text", Lang("AddValidItem", player.UserIDString), color: ColDb.LTD80);
				cui.v2.UpdateColor("ShoppyStockUI_CurrencyDeposit_Button_Icon", ColDb.LTD80);
				cui.v2.SendUi(player);
				return;
			}
			string shopKey = "";
			CurrencyItemConfig cic = null;
			foreach (var curr in config.curr)
			{
				bool found = false;
				if (!curr.Value.depositCfg.deposit) continue;
				foreach (var item in curr.Value.currCfg.currItems)
				{
					if (item.shortname == inputItem.info.shortname && item.skinId == inputItem.skin)
					{
						if (curr.Value.depositCfg.depositPerm.Length > 0 && !permission.UserHasPermission(player.UserIDString, curr.Value.depositCfg.depositPerm)) continue;
						found = true;
						shopKey = curr.Key;
						cic = item;
						break;
					}
				}
				if (found) break;
			}
			cui.v2.UpdateColor("ShoppyStockUI_CurrencyDeposit_Icon", ColDb.WhiteTrans80);
			cui.v2.Update("ShoppyStockUI_CurrencyDeposit_Icon").SetItemIcon(inputItem.info.itemid, inputItem.skin);
			if (cic == null)
			{
				cui.v2.UpdateText("ShoppyStockUI_CurrencyDeposit_Name", Lang("AddCurrencyItem", player.UserIDString));
				cui.v2.UpdateText("ShoppyStockUI_CurrencyDeposit_Price", Lang("Nothing", player.UserIDString));
				cui.v2.Update("ShoppyStockUI_CurrencyDeposit_Button").SetButton("", ColDb.LTD40);
				cui.v2.UpdateText("ShoppyStockUI_CurrencyDeposit_Button_Text", Lang("AddValidItem", player.UserIDString), color: ColDb.LTD80);
				cui.v2.UpdateColor("ShoppyStockUI_CurrencyDeposit_Button_Icon", ColDb.LTD80);
				cui.v2.SendUi(player);
				return;
			}
			StringBuilder sb = Pool.Get<StringBuilder>();
			cui.v2.UpdateText("ShoppyStockUI_CurrencyDeposit_Name", cic.displayName);
			cui.v2.UpdateText("ShoppyStockUI_CurrencyDeposit_Price", FormatCurrency(shopKey, cic.value * inputItem.amount, sb));
			Pool.FreeUnmanaged(ref sb);
			cui.v2.Update("ShoppyStockUI_CurrencyDeposit_Button").SetButton(Community.Protect("ShoppyStock_UI deposit deposit"), ColDb.GreenBg);
			cui.v2.UpdateText("ShoppyStockUI_CurrencyDeposit_Button_Text", Lang("SubmitDeposit", player.UserIDString), color: ColDb.GreenText);
			cui.v2.UpdateColor("ShoppyStockUI_CurrencyDeposit_Button_Icon", ColDb.GreenText);
			cui.v2.SendUi(player);
		}

		private void OpenLeaderboardUI(BasePlayer player)
		{
			using CUI cui = new CUI(CuiHandler);
			ShoppyCache sc = cache[player.userID];
			StringBuilder sb = Pool.Get<StringBuilder>();
			SwitchTypeUI(cui, sc, ShoppyType.Unassigned, sb);

			if ((DateTime.Now - lastLeaderboardCheck).TotalMinutes > config.ui.leaderboardCheck)
				DrawNewLeaderboards();
			
			//Element: Leaderboards
			LUI.LuiContainer leaderboards = cui.v2.CreateEmptyContainer("ShoppyStockUI_ElementParent", "ShoppyStockUI_ElementCore", true).SetOffset(new LuiOffset(0, 0, 1100, 528));

			//Element: Leaderboards Title
			cui.v2.CreateText(leaderboards, new LuiOffset(0, 480, 1100, 528), 25, ColDb.LTD80, Lang("CurrencyLeaderboards", player.UserIDString), TextAnchor.MiddleCenter, "ShoppyStockUI_LeaderboardsTitle");

			int currencyCount = 0;
			foreach (var curr in config.curr)
				if (curr.Value.currCfg.leaderboard)
					currencyCount++;

			//Element: Leaderboard Scroll
			LuiScrollbar leaderboards_Horizontal = new LuiScrollbar()
			{
				invert = true,
				autoHide = true,
				size = 8,
				handleColor = ColDb.LTD15,
				highlightColor = ColDb.LTD20,
				pressedColor = ColDb.LTD10,
				trackColor = ColDb.BlackTrans10
			};

			int scrollWidth = currencyCount * 227;
			if (scrollWidth < 1068)
				scrollWidth = 1068;
			
			LUI.LuiContainer leaderboardScroll = cui.v2.CreateScrollView(leaderboards, new LuiOffset(16, 28, 1084, 480), false, true, ScrollRect.MovementType.Elastic, 0.1f, true, 0.1f, 25f, default, leaderboards_Horizontal);
			leaderboardScroll.SetScrollContent(new LuiPosition(0, 0, 0, 1), new LuiOffset(0, 0, scrollWidth, 0));

			//Element: Scrollable Holder
			cui.v2.CreatePanel(leaderboardScroll, LuiPosition.Full, LuiOffset.None, ColDb.Transparent);
			
			int startX;
			switch (currencyCount)
			{
				case 1:
					startX = 421;
					break;
				case 2:
					startX = 291;
					break;
				case 3:
					startX = 161;
					break;
				case 4:
					startX = 32;
					break;
				default:
					startX = 0;
					break;
			}
			
			foreach (var curr in config.curr)
			{
				if (!curr.Value.currCfg.leaderboard) continue;
				//Element: Currency Panel
				LUI.LuiContainer currencyPanel = cui.v2.CreatePanel(leaderboardScroll, new LuiOffset(startX, 20, startX + 227, 460), ColDb.LTD5);
				currencyPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Icon Left
				FindAndCreateValidImage(cui, currencyPanel, new LuiOffset(5, 405, 27, 427), curr.Value.currCfg.icon, ColDb.LTD80);

				//Element: Currency Name
				cui.v2.CreateText(currencyPanel, new LuiOffset(0, 400, 227, 432), 18, ColDb.LightGray, Lang(sb.Clear().Append(curr.Key).Append("_Name").ToString(), player.UserIDString), TextAnchor.MiddleCenter);

				//Element: Icon Right
				FindAndCreateValidImage(cui, currencyPanel, new LuiOffset(200, 405, 222, 427), curr.Value.currCfg.icon, ColDb.LTD80);

				//Element: Leaderboard Background
				LUI.LuiContainer leaderboardBackground = cui.v2.CreatePanel(currencyPanel, new LuiOffset(0, 0, 227, 400), ColDb.LTD10);
				leaderboardBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Leaderboard
				LUI.LuiContainer leaderboard = cui.v2.CreateText(leaderboardBackground, new LuiOffset(0, 0, 227, 396), 11, ColDb.LTD80, cachedLeaderboards[curr.Key], TextAnchor.UpperCenter);
				leaderboard.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);
				startX += 259;
			}
			Pool.FreeUnmanaged(ref sb);
			byte[] uiBytes = cui.v2.GetUiBytes();
			cui.Destroy("ShoppyStockUI_ElementCore", player);
			cui.v2.SendUiBytes(player, uiBytes);
		}

		private void OpenExchangeUI(BasePlayer player, bool forced = false)
		{
			using CUI cui = new CUI(CuiHandler);
			ShoppyCache sc = cache[player.userID];
			StringBuilder sb = Pool.Get<StringBuilder>();
			SwitchTypeUI(cui, sc, ShoppyType.Exchange, sb, forced);
			
			//Element: Exchange Section
			LUI.LuiContainer exchangeSection = cui.v2.CreateEmptyContainer("ShoppyStockUI_ElementParent", "ShoppyStockUI_ElementCore", true).SetOffset(new LuiOffset(0, 0, 1100, 528));

			int exchangeCount = 0;
			foreach (var curr in config.curr)
			{
				if (!curr.Value.exchangeCfg.enabled) continue;
				foreach (var exchange in curr.Value.exchangeCfg.exchanges)
				{
					if (exchange.perm.Length > 0 && !permission.UserHasPermission(player.UserIDString, exchange.perm)) continue;
					exchangeCount++;
				}
			}
			
			//Element: Title
			cui.v2.CreateText(exchangeSection, new LuiOffset(0, 480, 1100, 528), 25, ColDb.LTD80, Lang("ExchangeTitle", player.UserIDString, exchangeCount), TextAnchor.MiddleCenter);

			//Element: Exchanges Scroll
			LuiScrollbar exchangeSection_Vertical = new LuiScrollbar()
			{
				autoHide = true,
				size = 8,
				handleColor = ColDb.LTD15,
				highlightColor = ColDb.LTD20,
				pressedColor = ColDb.LTD10,
				trackColor = ColDb.BlackTrans10
			};

			int scrollHeight = exchangeCount * 206;
			if (scrollHeight < 464)
				scrollHeight = 464;

			LUI.LuiContainer exchangesScroll = cui.v2.CreateScrollView(exchangeSection, new LuiOffset(16, 16, 1084, 480), true, false, ScrollRect.MovementType.Elastic, 0.1f, true, 0.1f, 25f, exchangeSection_Vertical, default);
			exchangesScroll.SetScrollContent(new LuiPosition(0, 1, 1, 1), new LuiOffset(0, -scrollHeight, 0, 0));

			//Element: Scrollable Holder
			LUI.LuiContainer scrollableHolder = cui.v2.CreatePanel(exchangesScroll, LuiPosition.Full, LuiOffset.None, ColDb.Transparent);
			scrollableHolder.SetMaterial("assets/content/ui/namefontmaterial.mat");

			int counter = -1;
			foreach (var curr in config.curr)
			{
				if (!curr.Value.exchangeCfg.enabled) continue;
				foreach (var exchange in curr.Value.exchangeCfg.exchanges)
				{
					counter++;
					if (exchange.perm.Length > 0 && !permission.UserHasPermission(player.UserIDString, exchange.perm)) continue;
					scrollHeight -= 206;
					
					//Element: Exchange Panel
					LUI.LuiContainer exchangePanel = cui.v2.CreatePanel(exchangesScroll, new LuiOffset(154, scrollHeight, 914, scrollHeight + 190), ColDb.LTD5);
					exchangePanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

					string text = Lang(sb.Clear().Append("ExchangeTitle_").Append(counter).ToString(), player.UserIDString);
					//Element: Exchange Title
					cui.v2.CreateText(exchangePanel, new LuiOffset(16, 146, 396, 184), 28, ColDb.LightGray, text, TextAnchor.LowerLeft);

					bool isCurrency = exchange.currName.Length > 0;
					text = isCurrency ? Lang("CurrencyExchange", player.UserIDString) : Lang("ItemExchange", player.UserIDString);
					//Element: Exchange Type
					cui.v2.CreateText(exchangePanel, new LuiOffset(16, 116, 396, 146), 20, ColDb.LTD60, text, TextAnchor.UpperLeft);

					//Element: From Icon
					if (isCurrency)
						FindAndCreateValidImage(cui, exchangePanel, new LuiOffset(16, 16, 96, 96), config.curr[exchange.currName].currCfg.icon, ColDb.LTD80);
					else
						cui.v2.CreateItemIcon(exchangePanel, new LuiOffset(16, 16, 96, 96), iconRedirect[exchange.itemShortname], exchange.itemSkin, ColDb.WhiteTrans80);

					float amount = isCurrency ? exchange.currAmount : exchange.itemAmount;
					string ratioString = amount < 1 ? sb.Clear().Append(exchange.amountReceived).Append(" : ").Append(amount).ToString() : sb.Clear().Append(amount).Append(" : ").Append(exchange.amountReceived).ToString();
					//Element: Ratio
					cui.v2.CreateText(exchangePanel, new LuiOffset(104, 16, 184, 96), 25, ColDb.GreenBg, ratioString, TextAnchor.MiddleCenter);

					//Element: To Icon
					FindAndCreateValidImage(cui, exchangePanel, new LuiOffset(192, 16, 272, 96), curr.Value.currCfg.icon, ColDb.LTD80);

					//Element: Taken Title
					cui.v2.CreateText(exchangePanel, new LuiOffset(396, 156, 526, 176), 15, ColDb.LTD60, Lang("Taken", player.UserIDString), TextAnchor.LowerCenter);

					//Element: Taken Input Background
					LUI.LuiContainer takenInputBackground = cui.v2.CreatePanel(exchangePanel, new LuiOffset(396, 111, 526, 153), ColDb.BlackTrans20);
					takenInputBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

					//Element: Taken Input Hint Icon
					if (isCurrency)
						FindAndCreateValidImage(cui, takenInputBackground, new LuiOffset(94, 6, 124, 36), config.curr[exchange.currName].currCfg.icon, ColDb.LTD15);
					else
						cui.v2.CreateItemIcon(takenInputBackground, new LuiOffset(94, 6, 124, 36), iconRedirect[exchange.itemShortname], exchange.itemSkin, ColDb.WhiteTrans40);

					//Element: Taken Input
					string command = sb.Clear().Append("ShoppyStock_UI exchange input ").Append(counter).ToString();
					string name = sb.Clear().Append("ShoppyStockUI_ExchangeTakenInput_").Append(counter).ToString();
					string stringAmount = sc.exchangeCache.currentId == counter ? sc.exchangeCache.amount.ToString() : "0";
					cui.v2.CreateInput(takenInputBackground, new LuiOffset(8, 0, 130, 42), ColDb.LightGray, stringAmount, 20, command, 0, true, CUI.Handler.FontTypes.RobotoCondensedRegular, TextAnchor.MiddleLeft, name: name).SetInputKeyboard(hudMenuInput: true);

					//Element: Taken Balance Title
					text = isCurrency ? Lang("Balance", player.UserIDString) : Lang("Inventory", player.UserIDString);
					cui.v2.CreateText(exchangePanel, new LuiOffset(396, 88, 526, 108), 15, ColDb.LTD60, text, TextAnchor.LowerCenter);

					//Element: Taken Balance
					if (isCurrency)
						stringAmount = FormatCurrency(config.curr[exchange.currName].currCfg, GetPlayerCurrency(player, exchange.currName), sb);
					else
					{
						int intAmount = 0;
						foreach (var item in player.inventory.containerMain.itemList)
							if (item.info.shortname == exchange.itemShortname && item.skin == exchange.itemSkin)
								intAmount += item.amount;
						string amountString = FormatAmount(curr.Value.currCfg, intAmount, sb);
						stringAmount = sb.Clear().Append(amountString).Append(' ').Append(exchange.itemDisplayName).ToString();
					}
					cui.v2.CreateText(exchangePanel, new LuiOffset(396, 62, 526, 88), 18, ColDb.LightGray, stringAmount, TextAnchor.UpperCenter);

					//Element: To Text
					cui.v2.CreateText(exchangePanel, new LuiOffset(526, 111, 586, 153), 20, ColDb.LTD80, Lang("To", player.UserIDString), TextAnchor.MiddleCenter);

					//Element: Given Title
					cui.v2.CreateText(exchangePanel, new LuiOffset(586, 156, 716, 176), 15, ColDb.LTD60, Lang("Given", player.UserIDString), TextAnchor.LowerCenter);

					//Element: Given Input Background
					LUI.LuiContainer givenInputBackground = cui.v2.CreatePanel(exchangePanel, new LuiOffset(586, 111, 716, 153), ColDb.BlackTrans20);
					givenInputBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

					//Element: Given Input Hint Icon
					FindAndCreateValidImage(cui, givenInputBackground, new LuiOffset(94, 6, 124, 36), curr.Value.currCfg.icon, ColDb.LTD15);

					//Element: Given Input
					name = sb.Clear().Append("ShoppyStockUI_ExchangeTakenOutput_").Append(counter).ToString();
					float amountRequired = isCurrency ? exchange.currAmount : exchange.itemAmount;
					stringAmount = sc.exchangeCache.currentId == counter ? (sc.exchangeCache.amount * (exchange.amountReceived / amountRequired)).ToString("0.##") : "0";
					LUI.LuiContainer givenInput = cui.v2.CreateText(givenInputBackground, new LuiOffset(8, 0, 111, 42), 20, ColDb.LightGray, stringAmount, TextAnchor.MiddleLeft, name);
					givenInput.SetTextFont(CUI.Handler.FontTypes.RobotoCondensedRegular);

					//Element: Given Balance Title
					cui.v2.CreateText(exchangePanel, new LuiOffset(586, 88, 716, 108), 15, ColDb.LTD60, Lang("Balance", player.UserIDString), TextAnchor.LowerCenter);

					//Element: Given Balance
					text = FormatCurrency(curr.Value.currCfg, GetPlayerCurrency(player, curr.Key), sb);
					cui.v2.CreateText(exchangePanel, new LuiOffset(586, 62, 716, 88), 18, ColDb.LightGray, text, TextAnchor.UpperCenter);

					//Element: Exchange Button
					command = sb.Clear().Append("ShoppyStock_UI exchange exchange ").Append(counter).ToString();
					LUI.LuiContainer exchangeButton = cui.v2.CreateButton(exchangePanel, new LuiOffset(491, 16, 621, 54), command, ColDb.GreenBg);
					exchangeButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

					//Element: Exchange Button Text
					cui.v2.CreateText(exchangeButton, LuiPosition.Full, LuiOffset.None, 20, ColDb.GreenText, Lang("Exchange", player.UserIDString), TextAnchor.MiddleCenter);
				}
			}
			Pool.FreeUnmanaged(ref sb);
			byte[] uiBytes = cui.v2.GetUiBytes();
			cui.Destroy("ShoppyStockUI_ElementCore", player);
			cui.v2.SendUiBytes(player, uiBytes);
		}

		private void UpdateExchangeCurrency(BasePlayer player)
		{
			using CUI cui = new CUI(CuiHandler);
			ShoppyCache sc = cache[player.userID];
			StringBuilder sb = Pool.Get<StringBuilder>();
			
			int counter = -1;
			foreach (var curr in config.curr)
			{
				if (!curr.Value.exchangeCfg.enabled) continue;
				foreach (var exchange in curr.Value.exchangeCfg.exchanges)
				{
					counter++;
					if (sc.exchangeCache.currentId != counter) continue;
					if (exchange.perm.Length > 0 && !permission.UserHasPermission(player.UserIDString, exchange.perm)) continue;
					string name = sb.Clear().Append("ShoppyStockUI_ExchangeTakenInput_").Append(counter).ToString();
					bool isCurrency = exchange.currName.Length > 0;
					if (isCurrency)
					{
						float amount = GetPlayerCurrency(player, exchange.currName);
						if (sc.exchangeCache.amount > amount)
							sc.exchangeCache.amount = Mathf.FloorToInt(amount);
					}
					else
					{
						int itemAmount = 0;
						foreach (var item in player.inventory.containerMain.itemList)
							if (item.info.shortname == exchange.itemShortname && item.skin == exchange.itemSkin)
								itemAmount += item.amount;
						if (sc.exchangeCache.amount > itemAmount)
							sc.exchangeCache.amount = itemAmount;
					}
					cui.v2.Update(name).SetInput(text: sc.exchangeCache.amount.ToString("0"), update: true);
					name = sb.Clear().Append("ShoppyStockUI_ExchangeTakenOutput_").Append(counter).ToString();
					float amountRequired = isCurrency ? exchange.currAmount : exchange.itemAmount;
					cui.v2.UpdateText(name, (sc.exchangeCache.amount * (exchange.amountReceived / amountRequired)).ToString("0.##"));
				}
			}
			Pool.FreeUnmanaged(ref sb);
			cui.v2.SendUi(player);
		}

		private void OpenAdminPage(BasePlayer player)
		{
			if (ConfigEditorCache.config == null)
				LoadDefaultEditingConfig();
			if (ConfigEditorCache.isBeingEdited)
			{
				Mess(player, "OnlyOneAdminCanEdit");
				return;
			}
			ConfigEditorCache.isBeingEdited = true;
			using CUI cui = new CUI(CuiHandler);
			LUI.LuiContainer adminUi = cui.v2.CreateParent(CUI.ClientPanels.HudMenu, LuiPosition.Full, "ShoppyAdminUI").AddCursor();
			//Element: Background Blur
			LUI.LuiContainer backgroundBlur = cui.v2.CreatePanel(adminUi, LuiPosition.Full, LuiOffset.None, ColDb.BlackTrans20);
			backgroundBlur.SetMaterial("assets/content/ui/uibackgroundblur.mat");

			//Element: Background Darker
			LUI.LuiContainer backgroundDarker = cui.v2.CreatePanel(adminUi, LuiPosition.Full, LuiOffset.None, ColDb.BlackTrans20);
			backgroundDarker.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Center Anchor
			LUI.LuiContainer centerAnchor = cui.v2.CreatePanel(adminUi, LuiPosition.MiddleCenter, LuiOffset.None, ColDb.Transparent);
			centerAnchor.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Main Panel
			LUI.LuiContainer mainPanel = cui.v2.CreatePanel(centerAnchor, LuiPosition.None, new LuiOffset(-550, -310, 550, 310), ColDb.DarkGray);
			mainPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Top Panel
			LUI.LuiContainer topPanel = cui.v2.CreatePanel(mainPanel, LuiPosition.None, new LuiOffset(0, 578, 1100, 620), ColDb.LTD5);
			topPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Title
			LUI.LuiContainer title = cui.v2.CreateText(topPanel, LuiPosition.None, new LuiOffset(10, 0, 512, 42), 24, ColDb.LightGray, Lang("AdminMenuTitle", player.UserIDString), TextAnchor.MiddleLeft);

			//Element: Unsaved Changes
			string color = ConfigEditorCache.edited ? ColDb.RedBg : ColDb.Transparent;
			LUI.LuiContainer unsavedChanges = cui.v2.CreateText(topPanel, LuiPosition.None, new LuiOffset(656, 0, 1056, 42), 18, color, Lang("UnsavedChanges", player.UserIDString), TextAnchor.MiddleRight, "ShoppyAdminUI_UnsavedChanges");

			//Element: Exit Button
			LUI.LuiContainer exitButton = cui.v2.CreateButton(topPanel, LuiPosition.None, new LuiOffset(1064, 6, 1094, 36), "ShoppyAdmin_UI close", ColDb.RedBg);
			exitButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Exit Icon
			LUI.LuiContainer exitIcon = cui.v2.CreateSprite(exitButton, LuiPosition.None, new LuiOffset(6, 6, 24, 24), "assets/icons/close.png", ColDb.RedText);

			//Element: Settings Menu
			LUI.LuiContainer settingsMenu = cui.v2.CreateEmptyContainer(mainPanel, "ShoppyAdminUI_SettingsMenu", true).SetOffset(new LuiOffset(0, 0, 1100, 578));

			//Element: Save Button
			LUI.LuiContainer saveButton = cui.v2.CreateButton(mainPanel, LuiPosition.None, new LuiOffset(968, 32, 1068, 82), "ShoppyAdmin_UI save", ColDb.GreenBg);
			saveButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Save Button Text
			LUI.LuiContainer saveButtonText = cui.v2.CreateText(saveButton, LuiPosition.Full, LuiOffset.None, 18, ColDb.GreenText, Lang("SaveConfig", player.UserIDString), TextAnchor.MiddleCenter);

			//Element: Revert Button
			LUI.LuiContainer revertButton = cui.v2.CreateButton(mainPanel, LuiPosition.None, new LuiOffset(852, 32, 952, 82), "ShoppyAdmin_UI revert", ColDb.RedBg);
			revertButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Revert Button Text
			LUI.LuiContainer revertButtonText = cui.v2.CreateText(revertButton, LuiPosition.Full, LuiOffset.None, 18, ColDb.RedText, Lang("RevertChanges", player.UserIDString), TextAnchor.MiddleCenter);

			//Element: Back Button
			LUI.LuiContainer backButton = cui.v2.CreateButton(mainPanel, LuiPosition.None, new LuiOffset(32, 32, 132, 82), "ShoppyAdmin_UI goBack", ColDb.RedBg);
			backButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Back Button Text
			LUI.LuiContainer backButtonText = cui.v2.CreateText(backButton, LuiPosition.Full, LuiOffset.None, 18, ColDb.RedText, Lang("GoBack", player.UserIDString), TextAnchor.MiddleCenter);
			
			cui.v2.SendUi(player);
			OpenAdminSettingsPage(player);
		}

		private void OpenAdminSettingsPage(BasePlayer player)
		{
			object parentObject = ConfigEditorCache.config;
			if (ConfigEditorCache.enterTree.Count > 0)
				parentObject = FindAdminParentPage();
			if (parentObject == null)
			{
				Puts("Couldnt find config parent. Reload plugin to make it work again!");
				return;
			}
			
			using CUI cui = new CUI(CuiHandler);

			//Element: Update Section
			LUI.LuiContainer updateSection = cui.v2.CreateEmptyContainer("ShoppyAdminUI_SettingsMenu", "ShoppyAdminUI_UpdateSection", true).SetAnchors(LuiPosition.Full).SetDestroy("ShoppyAdminUI_UpdateSection");
			if (ConfigEditorCache.edited)
				cui.v2.UpdateText("ShoppyAdminUI_UnsavedChanges", Lang("UnsavedChanges", player.UserIDString), color: ColDb.RedBg);
			else
				cui.v2.UpdateText("ShoppyAdminUI_UnsavedChanges", Lang("UnsavedChanges", player.UserIDString), color: ColDb.Transparent);

			var fields = parentObject.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

			string objectName = parentObject.GetType().Name;
			int additionalButtons = customMethodButtons.TryGetValue(objectName, out var elementList) ? elementList.Count : 0;
			
			//Element: Settings Scroll
			LuiScrollbar updateSection_Vertical = new LuiScrollbar()
			{
				autoHide = true,
				size = 4,
				handleColor = ColDb.LTD15,
				highlightColor = ColDb.LTD20,
				pressedColor = ColDb.LTD10,
				trackColor = ColDb.BlackTrans10
			};

			int scrollHeight = (fields.Length + additionalButtons + 1) * 42 + 100;
			if (scrollHeight < 514)
				scrollHeight = 514;

			LUI.LuiContainer settingsScroll = cui.v2.CreateScrollView(updateSection, LuiPosition.None, new LuiOffset(32, 32, 1086, 546), true, false, ScrollRect.MovementType.Elastic, 0.1f, true, 0.1f, 25f, updateSection_Vertical, default);
			settingsScroll.SetScrollContent(new LuiPosition(0, 1, 1, 1), new LuiOffset(0, -scrollHeight, 0, 0));

			//Element: Panel Holder
			LUI.LuiContainer panelHolder = cui.v2.CreatePanel(settingsScroll, LuiPosition.Full, LuiOffset.None, ColDb.Transparent);
			panelHolder.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Settings Path
			StringBuilder sb = Pool.Get<StringBuilder>();
			sb.Clear().Append("START<size=20>");
			foreach (var page in ConfigEditorCache.enterTree)
				sb.Append(" > ").Append(page.Replace("d--", "").Replace("l--", ""));
			sb.Append("</size>");
			LUI.LuiContainer settingsPath = cui.v2.CreateText(settingsScroll, LuiPosition.None, new LuiOffset(0, scrollHeight - 34, 1033, scrollHeight), 28, ColDb.LightGray, sb.ToString(), TextAnchor.UpperLeft);
			scrollHeight -= 78;

			if (additionalButtons > 0)
			{
				foreach (var button in elementList)
				{
					//Element: Option
					LUI.LuiContainer option = cui.v2.CreateEmptyContainer(settingsScroll, add: true).SetOffset(new LuiOffset(10, scrollHeight, 1022, scrollHeight + 36));

					//Element: Name
					string buttonText = Lang(sb.Clear().Append("CustomButton_").Append(button).ToString(), player.UserIDString);
					cui.v2.CreateText(option, LuiPosition.None, new LuiOffset(0, 0, 416, 36), 15, ColDb.LTD80, buttonText, TextAnchor.MiddleLeft);
					
					//Element: Option Button
					sb.Clear().Append("ShoppyAdmin_UI customButton ").Append(button);
					LUI.LuiContainer optionButton = cui.v2.CreateButton(option, LuiPosition.None, new LuiOffset(416, 0, 566, 36), sb.ToString(), ColDb.GreenBg);
					optionButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

					//Element: Option Button Text
					LUI.LuiContainer optionButtonText = cui.v2.CreateText(optionButton, LuiPosition.Full, LuiOffset.None, 25, ColDb.GreenText, Lang("GoThere", player.UserIDString), TextAnchor.MiddleCenter);

					scrollHeight -= 44;
				}
			}
			
			foreach (var field in fields)
			{
				JsonPropertyAttribute jsonProp = field.GetCustomAttribute<JsonPropertyAttribute>();
				string elementName = jsonProp != null ? jsonProp.PropertyName : field.Name;
				object value = field.GetValue(parentObject);
				Type valueType = value.GetType();
				//Element: Option
				LUI.LuiContainer option = cui.v2.CreateEmptyContainer(settingsScroll, add: true).SetOffset(new LuiOffset(10, scrollHeight, 1022, scrollHeight + 36));

				//Element: Name
				cui.v2.CreateText(option, LuiPosition.None, new LuiOffset(0, 0, 416, 36), 15, ColDb.LTD80, elementName, TextAnchor.MiddleLeft);
				if (valueType == typeof(string))
				{
					//Element: Option Input Background
					LUI.LuiContainer optionInputBackground = cui.v2.CreatePanel(option, LuiPosition.None, new LuiOffset(416, 0, 1012, 36), ColDb.BlackTrans20);
					optionInputBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

					//Element: Option Input
					sb.Clear().Append("ShoppyAdmin_UI stringInput ").Append(field.Name);
					LUI.LuiContainer optionInput = cui.v2.CreateInput(optionInputBackground, LuiPosition.None, new LuiOffset(8, 0, 588, 36), ColDb.LightGray, value.ToString(), 15, sb.ToString(), 0, true, CUI.Handler.FontTypes.RobotoCondensedRegular, TextAnchor.MiddleCenter);
					optionInput.SetInputKeyboard(hudMenuInput: true);
				}
				else if (typeof(IDictionary).IsAssignableFrom(valueType))
				{
					//Element: Option Button
					sb.Clear().Append("ShoppyAdmin_UI editDictionary ").Append(field.Name);
					LUI.LuiContainer optionButton = cui.v2.CreateButton(option, LuiPosition.None, new LuiOffset(416, 0, 566, 36), sb.ToString(), ColDb.GreenBg);
					optionButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

					//Element: Option Button Text
					LUI.LuiContainer optionButtonText = cui.v2.CreateText(optionButton, LuiPosition.Full, LuiOffset.None, 25, ColDb.GreenText, Lang("EditDict", player.UserIDString), TextAnchor.MiddleCenter);
				}
				else if (typeof(IList).IsAssignableFrom(valueType))
				{
					//Element: Option Button
					sb.Clear().Append("ShoppyAdmin_UI editList ").Append(field.Name);
					LUI.LuiContainer optionButton = cui.v2.CreateButton(option, LuiPosition.None, new LuiOffset(416, 0, 566, 36), sb.ToString(), ColDb.GreenBg);
					optionButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

					//Element: Option Button Text
					LUI.LuiContainer optionButtonText = cui.v2.CreateText(optionButton, LuiPosition.Full, LuiOffset.None, 25, ColDb.GreenText, Lang("EditList", player.UserIDString), TextAnchor.MiddleCenter);
				}
				else if (valueType == typeof(int) || valueType == typeof(float))
				{
					//Element: Option Input Background
					LUI.LuiContainer optionInputBackground = cui.v2.CreatePanel(option, LuiPosition.None, new LuiOffset(416, 0, 566, 36), ColDb.BlackTrans20);
					optionInputBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

					//Element: Option Input
					string command = valueType == typeof(int) ? sb.Clear().Append("ShoppyAdmin_UI intInput ").Append(field.Name).ToString() : sb.Clear().Append("ShoppyAdmin_UI floatInput ").Append(field.Name).ToString();
					LUI.LuiContainer optionInput = cui.v2.CreateInput(optionInputBackground, LuiPosition.None, new LuiOffset(8, 0, 142, 36), ColDb.LightGray, value.ToString(), 15, command, 0, true, CUI.Handler.FontTypes.RobotoCondensedRegular, TextAnchor.MiddleCenter);
					optionInput.SetInputKeyboard(hudMenuInput: true);
				}
				else if (valueType == typeof(ulong))
				{
					//Element: Option Input Background
					LUI.LuiContainer optionInputBackground = cui.v2.CreatePanel(option, LuiPosition.None, new LuiOffset(416, 0, 566, 36), ColDb.BlackTrans20);
					optionInputBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

					//Element: Option Input
					sb.Clear().Append("ShoppyAdmin_UI ulongInput ").Append(field.Name);
					LUI.LuiContainer optionInput = cui.v2.CreateInput(optionInputBackground, LuiPosition.None, new LuiOffset(8, 0, 142, 36), ColDb.LightGray, value.ToString(), 15, sb.ToString(), 0, true, CUI.Handler.FontTypes.RobotoCondensedRegular, TextAnchor.MiddleCenter);
					optionInput.SetInputKeyboard(hudMenuInput: true);
				}
				else if (valueType == typeof(bool))
				{
					//Element: Bool Button
					sb.Clear().Append("ShoppyAdmin_UI boolButton ").Append(field.Name);
					LUI.LuiContainer boolButton = cui.v2.CreateButton(option, LuiPosition.None, new LuiOffset(416, 0, 452, 36), sb.ToString(), ColDb.BlackTrans20);
					boolButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

					//Element: Bool Button Image
					bool boolValue = (bool)value;
					string color = boolValue ? ColDb.GreenBg : ColDb.RedBg;
					string icon = boolValue ? "assets/icons/vote_up.png" : "assets/icons/vote_down.png";
					LUI.LuiContainer boolButtonImage = cui.v2.CreateSprite(boolButton, LuiPosition.None, new LuiOffset(2, 2, 34, 34), icon, color);
				}
				else if (valueType.IsClass)
				{
					//Element: Option Button
					sb.Clear().Append("ShoppyAdmin_UI editClass ").Append(field.Name);
					LUI.LuiContainer optionButton = cui.v2.CreateButton(option, LuiPosition.None, new LuiOffset(416, 0, 566, 36), sb.ToString(), ColDb.GreenBg);
					optionButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

					//Element: Option Button Text
					LUI.LuiContainer optionButtonText = cui.v2.CreateText(optionButton, LuiPosition.Full, LuiOffset.None, 25, ColDb.GreenText, Lang("GoThere", player.UserIDString), TextAnchor.MiddleCenter);
				}
				scrollHeight -= 44;
			}
			cui.v2.SendUi(player);
			cui.Destroy("ShoppyAdminUI_SubPage", player);
		}

		private void OpenSubAdminPage(BasePlayer player)
		{
			using CUI cui = new CUI(CuiHandler);
			LUI.LuiContainer adminUi = cui.v2.CreateParent("ShoppyAdminUI", LuiPosition.Full, "ShoppyAdminUI_SubPage").SetDestroy("ShoppyAdminUI_SubPage");
			if (ConfigEditorCache.edited)
				cui.v2.UpdateText("ShoppyAdminUI_UnsavedChanges", Lang("UnsavedChanges", player.UserIDString), color: ColDb.RedBg);
			//Element: Background Blur
			LUI.LuiContainer backgroundBlur = cui.v2.CreatePanel(adminUi, LuiPosition.Full, LuiOffset.None, ColDb.BlackTrans20);
			backgroundBlur.SetMaterial("assets/content/ui/uibackgroundblur.mat");

			//Element: Background Darker
			LUI.LuiContainer backgroundDarker = cui.v2.CreatePanel(adminUi, LuiPosition.Full, LuiOffset.None, ColDb.BlackTrans20);
			backgroundDarker.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Center Anchor
			LUI.LuiContainer centerAnchor = cui.v2.CreatePanel(adminUi, LuiPosition.MiddleCenter, LuiOffset.None, ColDb.Transparent);
			centerAnchor.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Main Panel
			LUI.LuiContainer mainPanel = cui.v2.CreatePanel(centerAnchor, LuiPosition.None, new LuiOffset(-270, -225, 270, 225), ColDb.DarkGray, "ShoppyAdminUI_SubPageMain");
			mainPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Top Panel
			LUI.LuiContainer topPanel = cui.v2.CreatePanel(mainPanel, LuiPosition.None, new LuiOffset(0, 418, 540, 450), ColDb.LTD5);
			topPanel.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Top Title
			LUI.LuiContainer topTitle = cui.v2.CreateText(topPanel, LuiPosition.None, new LuiOffset(10, 0, 540, 32), 20, ColDb.LightGray, "", TextAnchor.MiddleLeft, "ShoppyAdminUI_SubPageTitle");

			//Element: Go Back Button
			LUI.LuiContainer goBackButton = cui.v2.CreateButton(mainPanel, LuiPosition.None, new LuiOffset(193, 24, 347, 60), "ShoppyAdmin_UI goBackSub", ColDb.RedBg);
			goBackButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Go Back Button Text
			LUI.LuiContainer goBackButtonText = cui.v2.CreateText(goBackButton, LuiPosition.Full, LuiOffset.None, 22, ColDb.RedText, Lang("GoBack", player.UserIDString), TextAnchor.MiddleCenter);
			cui.v2.SendUi(player);
		}

		private void DrawAdminListOptions(BasePlayer player)
		{
			using CUI cui = new CUI(CuiHandler);
			StringBuilder sb = Pool.Get<StringBuilder>();
			//Element: List Section
			LUI.LuiContainer listSection = cui.v2.CreateEmptyContainer("ShoppyAdminUI_SubPageMain", "ShoppyAdminUI_SubPageUpdate", true).SetOffset(new LuiOffset(0, 60, 540, 418)).SetDestroy("ShoppyAdminUI_SubPageUpdate");
			cui.v2.UpdateText("ShoppyAdminUI_SubPageTitle", Lang("ListEditor", player.UserIDString));

			IList parentObject = FindAdminParentPage() as IList;
			
			//Element: List Scroll
			LuiScrollbar listSection_Vertical = new LuiScrollbar()
			{
				autoHide = true,
				size = 4,
				handleColor = ColDb.LTD15,
				highlightColor = ColDb.LTD20,
				pressedColor = ColDb.LTD10,
				trackColor = ColDb.BlackTrans10
			};

			int scrollHeight = (parentObject.Count + 1) * 38;
			if (scrollHeight < 326)
				scrollHeight = 326;

			LUI.LuiContainer listScroll = cui.v2.CreateScrollView(listSection, LuiPosition.None, new LuiOffset(16, 16, 522, 342), true, false, ScrollRect.MovementType.Elastic, 0.1f, true, 0.1f, 25f, listSection_Vertical, default);
			listScroll.SetScrollContent(new LuiPosition(0, 1, 1, 1), new LuiOffset(0, -scrollHeight, 0, 0));

			//Element: Background Holder
			LUI.LuiContainer backgroundHolder = cui.v2.CreatePanel(listScroll, LuiPosition.Full, LuiOffset.None, ColDb.Transparent);
			backgroundHolder.SetMaterial("assets/content/ui/namefontmaterial.mat");

			scrollHeight -= 30;
			int counter = 0;
			bool needToInput = true;
			string inputType = "";
			foreach (var field in parentObject)
			{
				Type valueType = field.GetType();
				if (valueType == typeof(string))
				{
					inputType = "string";
					//Element: List Input Background
					LUI.LuiContainer listInputBackground = cui.v2.CreatePanel(listScroll, LuiPosition.None, new LuiOffset(16, scrollHeight, 466, scrollHeight + 30), ColDb.BlackTrans20);
					listInputBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

					//Element: List Input
					sb.Clear().Append("ShoppyAdmin_UI stringInput ").Append(counter);
					LUI.LuiContainer listInput = cui.v2.CreateInput(listInputBackground, LuiPosition.Full, LuiOffset.None, ColDb.LightGray, field.ToString(), 15, sb.ToString(), 0, true, CUI.Handler.FontTypes.RobotoCondensedRegular, TextAnchor.MiddleCenter);
				}
				else if (typeof(IDictionary).IsAssignableFrom(valueType))
				{
					//Element: Option Button
					sb.Clear().Append("ShoppyAdmin_UI editDictionary ").Append(counter);
					LUI.LuiContainer optionButton = cui.v2.CreateButton(listScroll, LuiPosition.None, new LuiOffset(16, scrollHeight, 466, scrollHeight + 30), sb.ToString(), ColDb.GreenBg);
					optionButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

					//Element: Option Button Text
					LUI.LuiContainer optionButtonText = cui.v2.CreateText(optionButton, LuiPosition.Full, LuiOffset.None, 25, ColDb.GreenText, Lang("EditDict", player.UserIDString), TextAnchor.MiddleCenter);
					needToInput = false;
				}
				else if (typeof(IList).IsAssignableFrom(valueType))
				{
					//Element: Option Button
					sb.Clear().Append("ShoppyAdmin_UI editList ").Append(counter);
					LUI.LuiContainer optionButton = cui.v2.CreateButton(listScroll, LuiPosition.None, new LuiOffset(16, scrollHeight, 466, scrollHeight + 30), sb.ToString(), ColDb.GreenBg);
					optionButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

					//Element: Option Button Text
					LUI.LuiContainer optionButtonText = cui.v2.CreateText(optionButton, LuiPosition.Full, LuiOffset.None, 25, ColDb.GreenText, Lang("EditList", player.UserIDString), TextAnchor.MiddleCenter);
					needToInput = false;
				}
				else if (valueType == typeof(int) || valueType == typeof(float))
				{
					inputType = valueType == typeof(int) ? "int" : "float";
					//Element: List Input Background
					LUI.LuiContainer listInputBackground = cui.v2.CreatePanel(listScroll, LuiPosition.None, new LuiOffset(16, scrollHeight, 466, scrollHeight + 30), ColDb.BlackTrans20);
					listInputBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

					//Element: List Input
					string command = valueType == typeof(int) ? sb.Clear().Append("ShoppyAdmin_UI intInput ").Append(counter).ToString() : sb.Clear().Append("ShoppyAdmin_UI floatInput ").Append(counter).ToString();
					LUI.LuiContainer listInput = cui.v2.CreateInput(listInputBackground, LuiPosition.Full, LuiOffset.None, ColDb.LightGray, field.ToString(), 15, command, 0, true, CUI.Handler.FontTypes.RobotoCondensedRegular, TextAnchor.MiddleCenter);
				}
				else if (valueType == typeof(ulong))
				{
					inputType = "ulong";
					//Element: List Input Background
					LUI.LuiContainer listInputBackground = cui.v2.CreatePanel(listScroll, LuiPosition.None, new LuiOffset(16, scrollHeight, 466, scrollHeight + 30), ColDb.BlackTrans20);
					listInputBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

					//Element: List Input
					sb.Clear().Append("ShoppyAdmin_UI ulongInput ").Append(counter);
					LUI.LuiContainer listInput = cui.v2.CreateInput(listInputBackground, LuiPosition.Full, LuiOffset.None, ColDb.LightGray, field.ToString(), 15, sb.ToString(), 0, true, CUI.Handler.FontTypes.RobotoCondensedRegular, TextAnchor.MiddleCenter);
				}
				else if (valueType.IsClass)
				{
					//Element: Option Button
					sb.Clear().Append("ShoppyAdmin_UI editClass ").Append(counter);
					LUI.LuiContainer optionButton = cui.v2.CreateButton(listScroll, LuiPosition.None, new LuiOffset(16, scrollHeight, 466, scrollHeight + 30), sb.ToString(), ColDb.GreenBg);
					optionButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

					//Element: Option Button Text
					LUI.LuiContainer optionButtonText = cui.v2.CreateText(optionButton, LuiPosition.Full, LuiOffset.None, 25, ColDb.GreenText, Lang("GoThere", player.UserIDString), TextAnchor.MiddleCenter);
					needToInput = false;
				}

				sb.Clear().Append("ShoppyAdmin_UI listRemove ").Append(counter);
				//Element: List Remove Button
				LUI.LuiContainer listRemoveButton = cui.v2.CreateButton(listScroll, LuiPosition.None, new LuiOffset(466, scrollHeight, 496, scrollHeight + 30), sb.ToString(), ColDb.Transparent);
				listRemoveButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Remove Button Image
				LUI.LuiContainer removeButtonImage = cui.v2.CreateSprite(listRemoveButton, LuiPosition.None, new LuiOffset(5, 5, 25, 25), "assets/icons/clear.png", ColDb.RedBg);
				scrollHeight -= 38;
				counter++;
			}
			
			//Element: List New Input Background
			LUI.LuiContainer listNewInputBackground = cui.v2.CreatePanel(listScroll, LuiPosition.None, new LuiOffset(16, scrollHeight, 466, scrollHeight + 30), ColDb.BlackTrans20);
			listNewInputBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: List New Input
			string inputText = needToInput ? Lang("InputAndClick", player.UserIDString) : Lang("ClickPlus", player.UserIDString);
			string inputCommand = "";
			if (inputType == "string")
				inputCommand = "ShoppyAdmin_UI stringInput newRecord";
			else if (inputType == "int")
				inputCommand = "ShoppyAdmin_UI intInput newRecord";
			else if (inputType == "float")
				inputCommand = "ShoppyAdmin_UI floatInput newRecord";
			else if (inputType == "ulong")
				inputCommand = "ShoppyAdmin_UI ulongInput newRecord";
			LUI.LuiContainer listNewInput = cui.v2.CreateInput(listNewInputBackground, LuiPosition.Full, LuiOffset.None, ColDb.LightGray, inputText, 15, inputCommand, 0, true, CUI.Handler.FontTypes.RobotoCondensedRegular, TextAnchor.MiddleCenter);

			//Element: List Add Button
			LUI.LuiContainer listAddButton = cui.v2.CreateButton(listNewInputBackground, LuiPosition.None, new LuiOffset(450, 0, 480, 30), "ShoppyAdmin_UI listAdd", ColDb.Transparent);
			listAddButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Add Button Image
			LUI.LuiContainer addButtonImage = cui.v2.CreateSprite(listAddButton, LuiPosition.None, new LuiOffset(5, 5, 25, 25), "assets/icons/health.png", ColDb.GreenBg);

			cui.v2.SendUi(player);
		}

		private void DrawAdminDictionaryOptions(BasePlayer player)
		{
			using CUI cui = new CUI(CuiHandler);
			StringBuilder sb = Pool.Get<StringBuilder>();
			//Element: Dictionary Section
			LUI.LuiContainer dictionarySection = cui.v2.CreateEmptyContainer("ShoppyAdminUI_SubPageMain", "ShoppyAdminUI_SubPageUpdate", true).SetOffset(new LuiOffset(0, 60, 540, 418)).SetDestroy("ShoppyAdminUI_SubPageUpdate");
			cui.v2.UpdateText("ShoppyAdminUI_SubPageTitle", Lang("DictionaryEditor", player.UserIDString));
			
			IDictionary parentObject = FindAdminParentPage() as IDictionary;
			//Element: Dictionary Scroll
			LuiScrollbar dictionarySection_Vertical = new LuiScrollbar()
			{
				autoHide = true,
				size = 4,
				handleColor = ColDb.LTD15,
				highlightColor = ColDb.LTD20,
				pressedColor = ColDb.LTD10,
				trackColor = ColDb.BlackTrans10
			};

			int scrollHeight = (parentObject.Count + 1) * 38;
			if (scrollHeight < 326)
				scrollHeight = 326;

			LUI.LuiContainer dictionaryScroll = cui.v2.CreateScrollView(dictionarySection, LuiPosition.None, new LuiOffset(16, 16, 522, 342), true, false, ScrollRect.MovementType.Elastic, 0.1f, true, 0.1f, 25f, dictionarySection_Vertical, default);
			dictionaryScroll.SetScrollContent(new LuiPosition(0, 1, 1, 1), new LuiOffset(0, -scrollHeight, 0, 0));

			//Element: Background Holder
			LUI.LuiContainer backgroundHolder = cui.v2.CreatePanel(dictionaryScroll, LuiPosition.Full, LuiOffset.None, ColDb.Transparent);
			backgroundHolder.SetMaterial("assets/content/ui/namefontmaterial.mat");

			scrollHeight -= 30;
			int counter = 0;
			Type valueType = parentObject.GetType().GetGenericArguments()[1];
			Type keyType = parentObject.GetType().GetGenericArguments()[0];
			string keyCommand = "";
			foreach (DictionaryEntry record in parentObject)
			{
				//Element: Record Section
				LUI.LuiContainer recordSection = cui.v2.CreateEmptyContainer(dictionaryScroll, add: true).SetOffset(new LuiOffset(16, scrollHeight, 496, scrollHeight + 30));

				//Element: Key Input Background
				LUI.LuiContainer keyInputBackground = cui.v2.CreatePanel(recordSection, LuiPosition.None, new LuiOffset(0, 0, 210, 30), ColDb.BlackTrans20);
				keyInputBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Key Input
				string keyString = record.Key.ToString();
				keyType = record.Key.GetType();
				if (keyType == typeof(string))
					keyCommand = sb.Clear().Append("ShoppyAdmin_UI dictKeyInput string ").Append(keyString).ToString();
				else if (keyType == typeof(float))
					keyCommand = sb.Clear().Append("ShoppyAdmin_UI dictKeyInput float ").Append(keyString).ToString();
				else if (keyType == typeof(ulong))
					keyCommand = sb.Clear().Append("ShoppyAdmin_UI dictKeyInput ulong ").Append(keyString).ToString();
				else if (keyType == typeof(int))
					keyCommand = sb.Clear().Append("ShoppyAdmin_UI dictKeyInput int ").Append(keyString).ToString();
				LUI.LuiContainer keyInput = cui.v2.CreateInput(keyInputBackground, LuiPosition.Full, LuiOffset.None, ColDb.LightGray, keyString, 15, keyCommand, 0, true, CUI.Handler.FontTypes.RobotoCondensedRegular, TextAnchor.MiddleCenter);

				//Element: Divider
				LUI.LuiContainer divider = cui.v2.CreateText(recordSection, LuiPosition.None, new LuiOffset(210, 0, 240, 30), 25, ColDb.LightGray, "-", TextAnchor.MiddleCenter);

				valueType = record.Value.GetType();
				
				if (valueType == typeof(string))
				{
					//Element: Value Input Background
					LUI.LuiContainer valueInputBackground = cui.v2.CreatePanel(recordSection, LuiPosition.None, new LuiOffset(240, 0, 450, 30), ColDb.BlackTrans20);
					valueInputBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

					//Element: Value Input
					sb.Clear().Append("ShoppyAdmin_UI stringInput ").Append(keyString);
					LUI.LuiContainer valueInput = cui.v2.CreateInput(valueInputBackground, LuiPosition.Full, LuiOffset.None, ColDb.LightGray, record.Value.ToString(), 15, sb.ToString(), 0, true, CUI.Handler.FontTypes.RobotoCondensedRegular, TextAnchor.MiddleCenter);
				}
				else if (typeof(IDictionary).IsAssignableFrom(valueType))
				{
					//Element: Value Edit Button
					sb.Clear().Append("ShoppyAdmin_UI editDictionary ").Append(keyString);
					LUI.LuiContainer valueEditButton = cui.v2.CreateButton(recordSection, LuiPosition.None, new LuiOffset(239, 0, 450, 30), sb.ToString(), ColDb.GreenBg);
					valueEditButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

					//Element: Edit Button Text
					LUI.LuiContainer editButtonText = cui.v2.CreateText(valueEditButton, LuiPosition.Full, LuiOffset.None, 15, ColDb.GreenText, Lang("EditDict", player.UserIDString), TextAnchor.MiddleCenter);
				}
				else if (typeof(IList).IsAssignableFrom(valueType))
				{
					//Element: Value Edit Button
					sb.Clear().Append("ShoppyAdmin_UI editList ").Append(keyString);
					LUI.LuiContainer valueEditButton = cui.v2.CreateButton(recordSection, LuiPosition.None, new LuiOffset(239, 0, 450, 30), sb.ToString(), ColDb.GreenBg);
					valueEditButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

					//Element: Edit Button Text
					LUI.LuiContainer editButtonText = cui.v2.CreateText(valueEditButton, LuiPosition.Full, LuiOffset.None, 15, ColDb.GreenText, Lang("EditList", player.UserIDString), TextAnchor.MiddleCenter);
				}
				else if (valueType == typeof(int) || valueType == typeof(float))
				{
					//Element: Value Input Background
					LUI.LuiContainer valueInputBackground = cui.v2.CreatePanel(recordSection, LuiPosition.None, new LuiOffset(240, 0, 450, 30), ColDb.BlackTrans20);
					valueInputBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");
					string command = valueType == typeof(int) ? sb.Clear().Append("ShoppyAdmin_UI intInput ").Append(keyString).ToString() : sb.Clear().Append("ShoppyAdmin_UI floatInput ").Append(keyString).ToString();
					LUI.LuiContainer valueInput = cui.v2.CreateInput(valueInputBackground, LuiPosition.Full, LuiOffset.None, ColDb.LightGray, record.Value.ToString(), 15, command, 0, true, CUI.Handler.FontTypes.RobotoCondensedRegular, TextAnchor.MiddleCenter);
				}
				else if (valueType == typeof(ulong))
				{
					//Element: Value Input Background
					LUI.LuiContainer valueInputBackground = cui.v2.CreatePanel(recordSection, LuiPosition.None, new LuiOffset(240, 0, 450, 30), ColDb.BlackTrans20);
					valueInputBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");
					sb.Clear().Append("ShoppyAdmin_UI ulongInput ").Append(keyString);
					LUI.LuiContainer valueInput = cui.v2.CreateInput(valueInputBackground, LuiPosition.Full, LuiOffset.None, ColDb.LightGray, record.Value.ToString(), 15, sb.ToString(), 0, true, CUI.Handler.FontTypes.RobotoCondensedRegular, TextAnchor.MiddleCenter);
				}
				else if (valueType == typeof(bool))
				{
					//Element: Value Edit Button
					sb.Clear().Append("ShoppyAdmin_UI boolButton ").Append(keyString);
					bool boolValue = (bool)record.Value;
					string color = boolValue ? ColDb.GreenBg : ColDb.RedBg;
					string textColor = boolValue ? ColDb.GreenText : ColDb.RedText;
					string text = boolValue ? "TRUE" : "FALSE";
					LUI.LuiContainer valueEditButton = cui.v2.CreateButton(recordSection, LuiPosition.None, new LuiOffset(239, 0, 450, 30), sb.ToString(), color);
					valueEditButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

					//Element: Edit Button Text
					LUI.LuiContainer editButtonText = cui.v2.CreateText(valueEditButton, LuiPosition.Full, LuiOffset.None, 15, textColor, text, TextAnchor.MiddleCenter);

				}
				else if (valueType.IsClass)
				{
					//Element: Value Edit Button
					sb.Clear().Append("ShoppyAdmin_UI editClass ").Append(keyString);
					LUI.LuiContainer valueEditButton = cui.v2.CreateButton(recordSection, LuiPosition.None, new LuiOffset(239, 0, 450, 30), sb.ToString(), ColDb.GreenBg);
					valueEditButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

					//Element: Edit Button Text
					LUI.LuiContainer editButtonText = cui.v2.CreateText(valueEditButton, LuiPosition.Full, LuiOffset.None, 15, ColDb.GreenText, Lang("GoThere", player.UserIDString), TextAnchor.MiddleCenter);
				}

				//Element: Remove Record Button
				sb.Clear().Append("ShoppyAdmin_UI dictRemove ").Append(keyString);
				LUI.LuiContainer removeRecordButton = cui.v2.CreateButton(recordSection, LuiPosition.None, new LuiOffset(450, 0, 480, 30), sb.ToString(), ColDb.Transparent);
				removeRecordButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Remove Button Image
				LUI.LuiContainer removeButtonImage = cui.v2.CreateSprite(removeRecordButton, LuiPosition.None, new LuiOffset(5, 5, 25, 25), "assets/icons/clear.png", ColDb.RedBg);
				scrollHeight -= 38;
				counter++;
			}
			
			//Element: Add Record Section
			LUI.LuiContainer recordAddSection = cui.v2.CreateEmptyContainer(dictionaryScroll, add: true).SetOffset(new LuiOffset(16, scrollHeight, 496, scrollHeight + 30));

			bool canEditValue = false;
			if (valueType == typeof(int) || valueType == typeof(float) || valueType == typeof(ulong) || valueType == typeof(string))
			{
				canEditValue = true;
				//Element: Key Add Input Background
				LUI.LuiContainer valueAddBackground = cui.v2.CreatePanel(recordAddSection, LuiPosition.None, new LuiOffset(239, 0, 450, 30), ColDb.BlackTrans20);
				valueAddBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");
				string valueCommand = "";
				if (valueType == typeof(string))
					valueCommand = "ShoppyAdmin_UI stringInput newRecord";
				if (valueType == typeof(float))
					valueCommand = "ShoppyAdmin_UI floatInput newRecord";
				if (valueType == typeof(ulong))
					valueCommand = "ShoppyAdmin_UI ulongInput newRecord";
				if (valueType == typeof(int))
					valueCommand = "ShoppyAdmin_UI intInput newRecord";

				//Element: Key Add Input
				LUI.LuiContainer valueAddInput = cui.v2.CreateInput(valueAddBackground, LuiPosition.Full, LuiOffset.None, ColDb.LightGray, Lang("InputKeyAndValue_Value", player.UserIDString), 15, valueCommand, 0, true, CUI.Handler.FontTypes.RobotoCondensedRegular, TextAnchor.MiddleCenter);
			}
			else
			{
				//Element: Value Locked Button
				LUI.LuiContainer valueLockedButton = cui.v2.CreateButton(recordAddSection, LuiPosition.None, new LuiOffset(239, 0, 450, 30), "", ColDb.LTD10);
				valueLockedButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

				//Element: Locked Button Text
				LUI.LuiContainer lockedButtonText = cui.v2.CreateText(valueLockedButton, LuiPosition.Full, LuiOffset.None, 15, ColDb.LTD60, Lang("AddFirst", player.UserIDString), TextAnchor.MiddleCenter);
			}
			
			//Element: Key Add Input Background
			LUI.LuiContainer keyAddInputBackground = cui.v2.CreatePanel(recordAddSection, LuiPosition.None, new LuiOffset(0, 0, 210, 30), ColDb.BlackTrans20);
			keyAddInputBackground.SetMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Key Add Input
			string inputHint = canEditValue ? Lang("InputKeyAndValue_Key", player.UserIDString) : Lang("InputKey", player.UserIDString);
			if (keyType == typeof(string))
				keyCommand = "ShoppyAdmin_UI dictKeyInput string newRecord";
			else if (keyType == typeof(float))
				keyCommand = "ShoppyAdmin_UI dictKeyInput float newRecord";
			else if (keyType == typeof(ulong))
				keyCommand = "ShoppyAdmin_UI dictKeyInput ulong newRecord";
			else if (keyType == typeof(int))
				keyCommand = "ShoppyAdmin_UI dictKeyInput int newRecord";
			LUI.LuiContainer keyAddInput = cui.v2.CreateInput(keyAddInputBackground, LuiPosition.Full, LuiOffset.None, ColDb.LightGray, inputHint, 15, keyCommand, 0, true, CUI.Handler.FontTypes.RobotoCondensedRegular, TextAnchor.MiddleCenter);

			//Element: Add Divider
			LUI.LuiContainer addDivider = cui.v2.CreateText(recordAddSection, LuiPosition.None, new LuiOffset(210, 0, 240, 30), 25, ColDb.LightGray, "-", TextAnchor.MiddleCenter);

			//Element: Add Record Button
			LUI.LuiContainer addRecordButton = cui.v2.CreateButton(recordAddSection, LuiPosition.None, new LuiOffset(450, 0, 480, 30), "ShoppyAdmin_UI dictAdd", ColDb.Transparent);
			addRecordButton.SetButtonMaterial("assets/content/ui/namefontmaterial.mat");

			//Element: Add Button Image
			LUI.LuiContainer addButtonImage = cui.v2.CreateSprite(addRecordButton, LuiPosition.None, new LuiOffset(5, 5, 25, 25), "assets/icons/health.png", ColDb.GreenBg);
			cui.v2.SendUi(player);
		}

		private LUI.LuiContainer FindAndCreateValidImage(CUI cui, LUI.LuiContainer container, LuiOffset offset, string icon, string color, string def = "assets/icons/folder.png", string name = "")
		{
			if (icon.Length == 0)
				return cui.v2.CreateSprite(container, offset, def, color, name);
			if (ulong.TryParse(icon, out ulong skin)) 
				return cui.v2.CreateItemIcon(container, offset, 1394042569, skin, ColDb.WhiteTrans80, name);
			ItemDefinition itemDef = ItemManager.FindItemDefinition(icon);
			if (itemDef)
				return cui.v2.CreateItemIcon(container, offset, iconRedirect[itemDef.shortname], 0, ColDb.WhiteTrans80, name);
			if (icon.Contains("http", CompareOptions.IgnoreCase))
				return cui.v2.CreateImageFromDb(container, offset, icon, color, name).SetMaterial("assets/content/ui/namefontmaterial.mat");
			if (icon.Contains(".png", CompareOptions.IgnoreCase))
				return cui.v2.CreateSprite(container, offset, icon, color, name);
			return cui.v2.CreateSprite(container, offset, def, color, name);
				
		}

		private static class ColDb
		{
			public static string Transparent = "1 1 1 0";
			public static string White = "1 1 1 1";
			public static string WhiteTrans40 = "1 1 1 0.4";
			public static string WhiteTrans80 = "1 1 1 0.8";
			public static string BlackTrans10 = "0 0 0 0.102";
			public static string BlackTrans20 = "0 0 0 0.2";
			public static string GreenBg = "0.365 0.447 0.224 1";
			public static string GreenText = "0.82 1 0.494 1";
			public static string RedBg = "0.667 0.278 0.204 1";
			public static string RedText = "1 0.647 0.58 1";
			public static string DarkGray = "0.153 0.141 0.114 1";
			public static string LTD5 = "0.196 0.18 0.153 1";
			public static string LTD10 = "0.239 0.224 0.196 1";
			public static string LTD15 = "0.282 0.263 0.235 1";
			public static string LTD20 = "0.325 0.306 0.275 1";
			public static string LTD30 = "0.412 0.388 0.357 1";
			public static string LTD40 = "0.498 0.471 0.439 1";
			public static string LTD50 = "0.58 0.553 0.518 1";
			public static string LTD60 = "0.667 0.635 0.6 1";
			public static string LTD80 = "0.839 0.8 0.761 1";
			public static string LightGray = "0.969 0.922 0.882 1";
			public static string LightGrayTransRust = "0.969 0.922 0.882 0.039";
			public static string Favourite = "1 0.878 0 0.902";
		}

		private void OverrideUiColors()
		{
			ColDb.Transparent = config.uiCol.Transparent;
			ColDb.White = config.uiCol.White;
			ColDb.WhiteTrans40 = config.uiCol.WhiteTrans40;
			ColDb.WhiteTrans80 = config.uiCol.WhiteTrans80;
			ColDb.BlackTrans10 = config.uiCol.BlackTrans10;
			ColDb.BlackTrans20 = config.uiCol.BlackTrans20;
			ColDb.GreenBg = config.uiCol.GreenBg;
			ColDb.GreenText = config.uiCol.GreenText;
			ColDb.RedBg = config.uiCol.RedBg;
			ColDb.RedText = config.uiCol.RedText;
			ColDb.DarkGray = config.uiCol.DarkGray;
			ColDb.LTD5 = config.uiCol.LTD5;
			ColDb.LTD10 = config.uiCol.LTD10;
			ColDb.LTD15 = config.uiCol.LTD15;
			ColDb.LTD20 = config.uiCol.LTD20;
			ColDb.LTD30 = config.uiCol.LTD30;
			ColDb.LTD40 = config.uiCol.LTD40;
			ColDb.LTD50 = config.uiCol.LTD50;
			ColDb.LTD60 = config.uiCol.LTD60;
			ColDb.LTD80 = config.uiCol.LTD80;
			ColDb.LightGray = config.uiCol.LightGray;
			ColDb.LightGrayTransRust = config.uiCol.LightGrayTransRust;
			ColDb.Favourite = config.uiCol.Favourite;
		}
		
		#endregion
		
		#region Language


        private void LoadMessages()
        {
	        Dictionary<string, string> langFile = new()
	        {
		        ["ShopTitle"] = "SERVER'S SHOPPING CENTER",
		        ["ShopButton"] = "SHOP",
		        ["StockMarketButton"] = "STOCK MARKET",
		        ["TransferButton"] = "TRANSFER",
		        ["ExchangeButton"] = "EXCHANGE",
		        ["DepositWithdrawButton"] = "DEPOSIT/WITHDRAW",
		        ["SelectStockHint"] = "SELECT STOCK MARKET THAT YOU WANT TO OPEN",
		        ["SelectShopHint"] = "SELECT SHOP THAT YOU WANT TO OPEN",
		        ["CurrentBalance"] = "CURRENT BALANCE",
		        ["OpenShop"] = "OPEN SHOP",
		        ["OpenStock"] = "OPEN MARKET",
		        ["NoValidPagesToOpen"] = "There is no valid store pages for you to open.",
		        ["ImagesLoading"] = "Shop images are loading. Please wait a moment...",
		        ["NoPermToShop"] = "You don't have permission to open shop using an command!",
		        ["AccessLockedPvP"] = "You can't open shop menu during combat!",
		        ["Search"] = "SEARCH",
		        ["TypeHere"] = "Type Here...",
		        ["SelectCategory"] = "SELECT CATEGORY",
		        ["Price"] = "PRICE",
		        ["ViewDetails"] = "VIEW DETAILS",
		        ["DlcMissing"] = "DLC MISSING",
		        ["Available"] = "AVAILABLE",
		        ["OnCooldown"] = "ON COOLDOWN [%TIME_LEFT%]",
		        ["DailyLimited"] = "DAILY LIMIT [{0}/{1}]",
		        ["WipeLimited"] = "WIPE LIMIT [{0}/{1}]",
		        ["SortBy_Default"] = "SORT BY",
		        ["SortBy_None"] = "NONE",
		        ["SortBy_Alphabetical"] = "A-Z",
		        ["SortBy_Popularity"] = "POPULARITY",
		        ["SortBy_Cheapest"] = "CHEAPEST",
		        ["ShowOnlyAffordable"] = "SHOW ONLY\nAFFORDABLE ITEMS",
		        ["YourBalance"] = "YOUR BALANCE",
		        ["ItemsAvailable"] = "{0} ITEMS AVAILABLE",
		        ["ItemsAvailableRoll"] = "OFFERS CHANGE EVERY {1} - {0} ITEMS AVAILABLE",
		        ["CategoryName_Search"] = "SEARCH RESULTS",
		        ["StackCount"] = "{0} Stacks",
		        ["IsCooldown"] = "COOLDOWN: {0} <color=#AA4734>[%TIME_LEFT%]</color>",
		        ["NoCooldown"] = "COOLDOWN: {0}",
		        ["WipeLimit"] = "WIPE LIMIT: {0}/{1}",
		        ["WipeLimitReached"] = "WIPE LIMIT: <color=#AA4734>{0}/{1}</color>", 
		        ["DailyLimit"] = "DAILY LIMIT: {0}/{1}",
		        ["DailyLimitReached"] = "DAILY LIMIT: <color=#AA4734>{0}/{1}</color>",
		        ["ServerWide"] = "[SERVER WIDE LIMIT]",
		        ["PurchaseAmount"] = "PURCHASE AMOUNT",
		        ["SellAmount"] = "SELL AMOUNT",
		        ["MoreBalanceRequired"] = "REQUIRED MORE BALANCE",
		        ["BalanceAfterPurchase"] = "BALANCE AFTER PURCHASE",
		        ["PurchaseCost"] = "PURCHASE COST",
		        ["GoBack"] = "GO BACK",
		        ["TooExpensive"] = "TOO EXPENSIVE",
		        ["BuyCooldown"] = "ON COOLDOWN",
		        ["LimitReached"] = "BUY LIMIT",
		        ["Purchase"] = "PURCHASE",
		        ["ListingDetails"] = "LISTING DETAILS",
		        ["SellToServer"] = "SELL TO SERVER",
		        ["StockHistory"] = "STOCK MARKET\nACTION HISTORY",
		        ["HideEmpty"] = "HIDE EMPTY\nLISTINGS",
		        ["ItemName"] = "ITEM NAME",
		        ["SellToServerButton"] = "SELL<size=10>\nTO SERVER\n</size>",
		        ["BuyOffersButton"] = "PLAYER<size=10>\nBUY OFFER\n</size>",
		        ["SellOffersButton"] = "PLAYER<size=10>\nSELL OFFER\n</size>",
		        ["BuyOfferCount"] = "{0} OFFERS FROM<size=18>\n{1}\n</size>",
		        ["SellOfferCount"] = "{0} OFFERS FROM<size=18>\n{1}\n</size>",
		        ["ClickToSortHint"] = "Click category, to sort market by type.",
		        ["BuyOffers"] = "BUY OFFERS",
		        ["SellOffers"] = "SELL OFFERS",
		        ["CurrentSellPrice"] = "CURRENT SELL PRICE",
		        ["PriceHistoryChart"] = "PRICE HISTORY CHART",
		        ["InventoryBankItemCount"] = "Amount in inventory - <color=#5D7239>{0}</color> <color=#AAA299>[{1}]</color>\nAmount in bank - <color=#5D7239>{2}</color> <color=#AAA299>[{3}]</color>",
		        ["InventoryItemCount"] = "Amount in inventory - <color=#5D7239>{0}</color> <color=#AAA299>[{1}]</color>",
		        ["OpenSellInventory"] = "OPEN<size=13>\nSELL INVENTORY\n</size>",
		        ["SellFromBank"] = "SELL<size=13>\nFROM ITEM BANK\n</size>", 
		        ["AlertSendPrice"] = "PRICE<size=15><color=#F7EBE1>\nSEND ALERT\n</color></size>",
		        ["AutoSellPrice"] = "PRICE<size=15><color=#F7EBE1>\nAUTO SELL\n</color></size>",
		        ["ChartResolution"] = "CHART RESOLUTION",
		        ["BuyFromServer"] = "BUY FROM SERVER",
		        ["AmountAvailable"] = "AMOUNT AVAILABLE",
		        ["AmountRequested"] = "AMOUNT REQUESTED",
		        ["CurrentPrice"] = "CURRENT PRICE",
		        ["PurchasePrice"] = "PURCHASE PRICE",
		        ["YouHave"] = "YOU HAVE",
		        ["AmountAvailableHint"] = "Amount available is based on amount sold to server.",
		        ["StockItemDetails"] = "STOCK ITEM DETAILS",
		        ["ListOfSellOffers"] = "AVAILABLE SELL OFFERS",
		        ["ListOfBuyOffers"] = "AVAILABLE BUY OFFERS",
		        ["CreateSellOffer"] = "CREATE SELL REQUEST",
		        ["CreateBuyOffer"] = "CREATE BUY REQUEST",
		        ["HideMyOffers"] = "HIDE MY\nOFFERS",
		        ["ListedOffers"] = "OFFERS AVAILABLE",
		        ["Details"] = "DETAILS",
		        ["Seller"] = "SELLER",
		        ["Buyer"] = "BUYER",
		        ["ListedBy"] = "LISTED BY",
		        ["SellPrice"] = "SELL PRICE",
		        ["BuyPrice"] = "BUY PRICE",
		        ["InStock"] = "IN STOCK",
		        ["Amount"] = "AMOUNT",
		        ["WrongAmount"] = "WRONG AMOUNT",
		        ["PurchaseItemFor"] = "PURCHASE {0} ITEMS<size=13>\nFOR {1}\n</size>",
		        ["SellItemFor"] = "SELL {0} ITEMS<size=13>\nFOR {1}\n</size>",
			    ["AmountFixedToLimit"] = "Amount input has been updated to valid number.",
		        ["PriceForEach"] = "{0}/each",
		        ["NoListingsAvailable"] = "THERE IS NO LISTINGS OF THIS ITEM\nCURRENTLY AVAILABLE",
		        ["BankInventory"] = "Bank Inventory [{0}/{1}]",
		        ["BankInventoryInfo"] = "Store items, that can be sold in the market.\nItems in bank can be sold even if you are offline.",
		        ["DepositAll"] = "Deposit All",
		        ["NoItemsInBank"] = "YOU DON'T HAVE ANY ITEMS IN BANK",
		        ["WithdrawHint"] = "Click resource that you want to withdraw.\nWithdrawn items go to your Redeem Inventory.",
		        ["WithdrawHint2"] = "Item you want to store",
		        ["SubmitItems"] = "Submit\nItems",
		        ["WithdrawAmount"] = "WITHDRAW\nAMOUNT",
		        ["CurrencyTransfer"] = "CURRENCY TRANSFER",
		        ["SelectPlayer"] = "SELECT PLAYER",
		        ["ShowOnlyOffline"] = "SHOW OFFLINE\nPLAYERS",
		        ["TransferToPlayer"] = "TRANSFER TO PLAYER",
		        ["SelectCurrency"] = "SELECT CURRENCY",
		        ["TransferTax"] = "{0}%\nTAX",
		        ["TransferAmount"] = "TRANSFER AMOUNT",
		        ["BalanceAfter"] = "BALANCE AFTER",
		        ["Transfer"] = "TRANSFER",
		        ["ExchangeTitle"] = "YOU HAVE {0} EXCHANGES AVAILABLE",
		        ["ItemExchange"] = "ITEM EXCHANGE",
		        ["CurrencyExchange"] = "CURRENCY EXCHANGE",
		        ["Taken"] = "TAKEN",
		        ["Given"] = "GIVEN",
		        ["To"] = "TO",
		        ["Inventory"] = "INVENTORY",
		        ["Balance"] = "BALANCE",
		        ["Exchange"] = "EXCHANGE",
		        ["DepositResourcesCount"] = "YOU HAVE {0} ITEMS TO DEPOSIT AVAILABLE",
		        ["DepositResourcesTitle"] = "DEPOSIT RESOURCES",
		        ["DepositResourcesDesc"] = "Here you can find a list of all resources that can be deposited and stored in the shop as virtual currency. Some of them might not be withdrawable again!",
		        ["Item"] = "ITEM",
		        ["Currency"] = "CURRENCY",
		        ["Ratio"] = "RATIO",
		        ["OpenDeposit"] = "OPEN DEPOSIT\nINVENTORY",
		        ["WithdrawResourcesCount"] = "YOU HAVE {0} WITHDRAW METHODS AVAILABLE",
		        ["PlayerBalance"] = "CURRENT BALANCE: {0}",
		        ["WithdrawTo"] = "WITHDRAW TO:",
		        ["WithdrawRatio"] = "RATIO:",
		        ["WithdrawCost"] = "COST",
		        ["Withdraw"] = "WITHDRAW",
		        ["CurrencyDeposit"] = "Currency Deposit",
		        ["AddCurrencyItem"] = "Add Valid Item",
		        ["Nothing"] = "NOTHING",
		        ["ItemWorth"] = "ITEM WORTH",
		        ["InputCurrencyHint"] = "Currency item that you want to deposit",
		        ["CurrencyDepositHint"] = "Some of the currency deposits might be only one-way deposits.\nYou might not be able to withdraw it later on!",
		        ["AddValidItem"] = "ADD\nITEM",
		        ["SubmitDeposit"] = "SUBMIT\nDEPOSIT",
		        ["CurrencyLeaderboards"] = "CURRENCY LEADERBOARDS",
		        ["RemainingTime"] = "REMAINING TIME",
		        ["HideListing"] = "HIDE LISTING",
		        ["ListingHidden"] = "LISTING HIDDEN<size=10>\nCODE: {0}\n</size>",
		        ["ListingHiddenPopUp"] = "You've hidden the listing. You can share this listing with /{0} {1} command.",
		        ["RemoveListing"] = "REMOVE LISTING",
		        ["SellInventory"] = "SELL INVENTORY",  
		        ["Sell"] = "SELL",
		        ["TotalSellPrice"] = "Total Sell Price: <color=#5D7239>{0}</color>",
		        ["NotForSale"] = "NOT FOR SALE",
		        ["ServerSellHint"] = "Place items that you want to sell.\nSelling action is irreversible!",
		        ["PutItemsHint"] = "PLACE ITEMS THAT YOU WANT TO SELL",
		        ["NewSellOffer"] = "New Sell Offer",
		        ["PricePerItem"] = "PRICE PER ITEM",
		        ["TaxAmount"] = "TAX - {0}%",
		        ["ListingTime"] = "LISTING TIME",
		        ["SellEarnings"] = "SELL EARNINGS",
		        ["ListingTax"] = "LISTING TAX",
		        ["SellOfferItem"] = "Place item that you want to list",
		        ["SellOfferHint"] = "If the sell request fails, items go to your Redeem Inventory.\nTax is non-refundable.",
		        ["BroadcastListing"] = "BROADCAST LISTING",
		        ["PriceRequired"] = "PRICE\nREQUIRED",
		        ["AmountRequired"] = "AMOUNT\nREQUIRED",
		        ["SubmitListing"] = "SUBMIT\nLISTING",
		        ["NewBuyOffer"] = "New Buy Offer",
		        ["ItemCost"] = "ITEM COST",
		        ["TotalCost"] = "TOTAL COST",
		        ["BuyOfferItem"] = "Item of your buy offer",
		        ["BuyOfferHint"] = "If the buy request fails, the currency returns to your balance.\nTax is non-refundable.",  
		        ["GoBackToMarket"] = "GO BACK TO MARKET",
		        ["SortActions"] = "SORT ACTIONS",
		        ["ActionHistory"] = "ACTION HISTORY",
		        ["NewSellOffers"] = "NEW SELL OFFERS",
		        ["NewBuyOffers"] = "NEW BUY OFFERS",
		        ["SoldItems"] = "SOLD ITEMS",
		        ["PurchasedItems"] = "PURCHASED ITEMS",
		        ["PriceRolls"] = "PRICE ROLLS",
		        ["Demands"] = "DEMANDS",
		        ["Alerts"] = "ALERTS/AUTO-SELL",
		        ["StockAction_SellOffer"] = "[NEW SELL OFFER] Player <color=#5D7239>{0}</color> created new offer, selling {1} {2} for {3} each.",
		        ["StockAction_BuyOffer"] = "[NEW BUY OFFER] Player <color=#5D7239>{0}</color> is looking for {1} {2}. He's willing to pay {3} for each.",
		        ["StockAction_SoldItem"] = "[SOLD ITEMS] Player <color=#5D7239>{0}</color> sold {1} {2} to <color=#5D7239>{3}</color> for {4}.",
		        ["StockAction_PurchasedItem"] = "[PURCHASED ITEMS] Player <color=#5D7239>{0}</color> purchased {1} {2} from <color=#5D7239>{3}</color> for {4}.",
		        ["StockAction_PriceRoll"] = "[PRICE ROLLS] Price in stock market has been rolled!",
		        ["StockAction_Demand"] = "[DEMANDS] There is high demand for <color=#5D7239>{0}</color>! The price has been increased by <color=#5D7239>{1}%</color>!",
		        ["StockAction_Demand_Neg"] = "[DEMANDS] There is low demand for <color=#5D7239>{0}</color>! The price has been decreased by <color=#5D7239>{1}%</color>!",
		        ["StockAction_AutoSell"] = "[AUTO-SELL] Price of <color=#5D7239>{0}</color> has exceed your requested value. Your {1} of {2} has been sold for <color=#5D7239>{3}</color>.",
		        ["Chat_StockAction_Alert"] = "<size=10>[STOCK MARKET - PRICE ALERT]</size>\nPrice of <color=#5c81ed>{0}</color> has exceed your requested value.",
		        ["Chat_StockAction_SellOffer"] = "<size=10>[STOCK MARKET - NEW SELL OFFER]</size>\nPlayer <color=#5c81ed>{0}</color> created new offer, selling {1} {2} for {3} each.",
		        ["Chat_StockAction_BuyOffer"] = "<size=10>[STOCK MARKET - NEW BUY OFFER]</size>\nPlayer <color=#5c81ed>{0}</color> is looking for {1} {2}. He's willing to pay {3} for each.",
		        ["Chat_StockAction_SoldItem"] = "<size=10>[STOCK MARKET - SOLD ITEMS]</size>\nPlayer <color=#5c81ed>{0}</color> sold {1} {2} to <color=#5c81ed>{3}</color> for {4}.",
		        ["Chat_StockAction_PurchasedItem"] = "<size=10>[STOCK MARKET - PURCHASED ITEMS]</size>\nPlayer <color=#5c81ed>{0}</color> purchased {1} {2} from <color=#5c81ed>{3}</color> for {4}.",
		        ["Chat_StockAction_PriceRoll"] = "<size=10>[STOCK MARKET - PRICE ROLLS]</size>\nPrice in stock market has been rolled!",
		        ["Chat_StockAction_Demand"] = "<size=10>[STOCK MARKET - DEMANDS]</size>\nThere is high demand for <color=#5c81ed>{0}</color>! The price has been increased by <color=#5c81ed>{1}%</color>!",
		        ["Chat_StockAction_Demand_Neg"] = "<size=10>[STOCK MARKET - DEMANDS]</size>\nThere is low demand for <color=#5c81ed>{0}</color>! The price has been decreased by <color=#5c81ed>{1}%</color>!",
		        ["Chat_StockAction_AutoSell"] = "<size=10>[STOCK MARKET - AUTO-SELL]</size>\nPrice of <color=#5c81ed>{0}</color> has exceed your requested value. Your {1} of {2} has been sold for <color=#5D7239>{3}</color>.",
		        ["Chat_StockAction_Alert"] = "<size=10>[STOCK MARKET - PRICE ALERT]</size>\nPrice of <color=#5c81ed>{0}</color> has exceed your requested value.",
		        ["WipeLength"] = "WIPE",
		        ["UntilWipe"] = "UNTIL WIPE",
		        ["NoLimit"] = "PERM",
		        ["PermanentTime"] = "PERMANENT",
		        ["Condition"] = "{0}% Durability",
		        ["InstaSellDiscordMessage"] = "Hey! The price of **{2}** exceeds your requested value and we sold **{0}** items from your bank for **{1}**.",
		        ["AlertDiscordMessage"] = "Hey! The price of **{0}** exceeds your requested value!",
		        ["ShopPurchaseMessage"] = "You've successfully purchased <color=#5D7239>{1} {0}</color> for <color=#5D7239>{2}</color>.",
		        ["ListingReturnedEndTime"] = "One of your sell offers has reached the end of it's listing time and has been returned to your redeem inventory.",
		        ["ListingMoneyReturnedEndTime"] = "One of your buy offers has reached the end of it's listing time and money has been refunded to your balance.",
		        ["NotEnoughCurrency"] = "You don't have enough currency to perform this action!",
		        ["ListingLimitReached"] = "You've reached limit of your posted listings. You need to cancel your previous ones in order to add a new one.",
		        ["CannotListSkinnedItems"] = "You cannot add unlisted skinned items. Remove skin first in order to create a listing.",
		        ["CreatedListing"] = "Listing created successfully!",
		        ["BuyOfferCompleted"] = "You've successfully sold <color=#5D7239>{0} {1}</color> for <color=#5D7239>{2}</color>.",
		        ["SellOfferCompleted"] = "You've successfully purchased <color=#5D7239>{0} {1}</color> for <color=#5D7239>{2}</color>.",
		        ["NoItemsInBank"] = "You don't have valid items in the bank!",
		        ["ItemSubmitedSuccessfully"] = "Your items have been submitted to the bank successfully!",
		        ["TooManyIndividualItems"] = "You've reached individual item limit in bank. Remove individual item types in order to insert new ones.",
		        ["TooManyItems"] = "Some of your bank items reached its capacity limit!",
		        ["ItemsWithdrawedFromBank"] = "You've successfully withdrawn <color=#5D7239>{0} {1}</color> from your bank.",
		        ["TooLessToSell"] = "You don't have <color=#5D7239>enough items</color> of this type to sell it to stock market!",
		        ["NotAllowedToSell"] = "You are <color=#5D7239>not allowed to sell</color> this type of item to stock market!",
		        ["SellLimitReached"] = "You've <color=#5D7239>reached limit</color> of item sold to stock market. Come back tomorrow!",
		        ["SoldToServer"] = "You've sold your resources to the server for <color=#5D7239>{0}</color>.",
		        ["Hidden"] = "HIDDEN",
		        ["CurrencyTransfered"] = "You've transferred <color=#5D7239>{0}</color> to <color=#5D7239>{1}'s</color> balance.",
		        ["CurrencyReceived"] = "You've received <color=#5D7239>{0}</color> from <color=#5D7239>{1}</color>!",
		        ["WithdrawedCurrency"] = "You've successfully withdrawn <color=#5D7239>{0}</color>.",
		        ["DepositedCurrency"] = "You've successfully deposited <color=#5D7239>{0}</color>.",
		        ["CurrencyExchanged"] = "You've successfully exchanged currency!",
		        ["NotEnoughToCreateItem"] = "This trade is not enough to create one currency item!",
		        ["PurchasedFromStock"] = "You've successfully purchased <color=#5D7239>{0} {1}</color> from the server stock market for <color=#5D7239>{2}</color>.",
		        ["SetAlertPrice"] = "You've successfully set your alert price to <color=#5D7239>{0}</color>.",
		        ["SetSellPrice"] = "You've successfully set your auto-sell price to <color=#5D7239>{0}</color>. Keep in mind you have to <color=#5D7239>put resources into bank</color> to make it work!",
		        ["NoAdminPermission"] = "You are not an admin!",
		        ["AdminCommandHelp"] = "Command Usage:\n{0} <shopName> give <userIdOrName> <amount> - Gives to player certain amount of currency.\n{0} <shopName> take <userIdOrName> <amount> - Takes from player certain amount of currency.\n{0} <shopName> clear <userIdOrName> - Clears player's balance.\n{0} <shopName> check <userIdOrName> - Checks player's current balance.",
		        ["CurrencyNotFound"] = "Currency '{0}' has not been found.",
		        ["UserNotFound"] = "User with ID/Name '{0}' has not been found.",
		        ["TooManyUsersFound"] = "Found more than one user with the same nickname. Try input full nickname or use ID instead.",
		        ["WrongAmountFormat"] = "Input value '{0}' is not an integer.",
		        ["CurrencyAdded"] = "Added {2} to {1}'s {0} shop balance. Current balance: {3}",
		        ["CurrencyTaken"] = "Took {2} from {1}'s {0} shop balance. Current balance: {3}",
		        ["CurrencyCleared"] = "Balance of {1}'s {0} shop has been cleared.",
		        ["CurrencyCheck"] = "Balance of {1}'s {0} shop: {2}",
		        ["DailyLimitCurrency"] = "DAILY LIMIT",
		        ["WipeLimitCurrency"] = "WIPE LIMIT",
		        ["NotAssignedNpc"] = "This NPC has nothing assigned. ShoppyStock won't work correctly without that!",
		        ["NotValidCode"] = "This code is not valid stock code!",
		        ["CodeNotFound"] = "We couldn't find listing with <color=#5D7239>{0}</color> code.",
		        ["UiClosedCombat"] = "Shop panel has been closed because of your combat status.",
		        ["NotAllowedThisPage"] = "You are not allowed to see this page!",
		        ["CategoryName_MyListings"] = "MY LISTINGS",
		        ["CategoryName_Bank"] = "BANK",
		        ["CategoryName_Favourites"] = "FAVOURITES",
		        ["CategoryName_AllItems"] = "ALL ITEMS",
		        ["ItemRefunded"] = "Listing has been removed and your items has been refunded.",
		        ["CurrencyRefunded"] = "Listing has been removed and required currency (<color=#5D7239>{0}</color>) has gone back into your account.",
		        ["UpdatePricesHelp"] = "Commands available:\nupdateprices <shopName> - Rolls prices in certain stock market\nupdateprices <shopName> <amount> - Rolls prices in certain stock market <amount> times\nupdateprices <shopName> <shortname> <skin> - Rolls price of an certain item\nupdateprices <shopName> <shortname> <skin> <amount> - Rolls price of an certain item <amount> times",
		        ["ShopNotFound"] = "Shop '{0}' was not found.",
		        ["NotValidSkinId"] = "Skin '{0}' is not valid skin ID.",
		        ["UpdatedAllItemPrices"] = "Updated all item prices x{1} times in '{0}' stock market.",
		        ["UpdatedCertainItemPrices"] = "Updated '{1}' with skin '{2}' prices x{3} times in '{0}' stock market.",
		        ["AdminMenuTitle"] = "SHOPPY STOCK ADMIN MENU",
		        ["UnsavedChanges"] = "You have some unsaved changes!",
		        ["SaveConfig"] = "SAVE\nCONFIG",
		        ["RevertChanges"] = "REVERT\nCHANGES",
		        ["GoThere"] = "GO THERE",
		        ["EditList"] = "EDIT LIST",
		        ["EditDict"] = "EDIT DICT.",
		        ["ListEditor"] = "LIST EDITOR",
		        ["InputAndClick"] = "Input and click + to add value!",
		        ["ClickPlus"] = "Click + to add new object!",
		        ["DictionaryEditor"] = "DICTIONARY EDITOR",
		        ["InputKeyAndValue_Value"] = "and VALUE and click +!",
		        ["AddFirst"] = "ADD RECORD FIRST",
		        ["InputKeyAndValue_Key"] = "Input KEY",
		        ["InputKey"] = "Input KEY and click +!",
		        ["InvalidInputFormat"] = "Your input is not valid for this type of field!",
		        ["ConfigSaved"] = "New config has been saved! To make all bigger changes work, it's recommended to reload the plugin!",
		        ["ConfigReverted"] = "Config editor is back to current default config.",
		        ["FieldSuccessfullyEdited"] = "Field has been updated successfully!",
		        ["CustomButton_AddCurrencyItem"] = "CUSTOM BUTTON - ADD CURRENCY ITEM (TAKES ITEM FROM HANDS)",
		        ["CustomButton_OpenShopData"] = "CUSTOM BUTTON - OPENS SHOP DATA CONFIG FILE",
		        ["CustomButton_OpenStockData"] = "CUSTOM BUTTON - OPENS STOCK DATA CONFIG FILE",
		        ["CustomButton_AddListingItem"] = "CUSTOM BUTTON - ADDS ITEM TO THIS CATEGORY FOR SALE (TAKES ITEM FROM HANDS)",
		        ["CustomButton_AddCustomItem"] = "CUSTOM BUTTON - ADDS CUSTOM ITEM TO LISTINGS (TAKES ITEM FROM HANDS)",
		        ["CustomButton_AddSellItem"] = "CUSTOM BUTTON - ADDS ITEM TO SERVER SELL (TAKES ITEM FROM HANDS)",
		        ["NoItemInHands"] = "You need to have item in hands in order to add it!",
		        ["CurrItemAddedSuccessfully"] = "Currency item has been added successfully!",
		        ["ListingAlreadyAdded"] = "Listing of the same shortname and skin already exist!",
		        ["ShopItemAddedSuccessfully"] = "Item has been successfully added to the shop!",
		        ["CustomItemAddedSuccessfully"] = "Custom item has been successfully added to the stock list!",
		        ["SellItemAddedSuccessfully"] = "Item has been successfully added to the server sell list!",
	        };
	        int counter = -1;
	        foreach (var curr in config.curr)
	        {
		        langFile.Add($"{curr.Key}_Name", curr.Key.ToUpper());
		        if (curr.Value.shopCfg.enabled)
		        {
			        langFile.Add($"Shop_{curr.Key}_Name", $"{curr.Key.ToUpper()} SHOP");
			        langFile.Add($"Shop_{curr.Key}_Desc", $"This is default description of {curr.Key.ToUpper()} SHOP. It can be changed in /lang/en/ShoppyStock.json file!");
			        foreach (var cat in data.shop[curr.Key].cfg.categories)
			        {
				        if (curr.Value.shopCfg.namesMultilingual)
					        foreach (var item in cat.Value.listings)
						        langFile.TryAdd($"ItemName_{item.Key}", item.Key);
				        if (cat.Value.rollBroadcastChat)
					        langFile.TryAdd($"ShopOfferBroadcast_{cat.Key}", $"There is price roll in {curr.Key} shop in {cat.Key} category. This message can be changed in /lang/en/ShoppyStock.json file!");
			        }
			        foreach (var sale in curr.Value.shopCfg.sales.Values)
				        if (sale.saleMessLangKey.Length > 0)
					        langFile.TryAdd(sale.saleMessLangKey, "This is message from some shop discount being started. It can be changed in /lang/en/ShoppyStock.json file!");
		        }
		        if (curr.Value.stockCfg.enabled)
		        {
			        langFile.Add($"Stock_{curr.Key}_Name", $"{curr.Key.ToUpper()} STOCK MARKET");
			        langFile.Add($"Stock_{curr.Key}_Desc", $"This is default description of {curr.Key.ToUpper()} STOCK MARKET. It can be changed in /lang/en/ShoppyStock.json file!");
			        if (curr.Value.stockCfg.marketLog.tabEnabled)
				        langFile.Add($"StockActionHistory_{curr.Key}", $"{curr.Key.ToUpper()} STOCK ACTION HISTORY");
			        foreach (var detail in curr.Value.stockCfg.itemsCustomDetails)
				        langFile.TryAdd(detail.Value, $"Details of {detail.Key}");
			        foreach (var multEvent in curr.Value.stockCfg.sellCfg.priceCalc.events)
				        langFile.TryAdd($"Event_{multEvent.Key}", $"Event {multEvent.Key} increased price of {0} by {1}%! This message can be changed in /lang/en/ShoppyStock.json file!");
			        if (curr.Value.stockCfg.itemsNamesMultilingual)
			        {
				        foreach (var item in ItemManager.itemList)
				        {
					        if (curr.Value.stockCfg.itemsHiddenShortnames.Contains(item.shortname)) continue;
					        langFile.TryAdd($"ItemName_{item.shortname}_0", item.displayName.english);
				        }
				        foreach (var shortname in data.stock[curr.Key].cfg.customItems)
					        foreach (var skin in shortname.Value)
						        langFile.TryAdd($"ItemName_{shortname.Key}_{skin.Key}", skin.Value.displayName);
			        }
		        }
		        foreach (var exchange in curr.Value.exchangeCfg.exchanges)
		        {
			        counter++;
			        langFile.Add($"ExchangeTitle_{counter}", $"{exchange.currName}{exchange.itemDisplayName} TO {curr.Key}");
		        }
	        }
	        foreach (var shop in data.shop.Values)
	        {
		        foreach (var category in shop.cfg.categories)
		        {
			        langFile.TryAdd($"CategoryName_{category.Key}", category.Key.ToUpper());
			        foreach (var listing in category.Value.listings)
				        if (listing.Value.description)
					        langFile.TryAdd($"Description_{listing.Key}", $"Default description of {listing.Key} listing. It can be changed in /lang/en/ShoppyStock.json file!");
		        }
	        }

	        foreach (var stock in stockItems)
		        foreach (var category in stock.Value.categories)
			        langFile.TryAdd($"CategoryName_{category.Key}", category.Key.ToUpper());
	        lang.RegisterMessages(langFile, this);
        }

        private void Mess(BasePlayer player, string key, params object[] args) => SendReply(player, Lang(key, player.UserIDString, args));

        private string Lang(string key, string id = null, params object[] args)
        {
	        if (args.Length == 0)
		        return lang.GetMessage(key, this, id);
			return string.Format(lang.GetMessage(key, this, id), args); 
        }

        private static readonly StringBuilder logBuilder = new();
        
        private void LogToConsole(string key, params object[] args)
        {
	        if (!config.consoleLogsEnabled) return;
	        if (!config.consoleLogs.TryGetValue(key, out string message)) return;
	        if (string.IsNullOrEmpty(message)) return;
	        string formattedMessage = string.Format(message, args);
	        if (config.consoleLogsToFile)
		        logBuilder.AppendLine(formattedMessage);
	        Puts(formattedMessage);
        }

        private void SaveLogs()
        {
	        string savedLogs = logBuilder.ToString().Trim();
	        logBuilder.Clear();
	        if (savedLogs.Length > 0)
				LogToFile("logs", savedLogs, this);
        }
        
        #endregion
        
        #region Config

        private static PluginConfig config = new();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            config = Config.ReadObject<PluginConfig>();
            Config.WriteObject(config);
        }

        protected override void SaveConfig() => Config.WriteObject(config);

        protected override void LoadDefaultConfig()
        {
            Config.WriteObject(config = new()
            {
	            cmd = new CommandsConfig()
	            {
		            mainUi = new() { "s", "shop" },
		            adminUi = new() { "adminshop" },
		            deposit = new() { "deposit" },
		            shops = new() {
			            { "mshop", "money" },
			            { "goldshop", "gold" },
		            },
		            markets = new() {
			            { "market", "money" }
		            },
		            sellInv = new() {
			            { "sell", "money" }
		            },
		            newListing = new() {
			            { "list", "money" }
		            },
		            bankInv = new() {
			            { "bank", "money" }
		            },
		            quickAccess = new() { "stockcode", "listcode" },
	            },
	            curr = new()
	            {
		            { "money", new() {
			            currCfg = new()
			            {
				            icon = "assets/icons/store.png",
				            invCurrency = false,
				            currItems = new()
				            {
					            new () { shortname = "researchpaper", displayName = "Research Paper"}
				            }
			            },
			            shopCfg = new()
			            {
				            discountPerms = new() {
					            { "shoppystock.premium", 10 },
					            { "shoppystock.vip", 5 }
				            },
				            saleInterval = 180,
				            saleAppearChance = 5,
				            sales = new()
				            {
					            { "vehicleSale", new() { minSale = 5, maxSale = 20, minLength = 60, maxLength = 120, categories = new() { "vehicles" }, items = new() { "deLorean" } } }
				            }
			            },
			            stockCfg = new()
			            {
				            categoriesCustomOrder = new()
				            {
					            "Resources", "CustomItems", "Component", "Electrical", "Food", "Misc", "Tool", "Items", "Attire", "Ammunition", "Construction", "Fun", "Weapon", "Medical", "Traps"
				            },
				            categoriesIcons = new()
				            {
					            { "Resources", "stones" },
					            { "CustomItems", "3415709965" },
					            { "Medical", "assets/icons/medical.png" },
					            { "Component", "https://images.pvrust.eu/items/scrap.png" },
				            },
				            itemsHiddenShortnames = new()
				            {
					            "coal", "habrepair", "mlrs", "minihelicopter.repair", "scraptransportheli.repair", "submarinesolo", "submarineduo", "locomotive", "wagon", "workcart",
					            "door.key",  "blueprintbase", "note", "photo", "captainslog", "rhib", "rowboat", "vehicle.chassis", "vehicle.chassis.3mod", "vehicle.chassis.2mod",
					            "vehicle.chassis.4mod", "vehicle.module", "ammo.snowballgun", "bottle.vodka", "dogtagneutral", "bluedogtags", "reddogtags", "skull.human",
					            "water.salt", "water",  "fishing.tackle", "spraycandecal", "snowmobile", "snowmobiletomaha", "door.closer", "wrappedgift"
				            },
				            itemsCustomDetails = new()
				            {
					            { 3415709965, "SpecialItem" },
					            { 3044984836, "SpecialItem" },
					            { 3040701163, "UpgradedCupboard" },
				            },
				            bankDefaultLimits = new()
				            {
					            maxIndividualItems = 10,
					            maxAmountOverride = new()
									{ { "wood" , new() { { 2945104512, 1 } } } }
				            },
				            bankPermLimits = new()
				            {
					            { "shoppystock.vip", new () {
						            maxIndividualItems = 15,
						            maxAmountOverride = new()
							            { { "wood" , new() { { 2945104512, 2 } } } }
					            } },
					            { "shoppystock.premium", new () {
						            maxIndividualItems = 25,
						            maxAmountOverride = new()
							            { { "wood" , new() { { 2945104512, 3 } } } }
					            } }
				            },
				            buyOfferTimes = new()
				            {
					            new () {
						            taxAmount = 0.5f,
						            taxAmountPerms = new()
						            {
							            { "shoppystock.vip", 0.4f },
							            { "shoppystock.premium", 0.3f }
						            },
						            listingTime = 3
					            },
					            new () {
						            taxAmount = 1.0f,
						            taxAmountPerms = new()
						            {
							            { "shoppystock.vip", 0.8f },
							            { "shoppystock.premium", 0.6f }
						            },
						            listingTime = 12
					            },
					            new () {
						            taxAmount = 2.0f,
						            taxAmountPerms = new()
						            {
							            { "shoppystock.vip", 1.6f },
							            { "shoppystock.premium", 1.2f }
						            },
						            listingTime = 72
					            },
					            new () {
						            taxAmount = 3.0f,
						            taxAmountPerms = new()
						            {
							            { "shoppystock.vip", 2.4f },
							            { "shoppystock.premium", 1.8f }
						            },
						            listingTime = 168
					            },
					            new () {
						            taxAmount = 5.0f,
						            taxAmountPerms = new()
						            {
							            { "shoppystock.vip", 4.0f },
							            { "shoppystock.premium", 3.0f }
						            },
						            listingTime = 24.7f
					            },
					            new () {
						            taxAmount = 10.0f,
						            taxAmountPerms = new()
						            {
							            { "shoppystock.vip", 8.0f },
							            { "shoppystock.premium", 6.0f }
						            },
						            listingTime = -1
					            },
				            },
				            buyOfferDisabled = new()
				            {
					            { "wood", new() { 5677445667, 5342346457 }  },
					            { "researchpaper", new() { 0, 2945104512 }  }
				            },
				            sellOfferTimes = new()
				            {
					            new () {
						            taxAmount = 0.5f,
						            taxAmountPerms = new()
						            {
							            { "shoppystock.vip", 0.4f },
							            { "shoppystock.premium", 0.3f }
						            },
						            listingTime = 3
					            },
					            new () {
						            taxAmount = 1.0f,
						            taxAmountPerms = new()
						            {
							            { "shoppystock.vip", 0.8f },
							            { "shoppystock.premium", 0.6f }
						            },
						            listingTime = 12
					            },
					            new () {
						            taxAmount = 2.0f,
						            taxAmountPerms = new()
						            {
							            { "shoppystock.vip", 1.6f },
							            { "shoppystock.premium", 1.2f }
						            },
						            listingTime = 72
					            },
					            new () {
						            taxAmount = 3.0f,
						            taxAmountPerms = new()
						            {
							            { "shoppystock.vip", 2.4f },
							            { "shoppystock.premium", 1.8f }
						            },
						            listingTime = 168
					            },
					            new () {
						            taxAmount = 5.0f,
						            taxAmountPerms = new()
						            {
							            { "shoppystock.vip", 4.0f },
							            { "shoppystock.premium", 3.0f }
						            },
						            listingTime = 24.7f
					            },
				            },
				            sellOfferDisabled = new()
				            {
					            { "wood", new() { 5677445667, 5342346457 }  },
					            { "researchpaper", new() { 0, 2945104512 }  }
				            },
				            maxItemPriceOverrides = new()
				            {
					            { "wood", new() { { 5677445667, 10000 }, { 5677656534, 2000 } } },
					            { "researchpaper", new() { { 0, 10000 } } },
				            },
				            minItemPriceOverrides = new()
				            {
					            { "wood", new() { { 5677445667, 50 }, { 5677656534, 20 } } },
					            { "researchpaper", new() { { 0, 10 } } },
				            },
				            listingLimitPerms = new()
				            {
					            { "shoppystock.premium", new() { buyOffers = 25, sellOffers = 25 } },
					            { "shoppystock.vip", new() { buyOffers = 15, sellOffers = 15 } },
				            },
				            sellCfg = new()
				            {
					            priceUpdateCertainMinutes = new() { 0, 30, 60 },
					            sellPriceMultipliers = new()
					            {
						            { "shoppystock.sellmultiplier.1.5", 1.5f },
						            { "shoppystock.sellmultiplier.2.0", 2.0f },
					            },
					            sellPriceMultBlacklist = new()
					            {
									{ "researchpaper", new()
										{
											1,
											534447,
											84556344
										}
									}
					            },
					            priceChartTimespanPerms = new()
					            {
						            { 30, "" },
						            { 120, "" },
						            { 480, "shoppystock.timestamp.4h" },
						            { 720, "shoppystock.timestamp.12h" },
						            { 1440, "shoppystock.timestamp.24h" }
					            },
					            priceCalc = new()
					            {
						            priceBarriers = new() {
							            { 70, 0 }, { 65, 1 }, { 60, 3 }, { 55, 5 }, { 50, 15 }, { 45, 35 }, { 40, 50 }, { 35, 65 }, { 25, 80 }, { 15, 95 }, { 0, 100 }
						            },
						            priceDropChart = new() {
							            { 5000, 4 }, { 2500, 4 }, { 1200, 3 }, { 800, 2.5f }, { 500, 2 }, { 250, 1.5f }, { 125, 1.2f }
						            },
						            sellPricePenalty = new() {
							            { 10000, new PricePenaltyConfig() { penaltyLength = 24, percentage = 35 } },
							            { 9000, new PricePenaltyConfig() { penaltyLength = 20, percentage = 40 } },
							            { 7000, new PricePenaltyConfig() { penaltyLength = 24, percentage = 45 } },
							            { 5000, new PricePenaltyConfig() { penaltyLength = 20, percentage = 50 } },
							            { 4000, new PricePenaltyConfig() { penaltyLength = 16, percentage = 55 } },
							            { 3000, new PricePenaltyConfig() { penaltyLength = 12, percentage = 60 } },
							            { 2000, new PricePenaltyConfig() { penaltyLength = 8, percentage = 65 } },
							            { 1000, new PricePenaltyConfig() { penaltyLength = 6, percentage = 75 } },
							            { 500, new PricePenaltyConfig() { penaltyLength = 4, percentage = 85 } },
							            { 300, new PricePenaltyConfig() { penaltyLength = 2, percentage = 90 } },
						            },
						            goalAchievedChart = new() {
							            { 0, 3 }, { 15, 2 }, { 25, 1.5f }, { 50, 1.3f }, { 75, 1.1f }
						            },
						            sellAmountOnlineMultiplier = new() {
							            { 10, 1.5f }, { 20, 2f }, { 40, 4f }, { 60, 6f }, { 80, 8f }, { 100, 10f }
						            },
						            multplierAmountChance = new() {
							            { 0, 20 }, { 25, 15 }, { 50, 10 }, { 100, 5 }, { 200, 1 }
						            },
						            events = new() {
							            { "ExtremeDemand", new() { min = 1.7f, max = 2.5f } },
							            { "HighDemand", new() { min = 1.3f, max = 2.3f } },
							            { "VeryHighDemand", new() { min = 1.1f, max = 1.7f } },
							            { "NegativeDemand", new() { min = 0.5f, max = 0.9f } },
							            { "UltraNegativeDemand", new() { min = 0.2f, max = 0.5f } },
						            }
					            }
				            }
			            },
			            transferCfg = new()
			            {
				            dailyTransferLimitPerms = new() { { "shoppystock.transferpunish", 1000 } },
				            wipeTransferLimitPerms = new() { { "shoppystock.transferpunish", 20000 } },
				            tax = 0.5f,
				            taxPerms = new() { { "shoppystock.transferpunish", 2.5f } }
			            },
			            exchangeCfg = new()
			            {
				            enabled = true,
				            exchanges = new()
				            {
					            new() {
						            perm = "shoppystock.exchange.scrap",
						            itemShortname = "scrap",
						            itemAmount = 500,
						            itemDisplayName = "Scrap"
					            }
				            }
			            },
			            depositCfg = new()
			            {
							withdrawItems = new()
							{
								new()
								{
									shortname = "researchpaper",
									displayName = "Research Paper"
								}
							}
			            }
		            } }
	            },
	            npcs = new() { 
		            { "845364534", new() {
			            shops = new()
			            {
				            { "money", new() { "vehicles" , "resources", "fishing" } },
				            { "gold", new() }
			            },
			            stocks = new()
			            {
				            { "money" },
						}
		            } }
	            }
            }, true);
        }

        private class PluginConfig
        {
            [JsonProperty("Commands")]
            public CommandsConfig cmd = new();
            
            [JsonProperty("UI Utility")]
            public UIConfig ui = new();
            
            [JsonProperty("Wipe Settings")]
            public WipeConfig wipe = new();
            
            [JsonProperty("Stores/Currencies")]
            public Dictionary<string, CurrConfig> curr = new();
            
            [JsonProperty("NPC Configuration")]
            public Dictionary<string, NPCConfig> npcs = new();
            
            [JsonProperty("Console Messages - Enabled")]
            public bool consoleLogsEnabled = true;
            
            [JsonProperty("Console Messages - Log to Indivdual Files")]
            public bool consoleLogsToFile = true;
            
            [JsonProperty("Console Messages (empty won't be displayed)")]
            public Dictionary<string, string> consoleLogs = new();
            
            [JsonProperty("UI Colors")]
            public UIColorConfig uiCol = new();
            
            [JsonProperty("Last Config Version (do not modify)")]
            public string lastVersion = "2.0.0";
        }

        private class CommandsConfig
        {
	        [JsonProperty("Permission Required To Run Any Command (not required, if empty)")]
	        public string perm = "";
	        
	        [JsonProperty("Console Currency Management Command")]
	        public string currCmd = "curr";
	        
	        [JsonProperty("Open Main UI")]
	        public List<string> mainUi = new();
	        
	        [JsonProperty("Open Admin UI")]
	        public List<string> adminUi = new();
	        
	        [JsonProperty("Open Currency Deposit Inventory")]
	        public List<string> deposit = new();
	        
	        [JsonProperty("Open Certain Shop (command: shopName)")]
	        public Dictionary<string, string> shops = new();
	        
	        [JsonProperty("Open Certain Market (command: shopName)")]
	        public Dictionary<string, string> markets = new();
	        
	        [JsonProperty("Open Certain Sell Inventory (command: shopName)")]
	        public Dictionary<string, string> sellInv = new();
	        
	        [JsonProperty("Open Certain New Listing Inventory (command: shopName)")]
	        public Dictionary<string, string> newListing = new();
	        
	        [JsonProperty("Open Certain Bank Inventory (command: shopName)")]
	        public Dictionary<string, string> bankInv = new();
	        
	        [JsonProperty("Open Stock Quick Access Code")]
	        public List<string> quickAccess = new();
        }

        private class UIConfig
        {
	        [JsonProperty("Clicking Sound (disabled, if empty)")]
	        public string clickSound = "assets/bundled/prefabs/fx/notice/loot.copy.fx.prefab";
	        
	        [JsonProperty("UI Action Cooldown (0, to disable)")]
	        public float cooldown = 0.2f;
	        
	        [JsonProperty("Custom Icon URLs (you can change the CDN if you want)")]
	        public CustomIconConfig icons = new();
	        
	        [JsonProperty("Pop-Up Font Size")]
	        public int popUpFontSize = 16;
	        
	        [JsonProperty("Pop-Up Display Time (in seconds)")]
	        public float popUpDisplayTime = 10;
	        
	        [JsonProperty("Search Record Limit")]
	        public int searchLimit = 50;
	        
	        [JsonProperty("Currency Leaderboard Update Interval (in minutes)")]
	        public int leaderboardCheck = 15;
		        
	        [JsonProperty("NoEscape - Lock Opening UI On Combat")]
	        public bool noEscapeCombat = true;
		        
	        [JsonProperty("NoEscape - Lock Opening UI On Raid")]
	        public bool noEscapeRaid = true;
        }

        private class CustomIconConfig
        {
	        [JsonProperty("Trophy Icon")]
	        public string trophyUrl = "https://images.pvrust.eu/ui_icons/ShoppyStock2/trophy.png";
	        
	        [JsonProperty("Boxes Icon")]
	        public string boxesUrl = "https://images.pvrust.eu/ui_icons/ShoppyStock2/product.png";
        }
        
        private class WipeConfig
        {
	        [JsonProperty("Trigger Wipe Check On Map Wipe")]
	        public bool mapWipe = true;
	        
	        [JsonProperty("Trigger Wipe Check On Map Name Change")]
	        public bool mapName = true;
	        
	        [JsonProperty("Trigger Wipe Check On Protocol Change")]
	        public bool protocol = false;
	        
	        [JsonProperty("Trigger Wipe Check Only On First Thursday")]
	        public bool onlyThursday = false;
	        
	        [JsonProperty("Clear Preview-Only Statistic Data Older Than X Days (-1, to disable)")]
	        public int statsWipeTime = -1;
        }

        private class CurrConfig
        {
	        [JsonProperty("Currency Config")]
	        public CurrencyConfig currCfg = new();
	        
	        [JsonProperty("Shop Config")]
	        public ShopConfig shopCfg = new();

	        [JsonProperty("Stock Market Config")] 
	        public StockConfig stockCfg = new();

	        [JsonProperty("Transfer Config")] 
	        public TransferConfig transferCfg = new();

	        [JsonProperty("Exchanges Config")] 
	        public ExchangesConfig exchangeCfg = new();

	        [JsonProperty("Deposit/Withdraw Config")] 
	        public DepositConfig depositCfg = new();
        }

        private class CurrencyConfig
        {
	        [JsonProperty("Icon (URL/RUST Path/Skin ID)")]
	        public string icon = "";

	        [JsonProperty("Color Currency Icon To Green")]
	        public bool colorCurrIcon = true;
		        
	        [JsonProperty("Currency Other Plugin Override (empty, for ShoppyStock database)")]
	        public string plugin = "";
	        
	        [JsonProperty("Currency Symbol Formatting")]
	        public string currencyFormat = "${0}";
	        
	        [JsonProperty("Amount Symbol Formatting")]
	        public string amountFormat = "x{0}";

	        [JsonProperty("Use Inventory Currency")]
	        public bool invCurrency = true;

	        [JsonProperty("Give Currency To Inventory")]
	        public bool invCurrGive = false;

	        [JsonProperty("Format Currency Amount")]
	        public bool formatCurrency = true;

	        [JsonProperty("Clear Currency On Wipe")]
	        public bool wipeCurrency = false;

	        [JsonProperty("Show Currency In Leaderboard")]
	        public bool leaderboard = true;

	        [JsonProperty("Currency Percentage Took On Wipe (0, to disable)")]
	        public float currencyPercentageTookOnWipe = 0;

	        [JsonProperty("Currency Items")]
	        public List<CurrencyItemConfig> currItems = new();
        }

        private class CurrencyItemConfig
        {
	        [JsonProperty("Shortname")] 
	        public string shortname = "";
	        
	        [JsonProperty("Skin ID")] 
	        public ulong skinId = 0;
	        
	        [JsonProperty("Value")] 
	        public float value = 1;

	        [JsonProperty("Item Display Name (For UI Purposes)")]
	        public string displayName = "";
        }

        private class ShopConfig
        {
	        [JsonProperty("Enabled")]
	        public bool enabled = true;
	        
	        [JsonProperty("Required Permission")]
	        public string perm = "";
	        
	        [JsonProperty("Generate Config With All Default RUST Items (delete the shop config file to generate)")]
	        public bool generateItems = false;
	        
	        [JsonProperty("Make Item Names Multilingual (generates a lot of text in lang file)")]
	        public bool namesMultilingual = false;
	        
	        [JsonProperty("Lock Opening Insufficient Currency Listings")]
	        public bool lockOpeningNotEnoughMoney = false;
	        
	        [JsonProperty("Discount Permissions (permission: percentage)")]
	        public Dictionary<string, float> discountPerms = new();
	        
	        [JsonProperty("Add Percentage Discounts To Value Discounts (sum discounts)")]
	        public bool sumDiscounts = false;
	        
	        [JsonProperty("Add Ownership To Purchased Items")]
	        public bool addOwnership = false;
	        
	        [JsonProperty("Add Ownership To Purchased Items (even if by default RUST doesn't add it, option above must be true)")]
	        public bool addOwnershipEverywhere = false;
	        
	        [JsonProperty("Shop Sales - Interval (in minutes, 0 to disable)")]
	        public float saleInterval = 0;
	        
	        [JsonProperty("Shop Sales - Appear Chance (0-100)")]
	        public float saleAppearChance = 0;
	        
	        [JsonProperty("Shop Sales (saleKey: SaleConfig)")]
	        public Dictionary<string, SaleConfig> sales = new();
        }

        private class SaleConfig
        {
	        [JsonProperty("Minimal Sale (percentage)")]
	        public float minSale = 0;
	        
	        [JsonProperty("Maximal Sale (percentage)")]
	        public float maxSale = 0;
	        
	        [JsonProperty("Minimal Length (in minutes)")]
	        public float minLength = 0;
	        
	        [JsonProperty("Maximal Length (in minutes)")]
	        public float maxLength = 0;
	        
	        [JsonProperty("Same Sale For Each Item")]
	        public bool sameSaleValue = true;
	        
	        [JsonProperty("Broadcast On Chat Message Language Key (empty, to disable)")]
	        public string saleMessLangKey = "";
	        
	        [JsonProperty("Weight (chance to appear)")]
	        public int weight = 1;
	        
	        [JsonProperty("Category Sales (empty for whole shop)")]
	        public List<string> categories = new();
	        
	        [JsonProperty("Certain Item Sales (keys)")]
	        public List<string> items = new();
	        
        }
        
        private class StockConfig
        {
	        [JsonProperty("Enabled")]
	        public bool enabled = true;
	        
	        [JsonProperty("Required Permission (not required, if empty)")]
	        public string perm = "";
	        
	        [JsonProperty("Wipe - Buy Offers (based on Wipe Settings)")]
	        public bool wipeBuyOffers = false;
	        
	        [JsonProperty("Wipe - Sell Offers (based on Wipe Settings)")]
	        public bool wipeSellOffers = true;
	        
	        [JsonProperty("Wipe - Bank (based on Wipe Settings)")]
	        public bool wipeBank = true;
	        
	        [JsonProperty("Categories - Enable My Listings")]
	        public bool categoriesMyListings = true;
	        
	        [JsonProperty("Categories - Enable All Valid Listings")]
	        public bool categoriesAllItems = true;
	        
	        [JsonProperty("Categories - Enable Favourites")]
	        public bool categoriesFavourites = true;
	        
	        [JsonProperty("Categories - Favourites Permission (not required, if empty)")]
	        public string categoriesFavouritePerm = "";
	        
	        [JsonProperty("Categories - Enable Bank")]
	        public bool categoriesBank = true;
	        
	        [JsonProperty("Categories - Bank Permission (not required, if empty)")]
	        public string categoriesBankPerm = "";
	        
	        [JsonProperty("Categories - Default Selected Category Key")]
	        public string categoriesDefaultKey = "Resources";
	        
	        [JsonProperty("Categories - Blacklisted Categories")]
	        public List<string> categoriesBlacklisted = new();
	        
	        [JsonProperty("Categories - Custom Category Order (default, if empty)")]
	        public List<string> categoriesCustomOrder = new();
	        
	        [JsonProperty("Categories - Custom Category Icons (URL/RUST Path/Skin ID)")]
	        public Dictionary<string, string> categoriesIcons = new();
	        
	        [JsonProperty("Items - Shortnames Removed From Display")]
	        public List<string> itemsHiddenShortnames = new();
	        
	        [JsonProperty("Items - Custom Detail Info (skinId: languageKey)")]
	        public Dictionary<ulong, string> itemsCustomDetails = new();
	        
	        [JsonProperty("Items - Update Custom Item Display Names Based On Items In Inventories")]
	        public bool itemsUpdateNamedBasedOnInventories = false;
	        
	        [JsonProperty("Items - Make UI Displayed Names Multilingual (generates a lot of text in lang file)")]
	        public bool itemsNamesMultilingual = false;
	        
	        [JsonProperty("Bank - Default Limits")]
	        public BankLimitConfig bankDefaultLimits = new();
	        
	        [JsonProperty("Bank - Permission Limits (permission: LimitConfig)")]
	        public Dictionary<string, BankLimitConfig> bankPermLimits = new();
	        
	        [JsonProperty("Bank - Give Deposited Items To Redeem Inventory When Bank Permission Revoked")]
	        public bool bankMoveResourcesWhenNoAccess = true;
	        
	        [JsonProperty("Buy Offers - Enabled")]
	        public bool buyOffersEnabled = true;
	        
	        [JsonProperty("Buy Offers - Listing Times And Taxes")]
	        public List<TimeListingConfig> buyOfferTimes = new();
	        
	        [JsonProperty("Buy Offers - Disabled On Certain Listings")]
	        public Dictionary<string, List<ulong>> buyOfferDisabled = new();
	        
	        [JsonProperty("Sell Offers - Enabled")]
	        public bool sellOffersEnabled = true;
	        
	        [JsonProperty("Sell Offers - Listing Times And Taxes")]
	        public List<TimeListingConfig> sellOfferTimes = new();
	        
	        [JsonProperty("Sell Offers - Disabled On Certain Listings")]
	        public Dictionary<string, List<ulong>> sellOfferDisabled = new();
	        
	        [JsonProperty("Player Listings - Max Item Price (0, to float.MaxValue)")]
	        public float maxItemPrice = 0;
	        
	        [JsonProperty("Player Listings - Min Item Price")]
	        public float minItemPrice = 0.001f;
	        
	        [JsonProperty("Player Listings - Max Item Price Overrides (shortname: [ skinId: value ])")]
	        public Dictionary<string, Dictionary<ulong, float>> maxItemPriceOverrides = new();
	        
	        [JsonProperty("Player Listings - Min Item Prices (shortname: [ skinId: value ])")]
	        public Dictionary<string, Dictionary<ulong, float>> minItemPriceOverrides = new();
	        
	        [JsonProperty("Redeem Inventory Name (required for offline buy requests)")]
	        public string redeemInventoryName = "market";
	        
	        [JsonProperty("All Market Received Items Goes To Redeem Inventory")]
	        public bool itemsGoToRedeem = true;
	        
	        [JsonProperty("Player Listings - Default Limits")]
	        public MaxListingConfig listingLimits = new();
	        
	        [JsonProperty("Player Listings - Limits Permissions (permission: ListingConfig)")]
	        public Dictionary<string, MaxListingConfig> listingLimitPerms = new();
	        
	        [JsonProperty("Player Listings - Lock Listing Non-Specified Skins")]
	        public bool listingLockNonListedSkins = false;
	        
	        [JsonProperty("Server Sell Config")]
	        public ServerSellConfig sellCfg = new();
	        
	        [JsonProperty("Market Actions Log Config")]
	        public MarketLogConfig marketLog = new();
	        
	        [JsonProperty("Web API Config")]
	        public WebApiConfig webApi = new();
        }

        private class BankLimitConfig
        {
	        [JsonProperty("Max Individual Items (0, to disable)")]
	        public int maxIndividualItems = 0;
	        
	        [JsonProperty("Max Stacks Per Item (0, to disable)")]
	        public float maxStacksPerItem = 0;
	        
	        [JsonProperty("Max Amount Per Item (shortname/skin: limit))")]
	        public Dictionary<string, Dictionary<ulong, float>> maxAmountOverride = new();
        }

        private class TimeListingConfig
        {
	        [JsonProperty("Tax Amount (percentage of price)")]
	        public float taxAmount = 2;
	        
	        [JsonProperty("Required Permission To Use (not required, if empty)")]
	        public string reqPerm = "";
	        
	        [JsonProperty("Tax Amount Permissions (permission: percentage of price)")]
	        public Dictionary<string, float> taxAmountPerms = new();
	        
	        [JsonProperty("Listing Time (in hours, 24.7 - Wipe, -1 - No Limit)")]
	        public float listingTime = 6;
        }

        private class MaxListingConfig
        {
	        [JsonProperty("Max Buy Offers")]
	        public int buyOffers = 10;
	        
	        [JsonProperty("Max Sell Offers")]
	        public int sellOffers = 10;
        }

        private class ServerSellConfig
        {
	        [JsonProperty("Price Update - Interval (in minutes)")]
	        public float priceUpdateInterval = 30;
	        
	        [JsonProperty("Price Update - Certain Minutes")]
	        public List<int> priceUpdateCertainMinutes = new();
	        
	        [JsonProperty("Sell Price Multiplier - Multipliers (permission: multiplier)")]
	        public Dictionary<string, float> sellPriceMultipliers = new();
	        
	        [JsonProperty("Sell Price Multiplier - Blacklisted Items")]
	        public Dictionary<string, List<ulong>> sellPriceMultBlacklist = new();
	        
	        [JsonProperty("Sell Price Multiplier - Display Format")]
	        public string sellPriceFormat = "{0} <color=#5D7239>[{1}]</color>";
	        
	        [JsonProperty("Multiplier Events - Show On Chat")]
	        public bool showOnChat = false;
	        
	        [JsonProperty("Multiplier Events Discord - Channel ID (disabled, if empty) (DiscordCore required)")]
	        public string eventsDiscordChannelId = "";
	        
	        [JsonProperty("Multiplier Events Discord - Show Positive")]
	        public bool eventsDiscordShowPositive = true;
	        
	        [JsonProperty("Multiplier Events Discord - Show Negative")]
	        public bool eventsDiscordShowNegative = false;
	        
	        [JsonProperty("Bank Auto Sell - Enabled")]
	        public bool bankAutoSellEnabled = true;
	        
	        [JsonProperty("Bank Auto Sell - Required Permission (not required, if empty)")]
	        public string bankAutoSellPerm = "shoppystock.autosell";
	        
	        [JsonProperty("Bank Auto Sell - Enable Discord Integration (DiscordCore required)")]
	        public bool bankDiscordIntegration = true;
	        
	        [JsonProperty("Price Alert - Enabled")]
	        public bool priceAlertEnabled = true;
	        
	        [JsonProperty("Price Alert - Required Permission (not required, if empty)")]
	        public string priceAlertPerm = "shoppystock.pricealert";
	        
	        [JsonProperty("Price Alert - Enable Discord Integration (DiscordCore required)")]
	        public bool priceAlertDiscordIntegration = true;
	        
	        [JsonProperty("Purchase From Server - Enabled")]
	        public bool buyFromServerEnabled = true;
	        
	        [JsonProperty("Purchase From Server - Permission (not required, if empty)")]
	        public string buyFromServerPerm = "";
	        
	        [JsonProperty("Purchase From Server - Percentage Of Sold Items To Purchase")]
	        public float buyFromServerPercentage = 75;
	        
	        [JsonProperty("Purchase From Server - Minimal Purchase Price (percentage of max sell price)")]
	        public float buyFromServerMinPrice = 125;
	        
	        [JsonProperty("Purchase From Server - Maximal Purchase Price (percentage of max sell price)")]
	        public float buyFromServerMaxPrice = 250;

	        [JsonProperty("Price Chart - Enabled")] 
	        public bool priceChart = true;
	        
	        [JsonProperty("Price Chart - Permission (not required, if empty)")]
	        public string priceChartPerm = "";
	        
	        [JsonProperty("Price Chart - Timespan Permissions (timeInMinutes: permission[optional])")]
	        public Dictionary<int, string> priceChartTimespanPerms = new();
	        
	        [JsonProperty("Price Calculator")]
	        public PriceCalculatorConfig priceCalc = new();
        }
        
        private class PriceCalculatorConfig
        {
            [JsonProperty("Price Change - Price Fluctuation Percentage")]
            public float priceFluctuation = 6f;

            [JsonProperty("Price Change - Same Price Actions Min")]
            public int sameActionsMin = 3;

            [JsonProperty("Price Change - Same Price Actions Max")]
            public int sameActionsMax = 6;

            [JsonProperty("Price Change - Chances To Increment Based On Current Price Percentage (pricePercentage: incrementChance[0-100])")]
            public Dictionary<float, float> priceBarriers = new();

            [JsonProperty("Price Drop - Amount Sell Values Penalty Multiplier (percentage from amount)")]
            public Dictionary<float, float> priceDropChart = new();

            [JsonProperty("Price Drop - Amount Sold Max Price Penalty (dsacPercentage: PenaltyConfig)")]
            public Dictionary<float, PricePenaltyConfig> sellPricePenalty = new();

            [JsonProperty("Price Increase - DSAC Not Achieved (dsacPercentage: priceIncreaseMultiplier)")]
            public Dictionary<float, float> goalAchievedChart = new();

            [JsonProperty("Default Sell Amount Calculation - Players Online Multiplier (playersOnline: multiplier)")]
            public Dictionary<int, float> sellAmountOnlineMultiplier = new();

            [JsonProperty("Price Multipliers - Minimal Time Distance Between Events (in price update ticks)")]
            public int minTimeDistance = 24;

            [JsonProperty("Price Multipliers - Chance To Appear Based On Sold Amount (dsacPercentage: chance[0-100])")]
            public Dictionary<float, float> multplierAmountChance = new();

            [JsonProperty("Price Multipliers - Minimal Actions Time")]
            public int multiplierMinLength = 2;

            [JsonProperty("Price Multipliers - Maximal Actions Time")]
            public int multiplierMaxLength = 4;
	        
            [JsonProperty("Price Multiplier Events (eventName: EventConfig)")]
            public Dictionary<string, SellMultiplierConfig> events = new();

            [JsonProperty("Positive Multipliers - Max Price (percentage of max price)")]
            public float positiveMaxPrice = 50;

            [JsonProperty("Negative Multipliers - Min Price (percentage of max price)")]
            public float negativeMinPrice = 40;
        }

        private class PricePenaltyConfig
        {
	        [JsonProperty("Max Price Percentage")]
	        public float percentage = 0;

	        [JsonProperty("Penalty Length (in price update ticks)")]
	        public int penaltyLength = 0;
        }

        private class SellMultiplierConfig
        {
	        [JsonProperty("Weight")] 
	        public int weight = 1;
	        
	        [JsonProperty("Multiplier - Min")] 
	        public float min = 1;
	        
	        [JsonProperty("Multiplier - Max")] 
	        public float max = 2;
        }

        private class MarketLogConfig
        {
	        [JsonProperty("Both - Show Buyer Nickname")]
	        public bool showBuyerNickname = false;
	        
	        [JsonProperty("UI Tab - Enabled")]
	        public bool tabEnabled = true;
	        
	        [JsonProperty("UI Tab - Required Permission (not required, if empty)")]
	        public string tabReqPerm = "";
	        
	        [JsonProperty("UI Tab - Sell Offers")]
	        public bool tabSellOffers = true;
	        
	        [JsonProperty("UI Tab - Buy Offers")]
	        public bool tabBuyOffers = true;
	        
	        [JsonProperty("UI Tab - Sold Items")]
	        public bool tabSoldItems = true;
	        
	        [JsonProperty("UI Tab - Purchased Items")]
	        public bool tabPurchasedItems = true;
	        
	        [JsonProperty("UI Tab - Price Rolls")]
	        public bool tabPriceRolls = false;
	        
	        [JsonProperty("UI Tab - Demands")]
	        public bool tabDemands = false;
	        
	        [JsonProperty("UI Tab - Alerts/Auto-Sell")]
	        public bool tabAlerts = false;
	        
	        [JsonProperty("Chat Messages - Sell Offers")]
	        public bool chatSellOffers = false;
	        
	        [JsonProperty("Chat Messages - Buy Offers")]
	        public bool chatBuyOffers = false;
	        
	        [JsonProperty("Chat Messages - Sold Items")]
	        public bool chatSoldItems = false;
	        
	        [JsonProperty("Chat Messages - Purchased Items")]
	        public bool chatPurchasedItems = false;
	        
	        [JsonProperty("Chat Messages - Price Rolls")]
	        public bool chatPriceRolls = false;
	        
	        [JsonProperty("Chat Messages - Demands")]
	        public bool chatDemands = false;
	        
	        [JsonProperty("Chat Messages - Alerts/Auto-Sell")]
	        public bool chatAlerts = false;
        }

        private class WebApiConfig
        {
	        [JsonProperty("Enabled")]
	        public bool enabled = false;
	        
	        [JsonProperty("URL")]
	        public string url = "";
	        
        }
        
        private class TransferConfig
        {
	        [JsonProperty("Enabled")] 
	        public bool enabled = true;
		        
	        [JsonProperty("Daily Transfer Limit (0, to disable)")]
	        public float dailyTransferLimit = 0;
		        
	        [JsonProperty("Daily Transfer Limit Permissions (permission: limit)")]
	        public Dictionary<string, float> dailyTransferLimitPerms = new();
		        
	        [JsonProperty("Wipe Transfer Limit (0, to disable)")]
	        public float wipeTransferLimit = 0;
		        
	        [JsonProperty("Wipe Transfer Limit Permissions (permission: limit)")]
	        public Dictionary<string, float> wipeTransferLimitPerms = new();
		        
	        [JsonProperty("Currency Transfer Tax (percentage of transfer, 0 to disable)")]
	        public float tax = 0;
		        
	        [JsonProperty("Currency Transfer Tax Permissions (permission: limit)")]
	        public Dictionary<string, float> taxPerms = new();
        }
        
        private class ExchangesConfig
        {
	        [JsonProperty("Enabled")]
	        public bool enabled = false;
	        
	        [JsonProperty("Exchange List")]
	        public List<ExchangeConfig> exchanges = new();
        }
        
        private class ExchangeConfig
        {
	        [JsonProperty("Required Permission (not required, if empty)")]
	        public string perm = "";
	        
	        [JsonProperty("Acquired Currency Amount")]
	        public float amountReceived = 1;
	        
	        [JsonProperty("Required Currency - Name (will work on item, if empty)")]
	        public string currName = "";

	        [JsonProperty("Required Currency - Amount")]
	        public float currAmount = 0;
	        
	        [JsonProperty("Required Item - Shortname")]
	        public string itemShortname = "";

	        [JsonProperty("Required Item - Amount")]
	        public int itemAmount = 0;

	        [JsonProperty("Required Item - Skin")]
	        public ulong itemSkin = 0;

	        [JsonProperty("Required Item - UI Display Name")]
	        public string itemDisplayName = "";
        }
        
        private class DepositConfig
        {
	        [JsonProperty("Deposit - Enabled")]
	        public bool deposit = true;
	        
	        [JsonProperty("Deposit - Required Permission (not required, if empty)")]
	        public string depositPerm = "";
	        
	        [JsonProperty("Withdraw - Enabled")]
	        public bool withdraw = false;
	        
	        [JsonProperty("Withdraw - Required Permission (not required, if empty)")]
	        public string withdrawPerm = "";

	        [JsonProperty("Withdraw - Items")]
	        public List<WithdrawItemConfig> withdrawItems = new();
        }

        private class WithdrawItemConfig
        {
	        [JsonProperty("Shortname")] 
	        public string shortname = "";
	        
	        [JsonProperty("Skin ID")] 
	        public ulong skinId = 0;
	        
	        [JsonProperty("Item Custom Name")] 
	        public string name = "";
	        
	        [JsonProperty("Value")] 
	        public float value = 1;

	        [JsonProperty("Item Display Name (For UI Purposes)")]
	        public string displayName = "";
        }
        
        private class NPCConfig
        {
	        [JsonProperty("Shops Available (shopName: [ categoriesList ])")]
	        public Dictionary<string, List<string>> shops = new();
	        
	        [JsonProperty("Stock Markets Available")]
	        public List<string> stocks = new();
	        
	        [JsonProperty("Transfer Available")]
	        public bool transfer = true;
	        
	        [JsonProperty("Exchanges Available")]
	        public bool exchanges = true;
	        
	        [JsonProperty("Deposits Available")]
	        public bool deposits = true;
	        
	        [JsonProperty("Withdraws Available")]
	        public bool withdraws = true;
        }

        private class UIColorConfig
        {
	        [JsonProperty("Transparent")]
	        public string Transparent = "1 1 1 0";
	        
	        [JsonProperty("White")]
	        public string White = "1 1 1 1";
	        
	        [JsonProperty("White Transparent 40%")]
	        public string WhiteTrans40 = "1 1 1 0.4";
	        
	        [JsonProperty("White Transparent 80%")]
	        public string WhiteTrans80 = "1 1 1 0.8";
	        
	        [JsonProperty("Black Transparent 10%")]
	        public string BlackTrans10 = "0 0 0 0.1";
	        
	        [JsonProperty("Black Transparent 20%")]
	        public string BlackTrans20 = "0 0 0 0.2";
	        
	        [JsonProperty("Green Background")]
	        public string GreenBg = "0.365 0.447 0.224 1";
	        
	        [JsonProperty("Green Text")]
	        public string GreenText = "0.82 1 0.494 1";
	        
	        [JsonProperty("Red Background")]
	        public string RedBg = "0.667 0.278 0.204 1";
	        
	        [JsonProperty("Red Text")]
	        public string RedText = "1 0.647 0.58 1";
	        
	        [JsonProperty("Dark Gray")]
	        public string DarkGray = "0.153 0.141 0.114 1";
	        
	        [JsonProperty("Light To Dark Gray 5%")]
	        public string LTD5 = "0.196 0.18 0.153 1";
	        
	        [JsonProperty("Light To Dark Gray 10%")]
	        public string LTD10 = "0.239 0.224 0.196 1";
	        
	        [JsonProperty("Light To Dark Gray 15%")]
	        public string LTD15 = "0.282 0.263 0.235 1";
	        
	        [JsonProperty("Light To Dark Gray 20%")]
	        public string LTD20 = "0.325 0.306 0.275 1";
	        
	        [JsonProperty("Light To Dark Gray 30%")]
	        public string LTD30 = "0.412 0.388 0.357 1";
	        
	        [JsonProperty("Light To Dark Gray 40%")]
	        public string LTD40 = "0.498 0.471 0.439 1";
	        
	        [JsonProperty("Light To Dark Gray 50%")]
	        public string LTD50 = "0.58 0.553 0.518 1";
	        
	        [JsonProperty("Light To Dark Gray 60%")]
	        public string LTD60 = "0.667 0.635 0.6 1";
	        
	        [JsonProperty("Light To Dark Gray 80%")]
	        public string LTD80 = "0.839 0.8 0.761 1";
	        
	        [JsonProperty("Light Gray")]
	        public string LightGray = "0.969 0.922 0.882 1";
	        
	        [JsonProperty("Light Gray Transparent")]
	        public string LightGrayTransRust = "0.969 0.922 0.882 0.039";
	        
	        [JsonProperty("Favourite Star")]
	        public string Favourite = "1 0.878 0 0.902";
        }
        
        #endregion
        
        #region Data

        private void GenerateShopConfig(string key, ShopDataConfig sdc)
        {
	        sdc.categories.Clear();
	        ShopConfig sc = config.curr[key].shopCfg;
	        if (sc.generateItems)
	        {
		        bool customPrices = ItemCostCalculator != null;
		        Dictionary<string, double> prices = customPrices ? ItemCostCalculator.Call<Dictionary<string, double>>("GetItemsCostByShortName") : null;
		        foreach (var item in ItemManager.itemList)
		        {
			        string cat = item.category.ToString();
			        sdc.categories.TryAdd(cat, new());
			        string fixedShortname = item.shortname.Replace(" ", "_");
			        float price = 1;
			        if (customPrices && prices.TryGetValue(item.shortname, out double customPrice))
				        price = (float)customPrice;
			        sdc.categories[cat].listings.Add(fixedShortname, new() { displayName = item.displayName.english, shortname = item.shortname, price = price });
		        }
		        LogToConsole("NewShopConfigGenerated", key);
	        }
	        else
	        {
		        sdc.categories.Add("Category1", new());
		        sdc.categories.Add("Category2", new());
		        sdc.categories.Add("Category3", new());
		        ShopCategoryConfig scc = sdc.categories["Category1"];
		        scc.icon = "assets/icons/voice.png";
		        scc.displayBlacklistPerms = new() { "somepermissionfrom.otherplugin", "shoppystock.lock1" };
		        scc.discountPerms = new() { { "shoppystock.discountcategory1", 25 }, { "shoppystock.discountcategory1evenmore", 40 } };
		        scc.listings.Add("CommandExample1", new()
		        {
			        commands = new() { "say {userName} ({userId}) purchased command example command standing at (userPosX}, userPosY}, userPosZ}).", "say It supports multiple commands!" },
			        displayName = "Test Command Purchase",
			        iconUrl = "assets/icons/store.png",
			        price = 1000,
			        discounts = new() { { "shoppystock.discountcommand.1", 700 }, { "shoppystock.discountcommand.2", 450 } },
			        dailyBuy = 3,
			        dailyBuyPerms = new() { { "shoppystock.limitdaily.1", 4 }, { "shoppystock.limitdaily.2", 5 } },
			        wipeBuy = 10,
			        wipeBuyPerms = new() { { "shoppystock.limitwipe.1", 20 }, { "shoppystock.limitwaily.2", 30 } },
			        cooldown = 60,
			        cooldownPerms = new() { { "shoppystock.cooldown.1", 45 }, { "shoppystock.cooldown.2", 30 } },
		        });
		        scc.listings.Add("ItemExample", new()
		        {
			        shortname = "box.repair.bench",
			        skin = 2795785961,
			        itemName = "Recycler",
			        displayName = "Placeable Recycler",
			        price = 250,
			        pricePerPurchaseMultiplier = 1.1f,
			        disableDiscount = true
		        });
		        scc = sdc.categories["Category2"];
		        scc.displayPerm = "shoppystock.testcategory";
		        scc.listings.Add("ItemExampleTest1", new()
		        {
			        shortname = "wood",
			        displayName = "Wood",
			        price = 10
		        });
		        scc.listings.Add("ItemExampleTest2", new()
		        {
			        shortname = "stones",
			        displayName = "Stones",
			        price = 15
		        });
		        scc = sdc.categories["Category3"];
		        scc.rollInterval = 30;
		        scc.listings.Add("ItemExample1", new()
		        {
			        shortname = "wood",
			        displayName = "Wood (Listing #1)",
			        price = 10,
			        weight = 5,
		        });
		        scc.listings.Add("ItemExample2", new()
		        {
			        shortname = "stones",
			        displayName = "Stones (Listing #2)",
			        price = 15
		        });
		        scc.listings.Add("ItemExample3", new()
		        {
			        shortname = "metal.fragments",
			        displayName = "Metal Fragments (Listing #3)",
			        price = 25
		        });
		        scc.listings.Add("ItemExample4", new()
		        {
			        shortname = "sulfur",
			        displayName = "Sulfur (Listing #4)",
			        price = 18
		        });
		        scc.listings.Add("ItemExample5", new()
		        {
			        shortname = "hq.metal.ore",
			        displayName = "High Quality Metal Ore (Listing #5)",
			        price = 55
		        });
		        scc.listings.Add("ItemExample6", new()
		        {
			        shortname = "cloth",
			        displayName = "Cloth (Listing #6)",
			        price = 6
		        });
		        scc.listings.Add("ItemExample7", new()
		        {
			        shortname = "leather",
			        displayName = "Leather (Listing #7)",
			        price = 11
		        });
	        }
	        sdc.generate = false;
	        LogToConsole("NewShopConfigGenerated", key);
	        SaveShopDataConfig(key);
        }

        private void GenerateStockConfig(string key, StockDataConfig sdc)
        {
	        sdc.customItems.Clear();
	        sdc.sellItems.Clear();
	        sdc.customItems.Add("explosive.timed", new());
	        sdc.customItems["explosive.timed"].Add(3040141794, new() { displayName = "Green Timed Explosive", category = "CustomItems" });
	        sdc.customItems["explosive.timed"].Add(3040141886, new() { displayName = "Yellow Timed Explosive", category = "CustomItems" });
	        sdc.customItems["explosive.timed"].Add(3040141964, new() { displayName = "Red Timed Explosive", category = "CustomItems" });
	        sdc.customItems.Add("ammo.rocket.basic", new());
	        sdc.customItems["ammo.rocket.basic"].Add(3040142076, new() { displayName = "Green Rocket", category = "CustomItems" });
	        sdc.customItems.Add("wood", new());
	        sdc.customItems["wood"].Add(2325417548, new() { displayName = "Cherry Wood", category = "CustomItems" });
	        sdc.sellItems.Add("wood", new());
	        sdc.sellItems["wood"].Add(0, new() { dsac = 50000, minPrice = 0.1f, maxPrice = 0.4f, maxSellAmount = 10000, maxSellAmountPerms = new() { { "shoppystock.selllimit.1", 15000 } } });
	        sdc.sellItems["wood"].Add(2325417548, new() { dsac = 1000, minPrice = 1.0f, maxPrice = 1.0f, priceParentShortname = "wood", priceParentMinPrice = 0.5f, priceParentMaxPrice = 1.0f });
	        sdc.sellItems.Add("stones", new());
	        sdc.sellItems["stones"].Add(0, new() { dsac = 100000, minPrice = 0.08f, maxPrice = 0.25f, maxSellAmount = 20000, maxSellAmountPerms = new() { { "shoppystock.selllimit.1", 30000 } } });
	        sdc.generate = false;
	        LogToConsole("NewStockConfigGenerated", key);
	        SaveStockDataConfig(key);
	        
        }

        private static PluginData data = new();

        private class PluginData
        {
            public Dictionary<string, ShopData> shop = new();
            public Dictionary<string, StockData> stock = new();
            public Dictionary<string, TransferData> transfer = new();
            public CurrencyData currencies = new();
        }
        private class ShopData
        {
	        public ShopDataConfig cfg = new();
	        public ShopStatsData stats = new();
        }

        private class ShopDataConfig
        {
	        [JsonProperty("Regenerate Categories (if true, will wipe config to default state after reload)")]
	        public bool generate = true;
	        
	        [JsonProperty("Shop Categories")]
	        public Dictionary<string, ShopCategoryConfig> categories = new();
        }

        private class ShopCategoryConfig
        {
	        [JsonProperty("Icon (URL/RUST Path/Skin ID)")]
	        public string icon = "";
	        
	        [JsonProperty("Required Permission To Show (not required, if empty)")]
	        public string displayPerm = "";
	        
	        [JsonProperty("Display Blacklist Permissions (need to have all)")]
	        public List<string> displayBlacklistPerms = new();
	        
	        [JsonProperty("Discount Permissions (permission: percentage)")]
	        public Dictionary<string, float> discountPerms = new();
	        
	        [JsonProperty("Offer Roll - Interval (in minutes, 0 to disable)")]
	        public float rollInterval = 0; 
	        
	        [JsonProperty("Offer Roll - Broadcast Update On Chat")]
	        public bool rollBroadcastChat = false;
	        
	        [JsonProperty("Offer Roll - Max Offers")]
	        public int rollMaxOffers = 3;
	        
	        [JsonProperty("EACH LISTING NEEDS TO HAVE UNIQUE KEY!")]
	        public string info = "LISTING WITHOUT UNIQUE KEY WILL NOT BE DISPLAYED. IT CAN BE ANYTHING WITHOUT WHITESPACES.";
	        
	        [JsonProperty("Listings")]
	        public Dictionary<string, ShopListingConfig> listings = new();
        }

        private class ShopListingConfig
        {
	        [JsonProperty("Console Commands Ran On Purchase (if set, ignores item)")]
	        public List<string> commands = new();

	        [JsonProperty("Item Shortname")]
	        public string shortname = "";

	        [JsonProperty("Item Skin ID")]
	        public ulong skin = 0;

	        [JsonProperty("Item Amount")]
	        public int amount = 1;

	        [JsonProperty("Custom Item Name")]
	        public string itemName = "";

	        [JsonProperty("Shop Display Name")]
	        public string displayName = "";

	        [JsonProperty("Is Blueprint")]
	        public bool blueprint = false;

	        [JsonProperty("Player Blueprint Ownership Required")]
	        public bool blueprintOwnerRequired = false;

	        [JsonProperty("Player Skin Ownership Required")]
	        public bool skinOwnerRequired = false;

	        [JsonProperty("Icon URL/RUST Path/Skin ID (for command listings)")]
	        public string iconUrl = "";

	        [JsonProperty("Price")]
	        public float price = 1;

	        [JsonProperty("Price Per Purchase Multiplier")]
	        public float pricePerPurchaseMultiplier = 1;

	        [JsonProperty("Multiply Price Per Daily (true) Or Per Wipe (false) Purchases")]
	        public bool multiplyPricePerDaily = true;

	        [JsonProperty("Show Description Field (generates line in language file)")]
	        public bool description = false;

	        [JsonProperty("Discount Permissions (permission: discountedPrice)")]
	        public Dictionary<string, int> discounts = new();

	        [JsonProperty("Disable Discount")]
	        public bool disableDiscount = false;

	        [JsonProperty("Required Permission To Display")]
	        public string permission = "";

	        [JsonProperty("Display Blacklist Permission")]
	        public string blacklistPermission = "";

	        [JsonProperty("Count Limits For Whole Server (not per player)")]
	        public bool globalLimit = false;

	        [JsonProperty("Daily Buy Max")]
	        public int dailyBuy = 0;

	        [JsonProperty("Daily Buy Permissions (permission: limit)")]
	        public Dictionary<string, int> dailyBuyPerms = new();

	        [JsonProperty("Wipe Buy Max")]
	        public int wipeBuy = 0;

	        [JsonProperty("Wipe Buy Permissions (permission: limit)")]
	        public Dictionary<string, int> wipeBuyPerms = new();

	        [JsonProperty("Limit Mass Purchase To One")]
	        public bool limitToOne = false;

	        [JsonProperty("Cooldown Between Purchases (in seconds, 0 to disable)")]
	        public float cooldown = 0;

	        [JsonProperty("Cooldown Permissions (permission: time)")]
	        public Dictionary<string, float> cooldownPerms = new();

	        [JsonProperty("Offer Roll - Weight (chance)")]
	        public int weight = 1;
        }

        private class ShopStatsData
        {
	        //<UserId, <Date, <Category, <ListingId, Count>>>>
	        public Dictionary<ulong, Dictionary<string, Dictionary<string, Dictionary<string, int>>>> playerDailyPurchases = new();
	        //<UserId, <Category, <ListingId, Count>>>
	        public Dictionary<ulong, Dictionary<string, Dictionary<string, int>>> playerWipePurchases = new();
	        //<Category, <ListingId, <UserId>>>
	        public Dictionary<string, Dictionary<string, List<ulong>>> uniquePurchases = new();
	        //<UserId, <Category, <ListingId, Date>>>
	        public Dictionary<ulong, Dictionary<string, Dictionary<string, DateTime>>> playerLastPurchases = new();
	        //<Category, RolledOffersData>
	        public Dictionary<string, RolledOffersData> rolledOffers = new();
        }

        private class RolledOffersData
        {
	        public DateTime nextRollTime = DateTime.MinValue;
	        public float cachedInterval = 0;
	        public HashSet<string> offers = new();
        }

        private class StockData
        {
	        public StockDataConfig cfg = new();
	        public StockPlayersData playerData = new();
	        public StockListingsData listings = new();
	        public StockPriceData prices = new();
	        public StockStatsData stats = new();
        }

        private class StockDataConfig
        {
	        [JsonProperty("Regenerate Items (if true, will wipe config to default state after reload)")]
	        public bool generate = true;
	        
	        [JsonProperty("Custom Item Listings (shortname : [ skinId : ItemConfig ])")]
	        public Dictionary<string, Dictionary<ulong, StockItemConfig>> customItems = new();
		        
	        [JsonProperty("Server Sell Items (shortname : [ skinId : ItemConfig ])")]
	        public Dictionary<string, Dictionary<ulong, SellItemConfig>> sellItems = new();
        }

        private class StockItemConfig
        {
	        [JsonProperty("Category Key")]
	        public string category = "";
	        
	        [JsonProperty("UI Display Name")]
	        public string displayName = "";
	        
	        [JsonProperty("Buy From Server Item Name")]
	        public string buyName = "";
        }

        private class SellItemConfig
        {
	        [JsonProperty("Default Sell Amount Calculation (DSAC) (Read On Website)")]
	        public int dsac = 1000;
	        
	        [JsonProperty("Price - Min")]
	        public float minPrice = 1;
	        
	        [JsonProperty("Price - Max")]
	        public float maxPrice = 1;
	        
	        [JsonProperty("Price Parent - Shortname (ignore, if empty)")]
	        public string priceParentShortname = "";
	        
	        [JsonProperty("Price Parent - Skin ID")]
	        public ulong priceParentSkin = 0;
	        
	        [JsonProperty("Price Parent - Min Boost")]
	        public float priceParentMinPrice = 0;
	        
	        [JsonProperty("Price Parent - Max Boost")]
	        public float priceParentMaxPrice = 0;

	        [JsonProperty("Minimal Sell Amount")]
	        public int minSellAmount = 1;

	        [JsonProperty("Max Daily Sell Amount (-1, to disable)")]
	        public int maxSellAmount = -1;

	        [JsonProperty("Max Daily Sell Amount Permissions (permission: limit)")]
	        public Dictionary<string, int> maxSellAmountPerms = new();

	        [JsonProperty("Allow Re-Purchase From Server")]
	        public bool purchaseFromServer = true;

	        [JsonProperty("Override Skinned Items As Unskinned (works only for configuration of skinId 0)")]
	        public bool allowSkinned = false;
        }

        private class StockPlayersData
        {
	        public Dictionary<ulong, StockPlayerData> players = new();
        }

        private class StockPlayerData 
        {
	        public Dictionary<string, Dictionary<ulong, PlayerAlertData>> alerts = new();
	        public Dictionary<string, List<ulong>> favourites = new();
	        public Dictionary<string, Dictionary<ulong, StockBankData>> bankItems = new();
	        public bool disabledChatAlerts = false;
        }

        private class PlayerAlertData
        {
	        public float alertPrice = 0;
	        public float instaSellPrice = 0;
        }

        private class StockBankData
        {
	        public int amount = 0;
	        public string displayName = "";
	        public string itemName = "";
        }

        private class StockListingsData
        {
	        public Dictionary<string, Dictionary<ulong, List<StockListingData>>> buyOffers = new();
	        public Dictionary<string, Dictionary<ulong, List<StockListingData>>> sellOffers = new();
        }

        private class StockListingData
        {
	        public string userName = "";
	        public ulong userId = 0;
	        public float price = 0;
	        public bool isCancelled = false;
	        public bool isHidden = false;
	        public int customAccessCode = -1;
	        public DateTime listingEndTime;
	        public ItemData item;
        }

        private class StockPriceData
        {
	        public Dictionary<string, Dictionary<ulong, SellItemData>> items = new();
        }

        private class SellItemData
        {
	        public float multiplier = 1;
	        public int multiplierLength = 0;
	        public float price;
	        public int sellAmount = 0;
	        public int penaltyLength = 0;
	        public int nextMultiplierEvent = 0;
	        public bool isDecreasing = false;
	        public int actionCount = 0;
	        public int actionGoal = 0;
	        public float buyPrice = 0;
	        public int amountAvailable = 0;
	        public List<float> priceHistory = new();
	        public List<int> sellAmountHistory = new();
	        public Dictionary<int, int> nextGraphUpdates = new();
	        public Dictionary<int, GraphData> cachedGraphs = new();
        }

        private struct GraphData
        {
	        public float minPrice;
	        public float maxPrice;
	        public uint imageId;
        }

        private class StockStatsData
        {
	        public Dictionary<ulong, Dictionary<string, float>> playerDailyEarnings = new();
	        public Dictionary<ulong, Dictionary<string, Dictionary<string, Dictionary<ulong, int>>>> playerDailySoldItems = new();
	        public Dictionary<ulong, Dictionary<string, Dictionary<string, Dictionary<ulong, int>>>> playerDailyPurchasedItems = new();
	        public int actionCounter = 0;
	        public Dictionary<int, ActionData> actions = new();
        }

        private class ActionData
        {
	        public string date;
	        public StockActionType type;
	        public string field1;
	        public string field2;
	        public string field3;
	        public string field4;
	        public string field5;
        }

        private enum StockActionType
        {
			SellOffer,
	        BuyOffer,
	        SoldItem,
	        PurchasedItem,
	        PriceRoll,
	        Demand,
	        Demand_Neg,
	        AutoSell,
	        Alert
        }

        private class TransferData
        {
	        public Dictionary<ulong, PlayerTransferData> players = new();
        }

        private class PlayerTransferData
        {
	        public Dictionary<string, float> dailyTransfers = new();
	        public float wipeTransfer = 0;
        }

        private class CurrencyData
        {
	        public DateTime lastWipeDate = DateTime.MinValue;
	        public int savedProtocol = -1;
	        public string lastMapName = string.Empty;
	        public Dictionary<ulong, PlayerData> players = new();
        }

        private class PlayerData
        {
	        public string userName = "";
	        public Dictionary<string, float> currencies = new();
        }
        
        private void LoadData()
        {
	        try
	        {
		        foreach (var curr in config.curr)
		        {
			        if (curr.Value.shopCfg.enabled)
			        {
				        data.shop.TryAdd(curr.Key, new());
				        data.shop[curr.Key].cfg = Interface.Oxide.DataFileSystem.ReadObject<ShopDataConfig>($"{Name}/Shops/Configs/{curr.Key}");
				        data.shop[curr.Key].stats = Interface.Oxide.DataFileSystem.ReadObject<ShopStatsData>($"{Name}/Shops/Statistics/{curr.Key}");
			        }
			        if (curr.Value.stockCfg.enabled)
			        {
				        data.stock.TryAdd(curr.Key, new());
				        data.stock[curr.Key].cfg = Interface.Oxide.DataFileSystem.ReadObject<StockDataConfig>($"{Name}/StockMarkets/Configs/{curr.Key}");
				        data.stock[curr.Key].playerData = Interface.Oxide.DataFileSystem.ReadObject<StockPlayersData>($"{Name}/StockMarkets/PlayersData/{curr.Key}");
				        data.stock[curr.Key].listings = Interface.Oxide.DataFileSystem.ReadObject<StockListingsData>($"{Name}/StockMarkets/Listings/{curr.Key}");
				        data.stock[curr.Key].prices = Interface.Oxide.DataFileSystem.ReadObject<StockPriceData>($"{Name}/StockMarkets/PriceCache/{curr.Key}");
				        data.stock[curr.Key].stats = Interface.Oxide.DataFileSystem.ReadObject<StockStatsData>($"{Name}/StockMarkets/Statistics/{curr.Key}");
			        }
			        if (curr.Value.transferCfg.enabled)
						data.transfer[curr.Key] = Interface.Oxide.DataFileSystem.ReadObject<TransferData>($"{Name}/TransferData/{curr.Key}");
		        }
		        data.currencies = Interface.Oxide.DataFileSystem.ReadObject<CurrencyData>($"{Name}/playersData");
		        if (data.currencies.lastWipeDate == DateTime.MinValue)
		        {
			        data.currencies.lastWipeDate = DateTime.Now;
			        SaveData();
		        }
		        timer.Every(Core.Random.Range(500, 700), SaveData);
		        validDataLoad = true;
	        }
	        catch (Exception e)
	        {
		        Puts($"Error while parsing ShoppyStock data. Error: {e}");
		        throw;
	        }
        }

        private void SaveData()
        {
	        if (!validDataLoad) return;
	        foreach (var curr in config.curr)
	        {
		        if (curr.Value.shopCfg.enabled)
					Interface.Oxide.DataFileSystem.WriteObject($"{Name}/Shops/Statistics/{curr.Key}", data.shop[curr.Key].stats);
		        if (curr.Value.stockCfg.enabled)
		        {
			        Interface.Oxide.DataFileSystem.WriteObject($"{Name}/StockMarkets/PlayersData/{curr.Key}", data.stock[curr.Key].playerData);
			        Interface.Oxide.DataFileSystem.WriteObject($"{Name}/StockMarkets/Listings/{curr.Key}", data.stock[curr.Key].listings);
			        Interface.Oxide.DataFileSystem.WriteObject($"{Name}/StockMarkets/PriceCache/{curr.Key}", data.stock[curr.Key].prices);
			        Interface.Oxide.DataFileSystem.WriteObject($"{Name}/StockMarkets/Statistics/{curr.Key}", data.stock[curr.Key].stats);
		        }
	        }
	        Interface.Oxide.DataFileSystem.WriteObject($"{Name}/playersData", data.currencies);
	        if (config.consoleLogsToFile)
	        {
		        SaveLogs();
		        LogToConsole("DataAndLogsSaved");
	        }
	        else
		        LogToConsole("DataSaved");
        }

        private void SaveShopDataConfig(string key = null)
        {
	        if (!validDataLoad) return;
	        if (!string.IsNullOrEmpty(key) && config.curr[key].shopCfg.enabled)
		        Interface.Oxide.DataFileSystem.WriteObject($"{Name}/Shops/Configs/{key}", data.shop[key].cfg);
	        else
		        foreach (var curr in config.curr)
			        if (curr.Value.shopCfg.enabled)
						Interface.Oxide.DataFileSystem.WriteObject($"{Name}/Shops/Configs/{curr.Key}", data.shop[curr.Key].cfg);
        }

        private void SaveStockDataConfig(string key = null)
        {
	        if (!validDataLoad) return;
	        if (!string.IsNullOrEmpty(key) && config.curr[key].stockCfg.enabled)
		        Interface.Oxide.DataFileSystem.WriteObject($"{Name}/StockMarkets/Configs/{key}", data.stock[key].cfg);
	        else
		        foreach (var curr in config.curr)
			        if (curr.Value.stockCfg.enabled)
						Interface.Oxide.DataFileSystem.WriteObject($"{Name}/StockMarkets/Configs/{curr.Key}", data.stock[curr.Key].cfg);
        }
        
        #endregion
        
        #region Chart Drawing
        
#if CARBON
        public class ChartDrawThread : BaseThreadedJob
#else
	    public class ChartDrawThread
#endif
        {
	        public int recordsPerBar = 1;
	        public List<float> priceHistory;
	        public uint targetImageId;
	        public Action<uint> onProcessEnded;
	        
	        public uint imageId;
#if CARBON
	        public override void ThreadFunction()
#else
	        public IEnumerator WaitFor()
#endif
	        {
		        try
		        {
			        using Bitmap cachedMap = new(3000, 340);
			        using System.Drawing.Graphics graphic = System.Drawing.Graphics.FromImage(cachedMap); 
			        using Brush greenPen = new SolidBrush(ColorTranslator.FromHtml("#5D7239"));
			        using Brush redPen = new SolidBrush(ColorTranslator.FromHtml("#AA4734"));
			        graphic.Clear(System.Drawing.Color.Transparent);
			        graphic.SmoothingMode = SmoothingMode.AntiAlias;
			        graphic.CompositingQuality = CompositingQuality.HighQuality;
			        graphic.PageUnit = GraphicsUnit.Display;
			        graphic.TextRenderingHint = TextRenderingHint.AntiAlias;
			        graphic.InterpolationMode = InterpolationMode.HighQualityBilinear;

			        int barCount = 0;
			        int counter = recordsPerBar;
			        float maxFoundPrice = float.MinValue;
			        float minFoundPrice = float.MaxValue;
			        foreach (var price in priceHistory)
			        {
				        if (counter == 0)
				        {
					        counter = recordsPerBar;
					        if (barCount++ > 200) break;
				        }
				        if (price < minFoundPrice)
					        minFoundPrice = price;
				        if (price > maxFoundPrice)
					        maxFoundPrice = price;
				        counter--;
			        }
			        counter = recordsPerBar; 
			        float minPrice = float.MaxValue;
			        float maxPrice = float.MinValue;
			        float endPrice = 0;
			        barCount = 0;
			        int position = 3000;
			        foreach (var price in priceHistory)
			        {
				        if (price < minPrice) 
					        minPrice = price;
				        if (price > maxPrice)
					        maxPrice = price;
				        counter--;
				        if (counter == 0)
				        {
					        bool increased = price < endPrice;
					        counter = recordsPerBar;
					        int minPricePercentage = Mathf.RoundToInt((minPrice - minFoundPrice) / (maxFoundPrice - minFoundPrice) * 332f);
					        int maxPricePercentage = Mathf.RoundToInt((maxPrice - minFoundPrice) / (maxFoundPrice - minFoundPrice) * 332f);
					        if (minPricePercentage == maxPricePercentage)
					        {
						        minPricePercentage--; 
						        maxPricePercentage++;
					        }
					        minPrice = float.MaxValue;
					        maxPrice = float.MinValue;
					        endPrice = price;
					        if (price < minPrice) 
						        minPrice = price;
					        if (price > maxPrice)  
						        maxPrice = price; 
					        graphic.FillRectangle(increased ? greenPen : redPen, position - 15, 336 - maxPricePercentage, 12, maxPricePercentage - minPricePercentage);
					        position -= 15;
					        if (barCount++ > 200 || position < 0) break;
				        }
			        }
			        using var memory = new MemoryStream();
			        cachedMap.Save(memory, ImageFormat.Png);
			        if (targetImageId != 0)
				        FileStorage.server.Remove(targetImageId, FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID);
				    imageId = FileStorage.server.Store(memory.ToArray(), FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID);
#if !CARBON
			        onProcessEnded?.Invoke(imageId);
#endif
		        } 
		        catch (Exception e)  
		        {
			        Console.WriteLine($"ShoppyStock chart drawing failed! Exception: {e}");
			        throw;
		        }
		        #if CARBON
		        base.ThreadFunction();
#else
		        yield return new WaitForEndOfFrame();
		        #endif
	        }
#if CARBON
	        public override void OnFinished()
	        {
		        onProcessEnded?.Invoke(imageId);
		        base.OnFinished();
	        }
#endif
        }
        
        #endregion
        
        #region Item Management

        private class ItemData
        {
	        public int id;
	        public int amount;

	        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
	        public ulong skin;

	        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
	        public float fuel;

	        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
	        public int flameFuel;

	        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
	        public float condition;

	        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
	        public float maxCondition = -1;

	        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
	        public int ammo;

	        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
	        public int ammoType;

	        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
	        public int dataInt;

	        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
	        public string name;

	        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
	        public string text;

	        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
	        public Item.Flag flags;

	        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
	        public int capacity;

	        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
	        public List<OwnershipData> ownership;

	        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
	        public List<ItemData> contents;

	        public Item ToItem()
	        {
		        Item item = ItemManager.CreateByItemID(id, amount, skin);
		        if (item == null) return null;
		        item.fuel = fuel;
		        item.condition = condition;
		        if (maxCondition != -1)
			        item.maxCondition = maxCondition;
		        if (!string.IsNullOrEmpty(name))
			        item.name = name;
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
		        if (ownership?.Count > 0)
		        {
			        if (item.ownershipShares == null)
				        item.ownershipShares = Pool.Get<List<ItemOwnershipShare>>();
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
		        item.text = text;
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
	        itemData.id = item.info.itemid;
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
        
        #endregion

    }
}