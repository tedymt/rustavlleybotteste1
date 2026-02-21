using Oxide.Core;
using Oxide.Core.Plugins;
using Rust;
using UnityEngine;
using System.Collections.Generic;
using Facepunch;

namespace Oxide.Plugins
{
    [Info("DoubleRocketGun", "Mestre Pardal", "1.2.0")]
    [Description("Shotgun Double que explode igual rocket e some após UM tiro, tudo configurável, com imunidade ao atirador.")]

    public class DoubleRocketGun : RustPlugin
    {
        #region Configuração

        private PluginConfig config;

        public class PluginConfig
        {
            public string WeaponShortname = "shotgun.double";
            public ulong WeaponSkin = 2486206437;
            public string WeaponName = "One Puch";
            public int WeaponAmmo = 1;

            public float ExplosionRadius = 10f;
            public float ExplosionDamage = 20000f;
            public string ExplosionEffect = "assets/prefabs/npc/patrol helicopter/effects/heli_explosion.prefab";

            public string PermissionUse = "doublerocketgun.use";
            public string PermissionGive = "doublerocketgun.give";

            // NOVO: tempo (em segundos) que o atirador fica imune ao próprio tiro/explosão
            public float SelfDamageImmunitySeconds = 1.5f;
        }

        protected override void LoadDefaultConfig()
        {
            config = new PluginConfig();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            config = Config.ReadObject<PluginConfig>();
            if (config == null)
            {
                config = new PluginConfig();
                SaveConfig();
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        #endregion

        #region Lang

        private string Lang(string key, string playerId = null, params object[] args)
            => string.Format(lang.GetMessage(key, this, playerId), args);

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermission"] = "You do not have permission to use this command!",
                ["Given"] = "You received the <color=#FF9800>{0}</color>!",
                ["NoUsePermission"] = "You do not have permission to use this weapon!",
                ["Removed"] = "The <color=#FF9800>{0}</color> disappeared after use!",
                ["Info"] = "<color=#FF9800>Double Rocket Gun:</color>\n• Massive area damage!\n• Disappears after 1 shot!\n• Explodes like a rocket!"
            }, this, "en");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermission"] = "Você não tem permissão para usar este comando!",
                ["Given"] = "Você recebeu a <color=#FF9800>{0}</color>!",
                ["NoUsePermission"] = "Você não tem permissão para usar esta arma!",
                ["Removed"] = "A <color=#FF9800>{0}</color> desapareceu após o uso!",
                ["Info"] = "<color=#FF9800>Double Rocket Gun:</color>\n• Dano em área gigante!\n• Some após 1 tiro!\n• Explosão igual rocket!"
            }, this, "pt-BR");
        }

        #endregion

        #region Permissões e estado

        private readonly HashSet<ulong> _immuneShooters = new HashSet<ulong>();
        private readonly Dictionary<ulong, Timer> _immuneTimers = new Dictionary<ulong, Timer>();

        private void Init()
        {
            permission.RegisterPermission(config.PermissionUse, this);
            permission.RegisterPermission(config.PermissionGive, this);
        }

        #endregion

        #region Comandos

        [ChatCommand("onepunch")]
        private void CmdDoubleGun(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, config.PermissionGive))
            {
                player.ChatMessage(Lang("NoPermission", player.UserIDString));
                return;
            }

            GiveWeapon(player);
        }

        [ChatCommand("doubleguninfo")]
        private void CmdDoubleGunInfo(BasePlayer player, string command, string[] args)
        {
            player.ChatMessage(Lang("Info", player.UserIDString));
        }

        #endregion

        #region Lógica Arma

        private void GiveWeapon(BasePlayer player)
        {
            var def = ItemManager.FindItemDefinition(config.WeaponShortname);
            if (def == null)
            {
                player.ChatMessage("Item não encontrado!");
                return;
            }

            var item = ItemManager.Create(def, 1, config.WeaponSkin);
            if (item == null)
            {
                player.ChatMessage("Falha ao criar o item!");
                return;
            }

            item.name = config.WeaponName;
            item.condition = item.maxCondition;
            item.MarkDirty();

            player.inventory.GiveItem(item);

            timer.Once(0.2f, () =>
            {
                var held = item.GetHeldEntity() as BaseProjectile;
                if (held != null)
                    held.primaryMagazine.contents = config.WeaponAmmo;
            });

            player.ChatMessage(Lang("Given", player.UserIDString, config.WeaponName));
        }

        object OnPlayerAttack(BasePlayer player, HitInfo info)
        {
            var item = player.GetActiveItem();
            if (!IsValidWeapon(item)) return null;

            if (!permission.UserHasPermission(player.UserIDString, config.PermissionUse))
            {
                player.ChatMessage(Lang("NoUsePermission", player.UserIDString));
                return false;
            }

            // Concede imunidade ao dano do próprio tiro/explosão
            GrantSelfImmunity(player);

            // Explode ao disparar
            Vector3 firePos = player.eyes.position + player.eyes.BodyForward() * 6f;
            ExplodeRocket(firePos, player);

            // Remove a arma instantaneamente
            RemoveWeapon(item, player);

            // Não bloqueia o disparo original
            return null;
        }

        private void GrantSelfImmunity(BasePlayer player)
        {
            if (player == null) return;

            _immuneShooters.Add(player.userID);

            if (_immuneTimers.TryGetValue(player.userID, out var t) && t != null && !t.Destroyed)
                t.Destroy();

            _immuneTimers[player.userID] = timer.Once(config.SelfDamageImmunitySeconds, () =>
            {
                _immuneShooters.Remove(player.userID);
                _immuneTimers.Remove(player.userID);
            });
        }

        private void ExplodeRocket(Vector3 position, BasePlayer attacker)
        {
            // Efeito visual de HV Rocket
            Effect.server.Run(config.ExplosionEffect, position);

            // Dano em área
            var list = Pool.GetList<BaseCombatEntity>();
            Vis.Entities<BaseCombatEntity>(position, config.ExplosionRadius, list, -1, QueryTriggerInteraction.Collide);

            foreach (var entity in list)
            {
                if (entity == null || entity.IsDestroyed) continue;

                // NUNCA causar dano ao atirador
                if (attacker != null && entity == attacker)
                    continue;

                float dist = Vector3.Distance(position, entity.transform.position);
                float multiplier = Mathf.Clamp01(1 - (dist / config.ExplosionRadius));
                float damage = config.ExplosionDamage * multiplier;
                if (damage <= 0f) continue;

                entity.Hurt(damage, DamageType.Explosion, attacker, true);
            }

            Pool.FreeList(ref list);
        }

        private void RemoveWeapon(Item item, BasePlayer player)
        {
            if (item?.parent == null) return;
            item.RemoveFromContainer();
            item.Remove();
            player.ChatMessage(Lang("Removed", player.UserIDString, config.WeaponName));
        }

        private bool IsValidWeapon(Item item)
        {
            return item != null
                && item.info.shortname == config.WeaponShortname
                && item.skin == config.WeaponSkin
                && item.condition > 0f;
        }

        #endregion

        #region Hooks de Dano (imunidade segura)

        // Redundância segura: se por qualquer motivo o atirador ainda receber dano de explosão
        // originada por ele mesmo durante a janela de imunidade, cancelamos aqui.
        object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var victim = entity as BasePlayer;
            if (victim == null || info == null) return null;

            if (_immuneShooters.Contains(victim.userID))
            {
                // Se a explosão foi atribuída ao próprio player, ou qualquer dano explosivo durante a janela
                // (principalmente para garantir contra edge cases)
                if (info.damageTypes.Has(DamageType.Explosion) &&
                    (info.InitiatorPlayer == victim || info.Initiator == victim))
                {
                    // Cancela o dano
                    info.damageTypes = new DamageTypeList();
                    info.DoHitEffects = false;
                    return true;
                }
            }

            return null;
        }

        #endregion

        #region Unload

        void Unload()
        {
            foreach (var kv in _immuneTimers)
                kv.Value?.Destroy();
            _immuneTimers.Clear();
            _immuneShooters.Clear();
        }

        #endregion
    }
}
