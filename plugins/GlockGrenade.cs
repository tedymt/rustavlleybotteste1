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
    [Info("GlockGrenade", "Remade by Finn", "1.1.1")]
    [Description("Dispara granadas F1 com uma pistola Prototype 17 específica.")]
    public class GlockGrenade : RustPlugin
    {
        #region Variáveis Globais

        [PluginReference]
        private Plugin Loottable;

        private PluginConfig config;
        
        private const string PERMISSION_USE = "glockgrenade.use";
        private const string GRENADE_AMMO_SHORTNAME = "grenade.f1";
        private ItemDefinition _grenadeAmmoItemDef;
        
        #endregion

        #region Configuração

        private class PluginConfig
        {
            [JsonProperty("Arma Base (Shortname)")]
            public string BaseWeaponShortname { get; set; } = "pistol.prototype17";

            [JsonProperty("ID da Skin (Use 0 para qualquer skin)")]
            public ulong RequiredSkinID { get; set; } = 3522459096;

            [JsonProperty("Nome Customizado da Arma")]
            public string CustomGunName { get; set; } = "Glock Grenade";

            [JsonProperty("Dano de Durabilidade por Disparo Especial")]
            public float DurabilityDamagePerShot { get; set; } = 3.0f;

            [JsonProperty("Tornar a Arma Indestrutível")]
            public bool IsIndestructible { get; set; } = true;
            
            [JsonProperty("Impedir que jogadores troquem a skin da Arma")]
            public bool PreventReskin { get; set; } = true;
            
            [JsonProperty("Caminho do Prefab da Granada F1")]
            public string GrenadePrefabPath { get; set; } = "assets/prefabs/weapons/f1 grenade/grenade.f1.deployed.prefab";

            [JsonProperty("Dispersão da Granada (Aimcone)")]
            public float GrenadeSpread { get; set; } = 3.0f;
            
            [JsonProperty("Velocidade da Granada Lançada")]
            public float GrenadeVelocity { get; set; } = 15.0f;

            [JsonProperty("Prefixo do Chat")]
            public string ChatPrefix { get; set; } = "<color=#7FFF00>[GlockGrenade]</color>";
        }

        protected override void LoadDefaultConfig()
        {
            Config.WriteObject(new PluginConfig
            {
                BaseWeaponShortname = "pistol.prototype17",
                RequiredSkinID = 0,
                CustomGunName = "Glock Grenade",
                DurabilityDamagePerShot = 3.0f,
                IsIndestructible = false,
                PreventReskin = true,
                GrenadePrefabPath = "assets/prefabs/weapons/f1 grenade/grenade.f1.deployed.prefab",
                GrenadeSpread = 3.0f,
                GrenadeVelocity = 15.0f,
                ChatPrefix = "<color=#7FFF00>[GlockGrenade]</color>"
            }, true);
        }
        
        #endregion

        #region Hooks do uMod

        private void Init()
        {
            config = Config.ReadObject<PluginConfig>();
            Config.WriteObject(config, true);
            permission.RegisterPermission(PERMISSION_USE, this);
            
            _grenadeAmmoItemDef = ItemManager.FindItemDefinition(GRENADE_AMMO_SHORTNAME);
            if (_grenadeAmmoItemDef == null)
            {
                PrintError($"Não foi possível encontrar a definição do item para '{GRENADE_AMMO_SHORTNAME}'. O consumo de granadas não funcionará.");
            }
            
            // --- MENSAGENS EM PORTUGUÊS (BR) ---
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["CannotChangeSkin"] = "Você não pode trocar a skin deste item especial.",
                ["NoGrenades"] = "<color=red>Você precisa de pelo menos 1 Granada F1 para usar esta habilidade.</color>",
                ["NoPermission"] = "<color=red>Você não tem permissão para usar os poderes da Glock Grenade.</color>"
            }, this, "pt-BR");

            // --- MENSAGENS EM INGLÊS (EN) ---
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["CannotChangeSkin"] = "You cannot change the skin of this special item.",
                ["NoGrenades"] = "<color=red>You need at least 1 F1 Grenade to use this ability.</color>",
                ["NoPermission"] = "<color=red>You do not have permission to use the powers of the Glock Grenade.</color>"
            }, this, "en");

            // --- MENSAGENS EM ESPANHOL (ES) ---
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["CannotChangeSkin"] = "No puedes cambiar la skin de este objeto especial.",
                ["NoGrenades"] = "<color=red>Necesitas al menos 1 Granada F1 para usar esta habilidad.</color>",
                ["NoPermission"] = "<color=red>No tienes permiso para usar los poderes de la Glock Grenade.</color>"
            }, this, "es-ES");
        }
        
        void OnServerInitialized()
        {
            if (string.IsNullOrEmpty(config.CustomGunName)) return;

            Puts("Verificando inventários para renomear Glocks Grenade existentes...");
            var allItems = new List<Item>();
            foreach (var player in BasePlayer.allPlayerList)
            {
                player.inventory.GetAllItems(allItems);
            }
            foreach (var container in UnityEngine.Object.FindObjectsOfType<StorageContainer>())
            {
                if (container.inventory?.itemList != null)
                {
                    allItems.AddRange(container.inventory.itemList);
                }
            }

            foreach (var item in allItems)
            {
                CheckAndRenameItem(item);
            }
            Puts("Verificação de nomes das Glocks Grenade concluída.");

            if (Loottable != null && Loottable.IsLoaded)
            {
                var weaponDef = ItemManager.FindItemDefinition(config.BaseWeaponShortname);
                if (weaponDef != null)
                {
                    Loottable.Call("AddCustomItem", this, weaponDef.itemid, config.RequiredSkinID, config.CustomGunName, true);
                    Puts("GlockGrenade registrada com sucesso no plugin Loottable.");
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
            if (item == null) return null;

            if (config.PreventReskin && IsGlockLauncher(item) && newSkinID != item.skin)
            {
                BasePlayer owner = item.GetOwnerPlayer();
                if (owner != null)
                {
                    owner.ChatMessage(lang.GetMessage("CannotChangeSkin", this, owner.UserIDString));
                }
                return true; 
            }
            
            NextTick(() =>
            {
                if (item != null && !item.isBroken)
                {
                    CheckAndRenameItem(item);
                }
            });
            
            return null;
        }

        private void OnLoseCondition(Item item, ref float amount)
        {
            if (IsGlockLauncher(item) && config.IsIndestructible)
            {
                amount = 0f;
            }
        }
        
        private object OnWeaponFired(BaseProjectile weapon, BasePlayer player, ItemModProjectile mod, ProtoBuf.ProjectileShoot projectiles)
        {
            if (weapon == null || player == null || !IsGlockLauncher(weapon.GetItem())) return null;
            
            if (!permission.UserHasPermission(player.UserIDString, PERMISSION_USE))
            {
                player.ChatMessage(lang.GetMessage("NoPermission", this, player.UserIDString));
                return true; 
            }
            
            if (_grenadeAmmoItemDef != null && player.inventory.GetAmount(_grenadeAmmoItemDef.itemid) >= 1)
            {
                player.inventory.Take(null, _grenadeAmmoItemDef.itemid, 1);
                
                try
                {
                    FireGrenade(player);

                    if (!config.IsIndestructible && config.DurabilityDamagePerShot > 0)
                    {
                        weapon.GetItem()?.LoseCondition(config.DurabilityDamagePerShot);
                    }
                }
                catch (Exception ex)
                {
                    PrintError($"Ocorreu um erro no GlockGrenade: {ex}");
                }

                return true;
            }
            else
            {
                player.ChatMessage(lang.GetMessage("NoGrenades", this, player.UserIDString));
                return true;
            }
        }
        
        #endregion

        #region Funções Auxiliares
        
        private void CheckAndRenameItem(Item item)
        {
            if (item == null || item.info.shortname != config.BaseWeaponShortname) return;

            var owner = item.GetOwnerPlayer();
            bool hasPermission = owner != null && permission.UserHasPermission(owner.UserIDString, PERMISSION_USE);
            bool isSpecialWeapon = IsGlockLauncher(item);
            
            string currentName = item.name;
            string expectedName = isSpecialWeapon && hasPermission ? config.CustomGunName : null;

            if (currentName != expectedName)
            {
                item.name = expectedName;
                item.MarkDirty();
            }
        }

        private void FireGrenade(BasePlayer player)
        {
            Vector3 spawnPos = player.eyes.position + player.eyes.HeadForward() * 0.5f;
            Vector3 direction = AimConeUtil.GetModifiedAimConeDirection(config.GrenadeSpread, player.eyes.HeadForward());
            
            BaseEntity grenadeEntity = GameManager.server.CreateEntity(config.GrenadePrefabPath, spawnPos, Quaternion.identity);
            if (grenadeEntity == null) 
            {
                PrintError($"Falha ao criar a entidade da granada. Verifique o GrenadePrefabPath na configuração.");
                return;
            }

            grenadeEntity.OwnerID = player.userID;
            
            var timedExplosive = grenadeEntity.GetComponent<TimedExplosive>();
            if (timedExplosive != null)
            {
                timedExplosive.creatorEntity = player;
            }

            var rigidBody = grenadeEntity.GetComponent<Rigidbody>();
            if (rigidBody != null)
            {
                rigidBody.velocity = direction * config.GrenadeVelocity;
            }
            
            grenadeEntity.Spawn();
        }

        private bool IsGlockLauncher(Item item)
        {
            if (item?.info.shortname != config.BaseWeaponShortname) return false;
            return config.RequiredSkinID == 0 || item.skin == config.RequiredSkinID;
        }

        #endregion
    }
}