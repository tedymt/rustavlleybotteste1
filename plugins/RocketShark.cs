using System;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RocketShark", "Mestre Pardal", "1.4.9")]
    [Description("Launcher (skin 3595911837) que dispara um tubarão visível até o alvo. Usa fish.smallshark como munição, bloqueia rockets vanilla e explode com alto dano.")]
    public class RocketShark : RustPlugin
    {
        private const string PERM_USE  = "rocketshark.use";
        private const string PERM_GIVE = "rocketshark.give";

        private ConfigData _cfg;
        private readonly Dictionary<ulong, float> _lastFire = new();

        private class ConfigData
        {
            // Arma
            public string LauncherShortname   = "rocket.launcher";
            public ulong  LauncherSkinId      = 3595911837;
            public string LauncherDisplayName = "RocketShark";

            // Munição = tubarão pescado
            public string AmmoShortname = "fish.smallshark";
            public ulong  AmmoSkinId    = 0;
            public int    AmmoPerShot   = 1;

            // Projétil/visual
            public string SharkPrefab         = "assets/prefabs/deployable/fishingtrophy/fish_shark.prefab";
            public float  SharkSpeed          = 45f;
            public float  MaxTravelTime       = 8f;
            public float  SharkHitRadius      = 0.80f;
            public float  SpawnForward        = 0.55f; 
            public float  SpawnUpOffset       = 0.00f;
            public string TravelFx            = "";

            // Precisão/impacto
            public float  ExplodeTouchDistance = 0.60f;

            // Dano
            public float  ExplosionRadius     = 4.0f;
            public float  ExplosionDamage     = 8500f;
            public bool   DamagePlayers       = true;
            public bool   DamageBuildings     = true;
            public bool   DamageDeployables   = true;
            public bool   DamageNPCs          = true;
            public string ImpactFx            = "assets/prefabs/weapons/rocketlauncher/effects/rocket_explosion.prefab";

            // Proteções
            public float  FireCooldown        = 1.0f;
            public bool   BlockVanillaRocket  = true;
        }

        protected override void LoadDefaultConfig() => _cfg = new ConfigData();
        protected override void LoadConfig()
        {
            base.LoadConfig();
            try { _cfg = Config.ReadObject<ConfigData>() ?? new ConfigData(); }
            catch { PrintWarning("Config inválido. Recriando padrão."); _cfg = new ConfigData(); }
        }
        protected override void SaveConfig() => Config.WriteObject(_cfg, true);

        private void Init()
        {
            permission.RegisterPermission(PERM_USE,  this);
            permission.RegisterPermission(PERM_GIVE, this);
            LoadConfig();
        }

        [ChatCommand("rocketshark")]
        private void CmdGiveWeapon(BasePlayer player, string cmd, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PERM_GIVE))
            {
                player.ChatMessage("<color=#ff5555>Sem permissão para receber a RocketShark.</color>");
                return;
            }
            GiveLauncher(player, 1);
            player.ChatMessage("<color=#00ffaa>Você recebeu a RocketShark!</color> Dispare com <color=#ffff88>fish.smallshark</color> no inventário.");
        }

        private void GiveLauncher(BasePlayer p, int amount)
        {
            var it = ItemManager.CreateByName(_cfg.LauncherShortname, amount, _cfg.LauncherSkinId);
            if (it == null) { p.ChatMessage("<color=#ff5555>Falha ao criar a arma.</color>"); return; }
            it.name = _cfg.LauncherDisplayName;
            it.maxCondition = 1_000_000f;
            it.condition    = it.maxCondition;
            p.GiveItem(it);
        }

        private bool IsOurLauncher(Item item)
        {
            if (item == null) return false;
            if (item.info?.shortname != _cfg.LauncherShortname) return false;
            return item.skin == _cfg.LauncherSkinId;
        }

        object OnLoseCondition(Item item, float amount)
        {
            if (IsOurLauncher(item)) return 0f;
            return null;
        }
        object OnEntityTakeDamage(HeldEntity held, HitInfo info)
        {
            try
            {
                var it = held?.GetItem();
                if (IsOurLauncher(it))
                {
                    info.damageTypes.ScaleAll(0f);
                    return true;
                }
            }
            catch { }
            return null;
        }

        object OnWeaponFired(BaseProjectile projectile, BasePlayer player, ItemModProjectile mod, ProtoBuf.ProjectileShoot shoot)
        {
            var active = player?.GetActiveItem();
            if (active != null && _cfg.BlockVanillaRocket && IsOurLauncher(active))
                return true;
            return null;
        }
        object OnWeaponFired(BaseProjectile projectile, BasePlayer player, ItemModProjectile mod)
        {
            var active = player?.GetActiveItem();
            if (active != null && _cfg.BlockVanillaRocket && IsOurLauncher(active))
                return true;
            return null;
        }

        bool? CanReloadWeapon(BasePlayer player, BaseProjectile projectile)
        {
            try
            {
                var item = projectile?.GetItem();
                if (IsOurLauncher(item)) return false;
            }
            catch { }
            return null;
        }

        void OnEntitySpawned(BaseNetworkable networkable)
        {
            var ent = networkable as BaseEntity;
            HandleRocketVanilla(ent);
        }
        void OnEntitySpawned(BaseEntity ent) => HandleRocketVanilla(ent);

        private void HandleRocketVanilla(BaseEntity ent)
        {
            if (ent == null || !_cfg.BlockVanillaRocket) return;
            var name = ent.ShortPrefabName ?? string.Empty;
            if (!name.Contains("rocket", StringComparison.OrdinalIgnoreCase)) return;

            var owner = GetOwnerPlayerSafe(ent);
            if (owner != null && IsOurLauncher(owner.GetActiveItem()))
            {
                NextTick(() =>
                {
                    if (ent != null && !ent.IsDestroyed) ent.Kill();
                });
            }
        }

        private BasePlayer GetOwnerPlayerSafe(BaseEntity ent)
        {
            if (ent == null) return null;
            try
            {
                var ce = ent.creatorEntity;
                if (ce is BasePlayer bp1) return bp1;
                if (ce is BaseEntity be1)
                {
                    var p = be1.ToPlayer();
                    if (p != null) return p;
                }
            }
            catch { }
            try
            {
                if (ent.OwnerID != 0)
                {
                    var p = BasePlayer.FindByID(ent.OwnerID) ?? BasePlayer.FindSleeping(ent.OwnerID);
                    if (p != null) return p;
                }
            }
            catch { }
            return null;
        }

         void OnPlayerInput(BasePlayer p, InputState input)
        {
            if (p == null || !p.IsConnected) return;
            if (!input.WasJustPressed(BUTTON.FIRE_PRIMARY)) return;

            var active = p.GetActiveItem();
            if (!IsOurLauncher(active)) return;

            if (!permission.UserHasPermission(p.UserIDString, PERM_USE) && !p.IsAdmin)
            {
                p.ChatMessage("<color=#ff5555>Você não tem permissão para usar a RocketShark.</color>");
                return;
            }

            var now = Time.realtimeSinceStartup;
            var last = _lastFire.TryGetValue(p.userID, out var t) ? t : 0f;
            if (now - last < _cfg.FireCooldown) return;
            _lastFire[p.userID] = now;

            if (!TryConsumeAmmo(p, _cfg.AmmoPerShot))
            {
                p.ChatMessage("<color=#ffff00>Sem munição!</color> Precisa de <color=#ffff88>fish.smallshark</color> (Tubarão Pequeno).");
                return;
            }

            FireShark(p);
        }

        private bool TryConsumeAmmo(BasePlayer p, int amount)
        {
            int remaining = amount;
            ItemContainer[] containers =
            {
                p.inventory?.containerMain,
                p.inventory?.containerBelt,
                p.inventory?.containerWear
            };

            foreach (var cont in containers)
            {
                if (cont == null) continue;
                var list = cont.itemList;
                if (list == null) continue;

                for (int i = list.Count - 1; i >= 0 && remaining > 0; i--)
                {
                    var item = list[i];
                    if (item == null || item.info == null) continue;
                    if (item.info.shortname != _cfg.AmmoShortname) continue;
                    if (_cfg.AmmoSkinId != 0 && item.skin != _cfg.AmmoSkinId) continue;

                    var take = Math.Min(item.amount, remaining);
                    if (take <= 0) continue;

                    item.UseItem(take);
                    remaining -= take;
                }

                if (remaining <= 0) return true;
            }

            return false;
        }

        private void GetEyeRay(BasePlayer p, out Vector3 eye, out Vector3 forward)
        {
            eye     = p.eyes?.position ?? p.transform.position + Vector3.up * 1.6f;
            forward = p.eyes?.HeadForward() ?? p.transform.forward;
            if (forward == Vector3.zero) forward = p.transform.forward;
        }

        private void FireShark(BasePlayer p)
        {
            GetEyeRay(p, out var eye, out var fwd);

            Vector3 target;
            if (Physics.Raycast(eye, fwd, out RaycastHit hit, 350f,
                    LayerMask.GetMask("Default", "Construction", "Deployed", "Player", "AI", "Terrain", "World")))
                target = hit.point;
            else
                target = eye + fwd.normalized * 240f;

            Vector3 origin = eye + fwd.normalized * _cfg.SpawnForward + Vector3.up * _cfg.SpawnUpOffset;

            BaseEntity shark = null;
            if (!string.IsNullOrEmpty(_cfg.SharkPrefab))
            {
                var rot = Quaternion.LookRotation((target - origin).normalized);
                shark = GameManager.server.CreateEntity(_cfg.SharkPrefab, origin, rot, true);
                if (shark != null)
                {
                    shark.Spawn();

                    try { shark.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast"); } catch { }

                    try
                    {
                        foreach (var rb in shark.GetComponentsInChildren<Rigidbody>(true))
                        {
                            rb.useGravity = false;
                            rb.isKinematic = true;
                        }
                    } catch { }

                    try
                    {
                        foreach (var col in shark.GetComponentsInChildren<Collider>(true))
                            col.enabled = false;
                    } catch { }

                    TryDisableComponent<UnityEngine.AI.NavMeshAgent>(shark);
                    TryDisableByName(shark, "BaseNavigator");
                    TryDisableByName(shark, "Ground");
                    TryDisableByName(shark, "Terrain");

                    shark.transform.position = origin;
                    shark.transform.rotation = rot;
                    shark.SendNetworkUpdateImmediate();
                }
            }

            float dt = 0.05f, elapsed = 0f;
            float speed = Mathf.Max(1f, _cfg.SharkSpeed);
            float maxTime = Mathf.Max(3f, _cfg.MaxTravelTime);
            float radius = Mathf.Max(0.05f, _cfg.SharkHitRadius);
            float touch = Mathf.Max(0.05f, _cfg.ExplodeTouchDistance);
            Vector3 dir = (target - origin).normalized;
            if (dir == Vector3.zero) dir = fwd.normalized;

            float ignoreSelfTime = 0.2f;

            int mask = LayerMask.GetMask("Default", "Construction", "Deployed", "Player", "AI", "Terrain", "World", "Vehicle");

            bool stopped = false;

            timer.Repeat(dt, (int)Math.Ceiling(maxTime / dt), () =>
            {
                if (stopped) return;
                if (p == null || !p.IsConnected) { Cleanup(); return; }
                if (shark != null && shark.IsDestroyed) { Cleanup(); return; }

                Vector3 pos  = shark != null ? shark.transform.position : origin + dir * (elapsed * speed);
                Vector3 next = pos + dir * (speed * dt);

                if (!string.IsNullOrEmpty(_cfg.TravelFx))
                    Effect.server.Run(_cfg.TravelFx, pos);

                if (Vector3.Distance(next, target) <= touch)
                {
                    Impact(target, p);
                    Cleanup();
                    return;
                }

                if (Physics.SphereCast(pos, radius, dir, out var sphereHit,
                        Vector3.Distance(next, pos) + radius, mask, QueryTriggerInteraction.Ignore))
                {
                    var hitEnt = sphereHit.collider.ToBaseEntity() ?? sphereHit.collider.GetComponentInParent<BaseEntity>();
                    if (!(elapsed < ignoreSelfTime && hitEnt is BasePlayer bp && bp.userID == p.userID))
                    {
                        Vector3 impact = sphereHit.point == Vector3.zero ? next : sphereHit.point;
                        Impact(impact, p);
                        Cleanup();
                        return;
                    }
                }

                if (shark != null)
                {
                    shark.transform.position = next;
                    shark.transform.rotation = Quaternion.LookRotation(dir);
                    shark.SendNetworkUpdate();
                }

                elapsed += dt;

                if (elapsed >= maxTime)
                {
                    Impact(shark != null ? shark.transform.position : next, p);
                    Cleanup();
                }
            });

            void Cleanup()
            {
                stopped = true;
                try { shark?.Kill(); } catch { }
            }
        }

        private void TryDisableComponent<T>(BaseEntity e) where T : Behaviour
        {
            try
            {
                foreach (var c in e.GetComponentsInChildren<T>(true))
                    c.enabled = false;
            }
            catch { }
        }
        private void TryDisableByName(BaseEntity e, string contains)
        {
            try
            {
                var monos = e.GetComponentsInChildren<MonoBehaviour>(true);
                foreach (var m in monos)
                {
                    var n = m?.GetType()?.Name ?? "";
                    if (n.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0)
                        m.enabled = false;
                }
            }
            catch { }
        }

        private void Impact(Vector3 pos, BasePlayer owner)
        {
            if (!string.IsNullOrEmpty(_cfg.ImpactFx))
                Effect.server.Run(_cfg.ImpactFx, pos);

            float baseDamage = Mathf.Max(0f, _cfg.ExplosionDamage);
            float radius     = Mathf.Max(0.1f, _cfg.ExplosionRadius);

            var mask = LayerMask.GetMask("Default", "Construction", "Deployed", "Player", "AI", "Terrain", "World");
            var cols = Physics.OverlapSphere(pos, radius, mask);

            foreach (var col in cols)
            {
                var be = col.ToBaseEntity() ?? col.gameObject?.GetComponentInParent<BaseEntity>();
                if (be == null) continue;

                if (be is BasePlayer && !_cfg.DamagePlayers) continue;
                if (be is BuildingBlock && !_cfg.DamageBuildings) continue;
                if (be is BaseNpc && !_cfg.DamageNPCs) continue;
                if (!(be is BuildingBlock) && !(be is BasePlayer) && !(be is BaseNpc) && !_cfg.DamageDeployables) continue;

                var combat = be as BaseCombatEntity;
                if (combat == null) continue;

                float dist    = Vector3.Distance(be.transform.position, pos);
                float falloff = Mathf.Clamp01(1f - (dist / radius) * 0.30f);
                if (falloff <= 0f) continue;

                float dmg = baseDamage * falloff;
                combat.Hurt(dmg, Rust.DamageType.Explosion, owner);
            }
        }
    }
}
