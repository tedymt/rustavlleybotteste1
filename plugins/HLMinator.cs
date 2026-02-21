using System;
using System.Collections.Generic;
using Oxide.Core;
using UnityEngine;
using Newtonsoft.Json;
using Rust;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("HLMinator", "Mestre Pardal", "1.6.1")]
    [Description("HMLMG explosiva ajustável. Dano, raio e multiplicadores por tipo de alvo na config.")]
    public class HLMinator : RustPlugin
    {
        #region Variáveis Globais

        [PluginReference]
        private Plugin Loottable;

        private PluginConfig config;

        private const string PERMISSION_GIVE = "hlminator.give";

        private ItemDefinition _ammoItemDef;
        private bool _processingExplosion;

        #endregion

        #region Configuração

        private class PluginConfig
        {
            [JsonProperty("Arma Base (Shortname)")]
            public string BaseWeaponShortname { get; set; } = "hmlmg";

            [JsonProperty("ID da Skin (Use 0 para qualquer skin)")]
            public ulong RequiredSkinID { get; set; } = 3634029859;

            [JsonProperty("Nome Customizado da Arma")]
            public string CustomGunName { get; set; } = "HLMINATOR TABAJARA";

            [JsonProperty("Dano Base da Explosão (antes dos multiplicadores)")]
            public float ExplosionDamageAmount { get; set; } = 400f;

            [JsonProperty("Raio da Explosão (metros)")]
            public float ExplosionRadius { get; set; } = 4f;

            [JsonProperty("Multiplicador de dano contra Jogadores (%)")]
            public float PlayerDamagePercent { get; set; } = 100f;

            [JsonProperty("Multiplicador de dano contra NPCs (%)")]
            public float NpcDamagePercent { get; set; } = 100f;

            [JsonProperty("Multiplicador contra Construções (BuildingBlock) (%)")]
            public float BuildingDamagePercent { get; set; } = 50f;

            [JsonProperty("Multiplicador contra Deployables (caixas, armadilhas etc.) (%)")]
            public float DeployableDamagePercent { get; set; } = 100f;

            [JsonProperty("Multiplicador contra Helicóptero Patrol (%)")]
            public float HelicopterDamagePercent { get; set; } = 75f;

            [JsonProperty("Multiplicador contra Bradley APC (%)")]
            public float BradleyDamagePercent { get; set; } = 60f;

            [JsonProperty("Multiplicador contra Outros alvos (%)")]
            public float OtherDamagePercent { get; set; } = 100f;

            [JsonProperty("Caminho do Prefab do Efeito de Explosão")]
            public string ExplosionEffectPrefab { get; set; } = "assets/prefabs/weapons/rocketlauncher/effects/rocket_explosion_hv.prefab";

            [JsonProperty("Consumir munição especial do inventário")]
            public bool ConsumeAmmo { get; set; } = true;

            [JsonProperty("Shortname da Munição (para consumo)")]
            public string AmmoShortname { get; set; } = "ammo.rocket.hv";

            [JsonProperty("Tornar a Arma Indestrutível")]
            public bool IsIndestructible { get; set; } = true;

            [JsonProperty("Impedir troca de skin")]
            public bool PreventReskin { get; set; } = true;

            [JsonProperty("Prefixo do Chat")]
            public string ChatPrefix { get; set; } = "<color=#FF0000>[HLMinator]</color>";
        }

        protected override void LoadDefaultConfig()
        {
            config = new PluginConfig();
            Config.WriteObject(config, true);
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<PluginConfig>();
                if (config == null)
                {
                    LoadDefaultConfig();
                }
            }
            catch
            {
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        #endregion

        #region Hooks do uMod

        private void Init()
        {
            if (config == null)
                LoadDefaultConfig();

            permission.RegisterPermission(PERMISSION_GIVE, this);

            if (config.ConsumeAmmo)
            {
                _ammoItemDef = ItemManager.FindItemDefinition(config.AmmoShortname);
                if (_ammoItemDef == null)
                    PrintError($"Item '{config.AmmoShortname}' não encontrado! O consumo falhará.");
            }

            // Português (Brasil)
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["CannotChangeSkin"] = "Você não pode trocar a skin desta arma lendária.",
                ["NoAmmo"] = "Você precisa de <color=orange>{0}</color> para usar a HLMINATOR.",
                ["ReceivedWeapon"] = "Você recebeu a lendária HLMINATOR!",
                ["InventoryFull"] = "Seu inventário está cheio!",
                ["NoPermissionCmd"] = "Você não tem permissão para usar este comando."
            }, this, "pt-BR");

            // English
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["CannotChangeSkin"] = "You cannot change the skin of this legendary weapon.",
                ["NoAmmo"] = "You need <color=orange>{0}</color> to use the HLMINATOR.",
                ["ReceivedWeapon"] = "You received the legendary HLMINATOR!",
                ["InventoryFull"] = "Your inventory is full!",
                ["NoPermissionCmd"] = "You do not have permission to use this command."
            }, this, "en");

            // Español
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["CannotChangeSkin"] = "No puedes cambiar la skin de esta arma legendaria.",
                ["NoAmmo"] = "Necesitas <color=orange>{0}</color> para usar la HLMINATOR.",
                ["ReceivedWeapon"] = "¡Has recibido la legendaria HLMINATOR!",
                ["InventoryFull"] = "¡Tu inventario está lleno!",
                ["NoPermissionCmd"] = "No tienes permiso para usar este comando."
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

        [ChatCommand("hlminator")]
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
                return;

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

        private object CanAcceptItem(ItemContainer container, Item item, int targetPos)
        {
            if (config != null && container?.playerOwner != null && IsHLMinator(item))
            {
                CheckAndRenameItem(item);
            }
            return null;
        }

        private object OnItemSkinChange(Item item, ulong newSkinID)
        {
            if (config == null || item == null || !IsHLMinator(item))
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
            if (config != null && IsHLMinator(item) && config.IsIndestructible)
            {
                amount = 0f;
            }
        }

        private void OnWeaponFired(BaseProjectile weapon, BasePlayer player, ItemModProjectile mod, ProtoBuf.ProjectileShoot projectiles)
        {
            if (config == null || weapon == null || player == null || !IsHLMinator(weapon.GetItem()))
                return;

            if (config.ConsumeAmmo)
            {
                if (_ammoItemDef == null || player.inventory.GetAmount(_ammoItemDef.itemid) <= 0)
                {
                    string ammoName = _ammoItemDef != null ? _ammoItemDef.displayName.english : config.AmmoShortname;
                    player.ChatMessage(string.Format(lang.GetMessage("NoAmmo", this, player.UserIDString), ammoName));
                    projectiles.projectiles.Clear();
                    return;
                }

                player.inventory.Take(null, _ammoItemDef.itemid, 1);
            }

        }

        private void OnPlayerAttack(BasePlayer player, HitInfo info)
        {
            if (config == null || player == null || info == null)
                return;

            var weapon = info.Weapon as BaseProjectile;
            if (weapon == null)
                return;

            var item = weapon.GetItem();
            if (!IsHLMinator(item))
                return;

            if (info.HitEntity != null && info.HitEntity is BaseCombatEntity)
                return;

            Vector3 pos = info.HitPositionWorld;

            if (pos == Vector3.zero)
            {
                pos = player.eyes.position + (player.eyes.BodyForward() * 15f);
            }

            if (!string.IsNullOrEmpty(config.ExplosionEffectPrefab))
            {
                Effect.server.Run(config.ExplosionEffectPrefab, pos);
            }
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (config == null || info == null)
                return null;

            if (_processingExplosion)
                return null;

            var owner = info.InitiatorPlayer;
            if (owner == null)
                return null;

            var weapon = info.Weapon as BaseProjectile;
            if (weapon == null)
                return null;

            var item = weapon.GetItem();
            if (item == null || !IsHLMinator(item))
                return null;

            Vector3 pos = info.HitPositionWorld;

            if (pos == Vector3.zero)
            {
                if (entity != null)
                    pos = entity.transform.position;
                else
                    pos = owner.eyes.position + (owner.eyes.BodyForward() * 15f);
            }

            DoHybridExplosion(pos, owner, info.WeaponPrefab, entity);

            return true;
        }

        private object OnProjectileHit(Projectile projectile, HitInfo info)
        {
            if (config == null || projectile == null || info == null)
                return null;

            if (info.HitEntity != null && info.HitEntity is BaseCombatEntity)
                return null;

            var owner = projectile.owner as BasePlayer;
            if (owner == null)
                return null;

            var heldItem = owner.GetActiveItem();
            if (heldItem == null || !IsHLMinator(heldItem))
                return null;

            Vector3 pos = info.HitPositionWorld;
            if (pos == Vector3.zero)
            {
                pos = owner.eyes.position + (owner.eyes.BodyForward() * 15f);
            }

            DoHybridExplosion(pos, owner, projectile.sourceWeaponPrefab, null);
            return true;
        }

        #endregion

        #region Funções de Explosão

        private void CheckAndRenameItem(Item item)
        {
            if (config == null || item == null)
                return;

            if (!IsHLMinator(item))
                return;

            if (item.name != config.CustomGunName)
            {
                item.name = config.CustomGunName;
                item.MarkDirty();
            }
        }

        private void DoHybridExplosion(Vector3 position, BasePlayer owner, BaseEntity weaponPrefab, BaseCombatEntity directHitTarget)
        {
            if (config == null)
                return;

            float radius = config.ExplosionRadius <= 0f ? 4f : config.ExplosionRadius;

            var hitEntities = new List<BaseCombatEntity>();
            Vis.Entities(position, radius, hitEntities);

            if (directHitTarget != null && !hitEntities.Contains(directHitTarget))
            {
                hitEntities.Add(directHitTarget);
            }

            _processingExplosion = true;
            try
            {
                foreach (var entity in hitEntities)
                {
                    if (entity == null || entity.IsDestroyed)
                        continue;

                    float distance = Vector3.Distance(entity.ClosestPoint(position), position);
                    if (entity == directHitTarget)
                    {
                        distance = 0f;
                    }

                    float falloff = Mathf.Clamp01(distance / radius);
                    float baseDamage = config.ExplosionDamageAmount * (1f - falloff);
                    if (baseDamage <= 0f)
                        continue;

                    float percent = GetDamageMultiplierPercent(entity);
                    if (percent <= 0f)
                        continue;

                    float finalDamage = baseDamage * (percent / 100f);
                    if (finalDamage <= 0.01f)
                        continue;

                    entity.Hurt(finalDamage, DamageType.Explosion, owner, true);
                }
            }
            finally
            {
                _processingExplosion = false;
            }

            if (!string.IsNullOrEmpty(config.ExplosionEffectPrefab))
            {
                Effect.server.Run(config.ExplosionEffectPrefab, position);
            }
        }

        private float GetDamageMultiplierPercent(BaseCombatEntity entity)
        {
            if (entity is BasePlayer)
                return config.PlayerDamagePercent;

            if (entity is BaseNpc)
                return config.NpcDamagePercent;

            if (entity is BuildingBlock)
                return config.BuildingDamagePercent;

            if (entity is BaseHelicopter)
                return config.HelicopterDamagePercent;

            if (entity is BradleyAPC)
                return config.BradleyDamagePercent;

            if (entity is DecayEntity && !(entity is BuildingBlock))
                return config.DeployableDamagePercent;

            return config.OtherDamagePercent;
        }

        private bool IsHLMinator(Item item)
        {
            if (config == null || item?.info?.shortname != config.BaseWeaponShortname)
                return false;

            if (config.RequiredSkinID == 0)
                return true;

            return item.skin == config.RequiredSkinID;
        }

        #endregion
    }
}
