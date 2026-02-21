using System.Collections.Generic;
using Oxide.Core;
using UnityEngine;
using Network;

namespace Oxide.Plugins
{
    [Info("MPLocker", "Mestre Pardal", "1.0.2")]
    [Description("Locker pessoal remoto: abre via /locker")]
    public class MPLocker : RustPlugin
    {
        #region Constants

        private const string DATA_FILE = "MPLocker";
        private const string LOCKER_ITEM_SHORTNAME = "locker";

        private static readonly HashSet<string> LOCKER_PREFABS = new HashSet<string>
        {
            "locker.deployed",
            "locker_deployed"
        };

        #endregion

        #region Config

        private ConfigData config;

        public class ConfigData
        {
            public string PermissionUse = "mplocker.use";
            public string PermissionGive = "mplocker.give";

            public ulong LockerSkinID = 3659562696;

            public string LockerNameFormat = "MY LOCKER - {player}";

            public bool RequireAuthedTCToOpen = true;
            public bool RequireAuthedTCToRegister = true;

            public string CommandOpen = "locker";
            public string CommandGive = "mplocker";
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<ConfigData>();
                if (config == null) throw new System.Exception();
            }
            catch
            {
                PrintWarning("Config inválida, criando nova config padrão.");
                LoadDefaultConfig();
            }
        }

        protected override void LoadDefaultConfig()
        {
            config = new ConfigData();
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(config, true);

        #endregion

        #region Data

        private StoredData data;

        public class LockerData
        {
            public ulong OwnerID;
            public ulong EntityID;
            public float X;
            public float Y;
            public float Z;
        }

        public class StoredData
        {
            public Dictionary<ulong, LockerData> Lockers = new Dictionary<ulong, LockerData>();
        }

        private void LoadData()
        {
            try
            {
                data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(DATA_FILE);
                if (data == null) data = new StoredData();
            }
            catch
            {
                data = new StoredData();
            }
        }

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(DATA_FILE, data);

        #endregion

        #region Lang

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermUse"] = "Você não tem permissão para usar o /{0}.",
                ["NoPermGive"] = "Você não tem permissão para usar o /{0}.",
                ["AlreadyHas"] = "Você já possui um MyLocker. Quebre o antigo antes de colocar outro.",
                ["MustBeAuthedTCRegister"] = "O MyLocker deve ser colocado dentro da área de um TC onde você esteja autorizado.",
                ["Registered"] = "Este locker agora é o seu MyLocker.",
                ["NoLocker"] = "Digite /kit para pegar seu MyLocker.",
                ["NotFound"] = "Seu MyLocker não foi encontrado (talvez tenha sido destruído).",
                ["LostAuth"] = "Você não está mais autorizado no TC do seu MyLocker.",
                ["NotOwner"] = "Você não é o dono deste MyLocker.",
                ["Given"] = "Você recebeu um MyLocker no inventário.",
                ["ItemDefMissing"] = "ItemDefinition do locker (locker) não encontrado.",
                ["Closed"] = "Locker remoto fechado."
            }, this, "pt-BR");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermUse"] = "You don't have permission to use /{0}.",
                ["NoPermGive"] = "You don't have permission to use /{0}.",
                ["AlreadyHas"] = "You already have a MyLocker. Destroy the old one before placing another.",
                ["MustBeAuthedTCRegister"] = "MyLocker must be placed inside a TC area where you are authorized.",
                ["Registered"] = "This locker is now your MyLocker.",
                ["NoLocker"] = "Type /kit to get your MyLocker.",
                ["NotFound"] = "Your MyLocker was not found (maybe it was destroyed).",
                ["LostAuth"] = "You are no longer authorized on your MyLocker TC.",
                ["NotOwner"] = "You are not the owner of this MyLocker.",
                ["Given"] = "You received a MyLocker in your inventory.",
                ["ItemDefMissing"] = "Locker ItemDefinition (locker) not found.",
                ["Closed"] = "Remote locker closed."
            }, this, "en");
			
			lang.RegisterMessages(new Dictionary<string, string>
			{
				["NoPermUse"] = "No tienes permiso para usar /{0}.",
				["NoPermGive"] = "No tienes permiso para usar /{0}.",
				["AlreadyHas"] = "Ya tienes un MyLocker. Destruye el anterior antes de colocar otro.",
				["MustBeAuthedTCRegister"] = "El MyLocker debe colocarse dentro del área de un TC donde estés autorizado.",
				["Registered"] = "Este locker ahora es tu MyLocker.",
				["NoLocker"] = "Type /kit to get your MyLocker.",
				["NotFound"] = "Tu MyLocker no fue encontrado (quizás fue destruido).",
				["LostAuth"] = "Ya no estás autorizado en el TC de tu MyLocker.",
				["NotOwner"] = "No eres el dueño de este MyLocker.",
				["Given"] = "Has recibido un MyLocker en tu inventario.",
				["ItemDefMissing"] = "ItemDefinition del locker (locker) no encontrado.",
				["Closed"] = "Locker remoto cerrado."
			}, this, "es");
        }

        private string Msg(string key, BasePlayer player = null, params object[] args)
        {
            var text = lang.GetMessage(key, this, player?.UserIDString);
            return args != null && args.Length > 0 ? string.Format(text, args) : text;
        }

        #endregion

        #region Init

        private void Init()
        {
            LoadData();
            permission.RegisterPermission(config.PermissionUse, this);
            permission.RegisterPermission(config.PermissionGive, this);

            AddCovalenceCommand(config.CommandOpen, nameof(CmdOpenLocker));
            AddCovalenceCommand(config.CommandGive, nameof(CmdGiveLocker));
        }

        #endregion

        #region Hooks

        private void OnEntityBuilt(Planner planner, GameObject go)
        {
            var player = planner?.GetOwnerPlayer();
            if (player == null) return;

            var entity = go.ToBaseEntity() as StorageContainer;
            if (entity == null) return;

            if (!IsTargetLocker(entity)) return;

            bool isCandidate = config.LockerSkinID == 0UL || entity.skinID == config.LockerSkinID;
            if (!isCandidate) return;

            if (!HasUse(player))
            {
                player.ChatMessage(Msg("NoPermUse", player, config.CommandOpen));
                timer.Once(0.1f, () =>
                {
                    if (entity != null && !entity.IsDestroyed)
                        entity.Kill();
                });
                return;
            }

            if (data.Lockers.ContainsKey(player.userID))
            {
                player.ChatMessage(Msg("AlreadyHas", player));
                RefundLocker(player);

                timer.Once(0.1f, () =>
                {
                    if (entity != null && !entity.IsDestroyed)
                        entity.Kill();
                });
                return;
            }

            if (config.RequireAuthedTCToRegister)
            {
                var priv = entity.GetBuildingPrivilege();
                if (priv == null || !priv.IsAuthed(player))
                {
                    player.ChatMessage(Msg("MustBeAuthedTCRegister", player));
                    RefundLocker(player);

                    timer.Once(0.1f, () =>
                    {
                        if (entity != null && !entity.IsDestroyed)
                            entity.Kill();
                    });
                    return;
                }
            }

            if (config.LockerSkinID != 0UL && entity.skinID != config.LockerSkinID)
                entity.skinID = config.LockerSkinID;

            var name = GetLockerDisplayName(player);
            if (!string.IsNullOrEmpty(name))
                entity._name = name;

            entity.SendNetworkUpdateImmediate();

            data.Lockers[player.userID] = new LockerData
            {
                OwnerID = player.userID,
                EntityID = entity.net.ID.Value,
                X = entity.transform.position.x,
                Y = entity.transform.position.y,
                Z = entity.transform.position.z
            };

            SaveData();
            player.ChatMessage(Msg("Registered", player));
        }

        private object CanLootEntity(BasePlayer player, StorageContainer container)
        {
            if (!IsTargetLocker(container))
                return null;

            if (!TryGetLockerData(container, out var lockerData))
                return null;

            if (player.userID != lockerData.OwnerID)
                return Msg("NotOwner", player);

            return null;
        }

        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var container = entity as StorageContainer;
            if (container == null) return;
            if (!IsTargetLocker(container)) return;

            if (!TryGetLockerData(container, out var lockerData))
                return;

            var attacker = info?.InitiatorPlayer;

            if (attacker == null)
            {
                info.damageTypes.ScaleAll(0f);
                info.DoHitEffects = false;
                info.HitMaterial = 0;
                return;
            }

            if (attacker.userID != lockerData.OwnerID)
            {
                info.damageTypes.ScaleAll(0f);
                info.DoHitEffects = false;
                info.HitMaterial = 0;
                attacker.ChatMessage(Msg("NotOwner", attacker));
            }
        }

        private void OnEntityKill(BaseNetworkable ent)
        {
            var sc = ent as StorageContainer;
            if (sc == null) return;
            if (!IsTargetLocker(sc)) return;

            if (!TryGetLockerData(sc, out var lockerData))
                return;

            if (data.Lockers.Remove(lockerData.OwnerID))
                SaveData();
        }

        #endregion

        #region Commands

        private void CmdOpenLocker(Oxide.Core.Libraries.Covalence.IPlayer iplayer, string command, string[] args)
        {
            var player = iplayer.Object as BasePlayer;
            if (player == null) return;

            if (!HasUse(player))
            {
                player.ChatMessage(Msg("NoPermUse", player, config.CommandOpen));
                return;
            }

            if (player.inventory.loot.IsLooting() && player.inventory.loot.entitySource is StorageContainer current)
            {
                if (TryGetLockerData(current, out var cd) && cd.OwnerID == player.userID)
                {
                    player.EndLooting();
                    player.ChatMessage(Msg("Closed", player));
                    return;
                }
            }

            if (!data.Lockers.TryGetValue(player.userID, out var lockerData))
            {
                player.ChatMessage(Msg("NoLocker", player));
                return;
            }

            var locker = FindLockerFromData(lockerData);
            if (locker == null || locker.IsDestroyed)
            {
                player.ChatMessage(Msg("NotFound", player));
                data.Lockers.Remove(player.userID);
                SaveData();
                return;
            }

            if (config.RequireAuthedTCToOpen)
            {
                var priv = locker.GetBuildingPrivilege();
                if (priv == null || !priv.IsAuthed(player))
                {
                    player.ChatMessage(Msg("LostAuth", player));
                    return;
                }
            }

            EnsureEntityToPlayer(player, locker);

            timer.Once(0.2f, () =>
            {
                if (player == null || !player.IsConnected) return;
                if (locker == null || locker.IsDestroyed) return;

                StartLooting(player, locker);
            });
        }

        private void CmdGiveLocker(Oxide.Core.Libraries.Covalence.IPlayer iplayer, string command, string[] args)
        {
            var player = iplayer.Object as BasePlayer;
            if (player == null) return;

            if (!HasGive(player))
            {
                player.ChatMessage(Msg("NoPermGive", player, config.CommandGive));
                return;
            }

            if (!GiveLockerItem(player))
            {
                player.ChatMessage(Msg("ItemDefMissing", player));
                return;
            }

            player.ChatMessage(Msg("Given", player));
        }

        #endregion

        #region Loot Mechanics (PortableLocker style)

        private void StartLooting(BasePlayer player, StorageContainer container)
        {
            if (player == null || container == null || container.IsDestroyed) return;

            if (player.inventory.loot.IsLooting())
                player.EndLooting();

            player.inventory.loot.StartLootingEntity(container, false);
            player.inventory.loot.AddContainer(container.inventory);
            player.inventory.loot.PositionChecks = false;
            player.inventory.loot.SendImmediate();
            player.ClientRPCPlayer(null, player, "RPC_OpenLootPanel", container.panelName);
        }

        private void EnsureEntityToPlayer(BasePlayer player, BaseEntity entity)
        {
            var connection = player?.Connection;
            if (connection == null || entity == null || entity.IsDestroyed) return;

            var write = Net.sv.StartWrite();
            if (write == null) return;

            ++connection.validate.entityUpdates;

            var saveInfo = new BaseNetworkable.SaveInfo
            {
                forConnection = connection,
                forDisk = false
            };

            write.PacketID(Message.Type.Entities);
            write.UInt32(connection.validate.entityUpdates);

            entity.ToStreamForNetwork(write, saveInfo);
            write.Send(new SendInfo(connection));
        }

        #endregion

        #region Helpers

        private bool HasUse(BasePlayer player) =>
            permission.UserHasPermission(player.UserIDString, config.PermissionUse);

        private bool HasGive(BasePlayer player) =>
            permission.UserHasPermission(player.UserIDString, config.PermissionGive);

        private bool IsTargetLocker(StorageContainer sc)
        {
            if (sc == null) return false;

            if (LOCKER_PREFABS.Contains(sc.ShortPrefabName))
                return true;

            if (!string.IsNullOrEmpty(sc.ShortPrefabName) && sc.ShortPrefabName.Contains("locker"))
                return true;

            return false;
        }

        private string GetLockerDisplayName(BasePlayer owner)
        {
            if (owner == null || string.IsNullOrEmpty(config.LockerNameFormat))
                return null;

            return config.LockerNameFormat.Replace("{player}", owner.displayName);
        }

        private bool GiveLockerItem(BasePlayer target)
        {
            var def = ItemManager.FindItemDefinition(LOCKER_ITEM_SHORTNAME);
            if (def == null) return false;

            var item = ItemManager.Create(def, 1, config.LockerSkinID);
            if (!target.inventory.GiveItem(item))
                item.Drop(target.GetDropPosition(), target.GetDropVelocity());

            return true;
        }

        private void RefundLocker(BasePlayer player) => GiveLockerItem(player);

        private bool TryGetLockerData(StorageContainer container, out LockerData lockerData)
        {
            foreach (var kvp in data.Lockers)
            {
                if (kvp.Value.EntityID == container.net.ID.Value)
                {
                    lockerData = kvp.Value;
                    return true;
                }
            }

            lockerData = null;
            return false;
        }

        private StorageContainer FindLockerFromData(LockerData lockerData)
        {
            if (lockerData == null) return null;

            var ent = BaseNetworkable.serverEntities.Find(new NetworkableId(lockerData.EntityID)) as StorageContainer;
            if (ent != null && !ent.IsDestroyed && IsTargetLocker(ent))
                return ent;

            var pos = new Vector3(lockerData.X, lockerData.Y, lockerData.Z);
            var nearby = new List<BaseEntity>();
            Vis.Entities(pos, 1.0f, nearby);

            foreach (var e in nearby)
            {
                var sc = e as StorageContainer;
                if (sc == null || sc.IsDestroyed) continue;
                if (!IsTargetLocker(sc)) continue;

                lockerData.EntityID = sc.net.ID.Value;
                SaveData();
                return sc;
            }

            return null;
        }

        #endregion
    }
}
