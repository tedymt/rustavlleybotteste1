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
    [Info("L96Explosive (Dano Híbrido)", "Remade by Finn", "3.2.1")]
    [Description("Dispara um tiro de L96 que cria uma explosão customizável no impacto, com multiplicadores de dano por tipo de entidade.")]
    public class L96Explosive : RustPlugin
    {
        #region Variáveis Globais

        [PluginReference]
        private Plugin Loottable;

        private PluginConfig config;
        
        private const string PERMISSION_USE = "l96explosive.use";
        private ItemDefinition _c4ItemDef;
        private readonly Dictionary<int, BasePlayer> _trackedBullets = new Dictionary<int, BasePlayer>();

        #endregion

        #region Configuração

        internal enum LockTypes { Player, NPC, Structure, PatrolHelicopter, BradleyAPC, Animal, Other }

        private class PluginConfig
        {
            [JsonProperty("Arma Base (Shortname)")]
            public string BaseWeaponShortname { get; set; } = "rifle.l96";

            [JsonProperty("ID da Skin (Use 0 para qualquer skin)")]
            public ulong RequiredSkinID { get; set; } = 3503172124;

            [JsonProperty("Nome Customizado da Arma")]
            public string CustomGunName { get; set; } = "L96 Overkill";

            [JsonProperty("Consumir uma C4 do inventário a cada tiro")]
            public bool ConsumeC4 { get; set; } = true;

            [JsonProperty("Shortname da C4 (para consumo)")]
            public string C4Shortname { get; set; } = "explosive.timed";

            [JsonProperty("Dano de Durabilidade por Disparo Especial")]
            public float DurabilityDamagePerShot { get; set; } = 10.0f;

            [JsonProperty("Tornar a Arma Indestrutível")]
            public bool IsIndestructible { get; set; } = false;

            [JsonProperty("Impedir que jogadores troquem a skin da Arma")]
            public bool PreventReskin { get; set; } = true;

            [JsonProperty("Raio da Explosão (em metros)")]
            public float ExplosionRadius { get; set; } = 6.0f;

            [JsonProperty("Dano Base da Explosão (será multiplicado)")]
            public float ExplosionDamageAmount { get; set; } = 275f;

            [JsonProperty("Caminho do Prefab do Efeito de Explosão")]
            public string ExplosionEffectPrefab { get; set; } = "assets/prefabs/tools/c4/effects/c4_explosion.prefab";

            [JsonProperty("Multiplicadores de Dano por Tipo de Alvo (1.0 = 100% do dano base)")]
            public Dictionary<LockTypes, float> DamageMultipliers { get; set; }
            
            [JsonProperty("Prefixo do Chat")]
            public string ChatPrefix { get; set; } = "<color=#FFD700>[Overkill]</color>";
        }

        protected override void LoadDefaultConfig()
        {
            Config.WriteObject(new PluginConfig
            {
                BaseWeaponShortname = "rifle.l96",
                RequiredSkinID = 0,
                CustomGunName = "L96 Explosive",
                ConsumeC4 = true,
                C4Shortname = "explosive.timed",
                DurabilityDamagePerShot = 10.0f,
                IsIndestructible = false,
                PreventReskin = true,
                ExplosionRadius = 6.0f,
                ExplosionDamageAmount = 275f,
                ExplosionEffectPrefab = "assets/prefabs/tools/c4/effects/c4_explosion.prefab",
                DamageMultipliers = new Dictionary<LockTypes, float>
                {
                    [LockTypes.Player] = 1.0f,
                    [LockTypes.NPC] = 1.0f,
                    [LockTypes.Structure] = 1.0f,
                    [LockTypes.PatrolHelicopter] = 2.0f,
                    [LockTypes.BradleyAPC] = 2.0f,
                    [LockTypes.Animal] = 1.0f,
                    [LockTypes.Other] = 1.0f
                },
                ChatPrefix = "<color=#FFD700>[Overkill]</color>"
            }, true);
        }

        #endregion

        #region Hooks do uMod

        private void Init()
        {
            config = Config.ReadObject<PluginConfig>();
            Config.WriteObject(config, true);
            permission.RegisterPermission(PERMISSION_USE, this);

            if (config.ConsumeC4)
            {
                _c4ItemDef = ItemManager.FindItemDefinition(config.C4Shortname);
                if (_c4ItemDef == null)
                    PrintError($"Não foi possível encontrar a definição do item para '{config.C4Shortname}'. O consumo de C4 não funcionará.");
            }

            // --- MENSAGENS EM PORTUGUÊS (BR) ---
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["CannotChangeSkin"] = "Você não pode trocar a skin deste item especial.",
                ["NoC4"] = "<color=red>Você não tem C4 para o seu L96 Explosivo.</color>",
                ["NoPermission"] = "<color=red>Você não tem permissão para usar os poderes da L96 Overkill.</color>"
            }, this, "pt-BR");

            // --- MENSAGENS EM INGLÊS (EN) ---
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["CannotChangeSkin"] = "You cannot change the skin of this special item.",
                ["NoC4"] = "<color=red>You do not have C4 for your Explosive L96.</color>",
                ["NoPermission"] = "<color=red>You do not have permission to use the powers of the L96 Overkill.</color>"
            }, this, "en");

            // --- MENSAGENS EM ESPANHOL (ES) ---
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["CannotChangeSkin"] = "No puedes cambiar la skin de este objeto especial.",
                ["NoC4"] = "<color=red>No tienes C4 para tu L96 Explosivo.</color>",
                ["NoPermission"] = "<color=red>No tienes permiso para usar los poderes de la L96 Overkill.</color>"
            }, this, "es-ES");
        }

        private void OnServerInitialized()
        {
            if (string.IsNullOrEmpty(config.CustomGunName)) return;

            Puts("Verificando inventários para renomear L96s Explosivas existentes...");
            var allItems = new List<Item>();
            foreach (var player in BasePlayer.allPlayerList)
            {
                player.inventory.GetAllItems(allItems);
            }
            foreach (var container in UnityEngine.Object.FindObjectsOfType<StorageContainer>())
            {
                if (container.inventory?.itemList != null)
                    allItems.AddRange(container.inventory.itemList);
            }

            foreach (var item in allItems)
                CheckAndRenameItem(item);
            Puts("Verificação de nomes das L96s Explosivas concluída.");

            if (Loottable != null && Loottable.IsLoaded)
            {
                var weaponDef = ItemManager.FindItemDefinition(config.BaseWeaponShortname);
                if (weaponDef != null)
                {
                    Loottable.Call("AddCustomItem", this, weaponDef.itemid, config.RequiredSkinID, config.CustomGunName, true);
                    Puts("L96Explosive registrado com sucesso no plugin Loottable.");
                }
            }
        }
        
        private object CanAcceptItem(ItemContainer container, Item item, int targetPos)
        {
            if (container.playerOwner != null && item.info.shortname == config.BaseWeaponShortname && (config.RequiredSkinID == 0 || item.skin == config.RequiredSkinID))
            {
                CheckAndRenameItem(item);
            }
            return null;
        }
        
        private object OnItemSkinChange(Item item, ulong newSkinID)
        {
            if (item == null || !IsExplosiveL96(item)) return null;

            if (config.PreventReskin && newSkinID != item.skin)
            {
                item.GetOwnerPlayer()?.ChatMessage(lang.GetMessage("CannotChangeSkin", this, item.GetOwnerPlayer().UserIDString));
                return true;
            }
            return null;
        }

        private void OnLoseCondition(Item item, ref float amount)
        {
            if (IsExplosiveL96(item) && config.IsIndestructible)
                amount = 0f;
        }

        private void OnWeaponFired(BaseProjectile weapon, BasePlayer player, ItemModProjectile mod, ProtoBuf.ProjectileShoot projectiles)
        {
            if (weapon == null || player == null || !IsExplosiveL96(weapon.GetItem())) return;
            
            if (!permission.UserHasPermission(player.UserIDString, PERMISSION_USE))
            {
                player.ChatMessage(lang.GetMessage("NoPermission", this, player.UserIDString));
                projectiles.projectiles.Clear();
                return;
            }

            if (config.ConsumeC4)
            {
                if (_c4ItemDef == null || player.inventory.GetAmount(_c4ItemDef.itemid) <= 0)
                {
                    player.ChatMessage(lang.GetMessage("NoC4", this, player.UserIDString));
                    projectiles.projectiles.Clear(); // Cancela o disparo
                    Effect.server.Run("assets/sounds/ui/ui.err.prefab", player.transform.position);
                    return;
                }
                player.inventory.Take(null, _c4ItemDef.itemid, 1);
            }

            if (!config.IsIndestructible)
                weapon.GetItem()?.LoseCondition(config.DurabilityDamagePerShot);
            
            foreach (var projectile in projectiles.projectiles)
            {
                if (!_trackedBullets.ContainsKey(projectile.projectileID))
                    _trackedBullets.Add(projectile.projectileID, player);
            }
        }
        
        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (info?.ProjectileID == 0) return null;

            if (_trackedBullets.TryGetValue(info.ProjectileID, out var owner) && owner != null)
            {
                DoHybridExplosion(info.HitPositionWorld, owner, info.WeaponPrefab);
                _trackedBullets.Remove(info.ProjectileID);
                return true; // Cancela o dano original da bala
            }
            return null;
        }

        #endregion

        #region Funções de Dano e Auxiliares

        private void CheckAndRenameItem(Item item)
        {
            if (item == null || item.info.shortname != config.BaseWeaponShortname) return;

            var owner = item.GetOwnerPlayer();
            bool hasPermission = owner != null && permission.UserHasPermission(owner.UserIDString, PERMISSION_USE);
            bool isSpecialWeapon = IsExplosiveL96(item);
            
            string currentName = item.name;
            string expectedName = isSpecialWeapon && hasPermission ? config.CustomGunName : null;

            if (item.name != expectedName)
            {
                item.name = expectedName;
                item.MarkDirty();
            }
        }

        private void DoHybridExplosion(Vector3 position, BasePlayer owner, BaseEntity weaponPrefab)
        {
            var hitEntities = new List<BaseCombatEntity>();
            Vis.Entities(position, config.ExplosionRadius, hitEntities);

            foreach (var entity in hitEntities)
            {
                if (entity == null || entity.IsDestroyed) continue;

                LockTypes entityType = GetLockTypeFromEntity(entity);
                if (!config.DamageMultipliers.TryGetValue(entityType, out float damageMultiplier))
                    damageMultiplier = 1.0f; 

                float finalDamage = config.ExplosionDamageAmount * damageMultiplier;

                float distance = Vector3.Distance(entity.ClosestPoint(position), position);
                float damageReduction = Mathf.Clamp01(distance / config.ExplosionRadius);
                float scaledDamage = finalDamage * (1f - damageReduction);

                if (scaledDamage > 0)
                {
                    var hitInfo = new HitInfo(owner, entity, DamageType.Explosion, scaledDamage, position)
                    {
                        WeaponPrefab = weaponPrefab
                    };
                    entity.OnAttacked(hitInfo);
                }
            }

            if (!string.IsNullOrEmpty(config.ExplosionEffectPrefab))
                Effect.server.Run(config.ExplosionEffectPrefab, position);
        }

        private LockTypes GetLockTypeFromEntity(BaseEntity entity)
        {
            if (entity is BasePlayer player) return player.IsNpc ? LockTypes.NPC : LockTypes.Player;
            if (entity is PatrolHelicopter) return LockTypes.PatrolHelicopter;
            if (entity is BradleyAPC) return LockTypes.BradleyAPC;
            if (entity is BaseNpc) return LockTypes.Animal; 
            if (entity is BuildingBlock) return LockTypes.Structure;

            return LockTypes.Other;
        }
        
        private bool IsExplosiveL96(Item item)
        {
            if (item?.info.shortname != config.BaseWeaponShortname) return false;
            return config.RequiredSkinID == 0 || item.skin == config.RequiredSkinID;
        }

        #endregion
    }
}