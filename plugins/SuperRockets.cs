using System;
using System.Collections.Generic;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("SuperRockets", "Mestre Pardal", "1.2.5")]
    [Description("Lançador cinematográfico com destruição otimizada e correção de Cooldown.")]
    public class SuperRockets : RustPlugin
    {
        private const string PERM_USE = "superrockets.use";
        private const string PERM_GIVE = "superrockets.give";

        private PluginConfig _cfg;
        private StoredData _data;
        private const string DATA_FILE = "SuperRockets_Cooldowns";

        [Serializable]
        public class ExplosionPattern
        {
            public bool Enabled = true;
            public string EffectPrefab = "assets/prefabs/npc/patrol helicopter/effects/heli_explosion.prefab";
            public int TotalExplosions = 20;
            public int ExplosionsPerWave = 8;
            public float WaveIntervalSeconds = 0.35f;
            public float EllipseRadiusX = 3.0f;
            public float EllipseRadiusZ = 2.0f;
            public float RisePerWave = 3.5f;
            public float PhaseShiftDegreesPerWave = 18f;
            public float JitterXZ = 0.35f;
            public float JitterY = 0.15f;
        }

        [Serializable]
        public class Tier
        {
            public string Key = "ULTRA";
            public string DisplayName = "ULTRA Rocket Launcher";
            public ulong SkinId;
            public float ExplosionDamage = 25000f;
            public float ExplosionRadius = 9.0f;
            public string ImpactFx = "assets/prefabs/weapons/rocketlauncher/effects/rocket_explosion.prefab";
            public ExplosionPattern CinematicFx = new ExplosionPattern();
            public float TouchDistance = 0.60f;
            public float SelfImmunitySeconds = 0.0f;
            public float ReuseCooldownSeconds = 0.0f;
        }

        public class PluginConfig
        {
            public string LauncherShortname = "rocket.launcher";
            public string GuidedMissilePrefab = "assets/prefabs/ammo/rocket/rocket_basic.prefab";
            public float SpeedMultiplier = 0.05f;
            public float BaseSpeed = 30f;
            public float TickInterval = 0.05f;
            public float MaxTravelTime = 18f;
            public float SpawnForward = 0.80f;
            public float SpawnUpOffset = 0.00f;
            public bool ExplodeOnlyAtTarget = true;
            public bool BlockVanillaRocket = true;
            public bool BlockReload = true;
            public float FireCooldown = 0.35f;
            public float MaxTargetDistance = 100f;
            
            public int DamageBatchAmount = 10; 
            public float DamageBatchInterval = 0.05f; 

            public bool DamagePlayers = true;
            public bool DamageBuildings = true;
            public bool DamageDeployables = true;
            public bool DamageNPCs = true;

            public List<Tier> Tiers = new List<Tier>();
        }

        private class StoredData
        {
            public Dictionary<ulong, double> NextUseAt = new Dictionary<ulong, double>();
        }

        protected override void LoadDefaultConfig()
        {
            _cfg = new PluginConfig
            {
                DamageBatchAmount = 10,
                DamageBatchInterval = 0.05f,
                Tiers = new List<Tier>
                {
                    new Tier { Key="SUPER",   DisplayName="SUPER Rocket Launcher",   SkinId=3644827395, ExplosionDamage=18000f, ExplosionRadius=7.5f,  ReuseCooldownSeconds = 10f },
                    new Tier { Key="MEGA",    DisplayName="MEGA Rocket Launcher",    SkinId=3644833947, ExplosionDamage=24000f, ExplosionRadius=8.5f,  ReuseCooldownSeconds = 20f },
                    new Tier { Key="ULTRA",   DisplayName="ULTRA Rocket Launcher",   SkinId=3644837448, ExplosionDamage=32000f, ExplosionRadius=9.5f,  ReuseCooldownSeconds = 30f },
                    new Tier { Key="BLASTER", DisplayName="BLASTER Rocket Launcher", SkinId=3644841515, ExplosionDamage=42000f, ExplosionRadius=10.5f, ReuseCooldownSeconds = 40f },
                    new Tier { Key="ATOMIC",  DisplayName="ATOMIC Rocket Launcher",  SkinId=3644847117, ExplosionDamage=60000f, ExplosionRadius=12.0f, ReuseCooldownSeconds = 50f,
                        CinematicFx = new ExplosionPattern
                        {
                            Enabled = true,
                            EffectPrefab = "assets/prefabs/npc/patrol helicopter/effects/heli_explosion.prefab",
                            TotalExplosions = 30,
                            ExplosionsPerWave = 10,
                            WaveIntervalSeconds = 0.25f,
                            EllipseRadiusX = 4.0f,
                            EllipseRadiusZ = 3.0f,
                            RisePerWave = 4.0f,
                            PhaseShiftDegreesPerWave = 22f,
                            JitterXZ = 0.45f,
                            JitterY = 0.20f
                        }
                    }
                }
            };
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _cfg = Config.ReadObject<PluginConfig>() ?? new PluginConfig();
                if (_cfg.Tiers == null) _cfg.Tiers = new List<Tier>();
                if (_cfg.MaxTargetDistance <= 0f) _cfg.MaxTargetDistance = 400f;
                if (_cfg.DamageBatchAmount <= 0) _cfg.DamageBatchAmount = 10; 
                if (_cfg.DamageBatchInterval <= 0.001f) _cfg.DamageBatchInterval = 0.05f;
            }
            catch
            {
                PrintWarning("Config inválida, recriando padrão.");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_cfg, true);

        private readonly Dictionary<ulong, float> _lastFire = new Dictionary<ulong, float>();
        private readonly HashSet<ulong> _immune = new HashSet<ulong>();
        private readonly Dictionary<ulong, Timer> _immuneTimers = new Dictionary<ulong, Timer>();

        private void Init()
        {
            permission.RegisterPermission(PERM_USE, this);
            permission.RegisterPermission(PERM_GIVE, this);
            LoadConfig();
            LoadData();
        }

        private void LoadData()
        {
            try
            {
                _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(DATA_FILE) ?? new StoredData();
            }
            catch
            {
                _data = new StoredData();
            }
            if (_data.NextUseAt == null)
                _data.NextUseAt = new Dictionary<ulong, double>();
        }

        private void SaveData()
        {
            try { Interface.Oxide.DataFileSystem.WriteObject(DATA_FILE, _data); }
            catch { }
        }

        private double GetTimestamp()
        {
            return DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
        }

        [ChatCommand("srocket")]
        private void CmdGive(BasePlayer player, string cmd, string[] args)
        {
            if (player == null) return;

            if (!permission.UserHasPermission(player.UserIDString, PERM_GIVE) && !player.IsAdmin)
            {
                player.ChatMessage("<color=#ff5555>Sem permissão.</color>");
                return;
            }

            if (args == null || args.Length < 1)
            {
                player.ChatMessage("<color=#ffd479>Uso:</color> /srocket <SUPER|MEGA|ULTRA|BLASTER|ATOMIC> <qtd>");
                return;
            }

            var tier = FindTier(args[0]);
            if (tier == null)
            {
                player.ChatMessage("<color=#ff5555>Tier inválido.</color> Use SUPER, MEGA, ULTRA, BLASTER ou ATOMIC.");
                return;
            }

            int amount = 1;
            if (args.Length >= 2) int.TryParse(args[1], out amount);
            if (amount <= 0) amount = 1;
            if (amount > 100) amount = 100;

            GiveLauncher(player, tier, amount);
            player.ChatMessage($"<color=#00ffaa>Entregue:</color> <color=#ffff88>{tier.DisplayName}</color> x{amount}");
        }

        private Tier FindTier(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            token = token.Trim().ToUpperInvariant();

            foreach (var t in _cfg.Tiers)
            {
                if (t == null) continue;
                if (!string.IsNullOrEmpty(t.Key) && t.Key.ToUpperInvariant() == token) return t;
                if (!string.IsNullOrEmpty(t.DisplayName) && t.DisplayName.ToUpperInvariant().Contains(token)) return t;
            }
            return null;
        }

        private Tier FindTierBySkin(ulong skin)
        {
            foreach (var t in _cfg.Tiers)
            {
                if (t == null) continue;
                if (t.SkinId == skin) return t;
            }
            return null;
        }

        private void GiveLauncher(BasePlayer p, Tier tier, int amount)
        {
            var it = ItemManager.CreateByName(_cfg.LauncherShortname, amount, tier.SkinId);
            if (it == null)
            {
                p.ChatMessage("<color=#ff5555>Falha ao criar o item.</color>");
                return;
            }

            it.name = tier.DisplayName;
            it.maxCondition = 1_000_000f;
            it.condition = it.maxCondition;
            it.MarkDirty();
            p.GiveItem(it);
        }

        private bool IsOurLauncher(Item item, out Tier tier)
        {
            tier = null;
            if (item == null || item.info == null) return false;
            if (item.info.shortname != _cfg.LauncherShortname) return false;
            tier = FindTierBySkin(item.skin);
            return tier != null;
        }

        object OnLoseCondition(Item item, float amount)
        {
            if (IsOurLauncher(item, out _)) return 0f;
            return null;
        }

        object OnEntityTakeDamage(HeldEntity held, HitInfo info)
        {
            try
            {
                var it = held?.GetItem();
                if (it != null && IsOurLauncher(it, out _))
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
            if (active != null && _cfg.BlockVanillaRocket && IsOurLauncher(active, out _))
                return true;
            return null;
        }

        object OnWeaponFired(BaseProjectile projectile, BasePlayer player, ItemModProjectile mod)
        {
            var active = player?.GetActiveItem();
            if (active != null && _cfg.BlockVanillaRocket && IsOurLauncher(active, out _))
                return true;
            return null;
        }

        bool? CanReloadWeapon(BasePlayer player, BaseProjectile projectile)
        {
            if (!_cfg.BlockReload) return null;
            try
            {
                var it = projectile?.GetItem();
                if (it != null && IsOurLauncher(it, out _)) return false;
            }
            catch { }
            return null;
        }

        void OnEntitySpawned(BaseEntity ent)
        {
            if (ent == null || !_cfg.BlockVanillaRocket) return;
            var name = ent.ShortPrefabName ?? "";
            if (name.IndexOf("rocket", StringComparison.OrdinalIgnoreCase) < 0) return;

            var owner = GetOwnerPlayerSafe(ent);
            if (owner != null && IsOurLauncher(owner.GetActiveItem(), out _))
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
                    return BasePlayer.FindByID(ent.OwnerID) ?? BasePlayer.FindSleeping(ent.OwnerID);
            }
            catch { }
            return null;
        }

        void OnPlayerInput(BasePlayer p, InputState input)
        {
            if (p == null || !p.IsConnected) return;
            if (!input.WasJustPressed(BUTTON.FIRE_PRIMARY)) return;

            var active = p.GetActiveItem();
            if (!IsOurLauncher(active, out var tier)) return;

            if (!permission.UserHasPermission(p.UserIDString, PERM_USE) && !p.IsAdmin)
            {
                p.ChatMessage("<color=#ff5555>Você não tem permissão para usar esse lançador.</color>");
                return;
            }

            // CORREÇÃO DO COOLDOWN AQUI
            double nowUtc = GetTimestamp();

            if (_data.NextUseAt.TryGetValue(p.userID, out var nextAt))
            {
                // Se a data salva for absurdamente antiga ou futura (lixo de memória), reseta
                if (nextAt > nowUtc + 360000) 
                {
                    _data.NextUseAt.Remove(p.userID);
                }
                else if (nextAt > nowUtc)
                {
                    var remaining = nextAt - nowUtc;
                    p.ChatMessage($"<color=#ff5555>Cooldown!</color> Aguarde <color=#ffff88>{FormatTime(remaining)}</color> para atirar novamente.");
                    return;
                }
            }

            var nowGame = Time.realtimeSinceStartup;
            var last = _lastFire.TryGetValue(p.userID, out var t) ? t : 0f;
            if (nowGame - last < Mathf.Max(0.05f, _cfg.FireCooldown)) return;
            _lastFire[p.userID] = nowGame;

            var cd = Mathf.Max(0f, tier.ReuseCooldownSeconds);
            if (cd > 0.01f)
            {
                _data.NextUseAt[p.userID] = nowUtc + cd;
                SaveData();
            }

            if (tier.SelfImmunitySeconds > 0.01f) GrantSelfImmunity(p, tier.SelfImmunitySeconds);

            FireGuidedSlow(p, tier);
            RemoveWeapon(active);
        }

        private string FormatTime(double seconds)
        {
            if (seconds < 0) seconds = 0;
            var ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours:00}h {ts.Minutes:00}m {ts.Seconds:00}s";
            if (ts.TotalMinutes >= 1)
                return $"{ts.Minutes:00}m {ts.Seconds:00}s";
            return $"{ts.Seconds:00}s";
        }

        private void RemoveWeapon(Item item)
        {
            try
            {
                if (item == null) return;
                item.RemoveFromContainer();
                item.Remove();
            }
            catch { }
        }

        private void GetEyeRay(BasePlayer p, out Vector3 eye, out Vector3 forward)
        {
            eye = p.eyes?.position ?? p.transform.position + Vector3.up * 1.6f;
            forward = p.eyes?.HeadForward() ?? p.transform.forward;
            if (forward == Vector3.zero) forward = p.transform.forward;
        }

        private void FireGuidedSlow(BasePlayer p, Tier tier)
        {
            GetEyeRay(p, out var eye, out var fwd);
            float maxDist = Mathf.Clamp(_cfg.MaxTargetDistance, 10f, 2000f);

            Vector3 target;
            if (Physics.Raycast(eye, fwd, out RaycastHit hit, maxDist,
                LayerMask.GetMask("Default", "Construction", "Deployed", "Player", "AI", "Terrain", "World", "Vehicle")))
            {
                target = hit.point;
            }
            else
            {
                target = eye + fwd.normalized * maxDist;
            }

            Vector3 origin = eye + fwd.normalized * _cfg.SpawnForward + Vector3.up * _cfg.SpawnUpOffset;
            var prefab = _cfg.GuidedMissilePrefab;
            if (string.IsNullOrEmpty(prefab)) return;

            var dir = (target - origin);
            if (dir.sqrMagnitude < 0.0001f) dir = fwd;
            dir.Normalize();

            var rot = Quaternion.LookRotation(dir);
            BaseEntity missile = null;
            try
            {
                missile = GameManager.server.CreateEntity(prefab, origin, rot, true);
                if (missile != null)
                {
                    missile.Spawn();
                    TryDisablePhysicsAndColliders(missile);
                    missile.OwnerID = p.userID;
                    missile.transform.position = origin;
                    missile.transform.rotation = rot;
                    missile.SendNetworkUpdateImmediate();
                }
            }
            catch { missile?.Kill(); return; }

            float dt = Mathf.Clamp(_cfg.TickInterval, 0.02f, 0.2f);
            float elapsed = 0f;
            float maxTime = Mathf.Max(3f, _cfg.MaxTravelTime);
            float speed = Mathf.Max(1f, _cfg.BaseSpeed) * Mathf.Clamp(_cfg.SpeedMultiplier, 0.01f, 2f);
            float touch = Mathf.Max(0.1f, tier.TouchDistance);
            bool stopped = false;

            timer.Repeat(dt, (int)Math.Ceiling(maxTime / dt), () =>
            {
                if (stopped) return;
                if (p == null || !p.IsConnected || missile == null || missile.IsDestroyed)
                {
                    Cleanup();
                    return;
                }

                var pos = missile.transform.position;
                var toTarget = target - pos;
                var dist = toTarget.magnitude;
                Vector3 stepDir = (dist > 0.001f) ? (toTarget / dist) : dir;

                if (dist <= touch)
                {
                    Impact(target, p, tier);
                    Cleanup(true);
                    return;
                }

                var next = pos + stepDir * (speed * dt);
                missile.transform.position = next;
                missile.transform.rotation = Quaternion.LookRotation(stepDir);
                missile.SendNetworkUpdate();

                elapsed += dt;
                if (elapsed >= maxTime)
                {
                    Impact(target, p, tier);
                    Cleanup(true);
                }
            });

            void Cleanup(bool explode = false)
            {
                stopped = true;
                try { missile?.Kill(); } catch { }
            }
        }

        private void TryDisablePhysicsAndColliders(BaseEntity ent)
        {
            if (ent == null) return;
            try
            {
                try { ent.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast"); } catch { }
                foreach (var rb in ent.GetComponentsInChildren<Rigidbody>(true))
                {
                    rb.useGravity = false;
                    rb.isKinematic = true;
                }
                foreach (var col in ent.GetComponentsInChildren<Collider>(true))
                    col.enabled = false;
                
                var monos = ent.GetComponentsInChildren<MonoBehaviour>(true);
                foreach (var m in monos)
                {
                    var n = m?.GetType()?.Name ?? "";
                    if (n.Contains("Ground") || n.Contains("Collision") || n.Contains("Projectile")) m.enabled = false;
                }
            }
            catch { }
        }

        private void Impact(Vector3 pos, BasePlayer owner, Tier tier)
        {
            if (!string.IsNullOrEmpty(tier.ImpactFx))
                Effect.server.Run(tier.ImpactFx, pos);

            RunTierCinematicFx(pos, tier);

            float baseDamage = Mathf.Max(0f, tier.ExplosionDamage);
            float radius = Mathf.Max(0.1f, tier.ExplosionRadius);

            var mask = LayerMask.GetMask("Default", "Construction", "Deployed", "Player", "AI", "Terrain", "World", "Vehicle");
            var cols = Physics.OverlapSphere(pos, radius, mask, QueryTriggerInteraction.Ignore);

            var targets = new List<BaseCombatEntity>();
            foreach (var col in cols)
            {
                var be = col.ToBaseEntity() ?? col.GetComponentInParent<BaseEntity>();
                if (be == null || be.IsDestroyed) continue;

                if (be is BasePlayer && !_cfg.DamagePlayers) continue;
                if (be is BuildingBlock && !_cfg.DamageBuildings) continue;
                if (be is BaseNpc && !_cfg.DamageNPCs) continue;
                if (!(be is BuildingBlock) && !(be is BasePlayer) && !(be is BaseNpc) && !_cfg.DamageDeployables) continue;

                var combat = be as BaseCombatEntity;
                if (combat != null && !targets.Contains(combat))
                {
                    targets.Add(combat);
                }
            }

            int totalTargets = targets.Count;
            if (totalTargets == 0) return;

            int batchSize = Mathf.Max(1, _cfg.DamageBatchAmount);
            float batchInterval = Mathf.Max(0.01f, _cfg.DamageBatchInterval);

            int processed = 0;
            int batchesNeeded = Mathf.CeilToInt((float)totalTargets / batchSize);

            timer.Repeat(batchInterval, batchesNeeded, () =>
            {
                for (int i = 0; i < batchSize; i++)
                {
                    if (processed >= totalTargets) break;
                    var entity = targets[processed];
                    processed++;

                    if (entity == null || entity.IsDestroyed) continue;

                    float dist = Vector3.Distance(entity.transform.position, pos);
                    float falloff = Mathf.Clamp01(1f - (dist / radius) * 0.30f);
                    
                    if (falloff > 0f)
                    {
                        float dmg = baseDamage * falloff;
                        entity.Hurt(dmg, Rust.DamageType.Explosion, owner, true);
                    }
                }
            });
        }

        private void RunTierCinematicFx(Vector3 basePos, Tier tier)
        {
            var p = tier?.CinematicFx;
            if (p == null || !p.Enabled) return;
            string prefab = p.EffectPrefab;
            if (string.IsNullOrEmpty(prefab)) return;

            int total = Mathf.Max(1, p.TotalExplosions);
            int perWave = Mathf.Max(1, p.ExplosionsPerWave);
            int waves = Mathf.CeilToInt(total / (float)perWave);
            float interval = Mathf.Max(0.05f, p.WaveIntervalSeconds);

            Vector3 forward = Vector3.forward;
            Vector3 right = Vector3.right;
            int waveIndex = 0;
            int spawned = 0;

            timer.Repeat(interval, waves, () =>
            {
                if (spawned >= total) return;
                int remaining = total - spawned;
                int countThisWave = Mathf.Min(perWave, remaining);
                float phaseRad = Mathf.Deg2Rad * (p.PhaseShiftDegreesPerWave * waveIndex);
                float yBase = basePos.y + (waveIndex * p.RisePerWave);

                for (int i = 0; i < countThisWave; i++)
                {
                    float t = (countThisWave <= 1) ? 0f : (i / (float)countThisWave);
                    float ang = (2f * Mathf.PI * t) + phaseRad;
                    float ex = Mathf.Cos(ang) * p.EllipseRadiusX;
                    float ez = Mathf.Sin(ang) * p.EllipseRadiusZ;

                    Vector3 pos = basePos + (right * ex) + (forward * ez);
                    pos.y = yBase;

                    if (p.JitterXZ > 0f)
                    {
                        pos.x += UnityEngine.Random.Range(-p.JitterXZ, p.JitterXZ);
                        pos.z += UnityEngine.Random.Range(-p.JitterXZ, p.JitterXZ);
                    }
                    if (p.JitterY > 0f)
                        pos.y += UnityEngine.Random.Range(-p.JitterY, p.JitterY);

                    Effect.server.Run(prefab, pos, Vector3.up, null, true);
                    spawned++;
                    if (spawned >= total) break;
                }
                waveIndex++;
            });
        }

        private void GrantSelfImmunity(BasePlayer player, float seconds)
        {
            if (player == null) return;
            _immune.Add(player.userID);
            if (_immuneTimers.TryGetValue(player.userID, out var t) && t != null && !t.Destroyed)
                t.Destroy();

            _immuneTimers[player.userID] = timer.Once(Mathf.Max(0.1f, seconds), () =>
            {
                _immune.Remove(player.userID);
                _immuneTimers.Remove(player.userID);
            });
        }

        object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var victim = entity as BasePlayer;
            if (victim == null || info == null) return null;

            if (_immune.Contains(victim.userID))
            {
                if (info.damageTypes != null && info.damageTypes.Has(Rust.DamageType.Explosion) &&
                    (info.InitiatorPlayer == victim || info.Initiator == victim))
                {
                    info.damageTypes = new Rust.DamageTypeList();
                    info.DoHitEffects = false;
                    return true;
                }
            }
            return null;
        }

        void Unload()
        {
            foreach (var kv in _immuneTimers) kv.Value?.Destroy();
            _immuneTimers.Clear();
            _immune.Clear();
            _lastFire.Clear();
            SaveData();
        }
    }
}