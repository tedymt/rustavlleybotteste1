using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using Rust;
using Facepunch;
using Network; // NetworkableId

namespace Oxide.Plugins
{
    [Info("RocketFury", "Mestre Pardal", "1.1.2")]
    [Description("Lança-foguetes indestrutível que carrega um Especial após N tiros, com HUD, aviso cinematográfico e sons customizados.")]
    public class RocketFury : RustPlugin
    {
        private const string PERM_USE  = "rocketfury.use";
        private const string PERM_GIVE = "rocketfury.give";

        private const string UI_PANEL = "RFURY_PANEL";
        private const string UI_TEXT1 = "RFURY_TEXT1";
        private const string UI_BARBG = "RFURY_BARBG";
        private const string UI_BARFG = "RFURY_BARFG";

        // 🔊 Prefabs de efeitos/sons (válidos) + fallback
        private const string FX_ROCKET_EXPLOSION   = "assets/prefabs/weapons/rocketlauncher/effects/rocket_explosion.prefab";
        private const string FX_EXPLOSION_FALLBACK = "assets/bundled/prefabs/fx/explosions/explosion_3.prefab";
        private const string FX_CODELOCK_OPEN      = "assets/prefabs/locks/keypad/effects/lock.code.unlock.prefab";

        // 🎬 UI cinematográfica
        private const string UI_READY_PANEL = "RFURY_READY_PANEL";
        private const string UI_READY_LABEL = "RFURY_READY_LABEL";

        private ConfigData config;
        private readonly Dictionary<ulong, PlayerState> states = new Dictionary<ulong, PlayerState>();
        private readonly HashSet<NetworkableId> specialRocketNetIDs = new HashSet<NetworkableId>();
        private Timer hudTick;

        #region Config
        private class ConfigData
        {
            [JsonProperty(PropertyName = "Permission Required (rocketfury.use)")]
            public bool RequirePermission = true;

            // Mantemos como ulong (API). Sempre compare com 0UL.
            [JsonProperty(PropertyName = "Rocket Launcher Skin ID")]
            public ulong SkinID = 3546654107UL;

            [JsonProperty(PropertyName = "Allowed Display Names (any match qualifies)")]
            public List<string> AllowedNames = new List<string> { "Rocket Fury" };

            [JsonProperty(PropertyName = "Require BOTH Name and Skin to qualify")]
            public bool RequireNameAndSkin = false;

            [JsonProperty(PropertyName = "Indestructible Launcher (block condition loss)")]
            public bool Indestructible = true;

            [JsonProperty(PropertyName = "Shots Required To Charge Special")]
            public int ShotsRequired = 5;

            [JsonProperty(PropertyName = "Idle Reset Seconds (reset progress if no shots)")]
            public float IdleResetSeconds = 30f;

            [JsonProperty(PropertyName = "Expire Special After Seconds (0 = never)")]
            public float SpecialExpireSeconds = 20f;

            [JsonProperty(PropertyName = "Special Damage Multiplier")]
            public float SpecialDamageMultiplier = 10.0f;

            [JsonProperty(PropertyName = "Special Radius Multiplier")]
            public float SpecialRadiusMultiplier = 3.0f;

            [JsonProperty(PropertyName = "Affect Rockets (short prefab names)")]
            public List<string> RocketPrefabs = new List<string>
            {
                "rocket_basic","rocket_hv","rocket_fire"
            };

            [JsonProperty(PropertyName = "HUD - Enabled")]
            public bool HudEnabled = true;

            [JsonProperty(PropertyName = "HUD - Font Size")]
            public int HudFontSize = 10;

            [JsonProperty(PropertyName = "HUD - Anchor X")]
            public float HudX = 0.5f;

            [JsonProperty(PropertyName = "HUD - Anchor Y")]
            public float HudY = 0.12f;

            [JsonProperty(PropertyName = "HUD - Width")]
            public float HudW = 0.2f;

            [JsonProperty(PropertyName = "HUD - Height")]
            public float HudH = 0.02f;

            [JsonProperty(PropertyName = "HUD - Colors [Panel, Text, BarBG, BarFG, Ready]")]
            public string PanelColor = "0 0 0 0";
            public string TextColor  = "1 1 1 1";
            public string BarBGColor = "0.15 0.15 0.15 0.9";
            public string BarFGColor = "0.0 0.8 1 0.95";
            public string ReadyColor = "0.08 0.35 0.08 0.95";

            [JsonProperty(PropertyName = "Give Command (chat)")]
            public string GiveCommand = "rfury";

            [JsonProperty(PropertyName = "Given Display Name")]
            public string GivenName = "Rocket Fury";

            [JsonProperty(PropertyName = "Auto-rename & enforce skin when holding")]
            public bool EnforceWhileHolding = true;

            [JsonProperty(PropertyName = "Localize (PT-BR & EN)")]
            public bool Localize = true;
        }
        protected override void LoadDefaultConfig() => config = new ConfigData();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<ConfigData>();
            }
            catch
            {
                PrintError("Config inválida; carregando padrão.");
                LoadDefaultConfig();
            }

            var changed = false;

            if (config.ShotsRequired < 1) { config.ShotsRequired = 1; changed = true; }
            if (config.HudFontSize <= 0) { config.HudFontSize = 12; changed = true; }

            if (config.AllowedNames == null) { config.AllowedNames = new List<string>(); changed = true; }
            else
            {
                var dedupNames = config.AllowedNames
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (dedupNames.Count != config.AllowedNames.Count) { config.AllowedNames = dedupNames; changed = true; }
            }

            if (config.RocketPrefabs == null) { config.RocketPrefabs = new List<string>(); changed = true; }
            else
            {
                var dedupRockets = config.RocketPrefabs
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim().ToLowerInvariant())
                    .Distinct()
                    .ToList();
                if (dedupRockets.Count != config.RocketPrefabs.Count) { config.RocketPrefabs = dedupRockets; changed = true; }
            }

            if (changed) SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(config);
        #endregion

        #region Lang
        private void LoadDefaultMessages()
        {
            var en = new Dictionary<string, string>
            {
                ["Given"] = "<color=#00FFFF>[RocketFury]</color> You received the launcher!",
                ["NoPerm"] = "<color=#00FFFF>[RocketFury]</color> You don't have permission.",
                ["ResetProgress"] = "<color=#00FFFF>[RocketFury]</color> Progress reset due to inactivity.",
                ["SpecialExpired"] = "<color=#00FFFF>[RocketFury]</color> Special expired."
            };
            var pt = new Dictionary<string, string>
            {
                ["Given"] = "<color=#00FFFF>[RocketFury]</color> Você recebeu o lança-foguetes!",
                ["NoPerm"] = "<color=#00FFFF>[RocketFury]</color> Você não tem permissão.",
                ["ResetProgress"] = "<color=#00FFFF>[RocketFury]</color> Progresso zerado por inatividade.",
                ["SpecialExpired"] = "<color=#00FFFF>[RocketFury]</color> Especial expirada."
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

        #region Data
        private class PlayerState
        {
            public int Count;
            public bool SpecialReady;
            public double LastShotTime;
            public double SpecialReadyUntil; // 0 = no expiry
            [JsonIgnore] public Timer IdleTimer;
            [JsonIgnore] public bool UiOpen;
        }
        private class StoredData { public Dictionary<ulong, PlayerState> Players = new Dictionary<ulong, PlayerState>(); }
        private StoredData data;

        private void LoadData()
        {
            try { data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name) ?? new StoredData(); }
            catch { data = new StoredData(); }
            foreach (var kvp in data.Players) states[kvp.Key] = kvp.Value;
        }
        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, data);
        #endregion

        #region Lifecycle
        private void Init()
        {
            permission.RegisterPermission(PERM_USE, this);
            permission.RegisterPermission(PERM_GIVE, this);
            LoadDefaultMessages();
            LoadData();
            if (hudTick == null) hudTick = timer.Every(1f, HudTick);
        }
        private void OnServerInitialized()
        {
            foreach (var p in BasePlayer.activePlayerList) DestroyHud(p);
        }
        private void Unload()
        {
            foreach (var st in states.Values) st.IdleTimer?.Destroy();
            hudTick?.Destroy(); hudTick = null;
            foreach (var p in BasePlayer.activePlayerList) DestroyHud(p);
            SaveData();
        }
        private void OnServerSave() => SaveData();
        private void OnPlayerDisconnected(BasePlayer player, string reason) => DestroyHud(player);
        #endregion

        #region Helpers (skin + effects)
        private bool MatchesSkin(ulong currentSkin) =>
            config.SkinID == 0UL || currentSkin == config.SkinID;

        private void ApplySkin(Item item, BasePlayer owner = null)
        {
            if (item == null || config.SkinID == 0UL) return;

            // Ícone/Item
            item.skin = config.SkinID;
            item.MarkDirty();

            // Modelo em mãos (fix)
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

        // Executa efeito com validação e fallback silencioso (sem poluir RCON)
        private void RunEffectSafe(string prefabPath, Vector3 position)
        {
            try
            {
                var go = GameManager.server.FindPrefab(prefabPath);
                if (go != null)
                {
                    Effect.server.Run(prefabPath, position);
                    return;
                }

                var goFallback = GameManager.server.FindPrefab(FX_EXPLOSION_FALLBACK);
                if (goFallback != null)
                {
                    Effect.server.Run(FX_EXPLOSION_FALLBACK, position);
                }
            }
            catch { /* silêncio total */ }
        }
        #endregion

        #region Commands
        [ChatCommand("rfury")]
        private void CmdGive(BasePlayer player, string cmd, string[] args)
        {
            if (!string.Equals(cmd, config.GiveCommand, StringComparison.OrdinalIgnoreCase)) return;
            if (player == null) return;

            if (config.RequirePermission && !permission.UserHasPermission(player.UserIDString, PERM_GIVE))
            {
                player.ChatMessage(L(player, "NoPerm"));
                return;
            }

            var item = ItemManager.CreateByName("rocket.launcher", 1);
            if (item == null) return;

            ApplySkin(item, player);
            item.name = config.GivenName;
            MakeIndestructible(item); // 🔒

            player.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);
            player.ChatMessage(L(player, "Given"));
        }
        #endregion

        #region Qualify / Indestrutível
        private bool HasUsePerm(BasePlayer player)
        {
            if (!config.RequirePermission) return true;
            return permission.UserHasPermission(player.UserIDString, PERM_USE)
                || permission.UserHasPermission(player.UserIDString, PERM_GIVE);
        }

        private bool IsOurLauncher(Item item)
        {
            if (item == null || item.info == null) return false;
            if (item.info.shortname != "rocket.launcher") return false;

            bool nameOk = config.AllowedNames == null || config.AllowedNames.Count == 0 ||
                          (item.name != null && config.AllowedNames.Any(n => string.Equals(n, item.name, StringComparison.OrdinalIgnoreCase)));
            bool skinOk = MatchesSkin((ulong)item.skin);

            return config.RequireNameAndSkin ? (nameOk && skinOk) : (nameOk || skinOk);
        }

        private void MakeIndestructible(Item item)
        {
            if (!config.Indestructible || item == null) return;
            if (item.maxCondition > 0f)
            {
                item.condition = item.maxCondition;
                item.conditionNormalized = 1f; // mantém 100%
                item.MarkDirty();
            }
        }

        // 🔁 Hook NOVO (uMod/Oxide recentes)
        private object OnLoseCondition(Item item, float amount)
        {
            if (!config.Indestructible || item == null) return null;
            if (!IsOurLauncher(item)) return null;

            // Cancela a perda e restaura
            if (item.maxCondition > 0f && item.condition < item.maxCondition)
            {
                item.condition = item.maxCondition;
                item.conditionNormalized = 1f;
                item.MarkDirty();
            }
            return true; // bloqueia perda
        }

        // 🔁 Hook ANTIGO (retrocompat)
        private object OnItemLoseCondition(Item item, ref float amount)
        {
            if (!config.Indestructible || item == null) return null;
            if (!IsOurLauncher(item)) return null;

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

        #region Progress / Especial
        private PlayerState GetState(BasePlayer player)
        {
            if (!states.TryGetValue(player.userID, out var st))
            {
                st = new PlayerState();
                states[player.userID] = st;
                data.Players[player.userID] = st;
            }
            return st;
        }

        private void IncrementProgress(BasePlayer player)
        {
            var st = GetState(player);
            st.Count++;
            st.LastShotTime = Time.realtimeSinceStartup;
            ResetIdleTimer(player, st);

            if (st.Count >= config.ShotsRequired)
            {
                st.Count = 0;
                st.SpecialReady = true;
                st.SpecialReadyUntil = config.SpecialExpireSeconds > 0
                    ? Time.realtimeSinceStartup + config.SpecialExpireSeconds
                    : 0;

                st.IdleTimer?.Destroy();
                st.IdleTimer = null;
                st.LastShotTime = 0;

                // 🎬 aviso cinematográfico + 🔊 som de codelock vindo do player
                ShowCinematicReady(player);
                RunEffectSafe(FX_CODELOCK_OPEN, player.transform.position);
            }
        }

        private void ResetProgress(BasePlayer player, PlayerState st, bool notify = true)
        {
            st.Count = 0;
            st.SpecialReady = false;
            st.SpecialReadyUntil = 0;

            st.IdleTimer?.Destroy(); st.IdleTimer = null;
            st.LastShotTime = 0;

            if (notify) player.ChatMessage(L(player, "ResetProgress"));
        }

        private void ResetIdleTimer(BasePlayer player, PlayerState st)
        {
            st.IdleTimer?.Destroy();
            if (config.IdleResetSeconds <= 0f) return;

            if (st.SpecialReady) return;

            st.IdleTimer = timer.Once(config.IdleResetSeconds + 0.05f, () =>
            {
                if (!st.SpecialReady && Time.realtimeSinceStartup - st.LastShotTime >= config.IdleResetSeconds - 0.01f)
                    ResetProgress(player, st, true);
            });
        }

        private bool ConsumeSpecialIfReady(BasePlayer player)
        {
            var st = GetState(player);
            if (!st.SpecialReady) return false;

            if (st.SpecialReadyUntil > 0 && Time.realtimeSinceStartup >= st.SpecialReadyUntil)
            {
                st.SpecialReady = false;
                st.SpecialReadyUntil = 0;
                player.ChatMessage(L(player, "SpecialExpired"));
                return false;
            }

            st.SpecialReady = false;
            st.SpecialReadyUntil = 0;
            return true;
        }
        #endregion

        #region Rocket Hooks
        private void OnEntitySpawned(BaseNetworkable entity)
        {
            if (entity == null) return;

            var shortname = entity.ShortPrefabName;
            if (string.IsNullOrEmpty(shortname)) return;
            if (!config.RocketPrefabs.Contains(shortname)) return;

            var rocket = entity as BaseEntity;
            if (rocket == null) return;

            BasePlayer owner = null;
            try { owner = (rocket as BaseEntity)?.creatorEntity as BasePlayer; } catch { }
            if (owner == null || !owner.IsConnected) return;
            if (!HasUsePerm(owner)) return;

            var active = owner.GetActiveItem();
            if (active == null || !IsOurLauncher(active)) return;

            if (ConsumeSpecialIfReady(owner))
            {
                TryBuffRocket(rocket);
                if (rocket.net != null) specialRocketNetIDs.Add(rocket.net.ID);
                return;
            }

            IncrementProgress(owner);
        }

        private void TryBuffRocket(BaseEntity rocket)
        {
            try
            {
                var timed = rocket.GetComponent<TimedExplosive>();
                if (timed != null)
                {
                    var list = timed.damageTypes;
                    if (list != null)
                    {
                        var mult = config.SpecialDamageMultiplier;
                        for (int i = 0; i < list.Count; i++)
                        {
                            var e = list[i];
                            e.amount *= mult;
                            list[i] = e;
                        }
                    }

                    timed.explosionRadius *= config.SpecialRadiusMultiplier;
                    rocket.SendNetworkUpdateImmediate();
                }
            }
            catch { /* silencioso */ }
        }

        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (info == null) return;
            var initiator = info.Initiator as BaseEntity;
            if (initiator == null || initiator.net == null) return;

            if (specialRocketNetIDs.Contains(initiator.net.ID))
            {
                try { info.damageTypes.ScaleAll(config.SpecialDamageMultiplier); } catch { }
            }
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            if (entity?.net == null) return;

            if (specialRocketNetIDs.Contains(entity.net.ID))
            {
                var be = entity as BaseEntity;
                if (be != null)
                {
                    var pos = be.transform.position;
                    // 🔊 explosão alta e válida + fallback
                    RunEffectSafe(FX_ROCKET_EXPLOSION, pos);
                }
                specialRocketNetIDs.Remove(entity.net.ID);
            }
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
                if (item == null || !IsOurLauncher(item)) { DestroyHud(player); continue; }

                var st = GetState(player);

                if (st.SpecialReady && st.SpecialReadyUntil > 0 && Time.realtimeSinceStartup >= st.SpecialReadyUntil)
                {
                    ExpireSpecial(player, st);
                }

                bool ready = st.SpecialReady;

                float timerSeconds = -1f;

                if (ready && st.SpecialReadyUntil > 0)
                {
                    var remain = (float)(st.SpecialReadyUntil - Time.realtimeSinceStartup);
                    if (remain > 0f) timerSeconds = remain;
                }
                else if (!ready && config.IdleResetSeconds > 0 && st.LastShotTime > 0 && st.Count > 0)
                {
                    var elapsed = Time.realtimeSinceStartup - st.LastShotTime;
                    var remain = Mathf.Clamp((float)(config.IdleResetSeconds - elapsed), 0f, config.IdleResetSeconds);
                    if (remain > 0f) timerSeconds = remain;
                }

                DrawHud(player, st.Count, config.ShotsRequired, ready, timerSeconds, st);
            }
        }

        private void ExpireSpecial(BasePlayer player, PlayerState st)
        {
            st.SpecialReady = false;
            st.SpecialReadyUntil = 0;
            st.Count = 0;
            st.LastShotTime = 0;
            st.IdleTimer?.Destroy();
            st.IdleTimer = null;
            player.ChatMessage(L(player, "SpecialExpired"));
        }

        private void DrawHud(BasePlayer player, int count, int need, bool ready, float timerSeconds, PlayerState st)
        {
            var container = new CuiElementContainer();

            // Painel base
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

            // Barra
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

            float frac = Mathf.Clamp01(need <= 0 ? 0f : (float)count / need);
            if (ready) frac = 1f;

            container.Add(new CuiPanel
            {
                Image = { Color = ready ? config.ReadyColor : config.BarFGColor },
                RectTransform = { AnchorMin = "0 0", AnchorMax = $"{frac} 1" },
                CursorEnabled = false
            }, UI_BARBG, UI_BARFG);

            string title;
            if (ready)
                title = (timerSeconds >= 0f) ? $"RocketFury — {Mathf.CeilToInt(timerSeconds)}s" : "RocketFury — READY!";
            else
                title = (timerSeconds >= 0f) ? $"RocketFury — {Mathf.CeilToInt(timerSeconds)}s" : "RocketFury";

            container.Add(new CuiLabel
            {
                Text = { Text = title, FontSize = Mathf.Clamp(config.HudFontSize, 8, 28), Align = TextAnchor.MiddleCenter, Color = config.TextColor },
                RectTransform = { AnchorMin = $"{barMinX} {barMinY}", AnchorMax = $"{barMaxX} {barMaxY}" }
            }, UI_PANEL, UI_TEXT1);

            CuiHelper.DestroyUi(player, UI_PANEL);
            CuiHelper.AddUi(player, container);
            st.UiOpen = true;
        }

        private void DestroyHud(BasePlayer player)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, UI_PANEL);
            DestroyReadyUI(player);
            if (states.TryGetValue(player.userID, out var st)) st.UiOpen = false;
        }
        #endregion

        #region UI Cinematográfica (Hollywood style)
        private void ShowCinematicReady(BasePlayer player)
        {
            DestroyReadyUI(player);

            // Passo 1: surge menor e levemente transparente
            var cont = new CuiElementContainer();
            cont.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = false
            }, "Hud", UI_READY_PANEL);

            cont.Add(new CuiLabel
            {
                Text = { Text = "ESPECIAL PRONTA!", FontSize = 24, Align = TextAnchor.MiddleCenter, Color = "1 1 1 0.85" },
                RectTransform = { AnchorMin = "0.35 0.45", AnchorMax = "0.65 0.55" }
            }, UI_READY_PANEL, UI_READY_LABEL);

            CuiHelper.AddUi(player, cont);

            // Passo 2: “aproxima”
            timer.Once(0.5f, () =>
            {
                var c2 = new CuiElementContainer();
                c2.Add(new CuiLabel
                {
                    Text = { Text = "ESPECIAL PRONTA!", FontSize = 32, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.30 0.42", AnchorMax = "0.70 0.58" }
                }, UI_READY_PANEL, UI_READY_LABEL);

                CuiHelper.DestroyUi(player, UI_READY_LABEL);
                CuiHelper.AddUi(player, c2);
            });

            // Passo 3: some
            timer.Once(1.4f, () =>
            {
                var c3 = new CuiElementContainer();
                c3.Add(new CuiLabel
                {
                    Text = { Text = "ESPECIAL PRONTA!", FontSize = 28, Align = TextAnchor.MiddleCenter, Color = "1 1 1 0.0" },
                    RectTransform = { AnchorMin = "0.32 0.44", AnchorMax = "0.68 0.56" }
                }, UI_READY_PANEL, UI_READY_LABEL);

                CuiHelper.DestroyUi(player, UI_READY_LABEL);
                CuiHelper.AddUi(player, c3);

                timer.Once(0.25f, () => DestroyReadyUI(player));
            });
        }

        private void DestroyReadyUI(BasePlayer player)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, UI_READY_PANEL);
        }
        #endregion

        #region QoL / Enforce skin & nome
        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null || !player.IsConnected || !config.EnforceWhileHolding) return;
            var item = player.GetActiveItem();
            if (item == null) return;

            if (IsOurLauncher(item))
            {
                if (!MatchesSkin((ulong)item.skin))
                {
                    ApplySkin(item, player);
                    player.SendNetworkUpdateImmediate();
                }

                if (config.AllowedNames != null && config.AllowedNames.Count > 0)
                {
                    var current = item.name ?? string.Empty;
                    if (!config.AllowedNames.Any(n => string.Equals(n, current, StringComparison.OrdinalIgnoreCase)))
                    {
                        item.name = config.AllowedNames[0];
                        item.MarkDirty();
                        player.SendNetworkUpdateImmediate();
                    }
                }

                MakeIndestructible(item); // 🔒 manter sempre
            }
        }

        private void OnActiveItemChanged(BasePlayer player, Item oldItem, Item newItem)
        {
            if (player == null || newItem == null) return;
            if (!IsOurLauncher(newItem)) return;

            ApplySkin(newItem, player);
            MakeIndestructible(newItem); // 🔒 ao equipar
            player.SendNetworkUpdateImmediate();
        }
        #endregion
    }
}