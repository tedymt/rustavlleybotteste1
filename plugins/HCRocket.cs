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
    [Info("HCRocket", "Remade by Finn", "1.4.1")]
    [Description("Dispara um foguete básico adicional a cada tiro de uma arma específica.")]
    public class HCRocket : RustPlugin
    {
        #region Variáveis

        [PluginReference]
        private Plugin Loottable;

        private PluginConfig config;
        
        private const string PERMISSION_USE = "hcrocket.use";
        private const string ROCKET_AMMO_SHORTNAME = "ammo.rocket.basic";
        private ItemDefinition _rocketItemDef;

        #endregion

        #region Configuração

        private class PluginConfig
        {
            [JsonProperty("Arma Base (Shortname)")]
            public string BaseWeaponShortname { get; set; } = "revolver.hc";

            [JsonProperty("ID da Skin (Use 0 para qualquer skin)")]
            public ulong RequiredSkinID { get; set; } = 3509804983;

            [JsonProperty("Nome Customizado da Arma")]
            public string CustomGunName { get; set; } = "HCRocket";

            [JsonProperty("Dano de Durabilidade por Disparo de Rocket")]
            public float DurabilityDamagePerShot { get; set; } = 2.0f;

            [JsonProperty("Tornar a Arma Indestrutível")]
            public bool IsIndestructible { get; set; } = false;
            
            [JsonProperty("Impedir que jogadores troquem a skin da Arma")]
            public bool PreventReskin { get; set; } = true;
            
            [JsonProperty("Caminho do Prefab do Rocket")]
            public string RocketPrefabPath { get; set; } = "assets/prefabs/ammo/rocket/rocket_basic.prefab";

            [JsonProperty("Velocidade do Rocket (Multiplicador)")]
            public float RocketSpeedMultiplier { get; set; } = 1.0f;
            
            [JsonProperty("Prefixo do Chat")]
            public string ChatPrefix { get; set; } = "<color=#C70039>[HCRocket]</color>";
        }

        protected override void LoadDefaultConfig()
        {
            Config.WriteObject(new PluginConfig
            {
                BaseWeaponShortname = "revolver.hc",
                RequiredSkinID = 0,
                CustomGunName = "HCRocket",
                DurabilityDamagePerShot = 2.0f,
                IsIndestructible = false,
                PreventReskin = true,
                RocketPrefabPath = "assets/prefabs/ammo/rocket/rocket_basic.prefab",
                RocketSpeedMultiplier = 1.0f,
                ChatPrefix = "<color=#C70039>[HCRocket]</color>"
            }, true);
        }
        
        #endregion

        #region Hooks do uMod

        private void Init()
        {
            config = Config.ReadObject<PluginConfig>();
            Config.WriteObject(config, true);
            permission.RegisterPermission(PERMISSION_USE, this);

            _rocketItemDef = ItemManager.FindItemDefinition(ROCKET_AMMO_SHORTNAME);
            if (_rocketItemDef == null)
            {
                PrintError($"Não foi possível encontrar a definição do item para '{ROCKET_AMMO_SHORTNAME}'. O consumo de rockets não funcionará.");
            }

            // --- MENSAGENS EM PORTUGUÊS (BR) ---
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["CannotChangeSkin"] = "Você não pode trocar a skin deste item especial.",
                ["NoRockets"] = "<color=red>Você não tem foguetes básicos para o HCRocket.</color>",
                ["NoPermission"] = "<color=red>Você não tem permissão para usar os poderes do HCRocket.</color>"
            }, this, "pt-BR");

            // --- MENSAGENS EM INGLÊS (EN) ---
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["CannotChangeSkin"] = "You cannot change the skin of this special item.",
                ["NoRockets"] = "<color=red>You do not have basic rockets for the HCRocket.</color>",
                ["NoPermission"] = "<color=red>You do not have permission to use the powers of the HCRocket.</color>"
            }, this, "en");

            // --- MENSAGENS EM ESPANHOL (ES) ---
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["CannotChangeSkin"] = "No puedes cambiar la skin de este objeto especial.",
                ["NoRockets"] = "<color=red>No tienes cohetes básicos para el HCRocket.</color>",
                ["NoPermission"] = "<color=red>No tienes permiso para usar los poderes del HCRocket.</color>"
            }, this, "es-ES");
        }
        
        void OnServerInitialized()
        {
            if (string.IsNullOrEmpty(config.CustomGunName)) return;

            Puts("Verificando inventários para renomear HCRockets existentes...");
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
            Puts("Verificação de nomes do HCRocket concluída.");

            if (Loottable != null && Loottable.IsLoaded)
            {
                var weaponDef = ItemManager.FindItemDefinition(config.BaseWeaponShortname);
                if (weaponDef != null)
                {
                    Loottable.Call("AddCustomItem", this, weaponDef.itemid, config.RequiredSkinID, config.CustomGunName, true);
                    Puts("HCRocket registrado com sucesso no plugin Loottable.");
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

            if (config.PreventReskin && IsHCRocket(item) && newSkinID != item.skin)
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
            if (IsHCRocket(item) && config.IsIndestructible)
            {
                amount = 0f;
            }
        }
        
        private void OnWeaponFired(BaseProjectile weapon, BasePlayer player, ItemModProjectile mod, ProtoBuf.ProjectileShoot projectiles)
        {
            if (weapon == null || player == null || !IsHCRocket(weapon.GetItem())) return;
            
            if (!permission.UserHasPermission(player.UserIDString, PERMISSION_USE))
            {
                player.ChatMessage(lang.GetMessage("NoPermission", this, player.UserIDString));
                return;
            }

            if (_rocketItemDef != null && player.inventory.GetAmount(_rocketItemDef.itemid) > 0)
            {
                player.inventory.Take(null, _rocketItemDef.itemid, 1);
                
                try
                {
                    FireRocket(player, weapon);
                    if (!config.IsIndestructible && config.DurabilityDamagePerShot > 0)
                    {
                        weapon.GetItem()?.LoseCondition(config.DurabilityDamagePerShot);
                    }
                }
                catch (Exception ex)
                {
                    PrintError($"Ocorreu um erro no HCRocket: {ex}");
                }
            }
            else
            {
                player.ChatMessage(lang.GetMessage("NoRockets", this, player.UserIDString));
            }
        }
        
        #endregion

        #region Funções Auxiliares

        private void CheckAndRenameItem(Item item)
        {
            if (item == null || item.info.shortname != config.BaseWeaponShortname) return;

            var owner = item.GetOwnerPlayer();
            bool hasPermission = owner != null && permission.UserHasPermission(owner.UserIDString, PERMISSION_USE);
            bool isSpecialWeapon = IsHCRocket(item);
            
            string currentName = item.name;
            string expectedName = isSpecialWeapon && hasPermission ? config.CustomGunName : null;

            if (currentName != expectedName)
            {
                item.name = expectedName;
                item.MarkDirty();
            }
        }

        private void FireRocket(BasePlayer player, BaseProjectile weapon)
        {
            Vector3 spawnPos = player.eyes.position;
            Quaternion spawnRot = Quaternion.LookRotation(player.eyes.HeadForward());

            BaseEntity missileEntity = GameManager.server.CreateEntity(config.RocketPrefabPath, spawnPos, spawnRot);
            if (missileEntity == null) 
            {
                PrintError("Falha ao criar a entidade do rocket. Verifique o RocketPrefabPath na configuração.");
                return;
            }

            missileEntity.creatorEntity = player;
            missileEntity.OwnerID = player.userID;

            var serverProjectile = missileEntity.GetComponent<ServerProjectile>();
            if (serverProjectile != null)
            {
                serverProjectile.InitializeVelocity(player.eyes.HeadForward() * serverProjectile.speed * config.RocketSpeedMultiplier);
            }

            missileEntity.Spawn();
        }

        private bool IsHCRocket(Item item)
        {
            if (item?.info.shortname != config.BaseWeaponShortname) return false;
            return config.RequiredSkinID == 0 || item.skin == config.RequiredSkinID;
        }

        #endregion
    }
}