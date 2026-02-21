using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using Rust;

namespace Oxide.Plugins
{
    [Info("MBird249", "Mestre Pardal", "1.5.1")]
    [Description("M249 indestrutível com HEAT incremental (resfriamento gradual), overheat em 2 estágios (grace + lock), HUD com cores dinâmicas e bônus de dano aritmético/geométrico em whitelist.")]
    public class MBird249 : RustPlugin
    {
        private const string PERM_USE  = "mbird249.use";
        private const string PERM_GIVE = "mbird249.give";

        private const string UI_PANEL = "MBIRD_PANEL";
        private const string UI_TEXT1 = "MBIRD_TEXT1";
        private const string UI_BARBG = "MBIRD_BARBG";
        private const string UI_BARFG = "MBIRD_BARFG";

        private const string M249_SHORTNAME = "lmg.m249";

        private ConfigData config;
        private readonly Dictionary<ulong, PlayerState> states = new Dictionary<ulong, PlayerState>();
        private Timer hudTick;

        #region Config
        private class ConfigData
        {
            [JsonProperty(PropertyName = "Permission Required (mbird249.use)")]
            public bool RequirePermission { get; set; } = true;

            [JsonProperty(PropertyName = "M249 Skin ID")]
            public ulong SkinID { get; set; } = 3561729076;

            [JsonProperty(PropertyName = "Allowed Display Names (any match qualifies)")]
            public List<string> AllowedNames { get; set; } = new List<string> { "MBird 249" };

            [JsonProperty(PropertyName = "Require BOTH Name and Skin to qualify")]
            public bool RequireNameAndSkin { get; set; } = false;

            [JsonProperty(PropertyName = "Indestructible (block condition loss)")]
            public bool Indestructible { get; set; } = true;

            [JsonProperty(PropertyName = "Spray - MaxProgressShots (limit)")]
            public int MaxProgressShots { get; set; } = 100;

            [JsonProperty(PropertyName = "Spray - DamagePerShot (flat add)")]
            public float DamagePerShot { get; set; } = 10.0f;

            [JsonProperty(PropertyName = "Spray - IdleResetSeconds (reset if no shots)")]
            public float IdleResetSeconds { get; set; } = 1.0f;

            [JsonProperty(PropertyName = "Progression Mode (Arithmetic|Geometric)")]
            public string ProgressionMode { get; set; } = "Arithmetic";

            [JsonProperty(PropertyName = "Geometric Multiplier (>=1.0)")]
            public float GeoMultiplier { get; set; } = 1.0f;

            [JsonProperty(PropertyName = "HUD - Enabled")]
            public bool HudEnabled { get; set; } = true;

            [JsonProperty(PropertyName = "HUD - Font Size")]
            public int HudFontSize { get; set; } = 8;

            [JsonProperty(PropertyName = "HUD - Anchor X")]
            public float HudX { get; set; } = 0.5f;

            [JsonProperty(PropertyName = "HUD - Anchor Y")]
            public float HudY { get; set; } = 0.12f;

            [JsonProperty(PropertyName = "HUD - Width")]
            public float HudW { get; set; } = 0.22f;

            [JsonProperty(PropertyName = "HUD - Height")]
            public float HudH { get; set; } = 0.02f;

            public string PanelColor { get; set; } = "0 0 0 0";
            public string TextColor  { get; set; } = "1 1 1 1";
            public string BarBGColor { get; set; } = "0.15 0.15 0.15 0.9";
            public string BarFGColor { get; set; } = "0 1 0 0.8";
            public string BarHitColor { get; set; } = "1 0 0 0.95";

            [JsonProperty(PropertyName = "Give Command (chat)")]
            public string GiveCommand { get; set; } = "mbird";

            [JsonProperty(PropertyName = "Given Display Name")]
            public string GivenName { get; set; } = "MBird 249";

            [JsonProperty(PropertyName = "Auto-rename & enforce skin when holding")]
            public bool EnforceWhileHolding { get; set; } = true;

            [JsonProperty(PropertyName = "Localize (PT-BR & EN)")]
            public bool Localize { get; set; } = true;

            [JsonProperty(PropertyName = "Whitelist - Players")]
            public bool WLPlayers { get; set; } = true;

            [JsonProperty(PropertyName = "Whitelist - NPCs (human)")]
            public bool WLNPCs { get; set; } = true;

            [JsonProperty(PropertyName = "Whitelist - Animals")]
            public bool WLAnimals { get; set; } = true;

            [JsonProperty(PropertyName = "Whitelist - Helicopter (Patrol)")]
            public bool WLHelicopter { get; set; } = true;

            [JsonProperty(PropertyName = "Whitelist - Bradley APC")]
            public bool WLBradley { get; set; } = false;

            [JsonProperty(PropertyName = "Whitelist - Prefab substrings (lowercase)")]
            public List<string> WLPrefabContains { get; set; } = new List<string> { "autoturret","flameturret","samsite" };

            [JsonProperty(PropertyName = "Heat - Decay Per Second (on release)")]
            public float HeatDecayPerSecond { get; set; } = 40f;

            [JsonProperty(PropertyName = "Heat - Decay Tick Seconds")]
            public float HeatDecayTick { get; set; } = 0.05f;

            [JsonProperty(PropertyName = "Overheat - Enabled")]
            public bool OverheatEnabled { get; set; } = true;

            [JsonProperty(PropertyName = "Overheat - Threshold (0..1 of MaxProgressShots)")]
            public float OverheatThreshold { get; set; } = 0.95f;

            [JsonProperty(PropertyName = "Overheat - Grace Seconds (keep firing before lock)")]
            public float OverheatGraceSeconds { get; set; } = 5.0f;

            [JsonProperty(PropertyName = "Overheat - Lock Seconds (no bullet damage)")]
            public float OverheatLockSeconds { get; set; } = 5.0f;

            [JsonProperty(PropertyName = "Overheat - Reset Heat after Lock")]
            public bool OverheatResetAfterLock { get; set; } = true;

            [JsonProperty(PropertyName = "HUD - Overheat Text Prefix")]
            public string OverheatTextPrefix { get; set; } = "OVERHEAT";

            [JsonProperty(PropertyName = "HUD - Use Smooth Gradient Colors")]
            public bool UseSmoothGradient { get; set; } = true;

            [JsonProperty(PropertyName = "FX - Medium Heat Prefab (optional)")]
            public string FxMedium { get; set; } = "";

            [JsonProperty(PropertyName = "FX - High Heat Prefab (optional)")]
            public string FxHigh { get; set; } = "";

            [JsonProperty(PropertyName = "FX - Lock Prefab (optional)")]
            public string FxLock { get; set; } = "assets/bundled/prefabs/fx/item_break.prefab";
        }
        protected override void LoadDefaultConfig() => config = new ConfigData();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try { config = Config.ReadObject<ConfigData>(); }
            catch { PrintError("Config inválida; carregando padrão."); LoadDefaultConfig(); }

            var changed = false;
            if (config.MaxProgressShots < 1) { config.MaxProgressShots = 1; changed = true; }
            if (config.DamagePerShot < 0f) { config.DamagePerShot = 0f; changed = true; }
            if (config.HudFontSize <= 0) { config.HudFontSize = 8; changed = true; }
            if (config.GeoMultiplier < 1f) { config.GeoMultiplier = 1f; changed = true; }
            if (config.OverheatThreshold < 0f) { config.OverheatThreshold = 0f; changed = true; }
            if (config.OverheatThreshold > 1f) { config.OverheatThreshold = 1f; changed = true; }
            if (config.HeatDecayPerSecond < 0f) { config.HeatDecayPerSecond = 0f; changed = true; }
            if (config.HeatDecayTick < 0.02f) { config.HeatDecayTick = 0.02f; changed = true; }
            if (config.AllowedNames == null) { config.AllowedNames = new List<string>(); changed = true; }
            if (config.WLPrefabContains == null) { config.WLPrefabContains = new List<string>(); changed = true; }
            if (changed) SaveConfig();
        }
        protected override void SaveConfig() => Config.WriteObject(config);
        #endregion

        #region Lang
        private void LoadDefaultMessages()
        {
            var en = new Dictionary<string, string>
            {
                ["Given"] = "<color=#00FFFF>[MBird249]</color> You received the MBird 249!",
                ["NoPerm"] = "<color=#00FFFF>[MBird249]</color> You don't have permission.",
                ["ResetProgress"] = "<color=#00FFFF>[MBird249]</color> Spray progress reset.",
                ["OverheatGrace"] = "<color=#FFA500>[MBird249]</color> Overheat! Stop firing in {0:0.0}s or weapon will lock.",
                ["OverheatLock"]  = "<color=#FF4500>[MBird249]</color> Weapon locked for {0:0.0}s!"
            };
            var pt = new Dictionary<string, string>
            {
                ["Given"] = "<color=#00FFFF>[MBird249]</color> Você recebeu a MBird 249!",
                ["NoPerm"] = "<color=#00FFFF>[MBird249]</color> Você não tem permissão.",
                ["ResetProgress"] = "<color=#00FFFF>[MBird249]</color> Progresso do spray zerado.",
                ["OverheatGrace"] = "<color=#FFA500>[MBird249]</color> Superaquecendo! Pare de atirar em {0:0.0}s ou a arma será travada.",
                ["OverheatLock"]  = "<color=#FF4500>[MBird249]</color> Arma travada por {0:0.0}s!"
            };
            lang.RegisterMessages(en, this, "en");
            lang.RegisterMessages(pt, this, "pt-BR");
        }

        private string L(BasePlayer player, string key, params object[] args)
        {
            var id = player?.UserIDString ?? "server_console";
            var msg = lang.GetMessage(key, this, id);
            if (string.IsNullOrEmpty(msg)) msg = lang.GetMessage(key, this, "en");
            return (args == null || args.Length == 0) ? msg : string.Format(msg, args);
        }
        #endregion

        #region State
        private enum OverheatStage { None, Grace, Lock }

        private class PlayerState
        {
            public int    LastMag = -1;

            public int    Heat;

            public double LastEventTime;
            public bool   IsFiring;

            public bool   LastCanDamage;
            [JsonIgnore] public Timer  CanDamageTintTimer;

            [JsonIgnore] public Timer IdleTimer;
            [JsonIgnore] public Timer ReleaseDecayTimer;

            // Overheat 2 estágios
            public OverheatStage OverheatState = OverheatStage.None;
            public double        OverheatEndTime;

            [JsonIgnore] public bool  UiOpen;
        }

        private PlayerState GetState(BasePlayer player)
        {
            if (!states.TryGetValue(player.userID, out var st))
            {
                st = new PlayerState();
                states[player.userID] = st;
            }
            return st;
        }
        #endregion

        #region Lifecycle
        private void Init()
        {
            permission.RegisterPermission(PERM_USE,  this);
            permission.RegisterPermission(PERM_GIVE, this);
            LoadDefaultMessages();

            if (hudTick == null) hudTick = timer.Every(0.10f, HudTick);
        }

        private void OnServerInitialized()
        {
            foreach (var p in BasePlayer.activePlayerList) DestroyHud(p);
        }

        private void Unload()
        {
            foreach (var st in states.Values)
            {
                st.IdleTimer?.Destroy();
                st.CanDamageTintTimer?.Destroy();
                st.ReleaseDecayTimer?.Destroy();
            }
            hudTick?.Destroy(); hudTick = null;
            foreach (var p in BasePlayer.activePlayerList) DestroyHud(p);
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason) => DestroyHud(player);
        #endregion

        #region Commands (give)
        [ChatCommand("mbird")]
        private void CmdGive(BasePlayer player, string cmd, string[] args)
        {
            if (!string.Equals(cmd, config.GiveCommand, StringComparison.OrdinalIgnoreCase)) return;
            if (player == null) return;

            if (config.RequirePermission && !permission.UserHasPermission(player.UserIDString, PERM_GIVE))
            {
                player.ChatMessage(L(player, "NoPerm"));
                return;
            }

            var item = CreateM249Safe();
            if (item == null)
            {
                player.ChatMessage("<color=#00FFFF>[MBird249]</color> Falha ao criar a M249.");
                return;
            }

            ApplySkin(item, player);
            item.name = config.GivenName;
            MakeIndestructible(item);
            GiveOrDrop(player, item);

            player.ChatMessage(L(player, "Given"));
        }

        private Item CreateM249Safe()
        {
            var item = ItemManager.CreateByName(M249_SHORTNAME, 1);
            if (item != null) return item;

            var def = ItemManager.FindItemDefinition(M249_SHORTNAME);
            if (def != null) return ItemManager.Create(def, 1);

            return null;
        }

        private void GiveOrDrop(BasePlayer player, Item item)
        {
            if (player?.inventory == null || item == null) return;

            if (player.inventory.containerBelt != null &&
                player.inventory.containerBelt.GiveItem(item))
                return;

            if (player.inventory.containerMain != null &&
                player.inventory.containerMain.GiveItem(item))
                return;

            var pos = player.transform.position + Vector3.up * 1.0f;
            var fwd = player.eyes != null ? player.eyes.HeadForward() : player.transform.forward;
            item.Drop(pos, fwd * 1.5f);
        }
        #endregion

        #region Qualify / Indestrutível / Enforce
        private bool HasUsePerm(BasePlayer player)
        {
            if (!config.RequirePermission) return true;
            return permission.UserHasPermission(player.UserIDString, PERM_USE)
                || permission.UserHasPermission(player.UserIDString, PERM_GIVE);
        }

        private bool MatchesSkin(ulong currentSkin) =>
            config.SkinID == 0UL || currentSkin == config.SkinID;

        private bool IsOurM249(Item item)
        {
            if (item == null || item.info == null) return false;
            if (item.info.shortname != M249_SHORTNAME) return false;

            bool nameOk = config.AllowedNames == null || config.AllowedNames.Count == 0 ||
                          (item.name != null && config.AllowedNames.Any(n => string.Equals(n, item.name, StringComparison.OrdinalIgnoreCase)));
            bool skinOk = MatchesSkin((ulong)item.skin);

            return config.RequireNameAndSkin ? (nameOk && skinOk) : (nameOk || skinOk);
        }

        private void ApplySkin(Item item, BasePlayer owner = null)
        {
            if (item == null || config.SkinID == 0UL) return;

            item.skin = config.SkinID;
            item.MarkDirty();

            var held = item.GetHeldEntity() as BaseEntity;
            if (held != null)
            {
                held.skinID = config.SkinID;
                held.SendNetworkUpdateImmediate();
            }
            else if (owner != null)
            {
                NextTick(() =>
                {
                    var again = item.GetHeldEntity() as BaseEntity;
                    if (again != null)
                    {
                        again.skinID = config.SkinID;
                        again.SendNetworkUpdateImmediate();
                    }
                });
            }
        }

        private void MakeIndestructible(Item item)
        {
            if (!config.Indestructible || item == null) return;
            if (item.maxCondition > 0f)
            {
                item.condition = item.maxCondition;
                item.conditionNormalized = 1f;
                item.MarkDirty();
            }
        }

        private object OnLoseCondition(Item item, float amount)
        {
            if (!config.Indestructible || item == null) return null;
            if (!IsOurM249(item)) return null;

            if (item.maxCondition > 0f && item.condition < item.maxCondition)
            {
                item.condition = item.maxCondition;
                item.conditionNormalized = 1f;
                item.MarkDirty();
            }
            return true;
        }

        private object OnItemLoseCondition(Item item, ref float amount)
        {
            if (!config.Indestructible || item == null) return null;
            if (!IsOurM249(item)) return null;

            amount = 0f;
            if (item.maxCondition > 0f && item.condition < item.maxCondition)
            {
                item.condition = item.maxCondition;
                item.conditionNormalized = 1f;
                item.MarkDirty();
            }
            return true;
        }
        #endregion

        #region HEAT / Overheat / Decay
        private bool TryGetCurrentAmmo(BasePlayer player, out int ammo)
        {
            ammo = 0;
            if (player == null) return false;

            var item = player.GetActiveItem();
            if (item == null || item.info == null || item.info.shortname != M249_SHORTNAME) return false;

            var held = item.GetHeldEntity() as BaseProjectile;
            if (held == null || held.primaryMagazine == null) return false;

            ammo = held.primaryMagazine.contents;
            return true;
        }

        private void BeginFiringBaseline(BasePlayer player, PlayerState st)
        {
            if (TryGetCurrentAmmo(player, out var ammo))
            {
                st.LastMag = ammo;
            }
        }

        private void AccumulateHeatFromAmmo(BasePlayer player, PlayerState st)
        {
            if (!TryGetCurrentAmmo(player, out var ammo)) return;

            if (st.LastMag < 0)
            {
                st.LastMag = ammo;
                return;
            }

            int consumed = Mathf.Max(0, st.LastMag - ammo);
            if (consumed > 0)
            {
                st.Heat = Mathf.Clamp(st.Heat + consumed, 0, config.MaxProgressShots);
                st.LastMag = ammo;
                st.LastEventTime = Time.realtimeSinceStartup;
            }
        }

        private void StartReleaseDecay(BasePlayer player, PlayerState st)
        {
            st.ReleaseDecayTimer?.Destroy();
            if (config.HeatDecayPerSecond <= 0f) return;

            float tick = Mathf.Max(0.02f, config.HeatDecayTick);
            st.ReleaseDecayTimer = timer.Every(tick, () =>
            {
                if (st.IsFiring || player == null || !player.IsConnected) { StopDecay(st); return; }
                if (st.OverheatState == OverheatStage.Lock) { StopDecay(st); return; } // durante lock não precisa

                float decayPerTick = config.HeatDecayPerSecond * tick;
                int newHeat = Mathf.Max(0, Mathf.RoundToInt(st.Heat - decayPerTick));
                if (newHeat == st.Heat) newHeat = Mathf.Max(0, st.Heat - 1);

                st.Heat = newHeat;
                st.LastEventTime = Time.realtimeSinceStartup;
                UpdateHudNow(player);

                if (st.Heat <= 0) StopDecay(st);
            });
        }

        private void StopDecay(PlayerState st)
        {
            st.ReleaseDecayTimer?.Destroy();
            st.ReleaseDecayTimer = null;
        }

        private void MaybeEnterOverheat(BasePlayer player, PlayerState st)
        {
            if (!config.OverheatEnabled) return;
            if (st.OverheatState != OverheatStage.None) return;

            float frac = (config.MaxProgressShots <= 0) ? 0f : (float)st.Heat / config.MaxProgressShots;
            if (frac >= Mathf.Clamp01(config.OverheatThreshold))
            {
                st.OverheatState = OverheatStage.Grace;
                st.OverheatEndTime = Time.realtimeSinceStartup + Mathf.Max(0.1f, config.OverheatGraceSeconds);
                player.ChatMessage(L(player, "OverheatGrace", config.OverheatGraceSeconds));

                PlayFx(player, config.FxHigh);
                UpdateHudNow(player);
            }
        }

        private void TickOverheat(BasePlayer player, PlayerState st)
        {
            if (st.OverheatState == OverheatStage.None) return;

            var now = Time.realtimeSinceStartup;

            if (st.OverheatState == OverheatStage.Grace)
            {
                float frac = (float)st.Heat / Mathf.Max(1, config.MaxProgressShots);
                if (!st.IsFiring && frac < Mathf.Clamp01(config.OverheatThreshold))
                {
                    st.OverheatState = OverheatStage.None;
                    UpdateHudNow(player);
                    return;
                }

                if (now >= st.OverheatEndTime)
                {
                    st.OverheatState = OverheatStage.Lock;
                    st.OverheatEndTime = now + Mathf.Max(0.1f, config.OverheatLockSeconds);

                    st.Heat = config.MaxProgressShots;
                    StopDecay(st);

                    player.ChatMessage(L(player, "OverheatLock", config.OverheatLockSeconds));
                    PlayFx(player, config.FxLock);
                    UpdateHudNow(player);
                }
            }
            else if (st.OverheatState == OverheatStage.Lock)
            {
                if (now >= st.OverheatEndTime)
                {
                    st.OverheatState = OverheatStage.None;

                    if (config.OverheatResetAfterLock)
                        st.Heat = 0;

                    if (st.Heat > 0) StartReleaseDecay(player, st);

                    UpdateHudNow(player);
                }
            }
        }
        #endregion

        #region Input / Dano
        private void ResetIdleTimer(BasePlayer player, PlayerState st)
        {
            st.IdleTimer?.Destroy();
            if (config.IdleResetSeconds <= 0f) return;

            st.IdleTimer = timer.Once(config.IdleResetSeconds + 0.02f, () =>
            {
                if (!st.IsFiring && st.ReleaseDecayTimer == null &&
                    Time.realtimeSinceStartup - st.LastEventTime >= config.IdleResetSeconds - 0.005f)
                {
                    st.Heat = 0;
                    UpdateHudNow(player);
                }
            });
        }

        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null || input == null) return;
            if (!HasUsePerm(player)) return;

            var item = player.GetActiveItem();
            if (item == null || !IsOurM249(item)) return;

            var st = GetState(player);
            bool fireDown = input.IsDown(BUTTON.FIRE_PRIMARY);

            if (fireDown)
            {
                if (!st.IsFiring)
                {
                    BeginFiringBaseline(player, st);
                    StopDecay(st);
                }

                st.IsFiring = true;

                AccumulateHeatFromAmmo(player, st);

                MaybeEnterOverheat(player, st);

                ResetIdleTimer(player, st);
                st.LastEventTime = Time.realtimeSinceStartup;
                UpdateHudNow(player);
            }
            else
            {
                if (st.IsFiring)
                {
                    st.IsFiring = false;
                    StartReleaseDecay(player, st);
                }
            }
        }

        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (info == null || entity == null) return;

            var attacker = info.InitiatorPlayer;
            if (attacker == null || !attacker.IsConnected) return;
            if (!HasUsePerm(attacker)) return;

            var active = attacker.GetActiveItem();
            if (active == null || !IsOurM249(active)) return;

            // 🔒 Proteção dura: MBird249 nunca causa dano em avião de carga / CH47
            if (entity is CargoPlane || entity is CH47HelicopterAIController)
            {
                info.damageTypes.ScaleAll(0f);
                return;
            }

            var st = GetState(attacker);

            if (config.OverheatEnabled && st.OverheatState == OverheatStage.Lock)
            {
                float before = info.damageTypes.Get(DamageType.Bullet);
                if (before > 0f) info.damageTypes.Scale(DamageType.Bullet, 0f);
                UpdateHudNow(attacker);
                return;
            }

            bool whitelisted = IsWhitelisted(entity);
            if (whitelisted)
            {
                st.LastCanDamage = true;
                st.CanDamageTintTimer?.Destroy();
                st.CanDamageTintTimer = timer.Once(0.6f, () =>
                {
                    st.LastCanDamage = false;
                    UpdateHudNow(attacker);
                });
                UpdateHudNow(attacker);
            }

            int shotsForDamage = Mathf.Clamp(st.Heat, 0, config.MaxProgressShots);
            if (shotsForDamage <= 0 || !whitelisted) return;

            if (IsGeometric())
            {
                float factor = Mathf.Pow(Mathf.Max(1f, config.GeoMultiplier), shotsForDamage);
                float before = info.damageTypes.Get(DamageType.Bullet);
                float after  = before * factor;
                info.damageTypes.Add(DamageType.Bullet, Mathf.Max(0f, after - before));
            }
            else
            {
                float flat = shotsForDamage * Mathf.Max(0f, config.DamagePerShot);
                if (flat > 0f) info.damageTypes.Add(DamageType.Bullet, flat);
            }
        }

        private bool IsWhitelisted(BaseCombatEntity ent)
        {
            if (ent == null) return false;

            // 🚫 Nunca buffar dano em avião de carga / CH47 (nem por prefab)
            if (ent is CargoPlane || ent is CH47HelicopterAIController)
                return false;

            if (ent.PrefabName != null)
            {
                var p0 = ent.PrefabName.ToLowerInvariant();
                if (p0.Contains("cargoplane") || p0.Contains("cargo_plane") || p0.Contains("ch47"))
                    return false;
            }

            // EXCLUSÃO explícita: não buffar dano em sentrys
            if (ent is AutoTurret || ent is FlameTurret || ent is SamSite)
                return false;

            if (ent is BasePlayer) return config.WLPlayers;
            if (config.WLBradley && ent is BradleyAPC) return true;

            if (config.WLHelicopter)
            {
                if (ent is BaseHelicopter) return true;
                var p = (ent.PrefabName ?? string.Empty).ToLowerInvariant();
                if (p.Contains("patrolhelicopter")) return true;
            }

            if (ent is BaseAnimalNPC) return config.WLAnimals;
            if (ent is BaseNpc) return config.WLNPCs;

            if (config.WLPrefabContains != null && config.WLPrefabContains.Count > 0)
            {
                var path = (ent.PrefabName ?? string.Empty).ToLowerInvariant();
                for (int i = 0; i < config.WLPrefabContains.Count; i++)
                {
                    var key = config.WLPrefabContains[i];
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    if (path.Contains(key.Trim().ToLowerInvariant()))
                        return true;
                }
            }

            return false;
        }

        private bool IsGeometric()
        {
            var s = (config.ProgressionMode ?? "Arithmetic").Trim();
            return s.IndexOf("geo", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        #endregion

        #region Troca de arma / Reload
        private void OnActiveItemChanged(BasePlayer player, Item oldItem, Item newItem)
        {
            if (player == null) return;

            if (newItem != null && IsOurM249(newItem))
            {
                ApplySkin(newItem, player);
                MakeIndestructible(newItem);
                player.SendNetworkUpdateImmediate();

                var st = GetState(player);
                st.LastMag = -1;
                st.Heat = 0;
                st.IsFiring = false;
                st.OverheatState = OverheatStage.None;
                st.OverheatEndTime = 0;
                st.LastCanDamage = false;
                st.CanDamageTintTimer?.Destroy(); st.CanDamageTintTimer = null;
                StopDecay(st);

                UpdateHudNow(player);
            }
            else
            {
                DestroyHud(player);
            }
        }

        private void OnReloadWeapon(BasePlayer player, BaseProjectile projectile)
        {
            if (player == null || projectile == null) return;

            var item = player.GetActiveItem();
            if (item == null || item.info == null || item.info.shortname != M249_SHORTNAME) return;
            if (!IsOurM249(item)) return;

            var st = GetState(player);

            if (TryGetCurrentAmmo(player, out var ammo))
                st.LastMag = ammo;

            st.LastEventTime = Time.realtimeSinceStartup;
            UpdateHudNow(player);
        }
        #endregion

        #region HUD
        private void HudTick()
        {
            if (!config.HudEnabled) return;

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected) continue;
                if (!HasUsePerm(player)) { DestroyHud(player); continue; }

                var item = player.GetActiveItem();
                if (item == null || !IsOurM249(item)) { DestroyHud(player); continue; }

                var st = GetState(player);

                TickOverheat(player, st);

                if (st.IsFiring) AccumulateHeatFromAmmo(player, st);

                DrawHud(player, st);
            }
        }

        private void UpdateHudNow(BasePlayer player)
        {
            if (player == null || !player.IsConnected) return;
            if (!HasUsePerm(player)) { DestroyHud(player); return; }

            var item = player.GetActiveItem();
            if (item == null || !IsOurM249(item)) { DestroyHud(player); return; }

            DrawHud(player, GetState(player));
        }

        private void DrawHud(BasePlayer player, PlayerState st)
        {
            int limit = Math.Max(1, config.MaxProgressShots);
            int value = Mathf.Clamp(st.Heat, 0, limit);
            float frac = Mathf.Clamp01((float)value / limit);

            var container = new CuiElementContainer();

            var xMin = config.HudX - config.HudW / 2f;
            var xMax = config.HudX + config.HudW / 2f;
            var yMin = config.HudY - config.HudH / 2f;
            var yMax = config.HudY + config.HudH / 2f;

            container.Add(new CuiPanel
            {
                Image = { Color = config.PanelColor },
                RectTransform = { AnchorMin = $"{xMin} {yMin}", AnchorMax = $"{xMax} {yMax}" },
                CursorEnabled = false
            }, "Hud", UI_PANEL);

            const float padX = 0.02f;
            const float padY = 0.10f;

            var barMinX = padX;
            var barMaxX = 1f - padX;
            var barMinY = padY;
            var barMaxY = 1f - padY;

            container.Add(new CuiPanel
            {
                Image = { Color = config.BarBGColor },
                RectTransform = { AnchorMin = $"{barMinX} {barMinY}", AnchorMax = $"{barMaxX} {barMaxY}" },
                CursorEnabled = false
            }, UI_PANEL, UI_BARBG);

            string fgColor = st.LastCanDamage
                ? config.BarHitColor
                : HeatColor(frac, st);

            container.Add(new CuiPanel
            {
                Image = { Color = fgColor },
                RectTransform = { AnchorMin = "0 0", AnchorMax = $"{frac} 1" },
                CursorEnabled = false
            }, UI_BARBG, UI_BARFG);

            string title;
            if (st.OverheatState == OverheatStage.Lock)
            {
                double left = Math.Max(0, st.OverheatEndTime - Time.realtimeSinceStartup);
                title = $"{config.OverheatTextPrefix} (LOCK): {left:0.0}s";
            }
            else if (st.OverheatState == OverheatStage.Grace)
            {
                double left = Math.Max(0, st.OverheatEndTime - Time.realtimeSinceStartup);
                title = $"{config.OverheatTextPrefix}: {left:0.0}s";
            }
            else if (IsGeometric())
            {
                var factor = Mathf.Pow(Mathf.Max(1f, config.GeoMultiplier), value);
                title = $"Damage: x{factor:0.##}";
            }
            else
            {
                var bonus = Mathf.RoundToInt(value * Mathf.Max(0f, config.DamagePerShot));
                title = $"Damage: +{bonus}";
            }

            container.Add(new CuiLabel
            {
                Text = { Text = title, FontSize = Mathf.Clamp(config.HudFontSize, 8, 32), Align = TextAnchor.MiddleCenter, Color = config.TextColor },
                RectTransform = { AnchorMin = $"{barMinX} {barMinY}", AnchorMax = $"{barMaxX} {barMaxY}" }
            }, UI_PANEL, UI_TEXT1);

            CuiHelper.DestroyUi(player, UI_PANEL);
            CuiHelper.AddUi(player, container);
            st.UiOpen = true;
        }

        private string HeatColor(float frac, PlayerState st)
        {
            if (!config.UseSmoothGradient)
            {
                if (frac < 0.5f) return "0 1 0 0.9";            // verde
                if (frac < 0.8f) return "1 1 0 0.9";            // amarelo
                if (st.OverheatState != OverheatStage.None) return "1 0 0 0.95"; // overheat = vermelho
                return "1 0.65 0 0.9";                           // laranja
            }

            Color c0 = new Color(0f, 1f, 0f, 0.9f);    // verde
            Color c1 = new Color(1f, 1f, 0f, 0.9f);    // amarelo
            Color c2 = new Color(1f, 0.65f, 0f, 0.9f); // laranja
            Color c3 = new Color(1f, 0f, 0f, 0.95f);   // vermelho

            Color mix;
            if (st.OverheatState == OverheatStage.Lock) mix = c3;
            else if (frac < 0.5f) mix = Color.Lerp(c0, c1, frac / 0.5f);
            else if (frac < 0.8f) mix = Color.Lerp(c1, c2, (frac - 0.5f) / 0.3f);
            else mix = Color.Lerp(c2, c3, (frac - 0.8f) / 0.2f);

            return $"{mix.r:0.###} {mix.g:0.###} {mix.b:0.###} {mix.a:0.###}";
        }
        #endregion

        #region Util
        private void PlayFx(BasePlayer player, string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return;
            try
            {
                Effect.server.Run(prefab, player.transform.position, Vector3.up);
            }
            catch { /* ignora prefab inválido */ }
        }

        private void DestroyHud(BasePlayer player)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, UI_PANEL);
            if (states.TryGetValue(player.userID, out var st)) st.UiOpen = false;
        }
        #endregion
    }
}
