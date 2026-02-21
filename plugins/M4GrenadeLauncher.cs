using System;
using System.Collections.Generic;
using Oxide.Core;
using UnityEngine;
using Newtonsoft.Json;
using Rust;

namespace Oxide.Plugins
{
    [Info("M4GrenadeLauncher", "Remade by Finn", "1.6.1")]
    [Description("Dispara 5 granadas de 40mm adicionais, mantendo o tiro original de uma Shotgun M4 específica.")]
    public class M4GrenadeLauncher : RustPlugin
    {
        #region Variáveis Globais

        private PluginConfig config;
        private const string Permission = "m4grenadelauncher.use";
        private const string GrenadeAmmoShortname = "ammo.grenadelauncher.he";
        private ItemDefinition _grenadeAmmoItemDef;
        
        #endregion

        #region Configuração

        private class PluginConfig
        {
            [JsonProperty("Arma Base (Shortname)")]
            public string BaseWeaponShortname { get; set; } = "shotgun.m4";

            [JsonProperty("ID da Skin (Use 0 para qualquer skin)")]
            public ulong RequiredSkinID { get; set; } = 3507984989;

            [JsonProperty("Nome Customizado da Arma")]
            public string CustomGunName { get; set; } = "M4 Grenade Launcher";

            [JsonProperty("Dano de Durabilidade por Disparo Especial")]
            public float DurabilityDamagePerShot { get; set; } = 8.0f;

            [JsonProperty("Tornar a Arma Indestrutível")]
            public bool IsIndestructible { get; set; } = false;
            
            [JsonProperty("Impedir que jogadores troquem a skin da Arma")]
            public bool PreventReskin { get; set; } = true;
            
            [JsonProperty("Caminho do Prefab da Granada de 40mm (HE)")]
            public string GrenadePrefabPath { get; set; } = "assets/prefabs/ammo/40mmgrenade/40mm_grenade_he.prefab";

            [JsonProperty("Dispersão das Granadas (Aimcone)")]
            public float GrenadeSpread { get; set; } = 5.0f;
        }

        protected override void LoadDefaultConfig() => Config.WriteObject(new PluginConfig(), true);
        
        #endregion

        #region Hooks do uMod

        private void Init()
        {
            config = Config.ReadObject<PluginConfig>();
            Config.WriteObject(config, true);
            permission.RegisterPermission(Permission, this);
            
            _grenadeAmmoItemDef = ItemManager.FindItemDefinition(GrenadeAmmoShortname);
            if (_grenadeAmmoItemDef == null)
            {
                PrintError($"Não foi possível encontrar a definição do item para '{GrenadeAmmoShortname}'. O consumo de granadas não funcionará.");
            }
            
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["CannotChangeSkin"] = "Você não pode trocar a skin deste item especial.",
                ["NoGrenades"] = "<color=red>Você precisa de pelo menos 5 granadas de 40mm (HE) para usar esta habilidade.</color>"
            }, this, "pt-BR");
        }
        
        void OnServerInitialized()
        {
            if (string.IsNullOrEmpty(config.CustomGunName)) return;

            Puts("Verificando inventários para renomear M4 Grenade Launchers existentes...");
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
            Puts("Verificação de nomes das M4 Grenade Launchers concluída.");
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

            if (config.PreventReskin && IsM4Launcher(item) && newSkinID != item.skin)
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
            if (IsM4Launcher(item) && config.IsIndestructible)
            {
                amount = 0f;
            }
        }
        
        private void OnWeaponFired(BaseProjectile weapon, BasePlayer player, ItemModProjectile mod, ProtoBuf.ProjectileShoot projectiles)
        {
            if (weapon == null || player == null || !IsM4Launcher(weapon.GetItem())) return;
            if (!permission.UserHasPermission(player.UserIDString, Permission)) return;
            
            // Verifica se o jogador tem 5 ou mais granadas.
            if (_grenadeAmmoItemDef != null && player.inventory.GetAmount(_grenadeAmmoItemDef.itemid) >= 5)
            {
                // Consome 5 granadas.
                player.inventory.Take(null, _grenadeAmmoItemDef.itemid, 5);
                
                try
                {
                    // Dispara 5 granadas com uma certa dispersão.
                    for (int i = 0; i < 5; i++)
                    {
                        FireGrenade(player);
                    }

                    if (!config.IsIndestructible && config.DurabilityDamagePerShot > 0)
                    {
                        weapon.GetItem()?.LoseCondition(config.DurabilityDamagePerShot);
                    }
                }
                catch (Exception ex)
                {
                    PrintError($"Ocorreu um erro no M4GrenadeLauncher: {ex}");
                }
            }
            else
            {
                // Envia a mensagem de aviso se não houver granadas suficientes.
                player.ChatMessage(lang.GetMessage("NoGrenades", this, player.UserIDString));
            }
        }
        
        #endregion

        #region Funções Auxiliares
        
        private void CheckAndRenameItem(Item item)
        {
            if (item == null || item.info.shortname != config.BaseWeaponShortname) return;

            bool isSpecialWeapon = IsM4Launcher(item);
            string currentName = item.name;
            string expectedName = isSpecialWeapon ? config.CustomGunName : null;

            if (currentName != expectedName)
            {
                item.name = expectedName;
                item.MarkDirty();
            }
        }

        private void FireGrenade(BasePlayer player)
        {
            Vector3 spawnPos = player.eyes.position;
            
            Vector3 direction = AimConeUtil.GetModifiedAimConeDirection(config.GrenadeSpread, player.eyes.HeadForward());
            Quaternion spawnRot = Quaternion.LookRotation(direction);
            
            BaseEntity grenadeEntity = GameManager.server.CreateEntity(config.GrenadePrefabPath, spawnPos, spawnRot);
            if (grenadeEntity == null) 
            {
                PrintError("Falha ao criar a entidade da granada. Verifique o GrenadePrefabPath na configuração.");
                return;
            }

            grenadeEntity.creatorEntity = player;
            grenadeEntity.OwnerID = player.userID;

            var serverProjectile = grenadeEntity.GetComponent<ServerProjectile>();
            if (serverProjectile != null)
            {
                serverProjectile.InitializeVelocity(direction * serverProjectile.speed);
            }

            grenadeEntity.Spawn();
        }

        private bool IsM4Launcher(Item item)
        {
            if (item?.info.shortname != config.BaseWeaponShortname) return false;
            return config.RequiredSkinID == 0 || item.skin == config.RequiredSkinID;
        }

        #endregion
    }
}