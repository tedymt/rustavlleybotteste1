using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using Rust;

namespace Oxide.Plugins
{
    [Info("M249AutoUp", "Mestre Pardal", "2.6.1")]
    [Description("M249 especial: dano escala por kill, magazine por nível, HUD acima da hotbar, categorias configuráveis, integração SkillTree (opcional).")]
    public class M249AutoUp : RustPlugin
    {
        #region Consts & Perms

        private const string SHORTNAME = "lmg.m249";
        private const string DEFAULT_NAME = "Highlander";
        private const ulong DEFAULT_SKIN = 3578475157;

        private const string PERM_USE  = "m249autoup.use";
        private const string PERM_GIVE = "m249autoup.give";

        private const string CHAT_CMD_GIVE = "highlander";

        private const string CON_PREFIX = "m249autoup.";
        private const string DATA_FOLDER = "M249AutoUp";

        private const string UI_ROOT = "M249HUD_Root";
        private const string UI_BAR_BG = "M249HUD_BarBG";

        #endregion

        #region Config

        private PluginConfig C;

        private class PluginConfig
        {
            [JsonProperty("Weapon Display Name")] public string WeaponName = DEFAULT_NAME;
            [JsonProperty("Weapon Skin (ulong)")] public ulong WeaponSkin = DEFAULT_SKIN;
            [JsonProperty("Make Weapon Indestructible")] public bool Indestructible = true;
            [JsonProperty("Require Use Permission For Bonus")] public bool RequireUsePerm = true;

            [JsonProperty("Per Kill Damage Bonus (e.g., 0.02 = +2%)")] public float PerKillBonus = 0.002f;
            [JsonProperty("Max Total Bonus Multiplier (1.0 = no bonus, 2.5 = +150%)")] public float MaxTotalMultiplier = 500.0f;

            [JsonProperty("Ignore Explosive Ammo For Bonus (ammo.rifle.explosive)")]
            public bool IgnoreExplosiveAmmo = true;

            [JsonProperty("Apply Bonus On Categories")] public CategoryMask Categories = new CategoryMask();
            [JsonProperty("Magazine Upgrade")] public MagazineConf Magazine = new MagazineConf();

            [JsonProperty("Health Upgrade")] public HealthConf Health = new HealthConf();
            [JsonProperty("Lifesteal Upgrade")] public LifestealConf Lifesteal = new LifestealConf();
            [JsonProperty("Critical Upgrade")] public CriticalConf Critical = new CriticalConf();
            [JsonProperty("Chat Style")] public ChatStyleConf Chat = new ChatStyleConf();
            [JsonProperty("Integration")] public IntegrationConf Integration = new IntegrationConf();

            [JsonProperty("HUD - AnchorMin (x y)")] public string HudAnchorMin = "0.3442 0.11";
            [JsonProperty("HUD - AnchorMax (x y)")] public string HudAnchorMax = "0.4930 0.135";
            [JsonProperty("HUD - Panel Color (r g b a)")] public string HudPanelColor = "0 0 0 0.60";
            [JsonProperty("HUD - Line Color (r g b a)")] public string HudLineColor = "0.15 0.15 0.15 0.80";
            [JsonProperty("HUD - Font Color (r g b a)")] public string HudFontColor = "1 1 1 1";
            [JsonProperty("HUD - Label Color (hex)")]   public string HudLabelHex = "#00FFFF";
            [JsonProperty("HUD - Value Color (hex)")]   public string HudValueHex = "#FFFFFF";
            [JsonProperty("HUD - FontSize")]            public int HudFontSize = 12;

            // ✅ NOVO: mensagem quando NÃO tem permissão de uso (aparece dentro da HUD)
            [JsonProperty("HUD - No Permission Text")] public string HudNoPermText = "EXCLUSIVO VIP 5";
            [JsonProperty("HUD - No Permission Text Color (hex)")] public string HudNoPermHex = "#FF3333";
            [JsonProperty("HUD - No Permission Text Size")] public int HudNoPermFontSize = 13;

            [JsonProperty("Messages")] public Dictionary<string, Dictionary<string, string>> Messages = DefaultMessages();
        }

        private class CategoryMask
        {
            [JsonProperty("Players")] public bool Players = true;
            [JsonProperty("NPCs")] public bool NPCs = true;
            [JsonProperty("Animals")] public bool Animals = true;
            [JsonProperty("Helicopter (Patrol, CH47)")] public bool Helicopters = true;
            [JsonProperty("Bradley APC")] public bool Bradley = true;
            [JsonProperty("SentryGun / AutoTurret")] public bool Sentry = true;
            [JsonProperty("Traps (e.g., shotgun trap)")] public bool Traps = true;
        }

        private class MagazineConf
        {
            [JsonProperty("Enabled")] public bool Enabled = true;
            [JsonProperty("Base Capacity (vanilla 100)")] public int BaseCapacity = 100;
            [JsonProperty("Kills Per Level")] public int KillsPerLevel = 20;
            [JsonProperty("Bonus Per Level (extra rounds)")] public int BonusPerLevel = 5;
            [JsonProperty("Max Extra (cap)")] public int MaxExtra = 1000;
        }

        private class HealthConf
        {
            [JsonProperty("Enabled")] public bool Enabled = true;
            [JsonProperty("Base Max Health (vanilla 100)")] public float BaseMaxHealth = 100f;
            [JsonProperty("Use Current Player Max Health As Base")] public bool UsePlayerCurrentMaxAsBase = true;
            [JsonProperty("Kills Per Level (same idea as magazine)")] public int KillsPerLevel = 20;
            [JsonProperty("Bonus Max Health Per Level")] public float BonusMaxHealthPerLevel = 5f;
            [JsonProperty("Max Extra Max Health (cap)")] public float MaxExtraMaxHealth = 100f;
            [JsonProperty("Only While Weapon Equipped")] public bool OnlyWhileEquipped = true;
        }

        private class LifestealConf
        {
            [JsonProperty("Enabled")] public bool Enabled = true;
            [JsonProperty("Percent Of Damage Converted To Heal (0.02 = 2%)")] public float Percent = 0.02f;
            [JsonProperty("Max Heal Per Hit")] public float MaxHealPerHit = 15f;
            [JsonProperty("Max Heal Per Second")] public float MaxHealPerSecond = 25f;
            [JsonProperty("Allow On Players")] public bool Players = true;
            [JsonProperty("Allow On NPCs")] public bool NPCs = true;
            [JsonProperty("Allow On Animals")] public bool Animals = true;
            [JsonProperty("Allow On Helicopters")] public bool Helicopters = false;
            [JsonProperty("Allow On Bradley")] public bool Bradley = false;
        
            [JsonProperty("Heal Popup UI")] public KillPopupConf Popup = new KillPopupConf();
        }


        public class KillPopupConf
{
    [JsonProperty("Enabled")] public bool Enabled = true;
    [JsonProperty("Seconds")] public float Seconds = 1f;
    [JsonProperty("Base Font Size")] public int BaseFontSize = 20;
    [JsonProperty("Dynamic Size At Heal=20")] public int DynamicSizeAt20 = 12;
    [JsonProperty("Color RGBA")] public string ColorRGBA = "0 0.92 1 1";
    [JsonProperty("AnchorMin")] public string AnchorMin = "0.45 0.47";
    [JsonProperty("AnchorMax")] public string AnchorMax = "0.55 0.53";
    [JsonProperty("Text Template (use {heal})")] public string TextTemplate = "+{heal}HP";
    [JsonProperty("Min Heal To Show")] public float MinHealToShow = 1f;
        }

        private class CriticalConf
        {
            [JsonProperty("Enabled")] public bool Enabled = true;
            [JsonProperty("Chance (0.03 = 3%)")] public float Chance = 0.03f;
            [JsonProperty("Damage Multiplier On Crit")] public float Multiplier = 2.0f;
            
            [JsonProperty("Use CUI Indicator")] public bool UseCuiIndicator = true;
            [JsonProperty("CUI Text")] public string CuiText = "x2/CRITICAL";
            [JsonProperty("CUI Text Color (r g b a)")] public string CuiTextColor = "1 0 0 1";
            [JsonProperty("CUI Seconds")] public float CuiSeconds = 1.0f;
            [JsonProperty("CUI AnchorMin")] public string CuiAnchorMin = "0.45 0.57";
            [JsonProperty("CUI AnchorMax")] public string CuiAnchorMax = "0.55 0.63";
            [JsonProperty("CUI Font Size")] public int CuiFontSize = 24;
            [JsonProperty("Show GameTip On Crit")] public bool ShowGameTip = true;
            [JsonProperty("GameTip Text")] public string GameTipText = "<color=#ff3333>x2 CRITICAL</color>";
            [JsonProperty("GameTip Duration Seconds")] public float GameTipDuration = 1.0f;
        }

        public class ChatStyleConf
        {
            [JsonProperty("Kills Font Size")] public int KillsFontSize = 18;
            [JsonProperty("Other Lines Font Size")] public int OtherFontSize = 16;
            [JsonProperty("Kills Label Color (hex)")] public string KillsLabelColor = "#cd412b";
            [JsonProperty("Other Labels Color (hex)")] public string OtherLabelColor = "#00FFFF";
            [JsonProperty("Values Color (hex)")] public string ValueColor = "#FFFFFF";
        }

        public class IntegrationConf
        {
            [JsonProperty("Ignore SkillTree")] public bool IgnoreSkillTree = true;
        }

        protected override void LoadDefaultConfig()
        {
            C = new PluginConfig();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                C = Config.ReadObject<PluginConfig>() ?? new PluginConfig();
                if (C.Chat == null) C.Chat = new ChatStyleConf();
                if (C.Integration == null) C.Integration = new IntegrationConf();
                if (C.Categories == null) C.Categories = new CategoryMask();
                if (C.Magazine == null) C.Magazine = new MagazineConf();
                if (C.Health == null) C.Health = new HealthConf();
                if (C.Lifesteal == null) C.Lifesteal = new LifestealConf();
                if (C.Lifesteal.Popup == null) C.Lifesteal.Popup = new KillPopupConf();
				if (C.Critical == null) C.Critical = new CriticalConf();
                if (C.Messages == null) C.Messages = DefaultMessages();

                if (string.IsNullOrEmpty(C.HudNoPermText)) C.HudNoPermText = "EXCLUSIVO VIP 5";
                if (string.IsNullOrEmpty(C.HudNoPermHex))  C.HudNoPermHex  = "#FF3333";
                if (C.HudNoPermFontSize <= 0) C.HudNoPermFontSize = 13;
            }
            catch
            {
                PrintWarning("Falha ao ler config, recriando padrão.");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(C, true);

        #endregion

        #region Data por jogador

        private class PlayerData { public ulong SteamId; public int Kills; }

        private string DataPathFor(ulong sid)
            => Path.Combine(Interface.Oxide.DataDirectory, DATA_FOLDER, sid + ".json");

        private PlayerData LoadP(ulong sid)
        {
            try
            {
                var dir = Path.Combine(Interface.Oxide.DataDirectory, DATA_FOLDER);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var path = DataPathFor(sid);
                if (!File.Exists(path))
                    return new PlayerData { SteamId = sid, Kills = 0 };

                var json = File.ReadAllText(path);
                var p = JsonConvert.DeserializeObject<PlayerData>(json) ?? new PlayerData();
                if (p.SteamId == 0) p.SteamId = sid;
                return p;
            }
            catch (Exception e)
            {
                PrintError($"Erro LoadP {sid}: {e}");
                return new PlayerData { SteamId = sid, Kills = 0 };
            }
        }

        private void SaveP(PlayerData p)
        {
            try
            {
                var dir = Path.Combine(Interface.Oxide.DataDirectory, DATA_FOLDER);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(DataPathFor(p.SteamId), JsonConvert.SerializeObject(p, Formatting.Indented));
            }
            catch (Exception e) { PrintError($"Erro SaveP {p.SteamId}: {e}"); }
        }

        private void Init()
        {
            permission.RegisterPermission(PERM_USE, this);
            permission.RegisterPermission(PERM_GIVE, this);

            RefreshHUDAll();
            NextTick(() => { foreach (var p in BasePlayer.activePlayerList) ApplyOrRemoveHealthBuff(p); });
        }

        #endregion

        #region Lang

        private static Dictionary<string, Dictionary<string, string>> DefaultMessages()
        {
            return new Dictionary<string, Dictionary<string, string>>
            {
                ["pt-BR"] = new Dictionary<string, string>
                {
                    ["NoPerm"]     = "Sem permissão.",
                    ["GaveSelf"]   = "Você recebeu: {0}.",
                    ["GaveTo"]     = "Entregou {0} para {1}.",
                    ["NotFound"]   = "Jogador não encontrado.",
                    ["ConsoleOK"]  = "OK.",
                    ["LblKills"]    = "Kills:",
                    ["LblBonus"]    = "Bônus:",
                    ["NeedM249"]    = "Equipe uma M249 primeiro."
                },
                ["en-US"] = new Dictionary<string, string>
                {
                    ["NoPerm"]     = "No permission.",
                    ["GaveSelf"]   = "You received: {0}.",
                    ["GaveTo"]     = "Gave {0} to {1}.",
                    ["NotFound"]   = "Player not found.",
                    ["ConsoleOK"]  = "OK.",
                    ["LblKills"]    = "Kills:",
                    ["LblBonus"]    = "Bonus:",
                    ["NeedM249"]    = "Equip an M249 first."
                }
            };
        }

        private string L(string key, BasePlayer p = null, params object[] args)
        {
            var langKey = "pt-BR";
            var dict = DefaultMessages()["pt-BR"];
            var fmt = dict.ContainsKey(key) ? dict[key] : key;
            return (args == null || args.Length == 0) ? fmt : string.Format(fmt, args);
        }

        #endregion

        #region HUD

        private void ShowHUD(BasePlayer player)
        {
            DestroyHUD(player);

            var active = player?.GetActiveItem();
            if (active == null || !IsOurItem(active)) return;

            bool hasUse = permission.UserHasPermission(player.UserIDString, PERM_USE);
            if (!hasUse)
            {
                var uiNo = new CuiElementContainer();

                uiNo.Add(new CuiPanel
                {
                    Image = { Color = C.HudPanelColor },
                    RectTransform = { AnchorMin = C.HudAnchorMin, AnchorMax = C.HudAnchorMax },
                    CursorEnabled = false
                }, "Under", UI_ROOT);

                uiNo.Add(new CuiPanel
                {
                    Image = { Color = C.HudLineColor },
                    RectTransform = { AnchorMin = "0.02 0.10", AnchorMax = "0.98 0.90" }
                }, UI_ROOT, UI_BAR_BG);

                var s = Mathf.Clamp(C.HudNoPermFontSize, 8, 30);
                var msg = string.IsNullOrEmpty(C.HudNoPermText) ? "EXCLUSIVO VIP 5" : C.HudNoPermText;
                var hex = string.IsNullOrEmpty(C.HudNoPermHex) ? "#FF3333" : C.HudNoPermHex;

                uiNo.Add(new CuiLabel
                {
                    Text =
                    {
                        Text = $"<size={s}><color={hex}>{msg}</color></size>",
                        FontSize = s,
                        Color = C.HudFontColor,
                        Align = TextAnchor.MiddleCenter
                    },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                }, UI_BAR_BG);

                CuiHelper.AddUi(player, uiNo);
                return;
            }

            var pd   = LoadP(player.userID);
            var mult = CalcTotalMultiplier(pd.Kills);

            var ui = new CuiElementContainer();

            ui.Add(new CuiPanel
            {
                Image = { Color = C.HudPanelColor },
                RectTransform = { AnchorMin = C.HudAnchorMin, AnchorMax = C.HudAnchorMax },
                CursorEnabled = false
            }, "Under", UI_ROOT);

            ui.Add(new CuiPanel
            {
                Image = { Color = C.HudLineColor },
                RectTransform = { AnchorMin = "0.02 0.10", AnchorMax = "0.98 0.90" }
            }, UI_ROOT, UI_BAR_BG);

            var fontSize = Mathf.Clamp(C.HudFontSize, 8, 30);

            var leftPanel = "M249HUD_Left";
            ui.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.02 0.10", AnchorMax = "0.49 0.90" }
            }, UI_BAR_BG, leftPanel);

            var killsText = $"<size={fontSize}><color={C.HudLabelHex}>{L("LblKills", player)}</color> <color={C.HudValueHex}>{pd.Kills}</color></size>";
            ui.Add(new CuiLabel
            {
                Text = { Text = killsText, FontSize = fontSize, Color = C.HudFontColor, Align = TextAnchor.MiddleLeft },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, leftPanel);

            ui.Add(new CuiLabel
            {
                Text = { Text = "/", FontSize = fontSize, Color = C.HudFontColor, Align = TextAnchor.MiddleCenter },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, UI_BAR_BG);

            var rightPanel = "M249HUD_Right";
            ui.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.51 0.10", AnchorMax = "0.98 0.90" }
            }, UI_BAR_BG, rightPanel);

            var bonusText = $"<size={fontSize}><color={C.HudLabelHex}>{L("LblBonus", player)}</color> <color={C.HudValueHex}>x{mult:0.00}</color></size>";
            ui.Add(new CuiLabel
            {
                Text = { Text = bonusText, FontSize = fontSize, Color = C.HudFontColor, Align = TextAnchor.MiddleRight },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, rightPanel);

            CuiHelper.AddUi(player, ui);
        }

        private void DestroyHUD(BasePlayer player) => CuiHelper.DestroyUi(player, UI_ROOT);

        private void RefreshHUDAll()
        {
            foreach (var p in BasePlayer.activePlayerList)
            {
                if (p == null || !p.IsConnected) continue;
                if (p.GetActiveItem() != null && IsOurItem(p.GetActiveItem()))
                    ShowHUD(p);
                else
                    DestroyHUD(p);
            }
        }

        #endregion

        #region Hooks & Timers

        private readonly Dictionary<ulong, int> _lastAmmoByUid = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, int> _lastCapByUid = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, Timer> _restoreTimers = new Dictionary<ulong, Timer>();

        private readonly Dictionary<ulong, float> _baseMaxHealth = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, float> _healWindowStart = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, float> _healWindowAmount = new Dictionary<ulong, float>();

        private readonly Dictionary<ulong, Timer> _regenTimers = new Dictionary<ulong, Timer>();

        private void ScheduleRestoreAmmo(BasePlayer owner, Item weapon)
        {
            if (weapon == null) return;
            ulong uid = weapon.uid.Value;

            if (_restoreTimers.TryGetValue(uid, out var t) && t != null && !t.Destroyed)
                t.Destroy();

            _restoreTimers[uid] = timer.Repeat(0.05f, 4, () =>
            {
                TryRestoreAmmo(owner, weapon);
                CacheAmmo(weapon);
            });
        }

        private 
void Unload()
        {
            foreach (var kv in _restoreTimers)
                if (kv.Value != null && !kv.Value.Destroyed) kv.Value.Destroy();
            _restoreTimers.Clear();

            foreach (var kv in _regenTimers)
                if (kv.Value != null && !kv.Value.Destroyed) kv.Value.Destroy();
            _regenTimers.Clear();

            _baseMaxHealth.Clear();
            _healWindowStart.Clear();
            _healWindowAmount.Clear();

            foreach (var p in BasePlayer.activePlayerList)
            {
                StopRegen(p.userID);
                DestroyHUD(p);
            }
        }

void OnPlayerConnected(BasePlayer player)
{
    if (player == null) return;
    timer.Once(1f, () =>
    {
        ApplyOrRemoveHealthBuff(player);
        RefreshRegen(player);
        if (IsWeaponEquipped(player))
            ShowHUD(player);
    });
}


        private 
void OnActiveItemChanged(BasePlayer player, Item oldItem, Item newItem)
        {
            DestroyHUD(player);

            NextTick(() =>
            {
                ApplyOrRemoveHealthBuff(player);
                RefreshRegen(player);
            });

            if (newItem != null && IsOurItem(newItem))
            {
                NextTick(() =>
                {
                    CacheAmmo(newItem);
                    EnsureMagazineLimits(player, newItem);
                    ShowHUD(player);
                    ApplyOrRemoveHealthBuff(player);
                    RefreshRegen(player);
                });
            }
        }

        private void OnWeaponReload(BaseProjectile weapon, BasePlayer player)
        {
            try
            {
                var item = weapon?.GetItem();
                if (player == null || item == null || !IsOurItem(item)) return;
                NextTick(() => { CacheAmmo(item); EnsureMagazineLimits(player, item); ShowHUD(player); });
            }
            catch { }
        }

        private void OnWeaponFired(BaseProjectile proj, BasePlayer player, ItemModProjectile mod, Projectile projectile)
        {
            try
            {
                var item = proj?.GetItem();
                if (player == null || item == null || !IsOurItem(item)) return;
                CacheAmmo(item);
                NextTick(() => { EnsureMagazineLimits(player, item); ShowHUD(player); });
            }
            catch { }
        }

        private void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            try
            {
                if (container?.parent == null) return;
                var parentItem = container.parent as Item;
                if (parentItem == null || parentItem.info?.shortname != SHORTNAME) return;

                var owner = parentItem.GetOwnerPlayer();
                if (owner == null) return;

                ScheduleRestoreAmmo(owner, parentItem);
            }
            catch { }
        }

        private void OnItemRemovedFromContainer(ItemContainer container, Item item)
        {
            try
            {
                if (container?.parent == null) return;
                var parentItem = container.parent as Item;
                if (parentItem == null || parentItem.info?.shortname != SHORTNAME) return;

                var owner = parentItem.GetOwnerPlayer();
                if (owner == null) return;

                ScheduleRestoreAmmo(owner, parentItem);
            }
            catch { }
        }

        private void OnEntityDeath(BaseCombatEntity victim, HitInfo info)
        {
            try
            {
                if (victim == null || info == null || info.InitiatorPlayer == null) return;

                var attacker = info.InitiatorPlayer;
                var weapItem = info.Weapon?.GetItem() ?? attacker.GetActiveItem();
                if (!IsOurItem(weapItem)) return;
                if (!IsLivingEntity(victim)) return;

                if (!permission.UserHasPermission(attacker.UserIDString, PERM_USE)) return;

                var pd = LoadP(attacker.userID);
                pd.Kills += 1;
                SaveP(pd);

                CacheAmmo(weapItem);
                if (attacker.GetActiveItem() == weapItem)
                    NextTick(() => { EnsureMagazineLimits(attacker, weapItem); ShowHUD(attacker); ApplyOrRemoveHealthBuff(attacker); });
            }
            catch (Exception e) { PrintError($"OnEntityDeath erro: {e}"); }
        }
		
        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            try
            {
                if (entity == null || info == null || info.InitiatorPlayer == null) return;

                var attacker = info.InitiatorPlayer;
                var item = info.Weapon?.GetItem();
                if (!IsOurItem(item)) return;

                if (C.RequireUsePerm && !permission.UserHasPermission(attacker.UserIDString, PERM_USE))
                    return;

                if (!ShouldAffectTarget(entity))
                    return;

                if (C.IgnoreExplosiveAmmo && IsExplosiveRifleAmmo(info))
                    return;

                var pd = LoadP(attacker.userID);
                var mult = CalcTotalMultiplier(pd.Kills);

                float critMult;
                bool isCrit = RollCritical(out critMult);

                if (Math.Abs(mult - 1f) > 0.001f)
                    info.damageTypes.ScaleAll(mult);

                if (isCrit && Math.Abs(critMult - 1f) > 0.001f)
                    info.damageTypes.ScaleAll(critMult);

                var dealt = info.damageTypes.Total();
                TryApplyLifesteal(attacker, entity, dealt);

                if (isCrit) ShowCritTip(attacker);
            }
            catch (Exception e) { PrintError($"OnEntityTakeDamage erro: {e}"); }
        }

        private void OnLoseCondition(Item item, ref float amount)
        {
            if (!C.Indestructible) return;
            if (IsOurItem(item)) amount = 0f;
        }

        #endregion

        private 
void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            CuiHelper.DestroyUi(player, UI_CRIT);
            CuiHelper.DestroyUi(player, $"{Name}_healpopup");
            StopRegen(player.userID);
            RemoveHealthBuff(player);
            _healWindowStart.Remove(player.userID);
            _healWindowAmount.Remove(player.userID);
        }

        private void OnPlayerRespawned(BasePlayer player)
        {
            NextTick(() => ApplyOrRemoveHealthBuff(player));
        }


        #region SkillTree Integration (ignore)

        private bool ShouldIgnoreST(Item item)
            => (C?.Integration?.IgnoreSkillTree ?? false) && IsOurItem(item);

        object SkillTree_ShouldIgnore(BasePlayer player, Item item) => ShouldIgnoreST(item) ? (object)true : null;
        object ST_ShouldIgnore(BasePlayer player, Item item)       => ShouldIgnoreST(item) ? (object)true : null;
        object SkillTree_CanAffect(BasePlayer player, Item item)   => ShouldIgnoreST(item) ? (object)false : null;
        object CanSkillTreeAffectItem(BasePlayer player, Item item)=> ShouldIgnoreST(item) ? (object)false : null;

        #endregion

        #region Helpers Core

        private bool IsOurItem(Item item)
        {
            if (item == null) return false;
            if (item.info?.shortname != SHORTNAME) return false;

            bool skinOk = (C.WeaponSkin <= 0) || (item.skin == C.WeaponSkin);
            bool nameOk = string.IsNullOrEmpty(C.WeaponName) || ((item.name ?? string.Empty).IndexOf(C.WeaponName, StringComparison.OrdinalIgnoreCase) >= 0);

            return skinOk || nameOk;
        }

        private bool IsLivingEntity(BaseCombatEntity ent)
        {
            if (ent == null) return false;

            if (ent is BasePlayer) return true;
            if (ent is NPCPlayer || ent is BaseNpc || ent is BaseAnimalNPC) return true;

            var n = (ent.ShortPrefabName ?? string.Empty).ToLowerInvariant();
            if (ent is BaseHelicopter || n.Contains("patrolhelicopter") || n.Contains("ch47")) return true;
            if (ent is BradleyAPC) return true;

            if (n.Contains("murderer") || n.Contains("scarecrow") || n.Contains("zombie") || n.Contains("scientist"))
                return true;

            return false;
        }

		private bool ShouldAffectTarget(BaseCombatEntity ent)
		{
			if (ent == null) return false;

			if (ent is BasePlayer bp) return bp.IsNpc ? C.Categories.NPCs : C.Categories.Players;

			if (ent.GetComponent("Rust.Ai.Gen2.FSMComponent") != null) return C.Categories.NPCs;

			if (ent is NPCPlayer || ent is BaseNpc) return C.Categories.NPCs;
			if (ent is BaseAnimalNPC) return C.Categories.Animals;

			var n = (ent.ShortPrefabName ?? string.Empty).ToLowerInvariant();
			if (ent is BaseHelicopter || n.Contains("patrolhelicopter") || n.Contains("ch47")) return C.Categories.Helicopters;
			if (ent is BradleyAPC) return C.Categories.Bradley;

			if (n.Contains("autoturret") || n.Contains("sentry")) return C.Categories.Sentry;
			if (n.Contains("guntrap") || n.Contains("shotguntrap") || n.Contains("flameturret")) return C.Categories.Traps;

			return false;
		}

        private bool IsExplosiveRifleAmmo(HitInfo info)
        {
            try
            {
                var heldEnt = info.Weapon?.GetItem()?.GetHeldEntity() as BaseProjectile;
                var ammo = heldEnt != null ? heldEnt.primaryMagazine?.ammoType : null;
                return ammo != null && ammo.shortname == "ammo.rifle.explosive";
            }
            catch { return false; }
        }

        private float CalcTotalMultiplier(int kills)
        {
            var m = 1f + kills * Mathf.Max(0f, C.PerKillBonus);
            if (C.MaxTotalMultiplier > 0f) m = Mathf.Min(m, C.MaxTotalMultiplier);
            return m;
        }

        private int CalcExtraMag(int kills)
        {
            if (!C.Magazine.Enabled || C.Magazine.KillsPerLevel <= 0 || C.Magazine.BonusPerLevel <= 0) return 0;
            var levels = kills / C.Magazine.KillsPerLevel;
            var extra = levels * C.Magazine.BonusPerLevel;
            return Mathf.Clamp(extra, 0, Mathf.Max(0, C.Magazine.MaxExtra));
        }

        private int CalcLevels(int kills, int killsPerLevel)
        {
            if (killsPerLevel <= 0) return 0;
            return Mathf.Max(0, kills / killsPerLevel);
        }

        private float CalcExtraMaxHealth(int kills)
        {
            if (C.Health == null || !C.Health.Enabled) return 0f;
            var levels = CalcLevels(kills, C.Health.KillsPerLevel);
            var extra = levels * Mathf.Max(0f, C.Health.BonusMaxHealthPerLevel);
            return Mathf.Clamp(extra, 0f, Mathf.Max(0f, C.Health.MaxExtraMaxHealth));
        }

        private bool IsWeaponEquipped(BasePlayer player)
        {
            var active = player?.GetActiveItem();
            return active != null && IsOurItem(active);
        }

        private void ApplyOrRemoveHealthBuff(BasePlayer player)
        {
            try
            {
                if (player == null || !player.IsConnected) return;
                if (C?.Health == null || !C.Health.Enabled) { RemoveHealthBuff(player); return; }

                if (C.Health.OnlyWhileEquipped && !IsWeaponEquipped(player))
                {
                    RemoveHealthBuff(player);
                    return;
                }

                if (C.RequireUsePerm && !permission.UserHasPermission(player.UserIDString, PERM_USE))
                {
                    RemoveHealthBuff(player);
                    return;
                }

                var pd = LoadP(player.userID);
                var extra = CalcExtraMaxHealth(pd.Kills);
                if (extra <= 0.001f)
                {
                    RemoveHealthBuff(player);
                    return;
                }

                float baseMax;
                if (!_baseMaxHealth.TryGetValue(player.userID, out baseMax))
                {
                    baseMax = C.Health.UsePlayerCurrentMaxAsBase ? GetPlayerMaxHealthSafe(player) : Mathf.Max(1f, C.Health.BaseMaxHealth);
                    _baseMaxHealth[player.userID] = baseMax;
                }

                var targetMax = baseMax + extra;
                SetPlayerMaxHealthSafe(player, targetMax);
            }
            catch { }
        }

        private void RefreshRegen(BasePlayer player)
        {
            if (player == null) return;

            bool shouldRun = IsWeaponEquipped(player);

            if (C != null && C.RequireUsePerm && shouldRun)
                shouldRun = permission.UserHasPermission(player.UserIDString, PERM_USE);

            if (!shouldRun)
            {
                StopRegen(player.userID);
                return;
            }

            StartRegen(player);
        }

        private void StartRegen(BasePlayer player)
        {
            if (player == null) return;

            if (_regenTimers.TryGetValue(player.userID, out var t) && t != null && !t.Destroyed)
                return;

            _regenTimers[player.userID] = timer.Every(1f, () => RegenTick(player));
        }

        private void StopRegen(ulong userId)
        {
            if (_regenTimers.TryGetValue(userId, out var t) && t != null && !t.Destroyed)
                t.Destroy();
            _regenTimers.Remove(userId);
        }

        private void RegenTick(BasePlayer player)
        {
            try
            {
                if (player == null || !player.IsConnected || player.IsDead())
                {
                    if (player != null) StopRegen(player.userID);
                    return;
                }

                if (!IsWeaponEquipped(player))
                {
                    StopRegen(player.userID);
                    return;
                }

                if (C != null && C.RequireUsePerm && !permission.UserHasPermission(player.UserIDString, PERM_USE))
                {
                    StopRegen(player.userID);
                    return;
                }

                const float perSec = 10f;

                if (player.health < player.MaxHealth())
                    player.Heal(perSec);

                var m = player.metabolism;
                if (m != null)
                {
                    // Fome
                    m.calories.value = Mathf.Min(m.calories.max, m.calories.value + perSec);
                    // Sede
                    m.hydration.value = Mathf.Min(m.hydration.max, m.hydration.value + perSec);
                    m.SendChangesToClient();
                }
            }
            catch { }
        }

        private void RemoveHealthBuff(BasePlayer player)
        {
            try
            {
                if (player == null) return;
                if (_baseMaxHealth.TryGetValue(player.userID, out var baseMax))
                {
                    SetPlayerMaxHealthSafe(player, baseMax);
                    _baseMaxHealth.Remove(player.userID);
                }
            }
            catch { }
        }

        private float GetPlayerMaxHealthSafe(BasePlayer player)
        {
            try
            {
                return player.MaxHealth();
            }
            catch
            {
                return Mathf.Max(1f, C?.Health?.BaseMaxHealth ?? 100f);
            }
        }

		private static float GetMaxHealthWithModifier(BasePlayer player)
		{
			try
			{
				return player._maxHealth * (float)(1.0 + (player.modifiers != null ? (double)player.modifiers.GetValue(Modifier.ModifierType.Max_Health) : 0.0));
			}
			catch { return player._maxHealth; }
		}

		private void SetPlayerMaxHealthSafe(BasePlayer player, float max)
		{
			try
			{
				if (player == null) return;

				max = Mathf.Max(1f, max);

				if (Math.Abs(player._maxHealth - max) <= 0.001f)
				{
					if (player.maxHealthOverride > 0f)
						player.maxHealthOverride = GetMaxHealthWithModifier(player);
					player.metabolism.pending_health.max = max;
					player.SendNetworkUpdate();
					return;
				}

				player._maxHealth = max;
				player.maxHealthOverride = GetMaxHealthWithModifier(player);
				player.metabolism.pending_health.max = max;

				if (player._health > player.maxHealthOverride)
					player._health = player.maxHealthOverride;

				player.SendNetworkUpdate();
			}
			catch { }
		}

				private bool ShouldLifestealFrom(BaseCombatEntity ent)
				{
					if (ent == null || C?.Lifesteal == null) return false;
					if (!C.Lifesteal.Enabled) return false;

					if (ent is BasePlayer bp) return bp.IsNpc ? C.Lifesteal.NPCs : C.Lifesteal.Players;
					if (ent is NPCPlayer || ent is BaseNpc) return C.Lifesteal.NPCs;
					if (ent is BaseAnimalNPC) return C.Lifesteal.Animals;

					var n = (ent.ShortPrefabName ?? string.Empty).ToLowerInvariant();
					if (ent is BaseHelicopter || n.Contains("patrolhelicopter") || n.Contains("ch47")) return C.Lifesteal.Helicopters;
					if (ent is BradleyAPC) return C.Lifesteal.Bradley;

					return false;
				}

				private void TryApplyLifesteal(BasePlayer attacker, BaseCombatEntity target, float dealtDamage)
				{
					try
					{
						if (attacker == null || target == null) return;
						if (C?.Lifesteal == null || !C.Lifesteal.Enabled) return;
						if (!ShouldLifestealFrom(target)) return;

						var pct = Mathf.Clamp(C.Lifesteal.Percent, 0f, 1f);
						if (pct <= 0.0001f) return;

						var heal = dealtDamage * pct;
						if (heal <= 0.001f) return;

						heal = Mathf.Min(heal, Mathf.Max(0f, C.Lifesteal.MaxHealPerHit));

						var now = Time.realtimeSinceStartup;
						if (!_healWindowStart.TryGetValue(attacker.userID, out var start) || now - start >= 1f)
						{
							_healWindowStart[attacker.userID] = now;
							_healWindowAmount[attacker.userID] = 0f;
						}

						var used = _healWindowAmount.TryGetValue(attacker.userID, out var u) ? u : 0f;
						var cap = Mathf.Max(0f, C.Lifesteal.MaxHealPerSecond);
						if (cap > 0f)
						{
							var allowed = cap - used;
							if (allowed <= 0f) return;
							heal = Mathf.Min(heal, allowed);
						}

						if (heal <= 0.001f) return;

						attacker.Heal(heal);
						ShowHealPopup(attacker, heal);
						_healWindowAmount[attacker.userID] = used + heal;
					}
					catch { }
				}


		private void ShowHealPopup(BasePlayer p, float healAmount)
		{
			try
			{
				if (p == null || p.IsDestroyed) return;
				var K = C?.Lifesteal?.Popup;
				if (K == null || !K.Enabled) return;

				if (healAmount < Mathf.Max(0f, K.MinHealToShow)) return;

				string id = $"{Name}_healpopup";
				CuiHelper.DestroyUi(p, id);

				var ui = new CuiElementContainer();
				var panel = new CuiPanel
				{
					Image = { Color = "0 0 0 0" },
					RectTransform = { AnchorMin = K.AnchorMin, AnchorMax = K.AnchorMax },
					CursorEnabled = false
				};
				ui.Add(panel, "Overlay", id);

				var t = Mathf.Clamp01(healAmount / 20f);
				int fsize = Mathf.RoundToInt(Mathf.Lerp(K.BaseFontSize, K.DynamicSizeAt20, t));
				string text = (K.TextTemplate ?? "+{heal}HP").Replace("{heal}", $"{healAmount:0.#}");

				string[] shifts = { "-0.002 0", "0.002 0", "0 -0.004", "0 0.004" };
				foreach (var s in shifts)
				{
					ui.Add(new CuiLabel
					{
						Text = { Text = text, FontSize = fsize, Color = "0 0 0 1", Align = TextAnchor.MiddleCenter },
						RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = s, OffsetMax = s }
					}, id);
				}

				ui.Add(new CuiLabel
				{
					Text = { Text = text, FontSize = fsize, Color = K.ColorRGBA, Align = TextAnchor.MiddleCenter },
					RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
				}, id);

				CuiHelper.AddUi(p, ui);
				timer.Once(Mathf.Max(0.1f, K.Seconds), () => { if (p != null && !p.IsDestroyed) CuiHelper.DestroyUi(p, id); });
			}
			catch { }
		}

			private bool RollCritical(out float critMult)
			{
				critMult = 1f;
				try
				{
					if (C?.Critical == null || !C.Critical.Enabled) return false;
					var ch = Mathf.Clamp(C.Critical.Chance, 0f, 1f);
					if (ch <= 0.0001f) return false;

					if (UnityEngine.Random.value <= ch)
					{
						critMult = Mathf.Max(1f, C.Critical.Multiplier);
						return critMult > 1.0001f;
					}
				}
			catch { }
			return false;
		}

				
		private const string UI_CRIT = "M249_CRIT";

		private void ShowCritTip(BasePlayer attacker)
		{
			try
			{
				if (attacker == null || attacker.IsDestroyed) return;
				if (C?.Critical == null || !C.Critical.Enabled) return;

				if (C.Critical.UseCuiIndicator)
				{
					CuiHelper.DestroyUi(attacker, UI_CRIT);

					var elements = new CuiElementContainer();
					var panel = new CuiPanel
					{
						CursorEnabled = false,
						Image = { Color = "0 0 0 0" },
						RectTransform = { AnchorMin = C.Critical.CuiAnchorMin, AnchorMax = C.Critical.CuiAnchorMax },
						FadeOut = Mathf.Clamp(C.Critical.CuiSeconds, 0.2f, 5f) * 0.8f
					};
					elements.Add(panel, "Hud", UI_CRIT);

					elements.Add(new CuiLabel
					{
						Text =
						{
							Text = string.IsNullOrEmpty(C.Critical.CuiText) ? "x2/CRITICAL" : C.Critical.CuiText,
							FontSize = Mathf.Clamp(C.Critical.CuiFontSize, 10, 60),
							Align = TextAnchor.MiddleCenter,
							Color = string.IsNullOrEmpty(C.Critical.CuiTextColor) ? "1 0 0 1" : C.Critical.CuiTextColor
						},
						RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
					}, UI_CRIT);

					CuiHelper.AddUi(attacker, elements);

					timer.Once(Mathf.Clamp(C.Critical.CuiSeconds, 0.2f, 5f), () =>
					{
						if (attacker != null && !attacker.IsDestroyed) CuiHelper.DestroyUi(attacker, UI_CRIT);
					});
					return;
				}

				if (!C.Critical.ShowGameTip) return;

				var txt = string.IsNullOrEmpty(C.Critical.GameTipText) ? "<color=#ff3333>x2 CRITICAL</color>" : C.Critical.GameTipText;
				var dur = Mathf.Clamp(C.Critical.GameTipDuration, 0.2f, 5f);

				attacker.SendConsoleCommand("gametip.hidegametip");
				attacker.SendConsoleCommand("gametip.showgametip", txt);
				timer.Once(dur, () =>
				{
					if (attacker != null && attacker.IsConnected)
						attacker.SendConsoleCommand("gametip.hidegametip");
				});
			}
			catch { }
		}

        private void CacheAmmo(Item item)
        {
            try
            {
                if (item == null) return;
                var proj = item.GetHeldEntity() as BaseProjectile;
                if (proj?.primaryMagazine == null) return;
                ulong uid = item.uid.Value;
                _lastAmmoByUid[uid] = proj.primaryMagazine.contents;
                _lastCapByUid[uid] = proj.primaryMagazine.capacity;
            }
            catch { }
        }

        private void TryRestoreAmmo(BasePlayer player, Item item)
        {
            try
            {
                var proj = item?.GetHeldEntity() as BaseProjectile;
                if (proj?.primaryMagazine == null) return;
                ulong uid = item.uid.Value;

                int prevAmmo = _lastAmmoByUid.ContainsKey(uid) ? _lastAmmoByUid[uid] : -1;
                int prevCap  = _lastCapByUid.ContainsKey(uid) ? _lastCapByUid[uid] : -1;

                if (prevAmmo > 0 && prevCap > 0 && proj.primaryMagazine.capacity == prevCap && proj.primaryMagazine.contents == 0)
                {
                    proj.primaryMagazine.contents = Mathf.Min(prevAmmo, proj.primaryMagazine.capacity);
                    proj.SendNetworkUpdateImmediate();
                }
            }
            catch { }
        }

        private void EnsureMagazineLimits(BasePlayer player, Item item)
        {
            try
            {
                if (player == null || item == null || !IsOurItem(item)) return;

                var proj = item.GetHeldEntity() as BaseProjectile;
                if (proj?.primaryMagazine == null) return;

                var baseCap = Mathf.Max(1, C.Magazine.BaseCapacity);
                int extra = 0;
                if (permission.UserHasPermission(player.UserIDString, PERM_USE))
                {
                    var pd = LoadP(player.userID);
                    extra = CalcExtraMag(pd.Kills);
                }
                var cap = baseCap + extra;

                int before = proj.primaryMagazine.contents;
                int beforeCap = proj.primaryMagazine.capacity;
                ulong uid = item.uid.Value;
                int cached = _lastAmmoByUid.ContainsKey(uid) ? _lastAmmoByUid[uid] : -1;

                if (proj.primaryMagazine.capacity != cap)
                    proj.primaryMagazine.capacity = cap;

                if (proj.primaryMagazine.contents > proj.primaryMagazine.capacity)
                    proj.primaryMagazine.contents = proj.primaryMagazine.capacity;

                if (beforeCap == proj.primaryMagazine.capacity)
                    proj.primaryMagazine.contents = before;

                if (proj.primaryMagazine.contents == 0)
                {
                    if (before > 0)
                        proj.primaryMagazine.contents = Mathf.Min(before, proj.primaryMagazine.capacity);
                    else if (cached > 0)
                        proj.primaryMagazine.contents = Mathf.Min(cached, proj.primaryMagazine.capacity);
                }
                proj.SendNetworkUpdateImmediate();
                CacheAmmo(item);
                ScheduleRestoreAmmo(player, item);
            }
            catch (Exception e) { PrintError($"EnsureMagazineLimits erro: {e}"); }
        }

        private Item CreateOurWeapon()
        {
            var def = ItemManager.FindItemDefinition(SHORTNAME);
            if (def == null) return null;

            var item = ItemManager.Create(def, 1, (ulong)(C.WeaponSkin > 0 ? C.WeaponSkin : 0));
            if (item == null) return null;

            if (!string.IsNullOrEmpty(C.WeaponName))
                item.name = C.WeaponName;

            var proj = item.GetHeldEntity() as BaseProjectile;
            if (proj?.primaryMagazine != null)
            {
                proj.primaryMagazine.capacity = Mathf.Max(1, C.Magazine.BaseCapacity);
                proj.SendNetworkUpdateImmediate();
            }

            if (C.Indestructible)
            {
                item.condition = item.maxCondition;
                item.MarkDirty();
            }

            CacheAmmo(item);
            return item;
        }

        #endregion

        #region Chat Command (apenas GIVE, exige permissão)

        [ChatCommand(CHAT_CMD_GIVE)]
        private void Chat_Highlander(BasePlayer p, string cmd, string[] args)
        {
            if (!permission.UserHasPermission(p.UserIDString, PERM_GIVE))
            {
                p.ChatMessage(L("NoPerm", p));
                return;
            }

            var item = CreateOurWeapon(); if (item == null) return;
            if (!p.inventory.GiveItem(item))
                item.Drop(p.transform.position + p.transform.forward * 0.5f, Vector3.up);

            NextTick(() =>
            {
                EnsureMagazineLimits(p, item);
                ShowHUD(p);
            });
            p.ChatMessage(string.Format(L("GaveSelf", p), C.WeaponName));
        }

        #endregion

        #region Console-only (admin)

        private void ReplyBoth(ConsoleSystem.Arg arg, string msg)
        {
            try
            {
                if (arg != null && arg.Connection != null)
                {
                    var bp = arg.Player();
                    if (bp != null) bp.ConsoleMessage(msg);
                }
            }
            catch { }
            Puts($"[M249AutoUp] {msg}");
        }
		
        private bool HasConsoleAuth(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.Connection == null) return true;
            var p = arg.Player();
            return p != null && p.IsAdmin;
        }

        private bool TryResolveSteamIdStr(string s, out ulong sid)
        {
            sid = 0UL;
            if (string.IsNullOrEmpty(s)) return false;

            if (ulong.TryParse(s, out sid)) return true;

            var digits = Regex.Replace(s, @"[^\d]", "");
            if (digits.Length >= 15 && ulong.TryParse(digits, out sid)) return true;

            var bp = FindBasePlayer(s);
            if (bp != null) { sid = bp.userID; return true; }

            return false;
        }
		
		private 
void OnPlayerDeath(BasePlayer player, HitInfo info)
		{
			if (player == null) return;
            StopRegen(player.userID);
			DestroyHUD(player);
		}

        [ConsoleCommand(CON_PREFIX + "addkills")]
        private void CmdC_AddKills(ConsoleSystem.Arg arg)
        {
            if (!HasConsoleAuth(arg)) { ReplyBoth(arg, "Sem permissão (apenas console/admin)."); return; }
            var a = arg.Args;
            if (a == null || a.Length < 2) { ReplyBoth(arg, "uso: m249autoup.addkills <steamid|nome> <amount>"); return; }

            if (!TryResolveSteamIdStr(a[0], out var sid)) { ReplyBoth(arg, "steamid/nome inválido"); return; }
            if (!int.TryParse(a[1], out var amt)) { ReplyBoth(arg, "amount inválido"); return; }

            var p = LoadP(sid);
            p.Kills = Mathf.Max(0, p.Kills + amt);
            SaveP(p);

            var bp = BasePlayer.FindByID(sid);
            if (bp != null && bp.GetActiveItem() != null && IsOurItem(bp.GetActiveItem()))
                NextTick(() => { EnsureMagazineLimits(bp, bp.GetActiveItem()); ShowHUD(bp); });

            ReplyBoth(arg, "OK.");
        }

        [ConsoleCommand(CON_PREFIX + "setkills")]
        private void CmdC_SetKills(ConsoleSystem.Arg arg)
        {
            if (!HasConsoleAuth(arg)) { ReplyBoth(arg, "Sem permissão (apenas console/admin)."); return; }
            var a = arg.Args;
            if (a == null || a.Length < 2) { ReplyBoth(arg, "uso: m249autoup.setkills <steamid|nome> <value>"); return; }

            if (!TryResolveSteamIdStr(a[0], out var sid)) { ReplyBoth(arg, "steamid/nome inválido"); return; }
            if (!int.TryParse(a[1], out var val)) { ReplyBoth(arg, "value inválido"); return; }

            var p = LoadP(sid);
            p.Kills = Mathf.Max(0, val);
            SaveP(p);

            var bp = BasePlayer.FindByID(sid);
            if (bp != null && bp.GetActiveItem() != null && IsOurItem(bp.GetActiveItem()))
                NextTick(() => { EnsureMagazineLimits(bp, bp.GetActiveItem()); ShowHUD(bp); });

            ReplyBoth(arg, "OK.");
        }

        [ConsoleCommand(CON_PREFIX + "resetkills")]
        private void CmdC_ResetKills(ConsoleSystem.Arg arg)
        {
            if (!HasConsoleAuth(arg)) { ReplyBoth(arg, "Sem permissão (apenas console/admin)."); return; }
            var a = arg.Args;
            if (a == null || a.Length < 1) { ReplyBoth(arg, "uso: m249autoup.resetkills <steamid|nome>"); return; }

            if (!TryResolveSteamIdStr(a[0], out var sid)) { ReplyBoth(arg, "steamid/nome inválido"); return; }

            var p = LoadP(sid); p.Kills = 0; SaveP(p);

            var bp = BasePlayer.FindByID(sid);
            if (bp != null && bp.GetActiveItem() != null && IsOurItem(bp.GetActiveItem()))
                NextTick(() => { EnsureMagazineLimits(bp, bp.GetActiveItem()); ShowHUD(bp); });

            ReplyBoth(arg, "OK.");
        }

        [ConsoleCommand(CON_PREFIX + "getkills")]
        private void CmdC_GetKills(ConsoleSystem.Arg arg)
        {
            if (!HasConsoleAuth(arg)) { ReplyBoth(arg, "Sem permissão (apenas console/admin)."); return; }
            var a = arg.Args;
            if (a == null || a.Length < 1) { ReplyBoth(arg, "uso: m249autoup.getkills <steamid|nome>"); return; }

            if (!TryResolveSteamIdStr(a[0], out var sid)) { ReplyBoth(arg, "steamid/nome inválido"); return; }

            var p = LoadP(sid);
            var mult = CalcTotalMultiplier(p.Kills);

            ReplyBoth(arg, $"kills={p.Kills} multiplier=x{mult:0.00}");
        }

        [ConsoleCommand(CON_PREFIX + "give")]
        private void CmdC_Give(ConsoleSystem.Arg arg)
        {
            if (!HasConsoleAuth(arg)) { ReplyBoth(arg, "Sem permissão (apenas console/admin)."); return; }
            var a = arg.Args;
            if (a == null || a.Length < 1) { ReplyBoth(arg, "uso: m249autoup.give <steamid|nome>"); return; }

            BasePlayer target = null;
            if (!TryResolveSteamIdStr(a[0], out var sid) || (target = BasePlayer.FindByID(sid)) == null)
            {
                target = FindBasePlayer(a[0]);
                if (target == null) { ReplyBoth(arg, "Jogador não encontrado."); return; }
            }

            var item = CreateOurWeapon();
            if (item == null) { ReplyBoth(arg, "Falha ao criar item."); return; }

            if (!target.inventory.GiveItem(item))
                item.Drop(target.transform.position + target.transform.forward * 0.5f, Vector3.up);

            NextTick(() => { EnsureMagazineLimits(target, item); ShowHUD(target); });
            ReplyBoth(arg, $"Entregue {C.WeaponName} para {target.displayName}.");
        }

        #endregion

        #region Utils

        private BasePlayer FindBasePlayer(string arg)
        {
            if (ulong.TryParse(arg, out var sid))
            {
                var by = BasePlayer.FindByID(sid) ?? BasePlayer.FindSleeping(sid);
                if (by != null) return by;
            }

            foreach (var p in BasePlayer.activePlayerList)
                if (p.displayName.IndexOf(arg, StringComparison.OrdinalIgnoreCase) >= 0)
                    return p;

            return null;
        }

        #endregion
    }
}
