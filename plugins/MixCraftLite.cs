using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using UnityEngine.UI;

namespace Oxide.Plugins
{
    [Info("MixCraftLite", "Mestre Pardal", "2.6.5")]
    [Description("HUD e mecânica de crafting em estações (Mixing Table, Cooking Table/BBQ, Cooking Workbench) + tiers/perm-item + limite por player + abas de categorias + lâmpada industrial de status.")]
    public class MixCraftLite : RustPlugin
    {
        private const string PERM_USE       = "mixcraftlite.use";
        private const string CMD_TOGGLE_UI  = "mixcraft.toggleui";
        private const string CMD_CRAFT      = "mixcraft.craft";
        private const string CMD_AUTOFILL   = "mixcraft.autofill";
        private const string CMD_PAGEUP     = "mixcraft.pageup";
        private const string CMD_PAGEDOWN   = "mixcraft.pagedown";
        private const string CMD_SETTAB     = "mixcraft.settab";

        private const string CMD_LIMIT_RESET_ALL    = "mixcraft.limitresetall";
        private const string CMD_LIMIT_RESET_USER   = "mixcraft.limitresetuser";
        private const string CMD_LIMIT_RESET_RECIPE = "mixcraft.limitresetrecipe";

        private const int RECIPE_MAX_SLOTS = 5;
        private const int ROWS_PER_PAGE    = 7;

        #region Config / Models
        private Configuration _config;

        public class Configuration
        {
            [JsonProperty("Require permission (mixcraftlite.use)")]
            public bool RequirePermission = true;

            [JsonProperty("Use on Mixing Table")]
            public bool UseOnMixingTable = true;

            [JsonProperty("Use on Cooking Table (BBQ)")]
            public bool UseOnCookingTable = false;

            [JsonProperty("Use on Cooking Workbench")]
            public bool UseOnCookingWorkbench = false;

            [JsonProperty("Drop Upwards Velocity (quando sem espaço)")]
            public float DropUpwardVelocity = 3f;

            [JsonProperty("UI - Dim locked recipes (no permission)")]
            public bool DimLockedRecipes = false;

            [JsonProperty("Limits - Count units (true) or craft times (false)")]
            public bool LimitCountsUnits = true;

            [JsonProperty("Lamp Red Prefab (short or full prefab path)")]
            public string LampPrefabRed = "assets/prefabs/misc/permstore/industriallight/industrial.wall.lamp.red.deployed.prefab";

            [JsonProperty("Lamp Green Prefab (short or full prefab path)")]
            public string LampPrefabGreen = "assets/prefabs/misc/permstore/industriallight/industrial.wall.lamp.green.deployed.prefab";

            [JsonProperty("Lamp Base Height (world units, default 1.5)")]
            public float LampBaseHeight = 1.5f;

            [JsonProperty("Lamp Pos Vertical (1 = base, <1 lower, >1 higher)")]
            public float LampPosVertical = 1.2f;

            [JsonProperty("Lamp Pos Horizontal (1 = center, <1 left, >1 right)")]
            public float LampPosHorizontal = 1.15f;

            [JsonProperty("Lamp Pos Forward (1 = center, <1 back, >1 forward)")]
            public float LampPosForward = 0.75f;

            [JsonProperty("Lamp Scale (1 = default)")]
            public float LampScale = 1.0f;

            [JsonProperty("Lamp Rotation Pitch (X)")]
            public float LampRotPitch = 0f;

            [JsonProperty("Lamp Rotation Yaw (Y)")]
            public float LampRotYaw = 0f;

            [JsonProperty("Lamp Rotation Roll (Z)")]
            public float LampRotRoll = 0f;

            [JsonProperty("Crafting Recipes", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<CustomRecipe> recipes = new List<CustomRecipe>();
        }

        public class CustomRecipe
        {
            [JsonProperty("Recipe Id (stable unique key; auto if empty)")]
            public string recipeId;

            [JsonProperty("Category / Tab (ex.: Gerais|Armas|Materiais)")]
            public string category = "Gerais";

            [JsonProperty("Ingredient Slots")]
            public Dictionary<int, Ingredient> ingredientSlots = new Dictionary<int, Ingredient>();

            [JsonProperty("Produced Item")]
            public ProducedItem producedItem = new ProducedItem();

            [JsonProperty("Seconds per produced unit")]
            public float secondsPerUnit = 2f;

            [JsonProperty("Required Tier (ex.: tier1 | tier2 | null)")]
            public string requiredTier;

            [JsonProperty("Custom Permission (opcional, ex.: vip.gold)")]
            public string customPermission;

            [JsonProperty("Per-Player Limit (0 = ilimitado)")]
            public int perPlayerLimit = 0;

            public bool TryCollect(ItemContainer container, out int collectTimes, out List<Item> collected, out List<int> perTakeAmounts)
            {
                collected = new List<Item>();
                perTakeAmounts = new List<int>();
                collectTimes = int.MaxValue;

                for (int slot = 0; slot < RECIPE_MAX_SLOTS; slot++)
                {
                    if (!ingredientSlots.TryGetValue(slot, out var ing))
                        continue;

                    var it = container.GetSlot(slot);
                    if (it == null ||
                        it.info.shortname != ing.shortName ||
                        it.skin != ing.skinId ||
                        it.amount < ing.amount)
                    {
                        collected.Clear();
                        perTakeAmounts.Clear();
                        collectTimes = 0;
                        return false;
                    }

                    collectTimes = Mathf.Min(collectTimes, Mathf.FloorToInt(it.amount / (float)ing.amount));
                    collected.Add(it);
                    perTakeAmounts.Add(ing.amount);
                }

                if (collectTimes == int.MaxValue)
                    collectTimes = 0;

                return collectTimes > 0;
            }

            public Item CreateResultTotal(int times)
            {
                int total = Math.Max(1, producedItem.amount) * Math.Max(1, times);
                return producedItem.CreateItem(total);
            }

            public string GetStableKey()
            {
                if (!string.IsNullOrEmpty(recipeId))
                    return recipeId.Trim();

                string prod = (producedItem?.shortName ?? "") + ":" + producedItem?.skinId;
                string ing = string.Join("|", ingredientSlots
                    .OrderBy(kv => kv.Key)
                    .Select(kv => $"{kv.Key}:{kv.Value?.shortName}:{kv.Value?.skinId}:{kv.Value?.amount}"));

                return $"auto::{prod}::{ing}".ToLowerInvariant();
            }
        }

        public class Ingredient : SkinnedItem
        {
            [JsonProperty("Amount")] public int amount = 1;
        }

        public class ProducedItem : CustomItem
        {
            [JsonProperty("Amount")] public int amount = 1;
        }

        public class CustomItem : SkinnedItem
        {
            [JsonProperty("Custom item name (null = default name)")]
            public string displayName;

            [JsonIgnore] public string UiDisplayName => GetDisplayName(false);

            public string GetDisplayName(bool nullIfNotCustom)
            {
                if (string.IsNullOrEmpty(displayName))
                {
                    if (nullIfNotCustom)
                        return null;

                    return ItemDefinition?.displayName?.english ?? shortName ?? "";
                }

                return displayName;
            }

            public override Item CreateItem(int amount)
            {
                var itm = ItemManager.Create(ItemDefinition, amount, skinId);
                var n = GetDisplayName(true);
                if (itm != null && n != null)
                {
                    itm.name = n;
                    itm.MarkDirty();
                }

                return itm;
            }
        }

        public class SkinnedItem
        {
            [JsonProperty("Item short name")] public string shortName;
            [JsonProperty("Item skin id")]   public ulong  skinId;

            [JsonIgnore]
            public ItemDefinition ItemDefinition
            {
                get
                {
                    if (string.IsNullOrEmpty(shortName))
                        return null;
                    return ItemManager.FindItemDefinition(shortName);
                }
            }

            public virtual Item CreateItem(int amount)
            {
                if (amount <= 0)
                    return null;

                var def = ItemDefinition;
                if (def == null)
                    return null;

                return ItemManager.Create(def, amount, skinId);
            }
        }
        #endregion

        #region Lang
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Toggle.Show"]       = "SHOW CUSTOM RECIPES",
                ["Toggle.Hide"]       = "HIDE CUSTOM RECIPES",
                ["Craft.Button"]      = "⏻ CRAFT",
                ["Craft.Progress"]    = "CRAFTING... {0}s",
                ["Craft.Done"]        = "Craft finished: {0} x {1}",
                ["Craft.NoneMatch"]   = "No custom recipe matches the items in slots.",
                ["Craft.Canceled"]    = "Craft canceled (ingredients changed).",
                ["Craft.Header"]      = "Crafting:",
                ["Page.Indicator"]    = "{0}/{1}",
                ["NoPermission"]      = "You don't have permission to craft this recipe.",
                ["LimitReached"]      = "Limit reached for this recipe. Remaining: {0}",
                ["LimitAdjusted"]     = "Your crafting amount was adjusted to the remaining limit: {0}.",
                ["LockedMark"]        = " (locked)",
                ["AlreadyCrafting"]   = "You are already crafting this recipe in another station."
            }, this, "en");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Toggle.Show"]       = "MOSTRAR RECEITAS CUSTOM",
                ["Toggle.Hide"]       = "OCULTAR RECEITAS CUSTOM",
                ["Craft.Button"]      = "⏻ FABRICAR",
                ["Craft.Progress"]    = "FABRICANDO... {0}s",
                ["Craft.Done"]        = "Fabricação concluída: {0} x {1}",
                ["Craft.NoneMatch"]   = "Nenhuma receita corresponde aos itens nos slots.",
                ["Craft.Canceled"]    = "Fabricação cancelada (ingredientes alterados).",
                ["Craft.Header"]      = "Fabricando:",
                ["Page.Indicator"]    = "{0}/{1}",
                ["NoPermission"]      = "Você não tem permissão para fabricar esta receita.",
                ["LimitReached"]      = "Limite atingido para esta receita. Restante: {0}",
                ["LimitAdjusted"]     = "Sua quantidade de fabricação foi ajustada para o limite restante: {0}.",
                ["LockedMark"]        = " (bloqueado)",
                ["AlreadyCrafting"]   = "Você já está fabricando esta receita em outra mesa."
            }, this, "pt-BR");
        }

        private string L(string key, BasePlayer p = null, params object[] args)
        {
            string text;

            if (p != null && lang.GetLanguage(p.UserIDString) == "en")
            {
                text = lang.GetMessage(key, this, p.UserIDString);
            }
            else
            {
                var pt = lang.GetMessages("pt-BR", this);
                if (!pt.TryGetValue(key, out text) || string.IsNullOrEmpty(text))
                    text = lang.GetMessage(key, this, p?.UserIDString);
            }

            if (args == null || args.Length == 0)
                return text;

            try
            {
                return string.Format(text, args);
            }
            catch (FormatException)
            {
                return text;
            }
        }
        #endregion

        #region Data (limites)
        private const string DATA_FILE = "MixCraftLite_Limits";

        private class PlayerLimitData
        {
            public Dictionary<string, int> Counters = new Dictionary<string, int>();
        }

        private Dictionary<ulong, PlayerLimitData> _limits;

        private PlayerLimitData GetLimitData(ulong id)
        {
            if (!_limits.TryGetValue(id, out var d))
            {
                d = new PlayerLimitData();
                _limits[id] = d;
            }
            return d;
        }

        private int GetUsedCount(ulong id, string recipeKey)
        {
            var d = GetLimitData(id);
            return d.Counters.TryGetValue(recipeKey, out var v) ? v : 0;
        }

        private void AddUsage(ulong id, string recipeKey, int amount)
        {
            var d = GetLimitData(id);
            d.Counters[recipeKey] = GetUsedCount(id, recipeKey) + Math.Max(0, amount);
            SaveData();
        }

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(DATA_FILE, _limits);
        private void LoadData()
        {
            try { _limits = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, PlayerLimitData>>(DATA_FILE) ?? new Dictionary<ulong, PlayerLimitData>(); }
            catch { _limits = new Dictionary<ulong, PlayerLimitData>(); }
        }
        #endregion

        #region State
        private readonly HashSet<ulong> _uiOpen = new HashSet<ulong>();

        // Pending craft usage (anti-multi-station bypass):
        // We reserve the limit while a job is running, so the player can't start the same recipe on multiple stations.
        // Key: playerId -> (recipeKey -> reservedUsageCount)
        private readonly Dictionary<ulong, Dictionary<string, int>> _pendingUsage = new Dictionary<ulong, Dictionary<string, int>>();

        private int GetPendingUsage(ulong playerId, string recipeKey)
        {
            if (string.IsNullOrEmpty(recipeKey)) return 0;
            if (_pendingUsage.TryGetValue(playerId, out var map) && map != null && map.TryGetValue(recipeKey, out var v))
                return Mathf.Max(0, v);
            return 0;
        }

        private void AddPendingUsage(ulong playerId, string recipeKey, int amount)
        {
            if (string.IsNullOrEmpty(recipeKey)) return;
            amount = Mathf.Max(0, amount);
            if (amount <= 0) return;

            if (!_pendingUsage.TryGetValue(playerId, out var map) || map == null)
            {
                map = new Dictionary<string, int>();
                _pendingUsage[playerId] = map;
            }

            map[recipeKey] = GetPendingUsage(playerId, recipeKey) + amount;
        }

        private void RemovePendingUsage(ulong playerId, string recipeKey, int amount)
        {
            if (string.IsNullOrEmpty(recipeKey)) return;
            amount = Mathf.Max(0, amount);
            if (amount <= 0) return;

            if (!_pendingUsage.TryGetValue(playerId, out var map) || map == null)
                return;

            int cur = GetPendingUsage(playerId, recipeKey);
            int next = Mathf.Max(0, cur - amount);

            if (next <= 0)
                map.Remove(recipeKey);
            else
                map[recipeKey] = next;

            if (map.Count == 0)
                _pendingUsage.Remove(playerId);
        }

        private void ClearPendingUsageForJob(CraftJob job)
        {
            if (job == null) return;
            if (job.PendingCount <= 0) return;
            RemovePendingUsage(job.StarterId, job.RecipeKey, job.PendingCount);
            job.PendingCount = 0;
        }

        private class CraftJob
        {
            public CustomRecipe  Recipe;
            public int           TotalUnits;
            public int           Times;
            public float         TotalSeconds;
            public float         EndTime;
            public ulong         StarterId;
            public ItemContainer Container;
            public Timer         Timer;
            public string        RecipeKey;
            public int           UsageCount;
            public int           PendingCount;
        }

        private readonly Dictionary<ulong, CraftJob> _jobsByEntity = new Dictionary<ulong, CraftJob>();
        private readonly Dictionary<ulong, Timer>    _uiTickers    = new Dictionary<ulong, Timer>();

        // Lâmpada + estado
        private readonly Dictionary<ulong, BaseEntity> _stationLamps      = new Dictionary<ulong, BaseEntity>();
        private readonly Dictionary<ulong, string>     _stationLampPrefab = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, Timer>      _lampBlinkers      = new Dictionary<ulong, Timer>();
        private readonly Dictionary<ulong, bool>       _stationReady      = new Dictionary<ulong, bool>();

        private class CraftUiState
        {
            public bool  Open;
            public bool  Busy;
            public int   RemainSecs;
            public ulong StationId;
        }

        private readonly Dictionary<ulong, CraftUiState> _lastUi       = new Dictionary<ulong, CraftUiState>();
        private readonly Dictionary<ulong, int>          _pageByPlayer = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, string>       _tabByPlayer  = new Dictionary<ulong, string>();

        private bool HasPluginPerm(BasePlayer p) => !_config.RequirePermission ||
                                                    permission.UserHasPermission(p.UserIDString, PERM_USE);

        private bool HasRecipePerm(BasePlayer p, CustomRecipe r)
        {
            bool hasRequirement = false;
            bool tierOk = false;
            bool customOk = false;

            if (!string.IsNullOrEmpty(r.requiredTier))
            {
                hasRequirement = true;
                var tierPerm = $"mixcraftlite.tier.{r.requiredTier.ToLowerInvariant()}";
                tierOk = permission.UserHasPermission(p.UserIDString, tierPerm);
            }

            if (!string.IsNullOrEmpty(r.customPermission))
            {
                hasRequirement = true;
                customOk = permission.UserHasPermission(p.UserIDString, r.customPermission);
            }

            if (hasRequirement)
                return tierOk || customOk;

            return true;
        }

        private bool TryGetStation(BasePlayer p, out BaseEntity station, out ItemContainer container)
        {
            station = p?.inventory?.loot?.entitySource as BaseEntity;
            var sc = station as StorageContainer;
            container = sc?.inventory;

            if (station == null || container == null)
                return false;

            return IsAllowedStation(station);
        }

        private ulong EntityId(BaseEntity e) => e?.net?.ID.Value ?? 0u;
        #endregion

        #region Station filtering
        private bool IsAllowedStation(BaseEntity entity)
        {
            if (entity == null)
                return false;

            var sn       = (entity.ShortPrefabName ?? string.Empty).ToLowerInvariant();
            var typeName = entity.GetType().Name;

            // Mixing Table padrão
            bool isMixingTable =
                entity is MixingTable ||
                sn.Contains("mixingtable");

            // Cooking Table híbrida (mixing + bbq embutido)
            bool isCookingTable =
                sn.Contains("cookingtable") ||
                sn.Contains("cooking_table");

            // BBQ padrão (churrasqueira)
            bool isBBQ =
                typeName.Equals("BBQDeployed", StringComparison.OrdinalIgnoreCase) ||
                sn.Contains("bbq");

            // Cooking Workbench separado
            bool isCookingWorkbench =
                typeName.Equals("CookingWorkbench", StringComparison.OrdinalIgnoreCase) ||
                sn.Contains("cookingworkbench");

            // 1) Cooking Table híbrida: obedece apenas "Use on Cooking Table (BBQ)"
            if (isCookingTable)
                return _config.UseOnCookingTable;

            // 2) Mixing Table normal
            if (isMixingTable)
                return _config.UseOnMixingTable;

            // 3) BBQ padrão: também controlado por "Use on Cooking Table (BBQ)"
            if (isBBQ)
                return _config.UseOnCookingTable;

            // 4) Cooking Workbench
            if (isCookingWorkbench)
                return _config.UseOnCookingWorkbench;

            return false;
        }
        #endregion

        #region Luz Industrial
        private string ResolveLampPrefab(string cfgValue, string fallbackShort)
        {
            string v = cfgValue;
            if (string.IsNullOrEmpty(v))
                v = fallbackShort;

            if (string.IsNullOrEmpty(v))
                return null;

            if (v.Contains("/"))
                return v;

            string autoPath = $"assets/prefabs/deployable/industrial lights/{v}.deployed.prefab";
            return autoPath;
        }

        private void StopLampBlink(ulong id)
        {
            if (_lampBlinkers.TryGetValue(id, out var t))
            {
                t.Destroy();
                _lampBlinkers.Remove(id);
            }
        }

        private void DestroyLamp(BaseEntity station)
        {
            if (station == null) return;
            var id = EntityId(station);
            if (id == 0) return;

            StopLampBlink(id);

            if (_stationLamps.TryGetValue(id, out var lamp))
            {
                if (lamp != null && !lamp.IsDestroyed)
                    lamp.Kill();
                _stationLamps.Remove(id);
            }

            _stationLampPrefab.Remove(id);
            _stationReady.Remove(id);
        }

        private void EnsureLamp(BaseEntity station, bool green, bool forceSwitchColor = false)
        {
            if (station == null)
                return;

            var id = EntityId(station);
            if (id == 0)
                return;

            string desiredShort = green ? _config.LampPrefabGreen : _config.LampPrefabRed;
            string prefabPath   = ResolveLampPrefab(desiredShort, green ? "industrial.wall.lamp.green.deployed" : "industrial.wall.lamp.red.deployed");
            if (string.IsNullOrEmpty(prefabPath))
                return;

            if (_stationLamps.TryGetValue(id, out var lamp) && lamp != null && !lamp.IsDestroyed)
            {
                if (_stationLampPrefab.TryGetValue(id, out var currentPath) &&
                    currentPath == prefabPath &&
                    !forceSwitchColor)
                {
                    return;
                }

                lamp.Kill();
                _stationLamps.Remove(id);
                _stationLampPrefab.Remove(id);
            }

            var basePos = station.transform.position;

            // Fatores: 1.0 = padrão; 0.5 = desloca -0.5; 1.5 = desloca +0.5
            float upOffset      = _config.LampBaseHeight + (_config.LampPosVertical   - 1f);
            float rightOffset   = (_config.LampPosHorizontal - 1f);
            float forwardOffset = (_config.LampPosForward    - 1f);

            var offset =
                station.transform.up      * upOffset +
                station.transform.right   * rightOffset +
                station.transform.forward * forwardOffset;

            var pos = basePos + offset;

            var rot = station.transform.rotation *
                      Quaternion.Euler(
                          _config.LampRotPitch,
                          _config.LampRotYaw,
                          _config.LampRotRoll
                      );

            var lampEnt = GameManager.server.CreateEntity(prefabPath, pos, rot, true) as BaseEntity;
            if (lampEnt == null)
            {
                PrintWarning($"MixCraftLite: falha ao criar lâmpada '{prefabPath}' (config='{desiredShort}') para estação '{station.ShortPrefabName}'.");
                return;
            }

            lampEnt.SetParent(station, true);
            lampEnt.Spawn();

            float scale = Mathf.Clamp(_config.LampScale, 0.1f, 5f);
            lampEnt.transform.localScale = Vector3.one * scale;

            lampEnt.SetFlag(BaseEntity.Flags.On, false);
            lampEnt.SendNetworkUpdateImmediate();

            _stationLamps[id]      = lampEnt;
            _stationLampPrefab[id] = prefabPath;
        }

        private void SetLampOn(BaseEntity station, bool on)
        {
            if (station == null) return;
            var id = EntityId(station);
            if (id == 0) return;

            if (!_stationLamps.TryGetValue(id, out var lamp) || lamp == null || lamp.IsDestroyed)
                return;

            lamp.SetFlag(BaseEntity.Flags.On, on);
            lamp.SendNetworkUpdate();
        }

        private void SetLampBlink(BaseEntity station, bool blink)
        {
            if (station == null) return;
            var id = EntityId(station);
            if (id == 0) return;

            if (!blink)
            {
                StopLampBlink(id);
                return;
            }

            StopLampBlink(id);
            if (!_stationLamps.TryGetValue(id, out var lamp) || lamp == null || lamp.IsDestroyed)
                return;

            _lampBlinkers[id] = timer.Every(0.4f, () =>
            {
                if (!_stationLamps.TryGetValue(id, out var l) || l == null || l.IsDestroyed)
                {
                    StopLampBlink(id);
                    return;
                }

                bool isOn = l.HasFlag(BaseEntity.Flags.On);
                l.SetFlag(BaseEntity.Flags.On, !isOn);
                l.SendNetworkUpdate();
            });
        }

        private void SetIdleLamp(BaseEntity station)
        {
            if (station == null) return;
            var id = EntityId(station);
            if (id == 0) return;

            _stationReady[id] = false;
            StopLampBlink(id);
            EnsureLamp(station, green: false, forceSwitchColor: true);
            SetLampOn(station, false);
        }

        private void OnStationCraftStarted(BaseEntity station)
        {
            if (station == null) return;
            var id = EntityId(station);
            if (id == 0) return;

            _stationReady[id] = false;

            EnsureLamp(station, green: false, forceSwitchColor: true);
            SetLampOn(station, true);
            SetLampBlink(station, true);
        }

        private void OnStationCraftFinished(BaseEntity station)
        {
            if (station == null) return;
            var id = EntityId(station);
            if (id == 0) return;

            _stationReady[id] = true;

            StopLampBlink(id);
            EnsureLamp(station, green: true, forceSwitchColor: true);
            SetLampOn(station, true);
        }

        private void OnStationCraftCanceled(BaseEntity station)
        {
            if (station == null) return;
            var id = EntityId(station);
            if (id == 0) return;

            _stationReady[id] = false;
            StopLampBlink(id);
            EnsureLamp(station, green: false, forceSwitchColor: true);
            SetLampOn(station, false);
        }

        private void OnStationCollectFinished(BaseEntity station)
        {
            SetIdleLamp(station);
        }
        #endregion

        #region Hooks + lifecycle
        private void Init()
        {
            permission.RegisterPermission(PERM_USE, this);

            foreach (var r in _config?.recipes ?? new List<CustomRecipe>())
            {
                if (!string.IsNullOrEmpty(r.requiredTier))
                {
                    var tierPerm = $"mixcraftlite.tier.{r.requiredTier.ToLowerInvariant()}";
                    if (!permission.PermissionExists(tierPerm, this))
                        permission.RegisterPermission(tierPerm, this);
                }

                if (!string.IsNullOrEmpty(r.customPermission))
                {
                    if (!permission.PermissionExists(r.customPermission, this))
                        permission.RegisterPermission(r.customPermission, this);
                }
            }

            AddCovalenceCommand(CMD_TOGGLE_UI, nameof(CmdToggleUi));
            AddCovalenceCommand(CMD_CRAFT,     nameof(CmdCraft));
            AddCovalenceCommand(CMD_PAGEUP,    nameof(CmdPageUp));
            AddCovalenceCommand(CMD_PAGEDOWN,  nameof(CmdPageDown));
            AddCovalenceCommand(CMD_SETTAB,    nameof(CmdSetTab));

            AddCovalenceCommand(CMD_LIMIT_RESET_ALL,    nameof(CmdLimitResetAll));
            AddCovalenceCommand(CMD_LIMIT_RESET_USER,   nameof(CmdLimitResetUser));
            AddCovalenceCommand(CMD_LIMIT_RESET_RECIPE, nameof(CmdLimitResetRecipe));
        }

        private void OnServerInitialized()
        {
            LoadData();

            foreach (var e in BaseNetworkable.serverEntities)
            {
                var be = e as BaseEntity;
                if (be != null && IsAllowedStation(be))
                {
                    ConfigureContainer(be);
                    EnsureLamp(be, green: false, forceSwitchColor: false);
                    SetLampOn(be, false);
                }
            }
        }

        private void OnUnload()
        {
            foreach (var kv in _uiTickers) kv.Value?.Destroy();
            _uiTickers.Clear();
            foreach (var job in _jobsByEntity.Values) job?.Timer?.Destroy();
            _jobsByEntity.Clear();
            _pendingUsage.Clear();

            foreach (var kv in _lampBlinkers) kv.Value?.Destroy();
            _lampBlinkers.Clear();

            foreach (var lamp in _stationLamps.Values)
            {
                if (lamp != null && !lamp.IsDestroyed)
                    lamp.Kill();
            }
            _stationLamps.Clear();
            _stationLampPrefab.Clear();
            _stationReady.Clear();

            foreach (var p in BasePlayer.activePlayerList)
                DestroyUi(p);

            _lastUi.Clear();
            _pageByPlayer.Clear();
            _tabByPlayer.Clear();

            SaveData();
        }

        protected override void LoadDefaultConfig()
        {
            _config = BuildExampleConfiguration();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try { _config = Config.ReadObject<Configuration>() ?? new Configuration(); }
            catch { _config = new Configuration(); }

            if (_config.recipes == null || _config.recipes.Count == 0)
            {
                _config = BuildExampleConfiguration();
                PrintWarning("Nenhuma receita no config. Gerando EXEMPLO.");
            }

            SanitizeConfig();
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        private Configuration BuildExampleConfiguration()
        {
            var cfg = new Configuration
            {
                RequirePermission     = true,
                UseOnMixingTable      = true,
                UseOnCookingTable     = false,
                UseOnCookingWorkbench = false,
                DropUpwardVelocity    = 3f,
                DimLockedRecipes      = true,
                LimitCountsUnits      = true,
                LampPrefabRed         = "assets/prefabs/misc/permstore/industriallight/industrial.wall.lamp.red.deployed.prefab",
                LampPrefabGreen       = "assets/prefabs/misc/permstore/industriallight/industrial.wall.lamp.green.deployed.prefab",
                LampBaseHeight        = 1.5f,
                LampPosVertical       = 1.0f,
                LampPosHorizontal     = 1.0f,
                LampPosForward        = 1.0f,
                LampScale             = 1.0f,
                LampRotPitch          = 0f,
                LampRotYaw            = 0f,
                LampRotRoll           = 0f,
                recipes               = new List<CustomRecipe>()
            };

            cfg.recipes.Add(new CustomRecipe
            {
                recipeId = "example_bandage",
                category = "Gerais",
                producedItem = new ProducedItem { shortName = "bandage", amount = 3, skinId = 0, displayName = "Bandage" },
                secondsPerUnit = 2f,
                requiredTier = "tier1",
                perPlayerLimit = 0,
                ingredientSlots = new Dictionary<int, Ingredient>
                {
                    [0] = new Ingredient { shortName = "cloth", amount = 30, skinId = 0 },
                    [1] = new Ingredient { shortName = "lowgradefuel", amount = 5, skinId = 0 }
                }
            });

            return cfg;
        }

        private void SanitizeConfig()
        {
            if (_config.recipes == null)
                _config.recipes = new List<CustomRecipe>();

            foreach (var r in _config.recipes)
            {
                if (string.IsNullOrWhiteSpace(r.recipeId))
                    r.recipeId = null;

                if (string.IsNullOrWhiteSpace(r.category))
                    r.category = "Gerais";

                if (r.ingredientSlots == null)
                    r.ingredientSlots = new Dictionary<int, Ingredient>();

                foreach (var k in r.ingredientSlots.Keys.ToList())
                    if (k < 0 || k >= RECIPE_MAX_SLOTS)
                        r.ingredientSlots.Remove(k);

                if (r.producedItem == null)
                    r.producedItem = new ProducedItem();

                if (r.secondsPerUnit <= 0f)
                    r.secondsPerUnit = 0.01f;

                if (r.perPlayerLimit < 0)
                    r.perPlayerLimit = 0;

                foreach (var p in r.ingredientSlots)
                    if (p.Value != null && p.Value.amount <= 0)
                        p.Value.amount = 1;
            }

            // Garantir fatores válidos
            _config.LampPosVertical   = Mathf.Clamp(_config.LampPosVertical,   0f, 2f);
            _config.LampPosHorizontal = Mathf.Clamp(_config.LampPosHorizontal, 0f, 2f);
            _config.LampPosForward    = Mathf.Clamp(_config.LampPosForward,    0f, 2f);
            _config.LampScale         = Mathf.Clamp(_config.LampScale,         0.1f, 5f);
        }
        #endregion

        #region Container config
        private void ConfigureContainer(BaseEntity station)
        {
            var sc = station as StorageContainer;
            var c = sc?.inventory;
            if (c == null) return;

            try { c.allowedContents = ItemContainer.ContentsType.Generic; } catch { }

            var type = c.GetType();
            var pOnly = type.GetProperty("onlyAllowedItems", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pOnly != null && pOnly.PropertyType == typeof(bool))
                pOnly.SetValue(c, false, null);

            var fOnly = type.GetField("onlyAllowedItems", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fOnly != null && fOnly.FieldType == typeof(bool))
                fOnly.SetValue(c, false);

            var pAllowedItem = type.GetProperty("allowedItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pAllowedItem != null)
                pAllowedItem.SetValue(c, null, null);

            var fAllowedItem = type.GetField("allowedItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fAllowedItem != null)
                fAllowedItem.SetValue(c, null);

            var pAllowedItems = type.GetProperty("allowedItems", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pAllowedItems != null && pAllowedItems.PropertyType == typeof(ItemDefinition[]))
                pAllowedItems.SetValue(c, null, null);

            var fAllowedItems = type.GetField("allowedItems", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fAllowedItems != null && fAllowedItems.FieldType == typeof(ItemDefinition[]))
                fAllowedItems.SetValue(c, null);

            var mix = station as MixingTable;
            if (mix != null)
            {
                try { mix.OnlyAcceptValidIngredients = false; } catch { }
            }
        }
        #endregion

        #region Hooks (loot + ui lifecycle)
        private void OnEntitySpawned(BaseNetworkable ent)
        {
            var be = ent as BaseEntity;
            if (be == null || be.IsDestroyed)
                return;

            if (!IsAllowedStation(be))
                return;

            ConfigureContainer(be);
            EnsureLamp(be, green: false, forceSwitchColor: false);
            SetLampOn(be, false);
        }

        private void OnEntityKill(BaseNetworkable ent)
        {
            var be = ent as BaseEntity;
            if (be == null) return;
            if (!IsAllowedStation(be)) return;
            DestroyLamp(be);
        }

        private void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (player == null || entity == null)
                return;

            if (!IsAllowedStation(entity))
            {
                DestroyUi(player);
                return;
            }

            if (!HasPluginPerm(player))
            {
                DestroyUi(player);
                return;
            }

            var id = EntityId(entity);
            bool hasJob = GetJob(entity, out _);

            if (hasJob)
            {
                OnStationCraftStarted(entity);
            }
            else
            {
                bool ready = _stationReady.TryGetValue(id, out var rdy) && rdy;
                if (ready)
                {
                    EnsureLamp(entity, green: true, forceSwitchColor: true);
                    StopLampBlink(id);
                    SetLampOn(entity, true);
                }
                else
                {
                    EnsureLamp(entity, green: false, forceSwitchColor: false);
                    StopLampBlink(id);
                    SetLampOn(entity, false);
                }
            }

            _pageByPlayer[player.userID] = 0;
            _tabByPlayer.Remove(player.userID);

            CreateToggleUi(player);
            if (_uiOpen.Contains(player.userID))
                CreateRecipeUi(player);

            UpdateCraftButtonUi(player, entity, force: true);
            StartUiTicker(player, entity);
        }

        private void OnLootEntityEnd(BasePlayer player, BaseEntity entity)
        {
            StopUiTicker(player);
            DestroyUi(player);
            _lastUi.Remove(player.userID);
            _pageByPlayer.Remove(player.userID);
            _tabByPlayer.Remove(player.userID);
        }

        private object CanAcceptItem(ItemContainer container, Item item, int targetPos)
        {
            var owner = container?.entityOwner as BaseEntity;
            if (owner != null && IsAllowedStation(owner))
                return ItemContainer.CanAcceptResult.CanAccept;

            return null;
        }

        private void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            var owner = container?.entityOwner as BaseEntity;
            if (owner == null || !IsAllowedStation(owner)) return;
            CheckContainerJob(container);
        }

        private void OnItemRemovedFromContainer(ItemContainer container, Item item)
        {
            var owner = container?.entityOwner as BaseEntity;
            if (owner == null || !IsAllowedStation(owner))
                return;

            CheckContainerJob(container);

            var id = EntityId(owner);
            if (_stationReady.TryGetValue(id, out var ready) && ready)
            {
                _stationReady[id] = false;
                OnStationCollectFinished(owner);
            }
        }

        private void OnPlayerDisconnected(BasePlayer p, string r)
        {
            if (p == null) return;
            StopUiTicker(p);
            DestroyUi(p);
            _lastUi.Remove(p.userID);
            _pageByPlayer.Remove(p.userID);
            _tabByPlayer.Remove(p.userID);
        }

        private void OnPlayerDeath(BasePlayer p, HitInfo i) => OnPlayerDisconnected(p, null);
        #endregion

        #region Craft core
        private bool ContainerExactMatch(ItemContainer c, CustomRecipe r, int times)
        {
            int cap = c.capacity;
            for (int s = 0; s < cap; s++)
            {
                bool usedByRecipe = (s < RECIPE_MAX_SLOTS) && r.ingredientSlots.ContainsKey(s);
                var it = c.GetSlot(s);

                if (usedByRecipe)
                {
                    var ing = r.ingredientSlots[s];
                    if (it == null ||
                        it.info.shortname != ing.shortName ||
                        it.skin != ing.skinId ||
                        it.amount < ing.amount * times)
                        return false;
                }
                else
                {
                    if (it != null && it.amount > 0)
                        return false;
                }
            }
            return true;
        }

        private bool ContainerMeetsNeeds(ItemContainer c, CustomRecipe r, int times) => ContainerExactMatch(c, r, times);

        private void ConsumeFromContainer(ItemContainer c, CustomRecipe r, int times)
        {
            for (int slot = 0; slot < RECIPE_MAX_SLOTS; slot++)
            {
                if (!r.ingredientSlots.TryGetValue(slot, out var ing) || ing == null)
                    continue;

                var it = c.GetSlot(slot);
                if (it == null)
                    continue;

                int need = ing.amount * times;
                if (it.amount == need)
                    it.Remove();
                else
                {
                    it.amount -= need;
                    it.MarkDirty();
                }
            }

            ItemManager.DoRemoves();
        }

        private void GiveBack(BasePlayer player, Item item)
        {
            if (item == null) return;

            if (!item.MoveToContainer(player.inventory.containerMain) &&
                !item.MoveToContainer(player.inventory.containerBelt))
            {
                var pos = player.transform.position + Vector3.up * 0.5f;
                item.Drop(pos, Vector3.up * _config.DropUpwardVelocity);
            }
        }

        private void ReturnExcessToPlayer(BasePlayer player, ItemContainer container, CustomRecipe recipe, int times)
        {
            for (int slot = 0; slot < RECIPE_MAX_SLOTS; slot++)
            {
                if (!recipe.ingredientSlots.TryGetValue(slot, out var ing) || ing == null)
                    continue;

                var it = container.GetSlot(slot);
                if (it == null)
                    continue;

                int need = ing.amount * times;
                if (it.amount > need)
                {
                    int extra = it.amount - need;
                    var split = it.SplitItem(extra);
                    if (split != null)
                        GiveBack(player, split);

                    it.MarkDirty();
                }
            }

            ItemManager.DoRemoves();
        }

        private int GetRemainingLimit(BasePlayer p, CustomRecipe r, string key)
        {
            if (r.perPlayerLimit <= 0)
                return int.MaxValue;

            int used    = GetUsedCount(p.userID, key);
            int pending = GetPendingUsage(p.userID, key);
            int remain  = Mathf.Max(0, r.perPlayerLimit - used - pending);
            return remain;
        }
        #endregion

        #region Job + ticker
        private void StartCraftJob(BasePlayer starter, BaseEntity station, ItemContainer container, CustomRecipe r, int times, string recipeKey, int usageCount)
        {
            var id = EntityId(station);
            if (id == 0)
                return;

            int   totalUnits   = Math.Max(1, r.producedItem.amount) * times;
            float totalSeconds = Math.Max(0.01f, r.secondsPerUnit) * totalUnits;

            if (_jobsByEntity.TryGetValue(id, out var exist))
            {
                exist.Timer?.Destroy();
                ClearPendingUsageForJob(exist);
                _jobsByEntity.Remove(id);
            }

            var job = new CraftJob
            {
                Recipe       = r,
                Times        = times,
                TotalUnits   = totalUnits,
                TotalSeconds = totalSeconds,
                EndTime      = Time.realtimeSinceStartup + totalSeconds,
                StarterId    = starter.userID,
                Container    = container,
                RecipeKey    = recipeKey,
                UsageCount   = usageCount,
                PendingCount = usageCount
            };

            if (job.Recipe != null && job.Recipe.perPlayerLimit > 0)
            {
                AddPendingUsage(job.StarterId, job.RecipeKey, job.PendingCount);
            }
            else
            {
                job.PendingCount = 0;
            }

            OnStationCraftStarted(station);

            job.Timer = timer.Once(totalSeconds, () =>
            {
                if (!ContainerMeetsNeeds(job.Container, job.Recipe, job.Times))
                {
                    ClearPendingUsageForJob(job);
                    _jobsByEntity.Remove(id);

                    var whoCancel = BasePlayer.FindByID(job.StarterId);
                    if (whoCancel != null)
                        SendReply(whoCancel, L("Craft.Canceled", whoCancel));

                    OnStationCraftCanceled(station);

                    foreach (var pl in BasePlayer.activePlayerList)
                    {
                        var src = pl?.inventory?.loot?.entitySource as BaseEntity;
                        if (src == station)
                            UpdateCraftButtonUi(pl, station, force: true);
                    }

                    return;
                }

                ConsumeFromContainer(job.Container, job.Recipe, job.Times);

                var result = job.Recipe.CreateResultTotal(job.Times);
                if (result != null && !result.MoveToContainer(job.Container))
                {
                    var pos = station.transform.position + station.transform.up * 1.2f;
                    result.DropAndTossUpwards(pos, _config.DropUpwardVelocity);
                }

                ClearPendingUsageForJob(job);
                AddUsage(job.StarterId, job.RecipeKey, job.UsageCount);

                _jobsByEntity.Remove(id);

                var who = BasePlayer.FindByID(job.StarterId);
                if (who != null)
                {
                    SendReply(who, L("Craft.Done", who,
                        job.Recipe.producedItem.UiDisplayName ?? job.Recipe.producedItem.shortName,
                        job.TotalUnits));
                }

                OnStationCraftFinished(station);

                foreach (var pl in BasePlayer.activePlayerList)
                {
                    var src = pl?.inventory?.loot?.entitySource as BaseEntity;
                    if (src == station)
                        UpdateCraftButtonUi(pl, station, force: true);
                }
            });

            _jobsByEntity[id] = job;
        }

        private bool  GetJob(BaseEntity e, out CraftJob job) => _jobsByEntity.TryGetValue(EntityId(e), out job);
        private float GetProgress(BaseEntity e)
        {
            if (!GetJob(e, out var job) || job.TotalSeconds <= 0f)
                return 0f;

            float rem = Mathf.Max(0f, job.EndTime - Time.realtimeSinceStartup);
            return 1f - (rem / job.TotalSeconds);
        }

        private float GetRemaining(BaseEntity e)
        {
            if (!GetJob(e, out var job))
                return 0f;

            return Mathf.Max(0f, job.EndTime - Time.realtimeSinceStartup);
        }

        private void CheckContainerJob(ItemContainer c)
        {
            var owner = c?.entityOwner as BaseEntity;
            if (owner == null)
                return;

            if (!_jobsByEntity.TryGetValue(EntityId(owner), out var job))
                return;

            if (ContainerMeetsNeeds(c, job.Recipe, job.Times))
                return;

            job.Timer?.Destroy();
            ClearPendingUsageForJob(job);
            _jobsByEntity.Remove(EntityId(owner));

            var who = BasePlayer.FindByID(job.StarterId);
            if (who != null)
                SendReply(who, L("Craft.Canceled", who));

            OnStationCraftCanceled(owner);

            foreach (var pl in BasePlayer.activePlayerList)
            {
                var src = pl?.inventory?.loot?.entitySource as BaseEntity;
                if (src == owner)
                    UpdateCraftButtonUi(pl, owner, force: true);
            }
        }
        #endregion

        #region UI
        private static class UIRef
        {
            public const string LAYER_RECIPE = "mixlite.ui.craft";
            public const string LAYER_TOGGLE = "mixlite.ui.toggle";
            public const string LAYER_CRAFT  = "mixlite.ui.craftbtn";

            public const string Text       = "0.745 0.709 0.674 1";
            public const string BtnText    = "0.2 0.8 0.2 1";
            public const string Panel      = "0.1 0.1 0.1 0.9";
            public const string Tile       = "0.2 0.2 0.19 1";
            public const string BtnGreen   = "0.415 0.5 0.258 0.7";
            public const string BtnNeutral = "0.75 0.75 0.75 0.3";
            public const string BtnDisabled= "0.45 0.45 0.45 0.35";

            public const int SecondsFont = 12;
        }

        private void CreateToggleUi(BasePlayer p)
        {
            var c = new CuiElementContainer();
            string root = c.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "430 608", OffsetMax = "572 633" }
            }, "Hud.Menu", UIRef.LAYER_TOGGLE);

            c.Add(new CuiButton
            {
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Button = { Command = CMD_TOGGLE_UI, Color = UIRef.BtnNeutral },
                Text = { Text = _uiOpen.Contains(p.userID) ? L("Toggle.Hide", p) : L("Toggle.Show", p), Align = TextAnchor.MiddleCenter, Color = UIRef.BtnText, FontSize = 12 }
            }, root);

            CuiHelper.AddUi(p, c);
        }

        private string GetRecipeCategory(CustomRecipe r)
        {
            return string.IsNullOrWhiteSpace(r.category) ? "Gerais" : r.category;
        }

        private List<string> GetCategoriesForPlayer(BasePlayer p)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var r in _config.recipes)
            {
                var cat = GetRecipeCategory(r);
                if (_config.DimLockedRecipes)
                {
                    set.Add(cat);
                }
                else
                {
                    if (HasRecipePerm(p, r))
                        set.Add(cat);
                }
            }

            if (set.Count == 0)
                set.Add("Gerais");

            return set
                .OrderBy(c => c.Equals("Gerais", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(c => c, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void CreateRecipeUi(BasePlayer p)
        {
            var cont = new CuiElementContainer();

            string root = cont.Add(new CuiPanel
            {
                Image =
                {
                    Color = UIRef.Panel,
                    Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"
                },
                RectTransform =
                {
                    AnchorMin = "0.5 0",
                    AnchorMax = "0.5 0",
                    OffsetMin = "193 302",
                    OffsetMax = "573 582"
                }
            }, "Hud.Menu", UIRef.LAYER_RECIPE);

            var categories = GetCategoriesForPlayer(p);

            string activeCat;
            if (!_tabByPlayer.TryGetValue(p.userID, out activeCat) ||
                !categories.Contains(activeCat, StringComparer.OrdinalIgnoreCase))
            {
                activeCat = categories.Count > 0 ? categories[0] : "Gerais";
                _tabByPlayer[p.userID] = activeCat;
            }

            float listTopY = 0.853f;

            if (categories.Count > 0)
            {
                float tabMinY = 0.91f;
                float tabMaxY = 0.99f;
                float tabWidth = 0.996f / categories.Count;

                for (int i = 0; i < categories.Count; i++)
                {
                    var cat = categories[i];
                    float minX = i * tabWidth;
                    float maxX = (i + 1) * tabWidth;

                    bool isActive = cat.Equals(activeCat, StringComparison.OrdinalIgnoreCase);
                    string bgColor = isActive ? "0.2 0.8 0.2 0.9" : UIRef.BtnNeutral;
                    string textColor = isActive ? "0 0 0 1" : UIRef.BtnText;

                    cont.Add(new CuiButton
                    {
                        RectTransform =
                        {
                            AnchorMin = $"{minX} {tabMinY}",
                            AnchorMax = $"{maxX} {tabMaxY}"
                        },
                        Button =
                        {
                            Command = $"{CMD_SETTAB} {cat}",
                            Color = bgColor
                        },
                        Text =
                        {
                            Text = cat,
                            Align = TextAnchor.MiddleCenter,
                            Color = textColor,
                            FontSize = 12
                        }
                    }, root);
                }
            }

            var visibleIndices = new List<int>();

            for (int i = 0; i < _config.recipes.Count; i++)
            {
                var r = _config.recipes[i];

                if (!GetRecipeCategory(r).Equals(activeCat, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (_config.DimLockedRecipes)
                {
                    visibleIndices.Add(i);
                }
                else
                {
                    if (HasRecipePerm(p, r))
                        visibleIndices.Add(i);
                }
            }

            int totalRows = visibleIndices.Count;

            if (totalRows == 0)
            {
                CuiHelper.AddUi(p, cont);
                return;
            }

            const int tileSize   = 14;
            const int rowHeight  = 34;
            const int visibleRowsTarget = 7;

            int contentHeight = Math.Max(totalRows, visibleRowsTarget) * rowHeight;

            string scrollName = "mixcraft.scroll";

            cont.Add(new CuiElement
            {
                Name = scrollName,
                Parent = root,
                Components =
                {
                    new CuiImageComponent
                    {
                        Color = "0 0 0 0"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = $"1 {listTopY}",
                        OffsetMin = "8 8",
                        OffsetMax = "-8 8"
                    },
                    new CuiScrollViewComponent
                    {
                        Vertical = true,
                        Horizontal = false,
                        ScrollSensitivity = 40f,
                        Elasticity = 0.1f,
                        ContentTransform = new CuiRectTransform
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "1 1",
                            OffsetMin = $"0 -{contentHeight}",
                            OffsetMax = "0 0"
                        },
                        VerticalScrollbar = new CuiScrollbar
                        {
                            Size = 2,
                            HandleColor = "1 1 1 1"
                        }
                    }
                }
            });

            for (int listIndex = 0; listIndex < totalRows; listIndex++)
            {
                int idx = visibleIndices[listIndex];
                var r = _config.recipes[idx];

                int minOffsetY = -((listIndex + 1) * rowHeight);
                int maxOffsetY = -(listIndex * rowHeight);

                string row = cont.Add(new CuiPanel
                {
                    Image = { Color = "0 0 0 0" },
                    RectTransform =
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "1 1",
                        OffsetMin = $"0 {minOffsetY}",
                        OffsetMax = $"-18 {maxOffsetY}"
                    }
                }, scrollName);

                var  prodDef = r.producedItem.ItemDefinition;
                bool hasPerm = HasRecipePerm(p, r);

                string iconColor = "1 1 1 1";
                string qtyColor  = UIRef.Text;
                if (_config.DimLockedRecipes && !hasPerm)
                {
                    iconColor = "1 1 1 0.35";
                    qtyColor  = "0.7 0.7 0.7 0.8";
                }

                if (prodDef != null)
                {
                    cont.Add(new CuiElement
                    {
                        Parent = row,
                        Components =
                        {
                            new CuiImageComponent
                            {
                                SkinId = r.producedItem.skinId,
                                ItemId = prodDef.itemid,
                                Color  = iconColor
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0.05 0.5",
                                AnchorMax = "0.05 0.5",
                                OffsetMin = "-14 -14",
                                OffsetMax = "14 14"
                            }
                        }
                    });

                    cont.Add(new CuiButton
                    {
                        RectTransform =
                        {
                            AnchorMin = "0.05 0.5",
                            AnchorMax = "0.05 0.5",
                            OffsetMin = "-14 -14",
                            OffsetMax = "14 14"
                        },
                        Button =
                        {
                            Command = hasPerm ? $"{CMD_AUTOFILL} {idx}" : "",
                            Color   = "0 0 0 0"
                        },
                        Text =
                        {
                            Text     = "",
                            Align    = TextAnchor.MiddleCenter,
                            Color    = "0 0 0 0",
                            FontSize = 1
                        }
                    }, row);
                }

                string amountGreen = $"<size=8><color=#00ff00ff>({r.producedItem.amount}X)</color></size>";

                string name = r.producedItem.amount > 1
                    ? $"{(r.producedItem.UiDisplayName ?? prodDef?.displayName?.english ?? r.producedItem.shortName)} {amountGreen}"
                    : (r.producedItem.UiDisplayName ?? prodDef?.displayName?.english ?? r.producedItem.shortName);

                if (_config.DimLockedRecipes && !hasPerm)
                    name += L("LockedMark", p);

                cont.Add(new CuiLabel
                {
                    RectTransform =
                    {
                        AnchorMin = "0.11 0",
                        AnchorMax = "0.40 1"
                    },
                    Text =
                    {
                        Text     = name,
                        Align    = TextAnchor.MiddleLeft,
                        Color    = qtyColor,
                        FontSize = 12
                    }
                }, row);

                for (int slot = 0; slot < RECIPE_MAX_SLOTS; slot++)
                {
                    string anchor = $"{0.58f + slot * 0.095f} 0.5";

                    cont.Add(new CuiPanel
                    {
                        Image = { Color = UIRef.Tile },
                        RectTransform =
                        {
                            AnchorMin = anchor,
                            AnchorMax = anchor,
                            OffsetMin = "-14 -14",
                            OffsetMax = "14 14"
                        }
                    }, row);

                    if (!r.ingredientSlots.TryGetValue(slot, out var ing) || ing == null)
                        continue;

                    var ingDef = ing.ItemDefinition;
                    if (ingDef == null)
                        continue;

                    cont.Add(new CuiElement
                    {
                        Parent = row,
                        Components =
                        {
                            new CuiImageComponent
                            {
                                SkinId = ing.skinId,
                                ItemId = ingDef.itemid,
                                Color  = iconColor
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = anchor,
                                AnchorMax = anchor,
                                OffsetMin = "-14 -14",
                                OffsetMax = "14 14"
                            }
                        }
                    });

                    string amtText = ing.amount < 1000
                        ? ing.amount.ToString()
                        : (ing.amount / 1000f).ToString("0.#", CultureInfo.InvariantCulture) + "k";

                    cont.Add(new CuiLabel
                    {
                        RectTransform =
                        {
                            AnchorMin = anchor,
                            AnchorMax = anchor,
                            OffsetMin = "-13 -16",
                            OffsetMax = "13 0"
                        },
                        Text =
                        {
                            Text     = amtText,
                            Align    = TextAnchor.MiddleRight,
                            Color    = qtyColor,
                            FontSize = 12
                        }
                    }, row);
                }
            }

            CuiHelper.AddUi(p, cont);
        }

        private static string XY(float x, float y)
        {
            var ci = CultureInfo.InvariantCulture;
            return x.ToString(ci) + " " + y.ToString(ci);
        }

        private void CreateCraftButtonUi(BasePlayer p, BaseEntity station)
        {
            if (!_uiOpen.Contains(p.userID))
                return;

            var cont = new CuiElementContainer();

            cont.Add(new CuiElement
            {
                Components =
                {
                    new CuiImageComponent
                    {
                        Color = UIRef.Panel,
                        Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0",
                        AnchorMax = "0.5 0",
                        OffsetMin = "193 109",
                        OffsetMax = "330 180"
                    }
                },
                Parent = "Hud.Menu",
                Name   = UIRef.LAYER_CRAFT
            });

            bool  busy       = GetJob(station, out var job);
            bool  open       = _uiOpen.Contains(p.userID);
            float remaining  = busy ? GetRemaining(station) : 0f;
            int   remainSecs = Mathf.Max(0, Mathf.CeilToInt(remaining));
            bool  enableBtn  = open && !busy;

            cont.Add(new CuiButton
            {
                RectTransform =
                {
                    AnchorMin = "0.03 0.06",
                    AnchorMax = "0.96 0.94"
                },
                Button =
                {
                    Command = enableBtn ? CMD_CRAFT : "",
                    Color   = enableBtn ? UIRef.BtnGreen : UIRef.BtnDisabled
                },
                Text =
                {
                    Text     = busy ? "" : L("Craft.Button", p),
                    Align    = TextAnchor.MiddleCenter,
                    Color    = UIRef.BtnText,
                    FontSize = 16
                }
            }, UIRef.LAYER_CRAFT);

            if (busy && job.TotalSeconds > 0f)
            {
                float prog = Mathf.Clamp01(GetProgress(station));

                cont.Add(new CuiPanel
                {
                    Image =
                    {
                        Color = "0.12 0.12 0.12 0.9"
                    },
                    RectTransform =
                    {
                        AnchorMin = "0.06 0.10",
                        AnchorMax = "0.94 0.30"
                    }
                }, UIRef.LAYER_CRAFT);

                float startX = 0.06f;
                float endX   = 0.94f;
                float width  = endX - startX;
                float fillX  = startX + width * prog;

                cont.Add(new CuiPanel
                {
                    Image =
                    {
                        Color = "0.35 0.7 0.35 0.9"
                    },
                    RectTransform =
                    {
                        AnchorMin = "0.06 0.10",
                        AnchorMax = XY(fillX, 0.30f)
                    }
                }, UIRef.LAYER_CRAFT);

                cont.Add(new CuiLabel
                {
                    RectTransform =
                    {
                        AnchorMin = "0.06 0.10",
                        AnchorMax = "0.94 0.30"
                    },
                    Text =
                    {
                        Text     = $"{remainSecs}s",
                        Align    = TextAnchor.MiddleCenter,
                        Color    = UIRef.Text,
                        FontSize = UIRef.SecondsFont
                    }
                }, UIRef.LAYER_CRAFT);
            }

            if (busy && job != null)
            {
                string prodName = job.Recipe.producedItem.UiDisplayName
                                  ?? job.Recipe.producedItem.ItemDefinition?.displayName?.english
                                  ?? job.Recipe.producedItem.shortName;

                string amountGreen = $"<size=8><color=#00ff00ff>({job.TotalUnits}X)</color></size>";

                string header =
                    "<size=14><color=#ffff66ff>Fabricando:</color></size>";

                string line2 =
                    $"\n<size=11>{prodName} {amountGreen}</size>";

                cont.Add(new CuiLabel
                {
                    RectTransform =
                    {
                        AnchorMin = "0.03 0.36",
                        AnchorMax = "0.97 0.94"
                    },
                    Text =
                    {
                        Text     = $"{header}{line2}",
                        Align    = TextAnchor.MiddleCenter,
                        Color    = UIRef.Text,
                        FontSize = 12
                    }
                }, UIRef.LAYER_CRAFT);
            }

            CuiHelper.AddUi(p, cont);
        }

        private void UpdateCraftButtonUi(BasePlayer p, BaseEntity station, bool force = false)
        {
            if (p == null || station == null)
                return;

            bool busy = GetJob(station, out _);
            bool open = _uiOpen.Contains(p.userID);

            if (!open)
            {
                CuiHelper.DestroyUi(p, UIRef.LAYER_CRAFT);
                _lastUi[p.userID] = new CraftUiState { Open = false, Busy = false, RemainSecs = 0, StationId = 0 };
                return;
            }

            int  remainSecs = busy ? Mathf.Max(0, Mathf.CeilToInt(GetRemaining(station))) : 0;
            ulong sid = EntityId(station);

            _lastUi.TryGetValue(p.userID, out var prev);

            bool changed = force
                || prev == null
                || prev.Open != open
                || prev.Busy != busy
                || prev.RemainSecs != remainSecs
                || prev.StationId != sid;

            if (!changed)
                return;

            _lastUi[p.userID] = new CraftUiState
            {
                Open = open,
                Busy = busy,
                RemainSecs = remainSecs,
                StationId = sid
            };

            CuiHelper.DestroyUi(p, UIRef.LAYER_CRAFT);
            CreateCraftButtonUi(p, station);
        }

        private void DestroyUi(BasePlayer p)
        {
            if (p == null) return;
            CuiHelper.DestroyUi(p, UIRef.LAYER_RECIPE);
            CuiHelper.DestroyUi(p, UIRef.LAYER_TOGGLE);
            CuiHelper.DestroyUi(p, UIRef.LAYER_CRAFT);
        }

        private void RefreshUi(BasePlayer p, BaseEntity station)
        {
            DestroyUi(p);
            CreateToggleUi(p);
            if (_uiOpen.Contains(p.userID))
                CreateRecipeUi(p);

            UpdateCraftButtonUi(p, station, force: true);
        }

        private void StartUiTicker(BasePlayer p, BaseEntity station)
        {
            StopUiTicker(p);
            _uiTickers[p.userID] = timer.Every(0.2f, () =>
            {
                var current = p?.inventory?.loot?.entitySource as BaseEntity;
                if (current != station)
                {
                    StopUiTicker(p);
                    DestroyUi(p);
                    _lastUi.Remove(p.userID);
                    return;
                }

                UpdateCraftButtonUi(p, station, force: false);
            });
        }

        private void StopUiTicker(BasePlayer p)
        {
            if (p == null) return;
            if (_uiTickers.TryGetValue(p.userID, out var tm))
            {
                tm?.Destroy();
                _uiTickers.Remove(p.userID);
            }
        }
        #endregion

        #region Commands
        private void CmdToggleUi(IPlayer ip, string cmd, string[] args)
        {
            if (ip.Object is not BasePlayer p)
                return;

            if (!HasPluginPerm(p))
            {
                DestroyUi(p);
                return;
            }

            if (!TryGetStation(p, out var station, out _))
            {
                DestroyUi(p);
                return;
            }

            if (_uiOpen.Contains(p.userID))
            {
                CancelJobAndRefund(p, station);
                _uiOpen.Remove(p.userID);
            }
            else
            {
                _uiOpen.Add(p.userID);
            }

            _pageByPlayer[p.userID] = 0;
            _tabByPlayer.Remove(p.userID);
            RefreshUi(p, station);
        }

        private void CancelJobAndRefund(BasePlayer player, BaseEntity station)
        {
            if (player == null || station == null)
                return;

            if (!GetJob(station, out var job))
                return;

            if (job.StarterId != player.userID)
                return;

            job.Timer?.Destroy();
            ClearPendingUsageForJob(job);
            _jobsByEntity.Remove(EntityId(station));

            OnStationCraftCanceled(station);

            var container = job.Container;
            if (container != null)
            {
                for (int slot = 0; slot < container.capacity; slot++)
                {
                    if (!job.Recipe.ingredientSlots.ContainsKey(slot))
                        continue;

                    var item = container.GetSlot(slot);
                    if (item == null)
                        continue;

                    GiveBack(player, item);
                }

                ItemManager.DoRemoves();
            }

            SendReply(player, L("Craft.Canceled", player));
        }

        private void CmdCraft(IPlayer ip, string cmd, string[] args)
        {
            var p = ip.Object as BasePlayer;
            if (p == null) return;
            if (!HasPluginPerm(p)) return;
            if (!_uiOpen.Contains(p.userID)) return;
            if (!TryGetStation(p, out var station, out var container)) return;
            if (GetJob(station, out _)) return;

            foreach (var r in _config.recipes)
            {
                if (!HasRecipePerm(p, r))
                    continue;

                if (!r.TryCollect(container, out var times, out _, out _))
                    continue;

                if (!ContainerExactMatch(container, r, times))
                    continue;

                string key = r.GetStableKey();

                // Anti-bypass: do not allow starting the SAME limited recipe while it's already crafting in any station
                if (r.perPlayerLimit > 0 && GetPendingUsage(p.userID, key) > 0)
                {
                    SendReply(p, L("AlreadyCrafting", p));
                    return;
                }

                int remaining = GetRemainingLimit(p, r, key);

                if (remaining <= 0)
                {
                    SendReply(p, L("LimitReached", p, 0));
                    return;
                }

                int usageForTimes = _config.LimitCountsUnits
                    ? (Math.Max(1, r.producedItem.amount) * times)
                    : times;

                if (usageForTimes > remaining)
                {
                    if (_config.LimitCountsUnits)
                    {
                        int unitsPerSet = Math.Max(1, r.producedItem.amount);
                        int maxSets = Mathf.FloorToInt((float)remaining / unitsPerSet);
                        if (maxSets <= 0)
                        {
                            SendReply(p, L("LimitReached", p, 0));
                            return;
                        }

                        times = Mathf.Max(1, maxSets);
                        SendReply(p, L("LimitAdjusted", p, times));
                    }
                    else
                    {
                        times = Mathf.Max(1, remaining);
                        SendReply(p, L("LimitAdjusted", p, times));
                    }

                    usageForTimes = _config.LimitCountsUnits
                        ? (Math.Max(1, r.producedItem.amount) * times)
                        : times;
                }

                ReturnExcessToPlayer(p, container, r, times);
                StartCraftJob(p, station, container, r, times, key, usageForTimes);
                RefreshUi(p, station);
                return;
            }

            SendReply(p, L("Craft.NoneMatch", p));
        }

        private void CmdPageUp(IPlayer ip, string cmd, string[] args)
        {
            if (ip.Object is not BasePlayer p) return;
            if (!_uiOpen.Contains(p.userID)) return;
            if (!TryGetStation(p, out var station, out _)) return;

            int page = 0;
            _pageByPlayer.TryGetValue(p.userID, out page);
            page = Math.Max(0, page - 1);
            _pageByPlayer[p.userID] = page;

            RefreshUi(p, station);
        }

        private void CmdPageDown(IPlayer ip, string cmd, string[] args)
        {
            if (ip.Object is not BasePlayer p) return;
            if (!_uiOpen.Contains(p.userID)) return;
            if (!TryGetStation(p, out var station, out _)) return;

            int page = 0;
            _pageByPlayer.TryGetValue(p.userID, out page);
            page = page + 1;
            _pageByPlayer[p.userID] = page;

            RefreshUi(p, station);
        }

        private void CmdSetTab(IPlayer ip, string cmd, string[] args)
        {
            var p = ip.Object as BasePlayer;
            if (p == null) return;
            if (!_uiOpen.Contains(p.userID)) return;
            if (!TryGetStation(p, out var station, out _)) return;
            if (args.Length < 1) return;

            string cat = args[0];
            _tabByPlayer[p.userID] = cat;
            _pageByPlayer[p.userID] = 0;
            RefreshUi(p, station);
        }

        [ConsoleCommand(CMD_AUTOFILL)]
        private void CcmdAutoFill(ConsoleSystem.Arg arg)
        {
            var p = arg?.Player();
            if (p == null) return;
            int idx = arg.GetInt(0, -1);
            CmdAutoFill(p.IPlayer, CMD_AUTOFILL, new[] { idx.ToString() });
        }

        void CmdAutoFill(IPlayer ip, string cmd, string[] args)
        {
            var p = ip.Object as BasePlayer;
            if (p == null) return;
            if (!HasPluginPerm(p)) return;
            if (!_uiOpen.Contains(p.userID)) return;
            if (!TryGetStation(p, out var station, out var container)) return;
            if (GetJob(station, out _)) return;

            if (args.Length < 1 || !int.TryParse(args[0], out int recipeIndex)) return;
            if (recipeIndex < 0 || recipeIndex >= _config.recipes.Count) return;

            var r = _config.recipes[recipeIndex];
            if (!HasRecipePerm(p, r))
            {
                SendReply(p, L("NoPermission", p));
                return;
            }

            // 1) Limpa completamente a estação, devolvendo tudo para o player
            for (int s = 0; s < container.capacity; s++)
            {
                var it = container.GetSlot(s);
                if (it == null) continue;
                GiveBack(p, it);
            }
            ItemManager.DoRemoves();

            // 2) Calcula quantos "sets" completos dá pra montar SOMENTE pelo inventário
            int maxSets = int.MaxValue;

            foreach (var kv in r.ingredientSlots)
            {
                var ing = kv.Value;
                if (ing == null) continue;

                int have = CountInPlayer(p, ing.shortName, ing.skinId);
                if (have <= 0)
                {
                    maxSets = 0;
                    break;
                }

                int setsForThis = have / Math.Max(1, ing.amount);
                if (setsForThis < maxSets)
                    maxSets = setsForThis;
            }

            if (maxSets == int.MaxValue)
                maxSets = 0;

            if (maxSets <= 0)
            {
                // Sem itens suficientes para formar pelo menos 1 receita
                RefreshUi(p, station);
                return;
            }

            // Limite de segurança para não puxar milhares de itens de uma vez
            maxSets = Mathf.Clamp(maxSets, 1, 100);

            // 3) Move exatamente a quantidade calculada para cada slot da receita
            for (int slot = 0; slot < RECIPE_MAX_SLOTS; slot++)
            {
                if (!r.ingredientSlots.TryGetValue(slot, out var ing) || ing == null)
                    continue;

                int need = ing.amount * maxSets;
                MoveFromPlayerToContainerSlot(p, container, slot, ing.shortName, ing.skinId, need);
            }

            ItemManager.DoRemoves();
            RefreshUi(p, station);
        }

        private static bool ItemMatches(Item it, string shortName, ulong skin)
        {
            if (it == null || it.info == null)
                return false;

            if (it.info.shortname != shortName)
                return false;

            // Se a receita usa skin específica (> 0), exige exatamente essa skin
            if (skin > 0 && it.skin != skin)
                return false;

            // Se a receita usa skin 0 (item sem skin), exige que o item também não tenha skin
            if (skin == 0 && it.skin != 0)
                return false;

            return true;
        }

        private int CountInPlayer(BasePlayer p, string shortName, ulong skin)
        {
            if (p == null) return 0;

            int total = 0;

            foreach (var cont in new[]
            {
                p.inventory.containerMain,
                p.inventory.containerBelt,
                p.inventory.containerWear
            })
            {
                if (cont == null) continue;

                foreach (var it in cont.itemList)
                {
                    if (ItemMatches(it, shortName, skin))
                        total += it.amount;
                }
            }

            return total;
        }

        private void MoveFromPlayerToContainerSlot(BasePlayer p, ItemContainer dest, int slot, string shortName, ulong skin, int amount)
        {
            if (p == null || dest == null || amount <= 0)
                return;

            int left = amount;

            var existing = dest.GetSlot(slot);
            if (existing != null && !ItemMatches(existing, shortName, skin))
            {
                GiveBack(p, existing);
                existing = null;
            }

            foreach (var cont in new[]
            {
                p.inventory.containerMain,
                p.inventory.containerBelt,
                p.inventory.containerWear
            })
            {
                if (cont == null || left <= 0)
                    continue;

                var items = cont.itemList.ToArray();
                foreach (var it in items)
                {
                    if (left <= 0)
                        break;

                    if (!ItemMatches(it, shortName, skin))
                        continue;

                    int take = Math.Min(left, it.amount);
                    Item portion = (take == it.amount) ? it : it.SplitItem(take);
                    if (portion == null)
                        continue;

                    if (!portion.MoveToContainer(dest, slot, true))
                    {
                        if (!portion.MoveToContainer(dest))
                            GiveBack(p, portion);
                        else
                        {
                            portion.position = slot;
                            portion.MarkDirty();
                        }
                    }

                    left -= take;
                }
            }

            ItemManager.DoRemoves();
        }

        private void CmdLimitResetAll(IPlayer ip, string cmd, string[] args)
        {
            if (!ip.IsServer && !ip.IsAdmin) return;
            _limits.Clear();
            SaveData();
            ip.Reply("MixCraftLite: todos os limites foram zerados.");
        }

        private void CmdLimitResetUser(IPlayer ip, string cmd, string[] args)
        {
            if (!ip.IsServer && !ip.IsAdmin) return;
            if (args.Length < 1)
            {
                ip.Reply("uso: mixcraft.limitresetuser <steamid>");
                return;
            }

            if (!ulong.TryParse(args[0], out var id))
            {
                ip.Reply("steamid inválido.");
                return;
            }

            _limits.Remove(id);
            SaveData();
            ip.Reply($"MixCraftLite: limites do usuário {id} zerados.");
        }

        private void CmdLimitResetRecipe(IPlayer ip, string cmd, string[] args)
        {
            if (!ip.IsServer && !ip.IsAdmin) return;
            if (args.Length < 1)
            {
                ip.Reply("uso: mixcraft.limitresetrecipe <recipeKey>");
                return;
            }

            string recipeKey = args[0];
            foreach (var kv in _limits)
            {
                kv.Value?.Counters?.Remove(recipeKey);
            }
            SaveData();
            ip.Reply($"MixCraftLite: limites da receita '{recipeKey}' removidos.");
        }
        #endregion
    }
}
