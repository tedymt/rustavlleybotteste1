using System;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;
using UnityEngine;
using Newtonsoft.Json;
using Rust;

namespace Oxide.Plugins
{
    [Info("Terminator", "Mestre Pardal", "2.0.3")]
    [Description("Minigun que dispara rockets reais de forma leve, usando dano nativo do Rust.")]
    public class Terminator : RustPlugin
    {
        #region Campos / Permissões

        private const string PERMISSION_GIVE = "terminator.give";

        [PluginReference]
        private Plugin Loottable;

        private PluginConfig config;
        private ItemDefinition _ammoItemDef;

        private readonly Dictionary<ulong, int> _fireCounters = new Dictionary<ulong, int>();

        #endregion

        #region Config

        private class PluginConfig
        {
            [JsonProperty("Arma Base (shortname)")]
            public string BaseWeaponShortname { get; set; } = "minigun";

            [JsonProperty("ID da Skin obrigatória (0 = qualquer skin)")]
            public ulong RequiredSkinID { get; set; } = 3499382862;

            [JsonProperty("Nome customizado da arma")]
            public string CustomGunName { get; set; } = "TERMINATOR TABAJARA";

            [JsonProperty("Prefixo do chat")]
            public string ChatPrefix { get; set; } = "<color=#FF0000>[TERMINATOR]</color>";

            [JsonProperty("Tornar arma indestrutível")]
            public bool IsIndestructible { get; set; } = true;

            [JsonProperty("Impedir troca de skin")]
            public bool PreventReskin { get; set; } = true;

            [JsonProperty("Consumir rockets do inventário")]
            public bool ConsumeAmmo { get; set; } = true;

            [JsonProperty("Shortname da rocket usada como munição")]
            public string AmmoShortname { get; set; } = "ammo.rocket.basic";

            [JsonProperty("Auto-recarregar a cada disparo")]
            public bool AutoReload { get; set; } = true;

            [JsonProperty("Tiros por recarga (mín. 1)")]
            public int ShotsPerReload { get; set; } = 2;

            [JsonProperty("Disparos da minigun para gerar 1 rocket (maior = mais leve)")]
            public int BulletsPerRocket { get; set; } = 2;

            [JsonProperty("Cancelar projétil padrão do tiro quando lançar rocket")]
            public bool CancelDefaultBulletOnRocketShot { get; set; } = true;

            [JsonProperty("Prefab da rocket lançada")]
            public string RocketPrefab { get; set; } = "assets/prefabs/ammo/rocket/rocket_basic.prefab";

            [JsonProperty("Remover gravidade do projétil (trajetória reta)")]
            public bool NoProjectileGravity { get; set; } = true;
        }

        protected override void LoadDefaultConfig()
        {
            config = new PluginConfig();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<PluginConfig>();
                if (config == null)
                {
                    PrintWarning("Config nula, carregando padrão.");
                    LoadDefaultConfig();
                }
            }
            catch (Exception e)
            {
                PrintError($"Erro ao ler config, usando padrão. Erro: {e}");
                LoadDefaultConfig();
            }

            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        #endregion

        #region Init / Lang / Permissões

        private void Init()
        {
            if (config == null)
                LoadDefaultConfig();

            permission.RegisterPermission(PERMISSION_GIVE, this);

            if (config.ConsumeAmmo)
            {
                _ammoItemDef = ItemManager.FindItemDefinition(config.AmmoShortname);
                if (_ammoItemDef == null)
                    PrintError($"Item '{config.AmmoShortname}' não encontrado! O consumo de munição vai falhar.");
            }

            // pt-BR
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermissionCmd"] = "Você não tem permissão para usar este comando.",
                ["InventoryFull"] = "Seu inventário está cheio!",
                ["ReceivedWeapon"] = "Você recebeu a lendária TERMINATOR TABAJARA!",
                ["CannotChangeSkin"] = "Você não pode trocar a skin desta arma lendária.",
                ["NoAmmo"] = "Acabaram as <color=orange>{0}</color> da TERMINATOR!",
                ["NeedAmmo"] = "Você precisa de <color=orange>{0}</color> para usar a TERMINATOR."
            }, this, "pt-BR");

            // EN
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermissionCmd"] = "You do not have permission to use this command.",
                ["InventoryFull"] = "Your inventory is full!",
                ["ReceivedWeapon"] = "You received the legendary TERMINATOR TABAJARA!",
                ["CannotChangeSkin"] = "You cannot change the skin of this legendary weapon.",
                ["NoAmmo"] = "You ran out of <color=orange>{0}</color> for the TERMINATOR!",
                ["NeedAmmo"] = "You need <color=orange>{0}</color> to use the TERMINATOR."
            }, this, "en");

            // ES
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermissionCmd"] = "No tienes permiso para usar este comando.",
                ["InventoryFull"] = "¡Tu inventario está lleno!",
                ["ReceivedWeapon"] = "¡Has recibido la legendaria TERMINATOR TABAJARA!",
                ["CannotChangeSkin"] = "No puedes cambiar la skin de esta arma legendaria.",
                ["NoAmmo"] = "¡Te quedaste sin <color=orange>{0}</color> para la TERMINATOR!",
                ["NeedAmmo"] = "Necesitas <color=orange>{0}</color> para usar la TERMINATOR."
            }, this, "es");
        }

        private void OnServerInitialized()
        {
            if (config == null || string.IsNullOrEmpty(config.CustomGunName))
                return;

            var allItems = new List<Item>();

            foreach (var player in BasePlayer.allPlayerList)
            {
                if (player?.inventory == null)
                    continue;

                player.inventory.GetAllItems(allItems);
            }

            foreach (var container in UnityEngine.Object.FindObjectsOfType<StorageContainer>())
            {
                if (container?.inventory?.itemList == null)
                    continue;

                allItems.AddRange(container.inventory.itemList);
            }

            foreach (var item in allItems)
            {
                CheckAndRenameItem(item);
            }

            if (Loottable != null && Loottable.IsLoaded)
            {
                var weaponDef = ItemManager.FindItemDefinition(config.BaseWeaponShortname);
                if (weaponDef != null)
                {
                    Loottable.Call("AddCustomItem", this, weaponDef.itemid, config.RequiredSkinID, config.CustomGunName, true);
                }
            }
        }

        #endregion

        #region Comandos / Utilitários de Item

        [ChatCommand("terminator")]
        private void GiveCommand(BasePlayer player, string command, string[] args)
        {
            if (config == null)
                return;

            if (!permission.UserHasPermission(player.UserIDString, PERMISSION_GIVE))
            {
                player.ChatMessage(config.ChatPrefix + " " + lang.GetMessage("NoPermissionCmd", this, player.UserIDString));
                return;
            }

            var itemDef = ItemManager.FindItemDefinition(config.BaseWeaponShortname);
            if (itemDef == null)
            {
                player.ChatMessage(config.ChatPrefix + " <color=red>Arma base não encontrada na config!</color>");
                return;
            }

            Item item = ItemManager.Create(itemDef, 1, config.RequiredSkinID);
            if (item == null)
                return;

            item.name = config.CustomGunName;

            if (!player.inventory.GiveItem(item))
            {
                item.Drop(player.transform.position, player.transform.forward * 2f);
                player.ChatMessage(config.ChatPrefix + " " + lang.GetMessage("InventoryFull", this, player.UserIDString));
            }
            else
            {
                player.ChatMessage(config.ChatPrefix + " " + lang.GetMessage("ReceivedWeapon", this, player.UserIDString));
            }
        }

        private void CheckAndRenameItem(Item item)
        {
            if (config == null || item == null)
                return;

            if (!IsTerminator(item))
                return;

            if (item.name != config.CustomGunName)
            {
                item.name = config.CustomGunName;
                item.MarkDirty();
            }
        }

        private bool IsTerminator(Item item)
        {
            if (config == null || item?.info?.shortname != config.BaseWeaponShortname)
                return false;

            if (config.RequiredSkinID == 0)
                return true;

            return item.skin == config.RequiredSkinID;
        }

        #endregion

        #region Hooks de Item (skin / condição)

        private object OnItemSkinChange(Item item, ulong newSkinID)
        {
            if (config == null || item == null || !IsTerminator(item))
                return null;

            if (config.PreventReskin && newSkinID != item.skin)
            {
                var owner = item.GetOwnerPlayer();
                if (owner != null)
                {
                    owner.ChatMessage(lang.GetMessage("CannotChangeSkin", this, owner.UserIDString));
                }
                return true;
            }

            return null;
        }

        private void OnLoseCondition(Item item, ref float amount)
        {
            if (config != null && IsTerminator(item) && config.IsIndestructible)
            {
                amount = 0f;
            }
        }

        private object CanAcceptItem(ItemContainer container, Item item, int targetPos)
        {
            if (config != null && container?.playerOwner != null && IsTerminator(item))
            {
                CheckAndRenameItem(item);
            }
            return null;
        }

        #endregion

        #region Fogo da Arma / Rockets

        private void OnWeaponFired(BaseProjectile weapon, BasePlayer player, ItemModProjectile mod, ProtoBuf.ProjectileShoot projectiles)
        {
            if (config == null || weapon == null || player == null)
                return;

            var heldItem = weapon.GetItem();
            if (heldItem == null || !IsTerminator(heldItem))
                return;

            int count;
            _fireCounters.TryGetValue(player.userID, out count);
            count++;
            _fireCounters[player.userID] = count;

            bool shouldLaunchRocket = config.BulletsPerRocket <= 1 || (count % Mathf.Max(1, config.BulletsPerRocket) == 0);

            if (shouldLaunchRocket)
            {
                if (config.ConsumeAmmo)
                {
                    if (_ammoItemDef == null || player.inventory.GetAmount(_ammoItemDef.itemid) <= 0)
                    {
                        string ammoName = _ammoItemDef != null ? _ammoItemDef.displayName.english : config.AmmoShortname;
                        player.ChatMessage(config.ChatPrefix + " " + string.Format(lang.GetMessage("NoAmmo", this, player.UserIDString), ammoName));

                        if (config.CancelDefaultBulletOnRocketShot && projectiles != null)
                            projectiles.projectiles.Clear();

                        return;
                    }

                    player.inventory.Take(null, _ammoItemDef.itemid, 1);
                }

                if (config.CancelDefaultBulletOnRocketShot && projectiles != null)
                {
                    projectiles.projectiles.Clear();
                }

                LaunchRocket(player);
            }

            if (config.AutoReload && weapon.primaryMagazine != null)
            {
                int cap = weapon.primaryMagazine.capacity > 0 ? weapon.primaryMagazine.capacity : 2;
                int shots = Mathf.Clamp(config.ShotsPerReload, 1, cap);

                NextTick(() =>
                {
                    if (weapon == null || weapon.IsDestroyed)
                        return;

                    var item = weapon.GetItem();
                    if (!IsTerminator(item))
                        return;

                    weapon.primaryMagazine.contents = shots;
                    weapon.SendNetworkUpdateImmediate();
                });
            }
        }

        private void LaunchRocket(BasePlayer player)
        {
            if (player == null || config == null)
                return;

            string prefab = string.IsNullOrEmpty(config.RocketPrefab)
                ? "assets/prefabs/ammo/rocket/rocket_basic.prefab"
                : config.RocketPrefab;

            Vector3 pos = player.eyes.position + player.eyes.BodyForward() * 0.5f;
            Quaternion rot = Quaternion.LookRotation(player.eyes.BodyForward());

            BaseEntity rocketEnt = GameManager.server.CreateEntity(prefab, pos, rot);
            if (rocketEnt == null)
            {
                PrintError($"Falha ao criar entidade da rocket: {prefab}");
                return;
            }

            rocketEnt.creatorEntity = player;
            rocketEnt.OwnerID = player.userID;

            var serverProjectile = rocketEnt.GetComponent<ServerProjectile>();
            if (serverProjectile != null)
            {
                if (config.NoProjectileGravity)
                {
                    serverProjectile.gravityModifier = 0f;
                    serverProjectile.drag = 0f;
                }

                Vector3 velocity = player.eyes.BodyForward() * serverProjectile.speed;
                serverProjectile.InitializeVelocity(velocity);
            }

            var rb = rocketEnt.GetComponent<Rigidbody>();
            if (rb != null && config.NoProjectileGravity)
            {
                rb.useGravity = false;
            }

            rocketEnt.Spawn();
        }

        #endregion
    }
}
