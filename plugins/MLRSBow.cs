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
    [Info("MLRSBow", "Remade by Finn", "1.4.1")]
    [Description("Dispara um ataque de MLRS no ponto de impacto da flecha de um Arco Composto especial.")]
    public class MLRSBow : RustPlugin
    {
        #region Variáveis Globais

        [PluginReference]
        private Plugin Loottable;

        private PluginConfig config;
        
        private const string PERMISSION_USE = "mlrsbow.use";
        private ItemDefinition _mlrsAmmoItemDef;
        private readonly Dictionary<ulong, bool> _isSpecialShot = new Dictionary<ulong, bool>();

        #endregion

        #region Configuração

        private class PluginConfig
        {
            [JsonProperty("Arma Base (Shortname)")]
            public string BaseWeaponShortname { get; set; } = "bow.compound";

            [JsonProperty("ID da Skin (Use 0 para qualquer skin)")]
            public ulong RequiredSkinID { get; set; } = 3510395854;

            [JsonProperty("Nome Customizado da Arma")]
            public string CustomGunName { get; set; } = "MLRS Bow";

            [JsonProperty("Consumir munição de MLRS do inventário a cada tiro")]
            public bool ConsumeMLRSAmmo { get; set; } = true;

            [JsonProperty("Shortname da Munição de MLRS (para consumo)")]
            public string MLRSAmmoShortname { get; set; } = "ammo.rocket.mlrs";

            [JsonProperty("Dano de Durabilidade por Disparo Especial")]
            public float DurabilityDamagePerShot { get; set; } = 15.0f;

            [JsonProperty("Tornar a Arma Indestrutível")]
            public bool IsIndestructible { get; set; } = false;

            [JsonProperty("Impedir que jogadores troquem a skin da Arma")]
            public bool PreventReskin { get; set; } = true;

            [JsonProperty("Caminho do Prefab do Flare (Implantado)")]
            public string FlarePrefabPath { get; set; } = "assets/prefabs/tools/flareold/flare.deployed.prefab";

            [JsonProperty("Caminho do Prefab do Rocket MLRS")]
            public string MLRSRocketPrefab { get; set; } = "assets/prefabs/ammo/rocket/rocket_mlrs.prefab";

            [JsonProperty("Número de Rockets a disparar")]
            public int NumberOfRockets { get; set; } = 1;

            [JsonProperty("Raio de Dispersão do Ataque (em metros)")]
            public float StrikeRadius { get; set; } = 20f;

            [JsonProperty("Atraso entre cada Rocket (em segundos)")]
            public float DelayBetweenRockets { get; set; } = 0.2f;

            [JsonProperty("Atraso do Ataque MLRS (em segundos)")]
            public float MLRSStrikeDelay { get; set; } = 5.0f;
        }

        protected override void LoadDefaultConfig()
        {
            Config.WriteObject(new PluginConfig
            {
                BaseWeaponShortname = "bow.compound",
                RequiredSkinID = 0,
                CustomGunName = "MLRS Bow",
                ConsumeMLRSAmmo = true,
                MLRSAmmoShortname = "ammo.rocket.mlrs",
                DurabilityDamagePerShot = 15.0f,
                IsIndestructible = false,
                PreventReskin = true,
                FlarePrefabPath = "assets/prefabs/tools/flareold/flare.deployed.prefab",
                MLRSRocketPrefab = "assets/prefabs/ammo/rocket/rocket_mlrs.prefab",
                NumberOfRockets = 1,
                StrikeRadius = 20f,
                DelayBetweenRockets = 0.2f,
                MLRSStrikeDelay = 5.0f
            }, true);
        }

        #endregion

        #region Hooks do uMod

        private void Init()
        {
            config = Config.ReadObject<PluginConfig>();
            Config.WriteObject(config, true);
            permission.RegisterPermission(PERMISSION_USE, this);

            if (config.ConsumeMLRSAmmo)
            {
                _mlrsAmmoItemDef = ItemManager.FindItemDefinition(config.MLRSAmmoShortname);
                if (_mlrsAmmoItemDef == null)
                {
                    PrintError($"Não foi possível encontrar a definição do item para '{config.MLRSAmmoShortname}'. O consumo de munição MLRS não funcionará.");
                }
            }

            // --- MENSAGENS EM PORTUGUÊS (BR) ---
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["CannotChangeSkin"] = "Você não pode trocar a skin deste item especial.",
                ["NoMLRSAmmo"] = "<color=red>Você precisa de pelo menos {0} munição de MLRS para usar esta habilidade.</color>",
                ["NoPermission"] = "<color=red>Você não tem permissão para usar os poderes do MLRS Bow.</color>"
            }, this, "pt-BR");

            // --- MENSAGENS EM INGLÊS (EN) ---
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["CannotChangeSkin"] = "You cannot change the skin of this special item.",
                ["NoMLRSAmmo"] = "<color=red>You need at least {0} MLRS ammo to use this ability.</color>",
                ["NoPermission"] = "<color=red>You do not have permission to use the powers of the MLRS Bow.</color>"
            }, this, "en");

            // --- MENSAGENS EM ESPANHOL (ES) ---
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["CannotChangeSkin"] = "No puedes cambiar la skin de este objeto especial.",
                ["NoMLRSAmmo"] = "<color=red>Necesitas al menos {0} munición de MLRS para usar esta habilidad.</color>",
                ["NoPermission"] = "<color=red>No tienes permiso para usar los poderes del MLRS Bow.</color>"
            }, this, "es-ES");
        }

        void OnServerInitialized()
        {
            if (string.IsNullOrEmpty(config.CustomGunName)) return;

            Puts("Verificando inventários para renomear MLRS Bows existentes...");
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
            Puts("Verificação de nomes dos MLRS Bows concluída.");

            if (Loottable != null && Loottable.IsLoaded)
            {
                var weaponDef = ItemManager.FindItemDefinition(config.BaseWeaponShortname);
                if (weaponDef != null)
                {
                    Loottable.Call("AddCustomItem", this, weaponDef.itemid, config.RequiredSkinID, config.CustomGunName, true);
                    Puts("MLRSBow registrado com sucesso no plugin Loottable.");
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
            if (!IsMLRSBow(item)) return null;

            if (config.PreventReskin && newSkinID != item.skin)
            {
                item.GetOwnerPlayer()?.ChatMessage(lang.GetMessage("CannotChangeSkin", this, item.GetOwnerPlayer().UserIDString));
                return true;
            }
            return null;
        }

        private void OnLoseCondition(Item item, ref float amount)
        {
            if (IsMLRSBow(item) && config.IsIndestructible)
            {
                amount = 0f;
            }
        }

        private object OnWeaponFired(BaseProjectile weapon, BasePlayer player, ItemModProjectile mod, ProtoBuf.ProjectileShoot projectiles)
        {
            if (weapon == null || player == null || !IsMLRSBow(weapon.GetItem())) return null;
            
            if (!permission.UserHasPermission(player.UserIDString, PERMISSION_USE))
            {
                player.ChatMessage(lang.GetMessage("NoPermission", this, player.UserIDString));
                return null;
            }

            if (config.ConsumeMLRSAmmo)
            {
                if (_mlrsAmmoItemDef == null || player.inventory.GetAmount(_mlrsAmmoItemDef.itemid) < config.NumberOfRockets)
                {
                    player.ChatMessage(string.Format(lang.GetMessage("NoMLRSAmmo", this, player.UserIDString), config.NumberOfRockets));
                    return true;
                }
                player.inventory.Take(null, _mlrsAmmoItemDef.itemid, config.NumberOfRockets);
            }

            if (!config.IsIndestructible)
            {
                weapon.GetItem()?.LoseCondition(config.DurabilityDamagePerShot);
            }

            _isSpecialShot[player.userID] = true;
            timer.Once(20f, () => _isSpecialShot.Remove(player.userID));

            return null;
        }

        private void OnPlayerAttack(BasePlayer player, HitInfo info)
        {
            if (player == null || info == null) return;

            bool isSpecial;
            if (!_isSpecialShot.TryGetValue(player.userID, out isSpecial) || !isSpecial) return;
            
            if (info.damageTypes.Get(DamageType.Arrow) <= 0) return;

            _isSpecialShot.Remove(player.userID);

            Vector3 impactPosition = info.HitPositionWorld;

            FireFlare(impactPosition);
            timer.Once(config.MLRSStrikeDelay, () =>
            {
                if (player != null && !player.IsDestroyed)
                {
                    CallMLRSStrike(impactPosition, player);
                }
            });
        }
        
        #endregion

        #region Funções Auxiliares

        private void CheckAndRenameItem(Item item)
        {
            if (item == null || item.info.shortname != config.BaseWeaponShortname) return;

            var owner = item.GetOwnerPlayer();
            bool hasPermission = owner != null && permission.UserHasPermission(owner.UserIDString, PERMISSION_USE);
            bool isSpecialWeapon = IsMLRSBow(item);
            
            string currentName = item.name;
            string expectedName = isSpecialWeapon && hasPermission ? config.CustomGunName : null;

            if (item.name != expectedName)
            {
                item.name = expectedName;
                item.MarkDirty();
            }
        }
        
        private void FireFlare(Vector3 position)
        {
            var flareEntity = GameManager.server.CreateEntity(config.FlarePrefabPath, position);
            if (flareEntity == null)
            {
                PrintError("Falha ao criar a entidade do Flare. Verifique o caminho do prefab na configuração.");
                return;
            }
            
            flareEntity.Spawn();
            flareEntity.Invoke(() => flareEntity.Kill(), config.MLRSStrikeDelay + (config.NumberOfRockets * config.DelayBetweenRockets) + 5f);
        }

        private void CallMLRSStrike(Vector3 position, BasePlayer owner)
        {
            for (int i = 0; i < config.NumberOfRockets; i++)
            {
                timer.Once(i * config.DelayBetweenRockets, () =>
                {
                    if (owner == null || owner.IsDestroyed) return;
                    FireSingleMLRSRocket(position, owner);
                });
            }
        }

        private void FireSingleMLRSRocket(Vector3 targetPos, BasePlayer owner)
        {
            Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * config.StrikeRadius;
            Vector3 randomTarget = targetPos + new Vector3(randomCircle.x, 0f, randomCircle.y);
            Vector3 fireOrigin = randomTarget + Vector3.up * 200f; 

            BaseEntity rocketEntity = GameManager.server.CreateEntity(config.MLRSRocketPrefab, fireOrigin);
            if (rocketEntity == null)
            {
                PrintError("Falha ao criar a entidade do rocket MLRS. Verifique o caminho do prefab na configuração.");
                return;
            }

            rocketEntity.creatorEntity = owner;
            rocketEntity.OwnerID = owner.userID;

            var serverProjectile = rocketEntity.GetComponent<ServerProjectile>();
            if (serverProjectile != null)
            {
                Vector3 direction = (randomTarget - fireOrigin).normalized;
                serverProjectile.InitializeVelocity(direction * serverProjectile.speed);
            }
            else
            {
                PrintError("O prefab do rocket MLRS não contém o componente 'ServerProjectile'.");
                rocketEntity.Kill();
                return;
            }

            rocketEntity.Spawn();
        }

        private bool IsMLRSBow(Item item)
        {
            if (item?.info.shortname != config.BaseWeaponShortname) return false;
            return config.RequiredSkinID == 0 || item.skin == config.RequiredSkinID;
        }

        #endregion
    }
}