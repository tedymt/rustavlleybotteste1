using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Facepunch;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Rust;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Oxide.Plugins
{
    [Info("Dungeon Events", "Marte6", "3.7.0")]
    [Description("Create dungeons populated with NPCs, turrets and loot containers.")]
    public class DungeonEvents : RustPlugin
    {
        #region Fields
        [PluginReference]
        private Plugin Friends,
            Economics,
            ServerRewards,
            Notify,
            NightVision,
            ZoneManager,
            Duelist,
            RaidableBases,
            AbandonedBases,
            RestoreUponDeath,
            SkillTree;
        private static DungeonEvents _instance;
        private Configuration _config;
        private List<ActiveDungeon> _activeDungeons = new List<ActiveDungeon>();
        private List<BasePlayer> _playersBuying = new List<BasePlayer>();
        private List<BasePlayer> _wasInDungeonPlayers = new List<BasePlayer>();
        private Dictionary<ulong, Dictionary<string, DateTime>> _buyCooldowns = new Dictionary<ulong, Dictionary<string, DateTime>>();
        private List<DungeonMarker> _activeDungeonMarkers = new List<DungeonMarker>();
        private Queue<DungeonRequest> _dungeonQueue = new Queue<DungeonRequest>();
        private Queue<DungeonRequest> _dungeonFailedAutoSpawnQueue = new Queue<DungeonRequest>();
        private Queue<DungeonRequest> _dungeonFailedBuyQueue = new Queue<DungeonRequest>();
        private Timer _dungeonTimer;
        private bool isCreatingDungeon = false;
        private const float TimeTimers = 10f;
        private const string PermissionAdmin = "dungeonevents.admin";
        private const string PermissionBuy = "dungeonevents.buy";
        private const string PermissionEnter = "dungeonevents.enter";
        private const ulong OwnerID = 10203040505;
        private const string UiEventPanel = "yFifW5s1zAY09h5cU0qeIQUNV6GGwevG";
        private const string UiMainPanel = "5cU06GGweeIQ09hyFifW5qvGVsUNAY1z";
        private const string UiCommandClose = "lDM82GJH3biLcI95ic8zzteteOeQx77h";
        private Dictionary<DungeonTierConfig, TierInfo> _tierInfoMap = new Dictionary<DungeonTierConfig, TierInfo>();
        private DateTime _lastAutoSpawnTime = DateTime.MinValue;
        private WaitForSeconds SmallDelay = new WaitForSeconds(0.15f);
        private WaitForSeconds BigDelay = new WaitForSeconds(0.50f);
        private Coroutine _startupRoutine;
        private const string WallPrefab = "assets/prefabs/building core/wall/wall.prefab";
        private const string FoundationPrefab = "assets/prefabs/building core/foundation/foundation.prefab";
        private const string CeilingPrefab = "assets/prefabs/building core/floor/floor.prefab";
        private const string TurretPrefab = "assets/prefabs/npc/autoturret/autoturret_deployed.prefab";
        private const string LightPrefab = "assets/bundled/prefabs/modding/cinematic/cinelights/cinelight_point_red.prefab";
        private const string PortalPrefab = "assets/prefabs/missions/portal/halloweenportalexit.prefab";
        private const string WallFramePrefab = "assets/prefabs/building core/wall.frame/wall.frame.prefab";
        private const string DoorPrefab = "assets/prefabs/building/wall.frame.garagedoor/wall.frame.garagedoor.prefab";
        private readonly List<ActiveDungeon> _activeRooms = new List<ActiveDungeon>();
        private readonly Dictionary<Vector3, ZoneInfo> _blockedZones = new Dictionary<Vector3, ZoneInfo>();
        private readonly Vector3 _wallOffset = new Vector3(-1.5f, 0, -1.5f);
        private static readonly Vector3 CeilingLightOffset = new Vector3(-1.5f, 0, -1.5f);
        private static readonly List<string> RockKeywords = new List<string> { "rock", "formation", "junk", "cliff", "invisible" };
        private int _gridSize = 5;
        private const int SubCells = 2;
        private const int MinimumActiveCells = 13;
        private const int MaxGenerationAttempts = 1000;
        private const float CellSpacing = 6f;
        private const float SubCellSpacing = 3f;
        private const float WallRotationOffset = 90f;
        private const float CeilingHeight = 3f;
        private const float CorridorLength = 12f;
        private const bool SpawnTurrets = false;
        private const bool PlaceCeiling = true;
        private const bool PlaceLights = true;
        private uint _randomSeed;
        private static readonly string[] BlockedRespawnPrefabs =
        {
            "assets/prefabs/deployable/bed/bed_deployed.prefab",
            "assets/prefabs/misc/summer_dlc/beach_towel/beachtowel.deployed.prefab",
            "assets/prefabs/deployable/sleeping bag/sleepingbag_leather_deployed.prefab",
        };
        private const string ScientistPrefab = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab";
        #endregion

        #region Hooks
        void Init()
        {
            _instance = this;
            permission.RegisterPermission(PermissionEnter, this);
            permission.RegisterPermission(PermissionAdmin, this);
            permission.RegisterPermission(PermissionBuy, this);
        }

        void OnServerInitialized()
        {
            float smallDelaySec = Mathf.Clamp(_config.SmallDelay, 0.15f, 5f);
            float bigDelaySec = Mathf.Clamp(_config.BigDelay, 0.50f, 5f);
            SmallDelay = new WaitForSeconds(smallDelaySec);
            BigDelay = new WaitForSeconds(bigDelaySec);
            _startupRoutine = ServerMgr.Instance.StartCoroutine(StartupRoutine());
            NextTick(RegisterCommands);
        }

        void Unload()
        {
            if (_startupRoutine != null)
                ServerMgr.Instance.StopCoroutine(_startupRoutine);
            _dungeonTimer?.Destroy();
            _dungeonQueue.Clear();
            DestroyAllUis();
            RemoveAllDungeons();
        }

        object CanDropActiveItem(BasePlayer player)
        {
            if (RestoreUponDeath != null)
            {
                return null;
            }
            if (player == null)
                return null;
            var wasInDungeon = _wasInDungeonPlayers.FirstOrDefault(d => d == player);
            if (wasInDungeon != null)
            {
                var activeItem = player.GetActiveItem();
                if (activeItem != null)
                    return false;
            }
            return null;
        }

        object OnBackpackDrop(Item backpack, PlayerInventory inventory)
        {
            if (!_config.PreventDropBackpack)
                return null;
            var player = inventory?.containerMain?.playerOwner;
            if (RestoreUponDeath != null)
                return null;
            if (player == null)
                return null;
            var wasInDungeon = _wasInDungeonPlayers.FirstOrDefault(d => d == player);
            if (wasInDungeon != null)
                return true;
            return null;
        }

        object CanDropBackpack(ulong userID, Vector3 backpackPluginHook)
        {
            var player = BasePlayer.Find(userID.ToString());
            if (player == null)
                return null;
            var wasInDungeon = _wasInDungeonPlayers.FirstOrDefault(d => d == player);
            if (wasInDungeon != null)
                return false;
            return null;
        }

        object CanPickupEntity(BasePlayer player, BaseEntity entity)
        {
            if (player == null)
                return null;
            if (GetDungeonForPlayer(player) != null)
                return false;
            return null;
        }

        object CanDeployItem(BasePlayer player, Deployer deployer, ulong entityId)
        {
            if (player == null)
                return null;
            var dungeon = GetDungeonForPlayer(player);
            if (dungeon != null)
                return false;
            return null;
        }

        object CanBuild(Planner planner, Construction prefab, Construction.Target target)
        {
            var player = planner?.GetOwnerPlayer();
            if (player == null)
                return null;
            if (GetDungeonForPlayer(player) != null)
                return false;
            Vector3 buildPos = target.position;
            if (buildPos == Vector3.zero && target.socket != null && target.entity != null)
                buildPos = target.GetWorldPosition();
            float r = _config.RadiusSphere;
            float r2 = r * r;
            foreach (var dungeon in _activeDungeons)
            {
                if (dungeon.PortalPosition == Vector3.zero)
                    continue;
                float dx = buildPos.x - dungeon.PortalPosition.x;
                float dz = buildPos.z - dungeon.PortalPosition.z;
                if (dx * dx + dz * dz <= r2)
                    return false;
            }
            return null;
        }

        object OnPlayerCommand(BasePlayer player, string command, string[] args)
        {
            if (player == null)
                return null;
            if (GetDungeonForPlayer(player) == null)
                return null;
            if (_config.BlockCommands.Contains("/" + command.ToLower()))
            {
                NotifyPlayer(player, "BlockedCommands", 0);
                return true;
            }
            return null;
        }

        object OnServerCommand(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.cmd == null)
                return null;
            BasePlayer player = arg.Player();
            if (player != null && (GetDungeonForPlayer(player) != null))
            {
                if (_config.BlockCommands.Contains(arg.cmd.Name.ToLower()) || _config.BlockCommands.Contains(arg.cmd.FullName.ToLower()))
                {
                    NotifyPlayer(player, "BlockedCommands", 0);
                    return true;
                }
            }
            return null;
        }

        object OnCustomNpcTarget(ScientistNPC scientistNPC, BasePlayer player)
        {
            if (scientistNPC == null || !IsDungeonEntity(scientistNPC))
                return null;
            if (player == null || !player.userID.IsSteamId())
                return false;
            return null;
        }

        object CanEntityBeTargeted(BasePlayer player, AutoTurret turret)
        {
            if (turret == null || !IsDungeonEntity(turret))
                return null;
            if (player == null || !player.userID.IsSteamId())
                return false;
            return true;
        }

        object OnEntityEnter(TargetTrigger trigger, ScientistNPC scientistNPC)
        {
            if (trigger == null || scientistNPC == null || scientistNPC.net == null || scientistNPC.userID.IsSteamId())
                return null;
            AutoTurret autoTurret = trigger.GetComponentInParent<AutoTurret>();
            if (autoTurret == null || autoTurret.net == null)
                return null;
            if (IsDungeonEntity(autoTurret))
                return true;
            return null;
        }

        object OnPortalUse(BasePlayer player, BasePortal portal)
        {
            var activeDungeon = GetActiveDungeonByPortal(portal);
            if (activeDungeon == null)
                return null;
            if (!activeDungeon.Spawned)
            {
                NotifyPlayer(player, "DungeonStillSpawningMessage", 0);
                return false;
            }
            if (_config.LockDungeonToPlayer && !CanEnter(player, activeDungeon))
            {
                NotifyPlayer(player, "NoAccess", 0);
                return false;
            }
            return null;
        }

        void OnCorpsePopulate(ScientistNPC npc, NPCPlayerCorpse corpse)
        {
            if (npc == null || corpse == null)
                return;
            if (!IsDungeonEntity(npc))
                return;
            var activeDungeon = GetActiveDungeonByEntity(npc);
            if (activeDungeon == null)
                return;
            bool isBoss = npc.GetComponent<BossMonoBehaviour>() != null;
            var corpseData = corpse.gameObject.AddComponent<CorpseLootData>();
            corpseData.IsBoss = isBoss;
            corpseData.LootConfig = isBoss ? activeDungeon.TierConfig?.BossNpcConfig : activeDungeon.TierConfig?.NpcConfig;
            NextTick(() =>
            {
                if (corpse == null || corpse.IsDestroyed)
                    return;
                var storedData = corpse.GetComponent<CorpseLootData>();
                if (storedData == null)
                    return;
                if (storedData.LootConfig == null)
                    return;
                Effect.server.Run("assets/prefabs/weapons/flashbang/effects/fx-flashbang-boom.prefab", corpse.transform.position);
                if (corpse.containers == null || corpse.containers.Length == 0 || corpse.containers[0] == null)
                    return;
                var container = corpse.containers[0];
                container.Clear();
                var items = GetNpcLootItems(storedData.LootConfig);
                if (items == null || items.Count == 0)
                    return;
                int maxDifferentItems = storedData.IsBoss ? (activeDungeon?.TierConfig?.MaxItemsPerBossNpc ?? int.MaxValue) : (activeDungeon?.TierConfig?.MaxItemsPerNpc ?? int.MaxValue);
                var lootItems = items.OrderBy(_ => Random.value).ToList();
                int differentItemsCount = 0;
                foreach (var itemCfg in lootItems)
                {
                    if (differentItemsCount >= maxDifferentItems)
                        break;
                    if (Random.value * 100f > itemCfg.InclusionChancePercentage)
                        continue;
                    var def = ItemManager.FindItemDefinition(itemCfg.ShortName);
                    if (def == null)
                        continue;
                    int min = itemCfg.MinimumAmount;
                    int max = itemCfg.MaximumAmount;
                    if (min <= 0 || max <= 0)
                        continue;
                    if (max < min)
                    {
                        int t = min;
                        min = max;
                        max = t;
                    }
                    int amount = Random.Range(min, max + 1);
                    if (amount <= 0)
                        continue;
                    if (container.itemList.Count >= container.capacity)
                        break;
                    var item = ItemManager.Create(def, amount, itemCfg.SkinID);
                    if (item == null)
                        continue;
                    if (!string.IsNullOrEmpty(itemCfg.CustomName))
                        item.name = itemCfg.CustomName;
                    item.MoveToContainer(container);
                    differentItemsCount++;
                }
            });
        }

        void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (player == null || !player.userID.IsSteamId())
                return;
            ActiveDungeon dungeon;
            try
            {
                dungeon = GetDungeonForPlayer(player);
            }
            catch (Exception)
            {
                return;
            }
            if (dungeon == null)
                return;
            try
            {
                TeleportPlayerToOutsidePortal(player, dungeon);
            }
            catch (Exception) { }
            if (!_config.CloseDoorsOnDeath || dungeon.Entities == null)
                return;
            foreach (var door in dungeon.Entities.OfType<Door>())
            {
                if (door == null || door.IsDestroyed || door.transform == null)
                    continue;
                try
                {
                    door.SetOpen(false);
                    door.SendNetworkUpdate();
                }
                catch (Exception) { }
            }
        }

        object CanRaidBlock(BasePlayer player)
        {
            var dungeon = GetDungeonForPlayer(player);
            if (dungeon != null)
                return false;
            return null;
        }

        void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info?.InitiatorPlayer == null)
                return;
            ActiveDungeon dungeon = GetActiveDungeonByEntity(entity);
            if (dungeon == null || !dungeon.Spawned)
                return;
            bool isNpc = entity is ScientistNPC;
            bool isTurret = entity is AutoTurret;
            bool isBox = entity is StorageContainer;
            if (!isNpc && !isTurret && !isBox)
                return;
            RewardConfig r = dungeon.TierConfig?.RewardConfig;
            if (r == null)
                return;
            int econ =
                isNpc ? r.NpcEconomicsReward
                : isTurret ? r.TurretEconomicsReward
                : r.BoxEconomicsReward;
            int sr =
                isNpc ? r.NpcServerRewards
                : isTurret ? r.TurretServerRewards
                : r.BoxServerRewards;
            BasePlayer attacker = info.InitiatorPlayer;
            if (econ > 0 && Economics != null)
            {
                Economics.Call("Deposit", attacker.userID, (double)econ);
                attacker.ChatMessage(Msg(attacker.UserIDString, "EconRewardMessage", econ));
            }
            if (sr > 0 && ServerRewards != null)
            {
                ServerRewards.Call("AddPoints", attacker.userID, sr);
                attacker.ChatMessage(Msg(attacker.UserIDString, "ServerRewardMessage", sr));
            }
        }

        object OnEntityTakeDamage(BuildingBlock block, HitInfo info)
        {
            if (!IsDungeonEntity(block))
                return null;
            info.damageTypes?.ScaleAll(0f);
            return true;
        }

        object OnEntityTakeDamage(ScientistNPC scientistNPC, HitInfo info)
        {
            if (scientistNPC == null || info == null || info.Initiator == null)
                return null;
            if (info.Initiator is AutoTurret turret && IsDungeonEntity(turret))
            {
                info.damageTypes?.ScaleAll(0f);
                return null;
            }
            return null;
        }

        object CanEntityTakeDamage(BuildingBlock block, HitInfo info)
        {
            if (!IsDungeonEntity(block))
                return null;
            info.damageTypes?.ScaleAll(0f);
            return false;
        }

        object CanEntityTakeDamage(BasePlayer victim, HitInfo info)
        {
            if (info?.Initiator == null || info.Initiator.net == null || !IsPlayer(victim))
                return null;
            if (info.Initiator is ScientistNPC && IsDungeonEntity(info.Initiator))
                return true;
            if (info.Initiator is AutoTurret && IsDungeonEntity(info.Initiator))
                return true;
            return null;
        }

        object CanEntityTakeDamage(AutoTurret autoTurret, HitInfo hitinfo)
        {
            if (autoTurret == null || hitinfo == null)
                return null;
            if (!IsDungeonEntity(autoTurret))
                return null;
            if (hitinfo.InitiatorPlayer == null || !IsPlayer(hitinfo.Initiator as BasePlayer))
                return false;
            return true;
        }

        object CanEntityTakeDamage(StorageContainer storageContainer, HitInfo hitinfo)
        {
            if (storageContainer == null || hitinfo == null)
                return null;
            if (!IsDungeonEntity(storageContainer))
                return null;
            if (hitinfo.InitiatorPlayer == null || !IsPlayer(hitinfo.Initiator as BasePlayer))
                return false;
            return true;
        }

        object CanEntityTakeDamage(Door victim, HitInfo hitinfo)
        {
            if (victim == null || hitinfo == null)
                return null;
            if (!IsDungeonEntity(victim))
                return null;
            if (hitinfo.InitiatorPlayer == null || !IsPlayer(hitinfo.Initiator as BasePlayer))
                return false;
            return true;
        }
        #endregion

        #region Data Initialization
        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                JObject raw;
                var threshold = new VersionNumber(3, 6, 0);
                VersionNumber parsedVersion = new VersionNumber(0, 0, 0);
                var hasVersion = TryReadRawConfig(out raw) && TryParseConfigVersion(raw, out parsedVersion);
                if (!hasVersion || parsedVersion < threshold)
                {
                    PrintWarning($"Legacy config detected {(hasVersion ? parsedVersion.ToString() : "unknown")} updating to {threshold}+.");
                    LoadDefaultConfig();
                    _config.Version = Version;
                    EnsureLootDataFiles();
                    InitializeTierInfoMap();
                    SaveConfig();
                    return;
                }
                var mutated = MigrateTiersObjectToArray(raw);
                _config = raw.ToObject<Configuration>() ?? new Configuration();
                var changed = false;
                if (_config.Version == null || _config.Version < Version)
                {
                    _config.Version = Version;
                    changed = true;
                }
                EnsureLootDataFiles();
                InitializeTierInfoMap();
                if (mutated || changed)
                    SaveConfig();
            }
            catch (Exception ex)
            {
                PrintError($"Error reading config: {ex.Message}");
                LoadDefaultConfig();
                _config.Version = Version;
                EnsureLootDataFiles();
                InitializeTierInfoMap();
                SaveConfig();
            }
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new configuration file");
            _config = new Configuration { Version = Version };
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(
                new Dictionary<string, string>
                {
                    ["AlreadyOwnsDungeonMessage"] = "You already own an active dungeon. You cannot purchase another one until it expires or is removed.",
                    ["DungeonAppeared"] = "A {0} has appeared at grid {1}!",
                    ["DungeonRemovedDetail"] = "Removed dungeon of tier {0} at grid {1}.",
                    ["DungeonSpawnedSuccess"] = "Dungeon spawned successfully at grid {0}!",
                    ["NpcSpawnNotLoaded"] = "NpcSpawn plugin is not loaded.",
                    ["DungeonStillSpawningMessage"] = "The dungeon is still spawning. Please wait until it's ready before entering.",
                    ["InsufficientFundsMessage"] = "You don't have enough funds to buy the {0} tier. Required: {1}.",
                    ["InvalidTierMessage"] = "Invalid tier name '{0}'. Available tiers: {1}.",
                    ["ThisTierDisabled"] = "This Tier is disabled for purchases",
                    ["MaxActiveDungeonsReached"] = "Cannot spawn: Maximum number of active dungeons reached.",
                    ["NoAccess"] = "You don't have permission to do this.",
                    ["PurchaseSuccessMessage"] = "You have successfully purchased the {0} tier!",
                    ["ReceivedRequest"] = "Your request has been added to the queue. Please wait.",
                    ["UsageCreateMessage"] = "Usage: /createdungeon <tier_name>",
                    ["CleanedUp"] = "All entities related to this plugin have been cleaned up.",
                    ["NoInactiveDungeonsToRemove"] = "There are no inactive dungeons to remove.",
                    ["WithdrawFailedMessage"] = "Failed to withdraw funds for {0}.",
                    ["DungeonBuyTitle"] = "Buy Dungeon",
                    ["BuyButton"] = "{0} ({1})",
                    ["BuyButtonWithName"] = "{0} ({1} {2})",
                    ["CloseButton"] = "Close",
                    ["DungeonRemovalCountdown"] = "Your dungeon will be removed in {0} minute(s)!",
                    ["BlockedCommands"] = "You cannot use commands inside a dungeon!",
                    ["EconRewardMessage"] = "You received {0} RP!",
                    ["ServerRewardMessage"] = "You received {0} RN!",
                    ["DungeonPurchased"] = "A {0} dungeon purchased by {1} has appeared at grid {2}!",
                    ["DungeonCooldownMessage"] = "You must wait {0} seconds before purchasing another dungeon.",
                    ["DungeonClearEconomyReward"] = "You received {0} RP for clearing the dungeon!",
                    ["DungeonClearServerReward"] = "You received {0} RN for clearing the dungeon!",
                    ["Dungeonclosed"] = "Dungeon closed. You have been teleported to the exit.",
                    ["DungeonClearXpReward"] = "You received {0} XP for clearing the dungeon!",
                    ["NoDungeonsToRemove"] = "There are no dungeons to remove.",
                    ["TooFarToRemove"] = "You must be within 10 meters of a dungeon to remove it.",
                    ["NotDungeonOwner"] = "You don't own an active dungeon.",
                    ["ConfigReloaded"] = "Configuration reloaded.",
                    ["PurchasesEnabled"] = "Purchases are now enabled.",
                    ["PurchasesDisabled"] = "Purchases are now disabled.",
                },
                this
            );
            lang.RegisterMessages(
                new Dictionary<string, string>
                {
                    ["AlreadyOwnsDungeonMessage"] = "Você já possui uma masmorra ativa. Não é possível comprar outra antes que ela expire ou seja removida.",
                    ["DungeonAppeared"] = "Uma {0} apareceu na grade {1}!",
                    ["DungeonRemovedDetail"] = "Masmorra de nível {0} removida na grade {1}.",
                    ["DungeonSpawnedSuccess"] = "Masmorra gerada com sucesso na grade {0}!",
                    ["NpcSpawnNotLoaded"] = "O plugin NpcSpawn não está carregado.",
                    ["DungeonStillSpawningMessage"] = "A masmorra ainda está sendo gerada. Por favor, aguarde até que esteja pronta antes de entrar.",
                    ["InsufficientFundsMessage"] = "Você não tem fundos suficientes para comprar o nível {0}. Requerido: {1}.",
                    ["InvalidTierMessage"] = "Nome de nível inválido '{0}'. Níveis disponíveis: {1}.",
                    ["ThisTierDisabled"] = "Esta dificuldade está desativada para compras",
                    ["MaxActiveDungeonsReached"] = "Não é possível gerar: número máximo de masmorras ativas alcançado.",
                    ["NoAccess"] = "Você não tem permissão para fazer isso.",
                    ["PurchaseSuccessMessage"] = "Você comprou com sucesso o nível {0}!",
                    ["ReceivedRequest"] = "Seu pedido foi adicionado à fila. Por favor aguarde.",
                    ["UsageCreateMessage"] = "Uso: /createdungeon <nome_do_nível>",
                    ["CleanedUp"] = "Todos os objetos relacionados a este plugin foram removidos.",
                    ["NoInactiveDungeonsToRemove"] = "Não há masmorras inativas para remover.",
                    ["WithdrawFailedMessage"] = "Falha ao retirar fundos para {0}.",
                    ["DungeonBuyTitle"] = "Comprar Masmorra",
                    ["BuyButton"] = "{0} ({1})",
                    ["BuyButtonWithName"] = "{0} ({1} {2})",
                    ["CloseButton"] = "Fechar",
                    ["DungeonRemovalCountdown"] = "Sua masmorra será removida em {0} minuto(s)!",
                    ["BlockedCommands"] = "Você não pode usar comandos dentro de uma masmorra!",
                    ["EconRewardMessage"] = "Você recebeu {0} RP!",
                    ["ServerRewardMessage"] = "Você recebeu {0} pontos de RN!",
                    ["DungeonPurchased"] = "Uma masmorra {0} comprada por {1} apareceu na grade {2}!",
                    ["DungeonCooldownMessage"] = "Você deve esperar {0} segundo(s) antes de comprar outra masmorra.",
                    ["DungeonClearEconomyReward"] = "Você recebeu {0} RP por limpar a masmorra!",
                    ["DungeonClearServerReward"] = "Você recebeu {0} pontos de RN por limpar a masmorra!",
                    ["Dungeonclosed"] = "Masmorra fechada. Você foi teleportado para a saída.",
                    ["DungeonClearXpReward"] = "Você recebeu {0} XP por limpar a masmorra!",
                    ["NoDungeonsToRemove"] = "Não há masmorras para remover.",
                    ["TooFarToRemove"] = "Você deve estar a até 10 metros de uma masmorra para removê-la.",
                    ["NotDungeonOwner"] = "Você não possui uma masmorra ativa.",
                    ["ConfigReloaded"] = "Configuração recarregada.",
                    ["PurchasesEnabled"] = "Compras agora estão habilitadas.",
                    ["PurchasesDisabled"] = "Compras agora estão desabilitadas.",
                },
                this,
                "pt-BR"
            );
            lang.RegisterMessages(
                new Dictionary<string, string>
                {
                    ["AlreadyOwnsDungeonMessage"] = "Du besitzt bereits ein aktives Verlies. Du kannst kein weiteres kaufen, bevor es abläuft oder entfernt wird.",
                    ["DungeonAppeared"] = "Ein {0} ist bei Gitter {1} erschienen!",
                    ["DungeonRemovedDetail"] = "Verlies der Stufe {0} bei Gitter {1} wurde entfernt.",
                    ["DungeonSpawnedSuccess"] = "Verlies erfolgreich bei Gitter {0} erstellt!",
                    ["NpcSpawnNotLoaded"] = "Das Plugin NpcSpawn ist nicht geladen.",
                    ["DungeonStillSpawningMessage"] = "Das Verlies wird noch erstellt. Bitte warte, bis es bereit ist, bevor du es betrittst.",
                    ["InsufficientFundsMessage"] = "Du hast nicht genug Geld, um die Stufe {0} zu kaufen. Erforderlich: {1}.",
                    ["InvalidTierMessage"] = "Ungültiger Stufenname '{0}'. Verfügbare Stufen: {1}.",
                    ["ThisTierDisabled"] = "Diese Schwierigkeitsstufe ist für Käufe deaktiviert",
                    ["MaxActiveDungeonsReached"] = "Kann nicht erstellt werden: Maximale Anzahl aktiver Verliese erreicht.",
                    ["NoAccess"] = "Du hast keine Berechtigung, dies zu tun.",
                    ["PurchaseSuccessMessage"] = "Du hast die Stufe {0} erfolgreich gekauft!",
                    ["ReceivedRequest"] = "Deine Anfrage wurde der Warteschlange hinzugefügt. Bitte warte.",
                    ["UsageCreateMessage"] = "Verwendung: /createdungeon <stufen_name>",
                    ["CleanedUp"] = "Alle mit diesem Plugin verbundenen Objekte wurden entfernt.",
                    ["NoInactiveDungeonsToRemove"] = "Es gibt keine inaktiven Verliese zum Entfernen.",
                    ["WithdrawFailedMessage"] = "Fehler beim Abheben der Mittel für {0}.",
                    ["DungeonBuyTitle"] = "Verlies kaufen",
                    ["BuyButton"] = "{0} ({1})",
                    ["BuyButtonWithName"] = "{0} ({1} {2})",
                    ["CloseButton"] = "Schließen",
                    ["DungeonRemovalCountdown"] = "Dein Verlies wird in {0} Minute(n) entfernt!",
                    ["BlockedCommands"] = "Du kannst in einem Verlies keine Befehle verwenden!",
                    ["EconRewardMessage"] = "Du hast {0} Münzen erhalten!",
                    ["ServerRewardMessage"] = "Du hast {0} RP erhalten!",
                    ["DungeonPurchased"] = "Ein {0} Verlies, gekauft von {1}, ist bei Gitter {2} erschienen!",
                    ["DungeonCooldownMessage"] = "Du musst {0} Sekunde(n) warten, bevor du ein weiteres Verlies kaufen kannst.",
                    ["DungeonClearEconomyReward"] = "Du hast {0} RP erhalten, weil du das Verlies vollständig geleert hast!",
                    ["DungeonClearServerReward"] = "Du hast {0} Serverpunkte erhalten, weil du das Verlies vollständig geleert hast!",
                    ["Dungeonclosed"] = "Verlies geschlossen. Du wurdest zum Ausgang teleportiert.",
                    ["DungeonClearXpReward"] = "Du hast {0} XP erhalten, weil du das Verlies vollständig geleert hast!",
                    ["NoDungeonsToRemove"] = "Es gibt keine Verliese zum Entfernen.",
                    ["TooFarToRemove"] = "Du musst dich innerhalb von 10 Metern eines Verlieses befinden, um es zu entfernen.",
                    ["NotDungeonOwner"] = "Du besitzt kein aktives Verlies.",
                    ["ConfigReloaded"] = "Konfiguration neu geladen.",
                    ["PurchasesEnabled"] = "Käufe sind jetzt aktiviert.",
                    ["PurchasesDisabled"] = "Käufe sind jetzt deaktiviert.",
                },
                this,
                "de"
            );
            lang.RegisterMessages(
                new Dictionary<string, string>
                {
                    ["AlreadyOwnsDungeonMessage"] = "Ya posees una mazmorra activa. No puedes comprar otra hasta que expire o sea eliminada.",
                    ["DungeonAppeared"] = "¡Una {0} ha aparecido en la cuadrícula {1}!",
                    ["DungeonRemovedDetail"] = "Mazmorra de nivel {0} eliminada en la cuadrícula {1}.",
                    ["DungeonSpawnedSuccess"] = "¡Mazmorra creada exitosamente en la cuadrícula {0}!",
                    ["NpcSpawnNotLoaded"] = "El plugin NpcSpawn no está cargado.",
                    ["DungeonStillSpawningMessage"] = "La mazmorra aún está generándose. Por favor, espera antes de entrar.",
                    ["InsufficientFundsMessage"] = "No tienes suficientes fondos para comprar el nivel {0}. Requerido: {1}.",
                    ["InvalidTierMessage"] = "Nombre de nivel inválido '{0}'. Niveles disponibles: {1}.",
                    ["ThisTierDisabled"] = "Esta dificultad está deshabilitada para compras",
                    ["MaxActiveDungeonsReached"] = "No se puede generar: se alcanzó el número máximo de mazmorras activas.",
                    ["NoAccess"] = "No tienes permiso para hacer esto.",
                    ["PurchaseSuccessMessage"] = "¡Has comprado con éxito el nivel {0}!",
                    ["ReceivedRequest"] = "Tu solicitud se ha añadido a la cola. Por favor, espera.",
                    ["UsageCreateMessage"] = "Uso: /createdungeon <nombre_del_nivel>",
                    ["CleanedUp"] = "Todos los objetos relacionados con este plugin han sido eliminados.",
                    ["NoInactiveDungeonsToRemove"] = "No hay mazmorras inactivas para eliminar.",
                    ["WithdrawFailedMessage"] = "Error al retirar fondos para {0}.",
                    ["DungeonBuyTitle"] = "Comprar Mazmorra",
                    ["BuyButton"] = "{0} ({1})",
                    ["BuyButtonWithName"] = "{0} ({1} {2})",
                    ["CloseButton"] = "Cerrar",
                    ["DungeonRemovalCountdown"] = "¡Tu mazmorra será eliminada en {0} minuto(s)!",
                    ["BlockedCommands"] = "¡No puedes usar comandos dentro de una mazmorra!",
                    ["EconRewardMessage"] = "¡Has recibido {0} RP!",
                    ["ServerRewardMessage"] = "¡Has recibido {0} puntos de RN!",
                    ["DungeonPurchased"] = "¡Una mazmorra {0} comprada por {1} ha aparecido en la cuadrícula {2}!",
                    ["DungeonCooldownMessage"] = "Debes esperar {0} segundo(s) antes de comprar otra mazmorra.",
                    ["DungeonClearEconomyReward"] = "¡Has recibido {0} RP por vaciar completamente la mazmorra!",
                    ["DungeonClearServerReward"] = "¡Has recibido {0} puntos de RN por vaciar completamente la mazmorra!",
                    ["Dungeonclosed"] = "Mazmorra cerrada. Has sido teletransportado a la salida.",
                    ["DungeonClearXpReward"] = "¡Has recibido {0} XP por vaciar completamente la mazmorra!",
                    ["NoDungeonsToRemove"] = "No hay mazmorras para eliminar.",
                    ["TooFarToRemove"] = "Debes estar a menos de 10 metros de una mazmorra para eliminarla.",
                    ["NotDungeonOwner"] = "No posees una mazmorra activa.",
                    ["ConfigReloaded"] = "Configuración recargada.",
                    ["PurchasesEnabled"] = "Las compras ahora están habilitadas.",
                    ["PurchasesDisabled"] = "Las compras ahora están deshabilitadas.",
                },
                this,
                "es"
            );
            lang.RegisterMessages(
                new Dictionary<string, string>
                {
                    ["AlreadyOwnsDungeonMessage"] = "У вас уже есть активное подземелье. Вы не можете приобрести другое, пока оно не истечет или не будет удалено.",
                    ["DungeonAppeared"] = "Подземелье {0} появилось в сетке {1}!",
                    ["DungeonRemovedDetail"] = "Удалено подземелье уровня {0} в сетке {1}.",
                    ["DungeonSpawnedSuccess"] = "Подземелье успешно создано в сетке {0}!",
                    ["NpcSpawnNotLoaded"] = "Плагин NpcSpawn не загружен.",
                    ["DungeonStillSpawningMessage"] = "Подземелье еще загружается. Пожалуйста, подождите, прежде чем войти.",
                    ["InsufficientFundsMessage"] = "У вас недостаточно средств для покупки уровня {0}. Нужно: {1}.",
                    ["InvalidTierMessage"] = "Неверное название уровня '{0}'. Доступные уровни: {1}.",
                    ["ThisTierDisabled"] = "Этот уровень сложности отключен для покупок",
                    ["MaxActiveDungeonsReached"] = "Невозможно создать: достигнуто максимальное количество активных подземелий.",
                    ["NoAccess"] = "У вас нет разрешения на это действие.",
                    ["PurchaseSuccessMessage"] = "Вы успешно приобрели уровень {0}!",
                    ["ReceivedRequest"] = "Ваш запрос был добавлен в очередь. Пожалуйста, подождите.",
                    ["UsageCreateMessage"] = "Использование: /createdungeon <название_уровня>",
                    ["CleanedUp"] = "Все объекты, связанные с этим плагином, были удалены.",
                    ["NoInactiveDungeonsToRemove"] = "Нет неактивных подземелий для удаления.",
                    ["WithdrawFailedMessage"] = "Ошибка при попытке вывести средства для {0}.",
                    ["DungeonBuyTitle"] = "Купить Подземелье",
                    ["BuyButton"] = "{0} ({1})",
                    ["BuyButtonWithName"] = "{0} ({1} {2})",
                    ["CloseButton"] = "Закрыть",
                    ["DungeonRemovalCountdown"] = "Ваше подземелье будет удалено через {0} минут(ы)!",
                    ["BlockedCommands"] = "Вы не можете использовать команды внутри подземелья!",
                    ["EconRewardMessage"] = "Вы получили {0} RP!",
                    ["ServerRewardMessage"] = "Вы получили {0} RN!",
                    ["DungeonPurchased"] = "Подземелье {0}, купленное {1}, появилось на сетке {2}!",
                    ["DungeonCooldownMessage"] = "Вы должны подождать {0} секунд(ы), прежде чем купить другое подземелье.",
                    ["DungeonClearEconomyReward"] = "Вы получили {0} RP за полное опустошение подземелья!",
                    ["DungeonClearServerReward"] = "Вы получили {0} RN за полное опустошение подземелья!",
                    ["Dungeonclosed"] = "Подземелье закрыто. Вы были телепортированы к выходу.",
                    ["DungeonClearXpReward"] = "Вы получили {0} XP за полное опустошение подземелья!",
                    ["NoDungeonsToRemove"] = "Нет подземелий для удаления.",
                    ["TooFarToRemove"] = "Вы должны находиться в пределах 10 метров от подземелья, чтобы удалить его.",
                    ["NotDungeonOwner"] = "Вы не являетесь владельцем активного подземелья.",
                    ["ConfigReloaded"] = "Конфигурация перезагружена.",
                    ["PurchasesEnabled"] = "Покупки теперь включены.",
                    ["PurchasesDisabled"] = "Покупки теперь отключены.",
                },
                this,
                "ru"
            );
        }
        #endregion

        #region Configuration
        private class Configuration
        {
            [JsonProperty("Enable Purchases")]
            public bool EnablePurchases { get; set; } = true;

            [JsonProperty("Use Notify Plugin")]
            public bool UseNotify { get; set; } = false;

            [JsonProperty("Enable Debug Logs")]
            public bool EnableDebugLogs { get; set; } = false;

            [JsonProperty("Enable Warning Logs")]
            public bool EnableWarningLogs { get; set; } = true;

            [JsonProperty("Enable Toast Messages")]
            public bool EnableShowToast { get; set; } = true;

            [JsonProperty("Enable Dungeon Lights")]
            public bool EnableDungeonLights { get; set; } = true;

            [JsonProperty("Dungeon Light Prefab Path")]
            public string DungeonLightPrefabPath { get; set; } = "assets/bundled/prefabs/modding/cinematic/cinelights/cinelight_point_red.prefab";

            [JsonProperty("Seconds Until NPCs Reset After Dungeon Becomes Empty")]
            public float EmptyDungeonNpcResetSeconds { get; set; } = 60f;

            [JsonProperty("Dungeon Light Spacing")]
            public float DungeonLightSpacing { get; set; } = 6f;

            [JsonProperty("Prevent Drop Backpack")]
            public bool PreventDropBackpack { get; set; } = true;

            [JsonProperty("Teleport Player To Outside On Death")]
            public bool TeleportPlayerOnDeath { get; set; } = true;

            [JsonProperty("Close Dungeon Doors On Player Death")]
            public bool CloseDoorsOnDeath { get; set; } = true;

            [JsonProperty("Spawn Point Configuration")]
            public SpawnPointConfiguration SpawnPointConfiguration { get; set; } = new SpawnPointConfiguration();

            [JsonProperty("Place Sphere on Dungeon Portal")]
            public bool EnablePlaceSphere { get; set; } = true;

            [JsonProperty("Radius Contruction Block on Dungeon Portal")]
            public int RadiusSphere { get; set; } = 20;

            [JsonProperty("Custom AutoTurret Behavior (Use in case AutoTurrets aren't shooting)")]
            public bool CustomAutoTurretBehavior { get; set; } = false;

            [JsonProperty("NightVision Plugin Time On Leaving Dungeon(-1 for no override)")]
            public int NightVisionTime { get; set; } = 12;

            [JsonProperty("Lock dungeon for first player")]
            public bool LockDungeonToPlayer { get; set; } = true;

            [JsonProperty("Economy Plugin (1 - Economics, 2 - ServerRewards, 3 - Custom)")]
            public int EconomyPlugin { get; set; } = 3;

            [JsonProperty("Custom Economy Item Name")]
            public string CustomEconomyItemName { get; set; } = "scrap";

            [JsonProperty("Custom Economy Display Name")]
            public string CustomEconomyDisplayName { get; set; } = "Scrap";

            [JsonProperty("Economics Display Name")]
            public string EconomicsDisplayName { get; set; } = "RP";

            [JsonProperty("ServerRewards Display Name")]
            public string ServerRewardsDisplayName { get; set; } = "RN";

            [JsonProperty("Enable SkillTree XP Team Sharing")]
            public bool EnableSkillTreeXpTeamSharing { get; set; } = true;

            [JsonProperty("SkillTree XP Share Mode (0 = Split total between players, 1 = Give full amount to each player)")]
            public int SkillTreeXpShareMode { get; set; } = 1;

            public string GetEconomyItemName()
            {
                return EconomyPlugin switch
                {
                    1 => "economics",
                    2 => "serverrewards",
                    _ => CustomEconomyItemName,
                };
            }

            public string GetEconomyDisplayName()
            {
                return EconomyPlugin switch
                {
                    1 => EconomicsDisplayName,
                    2 => ServerRewardsDisplayName,
                    _ => CustomEconomyDisplayName,
                };
            }

            [JsonProperty("Allow team members")]
            public bool AllowTeam { get; set; } = true;

            [JsonProperty("Allow friends")]
            public bool AllowFriends { get; set; } = true;

            [JsonProperty("Small Delay (seconds) – increase if FPS drops")]
            public float SmallDelay { get; set; } = 0.15f;

            [JsonProperty("Big Delay (seconds) – increase if FPS drops")]
            public float BigDelay { get; set; } = 0.50f;

            [JsonProperty("Dungeon Settings")]
            public DungeonSpawnSettings DungeonSpawn { get; set; } = new DungeonSpawnSettings();

            [JsonProperty("Blocked commands inside a dungeon", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public HashSet<string> BlockCommands { get; set; } = new HashSet<string> { "/remove", "/tpa", "remove.toggle" };

            [JsonProperty("Tiers", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<DungeonTierConfig> Tiers { get; set; } =
                new List<DungeonTierConfig>
                {
                    new DungeonTierConfig
                    {
                        TierName = "Easy",
                        MapName = "Dungeon: Easy",
                        MarkerColorHex = "#3cd834",
                        BuildingGradeSelection = 3,
                        ContainerColor = 0,
                        DungeonRooms = 1,
                        DungeonRoomSize = 5,
                        BuyCost = 300,
                        EntitySpawnLimits = new EntitySpawnLimits
                        {
                            NpcsCounts = 9,
                            BossesCounts = 1,
                            TurretsCounts = 5,
                            LootBoxesCounts = 5,
                        },
                        AutoTurretConfig = new TurretConfig { Health = 300, WeaponShortName = "pistol.revolver" },
                        LootBoxSkinID = 2998755525,
                        MaxItemsPerBox = 3,
                        MaxItemsPerNpc = 3,
                        MaxItemsPerBossNpc = 3,
                        NpcConfig = new NpcConfig
                        {
                            Name = "Wild Scavenger",
                            Health = 100,
                            DamageScale = 0.5f,
                            WearItems = new WearConfig
                            {
                                Items = new List<WearItemConfig>
                                {
                                    new WearItemConfig { ShortName = "tshirt.long", SkinID = 10118 },
                                    new WearItemConfig { ShortName = "burlap.gloves", SkinID = 949616124 },
                                    new WearItemConfig { ShortName = "attire.snowman.helmet", SkinID = 0 },
                                    new WearItemConfig { ShortName = "pants", SkinID = 2352962213 },
                                    new WearItemConfig { ShortName = "shoes.boots", SkinID = 2380731293 },
                                },
                            },
                            BeltItems = new BeltConfig
                            {
                                Items = new List<BeltItemConfig>
                                {
                                    new BeltItemConfig
                                    {
                                        ShortName = "pistol.revolver",
                                        Amount = 1,
                                        SkinID = 3140577175,
                                    },
                                },
                            },
                        },
                        BossNpcConfig = new NpcConfig
                        {
                            Name = "Easy Dungeon Boss",
                            Health = 1000,
                            DamageScale = 1.5f,
                            WearItems = new WearConfig
                            {
                                Items = new List<WearItemConfig>
                                {
                                    new WearItemConfig { ShortName = "riot.helmet", SkinID = 1087995729 },
                                    new WearItemConfig { ShortName = "hoodie", SkinID = 1087990973 },
                                    new WearItemConfig { ShortName = "burlap.gloves", SkinID = 1087998101 },
                                    new WearItemConfig { ShortName = "pants", SkinID = 1087992342 },
                                    new WearItemConfig { ShortName = "shoes.boots", SkinID = 1088000573 },
                                },
                            },
                            BeltItems = new BeltConfig
                            {
                                Items = new List<BeltItemConfig>
                                {
                                    new BeltItemConfig
                                    {
                                        ShortName = "rifle.ak",
                                        Amount = 1,
                                        SkinID = 3368362976,
                                        Mods = new List<string> { "weapon.mod.flashlight" },
                                    },
                                },
                            },
                        },
                    },
                    new DungeonTierConfig
                    {
                        TierName = "Normal",
                        MapName = "Dungeon: Normal",
                        MarkerColorHex = "#e7c50a",
                        BuildingGradeSelection = 2,
                        ContainerColor = 0,
                        DungeonRooms = 2,
                        DungeonRoomSize = 5,
                        BuyCost = 500,
                        EntitySpawnLimits = new EntitySpawnLimits
                        {
                            NpcsCounts = 18,
                            BossesCounts = 2,
                            TurretsCounts = 10,
                            LootBoxesCounts = 10,
                        },
                        AutoTurretConfig = new TurretConfig { Health = 400, WeaponShortName = "pistol.revolver" },
                        LootBoxSkinID = 2998755525,
                        MaxItemsPerBox = 4,
                        MaxItemsPerNpc = 4,
                        MaxItemsPerBossNpc = 4,
                        NpcConfig = new NpcConfig
                        {
                            Name = "Battle Forager",
                            Health = 150,
                            DamageScale = 0.8f,
                            WearItems = new WearConfig
                            {
                                Items = new List<WearItemConfig>
                                {
                                    new WearItemConfig { ShortName = "bucket.helmet", SkinID = 1073904216 },
                                    new WearItemConfig { ShortName = "jacket", SkinID = 2350426469 },
                                    new WearItemConfig { ShortName = "tshirt", SkinID = 10039 },
                                    new WearItemConfig { ShortName = "burlap.gloves.new", SkinID = 0 },
                                    new WearItemConfig { ShortName = "pants", SkinID = 1441311938 },
                                    new WearItemConfig { ShortName = "shoes.boots", SkinID = 2075527039 },
                                },
                            },
                            BeltItems = new BeltConfig
                            {
                                Items = new List<BeltItemConfig>
                                {
                                    new BeltItemConfig
                                    {
                                        ShortName = "smg.2",
                                        Amount = 1,
                                        SkinID = 2386688842,
                                        Mods = new List<string> { "weapon.mod.flashlight" },
                                    },
                                },
                            },
                        },
                        BossNpcConfig = new NpcConfig
                        {
                            Name = "Normal Dungeon Boss",
                            Health = 1500,
                            DamageScale = 1.5f,
                            WearItems = new WearConfig
                            {
                                Items = new List<WearItemConfig>
                                {
                                    new WearItemConfig { ShortName = "riot.helmet", SkinID = 1087995729 },
                                    new WearItemConfig { ShortName = "hoodie", SkinID = 1087990973 },
                                    new WearItemConfig { ShortName = "burlap.gloves", SkinID = 1087998101 },
                                    new WearItemConfig { ShortName = "pants", SkinID = 1087992342 },
                                    new WearItemConfig { ShortName = "shoes.boots", SkinID = 1088000573 },
                                },
                            },
                            BeltItems = new BeltConfig
                            {
                                Items = new List<BeltItemConfig>
                                {
                                    new BeltItemConfig
                                    {
                                        ShortName = "lmg.m249",
                                        Amount = 1,
                                        SkinID = 1883947256,
                                        Mods = new List<string> { "weapon.mod.flashlight" },
                                    },
                                },
                            },
                        },
                    },
                    new DungeonTierConfig
                    {
                        TierName = "Medium",
                        MapName = "Dungeon: Medium",
                        MarkerColorHex = "#e57109",
                        BuildingGradeSelection = 6,
                        ContainerColor = 0,
                        DungeonRooms = 3,
                        DungeonRoomSize = 5,
                        BuyCost = 700,
                        EntitySpawnLimits = new EntitySpawnLimits
                        {
                            NpcsCounts = 25,
                            BossesCounts = 5,
                            TurretsCounts = 15,
                            LootBoxesCounts = 15,
                        },
                        AutoTurretConfig = new TurretConfig { Health = 600, WeaponShortName = "rifle.ak" },
                        LootBoxSkinID = 2998755525,
                        MaxItemsPerBox = 5,
                        MaxItemsPerNpc = 5,
                        MaxItemsPerBossNpc = 5,
                        NpcConfig = new NpcConfig
                        {
                            Name = "Crimson Raider",
                            Health = 200,
                            DamageScale = 1.1f,
                            WearItems = new WearConfig
                            {
                                Items = new List<WearItemConfig>
                                {
                                    new WearItemConfig { ShortName = "coffeecan.helmet", SkinID = 1727561127 },
                                    new WearItemConfig { ShortName = "roadsign.jacket", SkinID = 1727562915 },
                                    new WearItemConfig { ShortName = "roadsign.gloves", SkinID = 2799639349 },
                                    new WearItemConfig { ShortName = "pants", SkinID = 2814837980 },
                                    new WearItemConfig { ShortName = "shoes.boots", SkinID = 1839313604 },
                                    new WearItemConfig { ShortName = "tshirt.long", SkinID = 566893368 },
                                },
                            },
                            BeltItems = new BeltConfig
                            {
                                Items = new List<BeltItemConfig>
                                {
                                    new BeltItemConfig
                                    {
                                        ShortName = "rifle.semiauto",
                                        Amount = 1,
                                        SkinID = 2617680693,
                                        Mods = new List<string> { "weapon.mod.flashlight" },
                                    },
                                    new BeltItemConfig
                                    {
                                        ShortName = "syringe.medical",
                                        Amount = 10,
                                        SkinID = 0,
                                    },
                                },
                            },
                        },
                        BossNpcConfig = new NpcConfig
                        {
                            Name = "Medium Dungeon Boss",
                            Health = 2000,
                            DamageScale = 1.5f,
                            WearItems = new WearConfig
                            {
                                Items = new List<WearItemConfig>
                                {
                                    new WearItemConfig { ShortName = "riot.helmet", SkinID = 1087995729 },
                                    new WearItemConfig { ShortName = "hoodie", SkinID = 1087990973 },
                                    new WearItemConfig { ShortName = "burlap.gloves", SkinID = 1087998101 },
                                    new WearItemConfig { ShortName = "pants", SkinID = 1087992342 },
                                    new WearItemConfig { ShortName = "shoes.boots", SkinID = 1088000573 },
                                },
                            },
                            BeltItems = new BeltConfig
                            {
                                Items = new List<BeltItemConfig>
                                {
                                    new BeltItemConfig
                                    {
                                        ShortName = "lmg.m249",
                                        Amount = 1,
                                        SkinID = 1883947256,
                                        Mods = new List<string> { "weapon.mod.flashlight" },
                                    },
                                },
                            },
                        },
                    },
                    new DungeonTierConfig
                    {
                        TierName = "Hard",
                        MapName = "Dungeon: Hard",
                        MarkerColorHex = "#9c1825",
                        BuildingGradeSelection = 8,
                        ContainerColor = 5,
                        DungeonRooms = 4,
                        DungeonRoomSize = 5,
                        BuyCost = 1000,
                        EntitySpawnLimits = new EntitySpawnLimits
                        {
                            NpcsCounts = 30,
                            BossesCounts = 5,
                            TurretsCounts = 20,
                            LootBoxesCounts = 25,
                        },
                        AutoTurretConfig = new TurretConfig { Health = 800, WeaponShortName = "rifle.ak" },
                        LootBoxSkinID = 2998755525,
                        MaxItemsPerBox = 6,
                        MaxItemsPerNpc = 6,
                        MaxItemsPerBossNpc = 6,
                        NpcConfig = new NpcConfig
                        {
                            Name = "Elite Outlaw",
                            Health = 250,
                            DamageScale = 1.3f,
                            WearItems = new WearConfig
                            {
                                Items = new List<WearItemConfig>
                                {
                                    new WearItemConfig { ShortName = "metal.facemask", SkinID = 3284864766 },
                                    new WearItemConfig { ShortName = "metal.plate.torso", SkinID = 2105505757 },
                                    new WearItemConfig { ShortName = "burlap.gloves", SkinID = 2090790324 },
                                    new WearItemConfig { ShortName = "hoodie", SkinID = 2080975449 },
                                    new WearItemConfig { ShortName = "shoes.boots", SkinID = 10023 },
                                    new WearItemConfig { ShortName = "pants", SkinID = 2080977144 },
                                    new WearItemConfig { ShortName = "roadsign.kilt", SkinID = 2120628865 },
                                },
                            },
                            BeltItems = new BeltConfig
                            {
                                Items = new List<BeltItemConfig>
                                {
                                    new BeltItemConfig
                                    {
                                        ShortName = "rifle.ak",
                                        Amount = 1,
                                        SkinID = 3190379864,
                                        Mods = new List<string> { "weapon.mod.flashlight" },
                                    },
                                    new BeltItemConfig
                                    {
                                        ShortName = "syringe.medical",
                                        Amount = 20,
                                        SkinID = 0,
                                    },
                                },
                            },
                        },
                        BossNpcConfig = new NpcConfig
                        {
                            Name = "Hard Dungeon Boss",
                            Health = 3000,
                            DamageScale = 1.5f,
                            WearItems = new WearConfig
                            {
                                Items = new List<WearItemConfig>
                                {
                                    new WearItemConfig { ShortName = "riot.helmet", SkinID = 1087995729 },
                                    new WearItemConfig { ShortName = "hoodie", SkinID = 1087990973 },
                                    new WearItemConfig { ShortName = "burlap.gloves", SkinID = 1087998101 },
                                    new WearItemConfig { ShortName = "pants", SkinID = 1087992342 },
                                    new WearItemConfig { ShortName = "shoes.boots", SkinID = 1088000573 },
                                },
                            },
                            BeltItems = new BeltConfig
                            {
                                Items = new List<BeltItemConfig>
                                {
                                    new BeltItemConfig
                                    {
                                        ShortName = "lmg.m249",
                                        Amount = 1,
                                        SkinID = 1883947256,
                                        Mods = new List<string> { "weapon.mod.flashlight" },
                                    },
                                },
                            },
                        },
                    },
                    new DungeonTierConfig
                    {
                        TierName = "Nightmare",
                        MapName = "Dungeon: Nightmare",
                        MarkerColorHex = "#9236f5",
                        BuildingGradeSelection = 10,
                        ContainerColor = 0,
                        DungeonRooms = 5,
                        DungeonRoomSize = 5,
                        BuyCost = 1500,
                        EntitySpawnLimits = new EntitySpawnLimits
                        {
                            NpcsCounts = 35,
                            BossesCounts = 10,
                            TurretsCounts = 25,
                            LootBoxesCounts = 30,
                        },
                        AutoTurretConfig = new TurretConfig { Health = 1000, WeaponShortName = "rifle.ak" },
                        LootBoxSkinID = 2998755525,
                        MaxItemsPerBox = 7,
                        MaxItemsPerNpc = 7,
                        MaxItemsPerBossNpc = 7,
                        NpcConfig = new NpcConfig
                        {
                            Name = "Nightmare Hunter",
                            Health = 300,
                            DamageScale = 1.5f,
                            WearItems = new WearConfig
                            {
                                Items = new List<WearItemConfig>
                                {
                                    new WearItemConfig { ShortName = "metal.facemask", SkinID = 3343860599 },
                                    new WearItemConfig { ShortName = "metal.plate.torso", SkinID = 3343861569 },
                                    new WearItemConfig { ShortName = "hoodie", SkinID = 1369835131 },
                                    new WearItemConfig { ShortName = "roadsign.kilt", SkinID = 1727564168 },
                                    new WearItemConfig { ShortName = "burlap.gloves", SkinID = 1210780157 },
                                    new WearItemConfig { ShortName = "pants", SkinID = 810745264 },
                                    new WearItemConfig { ShortName = "shoes.boots", SkinID = 1210771348 },
                                },
                            },
                            BeltItems = new BeltConfig
                            {
                                Items = new List<BeltItemConfig>
                                {
                                    new BeltItemConfig
                                    {
                                        ShortName = "lmg.m249",
                                        Amount = 1,
                                        SkinID = 1883947256,
                                        Mods = new List<string> { "weapon.mod.flashlight" },
                                    },
                                    new BeltItemConfig
                                    {
                                        ShortName = "syringe.medical",
                                        Amount = 50,
                                        SkinID = 0,
                                    },
                                },
                            },
                        },
                        BossNpcConfig = new NpcConfig
                        {
                            Name = "Nightmare Dungeon Boss",
                            Health = 5000,
                            DamageScale = 1.5f,
                            WearItems = new WearConfig
                            {
                                Items = new List<WearItemConfig>
                                {
                                    new WearItemConfig { ShortName = "riot.helmet", SkinID = 1087995729 },
                                    new WearItemConfig { ShortName = "hoodie", SkinID = 1087990973 },
                                    new WearItemConfig { ShortName = "burlap.gloves", SkinID = 1087998101 },
                                    new WearItemConfig { ShortName = "pants", SkinID = 1087992342 },
                                    new WearItemConfig { ShortName = "shoes.boots", SkinID = 1088000573 },
                                },
                            },
                            BeltItems = new BeltConfig
                            {
                                Items = new List<BeltItemConfig>
                                {
                                    new BeltItemConfig
                                    {
                                        ShortName = "lmg.m249",
                                        Amount = 1,
                                        SkinID = 1883947256,
                                        Mods = new List<string> { "weapon.mod.flashlight" },
                                    },
                                },
                            },
                        },
                    },
                    new DungeonTierConfig
                    {
                        TierName = "Impossible",
                        MapName = "Dungeon: Impossible",
                        MarkerColorHex = "#1a1412",
                        BuildingGradeSelection = 10,
                        ContainerColor = 4,
                        DungeonRooms = 6,
                        DungeonRoomSize = 6,
                        BuyCost = 2000,
                        EntitySpawnLimits = new EntitySpawnLimits
                        {
                            NpcsCounts = 5,
                            BossesCounts = 30,
                            TurretsCounts = 20,
                            LootBoxesCounts = 25,
                        },
                        AutoTurretConfig = new TurretConfig { Health = 1500, WeaponShortName = "rifle.ak" },
                        LootBoxSkinID = 2998755525,
                        MaxItemsPerBox = 8,
                        MaxItemsPerNpc = 8,
                        MaxItemsPerBossNpc = 8,
                        NpcConfig = new NpcConfig
                        {
                            Name = "Impossible Hunter",
                            Health = 300,
                            DamageScale = 1.5f,
                            WearItems = new WearConfig
                            {
                                Items = new List<WearItemConfig>
                                {
                                    new WearItemConfig { ShortName = "metal.facemask", SkinID = 3343860599 },
                                    new WearItemConfig { ShortName = "metal.plate.torso", SkinID = 3343861569 },
                                    new WearItemConfig { ShortName = "hoodie", SkinID = 1369835131 },
                                    new WearItemConfig { ShortName = "roadsign.kilt", SkinID = 1727564168 },
                                    new WearItemConfig { ShortName = "burlap.gloves", SkinID = 1210780157 },
                                    new WearItemConfig { ShortName = "pants", SkinID = 810745264 },
                                    new WearItemConfig { ShortName = "shoes.boots", SkinID = 1210771348 },
                                },
                            },
                            BeltItems = new BeltConfig
                            {
                                Items = new List<BeltItemConfig>
                                {
                                    new BeltItemConfig
                                    {
                                        ShortName = "lmg.m249",
                                        Amount = 1,
                                        SkinID = 1883947256,
                                        Mods = new List<string> { "weapon.mod.flashlight" },
                                    },
                                    new BeltItemConfig
                                    {
                                        ShortName = "syringe.medical",
                                        Amount = 50,
                                        SkinID = 0,
                                    },
                                },
                            },
                        },
                        BossNpcConfig = new NpcConfig
                        {
                            Name = "Impossible Dungeon Boss",
                            Health = 5000,
                            DamageScale = 1.5f,
                            WearItems = new WearConfig
                            {
                                Items = new List<WearItemConfig>
                                {
                                    new WearItemConfig { ShortName = "riot.helmet", SkinID = 871867116 },
                                    new WearItemConfig { ShortName = "hoodie", SkinID = 3308083017 },
                                    new WearItemConfig { ShortName = "pants", SkinID = 3269772521 },
                                    new WearItemConfig { ShortName = "shoes.boots", SkinID = 3332536449 },
                                },
                            },
                            BeltItems = new BeltConfig
                            {
                                Items = new List<BeltItemConfig>
                                {
                                    new BeltItemConfig
                                    {
                                        ShortName = "lmg.m249",
                                        Amount = 1,
                                        SkinID = 1883947256,
                                        Mods = new List<string> { "weapon.mod.flashlight" },
                                    },
                                },
                            },
                        },
                    },
                };

            [JsonProperty("Chat Commands")]
            public ChatCommandConfig ChatCommands { get; set; } = new ChatCommandConfig();

            [JsonProperty("Console Commands")]
            public ConsoleCommandConfig ConsoleCommands { get; set; } = new ConsoleCommandConfig();

            [JsonProperty("Version")]
            public VersionNumber Version { get; set; }
        }

        public class ChatCommandConfig
        {
            [JsonProperty("Buy Dungeon")]
            public string BuyDungeon { get; set; } = "buydungeon";

            [JsonProperty("Create Dungeon")]
            public string CreateDungeon { get; set; } = "createdungeon";

            [JsonProperty("Reload Config")]
            public string ReloadConfig { get; set; } = "de.reloadconfig";

            [JsonProperty("Toggle Purchases")]
            public string TogglePurchases { get; set; } = "de.toggle";

            [JsonProperty("Remove Inactive Dungeons")]
            public string RemoveInactiveDungeons { get; set; } = "removeinactivedungeons";

            [JsonProperty("Remove All Dungeons")]
            public string RemoveAllDungeons { get; set; } = "removealldungeons";

            [JsonProperty("Remove Nearest Dungeon")]
            public string RemoveNearest { get; set; } = "de.removenearest";

            [JsonProperty("Remove Own Dungeon")]
            public string RemoveOwnDungeon { get; set; } = "removedungeon";

            [JsonProperty("Force Remove All Dungeons")]
            public string ForceRemoveAll { get; set; } = "forceremovealldungeons";
        }

        public class ConsoleCommandConfig
        {
            [JsonProperty("Buy Dungeon")]
            public string BuyDungeon { get; set; } = "buydungeon";

            [JsonProperty("Spawn Random Dungeon")]
            public string SpawnRandomDungeon { get; set; } = "spawnrandomdungeon";

            [JsonProperty("Spawn Fixed Dungeon")]
            public string SpawnFixedDungeon { get; set; } = "spawnfixeddungeon";
        }

        public class RewardConfig
        {
            [JsonProperty("NPC Economics Reward")]
            public int NpcEconomicsReward { get; set; } = 300;

            [JsonProperty("Turret Economics Reward")]
            public int TurretEconomicsReward { get; set; } = 200;

            [JsonProperty("Box Economics Reward")]
            public int BoxEconomicsReward { get; set; } = 100;

            [JsonProperty("NPC ServerRewards Points")]
            public int NpcServerRewards { get; set; } = 300;

            [JsonProperty("Turret ServerRewards Points")]
            public int TurretServerRewards { get; set; } = 200;

            [JsonProperty("Box ServerRewards Points")]
            public int BoxServerRewards { get; set; } = 100;

            [JsonProperty("Dungeon Clear Economics Reward")]
            public int DungeonClearEconomicsReward { get; set; } = 500;

            [JsonProperty("Dungeon Clear ServerRewards Points")]
            public int DungeonClearServerRewards { get; set; } = 500;

            [JsonProperty("Dungeon Clear Skill Tree XP Reward")]
            public double DungeonClearXpReward { get; set; } = 500.0;
        }

        public class DungeonTierConfig
        {
            [JsonProperty("Tier Name")]
            public string TierName { get; set; } = "Tier";

            [JsonProperty("Map Name")]
            public string MapName { get; set; }

            [JsonProperty("Marker Color Hex")]
            public string MarkerColorHex { get; set; } = "#FF8800";

            [JsonProperty("Enable Auto Spawn")]
            public bool EnableAutoSpawn { get; set; } = true;

            [JsonProperty("Enable Buy")]
            public bool EnableBuy { get; set; } = true;

            [JsonProperty("Max Auto Spawn (0 to Unlimited)")]
            public int MaxActiveAutoSpawn { get; set; } = 2;

            [JsonProperty("Max Buy (0 to Unlimited)")]
            public int MaxActiveBuy { get; set; } = 2;

            [JsonProperty("Building Grade - 1(Wood) 2(Legacy Wood) 3(Gingerbread) 4(Stone) 5(Adobe) 6(Brick) 7(Brutalist) 8(Metal) 9(Container) 10(TopTier)")]
            public int BuildingGradeSelection { get; set; } = 9;

            [JsonProperty(
                "Container Color - 1(Light Blue) 2(Light Green) 3(Purple) 4(Dark Red) 5(Orange) 6(White) 7(Black) 8(Brown) 9(Dark Blue) 10(Dark Green) 11(Red) 12(Light Orange) 13(Yellow) 14(Dark Gray) 15(Cyan) 16(Light Gray)"
            )]
            public int ContainerColor { get; set; } = 2;

            [JsonProperty("Garage Door Skin ID")]
            public ulong DoorSkinID { get; set; } = 2427605919;

            [JsonProperty("Dungeon Time of Day Override(-1 for no override)")]
            public int TriggerTime { get; set; } = 0;

            [JsonProperty("Dungeon Rooms (1 - 6)")]
            public int DungeonRooms { get; set; } = 2;

            [JsonProperty("Dungeon Room Size (5 - 10)")]
            public int DungeonRoomSize { get; set; } = 5;

            [JsonProperty("Buy Cost")]
            public int BuyCost { get; set; }

            [JsonProperty("Per-Tier Buy Cooldown (seconds, 0 = use global)")]
            public int BuyCooldown { get; set; } = 0;

            [JsonProperty("Max Unentered Lifetime (in seconds)")]
            public int MaxUnenteredLifetimeSeconds { get; set; } = 1800;

            [JsonProperty("Max Time (in seconds) to Keep Dungeon Alive")]
            public int MaxDungeonLifetime { get; set; } = 1800;

            [JsonProperty("Entity Spawn Limits")]
            public EntitySpawnLimits EntitySpawnLimits { get; set; } = new EntitySpawnLimits();

            [JsonProperty("NPC Config")]
            public NpcConfig NpcConfig { get; set; } = new NpcConfig();

            [JsonProperty("Boss NPC Config")]
            public NpcConfig BossNpcConfig { get; set; } = new NpcConfig();

            [JsonProperty("Loot Box Prefab Path")]
            public string LootBoxPrefabPath { get; set; } = "assets/prefabs/deployable/woodenbox/woodbox_deployed.prefab";

            [JsonProperty("Loot Box Skin ID")]
            public ulong LootBoxSkinID { get; set; } = 0UL;

            [JsonProperty("Max Items Per Box")]
            public int MaxItemsPerBox { get; set; } = 3;

            [JsonProperty("Max Items Per NPC")]
            public int MaxItemsPerNpc { get; set; } = 3;

            [JsonProperty("Max Items Per Boss NPC")]
            public int MaxItemsPerBossNpc { get; set; } = 6;

            [JsonProperty("Auto Turret Config")]
            public TurretConfig AutoTurretConfig { get; set; } = new TurretConfig();

            [JsonProperty("Reward Config")]
            public RewardConfig RewardConfig { get; set; } = new RewardConfig();

            [JsonIgnore]
            public string LootBoxDataFile { get; set; } = string.Empty;

            [JsonIgnore]
            public List<ItemConfig> LootBoxItems { get; set; } = new List<ItemConfig>();
        }

        public class EntitySpawnLimits
        {
            [JsonProperty("NPC Count")]
            public int NpcsCounts { get; set; }

            [JsonProperty("Boss Count")]
            public int BossesCounts { get; set; }

            [JsonProperty("Turret Count")]
            public int TurretsCounts { get; set; }

            [JsonProperty("Loot Box Count")]
            public int LootBoxesCounts { get; set; }
        }

        public class TurretConfig
        {
            [JsonProperty("Health")]
            public float Health { get; set; }

            [JsonProperty("Weapon Short Name")]
            public string WeaponShortName { get; set; }
        }

        public class NpcConfig
        {
            [JsonProperty("NPC Name")]
            public string Name { get; set; } = "Default NPC";

            [JsonProperty("NPC Health")]
            public int Health { get; set; } = 100;

            [JsonProperty("Damage Scale")]
            public float DamageScale { get; set; } = 1.0f;

            [JsonProperty("Wear Items")]
            public WearConfig WearItems { get; set; } = new WearConfig();

            [JsonProperty("Belt Items")]
            public BeltConfig BeltItems { get; set; } = new BeltConfig();

            [JsonIgnore]
            public string LootDropDataFile { get; set; } = string.Empty;

            [JsonIgnore]
            public LootDropConfig LootDropConfig { get; set; } = new LootDropConfig();
        }

        public class WearConfig
        {
            [JsonProperty("Items", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<WearItemConfig> Items { get; set; } = new List<WearItemConfig>();
        }

        public class BeltConfig
        {
            [JsonProperty("Items", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<BeltItemConfig> Items { get; set; } = new List<BeltItemConfig>();
        }

        public class WearItemConfig
        {
            [JsonProperty("ShortName")]
            public string ShortName { get; set; }

            [JsonProperty("SkinID")]
            public ulong SkinID { get; set; }
        }

        public class BeltItemConfig
        {
            [JsonProperty("ShortName")]
            public string ShortName { get; set; }

            [JsonProperty("Amount")]
            public int Amount { get; set; }

            [JsonProperty("SkinID")]
            public ulong SkinID { get; set; }

            [JsonProperty("Mods")]
            public List<string> Mods { get; set; } = new List<string>();
        }

        public class LootDropConfig
        {
            [JsonProperty("Items", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ItemConfig> Items { get; set; } = new List<ItemConfig>();
        }

        public class ItemConfig
        {
            [JsonProperty("ShortName")]
            public string ShortName { get; set; }

            [JsonProperty("Inclusion Chance Percentage")]
            public float InclusionChancePercentage { get; set; }

            [JsonProperty("Minimum Amount")]
            public int MinimumAmount { get; set; }

            [JsonProperty("Maximum Amount")]
            public int MaximumAmount { get; set; }

            [JsonProperty("CustomName")]
            public string CustomName { get; set; } = string.Empty;

            [JsonProperty("SkinID")]
            public ulong SkinID { get; set; }
        }

        public class SpawnPointConfiguration
        {
            [JsonProperty("Avoid Terrain Topology")]
            public bool AvoidTerrainTopology { get; set; } = true;

            [JsonProperty("Enable Physics Overlap Check")]
            public bool CheckSphereRadius { get; set; } = true;

            [JsonProperty("Enable Entity Proximity Check")]
            public bool CheckEntities { get; set; } = true;

            [JsonProperty("Avoid ZoneManager Zones")]
            public bool CheckZoneManager { get; set; } = true;

            [JsonProperty("Avoid Duelist Territory")]
            public bool CheckDuelist { get; set; } = true;

            [JsonProperty("Avoid Raidable Bases Territory")]
            public bool CheckRaidableBases { get; set; } = true;

            [JsonProperty("Avoid Abandoned Bases Territory")]
            public bool CheckAbandonedBases { get; set; } = true;
        }

        public class DungeonSpawnSettings
        {
            [JsonProperty("Enable Auto Spawn")]
            public bool EnableAutoSpawn { get; set; } = false;

            [JsonProperty("Map Marker Radius")]
            public float MapMarkerRadius { get; set; } = 0.2f;

            [JsonProperty("Create Map Markers for Purchased Dungeons")]
            public bool EnableMapMarkersBuy { get; set; } = true;

            [JsonProperty("Create Map Markers for Auto-Spawned Dungeons")]
            public bool EnableMapMarkersAutoSpawn { get; set; } = true;

            [JsonProperty("Max Active Dungeons Auto Spawn (It is not recommended to exceed 10)")]
            public int MaxTotalActiveDungeonsAutoSpawn { get; set; } = 6;

            [JsonProperty("Max Active Dungeons Buy (It is not recommended to exceed 10")]
            public int MaxTotalActiveDungeonsBuy { get; set; } = 6;

            [JsonProperty("Dungeon Buy Cooldown (in seconds)")]
            public int BuyCooldown { get; set; } = 600;

            [JsonProperty("Auto Spawn Delay (in seconds)")]
            public int AutoSpawnDelay { get; set; } = 120;

            [JsonProperty("Dungeon Height (Y coordinate for dungeon)")]
            public float DungeonHeight { get; set; } = 800f;

            [JsonProperty("Dungeon Removal")]
            public DungeonRemovalSettings DungeonRemoval { get; set; } = new DungeonRemovalSettings();

            [JsonProperty("Show Dungeon Spawn Announcement")]
            public bool ShowDungeonSpawnAnnouncement { get; set; } = true;
        }

        public class DungeonRemovalSettings
        {
            [JsonProperty("Remove only if all NPCs are dead")]
            public bool RemoveIfAllNpcsDead { get; set; } = true;

            [JsonProperty("Remove only if all boxes are destroyed")]
            public bool RemoveIfAllBoxesDestroyed { get; set; } = true;

            [JsonProperty("Remove only if all turrets are destroyed")]
            public bool RemoveIfAllTurretsDestroyed { get; set; } = true;

            [JsonProperty("Removal timer after they are destroyed (in seconds)")]
            public int RemovalAfterDestroyedTimer { get; set; } = 300;
        }

        public class LootBoxConfig
        {
            [JsonProperty(PropertyName = "Loot Items", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ItemConfig> LootItems { get; set; } =
                new List<ItemConfig>
                {
                    new()
                    {
                        ShortName = "ammo.rifle",
                        InclusionChancePercentage = 15,
                        MinimumAmount = 100,
                        MaximumAmount = 300,
                    },
                    new()
                    {
                        ShortName = "sulfur.ore",
                        InclusionChancePercentage = 15,
                        MinimumAmount = 100,
                        MaximumAmount = 300,
                    },
                };
        }
        #endregion

        #region Models Configs
        public class DungeonRequest
        {
            public BasePlayer Player { get; set; }
            public DungeonTierConfig Tier { get; set; }
            public bool LockToPlayer { get; set; }
            public DungeonOrigin Origin { get; set; }
            public Vector3 PortalPosition { get; set; }
            public Vector3 DungeonPosition { get; set; }
        }

        private class IconInfo
        {
            public string Sprite { get; set; }
            public string Color { get; set; }
            public string Text { get; set; }
            public float X { get; set; }
            public float Y { get; set; }
            public float Width { get; set; }
            public float Height { get; set; }
        }

        private class ResourceItem
        {
            public string Shortname { get; set; }
            public int Amount { get; set; }
        }

        public class ActiveDungeon
        {
            public DateTime? ActivationTime { get; set; } = null;
            public bool HasBeenCleared { get; set; } = false;
            public DateTime? DestructionCountdownStart { get; set; } = null;
            public float DestructionDelay { get; set; } = 0;
            public string UniqueId { get; set; }
            public bool[] LastGeneratedGrid = null;
            public List<bool[]> DungeonGrids = new List<bool[]>();
            public List<Vector3> TurretPositions = new List<Vector3>();
            public List<Vector3> SpawnPoints { get; set; }
            public BasePortal BasePortal { get; set; }
            public BasePortal InsidePortal { get; set; }
            public ulong OwnerID { get; set; }
            public string OwnerName { get; set; }
            public Vector3 PortalPosition { get; set; }
            public Vector3 DungeonPosition { get; set; }
            public string Grid { get; set; }
            public string TierName { get; set; }
            public DungeonTierConfig TierConfig { get; set; }
            public List<BaseEntity> Entities { get; set; }
            public List<BaseEntity> Npcs { get; set; }
            public bool Spawned { get; set; }
            public DateTime CreationTime { get; set; }
            public DungeonOrigin Origin { get; set; }
            public DateTime? AllDestroyedTime { get; set; }
            public bool MarkedForRemoval { get; set; }
            public int LastReminderMinute { get; set; } = -1;
            public Bounds DungeonBounds { get; set; }
            public DungeonEventsTrigger Trigger { get; set; }
            public int DungeonRooms { get; set; } = 2;
            public int DungeonRoomSize { get; set; } = 2;
            public HashSet<string> OccupiedPositions = new HashSet<string>();
            public HashSet<Vector3> DungeonFloorCenters = new HashSet<Vector3>();
            public HashSet<ulong> PlayersInside = new HashSet<ulong>();
            public bool ClearRewardGiven { get; set; } = false;
            public List<Vector3> GridOrigins = new List<Vector3>();
            public List<bool[]> CellGrids = new List<bool[]>();
            public List<Vector3> Foundations = new List<Vector3>();
            public HashSet<string> FoundationKeys = new HashSet<string>();
            public HashSet<Vector3> RoomFloorCenters = new HashSet<Vector3>();
        }

        private class DungeonMarker
        {
            public VendingMachineMapMarker VendingMachineMapMarker { get; set; }
            public MapMarkerGenericRadius MapMarkerGenericRadius { get; set; }
            public Vector3 Position { get; set; }
            public string Tier { get; set; }
        }

        private class TierInfo
        {
            public string Name { get; set; }
            public string MapName { get; set; }
            public int BuyCost { get; set; }
            public Color MarkerColor { get; set; }
            public string ColorString { get; set; }
            public bool Enabled { get; set; }
        }

        public enum DungeonOrigin
        {
            AutoSpawn,
            Buy,
        }
        #endregion

        #region Chat Commands
        private void BuyDungeonChatCommand(BasePlayer player, string command, string[] args)
        {
            if (player == null)
                return;
            if (!permission.UserHasPermission(player.UserIDString, PermissionBuy))
            {
                NotifyPlayer(player, "NoAccess", 0);
                return;
            }
            if (args != null && args.Length > 0)
            {
                ProcessBuyDungeon(player, args[0]);
                return;
            }
            if (!_config.EnablePurchases)
            {
                NotifyPlayer(player, "PurchasesDisabled", 0);
                return;
            }
            OpenBuyDungeonGUI(player);
        }

        private void CreateDungeonChatCommand(BasePlayer player, string command, string[] args)
        {
            if (!IsAdmin(player))
                return;
            if (args.Length == 0)
            {
                NotifyPlayer(player, "UsageCreateMessage", 0);
                return;
            }
            var tierName = args[0];
            var tier = FindTierByName(tierName);
            if (tier == null)
            {
                NotifyPlayer(player, "InvalidTierMessage", 0, tierName, GetAvailableTiers());
                return;
            }
            if (!tier.EnableBuy)
            {
                NotifyPlayer(player, "ThisTierDisabled", 0);
                return;
            }
            LogDebug("Received dungeon creation request.", 1);
            EnqueueDungeonRequest(player, tier, false);
        }

        private void ReloadConfigChatCommand(BasePlayer player, string command, string[] args)
        {
            if (!IsAdmin(player))
                return;
            LoadConfig();
            RegisterCommands();
            _dungeonTimer?.Destroy();
            _dungeonTimer = timer.Every(TimeTimers, HandleDungeonTimers);
            UpdateMarkers();
            _dungeonQueue.Clear();
            NotifyPlayer(player, "ConfigReloaded", 0);
        }

        private void TogglePurchasesChatCommand(BasePlayer player, string command, string[] args)
        {
            if (!IsAdmin(player))
                return;
            _config.EnablePurchases = !_config.EnablePurchases;
            if (_config.EnablePurchases)
                NotifyPlayer(player, "PurchasesEnabled", 0);
            else
                NotifyPlayer(player, "PurchasesDisabled", 0);
        }

        private void RemoveInactiveDungeonsChatCommand(BasePlayer player, string command, string[] args)
        {
            if (!IsAdmin(player))
                return;
            var removedDungeons = RemoveInactiveDungeons();
            NotifyPlayerOnRemoval(player, removedDungeons);
        }

        private void RemoveAllDungeonsChatCommand(BasePlayer player, string command, string[] args)
        {
            if (!IsAdmin(player))
                return;
            var removed = RemoveAllDungeons();
            NotifyPlayerOnRemoval(player, removed);
        }

        private void RemoveNearestDungeonChatCommand(BasePlayer player, string command, string[] args)
        {
            if (!IsAdmin(player))
                return;
            if (_activeDungeons.Count == 0)
            {
                NotifyPlayer(player, "NoDungeonsToRemove", 0);
                return;
            }
            ActiveDungeon nearest = null;
            float minDist = float.MaxValue;
            foreach (var dungeon in _activeDungeons)
            {
                float d1 = Vector3.Distance(player.transform.position, dungeon.PortalPosition);
                float d2 = Vector3.Distance(player.transform.position, dungeon.DungeonPosition);
                float d = Mathf.Min(d1, d2);
                if (d < minDist)
                {
                    minDist = d;
                    nearest = dungeon;
                }
            }
            if (nearest == null || minDist > 10f)
            {
                NotifyPlayer(player, "TooFarToRemove", 0);
                return;
            }
            DestroyDungeon(nearest);
            NotifyPlayer(player, "DungeonRemovedDetail", 0, nearest.TierName, nearest.Grid);
        }

        private void RemoveOwnDungeonChatCommand(BasePlayer player, string command, string[] args)
        {
            if (player == null)
                return;
            var owned = _activeDungeons.FirstOrDefault(d => d.OwnerID == player.userID);
            if (owned == null)
            {
                NotifyPlayer(player, "NotDungeonOwner", 0);
                return;
            }
            DestroyDungeon(owned);
            NotifyPlayer(player, "DungeonRemovedDetail", 0, owned.TierName, owned.Grid);
        }

        private void ForceRemoveAllDungeonsChatCommand(BasePlayer player, string command, string[] args)
        {
            if (!IsAdmin(player))
                return;
            CleanupExistingEntities();
            NotifyPlayer(player, "CleanedUp", 0);
        }
        #endregion

        #region Console Commands
        private void BuyDungeonCommand(ConsoleSystem.Arg arg)
        {
            if (arg == null || !arg.HasArgs(2))
            {
                LogDebug("Usage: buydungeon <tierName> <playerID>", 1);
                return;
            }
            string tierName = arg.GetString(0);
            string playerId = arg.GetString(1);
            if (string.IsNullOrEmpty(tierName) || string.IsNullOrEmpty(playerId))
                return;
            BasePlayer player = BasePlayer.FindAwakeOrSleeping(playerId);
            if (player == null)
            {
                LogDebug($"Player with ID '{playerId}' not found.", 1);
                return;
            }
            ProcessBuyDungeon(player, tierName);
        }

        private void SpawnRandomDungeonCommand(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !IsAdmin(arg.Player()))
            {
                arg.ReplyWith("You do not have permission to run this command.");
                return;
            }
            var tier = GetDifficultyWithLeastActiveDungeons();
            EnqueueDungeonRequest(null, tier, false);
            arg.ReplyWith($"Enqueued random dungeon spawn of tier: {_tierInfoMap[tier].Name} (no owner).");
        }

        private void SpawnFixedDungeonCommand(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !IsAdmin(arg.Player()))
            {
                arg.ReplyWith("You do not have permission to run this command.");
                return;
            }
            if (!arg.HasArgs(1))
            {
                arg.ReplyWith("Usage: spawnfixeddungeon <tierName> [playerID]");
                return;
            }
            var tierName = arg.GetString(0);
            var tier = FindTierByName(tierName);
            if (tier == null)
            {
                arg.ReplyWith($"Invalid tier '{tierName}'. Available tiers: {GetAvailableTiers()}");
                return;
            }
            if (!tier.EnableAutoSpawn)
            {
                arg.ReplyWith("This tier is disabled for Auto Spawn.");
                return;
            }
            int activeAutoSameTier = _activeDungeons.Count(d => d.Origin == DungeonOrigin.AutoSpawn && d.TierConfig == tier);
            if (tier.MaxActiveAutoSpawn > 0 && activeAutoSameTier >= tier.MaxActiveAutoSpawn)
            {
                arg.ReplyWith("Reached the per-tier max for Auto Spawn.");
                return;
            }
            if (arg.Args != null && arg.Args.Length >= 2)
            {
                string playerId = arg.GetString(1);
                BasePlayer owner = BasePlayer.FindAwakeOrSleeping(playerId);
                if (owner == null)
                {
                    arg.ReplyWith($"Player with ID '{playerId}' not found.");
                    return;
                }
                var request = new DungeonRequest
                {
                    Player = owner,
                    Tier = tier,
                    LockToPlayer = true,
                    Origin = DungeonOrigin.AutoSpawn,
                };
                _dungeonQueue.Enqueue(request);
                ProcessDungeonQueue();
                arg.ReplyWith($"Enqueued dungeon spawn of tier: {_tierInfoMap[tier].Name} for player {owner.displayName}.");
                return;
            }
            EnqueueDungeonRequest(null, tier, false);
            arg.ReplyWith($"Enqueued dungeon spawn of tier: {_tierInfoMap[tier].Name} (no owner).");
        }

        [ConsoleCommand(UiCommandClose)]
        private void CloseUiCommand(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null)
                DestroyUi(player);
        }
        #endregion

        #region Flow
        private IEnumerator StartupRoutine()
        {
            yield return CoroutineEx.waitForEndOfFrame;
            float delay = BasePlayer.activePlayerList.Count > 0 ? 1f : 60f;
            yield return new WaitForSeconds(delay);
            RemoveLeftoverDungeonFiles();
            BlockZones();
            _dungeonTimer = timer.Every(TimeTimers, HandleDungeonTimers);
        }

        private bool HasBlockedRespawnEntityNearby(Vector3 position, float radius)
        {
            var entities = Pool.GetList<BaseEntity>();
            try
            {
                Vis.Entities(position, radius, entities, Layers.Mask.Deployed);
                foreach (var entity in entities)
                {
                    if (entity == null)
                        continue;
                    var prefabName = entity.PrefabName;
                    if (string.IsNullOrEmpty(prefabName))
                        continue;
                    for (int i = 0; i < BlockedRespawnPrefabs.Length; i++)
                    {
                        if (prefabName.Equals(BlockedRespawnPrefabs[i], StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
            finally
            {
                Pool.FreeList(ref entities);
            }
        }

        private IEnumerator CreateDungeon(DungeonRequest request)
        {
            yield return null;
            isCreatingDungeon = true;
            request.PortalPosition = Vector3.zero;
            request.DungeonPosition = Vector3.zero;
            var tierConfig = request.Tier;
            var player = request.Player;
            bool lockDungeonToPlayer = request.LockToPlayer;
            var origin = request.Origin;
            Vector3 portalPosition = Vector3.zero;
            Vector3 dungeonPosition = Vector3.zero;
            LogDebug("Generating portal spawn point.", 1);
            var sp = _config?.SpawnPointConfiguration ?? new SpawnPointConfiguration();
            var configPortal = new GeneratorConfig
            {
                Origin = player != null ? player.transform.position : Vector2.zero,
                ClosestToOrigin = player != null,
                YOffset = 0f,
                minDist = player != null ? 10 : 200,
                blocked = _activeDungeons.Select(d => d.PortalPosition).ToList(),
                AvoidTerrainTopology = sp.AvoidTerrainTopology,
                AvoidedTopology =
                    TerrainTopology.Enum.Building
                    | TerrainTopology.Enum.Cliff
                    | TerrainTopology.Enum.Clutter
                    | TerrainTopology.Enum.Monument
                    | TerrainTopology.Enum.Ocean
                    | TerrainTopology.Enum.Rail
                    | TerrainTopology.Enum.Road,
                AboveWater = true,
                MaxSlopeDegrees = 30f,
                CheckSphereRadius = sp.CheckSphereRadius ? 6f : 0f,
                CheckSphereMask =
                    Layers.Mask.AI
                    | Layers.Mask.Bush
                    | Layers.Mask.Clutter
                    | Layers.Mask.Construction
                    | Layers.Mask.Default
                    | Layers.Mask.Deployed
                    | Layers.Mask.Harvestable
                    | Layers.Mask.Physics_Debris
                    | Layers.Mask.Player_Server
                    | Layers.Mask.Prevent_Building
                    | Layers.Mask.Prevent_Movement
                    | Layers.Mask.Tree
                    | Layers.Mask.Trigger
                    | Layers.Mask.Vehicle_Detailed
                    | Layers.Mask.Vehicle_Large
                    | Layers.Mask.Vehicle_World
                    | Layers.Mask.Water
                    | Layers.Mask.World
                    | Layers.Server.Deployed,
                MaxAttempts = 10000,
                UseSlopeCheck = true,
                CheckSafeZones = true,
                ZoneManager = sp.CheckZoneManager,
                TerrainMask = Layers.Mask.Terrain | Layers.Mask.World | Layers.Mask.Construction,
                CheckRocks = true,
                RockCheck = RockKeywords,
            };
            yield return TryFindPosition(configPortal, request, true);
            portalPosition = request.PortalPosition;
            LogDebug("Generating dungeon spawn point.", 1);
            var configDungeon = new GeneratorConfig
            {
                Origin = Vector2.zero,
                ClosestToOrigin = false,
                YOffset = _config.DungeonSpawn.DungeonHeight,
                minDist = 200,
                blocked = _activeDungeons.Select(d => d.DungeonPosition).ToList(),
                AvoidTerrainTopology = sp.AvoidTerrainTopology,
                AvoidedTopology = TerrainTopology.Enum.Monument,
                AboveWater = true,
                MaxSlopeDegrees = 30f,
                CheckSphereRadius = sp.CheckSphereRadius ? 100f : 0f,
                CheckSphereMask =
                    Layers.Mask.AI
                    | Layers.Mask.Bush
                    | Layers.Mask.Clutter
                    | Layers.Mask.Construction
                    | Layers.Mask.Default
                    | Layers.Mask.Deployed
                    | Layers.Mask.Harvestable
                    | Layers.Mask.Physics_Debris
                    | Layers.Mask.Player_Server
                    | Layers.Mask.Prevent_Building
                    | Layers.Mask.Prevent_Movement
                    | Layers.Mask.Tree
                    | Layers.Mask.Trigger
                    | Layers.Mask.Vehicle_Detailed
                    | Layers.Mask.Vehicle_Large
                    | Layers.Mask.Vehicle_World
                    | Layers.Mask.Water
                    | Layers.Mask.World
                    | Layers.Server.Deployed,
                MaxAttempts = 1000,
                UseSlopeCheck = true,
                CheckSafeZones = true,
                ZoneManager = sp.CheckZoneManager,
                TerrainMask = Layers.Mask.Terrain | Layers.Mask.World | Layers.Mask.Construction,
                CheckRocks = true,
                RockCheck = RockKeywords,
            };
            yield return TryFindPosition(configDungeon, request, false);
            dungeonPosition = request.DungeonPosition;
            if (portalPosition == Vector3.zero || dungeonPosition == Vector3.zero)
            {
                Requeue(request, origin);
                LogDebug("Failed to find spawn points. Requeuing...", 1);
                isCreatingDungeon = false;
                yield break;
            }
            if (HasBlockedRespawnEntityNearby(portalPosition, 4f))
            {
                LogDebug("Rejected portal spawn point because a bed/sleeping bag/beach towel is too close. Requeuing...", 1);
                Requeue(request, origin);
                isCreatingDungeon = false;
                yield break;
            }
            LogDebug("Dungeon spawn point found.");
            portalPosition += new Vector3(0f, 0.3f, 0f);
            request.PortalPosition = portalPosition;
            if (!_tierInfoMap.TryGetValue(tierConfig, out var tierInfo))
            {
                LogDebug($"[CreateDungeon] Tier '{tierConfig?.MapName}' disabled!", 2);
                isCreatingDungeon = false;
                yield break;
            }
            var activeDungeon = RegisterActiveDungeon(null, portalPosition, dungeonPosition, MapHelper.PositionToString(portalPosition), tierConfig, player, lockDungeonToPlayer, origin);
            activeDungeon.UniqueId = Guid.NewGuid().ToString();
            activeDungeon.DungeonBounds = new Bounds(activeDungeon.DungeonPosition, new Vector3(1, 1, 1));
            activeDungeon.Trigger = new GameObject().AddComponent<DungeonEventsTrigger>();
            activeDungeon.Trigger.Initialize(activeDungeon);
            activeDungeon.Trigger.SetPosition(activeDungeon.DungeonPosition, new Vector3(1, 1, 1));
            activeDungeon.Trigger.TriggerTime = tierConfig.TriggerTime;
            activeDungeon.Trigger.NightVisionTime = _config.NightVisionTime;
            if (tierConfig.DungeonRooms < 1 || tierConfig.DungeonRooms > 6)
                tierConfig.DungeonRooms = 1;
            if (tierConfig.DungeonRoomSize < 5 || tierConfig.DungeonRoomSize > 10)
                tierConfig.DungeonRoomSize = 5;
            activeDungeon.DungeonRooms = tierConfig.DungeonRooms;
            activeDungeon.DungeonRoomSize = tierConfig.DungeonRoomSize;
            yield return CreateCustomProceduralDynamicDungeon(activeDungeon);
            yield return BuildWalls(activeDungeon);
            yield return BuildFrames(activeDungeon);
            LogDebug("Procedural Dynamic Dungeon Created.");
            activeDungeon.Trigger.SetPosition(activeDungeon.DungeonBounds.center, activeDungeon.DungeonBounds.size);
            if (activeDungeon.SpawnPoints.Count == 0)
            {
                UnityEngine.GameObject.Destroy(activeDungeon.Trigger);
                Requeue(request, origin);
                isCreatingDungeon = false;
                LogDebug("Failed to find spawn points. Requeuing...", 1);
                yield break;
            }
            _playersBuying.Remove(player);
            AssignEntitiesToSpawnPoints(activeDungeon.SpawnPoints, tierConfig, out var turretSpawns, out var boxSpawns, out var npcSpawns, out var bossSpawns);
            yield return PlaceEntities(turretSpawns, boxSpawns, npcSpawns, bossSpawns, activeDungeon);
            CreateDungeonMarkers(activeDungeon);
            activeDungeon.BasePortal = CreateDungeonPortal(portalPosition, activeDungeon);
            activeDungeon.InsidePortal.targetPortal = activeDungeon.BasePortal;
            activeDungeon.BasePortal.targetPortal = activeDungeon.InsidePortal;
            activeDungeon.InsidePortal.LinkPortal();
            activeDungeon.BasePortal.LinkPortal();
            if (_config.DungeonSpawn.ShowDungeonSpawnAnnouncement || activeDungeon.Origin == DungeonOrigin.Buy)
                NotifyPlayersOfNewDungeon(activeDungeon);
            LogDebug($"{activeDungeon.TierName} Dungeon spawned successfully at grid: {activeDungeon.Grid}.", 1);
            activeDungeon.Spawned = true;
            Interface.CallHook("OnDungeonSpawn", activeDungeon.OwnerID, activeDungeon.PortalPosition, activeDungeon.Grid, activeDungeon.TierName);
            isCreatingDungeon = false;
            UpdateMarkers();
            DrawLines(activeDungeon, player);
        }
        #endregion

        #region Behaviours
        public class CorpseLootData : MonoBehaviour
        {
            public bool IsBoss;
            public NpcConfig LootConfig;
        }

        public class NpcMovement : MonoBehaviour
        {
            private const float WaypointArrivalTol = 0.5f;
            private const float MaxStepHeight = 1.2f;
            private const int MaxWaypointHistory = 99;
            private const int WaypointsCapacity = MaxWaypointHistory + 4;
            private const float HeadHeight = 1.2f;
            private const float EPS = 1e-6f;
            private const float PlayerRescanInterval = 0.25f;
            private readonly Collider[] _playerScanBuf = new Collider[256];
            private readonly Collider[] _nearby = new Collider[64];
            private readonly Queue<Vector3> _wayPoints = new Queue<Vector3>(WaypointsCapacity);
            private float _separationRadius = 1.05f;
            private float _separationWeight = 0.9f;
            private BaseAIBrain _npcBrain;
            private NPCPlayer _npc;
            private BaseEntity _mainTarget;
            private Vector3 _initialPosition;
            private Vector3 _moveTarget;
            private int _ignoreLayersMask;
            private int _blockMask;
            private bool _contact;
            private bool _targetIsVisible;
            private float _npcMoveSpeed;
            private float _range;
            private float _attackDistance = 1f;
            private float _lastPlayerScanTime;
            private BasePlayer _cachedPlayer;
            private double _nextFireTime;

            private void Awake()
            {
                _npc = GetComponent<NPCPlayer>();
                if (_npc == null)
                {
                    Destroy(this);
                    return;
                }
                _npcBrain = GetComponent<BaseAIBrain>();
                _attackDistance = GetAttackDistance();
                _npcMoveSpeed = 0.9f;
                _ignoreLayersMask = ~(Layers.Mask.Ignore_Raycast | Layers.Mask.Reserved1 | Layers.Mask.Invisible | Layers.Mask.Trigger | Layers.PreventBuilding | Layers.Mask.Prevent_Movement);
                _blockMask = Layers.Mask.Construction | Layers.Mask.Deployed | Layers.Mask.World | Layers.Mask.Terrain | Layers.Mask.Default;
                _initialPosition = _npc.transform.position;
                _moveTarget = _npc.transform.position;
                _mainTarget = _npc;
                _contact = false;
                _targetIsVisible = false;
                _range = WaypointArrivalTol;
                try
                {
                    _npc.CancelInvoke(_npc.TickMovement);
                }
                catch { }
                try
                {
                    _npc.CancelInvoke(_npc.ServerThink_Internal);
                }
                catch { }
                _nextFireTime = 0;
                InvokeRepeating(nameof(UpdateAi), 1f, 0.5f);
                InvokeRepeating(nameof(UpdateMovement), 0.28f, 0.28f);
                InvokeRepeating(nameof(UpdateFire), 0.15f, 0.05f);
            }

            private float GetAttackDistance()
            {
                string shortName = null;
                var active = _npc?.GetActiveItem();
                if (active != null)
                    shortName = active.info?.shortname;
                if (string.IsNullOrEmpty(shortName))
                {
                    var belt = _npc?.inventory?.containerBelt;
                    if (belt?.itemList != null)
                    {
                        for (int i = 0; i < belt.itemList.Count; i++)
                        {
                            var it = belt.itemList[i];
                            if (it?.info == null)
                                continue;
                            shortName = it.info.shortname;
                            if (!string.IsNullOrEmpty(shortName))
                                break;
                        }
                    }
                }
                bool isMelee = _instance != null && _instance.IsMeleeWeapon(shortName);
                return isMelee ? 1f : 5f;
            }

            private BasePlayer GetFirstPlayer()
            {
                if (Time.realtimeSinceStartup - _lastPlayerScanTime < PlayerRescanInterval && _cachedPlayer != null && !_cachedPlayer.IsDestroyed)
                    return _cachedPlayer;
                float sense = _npcBrain != null ? _npcBrain.SenseRange : 30f;
                int hitCount = Physics.OverlapSphereNonAlloc(_npc.transform.position, sense, _playerScanBuf, Layers.Mask.Player_Server, QueryTriggerInteraction.Ignore);
                BasePlayer closest = null;
                float bestSqr = float.MaxValue;
                for (int i = 0; i < hitCount; i++)
                {
                    var p = _playerScanBuf[i]?.GetComponent<BasePlayer>();
                    if (p == null || p.IsNpc || p.IsSleeping())
                        continue;
                    float sq = (_npc.transform.position - p.transform.position).sqrMagnitude;
                    if (sq < bestSqr)
                    {
                        bestSqr = sq;
                        closest = p;
                    }
                }
                _cachedPlayer = closest;
                _lastPlayerScanTime = Time.realtimeSinceStartup;
                return closest;
            }

            private void UpdateAi()
            {
                var player = GetFirstPlayer();
                if (player == null)
                {
                    _mainTarget = _npc;
                    _contact = false;
                    _targetIsVisible = false;
                    _range = WaypointArrivalTol;
                    return;
                }
                if (_mainTarget != player)
                    _contact = false;
                Vector3 npcPos = _npc.transform.position;
                Vector3 playerPos = player.transform.position;
                Vector3 eyeNpc = npcPos + Vector3.up * HeadHeight;
                Vector3 eyePlayer = playerPos + Vector3.up * 1.0f;
                bool blocked = Physics.Linecast(eyeNpc, eyePlayer, _blockMask, QueryTriggerInteraction.Ignore);
                float distToPlayerSqr = (npcPos - playerPos).sqrMagnitude;
                float atkSqr = _attackDistance * _attackDistance;
                if (!blocked)
                {
                    _contact = true;
                    _targetIsVisible = true;
                    _range = 5f;
                    _wayPoints.Clear();
                    _mainTarget = player;
                    _moveTarget = playerPos;
                }
                else
                {
                    _targetIsVisible = false;
                    _range = distToPlayerSqr < atkSqr ? _attackDistance : 1f;
                }
            }

            private void UpdateMovement()
            {
                if (_npc == null)
                    return;
                var player = _mainTarget as BasePlayer;
                if (player == null || Mathf.Abs(player.transform.position.y - _npc.transform.position.y) > 3.5f)
                    _contact = false;
                if (!_contact)
                {
                    if ((_npc.transform.position - _initialPosition).sqrMagnitude > 0.01f)
                        MoveTowards(_initialPosition);
                    return;
                }
                float atkSqr = _attackDistance * _attackDistance;
                if ((_npc.transform.position - player.transform.position).sqrMagnitude <= atkSqr)
                    return;
                if (_wayPoints.Count >= WaypointsCapacity)
                    _wayPoints.Dequeue();
                _wayPoints.Enqueue(player.transform.position);
                if (_moveTarget == _npc.transform.position && _wayPoints.Count > 0)
                    _moveTarget = _wayPoints.Peek();
                float rangeSqr = _range * _range;
                float distToTargetSqr = (_npc.transform.position - _moveTarget).sqrMagnitude;
                if (distToTargetSqr < rangeSqr)
                {
                    if (_range < 0.6f && _wayPoints.Count > 0)
                        _wayPoints.Dequeue();
                    _moveTarget = _wayPoints.Count > 0 ? _wayPoints.Peek() : _npc.transform.position;
                }
                if (distToTargetSqr <= 0.04f)
                    return;
                Vector3 here = _npc.transform.position;
                Vector3 flatGoal = new Vector3(_moveTarget.x, here.y, _moveTarget.z);
                Vector3 dir = flatGoal - here;
                dir.y = 0f;
                float lenSqr = dir.sqrMagnitude;
                if (lenSqr < EPS)
                    return;
                dir /= Mathf.Sqrt(lenSqr);
                Vector3 sep = SeparationVector(_separationRadius, player);
                if (sep.sqrMagnitude > EPS)
                    dir = (dir + sep).normalized;
                AdvanceWithObstacleHandling(dir, _npcMoveSpeed);
            }

            private void UpdateFire()
            {
                if (_npc == null)
                    return;
                var player = _mainTarget as BasePlayer;
                if (player == null)
                    return;
                if (!_contact || !_targetIsVisible)
                    return;
                float atkSqr = _attackDistance * _attackDistance;
                if ((_npc.transform.position - player.transform.position).sqrMagnitude > atkSqr)
                    return;
                var held = _npc.GetHeldEntity() as BaseProjectile;
                if (held == null)
                    return;
                double now = Time.timeAsDouble;
                if (now < _nextFireTime)
                    return;
                Vector3 npcEye = _npc.transform.position + Vector3.up * HeadHeight;
                Vector3 playerAim = player.transform.position + Vector3.up * 1.0f;
                Vector3 aimDir = (playerAim - npcEye);
                if (aimDir.sqrMagnitude > 0.0001f)
                    aimDir.Normalize();
                try
                {
                    _npc.SetAimDirection(aimDir);
                }
                catch { }
                if (held.primaryMagazine != null && held.primaryMagazine.contents <= 0)
                {
                    try
                    {
                        held.ServerReload();
                    }
                    catch { }
                    _nextFireTime = now + 0.15;
                    return;
                }
                try
                {
                    held.ServerUse();
                }
                catch { }
                float delay = held.repeatDelay;
                if (delay < 0.05f)
                    delay = 0.05f;
                _nextFireTime = now + delay;
            }

            private void MoveTowards(Vector3 target)
            {
                Vector3 here = _npc.transform.position;
                Vector3 flatGoal = new Vector3(target.x, here.y, target.z);
                Vector3 dir = flatGoal - here;
                dir.y = 0f;
                if (dir.sqrMagnitude < EPS)
                    return;
                dir.Normalize();
                Vector3 sep = SeparationVector(_separationRadius, null);
                if (sep.sqrMagnitude > EPS)
                    dir = (dir + sep).normalized;
                AdvanceWithObstacleHandling(dir, _npcMoveSpeed);
            }

            private void AdvanceWithObstacleHandling(Vector3 dir, float moveDist)
            {
                Vector3 here = _npc.transform.position;
                Vector3 a = here + Vector3.up * 0.5f;
                Vector3 b = here + Vector3.up * 1.6f;
                if (Physics.CapsuleCast(a, b, 0.28f, dir, out RaycastHit hit, moveDist, _blockMask, QueryTriggerInteraction.Ignore) && !ShouldIgnoreCollider(hit.collider))
                {
                    Vector3 slideDir = Vector3.ProjectOnPlane(dir, hit.normal);
                    if (slideDir.sqrMagnitude < 1e-6f)
                        slideDir = Vector3.Cross(Vector3.up, dir);
                    if (slideDir.sqrMagnitude > 1e-6f)
                    {
                        slideDir.Normalize();
                        float slideDist = moveDist * 0.5f;
                        if (!Physics.CapsuleCast(a, b, 0.28f, slideDir, out _, slideDist, _blockMask, QueryTriggerInteraction.Ignore))
                        {
                            Vector3 next = here + slideDir * slideDist;
                            if (TryGetGroundPosition(next, out var g))
                                next = g;
                            if (!WouldOverlapNpcs(next, 0.55f))
                            {
                                _npc.transform.position = next;
                                _npc.transform.rotation = Quaternion.LookRotation(slideDir);
                                return;
                            }
                        }
                    }
                    Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
                    if (TrySideAdvance(here, a, b, side, moveDist))
                        return;
                    if (TrySideAdvance(here, a, b, -side, moveDist))
                        return;
                    float shortDist = moveDist * 0.3f;
                    if (!Physics.CapsuleCast(a, b, 0.28f, dir, out _, shortDist, _blockMask, QueryTriggerInteraction.Ignore))
                    {
                        Vector3 shorter = here + dir * shortDist;
                        if (TryGetGroundPosition(shorter, out var g2))
                            shorter = g2;
                        if (!WouldOverlapNpcs(shorter, 0.55f))
                        {
                            _npc.transform.position = shorter;
                            _npc.transform.rotation = Quaternion.LookRotation(dir);
                        }
                    }
                    return;
                }
                Vector3 nextFwd = here + dir * moveDist;
                if (TryGetGroundPosition(nextFwd, out var g3))
                    nextFwd = g3;
                if (!WouldOverlapNpcs(nextFwd, 0.55f))
                {
                    _npc.transform.position = nextFwd;
                    _npc.transform.rotation = Quaternion.LookRotation(dir);
                }
            }

            private bool TrySideAdvance(Vector3 here, Vector3 capA, Vector3 capB, Vector3 sideDir, float moveDist)
            {
                if (sideDir.sqrMagnitude < 1e-6f)
                    return false;
                if (Physics.CapsuleCast(capA, capB, 0.28f, sideDir, out _, moveDist, _blockMask, QueryTriggerInteraction.Ignore))
                    return false;
                Vector3 target = here + sideDir * moveDist;
                if (TryGetGroundPosition(target, out var g))
                    target = g;
                if (WouldOverlapNpcs(target, 0.55f))
                    return false;
                _npc.transform.position = target;
                _npc.transform.rotation = Quaternion.LookRotation(sideDir);
                return true;
            }

            private Vector3 SeparationVector(float radius, BasePlayer player)
            {
                int hits = Physics.OverlapSphereNonAlloc(_npc.transform.position, radius, _nearby, ~0, QueryTriggerInteraction.Ignore);
                Vector3 push = Vector3.zero;
                int count = 0;
                for (int i = 0; i < hits; i++)
                {
                    var col = _nearby[i];
                    if (col == null)
                        continue;
                    var other = col.GetComponentInParent<BaseCombatEntity>();
                    if (other == null || other == _npc || other == player)
                        continue;
                    if (other.IsDestroyed)
                        continue;
                    if (_instance == null || !_instance.IsDungeonEntity(other))
                        continue;
                    Vector3 away = _npc.transform.position - other.transform.position;
                    away.y = 0f;
                    float d = away.magnitude;
                    if (d < 0.001f || d > radius)
                        continue;
                    float t = 1f - (d / radius);
                    push += away / (d + 0.001f) * t;
                    count++;
                }
                if (count > 0)
                    push /= count;
                return push.normalized * _separationWeight;
            }

            private bool WouldOverlapNpcs(Vector3 p, float radius)
            {
                int hits = Physics.OverlapSphereNonAlloc(p, radius, _nearby, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < hits; i++)
                {
                    var col = _nearby[i];
                    if (col == null)
                        continue;
                    int lmask = 1 << col.gameObject.layer;
                    if ((lmask & _blockMask) != 0)
                        continue;
                    var other = col.GetComponentInParent<NPCPlayer>();
                    if (other != null)
                    {
                        if (other == _npc)
                            continue;
                        if (other.IsDestroyed)
                            continue;
                        if (_instance != null && !_instance.IsDungeonEntity(other))
                            continue;
                        return true;
                    }
                }
                return false;
            }

            private bool ShouldIgnoreCollider(Collider col)
            {
                if (col == null)
                    return true;
                if (col.gameObject == _npc.gameObject || col.transform.IsChildOf(_npc.transform))
                    return true;
                int lmask = 1 << col.gameObject.layer;
                if ((lmask & Layers.Mask.Player_Server) != 0)
                    return true;
                if (col.bounds.max.y < _npc.transform.position.y + 0.2f)
                    return true;
                return false;
            }

            private bool TryGetGroundPosition(Vector3 pos, out Vector3 groundPos)
            {
                groundPos = pos;
                if (Physics.Raycast(pos + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 3f, _blockMask, QueryTriggerInteraction.Ignore))
                {
                    float delta = _npc.transform.position.y - hit.point.y;
                    if (delta > 0f && delta <= MaxStepHeight)
                    {
                        groundPos = new Vector3(pos.x, hit.point.y, pos.z);
                        return true;
                    }
                }
                return false;
            }

            private void OnDestroy()
            {
                CancelInvoke(nameof(UpdateAi));
                CancelInvoke(nameof(UpdateMovement));
                CancelInvoke(nameof(UpdateFire));
            }
        }

        private bool IsMeleeWeapon(string shortName)
        {
            if (string.IsNullOrEmpty(shortName))
                return false;
            var def = ItemManager.FindItemDefinition(shortName);
            if (def == null)
                return false;
            var entityMod = def.GetComponent<ItemModEntity>();
            if (entityMod == null)
                return false;
            var prefab = entityMod.entityPrefab?.Get();
            if (prefab == null)
                return false;
            var attackEntity = prefab.GetComponent<AttackEntity>();
            if (attackEntity == null)
                return false;
            if (attackEntity is BaseProjectile)
                return false;
            if (attackEntity is BaseMelee)
                return true;
            return false;
        }

        public class BossMonoBehaviour : MonoBehaviour { }

        public class TurretMonoBehaviour : MonoBehaviour
        {
            private AutoTurret turretEntity;

            private void Awake()
            {
                turretEntity = GetComponent<AutoTurret>();
                turretEntity.SetPeacekeepermode(false);
                turretEntity.InitiateStartup();
                turretEntity.isLootable = false;
                turretEntity.dropFloats = false;
                turretEntity.dropsLoot = false;
                InvokeRepeating(nameof(ManageAmmo), 2f, 30f);
                InvokeRepeating(nameof(Perform360IdleSweep), 3f, 1f);
                InvokeRepeating(nameof(RemoveInterfence), 1f, 1f);
            }

            private void RemoveInterfence()
            {
                AutoTurret.interferenceUpdateList.Remove(turretEntity);
                if (turretEntity.HasFlag(BaseEntity.Flags.OnFire))
                {
                    turretEntity.SetFlag(BaseEntity.Flags.OnFire, false);
                }
            }

            private void ManageAmmo()
            {
                const int maxAmmoCapacity = 1000;
                if (turretEntity.AttachedWeapon is not BaseProjectile projectileWeapon || projectileWeapon.primaryMagazine?.ammoType == null)
                    return;
                var ammoType = projectileWeapon.primaryMagazine.ammoType;
                int currentAmmoCount = turretEntity.inventory.itemList.Where(item => item.info == ammoType).Sum(item => item.amount);
                if (currentAmmoCount < maxAmmoCapacity)
                {
                    int ammoNeeded = maxAmmoCapacity - currentAmmoCount;
                    Item additionalAmmo = ItemManager.Create(ammoType, ammoNeeded);
                    additionalAmmo?.MoveToContainer(turretEntity.inventory);
                    turretEntity.UpdateTotalAmmo();
                    turretEntity.EnsureReloaded();
                    turretEntity.SendNetworkUpdateImmediate();
                }
            }

            private void Perform360IdleSweep()
            {
                if (!turretEntity.HasTarget())
                {
                    turretEntity.targetAimDir = Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward;
                    turretEntity.aimDir = Vector3.Lerp(turretEntity.aimDir, turretEntity.targetAimDir, Time.deltaTime * 2f);
                    turretEntity.UpdateAiming(Time.deltaTime);
                }
            }

            private void OnDestroy()
            {
                CancelInvoke(nameof(ManageAmmo));
                CancelInvoke(nameof(Perform360IdleSweep));
                CancelInvoke(nameof(RemoveInterfence));
            }
        }

        public class TurretBehaviour : FacepunchBehaviour
        {
            AutoTurret autoTurret;
            public float targetRadius = 30f;

            private void Awake()
            {
                autoTurret = GetComponent<AutoTurret>();
                if (autoTurret == null)
                    return;
                SphereCollider sphereCollider = autoTurret.targetTrigger.GetComponent<SphereCollider>();
                if (sphereCollider != null)
                    sphereCollider.enabled = false;
                autoTurret.isLootable = false;
                autoTurret.dropFloats = false;
                autoTurret.dropsLoot = false;
                autoTurret.SetPeacekeepermode(false);
                autoTurret.InitiateStartup();
                autoTurret.CancelInvoke(autoTurret.ServerDo);
                autoTurret.InvokeRepeating(CustomServerTick, 3f, 0.015f);
                autoTurret.InvokeRepeating(ScanTargets, 3f, 1f);
                autoTurret.InvokeRepeating(ManageAmmo, 3f, 5f);
                autoTurret.InvokeRepeating(Perform360IdleSweep, 3f, 0.7f);
                autoTurret.InvokeRepeating(RemoveInterference, 3f, 1f);
            }

            private bool CustomObjectVisible(BaseCombatEntity target)
            {
                Vector3 eyePosition = autoTurret.eyePos.position;
                Vector3 aimOffset = autoTurret.AimOffset(target);
                float distance = Vector3.Distance(eyePosition, aimOffset);
                Ray ray = new Ray(eyePosition, (aimOffset - eyePosition).normalized);
                List<RaycastHit> hits = Facepunch.Pool.Get<List<RaycastHit>>();
                GamePhysics.TraceAll(ray, 0f, hits, distance * 1.1f, 1218652417);
                bool blockingFound = false;
                foreach (RaycastHit hit in hits)
                {
                    BaseEntity blockingEntity = RaycastHitEx.GetEntity(hit);
                    if (blockingEntity == null)
                        continue;
                    if (blockingEntity == target)
                        continue;
                    if (blockingEntity == autoTurret || blockingEntity.transform.IsChildOf(autoTurret.transform))
                        continue;
                    blockingFound = true;
                    break;
                }
                Facepunch.Pool.FreeUnmanaged(ref hits);
                return !blockingFound;
            }

            private void ScanTargets()
            {
                autoTurret.target = null;
                if (autoTurret.targetTrigger.entityContents == null)
                    autoTurret.targetTrigger.entityContents = new HashSet<BaseEntity>();
                else
                    autoTurret.targetTrigger.entityContents.Clear();
                int count = BaseEntity.Query.Server.GetPlayersInSphereFast(transform.position, targetRadius, AIBrainSenses.playerQueryResults, CanBeTargeted);
                if (count == 0)
                    return;
                autoTurret.authDirty = true;
                for (int i = 0; i < count; i++)
                {
                    BasePlayer player = AIBrainSenses.playerQueryResults[i];
                    if (player.IsSleeping())
                        continue;
                    autoTurret.targetTrigger.entityContents.Add(player);
                }
                if (autoTurret.target == null)
                {
                    foreach (BaseEntity entity in autoTurret.targetTrigger.entityContents)
                    {
                        BaseCombatEntity combatEntity = entity as BaseCombatEntity;
                        if (combatEntity != null)
                        {
                            bool isAlive = combatEntity.IsAlive();
                            bool inFiringArc = autoTurret.InFiringArc(combatEntity);
                            bool isVisible = CustomObjectVisible(combatEntity);
                            if (isAlive && inFiringArc && isVisible)
                            {
                                autoTurret.target = combatEntity;
                                autoTurret.lastTargetSeenTime = Time.realtimeSinceStartup;
                                Effect.server.Run(autoTurret.targetAcquiredEffect.resourcePath, autoTurret.transform.position, Vector3.up);
                                return;
                            }
                        }
                    }
                }
            }

            public void CustomServerTick()
            {
                if (autoTurret.isClient || autoTurret.IsDestroyed)
                    return;
                float timeSinceLastServerTick = (float)autoTurret.timeSinceLastServerTick;
                autoTurret.timeSinceLastServerTick = 0;
                if (!autoTurret.IsOnline())
                    autoTurret.OfflineTick();
                else if (!autoTurret.IsBeingControlled)
                {
                    if (!autoTurret.HasTarget())
                        autoTurret.IdleTick(timeSinceLastServerTick);
                    else
                        CustomTargetTick();
                }
                autoTurret.UpdateFacingToTarget(timeSinceLastServerTick);
                if (autoTurret.totalAmmoDirty && Time.time > autoTurret.nextAmmoCheckTime)
                {
                    autoTurret.UpdateTotalAmmo();
                    autoTurret.totalAmmoDirty = false;
                    autoTurret.nextAmmoCheckTime = Time.time + 0.5f;
                }
            }

            public void CustomTargetTick()
            {
                double nowRT = Time.realtimeSinceStartupAsDouble;
                if (nowRT >= autoTurret.nextVisCheck)
                {
                    autoTurret.nextVisCheck = nowRT + Random.Range(0.2f, 0.3f);
                    autoTurret.targetVisible = CustomObjectVisible(autoTurret.target);
                    if (autoTurret.targetVisible)
                        autoTurret.lastTargetSeenTime = nowRT;
                }
                autoTurret.EnsureReloaded();
                double now = Time.timeAsDouble;
                if (
                    now >= autoTurret.nextShotTime
                    && autoTurret.targetVisible
                    && Mathf.Abs(autoTurret.AngleToTarget(autoTurret.target, autoTurret.currentAmmoGravity != 0f)) < autoTurret.GetMaxAngleForEngagement()
                )
                {
                    BaseProjectile wp = autoTurret.GetAttachedWeapon();
                    if (wp != null)
                    {
                        float dmgMod = 1f;
                        float speedMod = 1f;
                        var projMod = wp.primaryMagazine.ammoType?.GetComponent<ItemModProjectile>();
                        if (projMod != null && projMod.projectileVelocity < 100f)
                            speedMod = 2f;
                        if (wp.primaryMagazine.contents > 0)
                        {
                            autoTurret.FireAttachedGun(autoTurret.AimOffset(autoTurret.target), autoTurret.aimCone, autoTurret.PeacekeeperMode() ? autoTurret.target : null, dmgMod, speedMod);
                            double delay = wp.isSemiAuto ? wp.repeatDelay * 1.5f : wp.repeatDelay;
                            delay = wp.ScaleRepeatDelay((float)delay);
                            autoTurret.nextShotTime = now + delay;
                        }
                        else
                        {
                            autoTurret.nextShotTime = now + 5.0;
                        }
                    }
                    else if (autoTurret.HasFallbackWeapon())
                    {
                        autoTurret.FireGun(autoTurret.AimOffset(autoTurret.target), autoTurret.aimCone, null, autoTurret.target);
                        autoTurret.nextShotTime = now + 0.115;
                    }
                    else if (autoTurret.HasGenericFireable())
                    {
                        autoTurret.AttachedWeapon.ServerUse();
                        autoTurret.nextShotTime = now + 0.115;
                    }
                    else
                    {
                        autoTurret.nextShotTime = now + 1.0;
                    }
                }
                if (
                    autoTurret.target != null
                    && (
                        !IsPlayer(autoTurret.target as BasePlayer)
                        || autoTurret.target.IsDead()
                        || nowRT - autoTurret.lastTargetSeenTime > 3.0
                        || Vector3.Distance(autoTurret.transform.position, autoTurret.target.transform.position) > autoTurret.sightRange
                    )
                )
                {
                    autoTurret.target = null;
                    Effect.server.Run(autoTurret.targetLostEffect.resourcePath, autoTurret.transform.position, Vector3.up);
                }
            }

            private bool CanBeTargeted(BasePlayer player)
            {
                if (!IsPlayer(player))
                    return false;
                if (player.IsDead() || player.IsSleeping() || player.IsWounded())
                    return false;
                if (player.InSafeZone() || player._limitedNetworking)
                    return false;
                return true;
            }

            private void RemoveInterference()
            {
                AutoTurret.interferenceUpdateList.Remove(autoTurret);
                if (autoTurret.HasFlag(BaseEntity.Flags.OnFire))
                    autoTurret.SetFlag(BaseEntity.Flags.OnFire, false);
            }

            private void ManageAmmo()
            {
                const int maxAmmoCapacity = 1000;
                if (!(autoTurret.AttachedWeapon is BaseProjectile projectileWeapon) || projectileWeapon.primaryMagazine?.ammoType == null)
                    return;
                var ammoType = projectileWeapon.primaryMagazine.ammoType;
                int currentAmmoCount = autoTurret.inventory.itemList.Where(item => item.info == ammoType).Sum(item => item.amount);
                if (currentAmmoCount < maxAmmoCapacity)
                {
                    int ammoNeeded = maxAmmoCapacity - currentAmmoCount;
                    Item additionalAmmo = ItemManager.Create(ammoType, ammoNeeded);
                    additionalAmmo?.MoveToContainer(autoTurret.inventory);
                    autoTurret.UpdateTotalAmmo();
                    autoTurret.EnsureReloaded();
                    autoTurret.SendNetworkUpdateImmediate();
                }
            }

            private void Perform360IdleSweep()
            {
                if (!autoTurret.HasTarget())
                {
                    autoTurret.targetAimDir = Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward;
                    autoTurret.aimDir = Vector3.Lerp(autoTurret.aimDir, autoTurret.targetAimDir, Time.deltaTime * 2f);
                    autoTurret.UpdateAiming(Time.deltaTime);
                }
            }

            private void OnDestroy()
            {
                CancelInvoke(nameof(CustomServerTick));
                CancelInvoke(nameof(ScanTargets));
                CancelInvoke(nameof(ManageAmmo));
                CancelInvoke(nameof(Perform360IdleSweep));
                CancelInvoke(nameof(RemoveInterference));
            }
        }

        public class DungeonEventsTrigger : FacepunchBehaviour
        {
            private ActiveDungeon _dungeon;
            private bool rewardSent = false;
            private Rigidbody _rigidbody;
            private Collider _collider;
            private Bounds _bounds;
            private BoxCollider _boxCollider;
            public int TriggerTime = 0;
            public float NightVisionTime = -1;
            private float _emptySince = -1f;
            private bool _npcsReturned = false;

            private void Awake()
            {
                if (_rigidbody != null)
                    DestroyImmediate(_rigidbody);
                gameObject.layer = (int)Layer.Reserved1;
                gameObject.name = "DungeonEventsTrigger";
                _rigidbody = gameObject.AddComponent<Rigidbody>();
                _rigidbody.useGravity = false;
                _rigidbody.isKinematic = true;
                _rigidbody.detectCollisions = true;
                _rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
                _boxCollider = gameObject.GetComponent<BoxCollider>();
            }

            private void OnDestroy()
            {
                CancelInvoke(nameof(UpdateDungeonPlayersUI));
                CancelInvoke(nameof(CheckDungeonClear));
            }

            public void Initialize(ActiveDungeon dungeon)
            {
                _dungeon = dungeon;
                _emptySince = -1f;
                _npcsReturned = false;
                InvokeRepeating(nameof(UpdateDungeonPlayersUI), 1f, 1f);
                InvokeRepeating(nameof(CheckDungeonClear), 5f, 5f);
            }

            private void CheckDungeonClear()
            {
                if (rewardSent || _dungeon == null || !_dungeon.Spawned)
                    return;
                bool allNpcsDead = _instance.AreAllNpcsGone(_dungeon);
                bool allBoxesDead = _instance.AreAllBoxesGone(_dungeon);
                bool allTurretsDead = _instance.AreAllTurretsGone(_dungeon);
                if (!_dungeon.ClearRewardGiven && allNpcsDead && allBoxesDead && allTurretsDead)
                {
                    _dungeon.ClearRewardGiven = true;
                    _instance.RewardDungeonClear(_dungeon);
                    rewardSent = true;
                }
            }

            public void SetPosition(Vector3 position, Vector3 size)
            {
                transform.position = position;
                if (position != Vector3.zero)
                {
                    if (_boxCollider == null)
                    {
                        _boxCollider = gameObject.AddComponent<BoxCollider>();
                        _boxCollider.isTrigger = true;
                    }
                    _boxCollider.size = size;
                    _bounds = _boxCollider.bounds;
                    _collider = _boxCollider;
                }
            }

            private void UpdateDungeonPlayersUI()
            {
                if (_dungeon == null || _instance == null)
                {
                    CancelInvoke(nameof(UpdateDungeonPlayersUI));
                    CancelInvoke(nameof(CheckDungeonClear));
                    return;
                }
                List<ulong> toRemove = new List<ulong>();
                double destructionTimeRemaining = 0;
                if (_dungeon.HasBeenCleared && _dungeon.DestructionCountdownStart.HasValue)
                {
                    double elapsed = (DateTime.UtcNow - _dungeon.DestructionCountdownStart.Value).TotalSeconds;
                    destructionTimeRemaining = _dungeon.DestructionDelay - elapsed;
                }
                foreach (var userID in _dungeon.PlayersInside.ToList())
                {
                    var player = BasePlayer.FindByID(userID);
                    if (player == null || !player.IsConnected || _instance.GetDungeonForPlayer(player) != _dungeon)
                    {
                        toRemove.Add(userID);
                        continue;
                    }
                    _instance.OpenEventUI(player, _dungeon);
                }
                foreach (var userID in toRemove)
                    _dungeon.PlayersInside.Remove(userID);
                if (_dungeon.PlayersInside.Count > 0)
                {
                    _emptySince = -1f;
                    _npcsReturned = false;
                }
                else
                {
                    if (_emptySince < 0f)
                        _emptySince = Time.realtimeSinceStartup;
                    if (!_npcsReturned && (Time.realtimeSinceStartup - _emptySince) >= _instance._config.EmptyDungeonNpcResetSeconds)
                    {
                        _instance.ResetDungeonNpcsToHome(_dungeon);
                        _npcsReturned = true;
                    }
                }
                if (_dungeon.HasBeenCleared && destructionTimeRemaining <= 0)
                {
                    _instance.TeleportPlayersAndDestroyDungeon(_dungeon);
                    CancelInvoke(nameof(UpdateDungeonPlayersUI));
                    CancelInvoke(nameof(CheckDungeonClear));
                }
            }

            private void OnTriggerEnter(Collider col)
            {
                if (col == null || _dungeon == null || _instance == null)
                    return;
                var entity = col.ToBaseEntity();
                if (entity is not BasePlayer player || !player.userID.IsSteamId())
                    return;
                if (!_dungeon.ActivationTime.HasValue)
                    _dungeon.ActivationTime = DateTime.UtcNow;
                _dungeon.PlayersInside.Add(player.userID);
                if (!IsInvoking(nameof(UpdateDungeonPlayersUI)))
                    InvokeRepeating(nameof(UpdateDungeonPlayersUI), 1f, 1f);
                _emptySince = -1f;
                _npcsReturned = false;
                _instance.UpdateTime(player, TriggerTime);
                _instance.OpenEventUI(player, _dungeon);
                if (player.GetComponent<PlayerDungeonChecker>() == null)
                    player.gameObject.AddComponent<PlayerDungeonChecker>().Initialize(player, NightVisionTime);
            }

            private void OnTriggerExit(Collider col)
            {
                if (col == null || _dungeon == null || _instance == null)
                    return;
                var entity = col.ToBaseEntity();
                if (entity == null)
                    return;
                if (entity is BasePlayer player && player.userID.IsSteamId())
                {
                    _dungeon.PlayersInside.Remove(player.userID);
                    _instance.UpdateTime(player, -1);
                    _instance.DestroyEventUi(player);
                    var checker = player.GetComponent<PlayerDungeonChecker>();
                    if (checker != null)
                        Destroy(checker);
                    if (_instance.NightVision != null && NightVisionTime > -1)
                    {
                        _instance.NightVision.CallHook("LockPlayerTime", player, NightVisionTime);
                    }
                }
            }
        }

        public class PlayerDungeonChecker : MonoBehaviour
        {
            private BasePlayer player;
            public float NightVisionTime = -1;
            private float nextAdjust;

            public void Initialize(BasePlayer p, float nightVisionTime)
            {
                player = p;
                NightVisionTime = nightVisionTime;
                nextAdjust = Time.time + 1f;
                InvokeRepeating(nameof(CheckDungeonStatus), 5f, 5f);
                _instance._wasInDungeonPlayers.Add(player);
            }

            private void FixedUpdate()
            {
                if (player == null || !player.IsConnected || !player.IsAlive())
                    return;
                if (Time.time >= nextAdjust)
                {
                    nextAdjust = Time.time + 1f;
                    float t = player.metabolism.temperature.value;
                    if (t < 19f || t > 31f)
                        player.metabolism.temperature.value = 25f;
                }
            }

            private void CheckDungeonStatus()
            {
                if (player == null || !player.IsConnected)
                {
                    Destroy(this);
                    return;
                }
                if (_instance.GetDungeonForPlayer(player) == null)
                {
                    _instance.UpdateTime(player, -1);
                    _instance.DestroyEventUi(player);
                    if (_instance.NightVision != null && NightVisionTime > -1)
                        _instance.NightVision.CallHook("LockPlayerTime", player, NightVisionTime);
                    Destroy(this);
                }
            }

            private void OnDestroy()
            {
                CancelInvoke(nameof(CheckDungeonStatus));
                _instance.timer.Once(4f, () => _instance._wasInDungeonPlayers.Remove(player));
            }
        }
        #endregion

        #region Utilities / Helpers
        private void RegisterCommands()
        {
            if (_config == null)
                return;
            _config.ChatCommands ??= new ChatCommandConfig();
            _config.ConsoleCommands ??= new ConsoleCommandConfig();
            TryAddChat(_config.ChatCommands.BuyDungeon, nameof(BuyDungeonChatCommand));
            TryAddChat(_config.ChatCommands.CreateDungeon, nameof(CreateDungeonChatCommand));
            TryAddChat(_config.ChatCommands.ReloadConfig, nameof(ReloadConfigChatCommand));
            TryAddChat(_config.ChatCommands.TogglePurchases, nameof(TogglePurchasesChatCommand));
            TryAddChat(_config.ChatCommands.RemoveInactiveDungeons, nameof(RemoveInactiveDungeonsChatCommand));
            TryAddChat(_config.ChatCommands.RemoveAllDungeons, nameof(RemoveAllDungeonsChatCommand));
            TryAddChat(_config.ChatCommands.RemoveNearest, nameof(RemoveNearestDungeonChatCommand));
            TryAddChat(_config.ChatCommands.RemoveOwnDungeon, nameof(RemoveOwnDungeonChatCommand));
            TryAddChat(_config.ChatCommands.ForceRemoveAll, nameof(ForceRemoveAllDungeonsChatCommand));
            TryAddConsole(_config.ConsoleCommands.BuyDungeon, nameof(BuyDungeonCommand));
            TryAddConsole(_config.ConsoleCommands.SpawnRandomDungeon, nameof(SpawnRandomDungeonCommand));
            TryAddConsole(_config.ConsoleCommands.SpawnFixedDungeon, nameof(SpawnFixedDungeonCommand));
        }

        private void TryAddChat(string command, string handler)
        {
            if (!string.IsNullOrWhiteSpace(command))
                cmd.AddChatCommand(command, this, handler);
        }

        private void TryAddConsole(string command, string handler)
        {
            if (!string.IsNullOrWhiteSpace(command))
                cmd.AddConsoleCommand(command, this, handler);
        }

        public void UpdateTime(BasePlayer player, int time)
        {
            if (!player.IsConnected)
                return;
            if (player.IsAdmin)
            {
                player.SendConsoleCommand("admintime", time);
            }
            else if (!player.IsFlying)
            {
                player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                player.SendNetworkUpdateImmediate();
                player.SendConsoleCommand("admintime", time);
                player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
            }
            player.SendNetworkUpdateImmediate();
        }

        private string FormatTime(double totalSeconds)
        {
            if (totalSeconds < 0)
                totalSeconds = 0;
            TimeSpan ts = TimeSpan.FromSeconds(totalSeconds);
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        private void DrawLines(ActiveDungeon activeDungeon, BasePlayer player)
        {
            if (activeDungeon == null || player == null)
                return;
            var bounds = activeDungeon.DungeonBounds;
            var center = bounds.center;
            var ex = bounds.extents.x;
            var ey = bounds.extents.y;
            var ez = bounds.extents.z;
            Vector3 c000 = center + new Vector3(-ex, -ey, -ez);
            Vector3 c001 = center + new Vector3(-ex, -ey, ez);
            Vector3 c010 = center + new Vector3(-ex, ey, -ez);
            Vector3 c011 = center + new Vector3(-ex, ey, ez);
            Vector3 c100 = center + new Vector3(ex, -ey, -ez);
            Vector3 c101 = center + new Vector3(ex, -ey, ez);
            Vector3 c110 = center + new Vector3(ex, ey, -ez);
            Vector3 c111 = center + new Vector3(ex, ey, ez);
            float duration = 120f;
            Color color = Color.red;
            string cmd = "ddraw.line";
            player.SendConsoleCommand(cmd, duration, color, c000, c001);
            player.SendConsoleCommand(cmd, duration, color, c001, c101);
            player.SendConsoleCommand(cmd, duration, color, c101, c100);
            player.SendConsoleCommand(cmd, duration, color, c100, c000);
            player.SendConsoleCommand(cmd, duration, color, c010, c011);
            player.SendConsoleCommand(cmd, duration, color, c011, c111);
            player.SendConsoleCommand(cmd, duration, color, c111, c110);
            player.SendConsoleCommand(cmd, duration, color, c110, c010);
            player.SendConsoleCommand(cmd, duration, color, c000, c010);
            player.SendConsoleCommand(cmd, duration, color, c001, c011);
            player.SendConsoleCommand(cmd, duration, color, c100, c110);
            player.SendConsoleCommand(cmd, duration, color, c101, c111);
        }

        private static bool TryParseHexColor(string hex, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(hex))
                return false;
            var h = hex.Trim().TrimStart('#');
            byte r,
                g,
                b,
                a = 255;
            if (h.Length == 6)
            {
                if (
                    byte.TryParse(h.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out r)
                    && byte.TryParse(h.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out g)
                    && byte.TryParse(h.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out b)
                )
                {
                    color = new Color32(r, g, b, a);
                    return true;
                }
            }
            else if (h.Length == 8)
            {
                if (
                    byte.TryParse(h.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out a)
                    && byte.TryParse(h.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out r)
                    && byte.TryParse(h.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out g)
                    && byte.TryParse(h.Substring(6, 2), System.Globalization.NumberStyles.HexNumber, null, out b)
                )
                {
                    color = new Color32(r, g, b, a);
                    return true;
                }
            }
            return false;
        }

        private bool IsAdmin(BasePlayer player) => player != null && player.IsAdmin;

        private static bool IsPlayer(BasePlayer targetPlayer) => targetPlayer != null && targetPlayer.userID.IsSteamId();

        private bool IsDungeonEntity(BaseEntity entity)
        {
            if (entity == null)
                return false;
            return _activeDungeons.Any(d => d.Entities.Contains(entity) || d.Npcs.Contains(entity));
        }

        private void LogDebug(string message, int level = 0)
        {
            if (string.IsNullOrEmpty(message))
                return;
            string formattedMessage = $"[{Name}] {message}";
            switch (level)
            {
                case 1:
                    if (_config.EnableWarningLogs)
                        Debug.LogWarning(formattedMessage);
                    break;
                case 2:
                    if (_config.EnableWarningLogs)
                        Debug.LogError(formattedMessage);
                    break;
                default:
                    if (_config.EnableDebugLogs)
                        Debug.Log(formattedMessage);
                    break;
            }
        }

        private string Msg(string userId, string key, params object[] args)
        {
            return string.Format(lang.GetMessage(key, this, userId), args);
        }

        private void NotifyPlayer(BasePlayer player, string key, int type, params object[] args)
        {
            if (player == null || !player.IsConnected)
                return;
            var msgString = Msg(player.UserIDString, key, args);
            if (_config.UseNotify && Notify != null)
            {
                Interface.Oxide.CallHook("SendNotify", player.UserIDString, type, msgString);
            }
            else
            {
                player.ChatMessage(msgString);
            }
            if (_config.EnableShowToast)
            {
                player.ShowToast(GameTip.Styles.Blue_Normal, msgString);
            }
        }

        private void NotifyPlayersOfNewDungeon(ActiveDungeon dungeon)
        {
            foreach (var p in BasePlayer.activePlayerList)
            {
                if (dungeon.OwnerName == null)
                    NotifyPlayer(p, "DungeonAppeared", 0, dungeon.TierConfig.MapName, dungeon.Grid);
                else if (_config.DungeonSpawn.EnableMapMarkersBuy || p.displayName == dungeon.OwnerName)
                    NotifyPlayer(p, "DungeonPurchased", 0, dungeon.TierConfig.MapName, dungeon.OwnerName, dungeon.Grid);
            }
        }

        private void NotifyPlayerOnRemoval(BasePlayer player, IEnumerable<ActiveDungeon> removedDungeons)
        {
            var list = removedDungeons.ToList();
            if (!list.Any())
            {
                NotifyPlayer(player, "NoInactiveDungeonsToRemove", 0);
                return;
            }
            foreach (var dungeon in list)
            {
                NotifyPlayer(player, "DungeonRemovedDetail", 0, dungeon.TierName, dungeon.Grid);
            }
        }

        private (BuildingGrade.Enum grade, ulong skinID) GetGradeAndSkin(int selection)
        {
            switch (selection)
            {
                case 1:
                    return (BuildingGrade.Enum.Wood, 0);
                case 2:
                    return (BuildingGrade.Enum.Wood, 10232);
                case 3:
                    return (BuildingGrade.Enum.Wood, 2);
                case 4:
                    return (BuildingGrade.Enum.Stone, 0);
                case 5:
                    return (BuildingGrade.Enum.Stone, 10220);
                case 6:
                    return (BuildingGrade.Enum.Stone, 10223);
                case 7:
                    return (BuildingGrade.Enum.Stone, 10225);
                case 8:
                    return (BuildingGrade.Enum.Metal, 0);
                case 9:
                    return (BuildingGrade.Enum.Metal, 10221);
                case 10:
                    return (BuildingGrade.Enum.TopTier, 0);
                default:
                    return (BuildingGrade.Enum.Stone, 0);
            }
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "Default";
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(s.Where(c => !invalid.Contains(c)).ToArray());
            return string.IsNullOrWhiteSpace(cleaned) ? "Default" : cleaned.Replace(' ', '_');
        }

        private static string ToUiColorString(Color c)
        {
            return $"{c.r:F2} {c.g:F2} {c.b:F2} 1";
        }

        private (Color color, string colorString) GetTierColor(string name)
        {
            if (string.IsNullOrEmpty(name))
                name = "Tier";
            int hash = name.Aggregate(0, (a, c) => unchecked(a * 31 + c));
            float h = (Mathf.Abs(hash) % 360) / 360f;
            float s = 0.6f;
            float v = 0.9f;
            Color col = Color.HSVToRGB(h, s, v);
            string sCol = $"{col.r:F2} {col.g:F2} {col.b:F2} 1";
            return (col, sCol);
        }

        private bool TryReadRawConfig(out JObject raw)
        {
            raw = null;
            try
            {
                raw = Config.ReadObject<JObject>();
                return raw != null;
            }
            catch
            {
                return false;
            }
        }

        private bool TryParseConfigVersion(JObject raw, out VersionNumber version)
        {
            version = new VersionNumber(0, 0, 0);
            if (raw == null)
                return false;
            var v = raw["Version"];
            if (v == null)
                return false;
            if (v.Type == JTokenType.Object)
            {
                var ma = 0;
                var mi = 0;
                var pa = 0;
                int.TryParse(v["Major"] != null ? v["Major"].ToString() : "0", out ma);
                int.TryParse(v["Minor"] != null ? v["Minor"].ToString() : "0", out mi);
                int.TryParse(v["Patch"] != null ? v["Patch"].ToString() : "0", out pa);
                version = new VersionNumber(ma, mi, pa);
                return true;
            }
            if (v.Type == JTokenType.String)
            {
                var parts = v.ToString().Split('.');
                var ma = 0;
                var mi = 0;
                var pa = 0;
                if (parts.Length > 0)
                    int.TryParse(parts[0], out ma);
                if (parts.Length > 1)
                    int.TryParse(parts[1], out mi);
                if (parts.Length > 2)
                    int.TryParse(parts[2], out pa);
                version = new VersionNumber(ma, mi, pa);
                return true;
            }
            return false;
        }

        private bool MigrateTiersObjectToArray(JObject root)
        {
            var tiers = root?["Tiers"];
            if (tiers == null || tiers.Type != JTokenType.Object)
                return false;
            var obj = (JObject)tiers;
            var arr = new JArray();
            foreach (var prop in obj.Properties())
            {
                var item = prop.Value as JObject ?? new JObject();
                if (item["Tier Name"] == null)
                    item["Tier Name"] = prop.Name;
                var enabledToken = item["Enabled"];
                if (enabledToken != null && enabledToken.Type == JTokenType.Boolean)
                {
                    var enabled = enabledToken.Value<bool>();
                    if (item["Enable Auto Spawn"] == null)
                        item["Enable Auto Spawn"] = enabled;
                    if (item["Enable Buy"] == null)
                        item["Enable Buy"] = enabled;
                    item.Remove("Enabled");
                }
                arr.Add(item);
            }
            root["Tiers"] = arr;
            return true;
        }

        private ScientistNPC CreateScientist(Vector3 pos, Quaternion rot)
        {
            BaseEntity ent = GameManager.server.CreateEntity(ScientistPrefab, pos, rot);
            if (ent == null)
                return null;
            var npc = ent as ScientistNPC;
            if (npc == null)
            {
                ent.Kill();
                return null;
            }
            npc.OwnerID = OwnerID;
            npc.Spawn();
            return npc;
        }

        private void ApplyNpcConfig(ScientistNPC npc, NpcConfig cfg)
        {
            if (npc == null || cfg == null)
                return;
            try
            {
                if (!string.IsNullOrEmpty(cfg.Name))
                {
                    npc.displayName = cfg.Name;
                    npc._name = cfg.Name;
                    npc.SendNetworkUpdateImmediate();
                }
            }
            catch { }
            try
            {
                npc.startHealth = cfg.Health;
                npc.InitializeHealth(cfg.Health, cfg.Health);
            }
            catch { }
            try
            {
                npc.inventory?.containerWear?.Clear();
                npc.inventory?.containerBelt?.Clear();
                npc.inventory?.containerMain?.Clear();
            }
            catch { }
            if (cfg.WearItems?.Items != null)
            {
                foreach (var w in cfg.WearItems.Items)
                {
                    var def = ItemManager.FindItemDefinition(w.ShortName);
                    if (def == null)
                        continue;
                    var item = ItemManager.Create(def, 1, w.SkinID);
                    item?.MoveToContainer(npc.inventory.containerWear);
                }
            }
            if (cfg.BeltItems?.Items != null)
            {
                foreach (var b in cfg.BeltItems.Items)
                {
                    if (b.Amount <= 0)
                        continue;
                    var def = ItemManager.FindItemDefinition(b.ShortName);
                    if (def == null)
                        continue;
                    var item = ItemManager.Create(def, b.Amount, b.SkinID);
                    if (item == null)
                        continue;
                    if (b.Mods != null && b.Mods.Count > 0 && item.contents != null)
                    {
                        foreach (var modShort in b.Mods)
                        {
                            var modDef = ItemManager.FindItemDefinition(modShort);
                            if (modDef == null)
                                continue;
                            var modItem = ItemManager.Create(modDef, 1);
                            modItem?.MoveToContainer(item.contents);
                        }
                    }
                    item.MoveToContainer(npc.inventory.containerBelt);
                }
            }
            try
            {
                npc.SendNetworkUpdateImmediate();
            }
            catch { }
        }
        #endregion

        #region Economy
        private bool TryGetBuyCooldown(ulong userId, string tierName, out DateTime until)
        {
            until = DateTime.MinValue;
            if (string.IsNullOrEmpty(tierName))
                return false;
            if (!_buyCooldowns.TryGetValue(userId, out var perTier))
                return false;
            if (perTier == null)
                return false;
            return perTier.TryGetValue(tierName, out until);
        }

        private void SetBuyCooldown(ulong userId, string tierName, int seconds)
        {
            if (string.IsNullOrEmpty(tierName) || seconds <= 0)
                return;
            if (!_buyCooldowns.TryGetValue(userId, out var perTier) || perTier == null)
            {
                perTier = new Dictionary<string, DateTime>();
                _buyCooldowns[userId] = perTier;
            }
            perTier[tierName] = DateTime.UtcNow.AddSeconds(seconds);
        }

        private void ProcessBuyDungeon(BasePlayer player, string tierName)
        {
            var tier = FindTierByName(tierName);
            if (tier == null || !tier.EnableBuy)
            {
                NotifyPlayer(player, "InvalidTierMessage", 0, tierName, GetAvailableTiers());
                return;
            }
            if (TryGetBuyCooldown(player.userID, tier.TierName, out DateTime until) && DateTime.UtcNow < until)
            {
                int remaining = (int)(until - DateTime.UtcNow).TotalSeconds;
                NotifyPlayer(player, "DungeonCooldownMessage", 0, remaining);
                return;
            }
            if (_activeDungeons.Any(d => d.OwnerID == player.userID) || _dungeonQueue.Any(r => r.Player?.userID == player.userID) || _dungeonFailedBuyQueue.Any(r => r.Player?.userID == player.userID))
            {
                NotifyPlayer(player, "AlreadyOwnsDungeonMessage", 0);
                return;
            }
            int activeBuySameTier = _activeDungeons.Count(d => d.Origin == DungeonOrigin.Buy && d.TierConfig == tier);
            if (tier.MaxActiveBuy > 0 && activeBuySameTier >= tier.MaxActiveBuy)
            {
                NotifyPlayer(player, "MaxActiveDungeonsReached", 0);
                return;
            }
            int cost = tier.BuyCost;
            if (cost > 0)
            {
                if (_config.EconomyPlugin == 3)
                {
                    if (!CheckAndRemoveBuyItems(player, cost))
                    {
                        NotifyPlayer(player, "InsufficientFundsMessage", 0, tierName, cost);
                        _playersBuying.Remove(player);
                        return;
                    }
                }
                else
                {
                    if (!CheckBalance(player, cost))
                    {
                        NotifyPlayer(player, "InsufficientFundsMessage", 0, tierName, cost);
                        _playersBuying.Remove(player);
                        return;
                    }
                    if (!WithdrawFunds(player, cost))
                    {
                        NotifyPlayer(player, "WithdrawFailedMessage", 0, tierName);
                        _playersBuying.Remove(player);
                        return;
                    }
                }
            }
            EnqueueDungeonRequest(player, tier, true);
            NotifyPlayer(player, "PurchaseSuccessMessage", 0, tier.MapName);
        }

        private bool CheckAndRemoveBuyItems(BasePlayer player, int cost)
        {
            var requiredItems = new List<ResourceItem>
            {
                new ResourceItem { Shortname = _config.GetEconomyItemName(), Amount = cost },
            };
            var requiredItemMap = requiredItems.ToDictionary(item => item.Shortname, item => item.Amount);
            var allItems = Pool.Get<List<Item>>();
            try
            {
                player.inventory.GetAllItems(allItems);
                if (!HasAllRequiredItems(allItems, requiredItemMap))
                    return false;
                RemoveItems(allItems, player, requiredItemMap);
                return true;
            }
            finally
            {
                Pool.FreeList(ref allItems);
            }
        }

        private bool HasAllRequiredItems(List<Item> allItems, Dictionary<string, int> requiredItemMap)
        {
            var playerItemMap = allItems.GroupBy(item => item.info.shortname).ToDictionary(g => g.Key, g => g.Sum(i => i.amount));
            foreach (var req in requiredItemMap)
            {
                if (!playerItemMap.TryGetValue(req.Key, out int count) || count < req.Value)
                {
                    return false;
                }
            }
            return true;
        }

        private void RemoveItems(List<Item> allItems, BasePlayer player, Dictionary<string, int> requiredItemMap)
        {
            foreach (var req in requiredItemMap)
            {
                int amountToRemove = req.Value;
                var items = allItems.Where(x => x.info.shortname == req.Key).ToList();
                foreach (var item in items)
                {
                    if (amountToRemove <= 0)
                        break;
                    if (item.amount <= amountToRemove)
                    {
                        amountToRemove -= item.amount;
                        player.inventory.Take(null, item.info.itemid, item.amount);
                        item.Remove();
                    }
                    else
                    {
                        item.amount -= amountToRemove;
                        item.MarkDirty();
                        amountToRemove = 0;
                    }
                }
            }
        }

        private bool CheckBalance(BasePlayer player, int cost)
        {
            return _config.EconomyPlugin switch
            {
                1 => CheckEconomicsBalance(player, cost),
                2 => CheckServerRewardsBalance(player, cost),
                _ => false,
            };
        }

        private bool WithdrawFunds(BasePlayer player, int cost)
        {
            return _config.EconomyPlugin switch
            {
                1 => WithdrawEconomicsFunds(player, cost),
                2 => WithdrawServerRewardsFunds(player, cost),
                _ => false,
            };
        }

        private bool CheckEconomicsBalance(BasePlayer player, int cost)
        {
            if (Economics == null)
            {
                PrintWarning("Economics plugin is not loaded.");
                return false;
            }
            object balanceObj = Economics.CallHook("Balance", player.userID);
            if (balanceObj == null || !double.TryParse(balanceObj.ToString(), out double balance))
                return false;
            return balance >= cost;
        }

        private bool CheckServerRewardsBalance(BasePlayer player, int cost)
        {
            if (ServerRewards == null)
            {
                PrintWarning("ServerRewards plugin is not loaded.");
                return false;
            }
            object balanceObj = ServerRewards.CallHook("CheckPoints", player.userID);
            if (balanceObj == null || !int.TryParse(balanceObj.ToString(), out int balance))
                return false;
            return balance >= cost;
        }

        private bool WithdrawEconomicsFunds(BasePlayer player, int cost)
        {
            if (Economics == null)
            {
                PrintWarning("Economics plugin is not loaded.");
                return false;
            }
            object result = Economics.CallHook("Withdraw", player.userID, (double)cost);
            return result != null && Convert.ToBoolean(result);
        }

        private bool WithdrawServerRewardsFunds(BasePlayer player, int cost)
        {
            if (ServerRewards == null)
            {
                PrintWarning("ServerRewards plugin is not loaded.");
                return false;
            }
            object result = ServerRewards.CallHook("TakePoints", player.userID, cost);
            return result != null && Convert.ToBoolean(result);
        }
        #endregion

        #region UI
        private void OpenEventUI(BasePlayer player, ActiveDungeon dungeon)
        {
            if (player == null || !player.IsConnected)
                return;
            double timeLeft;
            string counterText;
            if (dungeon.HasBeenCleared && dungeon.DestructionCountdownStart.HasValue)
            {
                double elapsed = (DateTime.UtcNow - dungeon.DestructionCountdownStart.Value).TotalSeconds;
                timeLeft = dungeon.DestructionDelay - elapsed;
                if (timeLeft < 0)
                    timeLeft = 0;
                counterText = FormatTime(timeLeft);
            }
            else
            {
                double elapsed = dungeon.ActivationTime.HasValue ? (DateTime.UtcNow - dungeon.ActivationTime.Value).TotalSeconds : 0;
                timeLeft = dungeon.TierConfig.MaxDungeonLifetime - elapsed;
                if (timeLeft < 0)
                    timeLeft = 0;
                counterText = FormatTime(timeLeft);
            }
            int npcCount = dungeon.Npcs.Count(n => n != null && !n.IsDestroyed);
            int boxCount = dungeon.Entities.OfType<StorageContainer>().Count(e => e.IsValid() && !e.IsDestroyed);
            int turretCount = dungeon.Entities.OfType<AutoTurret>().Count(t => t.IsValid() && !t.IsDestroyed);
            CuiHelper.DestroyUi(player, UiEventPanel);
            var infos = new List<IconInfo>
            {
                new IconInfo
                {
                    Sprite = "assets/icons/stopwatch.png",
                    Color = dungeon.HasBeenCleared ? "1 1 0 1" : "1 1 1 1",
                    Text = counterText,
                    X = 11,
                    Y = 4,
                    Width = 18,
                    Height = 18,
                },
                new IconInfo
                {
                    Sprite = "assets/icons/clan.png",
                    Color = dungeon.HasBeenCleared ? "1 1 0 1" : "1 1 1 1",
                    Text = npcCount.ToString(),
                    X = 9,
                    Y = 1.8f,
                    Width = 24,
                    Height = 24,
                },
                new IconInfo
                {
                    Sprite = "assets/icons/loot.png",
                    Color = dungeon.HasBeenCleared ? "1 1 0 1" : "1 1 1 1",
                    Text = boxCount.ToString(),
                    X = 9,
                    Y = 1,
                    Width = 24,
                    Height = 24,
                },
                new IconInfo
                {
                    Sprite = "assets/prefabs/npc/autoturret/autoturret.png",
                    Color = dungeon.HasBeenCleared ? "1 1 0 1" : "1 1 1 1",
                    Text = turretCount.ToString(),
                    X = 9,
                    Y = 4,
                    Width = 21,
                    Height = 17,
                },
            };
            CreateEventUI(player, dungeon, infos);
        }

        private void CreateEventUI(BasePlayer player, ActiveDungeon dungeon, List<IconInfo> infos)
        {
            string color = dungeon.HasBeenCleared ? "0.6 0.6 0 1" : "0.6 0 0 1";
            float panelWidth = 85f;
            float panelHeight = 25f;
            float panelSpacing = 95f;
            float totalWidth = panelWidth + panelSpacing * (infos.Count - 1);
            float border = totalWidth / 2f;
            CuiHelper.DestroyUi(player, UiEventPanel);
            var container = new CuiElementContainer();
            container.Add(
                new CuiPanel
                {
                    Image = { Color = "0 0 0 0" },
                    RectTransform =
                    {
                        AnchorMin = "0.5 1",
                        AnchorMax = "0.5 1",
                        OffsetMin = $"-{border} -56",
                        OffsetMax = $"{border} -36",
                    },
                    CursorEnabled = false,
                },
                "Under",
                UiEventPanel
            );
            int i = 0;
            foreach (var info in infos)
            {
                i++;
                float panelX = panelSpacing * (i - 1);
                container.Add(
                    new CuiElement
                    {
                        Name = $"Info_{i}_Panel",
                        Parent = UiEventPanel,
                        Components =
                        {
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0 0",
                                AnchorMax = "0 0",
                                OffsetMin = $"{panelX} 0",
                                OffsetMax = $"{panelX + panelWidth} {panelHeight}",
                            },
                        },
                    }
                );
                container.Add(
                    new CuiElement
                    {
                        Name = $"Info_{i}_Red",
                        Parent = $"Info_{i}_Panel",
                        Components =
                        {
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" },
                            new CuiImageComponent { Color = color, Material = "assets/content/ui/namefontmaterial.mat" },
                        },
                    }
                );
                container.Add(
                    new CuiElement
                    {
                        Name = $"Info_{i}_Sprite",
                        Parent = $"Info_{i}_Panel",
                        Components =
                        {
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" },
                            new CuiImageComponent { Color = "0 0 0 0.9", Sprite = "assets/content/ui/ui.background.transparent.radial.psd" },
                        },
                    }
                );
                container.Add(
                    new CuiElement
                    {
                        Name = $"Info_{i}_Icon",
                        Parent = $"Info_{i}_Panel",
                        Components =
                        {
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0 0",
                                AnchorMax = "0 0",
                                OffsetMin = $"{info.X} {info.Y}",
                                OffsetMax = $"{info.X + info.Width} {info.Y + info.Height}",
                            },
                            new CuiImageComponent
                            {
                                Sprite = info.Sprite,
                                Color = info.Color,
                                Material = "assets/icons/iconmaterial.mat",
                            },
                        },
                    }
                );
                container.Add(
                    new CuiLabel
                    {
                        Text =
                        {
                            Text = info.Text,
                            FontSize = 10,
                            Align = TextAnchor.MiddleCenter,
                            Color = "1 1 1 1",
                        },
                        RectTransform =
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "0 0",
                            OffsetMin = "20 6",
                            OffsetMax = "100 20",
                        },
                    },
                    $"Info_{i}_Panel",
                    $"Info_{i}_Text"
                );
            }
            CuiHelper.AddUi(player, container);
        }

        private void OpenBuyDungeonGUI(BasePlayer player)
        {
            if (player == null)
                return;
            var elements = new CuiElementContainer();
            elements.Add(
                new CuiPanel
                {
                    Image = { Color = "0.1 0.1 0.1 0.8", Material = "assets/content/ui/uibackgroundblur.mat" },
                    RectTransform = { AnchorMin = "0.25 0.2", AnchorMax = "0.75 0.8" },
                    CursorEnabled = true,
                },
                "Overlay",
                UiMainPanel
            );
            elements.Add(
                new CuiLabel
                {
                    Text =
                    {
                        Text = Msg(player.UserIDString, "DungeonBuyTitle"),
                        FontSize = 22,
                        Align = TextAnchor.MiddleCenter,
                        Color = "1 1 1 1",
                    },
                    RectTransform = { AnchorMin = "0 0.92", AnchorMax = "1 1" },
                },
                UiMainPanel,
                "DungeonBuyTitle"
            );
            var visibleTiers = new List<KeyValuePair<DungeonTierConfig, TierInfo>>();
            foreach (var kvp in _tierInfoMap)
            {
                if (kvp.Key != null && kvp.Key.EnableBuy)
                    visibleTiers.Add(kvp);
            }
            const int rowH = 40;
            const int gap = 4;
            int totalH = visibleTiers.Count * (rowH + gap);
            const int viewportH = 352;
            bool needScroll = totalH > viewportH;
            string body = UiMainPanel + "_viewport";
            elements.Add(new CuiPanel { Image = { Color = "0 0 0 0" }, RectTransform = { AnchorMin = "0.05 0.08", AnchorMax = "0.95 0.88" } }, UiMainPanel, body);
            string scroll = body + "_scroll";
            var scrollEl = new CuiElement
            {
                Parent = body,
                Name = scroll,
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0" },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1",
                        OffsetMin = "0 0",
                        OffsetMax = "0 0",
                    },
                },
            };
            if (needScroll)
            {
                scrollEl.Components.Add(
                    new CuiScrollViewComponent
                    {
                        ContentTransform = new CuiRectTransform
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "1 1",
                            OffsetMin = $"0 -{totalH}",
                            OffsetMax = "-16 0",
                        },
                        Vertical = true,
                        Horizontal = false,
                        ScrollSensitivity = 10f,
                        Elasticity = 0.05f,
                        VerticalScrollbar = new CuiScrollbar { HandleColor = "1 1 1 1", Size = 8 },
                    }
                );
            }
            elements.Add(scrollEl);
            int y = 0;
            int index = 0;
            foreach (var kvp in visibleTiers)
            {
                var tierConfig = kvp.Key;
                var tierInfo = kvp.Value;
                string rowId = $"{scroll}_row_{index}";
                elements.Add(
                    new CuiPanel
                    {
                        Image = { Color = "0.20 0.20 0.20 0.92", Material = "assets/content/ui/namefontmaterial.mat" },
                        RectTransform =
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "1 1",
                            OffsetMin = $"0 {-y - rowH}",
                            OffsetMax = $"0 {-y}",
                        },
                    },
                    scroll,
                    rowId
                );
                elements.Add(
                    new CuiButton
                    {
                        Button =
                        {
                            Color = tierInfo.ColorString,
                            Command = $"{_config?.ConsoleCommands?.BuyDungeon ?? "buydungeon"} {tierInfo.Name} {player.UserIDString}",
                            Sprite = "assets/content/ui/ui.background.transparent.radial.psd",
                            Close = UiMainPanel,
                        },
                        Text =
                        {
                            Text = Msg(player.UserIDString, "BuyButtonWithName", tierInfo.MapName, tierInfo.BuyCost, _config.GetEconomyDisplayName()),
                            FontSize = 18,
                            Align = TextAnchor.MiddleCenter,
                            Color = "1 1 1 1",
                        },
                        RectTransform =
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 1",
                            OffsetMin = "4 4",
                            OffsetMax = "-4 -4",
                        },
                    },
                    rowId
                );
                y += rowH + gap;
                index++;
            }
            elements.Add(
                new CuiButton
                {
                    Button =
                    {
                        Color = "1 0 0 1",
                        Command = UiCommandClose,
                        Close = UiMainPanel,
                    },
                    RectTransform = { AnchorMin = "0.965 0.95", AnchorMax = "1 1" },
                    Text =
                    {
                        Text = "✖",
                        FontSize = 14,
                        Align = TextAnchor.MiddleCenter,
                        Color = "1 1 1 1",
                    },
                },
                UiMainPanel
            );
            CuiHelper.AddUi(player, elements);
        }

        private void DestroyUi(BasePlayer player)
        {
            if (player == null)
                return;
            CuiHelper.DestroyUi(player, UiMainPanel);
        }

        private void DestroyEventUi(BasePlayer player)
        {
            if (player == null)
                return;
            CuiHelper.DestroyUi(player, UiEventPanel);
        }

        private void DestroyAllUis()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyUi(player);
                DestroyEventUi(player);
            }
        }
        #endregion

        #region Dungeon Spawn
        private void SaveDungeonEntities(string dungeonId, List<ulong> entityIds)
        {
            var file = Interface.Oxide.DataFileSystem.GetFile($"{Name}_Cache_Data/{dungeonId}");
            file.WriteObject(entityIds);
        }

        private List<ulong> LoadDungeonEntities(string dungeonId)
        {
            var file = Interface.Oxide.DataFileSystem.GetFile($"{Name}_Cache_Data/{dungeonId}");
            return file.ReadObject<List<ulong>>() ?? new List<ulong>();
        }

        private void DeleteDungeonEntitiesFile(string dungeonId)
        {
            string folder = Path.Combine(Interface.Oxide.DataFileSystem.Directory, $"{Name}_Cache_Data");
            if (!Directory.Exists(folder))
                return;
            string filePath = Path.Combine(folder, $"{dungeonId}.json");
            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch (Exception ex)
                {
                    PrintError("Error deleting dungeon file: " + ex.Message);
                }
            }
        }

        private void RegisterSpawnedEntityForDungeon(BaseEntity entity, string dungeonId)
        {
            if (entity != null && entity.net != null)
            {
                List<ulong> ids = LoadDungeonEntities(dungeonId);
                ids.Add(entity.net.ID.Value);
                SaveDungeonEntities(dungeonId, ids);
            }
        }

        private void RemoveEntitiesForDungeon(string dungeonId)
        {
            List<ulong> ids = LoadDungeonEntities(dungeonId);
            foreach (ulong id in ids)
            {
                BaseEntity entity = BaseNetworkable.serverEntities.Find(new NetworkableId(id)) as BaseEntity;
                if (entity != null && !entity.IsDestroyed)
                    entity.Kill();
            }
            DeleteDungeonEntitiesFile(dungeonId);
        }

        private void RemoveLeftoverDungeonFiles()
        {
            string folder = Path.Combine(Interface.Oxide.DataFileSystem.Directory, $"{Name}_Cache_Data");
            if (!Directory.Exists(folder))
                return;
            foreach (var file in Directory.GetFiles(folder, "*.json"))
            {
                try
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    RemoveEntitiesForDungeon(fileName);
                }
                catch (Exception ex)
                {
                    PrintError("Error removing leftover dungeon file: " + ex.Message);
                }
            }
            ServerMgr.Instance.StartCoroutine(CleanupExistingEntities());
        }

        private Vector3 GetCenterFoundationPositionNearPlayer(BasePlayer player, float portalHeight)
        {
            const float searchRadius = 8f;
            const float minDistance = 2f;
            const float playerClearance = 1.5f;
            Vector3 selected = Vector3.zero;
            float bestDist = float.MaxValue;
            List<BuildingBlock> blocks = Pool.GetList<BuildingBlock>();
            Vis.Entities(player.transform.position, searchRadius, blocks, Layers.Mask.Construction);
            foreach (var block in blocks)
            {
                if (block == null)
                    continue;
                if (!block.PrefabName.Contains("foundation"))
                    continue;
                float dist = Vector3.Distance(block.transform.position, player.transform.position);
                if (dist < minDistance)
                    continue;
                List<BasePlayer> nearPlayers = Pool.GetList<BasePlayer>();
                Vis.Entities(block.transform.position, playerClearance, nearPlayers, Layers.Mask.Player_Server);
                bool hasPlayer = nearPlayers.Count > 0;
                Pool.FreeList(ref nearPlayers);
                if (hasPlayer)
                    continue;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    selected = block.transform.position;
                }
            }
            if (selected == Vector3.zero)
            {
                foreach (var block in blocks)
                {
                    if (block == null)
                        continue;
                    if (!block.PrefabName.Contains("foundation"))
                        continue;
                    List<BasePlayer> nearPlayers = Pool.GetList<BasePlayer>();
                    Vis.Entities(block.transform.position, playerClearance, nearPlayers, Layers.Mask.Player_Server);
                    bool hasPlayer = nearPlayers.Count > 0;
                    Pool.FreeList(ref nearPlayers);
                    if (hasPlayer)
                        continue;
                    float dist = Vector3.Distance(block.transform.position, player.transform.position);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        selected = block.transform.position;
                    }
                }
            }
            Pool.FreeList(ref blocks);
            if (selected == Vector3.zero)
                return Vector3.zero;
            return new Vector3(selected.x, portalHeight, selected.z);
        }

        private void RewardDungeonClear(ActiveDungeon dungeon)
        {
            int econReward = dungeon.TierConfig.RewardConfig.DungeonClearEconomicsReward;
            int srReward = dungeon.TierConfig.RewardConfig.DungeonClearServerRewards;
            double xpReward = dungeon.TierConfig.RewardConfig.DungeonClearXpReward;
            bool rewarded = false;
            ulong winnerId = 0UL;
            int tierCooldown = GetTierCooldownSeconds(dungeon.TierConfig);
            if (dungeon.OwnerID != 0UL)
            {
                var owner = BasePlayer.FindByID(dungeon.OwnerID);
                if (owner != null && owner.IsConnected)
                {
                    try
                    {
                        if (econReward > 0 && Economics != null)
                        {
                            Economics.Call("Deposit", owner.userID, (double)econReward);
                            owner.ChatMessage(Msg(owner.UserIDString, "DungeonClearEconomyReward", econReward));
                        }
                        if (srReward > 0 && ServerRewards != null)
                        {
                            ServerRewards.Call("AddPoints", owner.userID, srReward);
                            owner.ChatMessage(Msg(owner.UserIDString, "DungeonClearServerReward", srReward));
                        }
                        rewarded = true;
                        winnerId = owner.userID;
                    }
                    catch
                    {
                        rewarded = false;
                    }
                }
            }
            if (!rewarded)
            {
                foreach (ulong uid in dungeon.PlayersInside.ToList())
                {
                    var participant = BasePlayer.FindByID(uid);
                    if (participant != null && participant.IsConnected)
                    {
                        try
                        {
                            if (econReward > 0 && Economics != null)
                            {
                                Economics.Call("Deposit", participant.userID, (double)econReward);
                                participant.ChatMessage(Msg(participant.UserIDString, "DungeonClearEconomyReward", econReward));
                            }
                            if (srReward > 0 && ServerRewards != null)
                            {
                                ServerRewards.Call("AddPoints", participant.userID, srReward);
                                participant.ChatMessage(Msg(participant.UserIDString, "DungeonClearServerReward", srReward));
                            }
                            rewarded = true;
                            winnerId = participant.userID;
                            break;
                        }
                        catch { }
                    }
                }
            }
            if (xpReward > 0.0 && SkillTree != null)
            {
                try
                {
                    if (_config.EnableSkillTreeXpTeamSharing)
                    {
                        var xpTargets = GetDungeonXpTargets(dungeon);
                        if (xpTargets.Count == 0)
                        {
                            if (winnerId != 0UL)
                            {
                                var winner = BasePlayer.FindByID(winnerId);
                                if (winner != null && winner.IsConnected)
                                {
                                    SkillTree.Call("AwardXP", winner, xpReward, "DungeonEvents", false);
                                    winner.ChatMessage(Msg(winner.UserIDString, "DungeonClearXpReward", xpReward));
                                }
                            }
                        }
                        else
                        {
                            double perPlayerXp = xpReward;
                            if (_config.SkillTreeXpShareMode == 0 && xpTargets.Count > 0)
                            {
                                perPlayerXp = xpReward / xpTargets.Count;
                            }
                            foreach (var player in xpTargets)
                            {
                                SkillTree.Call("AwardXP", player, perPlayerXp, "DungeonEvents", false);
                                player.ChatMessage(Msg(player.UserIDString, "DungeonClearXpReward", perPlayerXp));
                            }
                        }
                    }
                    else
                    {
                        if (winnerId != 0UL)
                        {
                            var winner = BasePlayer.FindByID(winnerId);
                            if (winner != null && winner.IsConnected)
                            {
                                SkillTree.Call("AwardXP", winner, xpReward, "DungeonEvents", false);
                                winner.ChatMessage(Msg(winner.UserIDString, "DungeonClearXpReward", xpReward));
                            }
                        }
                    }
                }
                catch { }
            }
            if (winnerId != 0UL)
            {
                SetBuyCooldown(winnerId, dungeon.TierConfig.TierName, tierCooldown);
                Interface.CallHook("OnDungeonWin", winnerId, dungeon.TierConfig.MapName);
            }
            string redLightPrefab = "assets/bundled/prefabs/modding/cinematic/cinelights/cinelight_point_red.prefab";
            string greenLightPrefab = "assets/bundled/prefabs/modding/cinematic/cinelights/cinelight_point_green.prefab";
            foreach (var entity in dungeon.Entities.ToList())
            {
                if (entity != null && entity.PrefabName == redLightPrefab)
                {
                    Vector3 pos = entity.transform.position;
                    Quaternion rot = entity.transform.rotation;
                    entity.Kill();
                    var newLight = GameManager.server.CreateEntity(greenLightPrefab, pos, rot);
                    if (newLight != null)
                    {
                        newLight.Spawn();
                        dungeon.Entities.Add(newLight);
                        RegisterSpawnedEntityForDungeon(newLight, dungeon.UniqueId);
                    }
                }
            }
            foreach (var entity in dungeon.Entities.ToList())
            {
                if (entity == null)
                {
                    continue;
                }
                if (entity is Door || entity.PrefabName.Contains("door"))
                {
                    entity.Kill();
                    dungeon.Entities.Remove(entity);
                }
            }
            dungeon.HasBeenCleared = true;
            dungeon.DestructionDelay = _config.DungeonSpawn.DungeonRemoval.RemovalAfterDestroyedTimer;
            dungeon.DestructionCountdownStart = DateTime.UtcNow;
            if (dungeon.InsidePortal != null && !dungeon.InsidePortal.IsDestroyed)
            {
                BasePlayer targetPlayer = null;
                foreach (ulong uid in dungeon.PlayersInside)
                {
                    var p = BasePlayer.FindByID(uid);
                    if (p != null && p.IsConnected)
                    {
                        targetPlayer = p;
                        break;
                    }
                }
                if (targetPlayer == null && dungeon.OwnerID != 0UL)
                {
                    targetPlayer = BasePlayer.FindByID(dungeon.OwnerID);
                }
                if (targetPlayer != null)
                {
                    Vector3 centerPos = GetCenterFoundationPositionNearPlayer(targetPlayer, dungeon.InsidePortal.transform.position.y);
                    if (centerPos == Vector3.zero)
                    {
                        Vector3 fallback = targetPlayer.transform.position + targetPlayer.transform.right * 2f;
                        fallback.y = dungeon.InsidePortal.transform.position.y;
                        dungeon.InsidePortal.transform.position = fallback;
                    }
                    else
                    {
                        dungeon.InsidePortal.transform.position = centerPos;
                    }
                    dungeon.InsidePortal.SendNetworkUpdateImmediate();
                }
            }
        }

        private List<BasePlayer> GetDungeonXpTargets(ActiveDungeon dungeon)
        {
            var result = new List<BasePlayer>();
            if (dungeon == null || dungeon.PlayersInside == null || dungeon.PlayersInside.Count == 0)
            {
                return result;
            }
            foreach (var uid in dungeon.PlayersInside)
            {
                var player = BasePlayer.FindByID(uid);
                if (player != null && player.IsConnected && !result.Any(x => x.userID == player.userID))
                {
                    result.Add(player);
                }
            }
            return result;
        }

        private void TeleportPlayersAndDestroyDungeon(ActiveDungeon dungeon)
        {
            foreach (ulong userID in dungeon.PlayersInside.ToList())
            {
                BasePlayer player = BasePlayer.FindByID(userID);
                TeleportPlayerToOutsidePortal(player, dungeon);
                player.ChatMessage(Msg(player?.UserIDString, "Dungeonclosed"));
            }
            DestroyDungeon(dungeon);
        }

        private void TeleportPlayerToOutsidePortal(BasePlayer player, ActiveDungeon dungeon)
        {
            if (player == null || !player.IsConnected)
                return;
            Vector3 exitPos = (dungeon.BasePortal != null && dungeon.BasePortal.transform != null) ? dungeon.BasePortal.transform.position : dungeon.PortalPosition;
            var playersInside = dungeon.PlayersInside.ToList();
            playersInside.Sort();
            int count = playersInside.Count;
            int index = playersInside.IndexOf(player.userID);
            if (index < 0)
                index = 0;
            if (_config.TeleportPlayerOnDeath)
            {
                float angleIncrement = 360f / Mathf.Max(count, 1);
                float angle = angleIncrement * index;
                float rad = angle * Mathf.Deg2Rad;
                float distance = 10f;
                Vector3 horizontalOffset = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * distance;
                Vector3 targetFlat = exitPos + horizontalOffset;
                Vector3 safePos = GetSafeTeleportPosition(targetFlat, 8f);
                SafeTeleport(player, safePos);
            }
            dungeon.PlayersInside.Remove(player.userID);
            UpdateTime(player, -1);
            DestroyEventUi(player);
            player.SendNetworkUpdateImmediate();
        }

        private Vector3 GetSafeTeleportPosition(Vector3 targetXZ, float extraHeight)
        {
            float surfaceY = WaterLevel.GetWaterOrTerrainSurface(targetXZ, false, false);
            Vector3 p = new Vector3(targetXZ.x, surfaceY + Mathf.Max(0f, extraHeight), targetXZ.z);
            int tries = 0;
            while (AntiHack.TestInsideTerrain(p) && tries++ < 16)
            {
                p.y += 2f;
            }
            return p;
        }

        private void SafeTeleport(BasePlayer player, Vector3 pos)
        {
            if (player == null || !player.IsConnected)
                return;
            player.limitNetworking = true;
            player.EnsureDismounted();
            player.Teleport(pos);
            player.limitNetworking = false;
            player.SendNetworkUpdateImmediate();
        }

        private List<Vector3> GreedySelectPositions(List<Vector3> candidates, int requiredCount)
        {
            List<Vector3> selected = new List<Vector3>();
            if (candidates.Count == 0)
                return selected;
            selected.Add(candidates[0]);
            while (selected.Count < requiredCount && selected.Count < candidates.Count)
            {
                Vector3 nextCandidate = candidates[0];
                float bestMinDistance = 0;
                foreach (var candidate in candidates)
                {
                    if (selected.Contains(candidate))
                        continue;
                    float minDistance = float.MaxValue;
                    foreach (var sel in selected)
                    {
                        float d = Vector3.Distance(candidate, sel);
                        if (d < minDistance)
                            minDistance = d;
                    }
                    if (minDistance > bestMinDistance)
                    {
                        bestMinDistance = minDistance;
                        nextCandidate = candidate;
                    }
                }
                selected.Add(nextCandidate);
            }
            return selected;
        }

        private List<Tuple<Vector3, Vector3>> GreedySelectPairs(List<Tuple<Vector3, Vector3>> pairs, int requiredCount)
        {
            List<Tuple<Vector3, Vector3>> selected = new List<Tuple<Vector3, Vector3>>();
            if (requiredCount <= 0 || pairs.Count == 0)
                return selected;
            selected.Add(pairs[0]);
            while (selected.Count < requiredCount && selected.Count < pairs.Count)
            {
                Tuple<Vector3, Vector3> nextPair = pairs[0];
                float bestMinDistance = 0;
                foreach (var pair in pairs)
                {
                    if (selected.Contains(pair))
                        continue;
                    Vector3 avg = (pair.Item1 + pair.Item2) / 2;
                    float minDistance = float.MaxValue;
                    foreach (var sel in selected)
                    {
                        Vector3 selAvg = (sel.Item1 + sel.Item2) / 2;
                        float d = Vector3.Distance(avg, selAvg);
                        if (d < minDistance)
                            minDistance = d;
                    }
                    if (minDistance > bestMinDistance)
                    {
                        bestMinDistance = minDistance;
                        nextPair = pair;
                    }
                }
                selected.Add(nextPair);
            }
            return selected;
        }

        private void AssignEntitiesToSpawnPoints(
            List<Vector3> spawnPoints,
            DungeonTierConfig tierConfig,
            out List<Vector3> turretSpawns,
            out List<Vector3> boxSpawns,
            out List<Vector3> npcSpawns,
            out List<Vector3> bossSpawns
        )
        {
            float spawnHeightOffset = 0.1f;
            turretSpawns = new List<Vector3>();
            boxSpawns = new List<Vector3>();
            npcSpawns = new List<Vector3>();
            bossSpawns = new List<Vector3>();
            int totalPoints = spawnPoints.Count;
            LogDebug("Total spawn points: " + totalPoints);
            LogDebug("TierConfig TurretsCounts: " + tierConfig.EntitySpawnLimits.TurretsCounts);
            LogDebug("TierConfig LootBoxesCounts: " + tierConfig.EntitySpawnLimits.LootBoxesCounts);
            LogDebug("TierConfig NpcsCounts: " + tierConfig.EntitySpawnLimits.NpcsCounts);
            LogDebug("TierConfig BossesCounts: " + tierConfig.EntitySpawnLimits.BossesCounts);
            int requiredTurrets = Math.Min(tierConfig.EntitySpawnLimits.TurretsCounts, totalPoints);
            int requiredBoxes = Math.Min(tierConfig.EntitySpawnLimits.LootBoxesCounts, totalPoints);
            int requiredNPCs = Math.Min(tierConfig.EntitySpawnLimits.NpcsCounts, totalPoints);
            int requiredBosses = Math.Min(tierConfig.EntitySpawnLimits.BossesCounts, totalPoints);
            LogDebug("Initial requiredTurrets: " + requiredTurrets);
            LogDebug("Initial requiredBoxes: " + requiredBoxes);
            LogDebug("Initial requiredNPCs: " + requiredNPCs);
            LogDebug("Initial requiredBosses: " + requiredBosses);
            int pairCount = Math.Min(requiredTurrets, requiredBoxes);
            LogDebug("Pair count (min of requiredTurrets and requiredBoxes): " + pairCount);
            List<Tuple<Vector3, Vector3>> selectedPairs = new List<Tuple<Vector3, Vector3>>();
            if (pairCount > 0)
            {
                float maxPairDistance = 5f;
                List<Tuple<Vector3, Vector3>> candidatePairs = new List<Tuple<Vector3, Vector3>>();
                for (int i = 0; i < spawnPoints.Count; i++)
                {
                    for (int j = i + 1; j < spawnPoints.Count; j++)
                    {
                        float distance = Vector3.Distance(spawnPoints[i], spawnPoints[j]);
                        if (distance <= maxPairDistance)
                        {
                            candidatePairs.Add(new Tuple<Vector3, Vector3>(spawnPoints[i], spawnPoints[j]));
                        }
                    }
                }
                LogDebug("Candidate pairs count: " + candidatePairs.Count);
                selectedPairs = GreedySelectPairs(candidatePairs, pairCount);
                LogDebug("Selected pairs count: " + selectedPairs.Count);
            }
            HashSet<Vector3> usedPoints = new HashSet<Vector3>();
            foreach (var pair in selectedPairs)
            {
                LogDebug("Adding pair - Turret: " + pair.Item1 + ", Box: " + pair.Item2);
                turretSpawns.Add(new Vector3(pair.Item1.x, pair.Item1.y + spawnHeightOffset, pair.Item1.z));
                boxSpawns.Add(new Vector3(pair.Item2.x, pair.Item2.y + spawnHeightOffset, pair.Item2.z));
                usedPoints.Add(pair.Item1);
                usedPoints.Add(pair.Item2);
            }
            requiredTurrets -= selectedPairs.Count;
            requiredBoxes -= selectedPairs.Count;
            LogDebug("After pairs, requiredTurrets: " + requiredTurrets + ", requiredBoxes: " + requiredBoxes);
            List<Vector3> remainingPoints = new List<Vector3>();
            foreach (var point in spawnPoints)
            {
                if (!usedPoints.Contains(point))
                    remainingPoints.Add(point);
            }
            LogDebug("Remaining points count after pairs: " + remainingPoints.Count);
            int remainingRequired = requiredNPCs + requiredBosses;
            List<Vector3> selectedRemaining = new List<Vector3>();
            if (remainingPoints.Count > 0 && remainingRequired > 0)
            {
                selectedRemaining = GreedySelectPositions(remainingPoints, remainingRequired);
            }
            LogDebug("Selected remaining points count for NPCs and Bosses: " + selectedRemaining.Count);
            int count = selectedRemaining.Count;
            for (int i = 0; i < count; i++)
            {
                if (i < requiredNPCs)
                {
                    LogDebug("Assigning NPC spawn: " + selectedRemaining[i]);
                    npcSpawns.Add(new Vector3(selectedRemaining[i].x, selectedRemaining[i].y + spawnHeightOffset, selectedRemaining[i].z));
                }
                else if (i < (requiredNPCs + requiredBosses))
                {
                    LogDebug("Assigning Boss spawn: " + selectedRemaining[i]);
                    bossSpawns.Add(new Vector3(selectedRemaining[i].x, selectedRemaining[i].y + spawnHeightOffset, selectedRemaining[i].z));
                }
            }
            if (requiredTurrets > 0 || requiredBoxes > 0)
            {
                HashSet<Vector3> alreadyUsed = new HashSet<Vector3>(usedPoints);
                alreadyUsed.UnionWith(selectedRemaining);
                List<Vector3> extraPoints = new List<Vector3>();
                foreach (var point in spawnPoints)
                {
                    if (!alreadyUsed.Contains(point))
                        extraPoints.Add(point);
                }
                LogDebug("Extra points count: " + extraPoints.Count);
                int extraNeeded = requiredTurrets + requiredBoxes;
                LogDebug("Extra needed (requiredTurrets + requiredBoxes): " + extraNeeded);
                if (extraPoints.Count > 0 && extraNeeded > 0)
                {
                    List<Vector3> selectedExtra = GreedySelectPositions(extraPoints, extraNeeded);
                    LogDebug("Selected extra points count: " + selectedExtra.Count);
                    int extraCount = selectedExtra.Count;
                    for (int i = 0; i < extraCount; i++)
                    {
                        if (i < requiredTurrets)
                        {
                            LogDebug("Assigning extra Turret spawn: " + selectedExtra[i]);
                            turretSpawns.Add(new Vector3(selectedExtra[i].x, selectedExtra[i].y + spawnHeightOffset, selectedExtra[i].z));
                        }
                        else if (i < (requiredTurrets + requiredBoxes))
                        {
                            LogDebug("Assigning extra Box spawn: " + selectedExtra[i]);
                            boxSpawns.Add(new Vector3(selectedExtra[i].x, selectedExtra[i].y + spawnHeightOffset, selectedExtra[i].z));
                        }
                    }
                }
            }
        }

        private IEnumerator PlaceEntities(List<Vector3> turretSpawns, List<Vector3> boxSpawns, List<Vector3> npcSpawns, List<Vector3> bossSpawns, ActiveDungeon activeDungeon)
        {
            LogDebug("Placing Entities.");
            var count = 0;
            foreach (var turretPos in turretSpawns)
            {
                var rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
                SpawnTurret(turretPos, rotation, activeDungeon.TierConfig.AutoTurretConfig, activeDungeon);
                count++;
                if (count >= 10)
                {
                    count = 0;
                    yield return BigDelay;
                }
            }
            count = 0;
            foreach (var boxPos in boxSpawns)
            {
                SpawnLootBox(boxPos, Quaternion.identity, activeDungeon.TierConfig, activeDungeon);
                count++;
                if (count >= 10)
                {
                    count = 0;
                    yield return BigDelay;
                }
            }
            count = 0;
            foreach (var npcPos in npcSpawns)
            {
                SpawnNpc(npcPos, activeDungeon);
                count++;
                if (count >= 10)
                {
                    count = 0;
                    yield return BigDelay;
                }
            }
            count = 0;
            foreach (var bossPos in bossSpawns)
            {
                SpawnNpc(bossPos, activeDungeon, true);
                count++;
                if (count >= 10)
                {
                    count = 0;
                    yield return BigDelay;
                }
            }
        }

        private BasePortal CreateDungeonPortal(Vector3 position, ActiveDungeon activeDungeon)
        {
            var prefabPath = "assets/prefabs/missions/portal/halloweenportalexit.prefab";
            var dungeonPortal = GameManager.server.CreateEntity(prefabPath, position) as BasePortal;
            if (dungeonPortal == null)
                return null;
            dungeonPortal.OwnerID = OwnerID;
            dungeonPortal.Spawn();
            if (_config.EnablePlaceSphere)
                CreateSpheres(position, _config.RadiusSphere * 1.5f, activeDungeon);
            return dungeonPortal;
        }

        private void CreateSpheres(Vector3 center, float radius, ActiveDungeon activeDungeon)
        {
            for (int i = 0; i < 10; i++)
            {
                var sphere = GameManager.server.CreateEntity("assets/prefabs/visualization/sphere.prefab", center) as SphereEntity;
                if (sphere == null)
                    continue;
                sphere.currentRadius = radius;
                sphere.lerpSpeed = 0f;
                sphere.enableSaving = false;
                sphere.OwnerID = OwnerID;
                sphere.Spawn();
                RegisterSpawnedEntityForDungeon(sphere, activeDungeon.UniqueId);
            }
            var colorSphere = GameManager.server.CreateEntity("assets/bundled/prefabs/modding/events/twitch/br_sphere_purple.prefab", center) as SphereEntity;
            colorSphere.currentRadius = radius;
            colorSphere.lerpSpeed = 0f;
            colorSphere.enableSaving = false;
            colorSphere.OwnerID = OwnerID;
            colorSphere.Spawn();
            RegisterSpawnedEntityForDungeon(colorSphere, activeDungeon.UniqueId);
        }

        private void AutoSpawnDungeon()
        {
            if (_config?.DungeonSpawn == null)
                return;
            int delay = _config.DungeonSpawn.AutoSpawnDelay;
            if (delay > 0 && (DateTime.UtcNow - _lastAutoSpawnTime).TotalSeconds < delay)
                return;
            var tier = GetDifficultyWithLeastActiveDungeons();
            if (tier == null || !tier.EnableAutoSpawn)
                return;
            EnqueueDungeonRequest(null, tier);
            _lastAutoSpawnTime = DateTime.UtcNow;
            LogDebug($"Enqueued Dungeon Auto Spawn of tier {_tierInfoMap[tier].Name}.", 1);
        }

        private void Requeue(DungeonRequest request, DungeonOrigin origin)
        {
            if (request == null)
                return;
            request.PortalPosition = Vector3.zero;
            request.DungeonPosition = Vector3.zero;
            if (origin == DungeonOrigin.AutoSpawn)
            {
                _dungeonFailedAutoSpawnQueue.Enqueue(request);
            }
            else
            {
                _dungeonFailedBuyQueue.Enqueue(request);
            }
        }

        private ActiveDungeon RegisterActiveDungeon(
            BasePortal dungeonPortal,
            Vector3 portalPosition,
            Vector3 DungeonPosition,
            string grid,
            DungeonTierConfig selectedTier,
            BasePlayer owner,
            bool lockToPlayer,
            DungeonOrigin origin
        )
        {
            var activeDungeon = new ActiveDungeon
            {
                BasePortal = dungeonPortal,
                PortalPosition = portalPosition,
                DungeonPosition = DungeonPosition,
                Grid = grid,
                TierName = _tierInfoMap[selectedTier].Name,
                TierConfig = selectedTier,
                Entities = new List<BaseEntity>(),
                Npcs = new List<BaseEntity>(),
                Spawned = false,
                CreationTime = DateTime.UtcNow,
                Origin = origin,
                AllDestroyedTime = null,
            };
            if (lockToPlayer && owner != null)
            {
                activeDungeon.OwnerID = owner.userID;
                activeDungeon.OwnerName = owner.displayName;
            }
            _activeDungeons.Add(activeDungeon);
            return activeDungeon;
        }

        private List<ActiveDungeon> RemoveInactiveDungeons()
        {
            var removedDungeons = new List<ActiveDungeon>();
            foreach (var dungeon in _activeDungeons.ToList())
            {
                if (CanBeRemoved(dungeon))
                {
                    removedDungeons.Add(dungeon);
                    DestroyDungeon(dungeon);
                    LogDebug($"Removed inactive dungeon of tier {dungeon.TierName} at grid {dungeon.Grid}.", 1);
                }
            }
            return removedDungeons;
        }

        private void DestroyDungeon(ActiveDungeon dungeon)
        {
            UnityEngine.GameObject.Destroy(dungeon.Trigger);
            dungeon.BasePortal?.Kill();
            ServerMgr.Instance.StartCoroutine(DestroyEntities(dungeon));
            RemoveDungeonMarkers(dungeon);
            _activeDungeons.Remove(dungeon);
            Interface.CallHook("OnDungeonDespawn", dungeon.OwnerID, dungeon.PortalPosition, dungeon.Grid, dungeon.TierName);
        }

        private void CleanupDungeonArea(ActiveDungeon dungeon)
        {
            if (dungeon == null)
                return;
            var bounds = dungeon.DungeonBounds;
            if (bounds.size == Vector3.zero)
                return;
            Vector3 center = bounds.center;
            float radius = bounds.extents.magnitude + 5f;
            bool InsideBounds(BaseEntity ent)
            {
                if (ent == null || ent.transform == null)
                    return false;
                return bounds.Contains(ent.transform.position);
            }
            var corpses = Facepunch.Pool.GetList<LootableCorpse>();
            Vis.Entities(center, radius, corpses);
            foreach (var c in corpses)
            {
                if (c == null)
                {
                    continue;
                }
                if (!InsideBounds(c))
                    continue;
                if (!c.IsDestroyed)
                {
                    c.Kill();
                }
            }
            Facepunch.Pool.FreeList(ref corpses);
            var droppedItems = Facepunch.Pool.GetList<DroppedItem>();
            Vis.Entities(center, radius, droppedItems);
            foreach (var di in droppedItems)
            {
                if (di == null)
                {
                    continue;
                }
                if (!InsideBounds(di))
                    continue;
                if (!di.IsDestroyed)
                {
                    di.Kill();
                }
            }
            Facepunch.Pool.FreeList(ref droppedItems);
            var droppedContainers = Facepunch.Pool.GetList<DroppedItemContainer>();
            Vis.Entities(center, radius, droppedContainers);
            foreach (var container in droppedContainers)
            {
                if (container == null)
                {
                    continue;
                }
                if (!InsideBounds(container))
                    continue;
                if (!container.IsDestroyed)
                {
                    container.Kill();
                }
            }
            Facepunch.Pool.FreeList(ref droppedContainers);
        }

        private IEnumerable<ActiveDungeon> RemoveAllDungeons()
        {
            var removed = new List<ActiveDungeon>(_activeDungeons);
            foreach (var d in removed)
            {
                DestroyDungeon(d);
            }
            _activeDungeons.Clear();
            _activeDungeonMarkers.Clear();
            return removed;
        }

        private IEnumerator DestroyEntities(ActiveDungeon dungeon)
        {
            yield return null;
            CleanupDungeonArea(dungeon);
            foreach (var entity in dungeon.Entities)
            {
                var count = 0;
                if (entity != null && !entity.IsDestroyed)
                {
                    LogDebug($"Destroying entity: {entity.GetType().Name} (ID: {entity.net.ID.Value})");
                    entity.Kill();
                    count++;
                    if (count >= 50)
                    {
                        count = 0;
                        yield return SmallDelay;
                    }
                }
            }
            foreach (var npc in dungeon.Npcs)
            {
                var count = 0;
                if (npc != null && !npc.IsDestroyed)
                {
                    LogDebug($"Destroying NPC: {npc.GetType().Name} (ID: {npc.net.ID.Value})");
                    npc.Kill();
                    count++;
                    if (count >= 50)
                    {
                        count = 0;
                        yield return SmallDelay;
                    }
                }
            }
            RemoveEntitiesForDungeon(dungeon.UniqueId);
        }

        private IEnumerator CleanupExistingEntities()
        {
            yield return null;
            var count = 0;
            foreach (BaseNetworkable networkable in BaseNetworkable.serverEntities)
            {
                BaseEntity entity = networkable as BaseEntity;
                if (entity == null)
                    continue;
                if (entity.OwnerID != OwnerID)
                    continue;
                entity.Kill();
                LogDebug("Removed: " + entity, 0);
                count++;
                if (count >= 1000)
                {
                    count = 0;
                    yield return SmallDelay;
                }
            }
            _activeDungeons.Clear();
            _activeDungeonMarkers.Clear();
        }

        private bool CanBeRemoved(ActiveDungeon dungeon)
        {
            if (dungeon.ActivationTime == null)
            {
                int idleLimit = dungeon.TierConfig.MaxUnenteredLifetimeSeconds;
                if (idleLimit > 0)
                {
                    var elapsedIdle = DateTime.UtcNow - dungeon.CreationTime;
                    if (elapsedIdle.TotalSeconds >= idleLimit)
                        return !ContainsAnyPlayers(dungeon) && dungeon.Spawned;
                }
                return false;
            }
            if (dungeon.TierConfig.MaxDungeonLifetime > 0)
            {
                var elapsedActive = DateTime.UtcNow - dungeon.ActivationTime.Value;
                if (elapsedActive.TotalSeconds >= dungeon.TierConfig.MaxDungeonLifetime)
                    return !ContainsAnyPlayers(dungeon) && dungeon.Spawned;
            }
            var removalCfg = _config.DungeonSpawn.DungeonRemoval;
            if (!removalCfg.RemoveIfAllNpcsDead && !removalCfg.RemoveIfAllBoxesDestroyed && !removalCfg.RemoveIfAllTurretsDestroyed)
                return false;
            bool npcsGone = !removalCfg.RemoveIfAllNpcsDead || AreAllNpcsGone(dungeon);
            bool boxesGone = !removalCfg.RemoveIfAllBoxesDestroyed || AreAllBoxesGone(dungeon);
            bool turretsGone = !removalCfg.RemoveIfAllTurretsDestroyed || AreAllTurretsGone(dungeon);
            if (npcsGone && boxesGone && turretsGone)
            {
                if (dungeon.AllDestroyedTime == null)
                {
                    dungeon.AllDestroyedTime = DateTime.UtcNow;
                    dungeon.LastReminderMinute = -1;
                }
                else
                {
                    double elapsed = (DateTime.UtcNow - dungeon.AllDestroyedTime.Value).TotalSeconds;
                    double timeLeft = removalCfg.RemovalAfterDestroyedTimer - elapsed;
                    if (timeLeft <= 0)
                        return !ContainsAnyPlayers(dungeon) && dungeon.Spawned;
                    SendRemovalReminder(dungeon, timeLeft);
                }
            }
            else
            {
                dungeon.AllDestroyedTime = null;
                dungeon.LastReminderMinute = -1;
            }
            return false;
        }

        private void SendRemovalReminder(ActiveDungeon dungeon, double timeLeftSeconds)
        {
            int minutesLeft = Mathf.CeilToInt((float)(timeLeftSeconds / 60.0));
            double totalElapsed = (DateTime.UtcNow - dungeon.AllDestroyedTime.Value).TotalSeconds;
            int elapsedMinutes = Mathf.FloorToInt((float)(totalElapsed / 60.0f));
            if (elapsedMinutes == dungeon.LastReminderMinute)
                return;
            dungeon.LastReminderMinute = elapsedMinutes;
            if (dungeon.OwnerID != 0UL)
            {
                var owner = BasePlayer.FindByID(dungeon.OwnerID);
                if (owner != null && owner.IsConnected)
                {
                    NotifyPlayer(owner, "DungeonRemovalCountdown", 0, minutesLeft);
                }
            }
        }

        private bool AreAllNpcsGone(ActiveDungeon dungeon)
        {
            return dungeon.Npcs.All(npc => npc == null || npc.IsDestroyed);
        }

        private bool AreAllBoxesGone(ActiveDungeon dungeon)
        {
            var boxes = dungeon.Entities.OfType<StorageContainer>().Where(e => e != null && e.IsValid()).ToList();
            return boxes.Count == 0 || boxes.All(b => b.IsDestroyed);
        }

        private bool AreAllTurretsGone(ActiveDungeon dungeon)
        {
            var turrets = dungeon.Entities.OfType<AutoTurret>().Where(e => e != null && e.IsValid()).ToList();
            return turrets.Count == 0 || turrets.All(t => t.IsDestroyed);
        }

        private bool ContainsAnyPlayers(ActiveDungeon dungeon)
        {
            if (dungeon == null)
                return false;
            Bounds dungeonBounds = dungeon.DungeonBounds;
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (dungeonBounds.Contains(player.transform.position))
                    return true;
            }
            foreach (BasePlayer sleeper in BasePlayer.sleepingPlayerList)
            {
                if (dungeonBounds.Contains(sleeper.transform.position))
                    return true;
            }
            return false;
        }

        private ActiveDungeon GetDungeonForPlayer(BasePlayer player)
        {
            if (player == null)
                return null;
            Vector3 playerPos = player.transform.position;
            foreach (var dungeon in _activeDungeons)
            {
                if (dungeon == null || dungeon.DungeonBounds.size == Vector3.zero)
                    continue;
                if (dungeon.DungeonBounds.Contains(playerPos))
                {
                    return dungeon;
                }
            }
            return null;
        }

        private BaseEntity FindSupportingFoundation(BaseEntity baseEntity)
        {
            Vector3 origin = baseEntity.transform.position + Vector3.up * 0.1f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hitData, 4f))
            {
                return hitData.GetEntity();
            }
            return null;
        }

        private void SpawnTurret(Vector3 position, Quaternion rotation, TurretConfig turretConfig, ActiveDungeon activeDungeon)
        {
            const string turretPrefabPath = "assets/prefabs/npc/autoturret/autoturret_deployed.prefab";
            var turretEntity = GameManager.server.CreateEntity(turretPrefabPath, position, rotation) as AutoTurret;
            if (turretEntity == null)
            {
                LogDebug($"Failed to spawn turret at {position}.", 1);
                return;
            }
            turretEntity.startHealth = turretConfig.Health;
            turretEntity.InitializeHealth(turretConfig.Health, turretConfig.Health);
            turretEntity.SetFlag(BaseEntity.Flags.Busy, true);
            turretEntity.SetFlag(BaseEntity.Flags.Locked, true);
            turretEntity.SetFlag(BaseEntity.Flags.Reserved8, true);
            turretEntity.OwnerID = OwnerID;
            var parent = FindSupportingFoundation(turretEntity);
            turretEntity.SetParent(parent, true, true);
            turretEntity.Spawn();
            turretEntity.inventory.canAcceptItem -= turretEntity.CanAcceptItem;
            turretEntity.inventory.canAcceptItem += CanAcceptItem;
            var weapon = ItemManager.CreateByName(turretConfig.WeaponShortName);
            weapon?.MoveToContainer(turretEntity.inventory, 0);
            var laserSight = ItemManager.CreateByName("weapon.mod.lasersight");
            laserSight?.MoveToContainer(weapon?.contents);
            turretEntity.UpdateAttachedWeapon();
            if (_config.CustomAutoTurretBehavior)
            {
                turretEntity.gameObject.AddComponent<TurretBehaviour>();
            }
            else
            {
                turretEntity.gameObject.AddComponent<TurretMonoBehaviour>();
            }
            activeDungeon.Entities.Add(turretEntity);
            RegisterSpawnedEntityForDungeon(turretEntity, activeDungeon.UniqueId);
            LogDebug($"AutoTurret spawned at {position} with '{turretConfig.WeaponShortName}'.", 0);
        }

        private bool CanAcceptItem(Item item, int pos)
        {
            if (pos == 0)
                return item.info.category == ItemCategory.Weapon;
            return item.info.category == ItemCategory.Ammunition;
        }

        private void SpawnLootBox(Vector3 position, Quaternion rotation, DungeonTierConfig tierConfig, ActiveDungeon activeDungeon)
        {
            var lootBoxEntity = GameManager.server.CreateEntity(tierConfig.LootBoxPrefabPath, position, rotation) as StorageContainer;
            if (lootBoxEntity == null)
            {
                LogDebug($"Failed to spawn loot box at {position}.", 1);
                return;
            }
            lootBoxEntity.skinID = tierConfig.LootBoxSkinID;
            lootBoxEntity.OwnerID = OwnerID;
            var parent = FindSupportingFoundation(lootBoxEntity);
            lootBoxEntity.SetParent(parent, true, true);
            lootBoxEntity.Spawn();
            var boxItems = GetLootBoxItems(tierConfig);
            PopulateStorage(lootBoxEntity.inventory, tierConfig, boxItems);
            AddLock(lootBoxEntity);
            activeDungeon.Entities.Add(lootBoxEntity);
            RegisterSpawnedEntityForDungeon(lootBoxEntity, activeDungeon.UniqueId);
            LogDebug($"LootBox spawned at {position}.", 0);
        }

        private void SpawnNpc(Vector3 position, ActiveDungeon activeDungeon, bool boss = false)
        {
            if (activeDungeon == null)
                return;
            var rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            var npc = CreateScientist(position, rot);
            if (npc == null)
            {
                LogDebug($"Failed to spawn ScientistNPC at {position}", 1);
                return;
            }
            var cfg = boss ? activeDungeon.TierConfig.BossNpcConfig : activeDungeon.TierConfig.NpcConfig;
            ApplyNpcConfig(npc, cfg);
            var home = npc.GetComponent<NpcHomePosition>();
            if (home == null)
                home = npc.gameObject.AddComponent<NpcHomePosition>();
            home.HomePosition = position;
            home.HomeRotation = rot;
            DisableNpcNavAndBrainMovement(npc);
            npc.gameObject.AddComponent<NpcMovement>();
            if (boss)
                npc.gameObject.AddComponent<BossMonoBehaviour>();
            activeDungeon.Entities.Add(npc);
            activeDungeon.Npcs.Add(npc);
            RegisterSpawnedEntityForDungeon(npc, activeDungeon.UniqueId);
            LogDebug($"NPC spawned (native) at {position}.", 0);
        }

        public class NpcHomePosition : MonoBehaviour
        {
            public Vector3 HomePosition;
            public Quaternion HomeRotation;
        }

        private void ResetDungeonNpcsToHome(ActiveDungeon dungeon)
        {
            if (dungeon == null || dungeon.Npcs == null)
                return;
            foreach (var ent in dungeon.Npcs.ToList())
            {
                var npc = ent as ScientistNPC;
                if (npc == null || npc.IsDestroyed)
                    continue;
                var home = npc.GetComponent<NpcHomePosition>();
                if (home == null)
                    continue;
                npc.Teleport(home.HomePosition);
                npc.transform.rotation = home.HomeRotation;
                npc.SendNetworkUpdateImmediate();
                var mv = npc.GetComponent<NpcMovement>();
                if (mv != null)
                    UnityEngine.Object.Destroy(mv);
                npc.gameObject.AddComponent<NpcMovement>();
            }
        }

        private void DisableNpcNavAndBrainMovement(ScientistNPC npc)
        {
            if (npc == null || npc.IsDestroyed)
                return;
            NextTick(() =>
            {
                try
                {
                    var brain = npc.Brain;
                    if (brain != null)
                    {
                        if (brain.Navigator != null)
                        {
                            brain.Navigator.CanUseNavMesh = false;
                            try
                            {
                                var agent = brain.Navigator.Agent;
                                if (agent != null)
                                    agent.enabled = false;
                            }
                            catch { }
                            try
                            {
                                brain.Navigator.Stop();
                            }
                            catch { }
                        }
                        try
                        {
                            brain.enabled = false;
                        }
                        catch { }
                    }
                    npc.enableSaving = false;
                }
                catch { }
            });
        }

        private DungeonTierConfig FindTierByName(string name)
        {
            if (_config?.Tiers == null || string.IsNullOrEmpty(name))
                return null;
            return _config.Tiers.FirstOrDefault(t => t != null && string.Equals(t.TierName, name, StringComparison.OrdinalIgnoreCase));
        }

        private List<ItemConfig> GetNpcLootItems(NpcConfig cfg)
        {
            if (cfg == null)
                return new List<ItemConfig>();
            if (string.IsNullOrEmpty(cfg.LootDropDataFile))
                return GetDefaultLootItems().Where(i => i.MinimumAmount > 0 && i.MaximumAmount > 0).ToList();
            var list = LoadLootItems(cfg.LootDropDataFile);
            if (list == null || list.Count == 0)
                list = GetDefaultLootItems();
            return (list ?? new List<ItemConfig>()).Where(i => i.MinimumAmount > 0 && i.MaximumAmount > 0).ToList();
        }

        private List<ItemConfig> GetLootBoxItems(DungeonTierConfig tierConfig)
        {
            if (tierConfig == null)
                return new List<ItemConfig>();
            if (string.IsNullOrEmpty(tierConfig.LootBoxDataFile))
                return GetDefaultLootItems().Where(i => i.MinimumAmount > 0 && i.MaximumAmount > 0).ToList();
            var list = LoadLootItems(tierConfig.LootBoxDataFile);
            if (list == null || list.Count == 0)
                list = GetDefaultLootItems();
            return (list ?? new List<ItemConfig>()).Where(i => i.MinimumAmount > 0 && i.MaximumAmount > 0).ToList();
        }

        private List<ItemConfig> LoadLootItems(string dataFile)
        {
            if (string.IsNullOrEmpty(dataFile))
                return new List<ItemConfig>();
            var file = Interface.Oxide.DataFileSystem.GetFile(dataFile);
            var obj = file.ReadObject<LootDropConfig>();
            return obj?.Items ?? new List<ItemConfig>();
        }

        private void EnsureLootDataFiles()
        {
            EnsureDefaultLootFile();
            if (_config.Tiers == null || _config.Tiers.Count == 0)
                return;
            foreach (var tier in _config.Tiers)
            {
                if (tier == null)
                    continue;
                var tierFolder = Sanitize(tier.TierName);
                if (tier.NpcConfig == null)
                    tier.NpcConfig = new NpcConfig();
                if (tier.BossNpcConfig == null)
                    tier.BossNpcConfig = new NpcConfig();
                tier.NpcConfig.LootDropDataFile = $"DungeonEvents/{tierFolder}/npc_loot";
                EnsureEmptyLootFile(tier.NpcConfig.LootDropDataFile);
                tier.BossNpcConfig.LootDropDataFile = $"DungeonEvents/{tierFolder}/boss_loot";
                EnsureEmptyLootFile(tier.BossNpcConfig.LootDropDataFile);
                tier.LootBoxDataFile = $"DungeonEvents/{tierFolder}/lootbox";
                EnsureEmptyLootFile(tier.LootBoxDataFile);
            }
        }

        private void EnsureEmptyLootFile(string dataFile)
        {
            if (string.IsNullOrEmpty(dataFile))
                return;
            var file = Interface.Oxide.DataFileSystem.GetFile(dataFile);
            var current = file.ReadObject<LootDropConfig>();
            if (current == null || current.Items == null)
            {
                file.WriteObject(new LootDropConfig { Items = new List<ItemConfig>() });
            }
        }

        private void EnsureDefaultLootFile()
        {
            var file = Interface.Oxide.DataFileSystem.GetFile("DungeonEvents/Default_Loot");
            var current = file.ReadObject<LootDropConfig>();
            if (current == null || current.Items == null || current.Items.Count == 0)
            {
                file.WriteObject(
                    new LootDropConfig
                    {
                        Items = new List<ItemConfig>
                        {
                            new ItemConfig
                            {
                                ShortName = "ammo.rifle",
                                InclusionChancePercentage = 15.0f,
                                MinimumAmount = 100,
                                MaximumAmount = 300,
                                CustomName = "",
                                SkinID = 0,
                            },
                            new ItemConfig
                            {
                                ShortName = "sulfur.ore",
                                InclusionChancePercentage = 15.0f,
                                MinimumAmount = 100,
                                MaximumAmount = 300,
                                CustomName = "",
                                SkinID = 0,
                            },
                            new ItemConfig
                            {
                                ShortName = "scrap",
                                InclusionChancePercentage = 70.0f,
                                MinimumAmount = 500,
                                MaximumAmount = 2000,
                                CustomName = "",
                                SkinID = 0,
                            },
                            new ItemConfig
                            {
                                ShortName = "grenade.f1",
                                InclusionChancePercentage = 10.0f,
                                MinimumAmount = 1,
                                MaximumAmount = 5,
                                CustomName = "",
                                SkinID = 0,
                            },
                            new ItemConfig
                            {
                                ShortName = "crude.oil",
                                InclusionChancePercentage = 40.0f,
                                MinimumAmount = 10,
                                MaximumAmount = 100,
                                CustomName = "",
                                SkinID = 0,
                            },
                            new ItemConfig
                            {
                                ShortName = "diesel_barrel",
                                InclusionChancePercentage = 30.0f,
                                MinimumAmount = 1,
                                MaximumAmount = 5,
                                CustomName = "",
                                SkinID = 0,
                            },
                            new ItemConfig
                            {
                                ShortName = "cctv.camera",
                                InclusionChancePercentage = 20.0f,
                                MinimumAmount = 5,
                                MaximumAmount = 20,
                                CustomName = "",
                                SkinID = 0,
                            },
                            new ItemConfig
                            {
                                ShortName = "gears",
                                InclusionChancePercentage = 20.0f,
                                MinimumAmount = 1,
                                MaximumAmount = 20,
                                CustomName = "",
                                SkinID = 0,
                            },
                            new ItemConfig
                            {
                                ShortName = "metal.refined",
                                InclusionChancePercentage = 20.0f,
                                MinimumAmount = 10,
                                MaximumAmount = 80,
                                CustomName = "",
                                SkinID = 0,
                            },
                            new ItemConfig
                            {
                                ShortName = "hazmatsuit",
                                InclusionChancePercentage = 10.0f,
                                MinimumAmount = 1,
                                MaximumAmount = 1,
                                CustomName = "",
                                SkinID = 0,
                            },
                            new ItemConfig
                            {
                                ShortName = "ammo.rocket.hv",
                                InclusionChancePercentage = 10.0f,
                                MinimumAmount = 1,
                                MaximumAmount = 20,
                                CustomName = "",
                                SkinID = 0,
                            },
                            new ItemConfig
                            {
                                ShortName = "ammo.rifle.hv",
                                InclusionChancePercentage = 10.0f,
                                MinimumAmount = 100,
                                MaximumAmount = 300,
                                CustomName = "",
                                SkinID = 0,
                            },
                            new ItemConfig
                            {
                                ShortName = "ammo.rocket.fire",
                                InclusionChancePercentage = 10.0f,
                                MinimumAmount = 5,
                                MaximumAmount = 10,
                                CustomName = "",
                                SkinID = 0,
                            },
                            new ItemConfig
                            {
                                ShortName = "lowgradefuel",
                                InclusionChancePercentage = 70.0f,
                                MinimumAmount = 50,
                                MaximumAmount = 200,
                                CustomName = "",
                                SkinID = 0,
                            },
                            new ItemConfig
                            {
                                ShortName = "rifle.lr300",
                                InclusionChancePercentage = 5.0f,
                                MinimumAmount = 1,
                                MaximumAmount = 1,
                                CustomName = "",
                                SkinID = 0,
                            },
                            new ItemConfig
                            {
                                ShortName = "rifle.m39",
                                InclusionChancePercentage = 5.0f,
                                MinimumAmount = 1,
                                MaximumAmount = 1,
                                CustomName = "",
                                SkinID = 0,
                            },
                            new ItemConfig
                            {
                                ShortName = "pistol.m92",
                                InclusionChancePercentage = 10.0f,
                                MinimumAmount = 1,
                                MaximumAmount = 1,
                                CustomName = "",
                                SkinID = 0,
                            },
                            new ItemConfig
                            {
                                ShortName = "lmg.m249",
                                InclusionChancePercentage = 2.0f,
                                MinimumAmount = 1,
                                MaximumAmount = 1,
                                CustomName = "",
                                SkinID = 0,
                            },
                            new ItemConfig
                            {
                                ShortName = "syringe.medical",
                                InclusionChancePercentage = 70.0f,
                                MinimumAmount = 10,
                                MaximumAmount = 30,
                                CustomName = "",
                                SkinID = 0,
                            },
                            new ItemConfig
                            {
                                ShortName = "metal.ore",
                                InclusionChancePercentage = 80.0f,
                                MinimumAmount = 100,
                                MaximumAmount = 300,
                                CustomName = "",
                                SkinID = 0,
                            },
                            new ItemConfig
                            {
                                ShortName = "metalpipe",
                                InclusionChancePercentage = 60.0f,
                                MinimumAmount = 1,
                                MaximumAmount = 5,
                                CustomName = "",
                                SkinID = 0,
                            },
                            new ItemConfig
                            {
                                ShortName = "metalspring",
                                InclusionChancePercentage = 60.0f,
                                MinimumAmount = 1,
                                MaximumAmount = 5,
                                CustomName = "",
                                SkinID = 0,
                            },
                            new ItemConfig
                            {
                                ShortName = "minigun",
                                InclusionChancePercentage = 2.0f,
                                MinimumAmount = 1,
                                MaximumAmount = 1,
                                CustomName = "",
                                SkinID = 0,
                            },
                            new ItemConfig
                            {
                                ShortName = "riflebody",
                                InclusionChancePercentage = 60.0f,
                                MinimumAmount = 5,
                                MaximumAmount = 10,
                                CustomName = "",
                                SkinID = 0,
                            },
                            new ItemConfig
                            {
                                ShortName = "ammo.rocket.basic",
                                InclusionChancePercentage = 30.0f,
                                MinimumAmount = 1,
                                MaximumAmount = 5,
                                CustomName = "",
                                SkinID = 0,
                            },
                            new ItemConfig
                            {
                                ShortName = "rocket.launcher",
                                InclusionChancePercentage = 5.0f,
                                MinimumAmount = 1,
                                MaximumAmount = 1,
                                CustomName = "",
                                SkinID = 0,
                            },
                            new ItemConfig
                            {
                                ShortName = "rifle.sks",
                                InclusionChancePercentage = 7.0f,
                                MinimumAmount = 1,
                                MaximumAmount = 1,
                                CustomName = "",
                                SkinID = 0,
                            },
                            new ItemConfig
                            {
                                ShortName = "tarp",
                                InclusionChancePercentage = 60.0f,
                                MinimumAmount = 1,
                                MaximumAmount = 5,
                                CustomName = "",
                                SkinID = 0,
                            },
                        },
                    }
                );
            }
        }

        private List<ItemConfig> GetDefaultLootItems()
        {
            var file = Interface.Oxide.DataFileSystem.GetFile("DungeonEvents/Default_Loot");
            var obj = file.ReadObject<LootDropConfig>();
            return obj?.Items ?? new List<ItemConfig>();
        }

        private JObject NpcSpawnObjectConfig(ActiveDungeon activeDungeon, bool boss = false)
        {
            var npcConfig = boss ? activeDungeon.TierConfig.BossNpcConfig : activeDungeon.TierConfig.NpcConfig;
            return new JObject
            {
                ["Name"] = npcConfig.Name,
                ["HostileTargetsOnly"] = false,
                ["DamageScale"] = npcConfig.DamageScale,
                ["States"] = new JArray { "CombatState" },
                ["AttackRangeMultiplier"] = 1f,
                ["SenseRange"] = 60f,
                ["MemoryDuration"] = 30f,
                ["CheckVisionCone"] = false,
                ["VisionCone"] = 135f,
                ["Health"] = npcConfig.Health,
                ["RoamRange"] = 15f,
                ["ChaseRange"] = 15f,
                ["WearItems"] = new JArray(npcConfig.WearItems.Items.Select(item => new JObject { ["ShortName"] = item.ShortName, ["SkinID"] = item.SkinID })),
                ["BeltItems"] = new JArray(
                    npcConfig
                        .BeltItems.Items.Where(item => item.Amount > 0)
                        .Select(item => new JObject
                        {
                            ["ShortName"] = item.ShortName,
                            ["Amount"] = item.Amount,
                            ["SkinID"] = item.SkinID,
                            ["Mods"] = new JArray(item.Mods ?? new List<string>()),
                            ["Ammo"] = "",
                        })
                ),
                ["TurretDamageScale"] = 1f,
                ["AimConeScale"] = 1.5f,
                ["DisableRadio"] = false,
                ["Stationary"] = true,
                ["CanUseWeaponMounted"] = true,
                ["CanRunAwayWater"] = false,
                ["AreaMask"] = 25,
                ["AgentTypeID"] = 0,
                ["HomePosition"] = string.Empty,
            };
        }

        private void EnqueueDungeonRequest(BasePlayer player, DungeonTierConfig tier, bool lockToPlayer = false)
        {
            var request = new DungeonRequest
            {
                Player = player,
                Tier = tier,
                LockToPlayer = lockToPlayer,
                Origin = player != null ? DungeonOrigin.Buy : DungeonOrigin.AutoSpawn,
            };
            _dungeonQueue.Enqueue(request);
            if (player != null)
            {
                NotifyPlayer(player, "ReceivedRequest", 0);
                if (!isCreatingDungeon)
                    ProcessDungeonQueue();
            }
        }

        private void ProcessDungeonQueue()
        {
            if (isCreatingDungeon || _dungeonQueue.Count == 0)
                return;
            DungeonRequest req = _dungeonQueue.Dequeue();
            if (req == null || req.Tier == null)
                return;
            if (req.Origin == DungeonOrigin.AutoSpawn)
            {
                int totalAuto = _activeDungeons.Count(d => d.Origin == DungeonOrigin.AutoSpawn);
                if (totalAuto >= _config.DungeonSpawn.MaxTotalActiveDungeonsAutoSpawn)
                {
                    _dungeonFailedAutoSpawnQueue.Enqueue(req);
                    return;
                }
                if (!req.Tier.EnableAutoSpawn)
                {
                    _dungeonFailedAutoSpawnQueue.Enqueue(req);
                    return;
                }
                int activeAutoSameTier = _activeDungeons.Count(d => d.Origin == DungeonOrigin.AutoSpawn && d.TierConfig == req.Tier);
                if (req.Tier.MaxActiveAutoSpawn > 0 && activeAutoSameTier >= req.Tier.MaxActiveAutoSpawn)
                {
                    _dungeonFailedAutoSpawnQueue.Enqueue(req);
                    return;
                }
            }
            else
            {
                int totalBuy = _activeDungeons.Count(d => d.Origin == DungeonOrigin.Buy);
                if (totalBuy >= _config.DungeonSpawn.MaxTotalActiveDungeonsBuy)
                {
                    _dungeonFailedBuyQueue.Enqueue(req);
                    if (req.Player != null)
                        NotifyPlayer(req.Player, "MaxActiveDungeonsReached", 0);
                    return;
                }
                if (!req.Tier.EnableBuy)
                {
                    _dungeonFailedBuyQueue.Enqueue(req);
                    if (req.Player != null)
                        NotifyPlayer(req.Player, "MaxActiveDungeonsReached", 0);
                    return;
                }
                int activeBuySameTier = _activeDungeons.Count(d => d.Origin == DungeonOrigin.Buy && d.TierConfig == req.Tier);
                if (req.Tier.MaxActiveBuy > 0 && activeBuySameTier >= req.Tier.MaxActiveBuy)
                {
                    _dungeonFailedBuyQueue.Enqueue(req);
                    if (req.Player != null)
                        NotifyPlayer(req.Player, "MaxActiveDungeonsReached", 0);
                    return;
                }
            }
            ServerMgr.Instance.StartCoroutine(CreateDungeon(req));
        }

        private int GetTierCooldownSeconds(DungeonTierConfig tier)
        {
            if (tier != null && tier.BuyCooldown > 0)
                return tier.BuyCooldown;
            return _config?.DungeonSpawn?.BuyCooldown ?? 0;
        }

        private void InitializeTierInfoMap()
        {
            _tierInfoMap = new Dictionary<DungeonTierConfig, TierInfo>();
            if (_config?.Tiers == null || _config.Tiers.Count == 0)
                return;
            foreach (var cfg in _config.Tiers)
            {
                if (cfg == null)
                    continue;
                var name = string.IsNullOrEmpty(cfg.TierName) ? "Tier" : cfg.TierName;
                Color marker;
                if (!TryParseHexColor(cfg.MarkerColorHex, out marker))
                {
                    var pair = GetTierColor(name);
                    marker = pair.color;
                }
                _tierInfoMap[cfg] = new TierInfo
                {
                    Name = name,
                    MapName = cfg.MapName,
                    BuyCost = cfg.BuyCost,
                    MarkerColor = marker,
                    ColorString = ToUiColorString(marker),
                    Enabled = cfg.EnableBuy,
                };
            }
        }

        private ActiveDungeon GetActiveDungeonByPortal(BasePortal portal)
        {
            return _activeDungeons.FirstOrDefault(d => d.BasePortal == portal);
        }

        private ActiveDungeon GetActiveDungeonByEntity(BaseEntity entity)
        {
            if (entity == null)
                return null;
            return _activeDungeons.FirstOrDefault(d => d.Entities.Contains(entity) || d.Npcs.Contains(entity));
        }

        private string GetAvailableTiers()
        {
            if (_config?.Tiers == null || _config.Tiers.Count == 0)
                return "None";
            var names = _config.Tiers.Where(t => t != null && t.EnableBuy).Select(t => t.TierName);
            return string.Join(", ", names);
        }

        private void HandleDungeonTimers()
        {
            foreach (var dungeon in _activeDungeons.ToList())
            {
                if (dungeon == null || !dungeon.Spawned)
                    continue;
                if (!dungeon.ActivationTime.HasValue)
                    continue;
                int lifetime = dungeon.TierConfig?.MaxDungeonLifetime ?? 0;
                if (lifetime <= 0)
                    continue;
                double elapsed = (DateTime.UtcNow - dungeon.ActivationTime.Value).TotalSeconds;
                if (elapsed >= lifetime)
                {
                    TeleportPlayersAndDestroyDungeon(dungeon);
                }
            }
            RemoveInactiveDungeons();
            int activeAutoSpawn = _activeDungeons.Count(d => d.Origin == DungeonOrigin.AutoSpawn);
            if (_config.DungeonSpawn.MaxTotalActiveDungeonsAutoSpawn > activeAutoSpawn)
            {
                if (_dungeonFailedAutoSpawnQueue.Count > 0)
                {
                    var req = _dungeonFailedAutoSpawnQueue.Dequeue();
                    _dungeonQueue.Enqueue(req);
                }
            }
            int activeBuyDungeons = _activeDungeons.Count(d => d.Origin == DungeonOrigin.Buy);
            if (_config.DungeonSpawn.MaxTotalActiveDungeonsBuy > activeBuyDungeons)
            {
                if (_dungeonFailedBuyQueue.Count > 0)
                {
                    var req = _dungeonFailedBuyQueue.Dequeue();
                    _dungeonQueue.Enqueue(req);
                }
            }
            if (_config.DungeonSpawn.EnableAutoSpawn && activeAutoSpawn < _config.DungeonSpawn.MaxTotalActiveDungeonsAutoSpawn)
            {
                AutoSpawnDungeon();
            }
            UpdateMarkers();
            ProcessDungeonQueue();
        }

        private T CreateMapMarker<T>(string prefab, Vector3 position)
            where T : BaseEntity
        {
            var entity = GameManager.server.CreateEntity(prefab, position) as T;
            entity?.Spawn();
            return entity;
        }

        private void CreateDungeonMarkers(ActiveDungeon activeDungeon)
        {
            if (!_tierInfoMap.TryGetValue(activeDungeon.TierConfig, out var tierInfo))
                return;
            if (activeDungeon.Origin == DungeonOrigin.Buy && !_config.DungeonSpawn.EnableMapMarkersBuy)
                return;
            if (activeDungeon.Origin == DungeonOrigin.AutoSpawn && !_config.DungeonSpawn.EnableMapMarkersAutoSpawn)
                return;
            const string vendingPrefab = "assets/prefabs/deployable/vendingmachine/vending_mapmarker.prefab";
            var vendMarker = CreateMapMarker<VendingMachineMapMarker>(vendingPrefab, activeDungeon.PortalPosition);
            if (vendMarker != null)
            {
                vendMarker.OwnerID = OwnerID;
                string timeText = FormatTime(activeDungeon.TierConfig.MaxDungeonLifetime);
                vendMarker.markerShopName = $"{tierInfo.MapName} - {timeText}";
                vendMarker.SendNetworkUpdate();
            }
            const string radiusPrefab = "assets/prefabs/tools/map/genericradiusmarker.prefab";
            var radMarker = CreateMapMarker<MapMarkerGenericRadius>(radiusPrefab, activeDungeon.PortalPosition);
            if (radMarker != null)
            {
                radMarker.OwnerID = OwnerID;
                radMarker.alpha = 0.75f;
                radMarker.radius = _config.DungeonSpawn.MapMarkerRadius;
                radMarker.color2 = tierInfo.MarkerColor;
                radMarker.SendUpdate();
                radMarker.SendNetworkUpdate();
            }
            _activeDungeonMarkers.Add(
                new DungeonMarker
                {
                    VendingMachineMapMarker = vendMarker,
                    MapMarkerGenericRadius = radMarker,
                    Position = activeDungeon.PortalPosition,
                    Tier = tierInfo.MapName,
                }
            );
        }

        private void UpdateMarkers()
        {
            foreach (var dungeon in _activeDungeons)
            {
                if (!_tierInfoMap.TryGetValue(dungeon.TierConfig, out var tierInfo))
                    continue;
                double timeLeft = dungeon.ActivationTime.HasValue
                    ? dungeon.TierConfig.MaxDungeonLifetime - (DateTime.UtcNow - dungeon.ActivationTime.Value).TotalSeconds
                    : dungeon.TierConfig.MaxDungeonLifetime;
                if (timeLeft < 0)
                    timeLeft = 0;
                string timeText = FormatTime(timeLeft);
                var markers = _activeDungeonMarkers.Where(m => m.Position == dungeon.PortalPosition).ToList();
                foreach (var marker in markers)
                {
                    string name = tierInfo.MapName;
                    if (!string.IsNullOrEmpty(dungeon.OwnerName))
                        name = $"{name} ({dungeon.OwnerName})";
                    marker.VendingMachineMapMarker.markerShopName = $"{name} - {timeText}";
                    marker.VendingMachineMapMarker.SendNetworkUpdate();
                    marker.MapMarkerGenericRadius?.SendUpdate();
                }
            }
        }

        private void RemoveDungeonMarkers(ActiveDungeon dungeon)
        {
            var markersToRemove = _activeDungeonMarkers.Where(m => m.Position == dungeon.PortalPosition).ToList();
            foreach (var marker in markersToRemove)
            {
                marker.VendingMachineMapMarker?.Kill();
                marker.MapMarkerGenericRadius?.Kill();
                _activeDungeonMarkers.Remove(marker);
            }
        }

        private DungeonTierConfig GetDifficultyWithLeastActiveDungeons()
        {
            var availableTiers = _tierInfoMap.Keys.Where(t => t.EnableAutoSpawn).ToList();
            if (availableTiers.Count == 0)
                return null;
            var viable = new Dictionary<DungeonTierConfig, int>();
            foreach (var tier in availableTiers)
            {
                int activeAuto = _activeDungeons.Count(d => d.Origin == DungeonOrigin.AutoSpawn && d.TierConfig == tier);
                if (tier.MaxActiveAutoSpawn > 0 && activeAuto >= tier.MaxActiveAutoSpawn)
                    continue;
                viable[tier] = activeAuto;
            }
            if (viable.Count == 0)
                return null;
            int minCount = viable.Values.Min();
            var candidates = viable.Where(kv => kv.Value == minCount).Select(kv => kv.Key).ToList();
            return candidates.Count == 0 ? null : candidates[Random.Range(0, candidates.Count)];
        }

        private void PopulateStorage(ItemContainer container, DungeonTierConfig tierConfig, List<ItemConfig> lootItems)
        {
            var shuffled = lootItems.OrderBy(_ => Random.value).ToList();
            int differentItemsCount = 0;
            foreach (var itemConfig in shuffled)
            {
                if (differentItemsCount >= tierConfig.MaxItemsPerBox)
                    break;
                if (Random.value * 100f > itemConfig.InclusionChancePercentage)
                    continue;
                var def = ItemManager.FindItemDefinition(itemConfig.ShortName);
                if (def == null)
                    continue;
                int min = itemConfig.MinimumAmount;
                int max = itemConfig.MaximumAmount;
                if (min <= 0 || max <= 0)
                    continue;
                if (max < min)
                {
                    int t = min;
                    min = max;
                    max = t;
                }
                int amount = Random.Range(min, max + 1);
                if (amount <= 0)
                    continue;
                if (container.itemList.Count >= container.capacity)
                    break;
                var item = ItemManager.Create(def, amount, itemConfig.SkinID);
                if (item == null)
                    continue;
                if (!string.IsNullOrEmpty(itemConfig.CustomName))
                    item.name = itemConfig.CustomName;
                item.MoveToContainer(container);
                differentItemsCount++;
            }
        }

        private void AddLock(BaseEntity storage)
        {
            const string prefabName = "assets/prefabs/locks/keylock/lock.key.prefab";
            var keyLock = GameManager.server.CreateEntity(prefabName) as KeyLock;
            if (keyLock == null)
                return;
            keyLock.gameObject.Identity();
            keyLock.SetParent(storage, storage.GetSlotAnchorName(BaseEntity.Slot.Lock));
            keyLock.Spawn();
            storage.SetSlot(BaseEntity.Slot.Lock, keyLock);
            keyLock.keyCode = Random.Range(1000, 9999);
            keyLock.OwnerID = 0UL;
            keyLock.firstKeyCreated = true;
            keyLock.SetFlag(BaseEntity.Flags.Locked, true);
        }

        private bool CanEnter(BasePlayer player, ActiveDungeon activeDungeon)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermissionEnter) && !permission.UserHasPermission(player.UserIDString, PermissionAdmin))
                return false;
            var ownPurchasedDungeon = _activeDungeons.FirstOrDefault(d => d.OwnerID == player.userID);
            if (ownPurchasedDungeon != null && ownPurchasedDungeon != activeDungeon && !permission.UserHasPermission(player.UserIDString, PermissionAdmin))
                return false;
            if (activeDungeon.OwnerID == 0UL)
            {
                activeDungeon.OwnerID = player.userID;
                activeDungeon.OwnerName = player.displayName;
                return true;
            }
            if (activeDungeon.OwnerID == player.userID)
                return true;
            if (_config.AllowTeam && player.currentTeam != 0UL)
            {
                var team = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
                if (team != null && team.members.Contains(activeDungeon.OwnerID))
                    return true;
            }
            if (_config.AllowFriends && Friends != null)
            {
                var areFriends = Friends.CallHook("AreFriends", activeDungeon.OwnerID, player.userID);
                if (areFriends != null && Convert.ToBoolean(areFriends))
                    return true;
            }
            return permission.UserHasPermission(player.UserIDString, PermissionAdmin);
        }
        #endregion

        #region Grid / Construction
        private IEnumerator CreateCustomProceduralDynamicDungeon(ActiveDungeon activeDungeon)
        {
            yield return null;
            _gridSize = activeDungeon.DungeonRoomSize;
            var basePosition = activeDungeon.DungeonPosition + new Vector3(-90f, 0, 0);
            var corridorRow = _gridSize / 2;
            _randomSeed = (uint)Random.Range(0, int.MaxValue);
            var origins = GenerateRoomOrigins(activeDungeon.DungeonRooms, basePosition);
            var grids = GenerateRoomGrids(origins.Count, corridorRow, activeDungeon);
            BuildRoomCells(origins, grids, corridorRow, activeDungeon);
            bool lastHorizontal;
            BuildRoomCorridors(origins, grids, activeDungeon, out lastHorizontal);
            ConnectAdjacentRooms(origins, grids, activeDungeon);
            BuildSingleRoomExtraCorridor(origins[0], grids[0], origins.Count > 1 ? origins[1] : (Vector3?)null, activeDungeon);
            BuildExitCorridor(origins[^1], grids[^1], lastHorizontal, activeDungeon);
            activeDungeon.SpawnPoints = activeDungeon.TurretPositions;
            activeDungeon.GridOrigins = origins;
            activeDungeon.CellGrids = grids;
            activeDungeon.DungeonBounds = CalculateRoomBounds(activeDungeon);
        }

        private void BuildSingleRoomExtraCorridor(Vector3 origin, bool[] grid, Vector3? nextOrigin, ActiveDungeon activeDungeon)
        {
            var corridorRow = _gridSize / 2;
            var corridorCol = _gridSize / 2;
            Vector3 dir;
            if (nextOrigin.HasValue)
            {
                var diff = nextOrigin.Value - origin;
                if (Mathf.Abs(diff.x) >= Mathf.Abs(diff.z))
                    dir = diff.x >= 0 ? Vector3.left : Vector3.right;
                else
                    dir = diff.z >= 0 ? Vector3.back : Vector3.forward;
            }
            else
            {
                if (Random.Range(0, 2) == 0)
                    dir = Random.Range(0, 2) == 0 ? Vector3.left : Vector3.right;
                else
                    dir = Random.Range(0, 2) == 0 ? Vector3.back : Vector3.forward;
            }
            var size = SubCells * SubCellSpacing;
            if (dir == Vector3.left)
            {
                var startX = FindActiveCellHorizontalLeft(grid, corridorRow);
                var start = GetCellPosition(origin, startX, corridorRow);
                var end = start + new Vector3(-CorridorLength, 0, 0);
                BuildCorridor(start, end, activeDungeon, true, true);
                PlaceCorridorFrame("west", start, size, activeDungeon);
            }
            else if (dir == Vector3.right)
            {
                var startX = FindActiveCellHorizontalRight(grid, corridorRow);
                var start = GetCellPosition(origin, startX, corridorRow);
                var end = start + new Vector3(CorridorLength, 0, 0);
                BuildCorridor(start, end, activeDungeon, true, true);
                PlaceCorridorFrame("east", start, size, activeDungeon);
            }
            else if (dir == Vector3.forward)
            {
                var startY = FindActiveCellVerticalTop(grid, corridorCol);
                var start = GetCellPosition(origin, corridorCol, startY);
                var end = start + new Vector3(0, 0, CorridorLength);
                BuildCorridor(start, end, activeDungeon, true, true);
                PlaceCorridorFrame("north", start, size, activeDungeon);
            }
            else
            {
                var startY = FindActiveCellVerticalBottom(grid, corridorCol);
                var start = GetCellPosition(origin, corridorCol, startY);
                var end = start + new Vector3(0, 0, -CorridorLength);
                BuildCorridor(start, end, activeDungeon, true, true);
                PlaceCorridorFrame("south", start, size, activeDungeon);
            }
        }

        private void ConnectAdjacentRooms(IReadOnlyList<Vector3> origins, IReadOnlyList<bool[]> grids, ActiveDungeon activeDungeon)
        {
            var step = _gridSize * CellSpacing + CorridorLength;
            var corridorRow = _gridSize / 2;
            var corridorCol = _gridSize / 2;
            var size = SubCells * SubCellSpacing;
            for (var i = 0; i < origins.Count - 1; i++)
            {
                for (var j = i + 1; j < origins.Count; j++)
                {
                    var aOrigin = origins[i];
                    var bOrigin = origins[j];
                    var sameX = Mathf.Abs(aOrigin.x - bOrigin.x) < 0.1f;
                    var sameZ = Mathf.Abs(aOrigin.z - bOrigin.z) < 0.1f;
                    if (sameX && Mathf.Abs(Mathf.Abs(aOrigin.z - bOrigin.z) - step) < 0.1f)
                    {
                        var aGrid = grids[i];
                        var bGrid = grids[j];
                        var verticalDir = aOrigin.z < bOrigin.z;
                        var startY = verticalDir ? FindActiveCellVerticalTop(aGrid, corridorCol) : FindActiveCellVerticalBottom(aGrid, corridorCol);
                        var endY = verticalDir ? FindActiveCellVerticalBottom(bGrid, corridorCol) : FindActiveCellVerticalTop(bGrid, corridorCol);
                        var start = GetCellPosition(aOrigin, corridorCol, startY);
                        var end = GetCellPosition(bOrigin, corridorCol, endY);
                        BuildCorridor(start, end, activeDungeon, true, false);
                        PlaceCorridorFrame(verticalDir ? "north" : "south", start, size, activeDungeon);
                        PlaceCorridorFrame(verticalDir ? "south" : "north", end, size, activeDungeon);
                    }
                    else if (sameZ && Mathf.Abs(Mathf.Abs(aOrigin.x - bOrigin.x) - step) < 0.1f)
                    {
                        var aGrid = grids[i];
                        var bGrid = grids[j];
                        var horizontalDir = aOrigin.x < bOrigin.x;
                        var startX = horizontalDir ? FindActiveCellHorizontalRight(aGrid, corridorRow) : FindActiveCellHorizontalLeft(aGrid, corridorRow);
                        var endX = horizontalDir ? FindActiveCellHorizontalLeft(bGrid, corridorRow) : FindActiveCellHorizontalRight(bGrid, corridorRow);
                        var start = GetCellPosition(aOrigin, startX, corridorRow);
                        var end = GetCellPosition(bOrigin, endX, corridorRow);
                        BuildCorridor(start, end, activeDungeon, true, false);
                        PlaceCorridorFrame(horizontalDir ? "east" : "west", start, size, activeDungeon);
                        PlaceCorridorFrame(horizontalDir ? "west" : "east", end, size, activeDungeon);
                    }
                }
            }
        }

        private Bounds CalculateRoomBounds(ActiveDungeon activeDungeon)
        {
            if (activeDungeon.Foundations == null || activeDungeon.Foundations.Count == 0)
                return new Bounds(activeDungeon.DungeonPosition, Vector3.zero);
            var min = activeDungeon.Foundations[0];
            var max = activeDungeon.Foundations[0];
            foreach (var p in activeDungeon.Foundations)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            var center = (min + max) * 0.5f;
            var size = max - min + new Vector3(CellSpacing, 30f, CellSpacing);
            if (size.y < 30f)
                size.y = 30f;
            return new Bounds(center, size);
        }

        private List<Vector3> GenerateRoomOrigins(int roomsToBuild, Vector3 basePosition)
        {
            var origins = new List<Vector3> { basePosition };
            if (roomsToBuild <= 1)
                return origins;
            var step = _gridSize * CellSpacing + CorridorLength;
            var directions = new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
            const int attemptsPerRoom = 20;
            Vector3 prevDir = Vector3.zero;
            for (var i = 1; i < roomsToBuild; i++)
            {
                var placed = false;
                for (var a = 0; a < attemptsPerRoom && !placed; a++)
                {
                    var dir = directions[Random.Range(0, directions.Length)];
                    if (prevDir != Vector3.zero && Vector3.Dot(dir, prevDir) > 0.9f)
                        continue;
                    var next = origins[^1] + dir * step;
                    if (origins.Any(o => Vector3.Distance(o, next) < 0.1f))
                        continue;
                    origins.Add(next);
                    prevDir = dir;
                    placed = true;
                }
                if (!placed)
                {
                    var fallbackDir = Mathf.Abs(prevDir.x) > 0 ? Vector3.forward : Vector3.right;
                    origins.Add(origins[^1] + fallbackDir * step);
                    prevDir = fallbackDir;
                }
            }
            return origins;
        }

        private List<bool[]> GenerateRoomGrids(int roomsToBuild, int corridorRow, ActiveDungeon activeDungeon)
        {
            var layouts = new List<bool[]>();
            var corridorCol = _gridSize / 2;
            for (var i = 0; i < roomsToBuild; i++)
            {
                var forcedCells = new List<Vector2Int>
                {
                    new Vector2Int(_gridSize - 1, corridorRow),
                    new Vector2Int(0, corridorRow),
                    new Vector2Int(corridorCol, _gridSize - 1),
                    new Vector2Int(corridorCol, 0),
                };
                GenerateGrid(forcedCells, corridorRow, activeDungeon);
                if (activeDungeon.LastGeneratedGrid == null)
                    throw new Exception($"Failed to generate grid for activeDungeon {i}");
                layouts.Add(activeDungeon.LastGeneratedGrid);
            }
            return layouts;
        }

        private void BuildRoomCells(IReadOnlyList<Vector3> origins, IReadOnlyList<bool[]> grids, int corridorRow, ActiveDungeon activeDungeon)
        {
            for (var i = 0; i < origins.Count; i++)
            {
                var westForced = i == 0 ? (Vector2Int?)null : new Vector2Int(0, corridorRow);
                var eastForced = new Vector2Int(_gridSize - 1, corridorRow);
                BuildRoomGrid(grids[i], origins[i], westForced, eastForced, activeDungeon);
            }
        }

        private void BuildRoomCorridors(IReadOnlyList<Vector3> origins, IReadOnlyList<bool[]> grids, ActiveDungeon activeDungeon, out bool lastHorizontal)
        {
            var corridorRow = _gridSize / 2;
            var corridorCol = _gridSize / 2;
            lastHorizontal = true;
            for (var i = 0; i < origins.Count - 1; i++)
            {
                var aGrid = grids[i];
                var bGrid = grids[i + 1];
                var aOrigin = origins[i];
                var bOrigin = origins[i + 1];
                var horizontal = Mathf.Abs(bOrigin.x - aOrigin.x) >= Mathf.Abs(bOrigin.z - aOrigin.z);
                lastHorizontal = horizontal;
                if (horizontal)
                {
                    var startX = FindActiveCellHorizontalRight(aGrid, corridorRow);
                    var endX = FindActiveCellHorizontalLeft(bGrid, corridorRow);
                    var start = GetCellPosition(aOrigin, startX, corridorRow);
                    var end = GetCellPosition(bOrigin, endX, corridorRow);
                    BuildCorridor(start, end, activeDungeon, true, false);
                }
                else
                {
                    var startY = FindActiveCellVerticalTop(aGrid, corridorCol);
                    var endY = FindActiveCellVerticalBottom(bGrid, corridorCol);
                    var start = GetCellPosition(aOrigin, corridorCol, startY);
                    var end = GetCellPosition(bOrigin, corridorCol, endY);
                    BuildCorridor(start, end, activeDungeon, true, false);
                }
            }
        }

        private void BuildExitCorridor(Vector3 lastOrigin, bool[] lastGrid, bool horizontal, ActiveDungeon activeDungeon)
        {
            var corridorRow = _gridSize / 2;
            var corridorCol = _gridSize / 2;
            if (horizontal)
            {
                var startX = FindActiveCellHorizontalRight(lastGrid, corridorRow);
                var start = GetCellPosition(lastOrigin, startX, corridorRow);
                var end = start + new Vector3(9.01f, 0, 0);
                BuildCorridor(start, end, activeDungeon, false, false);
            }
            else
            {
                var startY = FindActiveCellVerticalTop(lastGrid, corridorCol);
                var start = GetCellPosition(lastOrigin, corridorCol, startY);
                var end = start + new Vector3(0, 0, 9.01f);
                BuildCorridor(start, end, activeDungeon, false, false);
            }
        }

        private int FindActiveCellHorizontalRight(bool[] grid, int row)
        {
            for (var x = _gridSize - 1; x >= 0; x--)
                if (GetGridValue(grid, x, row))
                    return x;
            return _gridSize - 1;
        }

        private int FindActiveCellHorizontalLeft(bool[] grid, int row)
        {
            for (var x = 0; x < _gridSize; x++)
                if (GetGridValue(grid, x, row))
                    return x;
            return 0;
        }

        private int FindActiveCellVerticalTop(bool[] grid, int col)
        {
            for (var y = _gridSize - 1; y >= 0; y--)
                if (GetGridValue(grid, col, y))
                    return y;
            return _gridSize - 1;
        }

        private int FindActiveCellVerticalBottom(bool[] grid, int col)
        {
            for (var y = 0; y < _gridSize; y++)
                if (GetGridValue(grid, col, y))
                    return y;
            return 0;
        }

        private Vector3 GetCellPosition(Vector3 origin, int x, int y)
        {
            var start = origin - new Vector3(_gridSize * CellSpacing * 0.5f, 0, _gridSize * CellSpacing * 0.5f);
            return start + new Vector3(x * CellSpacing, 0, y * CellSpacing);
        }

        private IEnumerator BuildFrames(ActiveDungeon activeDungeon)
        {
            yield return null;
            var corridorRow = _gridSize / 2;
            for (var i = 0; i < activeDungeon.CellGrids.Count; i++)
            {
                var westForced = i == 0 ? (Vector2Int?)null : new Vector2Int(0, corridorRow);
                var eastForced = new Vector2Int(_gridSize - 1, corridorRow);
                BuildFrameGrid(activeDungeon.CellGrids[i], activeDungeon.GridOrigins[i], westForced, eastForced, activeDungeon);
                if (i % 10 == 0)
                    yield return SmallDelay;
            }
            for (var i = 0; i < activeDungeon.GridOrigins.Count - 1; i++)
            {
                CreateCorridorFrames(activeDungeon.GridOrigins[i], activeDungeon.GridOrigins[i + 1], corridorRow, activeDungeon);
                if (i % 10 == 0)
                    yield return SmallDelay;
            }
            if (activeDungeon.GridOrigins.Count > 1)
                PlaceFinalExitFrames(activeDungeon.GridOrigins[^1], activeDungeon.GridOrigins[^2], corridorRow, activeDungeon);
            else
                PlaceFinalExitFrames(activeDungeon.GridOrigins[^1], activeDungeon.GridOrigins[^1] + Vector3.right, corridorRow, activeDungeon);
        }

        private void CreateCorridorFrames(Vector3 originA, Vector3 originB, int corridorRow, ActiveDungeon activeDungeon)
        {
            var size = SubCells * SubCellSpacing;
            var corridorCol = _gridSize / 2;
            if (Mathf.Abs(originB.x - originA.x) > Mathf.Abs(originB.z - originA.z))
            {
                var leftCell = GetCellPosition(originA, _gridSize - 1, corridorRow);
                var rightCell = GetCellPosition(originB, 0, corridorRow);
                PlaceCorridorFrame("east", leftCell, size, activeDungeon);
                PlaceCorridorFrame("west", rightCell, size, activeDungeon);
            }
            else
            {
                var bottomCell = GetCellPosition(originA, corridorCol, _gridSize - 1);
                var topCell = GetCellPosition(originB, corridorCol, 0);
                PlaceCorridorFrame("north", bottomCell, size, activeDungeon);
                PlaceCorridorFrame("south", topCell, size, activeDungeon);
            }
        }

        private void PlaceFinalExitFrames(Vector3 lastOrigin, Vector3 prevOrigin, int corridorRow, ActiveDungeon activeDungeon)
        {
            var size = SubCells * SubCellSpacing;
            var corridorCol = _gridSize / 2;
            var horizontal = Mathf.Abs(lastOrigin.x - prevOrigin.x) > Mathf.Abs(lastOrigin.z - prevOrigin.z);
            if (horizontal)
            {
                var lastCell = GetCellPosition(lastOrigin, _gridSize - 1, corridorRow);
                PlaceCorridorFrame("east", lastCell, size, activeDungeon);
            }
            else
            {
                var lastCell = GetCellPosition(lastOrigin, corridorCol, _gridSize - 1);
                PlaceCorridorFrame("north", lastCell, size, activeDungeon);
            }
        }

        private IEnumerator BuildWalls(ActiveDungeon activeDungeon)
        {
            yield return null;
            var loop = 0;
            foreach (var foundation in activeDungeon.Foundations)
            {
                TryPlaceWall(foundation, Vector3.forward * SubCellSpacing, WallRotationOffset, activeDungeon);
                TryPlaceWall(foundation, Vector3.back * SubCellSpacing, 180f + WallRotationOffset, activeDungeon);
                TryPlaceWall(foundation, Vector3.right * SubCellSpacing, 90f + WallRotationOffset, activeDungeon);
                TryPlaceWall(foundation, Vector3.left * SubCellSpacing, 270f + WallRotationOffset, activeDungeon);
                if (loop % 100 == 0)
                    yield return SmallDelay;
                loop++;
            }
        }

        private void GenerateGrid(IEnumerable<Vector2Int> forcedCells, int corridorRow, ActiveDungeon activeDungeon)
        {
            var grid = new bool[_gridSize * _gridSize];
            var success = false;
            for (var attempt = 0; attempt < MaxGenerationAttempts; attempt++)
            {
                RandomiseGrid(grid);
                ForceCorridorRow(grid, corridorRow);
                ApplyForcedCells(grid, forcedCells);
                var allForced = CollectForcedCells(grid, forcedCells);
                var root = allForced[0];
                if (!IsGridConnected(grid, allForced, root))
                {
                    ResetRandomSeed();
                    continue;
                }
                PruneIsolatedCells(grid, root.x, root.y);
                if (CountActiveCells(grid) >= MinimumActiveCells)
                {
                    success = true;
                    break;
                }
                ResetRandomSeed();
            }
            activeDungeon.LastGeneratedGrid = success ? grid : null;
        }

        private void RandomiseGrid(bool[] grid)
        {
            for (var i = 0; i < grid.Length; i++)
                grid[i] = SeedRandom.Range(ref _randomSeed, 0, 2) == 0;
        }

        private void ForceCorridorRow(bool[] grid, int corridorRow)
        {
            for (var x = 0; x < _gridSize; x++)
                grid[corridorRow * _gridSize + x] = true;
        }

        private void ApplyForcedCells(bool[] grid, IEnumerable<Vector2Int> forcedCells)
        {
            foreach (var cell in forcedCells)
            {
                SetGridValue(grid, cell.x, cell.y, true);
                if (cell.x > 0)
                    SetGridValue(grid, cell.x - 1, cell.y, true);
                if (cell.x < _gridSize - 1)
                    SetGridValue(grid, cell.x + 1, cell.y, true);
            }
        }

        private List<Vector2Int> CollectForcedCells(bool[] grid, IEnumerable<Vector2Int> forcedCells)
        {
            var allForced = new List<Vector2Int>(forcedCells);
            for (var x = 0; x < _gridSize; x++)
            for (var y = 0; y < _gridSize; y++)
                if (GetGridValue(grid, x, y))
                {
                    var cell = new Vector2Int(x, y);
                    if (!allForced.Contains(cell))
                        allForced.Add(cell);
                }
            return allForced;
        }

        private bool IsGridConnected(bool[] grid, IEnumerable<Vector2Int> requiredCells, Vector2Int root)
        {
            return requiredCells.All(c => HasPath(grid, root.x, root.y, c.x, c.y));
        }

        private void ResetRandomSeed()
        {
            _randomSeed = (uint)Random.Range(0, int.MaxValue);
        }

        private BaseEntity CreateBlock(string prefab, Vector3 position, Quaternion rotation, ActiveDungeon room)
        {
            var key = PositionKey(position, 2);
            if (room.OccupiedPositions.Contains(key))
                return null;
            var entity = GameManager.server.CreateEntity(prefab, position, rotation);
            if (entity == null)
                return null;
            if (entity is BuildingBlock block)
            {
                block.blockDefinition = PrefabAttribute.server.Find<Construction>(block.prefabID);
                var (grade, skin) = GetGradeAndSkin(room.TierConfig.BuildingGradeSelection);
                block.SetGrade(grade);
                block.skinID = skin;
                block.grounded = true;
            }
            entity.OwnerID = OwnerID;
            entity.Spawn();
            if (entity is BuildingBlock b)
            {
                b.SetHealthToMax();
                if (b.skinID == 10221 && room.TierConfig.ContainerColor >= 1 && room.TierConfig.ContainerColor <= 16)
                {
                    b.SetCustomColour((uint)room.TierConfig.ContainerColor);
                }
            }
            room.OccupiedPositions.Add(key);
            return entity;
        }

        private BaseEntity CreateDoor(Vector3 position, Quaternion rotation, ActiveDungeon room)
        {
            var door = GameManager.server.CreateEntity(DoorPrefab, position, rotation);
            if (door == null)
                return null;
            door.skinID = room.TierConfig.DoorSkinID;
            door.Spawn();
            room.Entities.Add(door);
            return door;
        }

        private void CreateLight(Vector3 centre, ActiveDungeon room)
        {
            if (room == null || _config == null)
                return;
            if (!_config.EnableDungeonLights)
                return;
            var prefab = string.IsNullOrWhiteSpace(_config.DungeonLightPrefabPath) ? LightPrefab : _config.DungeonLightPrefabPath;
            var lightPos = centre + CeilingLightOffset + Vector3.up * CeilingHeight;
            var posKey = "LIGHT:" + PositionKey(lightPos, 2);
            if (room.OccupiedPositions.Contains(posKey))
                return;
            room.OccupiedPositions.Add(posKey);
            var rotation = Quaternion.Euler(90f, 0, 0);
            var lightEntity = GameManager.server.CreateEntity(prefab, lightPos, rotation);
            if (lightEntity == null)
                return;
            if (lightEntity is IOEntity io)
            {
                io.UpdateHasPower(1000, 100);
                io.IOStateChanged(1000, 100);
            }
            lightEntity.OwnerID = OwnerID;
            lightEntity.Spawn();
            room.Entities.Add(lightEntity);
            RegisterSpawnedEntityForDungeon(lightEntity, room.UniqueId);
        }

        private void CreateTurret(Vector3 subCellPos, ActiveDungeon room)
        {
            var turretPos = subCellPos + new Vector3(SubCellSpacing * 0.5f, 0, SubCellSpacing * 0.5f) + _wallOffset;
            room.TurretPositions.Add(turretPos);
            if (!SpawnTurrets)
                return;
            var turret = GameManager.server.CreateEntity(TurretPrefab, turretPos, Quaternion.identity);
            if (turret == null)
                return;
            turret.enableSaving = false;
            turret.Spawn();
            room.Entities.Add(turret);
        }

        private void HandleSubCell(Vector3 subCellPos, ActiveDungeon room, bool allowTurret)
        {
            var foundation = CreateBlock(FoundationPrefab, subCellPos, Quaternion.identity, room);
            if (foundation == null)
                return;
            room.Entities.Add(foundation);
            var key = PositionKey(subCellPos, 2);
            if (room.FoundationKeys.Add(key))
                room.Foundations.Add(subCellPos);
            if (allowTurret)
                CreateTurret(subCellPos, room);
            var centre = RoundVector(subCellPos + new Vector3(SubCellSpacing * 0.5f, 0, SubCellSpacing * 0.5f), 2);
            room.RoomFloorCenters.Add(centre);
            if (PlaceCeiling)
                CreateCeiling(subCellPos, room);
        }

        private void CreateCeiling(Vector3 subCellPos, ActiveDungeon room)
        {
            var ceilingPos = subCellPos + Vector3.up * CeilingHeight;
            var ceiling = CreateBlock(CeilingPrefab, ceilingPos, Quaternion.identity, room);
            if (ceiling == null)
                return;
            room.Entities.Add(ceiling);
        }

        private void BuildCorridor(Vector3 start, Vector3 end, ActiveDungeon room, bool spawnEntities, bool placePortal)
        {
            var diff = end - start;
            var alongX = Mathf.Abs(diff.x) >= Mathf.Abs(diff.z);
            var primarySteps = Mathf.CeilToInt((alongX ? Mathf.Abs(diff.x) : Mathf.Abs(diff.z)) / SubCellSpacing);
            var dirX = alongX ? Mathf.Sign(diff.x) : 0f;
            var dirZ = alongX ? 0f : Mathf.Sign(diff.z);
            var allowTurret = spawnEntities && !placePortal;
            for (var i = 0; i <= primarySteps; i++)
            {
                for (var j = 0; j < SubCells; j++)
                {
                    Vector3 p = alongX
                        ? new Vector3(start.x + dirX * i * SubCellSpacing, start.y, start.z + j * SubCellSpacing)
                        : new Vector3(start.x + j * SubCellSpacing, start.y, start.z + dirZ * i * SubCellSpacing);
                    HandleSubCell(p, room, allowTurret);
                }
            }
            if (_config != null && _config.EnableDungeonLights)
            {
                var totalLength = alongX ? Mathf.Abs(diff.x) : Mathf.Abs(diff.z);
                var spacing = Mathf.Clamp(_config.DungeonLightSpacing, 2f, 30f);
                var lightCount = Mathf.Max(1, Mathf.CeilToInt(totalLength / spacing));
                for (var c = 0; c <= lightCount; c++)
                {
                    var offset = c * spacing + spacing * 0.5f;
                    if (offset > totalLength + 0.01f)
                        break;
                    Vector3 lightPos = alongX ? new Vector3(start.x + dirX * offset, start.y, start.z + SubCellSpacing) : new Vector3(start.x + SubCellSpacing, start.y, start.z + dirZ * offset);
                    CreateLight(lightPos, room);
                }
            }
            if (!placePortal)
                return;
            var mid = (start + end) * 0.5f;
            var portalOffset = alongX ? new Vector3(2.5f * dirX, 0.2f, 1.5f) : new Vector3(1.5f, 0.2f, 2.5f * dirZ);
            var yaw = alongX ? (dirX >= 0 ? 90f : 270f) : (dirZ >= 0 ? 0f : 180f);
            var portal = GameManager.server.CreateEntity(PortalPrefab, mid + portalOffset, Quaternion.Euler(0, yaw, 0)) as BasePortal;
            if (portal == null)
                return;
            portal.Spawn();
            room.InsidePortal = portal;
            room.Entities.Add(portal);
            RegisterSpawnedEntityForDungeon(portal, room.UniqueId);
        }

        private void TryPlaceWall(Vector3 foundationPos, Vector3 offset, float yaw, ActiveDungeon room)
        {
            if (room.FoundationKeys.Contains(PositionKey(foundationPos + offset, 2)))
                return;
            Vector3 basePos =
                Mathf.Abs(offset.x) > 0
                    ? foundationPos + new Vector3(offset.x > 0 ? SubCellSpacing : 0, 0, SubCellSpacing * 0.5f) + _wallOffset
                    : foundationPos + new Vector3(SubCellSpacing * 0.5f, 0, offset.z > 0 ? SubCellSpacing : 0) + _wallOffset;
            var wall = CreateBlock(WallPrefab, basePos, Quaternion.Euler(0, yaw, 0), room);
            if (wall != null)
                room.Entities.Add(wall);
        }

        private void PlaceEdgeWalls(Vector3 cellPos, float size, bool frameNeeded, float yaw, string direction, ActiveDungeon room)
        {
            if (!frameNeeded)
                return;
            for (var i = 0; i < SubCells; i++)
            {
                float x,
                    z;
                if (direction is "north" or "south")
                {
                    x = cellPos.x + i * SubCellSpacing + SubCellSpacing * 0.5f;
                    z = direction == "north" ? cellPos.z + size : cellPos.z;
                }
                else
                {
                    x = direction == "east" ? cellPos.x + size : cellPos.x;
                    z = cellPos.z + i * SubCellSpacing + SubCellSpacing * 0.5f;
                }
                var basePos = new Vector3(x, cellPos.y, z) + _wallOffset;
                if (room.OccupiedPositions.Contains(PositionKey(basePos, 2)))
                    continue;
                var frameEnt = CreateBlock(WallFramePrefab, basePos, Quaternion.Euler(0, yaw, 0), room);
                if (frameEnt == null)
                    continue;
                room.Entities.Add(frameEnt);
                CreateDoor(basePos, Quaternion.Euler(0, yaw, 0), room);
            }
        }

        private void PlaceCorridorFrame(string side, Vector3 cellPos, float size, ActiveDungeon room)
        {
            float yaw;
            switch (side)
            {
                case "east":
                    yaw = 90f + WallRotationOffset;
                    break;
                case "west":
                    yaw = 270f + WallRotationOffset;
                    break;
                case "north":
                    yaw = 0f + WallRotationOffset;
                    break;
                case "south":
                    yaw = 180f + WallRotationOffset;
                    break;
                default:
                    yaw = 0f;
                    break;
            }
            for (var i = 0; i < SubCells; i++)
            {
                float x,
                    z;
                if (side == "east" || side == "west")
                {
                    x = side == "east" ? cellPos.x + size : cellPos.x;
                    z = cellPos.z + i * SubCellSpacing + SubCellSpacing * 0.5f;
                }
                else
                {
                    x = cellPos.x + i * SubCellSpacing + SubCellSpacing * 0.5f;
                    z = side == "north" ? cellPos.z + size : cellPos.z;
                }
                var basePos = new Vector3(x, cellPos.y, z) + _wallOffset;
                if (room.OccupiedPositions.Contains(PositionKey(basePos, 2)))
                    continue;
                var frameEnt = CreateBlock(WallFramePrefab, basePos, Quaternion.Euler(0, yaw, 0), room);
                if (frameEnt == null)
                    continue;
                room.Entities.Add(frameEnt);
                CreateDoor(basePos, Quaternion.Euler(0, yaw, 0), room);
            }
        }

        private void BuildFrameGrid(bool[] grid, Vector3 origin, Vector2Int? westCell, Vector2Int? eastCell, ActiveDungeon room)
        {
            var start = origin - new Vector3(_gridSize * CellSpacing * 0.5f, 0, _gridSize * CellSpacing * 0.5f);
            for (var x = 0; x < _gridSize; x++)
            for (var y = 0; y < _gridSize; y++)
            {
                if (!GetGridValue(grid, x, y))
                    continue;
                var cellPos = start + new Vector3(x * CellSpacing, 0, y * CellSpacing);
                ProcessCellForFrames(grid, x, y, cellPos, westCell, eastCell, room);
            }
        }

        private void ProcessCellForFrames(bool[] grid, int x, int y, Vector3 cellPos, Vector2Int? westCell, Vector2Int? eastCell, ActiveDungeon room)
        {
            var size = SubCells * SubCellSpacing;
            var horizontal = GetGridValue(grid, x - 1, y) && GetGridValue(grid, x + 1, y);
            if (horizontal)
            {
                if (!GetGridValue(grid, x - 1, y))
                    PlaceEdgeWalls(cellPos, size, true, 0f + WallRotationOffset, "north", room);
                else
                    PlaceEdgeWalls(cellPos, size, GetGridValue(grid, x, y + 1), 0f + WallRotationOffset, "north", room);
                if (!GetGridValue(grid, x, y - 1))
                    PlaceEdgeWalls(cellPos, size, GetGridValue(grid, x, y - 1), 180f + WallRotationOffset, "south", room);
            }
            else
            {
                if (!GetGridValue(grid, x, y + 1))
                    PlaceEdgeWalls(cellPos, size, GetGridValue(grid, x, y + 1), 0f + WallRotationOffset, "north", room);
                if (!GetGridValue(grid, x, y - 1))
                    PlaceEdgeWalls(cellPos, size, GetGridValue(grid, x, y - 1), 180f + WallRotationOffset, "south", room);
            }
            if (!GetGridValue(grid, x + 1, y) && !(eastCell.HasValue && x == eastCell.Value.x && y == eastCell.Value.y))
                PlaceEdgeWalls(cellPos, size, true, 90f + WallRotationOffset, "east", room);
            if (!GetGridValue(grid, x - 1, y) && !(westCell.HasValue && x == westCell.Value.x && y == westCell.Value.y))
                PlaceEdgeWalls(cellPos, size, true, 270f + WallRotationOffset, "west", room);
        }

        private void BuildRoomGrid(bool[] grid, Vector3 origin, Vector2Int? westCell, Vector2Int? eastCell, ActiveDungeon room)
        {
            var start = origin - new Vector3(_gridSize * CellSpacing * 0.5f, 0, _gridSize * CellSpacing * 0.5f);
            for (var x = 0; x < _gridSize; x++)
            for (var y = 0; y < _gridSize; y++)
            {
                if (!GetGridValue(grid, x, y))
                    continue;
                var cellPos = start + new Vector3(x * CellSpacing, 0, y * CellSpacing);
                ProcessCell(grid, x, y, cellPos, westCell, eastCell, room);
            }
        }

        private void ProcessCell(bool[] grid, int x, int y, Vector3 cellPos, Vector2Int? westCell, Vector2Int? eastCell, ActiveDungeon room)
        {
            var isolated = IsIsolatedCell(grid, x, y);
            for (var sx = 0; sx < SubCells; sx++)
            for (var sy = 0; sy < SubCells; sy++)
            {
                var subPos = cellPos + new Vector3(sx * SubCellSpacing, 0, sy * SubCellSpacing);
                HandleSubCell(subPos, room, !isolated);
            }
            if (PlaceLights)
            {
                var size = SubCells * SubCellSpacing;
                var centre = cellPos + new Vector3(size / 2f, 0, size / 2f);
                CreateLight(centre, room);
            }
        }

        private int GridIndex(int x, int y)
        {
            return x < 0 || x >= _gridSize || y < 0 || y >= _gridSize ? -1 : y * _gridSize + x;
        }

        private bool GetGridValue(bool[] grid, int x, int y)
        {
            var idx = GridIndex(x, y);
            return idx != -1 && grid[idx];
        }

        private void SetGridValue(bool[] grid, int x, int y, bool value)
        {
            var idx = GridIndex(x, y);
            if (idx != -1)
                grid[idx] = value;
        }

        private static int CountActiveCells(bool[] grid)
        {
            return grid.Count(t => t);
        }

        private bool IsIsolatedCell(bool[] grid, int x, int y)
        {
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
                if ((dx != 0 || dy != 0) && GetGridValue(grid, x + dx, y + dy))
                    return false;
            return true;
        }

        private void PruneIsolatedCells(bool[] grid, int targetX, int targetY)
        {
            for (var x = 0; x < _gridSize; x++)
            for (var y = 0; y < _gridSize; y++)
                if (GetGridValue(grid, x, y) && !HasPath(grid, x, y, targetX, targetY))
                    SetGridValue(grid, x, y, false);
        }

        private bool HasPath(bool[] grid, int x, int y, int targetX, int targetY)
        {
            var checkedCells = new List<int>();
            var result = CheckPath(grid, x, y, targetX, targetY, checkedCells);
            checkedCells.Clear();
            return result;
        }

        private bool CheckPath(bool[] grid, int x, int y, int targetX, int targetY, ICollection<int> checkedCells)
        {
            var index = GridIndex(x, y);
            if (index == -1 || checkedCells.Contains(index))
                return false;
            checkedCells.Add(index);
            if (!GetGridValue(grid, x, y))
                return false;
            if (x == targetX && y == targetY)
                return true;
            return CheckPath(grid, x + 1, y, targetX, targetY, checkedCells)
                || CheckPath(grid, x - 1, y, targetX, targetY, checkedCells)
                || CheckPath(grid, x, y + 1, targetX, targetY, checkedCells)
                || CheckPath(grid, x, y - 1, targetX, targetY, checkedCells);
        }

        private static Vector3 RoundVector(Vector3 v, int decimals)
        {
            return new Vector3((float)Math.Round(v.x, decimals), (float)Math.Round(v.y, decimals), (float)Math.Round(v.z, decimals));
        }

        private static string PositionKey(Vector3 pos, int decimals)
        {
            var r = RoundVector(pos, decimals);
            return $"{r.x}_{r.y}_{r.z}";
        }

        private Dictionary<Vector3, ZoneInfo> zones = new Dictionary<Vector3, ZoneInfo>();

        private class ZoneInfo
        {
            public Vector3 Pos;
            public float Dist;
            public Vector3 Size;
            public OBB OBB;
        }

        private class GeneratorConfig
        {
            public Vector3 Origin;
            public bool ClosestToOrigin;
            public float YOffset;
            public float minDist;
            public List<Vector3> blocked;
            public bool AvoidTerrainTopology;
            public TerrainTopology.Enum AvoidedTopology;
            public bool AboveWater;
            public float MaxSlopeDegrees;
            public float CheckSphereRadius;
            public int CheckSphereMask;
            public int MaxAttempts;
            public bool UseSlopeCheck;
            public bool CheckSafeZones;
            public bool ZoneManager;
            public int TerrainMask;
            public bool CheckRocks;
            public List<string> RockCheck;
        }

        private IEnumerator TryFindPosition(GeneratorConfig config, DungeonRequest request, bool IsPortal)
        {
            var count = 0;
            var sp = _config?.SpawnPointConfiguration;
            bool duelistLoaded = Duelist != null && Duelist.IsLoaded && (sp?.CheckDuelist ?? true);
            bool raidableBasesLoaded = RaidableBases != null && RaidableBases.IsLoaded && (sp?.CheckRaidableBases ?? true);
            bool abandonedBasesLoaded = AbandonedBases != null && AbandonedBases.IsLoaded && (sp?.CheckAbandonedBases ?? true);
            int attempts = config.MaxAttempts;
            float half = World.Size * 0.5f;
            if (config.ClosestToOrigin)
            {
                Vector3 bestPosition = Vector3.zero;
                float bestDist = float.MaxValue;
                while (attempts-- > 0)
                {
                    float rx = Random.Range(-half, half);
                    float rz = Random.Range(-half, half);
                    Vector3 candidate = new Vector3(rx, 0f, rz);
                    Vector3 final = CheckCandidate(candidate, config, duelistLoaded, raidableBasesLoaded, abandonedBasesLoaded);
                    if (final != Vector3.zero)
                    {
                        float dist = Vector3.Distance(final, config.Origin);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestPosition = final;
                        }
                    }
                    count++;
                    if (count >= 1000)
                    {
                        count = 0;
                        yield return SmallDelay;
                    }
                }
                if (IsPortal)
                    request.PortalPosition = bestPosition;
                else
                    request.DungeonPosition = bestPosition;
                yield break;
            }
            else
            {
                count = 0;
                while (attempts-- > 0)
                {
                    float rx = Random.Range(-half, half);
                    float rz = Random.Range(-half, half);
                    Vector3 candidate = new Vector3(rx, 0f, rz);
                    Vector3 final = CheckCandidate(candidate, config, duelistLoaded, raidableBasesLoaded, abandonedBasesLoaded);
                    if (final != Vector3.zero)
                    {
                        if (IsPortal)
                            request.PortalPosition = final;
                        else
                            request.DungeonPosition = final;
                        yield break;
                    }
                    count++;
                    if (count >= 1000)
                    {
                        count = 0;
                        yield return SmallDelay;
                    }
                }
            }
            if (IsPortal)
                request.PortalPosition = Vector3.zero;
            else
                request.DungeonPosition = Vector3.zero;
        }

        private Vector3 CheckCandidate(Vector3 position, GeneratorConfig cfg, bool duelistLoaded, bool raidableBasesLoaded, bool abandonedBasesLoaded)
        {
            if (IsPositionTooCloseToExistingDungeons(position, cfg.minDist))
                return Vector3.zero;
            if (IsNearBlocked(position, cfg))
                return Vector3.zero;
            if (cfg.YOffset < 2)
            {
                if (cfg.AboveWater)
                    position.y = WaterLevel.GetWaterOrTerrainSurface(position, false, false);
                else
                    position.y = TerrainMeta.HeightMap.GetHeight(position);
            }
            else
                position.y = cfg.YOffset;
            float slope = TerrainMeta.HeightMap.GetSlope(position);
            if (cfg.UseSlopeCheck && slope > cfg.MaxSlopeDegrees)
                return Vector3.zero;
            if (cfg.AvoidTerrainTopology && InAvoidedTopology(position, cfg))
                return Vector3.zero;
            if (cfg.CheckSphereRadius > 0f && Physics.CheckSphere(position, cfg.CheckSphereRadius, cfg.CheckSphereMask))
                return Vector3.zero;
            if ((_config?.SpawnPointConfiguration?.CheckEntities ?? true) && cfg.CheckSphereRadius > 0f && HasEntities(position, cfg.CheckSphereRadius, cfg.CheckSphereMask))
                return Vector3.zero;
            if (cfg.CheckRocks && (IsInsideRock(position, cfg) || AntiHack.TestInsideTerrain(position)))
                return Vector3.zero;
            if (cfg.CheckSafeZones && SafeZoneBlocked(position))
                return Vector3.zero;
            if (cfg.ZoneManager && ZoneBlocked(position))
                return Vector3.zero;
            if (duelistLoaded && Convert.ToBoolean(Duelist?.Call("DuelistTerritory", position)))
                return Vector3.zero;
            if (raidableBasesLoaded && Convert.ToBoolean(RaidableBases?.Call("EventTerritory", position)))
                return Vector3.zero;
            if (abandonedBasesLoaded && Convert.ToBoolean(AbandonedBases?.Call("EventTerritory", position)))
                return Vector3.zero;
            return position;
        }

        private bool IsPositionTooCloseToExistingDungeons(Vector3 position, float buffer)
        {
            foreach (var dungeon in _activeRooms)
            {
                if (dungeon == null || !dungeon.Spawned)
                    continue;
                Bounds b = dungeon.DungeonBounds;
                float horizontalExtent = Mathf.Max(b.extents.x, b.extents.z);
                float minimumDistance = horizontalExtent + buffer;
                if (Vector3.Distance(position, b.center) < minimumDistance)
                    return true;
            }
            return false;
        }

        private bool IsNearBlocked(Vector3 position, GeneratorConfig cfg)
        {
            if (cfg.blocked == null || cfg.blocked.Count == 0)
                return false;
            for (int i = 0; i < cfg.blocked.Count; i++)
            {
                if (Vector3.Distance(position, cfg.blocked[i]) < cfg.minDist)
                    return true;
            }
            return false;
        }

        private bool InAvoidedTopology(Vector3 pos, GeneratorConfig cfg)
        {
            int top = TerrainMeta.TopologyMap.GetTopology(pos);
            return (top & (int)cfg.AvoidedTopology) != 0;
        }

        private bool IsInsideRock(Vector3 point, GeneratorConfig cfg)
        {
            Physics.queriesHitBackfaces = true;
            bool upward = CheckRockUp(point, cfg);
            Physics.queriesHitBackfaces = false;
            if (upward)
                return true;
            return CheckRockDown(point, cfg);
        }

        private bool CheckRockUp(Vector3 p, GeneratorConfig cfg)
        {
            if (!Physics.Raycast(p, Vector3.up, out RaycastHit hit, 20f, cfg.TerrainMask))
                return false;
            return IsRockName(hit.collider.name, cfg.RockCheck);
        }

        private bool CheckRockDown(Vector3 p, GeneratorConfig cfg)
        {
            Vector3 from = p + Vector3.up * 20f;
            Vector3 dir = p - from;
            RaycastHit[] hits = Physics.RaycastAll(from, dir, dir.magnitude, cfg.TerrainMask);
            for (int i = 0; i < hits.Length; i++)
            {
                if (IsRockName(hits[i].collider.name, cfg.RockCheck))
                    return true;
            }
            return false;
        }

        private bool IsRockName(string s, List<string> terms)
        {
            for (int i = 0; i < terms.Count; i++)
            {
                if (s.IndexOf(terms[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private bool SafeZoneBlocked(Vector3 pos)
        {
            foreach (var t in TriggerSafeZone.allSafeZones)
            {
                if (Vector3Ex.Distance2D(t.transform.position, pos) <= 200f)
                    return true;
            }
            return false;
        }

        private bool ZoneBlocked(Vector3 pos)
        {
            if (zones.Count == 0)
                return false;
            foreach (var kvp in zones)
            {
                var z = kvp.Value;
                if (z.Size != Vector3.zero)
                {
                    if (z.OBB.ClosestPoint(pos) == pos)
                        return true;
                }
                else
                {
                    if (Vector3Ex.Distance2D(z.Pos, pos) <= z.Dist)
                        return true;
                }
            }
            return false;
        }

        private void BlockZones()
        {
            if (ZoneManager == null || !ZoneManager.IsLoaded)
                return;
            zones.Clear();
            var zoneIds = ZoneManager.Call("GetZoneIDs") as string[];
            if (zoneIds == null)
                return;
            for (int i = 0; i < zoneIds.Length; i++)
            {
                var p = ZoneManager.Call("GetZoneLocation", zoneIds[i]);
                if (!(p is Vector3 v) || v == default)
                    continue;
                var zi = new ZoneInfo { Pos = v, OBB = new OBB(v, Vector3.zero, Quaternion.identity) };
                var rr = ZoneManager.Call("GetZoneRadius", zoneIds[i]);
                if (rr is float dist)
                    zi.Dist = dist;
                var sz = ZoneManager.Call("GetZoneSize", zoneIds[i]);
                if (sz is Vector3 s)
                {
                    zi.Size = s;
                    zi.OBB = new OBB(zi.Pos, s, Quaternion.identity);
                }
                zones[v] = zi;
            }
        }

        private bool HasEntities(Vector3 position, float radius, int layers)
        {
            List<BaseEntity> list = Pool.Get<List<BaseEntity>>();
            Vis.Entities(position, radius, list, 2162689);
            if (list.Count > 0)
            {
                Facepunch.Pool.FreeUnmanaged(ref list);
                return true;
            }
            Facepunch.Pool.FreeUnmanaged(ref list);
            return false;
        }
        #endregion
    }
}
 