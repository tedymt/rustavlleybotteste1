// File: L96BasicRocket.cs
// Versão: 1.1.0
// Autor: ChatGPT (modificado para velocidade 3x e arma indestrutível)
// Descrição: Dispara um rocket básico 3x mais rápido quando a L96 com skin 2560763767 é usada. Arma indestrutível.

using System;
using System.Collections.Generic;
using Oxide.Core;
using UnityEngine;
using Newtonsoft.Json;
using Rust;

namespace Oxide.Plugins
{
    [Info("L96BasicRocket", "ChatGPT", "1.1.0")]
    [Description("Dispara um rocket básico 3x mais rápido ao usar a rifle.l96 com skin 2560763767.")]
    public class L96BasicRocket : RustPlugin
    {
        private const string PERMISSION_USE = "l96basicrocket.use";
        private const string ROCKET_AMMO_SHORTNAME = "ammo.rocket.basic";

        private ItemDefinition _rocketItemDef;
        private PluginConfig config;

        #region Config
        private class PluginConfig
        {
            [JsonProperty("Arma Base (Shortname)")]
            public string BaseWeaponShortname { get; set; } = "rifle.l96";

            [JsonProperty("ID da Skin (Use 0 para qualquer skin)")]
            public ulong RequiredSkinID { get; set; } = 2560763767;

            [JsonProperty("Nome Customizado da Arma")]
            public string CustomGunName { get; set; } = "L96 Rocket";

            [JsonProperty("Impedir troca de skin?")]
            public bool PreventReskin { get; set; } = true;

            [JsonProperty("Tornar a arma indestrutível?")]
            public bool IsIndestructible { get; set; } = true;

            [JsonProperty("Dano de Durabilidade por Disparo")]
            public float DurabilityDamagePerShot { get; set; } = 0f;

            [JsonProperty("Prefab do Rocket (basic)")]
            public string RocketPrefabPath { get; set; } = "assets/prefabs/ammo/rocket/rocket_basic.prefab";

            [JsonProperty("Multiplicador de velocidade do rocket")]
            public float RocketSpeedMultiplier { get; set; } = 3.0f;

            [JsonProperty("Prefixo do chat")]
            public string ChatPrefix { get; set; } = "<color=#ff3333>[L96BasicRocket]</color>";
        }

        protected override void LoadDefaultConfig()
        {
            Config.WriteObject(new PluginConfig(), true);
        }
        #endregion

        #region Init
        private void Init()
        {
            config = Config.ReadObject<PluginConfig>();
            if (config == null)
            {
                LoadDefaultConfig();
                config = Config.ReadObject<PluginConfig>();
            }

            permission.RegisterPermission(PERMISSION_USE, this);

            _rocketItemDef = ItemManager.FindItemDefinition(ROCKET_AMMO_SHORTNAME);
            if (_rocketItemDef == null)
                PrintError($"L96BasicRocket: não encontrou o item '{ROCKET_AMMO_SHORTNAME}'.");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermission"] = "Você não tem permissão para usar esta L96 especial.",
                ["NoRockets"] = "Você não possui rockets básicos para disparar.",
                ["CannotChangeSkin"] = "Você não pode trocar a skin desta L96 especial."
            }, this, "pt-BR");
        }
        #endregion

        #region Hooks
        private object OnItemSkinChange(Item item, ulong newSkin)
        {
            if (item == null) return null;
            if (config.PreventReskin && IsSpecialWeapon(item) && newSkin != item.skin)
            {
                BasePlayer owner = item.GetOwnerPlayer();
                if (owner != null)
                    owner.ChatMessage(lang.GetMessage("CannotChangeSkin", this, owner.UserIDString));
                return true; // bloqueia troca de skin
            }
            return null;
        }

        private void OnLoseCondition(Item item, ref float amount)
        {
            // Impede qualquer perda de condição se for a L96 Rocket e estiver configurada como indestrutível
            if (item != null && IsSpecialWeapon(item) && config.IsIndestructible)
                amount = 0f;
        }

        private void OnWeaponFired(BaseProjectile weapon, BasePlayer player, ItemModProjectile mod, ProtoBuf.ProjectileShoot projectiles)
        {
            if (weapon == null || player == null) return;
            var item = weapon.GetItem();
            if (!IsSpecialWeapon(item)) return;

            if (!permission.UserHasPermission(player.UserIDString, PERMISSION_USE))
            {
                player.ChatMessage($"{config.ChatPrefix} {lang.GetMessage("NoPermission", this, player.UserIDString)}");
                return;
            }

            if (_rocketItemDef != null && player.inventory.GetAmount(_rocketItemDef.itemid) > 0)
            {
                player.inventory.Take(null, _rocketItemDef.itemid, 1);

                try
                {
                    FireRocket(player);
                }
                catch (Exception ex)
                {
                    PrintError($"Erro ao disparar rocket: {ex}");
                }
            }
            else
            {
                player.ChatMessage($"{config.ChatPrefix} {lang.GetMessage("NoRockets", this, player.UserIDString)}");
            }
        }
        #endregion

        #region Core Logic
        private bool IsSpecialWeapon(Item item)
        {
            if (item == null) return false;
            if (item.info?.shortname != config.BaseWeaponShortname) return false;
            if (config.RequiredSkinID == 0) return true;
            return item.skin == config.RequiredSkinID;
        }

        private void FireRocket(BasePlayer player)
        {
            Vector3 spawnPos = player.eyes.position + player.eyes.HeadForward() * 1.2f;
            Quaternion spawnRot = Quaternion.LookRotation(player.eyes.HeadForward());

            BaseEntity rocket = GameManager.server.CreateEntity(config.RocketPrefabPath, spawnPos, spawnRot);
            if (rocket == null)
            {
                PrintError("Falha ao criar rocket prefab. Verifique RocketPrefabPath no config.");
                return;
            }

            rocket.creatorEntity = player;
            rocket.OwnerID = player.userID;

            var serverProj = rocket.GetComponent<ServerProjectile>();
            if (serverProj != null)
            {
                serverProj.InitializeVelocity(player.eyes.HeadForward() * serverProj.speed * config.RocketSpeedMultiplier);
            }
            else
            {
                var rb = rocket.GetComponent<Rigidbody>();
                if (rb != null)
                    rb.velocity = player.eyes.HeadForward() * 80f * config.RocketSpeedMultiplier;
            }

            rocket.Spawn();
        }
        #endregion
    }
}
