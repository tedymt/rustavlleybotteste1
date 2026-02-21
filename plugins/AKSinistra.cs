using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using Rust;
using Random = UnityEngine.Random;

namespace Oxide.Plugins
{
    [Info("AKSinistra", "Mestre Pardal", "2.8.0")]
    [Description("AK personalizada com progressão por categoria, Deployables (portas, janelas, baús, traps, sentrys etc.), bônus CRITICAL, RCON upgrades e proteção de magazine/condição. + Botão IL para abrir AKSinistraHUD quando equipada.")]
    public class AKSinistra : CovalencePlugin
    {
        #region References

        [PluginReference] private Plugin Economics;
        [PluginReference] private Plugin ImageLibrary;

        #endregion

        #region Config

        private ConfigData config;

        public class PerCategoryCost
        {
            [JsonProperty(PropertyName = "Cost base per level (1..10, 11..20, etc.)")]
            public int CostBasePerLevel = 100;

            [JsonProperty(PropertyName = "Cost step added every 10 levels (e.g., +50)")]
            public int CostStepEvery10 = 50;

            [JsonProperty(PropertyName = "Max level (0 = unlimited)")]
            public int MaxLevel = 0;
        }

        public class ConfigData
        {
            // === Geral ===
            [JsonProperty(PropertyName = "SilentChat (no player chat messages)")]
            public bool SilentChat = true;

            [JsonProperty(PropertyName = "Permission required to use (/aksinistra)")]
            public bool RequirePermission = true;

            [JsonProperty(PropertyName = "Permission name (self)")]
            public string PermissionNameUse = "aksinistra.use";

            [JsonProperty(PropertyName = "Permission name (give to others)")]
            public string PermissionNameGive = "aksinistra.give";

            [JsonProperty(PropertyName = "Weapon shortname (keep 'rifle.ak')")]
            public string WeaponShortname = "rifle.ak";

            [JsonProperty(PropertyName = "Custom display name")]
            public string DisplayName = "AKSinistra";

            [JsonProperty(PropertyName = "Skin ID (0 = default)")]
            public ulong SkinId = 3555523603;

            [JsonProperty(PropertyName = "Indestructible weapon (no condition loss)")]
            public bool Indestructible = true;

            [JsonProperty(PropertyName = "Require BOTH Name and Skin to qualify")]
            public bool RequireBothNameAndSkin = false;

            [JsonProperty(PropertyName = "Magazine capacity FINAL override (0 = compute from vanilla + upgrades)")]
            public int MagazineCapacity = 0;

            [JsonProperty(PropertyName = "RepeatDelay scale (lower = mais RPM). 1.0 = default")]
            public float RepeatDelayScale = 1.0f;

            [JsonProperty(PropertyName = "ReloadTime scale (lower = recarrega mais rápido). 1.0 = default")]
            public float ReloadTimeScale = 1.0f;

            [JsonProperty(PropertyName = "DeployDelay scale (lower = saca mais rápido). 1.0 = default")]
            public float DeployDelayScale = 1.0f;

            [JsonProperty(PropertyName = "ProjectileVelocityScale (1.0 = padrão da munição)")]
            public float ProjectileVelocityScale = 1.0f;

            [JsonProperty(PropertyName = "AimCone (precisão ADS). -1 = não alterar")]
            public float AimCone = -1f;
            
			[JsonProperty(PropertyName = "HipAimCone (precisão hipfire). -1 = não alterar")]
            public float HipAimCone = -1f;
            
			[JsonProperty(PropertyName = "AimconePenaltyPerShot (-1 = não alterar)")]
            public float AimconePenaltyPerShot = -1f;
            
			[JsonProperty(PropertyName = "AimconePenaltyRecoverTime (-1 = não alterar)")]
            public float AimconePenaltyRecoverTime = -1f;
            
			[JsonProperty(PropertyName = "AimconePenaltyMax (-1 = não alterar)")]
            public float AimconePenaltyMax = -1f;
            
			[JsonProperty(PropertyName = "Global damage multiplier (aplicado antes dos específicos)")]
            public float GlobalDamageMultiplier = 1.0f;
            
			[JsonProperty(PropertyName = "Headshot multiplier extra (players/NPCs). 1.0 = sem mudança")]
            public float HeadshotBonusMultiplier = 1.0f;

            public TargetMultipliers Multipliers = new TargetMultipliers();
            public class TargetMultipliers
            {
                [JsonProperty(PropertyName = "Damage vs Players")] public float Players = 1.0f;
                [JsonProperty(PropertyName = "Damage vs NPCs (scientists/animals)")] public float Npcs = 1.0f;
                [JsonProperty(PropertyName = "Damage vs Patrol Helicopter")] public float Helicopter = 1.0f;
                [JsonProperty(PropertyName = "Damage vs Bradley APC")] public float Bradley = 1.0f;
                [JsonProperty(PropertyName = "Damage vs Buildings - Twig")] public float BuildingTwig = 1.0f;
                [JsonProperty(PropertyName = "Damage vs Buildings - Wood")] public float BuildingWood = 1.0f;
                [JsonProperty(PropertyName = "Damage vs Buildings - Stone")] public float BuildingStone = 1.0f;
                [JsonProperty(PropertyName = "Damage vs Buildings - Metal")] public float BuildingMetal = 1.0f;
                [JsonProperty(PropertyName = "Damage vs Buildings - Armored (HQM)")] public float BuildingArmored = 1.0f;
                [JsonProperty(PropertyName = "Damage vs Deployables (doors, windows, boxes, traps, sentries, ladders, cupboards etc.)")]
                public float Deployables = 1.0f;
            }

            [JsonProperty(PropertyName = "Fill magazine on equip (qualquer equip)")]
            public bool FillMagazineOnEquip = false;
            [JsonProperty(PropertyName = "Fill magazine only on FIRST equip")]
            public bool FillMagazineOnlyOnFirstEquip = true;
            [JsonProperty(PropertyName = "Debug logs")]
            public bool Debug = false;

            [JsonProperty(PropertyName = "Per-level bonus percent (ex.: 0.05 = +5% por nível)")]
            public float PerLevelBonusPercent = 0.05f;
            [JsonProperty(PropertyName = "Per-level magazine add (balas por nível)")]
            public int PerLevelMagazineAdd = 1;
            [JsonProperty(PropertyName = "Max magazine capacity (hard cap, 0 = sem limite)")]
            public int MaxMagazineCapacity = 0;

            [JsonProperty(PropertyName = "Costs per Category (same base/step pattern but per item)")]
            public Dictionary<string, PerCategoryCost> Costs = new Dictionary<string, PerCategoryCost>
            {
                ["Players"]         = new PerCategoryCost { CostBasePerLevel = 120, CostStepEvery10 = 60 },
                ["Npcs"]            = new PerCategoryCost { CostBasePerLevel = 110, CostStepEvery10 = 55 },
                ["Helicopter"]      = new PerCategoryCost { CostBasePerLevel = 250, CostStepEvery10 = 100 },
                ["Bradley"]         = new PerCategoryCost { CostBasePerLevel = 250, CostStepEvery10 = 100 },
                ["BuildingTwig"]    = new PerCategoryCost { CostBasePerLevel =  60, CostStepEvery10 = 30 },
                ["BuildingWood"]    = new PerCategoryCost { CostBasePerLevel =  80, CostStepEvery10 = 40 },
                ["BuildingStone"]   = new PerCategoryCost { CostBasePerLevel = 120, CostStepEvery10 = 60 },
                ["BuildingMetal"]   = new PerCategoryCost { CostBasePerLevel = 150, CostStepEvery10 = 75 },
                ["BuildingArmored"] = new PerCategoryCost { CostBasePerLevel = 180, CostStepEvery10 = 90 },
                ["Deployables"]     = new PerCategoryCost { CostBasePerLevel = 140, CostStepEvery10 = 70 },
                ["Magazine"]        = new PerCategoryCost { CostBasePerLevel = 200, CostStepEvery10 = 100 },
                ["Bonus"]           = new PerCategoryCost { CostBasePerLevel = 100, CostStepEvery10 = 50, MaxLevel = 100 },
            };

            [JsonProperty(PropertyName = "Bonus - Chance per level (in percent)")]
            public float BonusChancePerLevelPercent = 1.0f;
            [JsonProperty(PropertyName = "Bonus - Max chance (cap) in percent")]
            public float BonusMaxChancePercent = 50.0f;
            [JsonProperty(PropertyName = "Bonus - Duplicate damage multiplier (1.0 = +100% dano no proc)")]
            public float BonusDuplicateMultiplier = 1.0f;
            [JsonProperty(PropertyName = "Bonus - Proc UI text ('x2' ou 'CRITICAL!')")]
            public string BonusProcText = "x2";

            [JsonProperty(PropertyName = "Extended mags extra capacity (if mod equipped)")]
            public int ExtendedMagsExtra = 8;
            [JsonProperty(PropertyName = "Honor extended mags attachment")]
            public bool HonorExtendedMags = true;
            [JsonProperty(PropertyName = "IGNORE external mag plugins (ex.: SkillTree)")]
            public bool IgnoreExternalMagPlugins = true;
            [JsonProperty(PropertyName = "Capacity rechecks after EQUIP (if ignoring external)")]
            public int EnforceTicksAfterEquip = 3;
            [JsonProperty(PropertyName = "Recheck delay seconds after EQUIP")]
            public float EnforceTickDelay = 0.15f;
            [JsonProperty(PropertyName = "Capacity rechecks after RELOAD (if ignoring external)")]
            public int EnforceTicksAfterReload = 4;
            [JsonProperty(PropertyName = "Recheck delay seconds after RELOAD")]
            public float EnforceReloadDelay = 0.20f;
            [JsonProperty(PropertyName = "RCON upgrades cost Economics? (default false)")]
            public bool RconUpgradesCostEconomics = false;
            [JsonProperty(PropertyName = "Deployables - Player placeable only (OwnerID != 0)")]
            public bool DeployablesPlayerPlaceableOnly = true;
            [JsonProperty(PropertyName = "Deployables - Extra CONTAINS (shortprefab minúsculo)")]
            public List<string> DeployablesExtraContains = new List<string>();
            [JsonProperty(PropertyName = "Deployables - Extra EXACT (shortprefab exato, minúsculo)")]
            public List<string> DeployablesExtraExact = new List<string>();
            [JsonProperty(PropertyName = "Deployables - Blacklist CONTAINS")]
            public List<string> DeployablesBlacklistContains = new List<string>();
            [JsonProperty(PropertyName = "Deployables - Blacklist EXACT")]
            public List<string> DeployablesBlacklistExact = new List<string>();
            [JsonProperty(PropertyName = "OutgoingDamageOnly (apply bonus only on outgoing attacks)")]
            public bool OutgoingDamageOnly = true;
            [JsonProperty(PropertyName = "Deployables - Ignore OwnerID for Turrets (Auto/Flame/SAM)")]
            public bool DeployablesIgnoreOwnerForTurrets = true;
        }

        protected override void LoadDefaultConfig()
        {
            config = new ConfigData();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<ConfigData>();
                if (config == null) throw new Exception();
            }
            catch
            {
                PrintError("Config inválida, recriando padrão.");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(config, true);

        #endregion

        #region State / Data

        private const string DataFolder = "AKSinistra";

        private readonly HashSet<ItemId> _magazineFilledOnce = new HashSet<ItemId>();
        private readonly Dictionary<ulong, PlayerProgress> _progressCache = new Dictionary<ulong, PlayerProgress>();

        public class PlayerProgress
        {
            public int Players = 0;
            public int Npcs = 0;
            public int Helicopter = 0;
            public int Bradley = 0;
            public int BuildingTwig = 0;
            public int BuildingWood = 0;
            public int BuildingStone = 0;
            public int BuildingMetal = 0;
            public int BuildingArmored = 0;
            public int Deployables = 0;

            public int Magazine = 0;
            public int Bonus = 0; // chance global
        }

        private string GetPlayerFile(ulong id) =>
            Path.Combine(Interface.Oxide.DataDirectory, DataFolder, $"{id}.json");

        private PlayerProgress GetProgress(ulong id)
        {
            if (_progressCache.TryGetValue(id, out var p)) return p;

            var folder = Path.Combine(Interface.Oxide.DataDirectory, DataFolder);
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            var file = GetPlayerFile(id);
            if (File.Exists(file))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    p = JsonConvert.DeserializeObject<PlayerProgress>(json) ?? new PlayerProgress();
                }
                catch
                {
                    p = new PlayerProgress();
                }
            }
            else
            {
                p = new PlayerProgress();
            }
            _progressCache[id] = p;
            return p;
        }

        private void SaveProgress(ulong id)
        {
            if (!_progressCache.TryGetValue(id, out var p)) return;

            var folder = Path.Combine(Interface.Oxide.DataDirectory, DataFolder);
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            var file = GetPlayerFile(id);
            File.WriteAllText(file, JsonConvert.SerializeObject(p, Formatting.Indented));
        }

        #endregion

        #region Setup / Commands

        private void Init()
        {
            if (!string.IsNullOrEmpty(config.PermissionNameUse))
                permission.RegisterPermission(config.PermissionNameUse, this);
            if (!string.IsNullOrEmpty(config.PermissionNameGive))
                permission.RegisterPermission(config.PermissionNameGive, this);

            AddCovalenceCommand("aksinistra", nameof(CmdAksinistra));
            AddCovalenceCommand("aksinistra.give", nameof(CmdConsoleGiveOther));
            AddCovalenceCommand("aksinistra.upgrade", nameof(CmdRconUpgradeCategory));
            AddCovalenceCommand("aksinistra.upgradeall", nameof(CmdRconUpgradeAll));
            AddCovalenceCommand("aksinistra.setlevel", nameof(CmdRconSetLevel));
            AddCovalenceCommand("aksinistra.stats", nameof(CmdRconStats));
            AddCovalenceCommand("aksinistra.fixmag", nameof(CmdFixMag));
            AddCovalenceCommand("aksinistra.hud", nameof(CmdOpenHud));

            ImageLibrary?.Call("AddImage", AKS_BTN_URL, AKS_BTN_KEY, 0UL);
        }

        #endregion

        #region Commands

        private void CmdOpenHud(IPlayer iPlayer, string command, string[] args)
        {
            if (!iPlayer.IsConnected) return;
            var bp = iPlayer.Object as BasePlayer;
            if (bp == null) return;
            bp.SendConsoleCommand("aksinistrahud");
        }

        private void CmdAksinistra(IPlayer iPlayer, string command, string[] args)
        {
            if (!iPlayer.IsConnected) return;
            var bp = iPlayer.Object as BasePlayer;
            if (bp == null) return;

            if (args.Length == 0)
            {
                if (config.RequirePermission && !iPlayer.HasPermission(config.PermissionNameGive))
                {
                    if (!config.SilentChat)
                        iPlayer.Reply("Você não tem permissão para pegar a AKSinistra. (perm necessária: " + config.PermissionNameGive + ")");
                    return;
                }

                var item = CreateSinistraItem();
                if (item == null) return;
                if (!bp.inventory.GiveItem(item)) item.Drop(bp.transform.position + Vector3.up, Vector3.zero);
                return;
            }

            var sub = args[0].ToLowerInvariant();
            if (sub == "upgrade")
            {
                if (args.Length < 2) return;
                var cat = args[1];
                int qty = 1;
                if (args.Length >= 3) int.TryParse(args[2], out qty);
                qty = Mathf.Clamp(qty, 1, 100);
                TryUpgradeCategory(iPlayer, cat, qty);
                return;
            }

            TryUpgradeCategory(iPlayer, sub, 1);
        }

        private bool EnsureConsoleOrAdmin(IPlayer iPlayer)
        {
            return iPlayer.IsServer || iPlayer.IsAdmin || iPlayer.HasPermission(config.PermissionNameGive);
        }

        private void CmdConsoleGiveOther(IPlayer iPlayer, string command, string[] args)
        {
            if (!EnsureConsoleOrAdmin(iPlayer)) return;
            if (args.Length < 1) { iPlayer.Reply($"Uso: {command} <player>"); return; }
            var target = FindPlayer(args[0]);
            if (target == null) { iPlayer.Reply("Jogador não encontrado."); return; }
            var bp = target.Object as BasePlayer;
            if (bp == null) { iPlayer.Reply("Player inválido."); return; }

            var item = CreateSinistraItem();
            if (item == null) { iPlayer.Reply("Falha ao criar item."); return; }
            if (!bp.inventory.GiveItem(item)) item.Drop(bp.transform.position + Vector3.up, Vector3.zero);
            iPlayer.Reply($"Dada AKSinistra para {target.Name}.");
        }

        private void CmdFixMag(IPlayer iPlayer, string command, string[] args)
        {
            if (!iPlayer.IsConnected) return;
            var bp = iPlayer.Object as BasePlayer;
            if (bp == null) return;
            var item = bp.GetActiveItem();
            if (item == null || !IsSinistra(item)) { iPlayer.Reply("Equipe a AKSinistra primeiro."); return; }
            var held = item.GetHeldEntity() as BaseProjectile;
            if (held == null) { iPlayer.Reply("Arma inválida."); return; }

            if (config.IgnoreExternalMagPlugins)
                EnforceOurMagazineStrict(bp, held, item, clampContents: true);
            else
                ApplyConfigMagazine(bp, item, held, firstPass: false, fromCommand: true);

            EnsureIndestructible(item);
            iPlayer.Reply("Magazine/Condição re-sincronizados.");
        }

        private void CmdRconStats(IPlayer iPlayer, string command, string[] args)
        {
            if (!EnsureConsoleOrAdmin(iPlayer)) return;
            if (args.Length < 1) { iPlayer.Reply($"Uso: {command} <player|steamid>"); return; }

            if (!TryResolveUserId(args[0], out var userId, out var name, out var bp))
            {
                iPlayer.Reply("Jogador não encontrado.");
                return;
            }

            var pr = GetProgress(userId);
            var per = config.PerLevelBonusPercent;
            iPlayer.Reply(
                $"Stats {name}: P={pr.Players}({FormatPct(pr.Players*per)}), N={pr.Npcs}({FormatPct(pr.Npcs*per)}), Heli={pr.Helicopter}, Brad={pr.Bradley}, " +
                $"Twig={pr.BuildingTwig}, Wood={pr.BuildingWood}, Stone={pr.BuildingStone}, Metal={pr.BuildingMetal}, Armored={pr.BuildingArmored}, Deploy={pr.Deployables}, " +
                $"Mag={pr.Magazine}(+{pr.Magazine*config.PerLevelMagazineAdd}), Bonus={Mathf.Min(pr.Bonus*config.BonusChancePerLevelPercent, config.BonusMaxChancePercent):0.#}%"
            );
        }

        private void CmdRconUpgradeCategory(IPlayer iPlayer, string command, string[] args)
        {
            if (!EnsureConsoleOrAdmin(iPlayer)) return;
            if (args.Length < 2) { iPlayer.Reply($"Uso: {command} <player|steamid> <categoria> [levels=1] [charge=true/false]"); return; }

            if (!TryResolveUserId(args[0], out var userId, out var name, out var bp))
            {
                iPlayer.Reply("Jogador não encontrado.");
                return;
            }

            if (!ResolveCategory(args[1], out var canonical)) { iPlayer.Reply("Categoria inválida."); return; }
            int qty = 1; if (args.Length >= 3) int.TryParse(args[2], out qty);
            bool charge = config.RconUpgradesCostEconomics; if (args.Length >= 4) bool.TryParse(args[3], out charge);

            bool ok;
            int newLevel, totalCost; string bonusMsg, error;

            if (bp != null)
                ok = DoUpgradeCategory(bp, canonical, Mathf.Clamp(qty, 1, 100), charge, out newLevel, out totalCost, out bonusMsg, out error);
            else
                ok = DoUpgradeCategory(userId, canonical, Mathf.Clamp(qty, 1, 100), charge, out newLevel, out totalCost, out bonusMsg, out error);

            if (ok)
                iPlayer.Reply($"{name}: {canonical} => nível {newLevel}. Custo total: {totalCost}. [{bonusMsg}]");
            else
                iPlayer.Reply($"Falhou: {error}");
        }

        private void CmdRconUpgradeAll(IPlayer iPlayer, string command, string[] args)
        {
            if (!EnsureConsoleOrAdmin(iPlayer)) return;
            if (args.Length < 1) { iPlayer.Reply($"Uso: {command} <player|steamid> [levels=1] [charge=true/false]"); return; }

            if (!TryResolveUserId(args[0], out var userId, out var name, out var bp))
            {
                iPlayer.Reply("Jogador não encontrado.");
                return;
            }

            int qty = 1; if (args.Length >= 2) int.TryParse(args[1], out qty);
            bool charge = config.RconUpgradesCostEconomics; if (args.Length >= 3) bool.TryParse(args[2], out charge);

            var cats = GetAllCategories();
            int grandCost = 0;
            var msgs = new List<string>();

            foreach (var cat in cats)
            {
                bool ok;
                int newLevel, cost;
                string bonusMsg, err;

                if (bp != null)
                    ok = DoUpgradeCategory(bp, cat, Mathf.Clamp(qty, 1, 100), charge, out newLevel, out cost, out bonusMsg, out err);
                else
                    ok = DoUpgradeCategory(userId, cat, Mathf.Clamp(qty, 1, 100), charge, out newLevel, out cost, out bonusMsg, out err);

                if (ok)
                {
                    grandCost += cost;
                    msgs.Add($"{cat}->{newLevel}");
                }
                else msgs.Add($"{cat}: {err}");
            }

            iPlayer.Reply($"{name}: upgrade ALL (+{qty}) ok. Novos níveis: {string.Join(", ", msgs)}. Custo total: {grandCost}.");
        }

        private void CmdRconSetLevel(IPlayer iPlayer, string command, string[] args)
        {
            if (!EnsureConsoleOrAdmin(iPlayer)) return;
            if (args.Length < 3) { iPlayer.Reply($"Uso: {command} <player|steamid> <categoria> <level>"); return; }

            if (!TryResolveUserId(args[0], out var userId, out var name, out var bp))
            {
                iPlayer.Reply("Jogador não encontrado.");
                return;
            }

            if (!ResolveCategory(args[1], out var canonical)) { iPlayer.Reply("Categoria inválida."); return; }
            if (!int.TryParse(args[2], out var level)) { iPlayer.Reply("Level inválido."); return; }
            level = Mathf.Clamp(level, 0, 100000);

            var pr = GetProgress(userId);
            var cfg = GetCostCfg(canonical);
            if (cfg.MaxLevel > 0 && level > cfg.MaxLevel) level = cfg.MaxLevel;

            SetLevelRef(pr, canonical, level);
            SaveProgress(userId);

            string bonusMsg = FormatCategoryBonusText(canonical, level);
            iPlayer.Reply($"{name}: {canonical} SET nível {level}. [{bonusMsg}]");
        }

        private IPlayer FindPlayer(string nameOrId)
        {
            foreach (var p in players.Connected) if (p.Id == nameOrId) return p;
            var found = players.FindPlayer(nameOrId); if (found != null && found.IsConnected) return found;
            nameOrId = nameOrId.ToLowerInvariant(); IPlayer best = null;
            foreach (var p in players.Connected)
            {
                if (p.Name.ToLowerInvariant().Contains(nameOrId)) { if (best != null) return null; best = p; }
            }
            return best;
        }

        private bool TryResolveUserId(string nameOrId, out ulong userId, out string displayName, out BasePlayer basePlayer)
        {
            userId = 0;
            displayName = nameOrId;
            basePlayer = null;

            if (string.IsNullOrEmpty(nameOrId))
                return false;

            if (ulong.TryParse(nameOrId, out userId))
            {
                basePlayer = BasePlayer.FindByID(userId) ?? BasePlayer.FindSleeping(userId);
                if (basePlayer != null)
                    displayName = basePlayer.displayName;
                return true; // offline ok
            }

            var target = FindPlayer(nameOrId);
            if (target == null) return false;

            if (!ulong.TryParse(target.Id, out userId))
                return false;

            displayName = target.Name;
            basePlayer = target.Object as BasePlayer;
            return true;
        }
        #endregion

        #region IL Button (ImageLibrary)

        private const string AKS_BTN_URL = "https://i.ibb.co/TZ5YB8f/AKS.png";
        private const string AKS_BTN_KEY = "mpb_4EE49BE2";
        private const string UI_AKS_BTN = "AKS_BTN";
        private const float IL_RETRY_INTERVAL = 0.25f;
        private const int   IL_MAX_RETRIES   = 60;

        private void ShowAksButton(BasePlayer player, int attempt = 0)
        {
            if (player == null || !player.IsConnected) return;

            var isReady = (bool?)ImageLibrary?.Call("HasImage", AKS_BTN_KEY);
            if (!(isReady ?? false))
            {
                if (attempt >= IL_MAX_RETRIES) return;
                ImageLibrary?.Call("AddImage", AKS_BTN_URL, AKS_BTN_KEY, 0UL);
                timer.Once(IL_RETRY_INTERVAL, () => ShowAksButton(player, attempt + 1));
                return;
            }

            var png = ImageLibrary?.Call("GetImage", AKS_BTN_KEY) as string;
            if (string.IsNullOrEmpty(png))
            {
                if (attempt >= IL_MAX_RETRIES) return;
                timer.Once(IL_RETRY_INTERVAL, () => ShowAksButton(player, attempt + 1));
                return;
            }

            CuiHelper.DestroyUi(player, UI_AKS_BTN);
            var elements = new CuiElementContainer();

            var panel = new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.22 0.04", AnchorMax = "0.25 0.086" },
            };
            elements.Add(panel, "Hud.Menu", UI_AKS_BTN);

            elements.Add(new CuiElement
            {
                Name = UI_AKS_BTN + "_IMG",
                Parent = UI_AKS_BTN,
                Components =
                {
                    new CuiRawImageComponent { Png = png, Sprite = "assets/content/ui/ui.background.tiletex.psd" },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                }
            });

            elements.Add(new CuiButton
            {
                Button = { Command = "AKS", Color = "0 0 0 0" },
                Text   = { Text = "", Align = TextAnchor.MiddleCenter, FontSize = 12 },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, UI_AKS_BTN);

            CuiHelper.AddUi(player, elements);
        }

        private void HideAksButton(BasePlayer player)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, UI_AKS_BTN);
        }

        #endregion

        #region Item / Tuning / Magazine / Condition

        private Item CreateSinistraItem()
        {
            ItemDefinition def = ItemManager.FindItemDefinition(config.WeaponShortname);
            if (def == null)
            {
                PrintError($"ItemDefinition não encontrado: {config.WeaponShortname}");
                return null;
            }

            var item = ItemManager.Create(def, 1, config.SkinId);
            if (item == null) return null;
            if (!string.IsNullOrEmpty(config.DisplayName)) item.name = config.DisplayName;
            item.OnVirginSpawn();
            EnsureIndestructible(item);
            return item;
        }

        private void EnsureIndestructible(Item item)
        {
            if (item == null || !config.Indestructible) return;
            try
            {
                float maxC = GetItemMaxCondition(item);
                if (item.maxCondition != maxC) item.maxCondition = maxC;
                if (item.condition != maxC) item.condition = maxC;
                item.MarkDirty();
            }
            catch { }
        }

        private bool IsSinistra(Item item)
        {
            if (item == null || item.info == null) return false;
            if (item.info.shortname != config.WeaponShortname) return false;
            bool nameOk = string.IsNullOrEmpty(config.DisplayName) || item.name == config.DisplayName;
            bool skinOk = config.SkinId == 0 || item.skin == config.SkinId;
            return config.RequireBothNameAndSkin ? (nameOk && skinOk) : (nameOk || skinOk);
        }

        private bool IsSinistra(BasePlayer player) => IsSinistra(player?.GetActiveItem());

        private float GetItemMaxCondition(Item item)
        {
            if (item != null && item.maxCondition > 0f) return item.maxCondition;
            try
            {
                var info = item?.info;
                if (info != null)
                {
                    var t = info.GetType();
                    var condField = t.GetField("condition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (condField != null)
                    {
                        var condObj = condField.GetValue(info);
                        if (condObj != null)
                        {
                            var maxF = condObj.GetType().GetField("max", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (maxF != null && maxF.FieldType == typeof(float)) return (float)maxF.GetValue(condObj);
                            var maxP = condObj.GetType().GetProperty("max", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (maxP != null && maxP.PropertyType == typeof(float)) return (float)maxP.GetValue(condObj, null);
                        }
                    }

                    var mcF = t.GetField("MaxCondition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (mcF != null && mcF.FieldType == typeof(float)) return (float)mcF.GetValue(info);
                    var mcP = t.GetProperty("MaxCondition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (mcP != null && mcP.PropertyType == typeof(float)) return (float)mcP.GetValue(info, null);
                }
            }
            catch { }

            return 100f;
        }

        private bool TrySetFloat(object obj, float value, params string[] names)
        {
            if (obj == null) return false;

            var t = obj.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var n in names)
            {
                var f = t.GetField(n, flags);
                if (f != null && f.FieldType == typeof(float))
                {
                    f.SetValue(obj, value);
                    return true;
                }

                var p = t.GetProperty(n, flags);
                if (p != null && p.PropertyType == typeof(float) && p.CanWrite)
                {
                    p.SetValue(obj, value, null);
                    return true;
                }
            }
            return false;
        }

        private void OnActiveItemChanged(BasePlayer player, Item oldItem, Item newItem)
        {
            if (player == null) return;

            if (oldItem != null && IsSinistra(oldItem))
                HideAksButton(player);

            if (newItem == null) return;
            if (!IsSinistra(newItem)) return;

            ShowAksButton(player);

            EnsureIndestructible(newItem);

            NextTick(() =>
            {
                var held = newItem.GetHeldEntity() as BaseProjectile;
                if (held != null)
                {
                    TryApplyTuning(held);
                    if (config.IgnoreExternalMagPlugins)
                    {
                        EnforceOurMagazineStrict(player, held, newItem, clampContents: false);
                        ScheduleStrictEnforce(player, newItem, held, afterReload: false);
                    }
                    else
                    {
                        ApplyConfigMagazine(player, newItem, held, firstPass: true, fromCommand: false);
                        NormalizeMagazine(held);
                    }
                }
                EnsureIndestructible(newItem);
            });
        }

        private void OnWeaponReload(BaseProjectile weapon, BasePlayer player)
        {
            if (weapon == null || player == null) return;
            var item = weapon.GetItem();
            if (item == null || !IsSinistra(item)) return;

            if (config.IgnoreExternalMagPlugins)
                ScheduleStrictEnforce(player, item, weapon, afterReload: true);
            else
                NextTick(() => ApplyConfigMagazine(player, item, weapon, firstPass: false, fromCommand: false));

            NextTick(() => EnsureIndestructible(item));
        }

        private void OnWeaponFired(BaseProjectile weapon, BasePlayer player, ItemModProjectile mod, ProtoBuf.ProjectileShoot projectiles)
        {
            if (weapon == null || player == null) return;
            var item = weapon.GetItem();
            if (item == null || !IsSinistra(item) || !config.Indestructible) return;
            NextTick(() => EnsureIndestructible(item));
        }

        private void OnPlayerAttack(BasePlayer attacker, HitInfo info)
        {
            if (attacker == null) return;
            var item = attacker.GetActiveItem();
            if (!IsSinistra(item) || !config.Indestructible) return;
            NextTick(() => EnsureIndestructible(item));
        }

        private object OnLoseCondition(Item item, float amount)
        {
            if (item == null || !config.Indestructible) return null;
            if (!IsSinistra(item)) return null;
            NextTick(() => EnsureIndestructible(item));
            return true;
        }

        private void OnItemRepair(BasePlayer player, Item item)
        {
            if (item == null) return;
            if (IsSinistra(item)) NextTick(() => EnsureIndestructible(item));
        }

        #endregion

        #region Magazine helpers

        private void ScheduleStrictEnforce(BasePlayer owner, Item item, BaseProjectile bp, bool afterReload)
        {
            int n = Mathf.Clamp(afterReload ? config.EnforceTicksAfterReload : config.EnforceTicksAfterEquip, 0, 8);
            float delay = Mathf.Clamp(afterReload ? config.EnforceReloadDelay : config.EnforceTickDelay, 0.05f, 1.0f);
            for (int i = 1; i <= n; i++)
            {
                timer.Once(delay * i, () =>
                {
                    if (bp == null || bp.IsDestroyed || item == null) return;
                    EnforceOurMagazineStrict(owner, bp, item, clampContents: true);
                    EnsureIndestructible(item);
                });
            }
        }

        private int GetExtMagBonus(Item weaponItem)
        {
            if (!config.HonorExtendedMags || weaponItem == null) return 0;
            var cont = weaponItem.contents;
            if (cont == null) return 0;
            foreach (var it in cont.itemList)
            {
                if (it?.info?.shortname == "weapon.mod.extendedmags")
                    return Math.Max(0, config.ExtendedMagsExtra);
            }
            return 0;
        }

        private void EnforceOurMagazineStrict(BasePlayer owner, BaseProjectile bp, Item weaponItem, bool clampContents)
        {
            if (bp?.primaryMagazine == null) return;

            int baseCapacity;
            if (config.MagazineCapacity > 0)
            {
                baseCapacity = config.MagazineCapacity;
            }
            else
            {
                int baseVanilla = GetVanillaBaseCapacity(config.WeaponShortname);
                int addByLevel = 0;
                if (owner != null)
                {
                    var pr = GetProgress(owner.userID);
                    addByLevel = pr.Magazine * config.PerLevelMagazineAdd;
                }
                baseCapacity = baseVanilla + addByLevel;
            }

            baseCapacity += GetExtMagBonus(weaponItem);

            if (config.MaxMagazineCapacity > 0 && baseCapacity > config.MaxMagazineCapacity)
                baseCapacity = config.MaxMagazineCapacity;

            var mag = bp.primaryMagazine;
            mag.capacity = baseCapacity;

            if (clampContents)
            {
                if (mag.contents > mag.capacity) mag.contents = mag.capacity;
                if (mag.contents < 0) mag.contents = 0;
            }
            bp.SendNetworkUpdate();
        }

        private void ApplyConfigMagazine(BasePlayer owner, Item item, BaseProjectile bp, bool firstPass, bool fromCommand)
        {
            if (bp == null || bp.primaryMagazine == null) return;

            int externalCap = bp.primaryMagazine.capacity;
            int ownCap;

            if (config.MagazineCapacity > 0)
            {
                ownCap = config.MagazineCapacity;
            }
            else
            {
                int baseVanilla = GetVanillaBaseCapacity(config.WeaponShortname);
                int addByLevel = 0;
                if (owner != null)
                {
                    var pr = GetProgress(owner.userID);
                    addByLevel = pr.Magazine * config.PerLevelMagazineAdd;
                }
                ownCap = baseVanilla + addByLevel;
            }

            ownCap += GetExtMagBonus(item);
            int finalCapacity = Math.Max(ownCap, externalCap);

            if (config.MaxMagazineCapacity > 0 && finalCapacity > config.MaxMagazineCapacity)
                finalCapacity = config.MaxMagazineCapacity;

            bp.primaryMagazine.capacity = finalCapacity;

            if (firstPass && !_magazineFilledOnce.Contains(item.uid))
            {
                if (config.FillMagazineOnEquip || config.FillMagazineOnlyOnFirstEquip)
                {
                    bp.primaryMagazine.contents = Mathf.Clamp(bp.primaryMagazine.contents, 0, bp.primaryMagazine.capacity);
                }
                if (config.FillMagazineOnlyOnFirstEquip && bp.primaryMagazine.contents <= 0)
                {
                    bp.primaryMagazine.contents = bp.primaryMagazine.capacity;
                }
                _magazineFilledOnce.Add(item.uid);
            }

            bp.SendNetworkUpdate();
        }

        private void NormalizeMagazine(BaseProjectile bp)
        {
            if (bp?.primaryMagazine == null) return;
            var mag = bp.primaryMagazine;
            if (mag.contents > mag.capacity) mag.contents = mag.capacity;
            if (mag.contents < 0) mag.contents = 0;
            bp.SendNetworkUpdateImmediate();
        }

        private int GetVanillaBaseCapacity(string weaponShortname)
        {
            switch (weaponShortname)
            {
                case "rifle.ak": return 30;
                default: return 30;
            }
        }

        private void TryApplyTuning(BaseProjectile bp)
        {
            try
            {
                if (config.RepeatDelayScale > 0f)
                    bp.repeatDelay *= config.RepeatDelayScale;

                if (config.ReloadTimeScale > 0f)
                    bp.reloadTime *= config.ReloadTimeScale;

                if (config.DeployDelayScale > 0f)
                    bp.deployDelay *= config.DeployDelayScale;

                if (config.ProjectileVelocityScale > 0f)
                    bp.projectileVelocityScale = config.ProjectileVelocityScale;

                if (config.AimCone >= 0f) bp.aimCone = config.AimCone;
                if (config.HipAimCone >= 0f) bp.hipAimCone = config.HipAimCone;

                if (config.AimconePenaltyPerShot >= 0f)
                    TrySetFloat(bp, config.AimconePenaltyPerShot, "aimconePenaltyPerShot", "aimConePenaltyPerShot");

                if (config.AimconePenaltyRecoverTime >= 0f)
                    TrySetFloat(bp, config.AimconePenaltyRecoverTime, "aimconePenaltyRecoverTime", "aimconePenaltyRecoverTime");

                if (config.AimconePenaltyMax >= 0f)
                    TrySetFloat(bp, config.AimconePenaltyMax, "aimconePenaltyMax", "aimconePenaltyMax", "aimConePenaltyMax");

                bp.SendNetworkUpdate();
            }
            catch (Exception e)
            {
                PrintWarning($"Falha ao aplicar tuning: {e.Message}");
            }
        }

        #endregion

        #region Upgrade helpers

        private string[] GetAllCategories() => new string[]
        {
            "Players","Npcs","Helicopter","Bradley",
            "BuildingTwig","BuildingWood","BuildingStone","BuildingMetal","BuildingArmored",
            "Deployables","Magazine","Bonus"
        };

        private string FormatPct(float v) => $"{Mathf.RoundToInt(v * 100f)}%";

        private bool ResolveCategory(string input, out string canonical)
        {
            canonical = null;
            switch (input.ToLowerInvariant())
            {
                case "player":
                case "players": canonical = "Players"; return true;
                case "npc":
                case "npcs":
                case "animals":
                case "scientists": canonical = "Npcs"; return true;
                case "heli":
                case "helicopter":
                case "patrol": canonical = "Helicopter"; return true;
                case "brad":
                case "bradley": canonical = "Bradley"; return true;
                case "twig": canonical = "BuildingTwig"; return true;
                case "wood": canonical = "BuildingWood"; return true;
                case "stone": canonical = "BuildingStone"; return true;
                case "metal": canonical = "BuildingMetal"; return true;
                case "armored":
                case "hqm": canonical = "BuildingArmored"; return true;
                case "deploy":
                case "deployables":
                case "box":
                case "boxes":
                case "trap":
                case "traps": canonical = "Deployables"; return true;
                case "mag":
                case "magazine":
                case "ammo": canonical = "Magazine"; return true;
                case "bonus": canonical = "Bonus"; return true;
            }
            return false;
        }

        private string FormatCategoryBonusText(string canonical, int level)
        {
            if (canonical == "Magazine") return $"+{level * config.PerLevelMagazineAdd} balas";
            if (canonical == "Bonus") return $"{Mathf.Min(level * config.BonusChancePerLevelPercent, config.BonusMaxChancePercent):0.#}% de proc";
            return $"+{FormatPct(config.PerLevelBonusPercent * level)}";
        }

        private int GetLevelRef(PlayerProgress pr, string canonical)
        {
            switch (canonical)
            {
                case "Players": return pr.Players;
                case "Npcs": return pr.Npcs;
                case "Helicopter": return pr.Helicopter;
                case "Bradley": return pr.Bradley;
                case "BuildingTwig": return pr.BuildingTwig;
                case "BuildingWood": return pr.BuildingWood;
                case "BuildingStone": return pr.BuildingStone;
                case "BuildingMetal": return pr.BuildingMetal;
                case "BuildingArmored": return pr.BuildingArmored;
                case "Deployables": return pr.Deployables;
                case "Magazine": return pr.Magazine;
                case "Bonus": return pr.Bonus;
            }
            return 0;
        }

        private void SetLevelRef(PlayerProgress pr, string canonical, int value)
        {
            switch (canonical)
            {
                case "Players": pr.Players = value; break;
                case "Npcs": pr.Npcs = value; break;
                case "Helicopter": pr.Helicopter = value; break;
                case "Bradley": pr.Bradley = value; break;
                case "BuildingTwig": pr.BuildingTwig = value; break;
                case "BuildingWood": pr.BuildingWood = value; break;
                case "BuildingStone": pr.BuildingStone = value; break;
                case "BuildingMetal": pr.BuildingMetal = value; break;
                case "BuildingArmored": pr.BuildingArmored = value; break;
                case "Deployables": pr.Deployables = value; break;
                case "Magazine": pr.Magazine = value; break;
                case "Bonus": pr.Bonus = value; break;
            }
        }

        private PerCategoryCost GetCostCfg(string canonical)
        {
            if (config.Costs != null && config.Costs.TryGetValue(canonical, out var pcc))
                return pcc;
            return new PerCategoryCost { CostBasePerLevel = 100, CostStepEvery10 = 50, MaxLevel = 0 };
        }

        private int CostForLevelIndex(string canonical, int levelIndex)
        {
            var c = GetCostCfg(canonical);
            int block = (levelIndex - 1) / 10;
            return c.CostBasePerLevel + (c.CostStepEvery10 * block);
        }

        private int TotalCostForUpgrading(string canonical, int currentLevel, int addLevels)
        {
            int total = 0;
            for (int i = 1; i <= addLevels; i++)
            {
                int nextLevel = currentLevel + i;
                total += CostForLevelIndex(canonical, nextLevel);
            }
            return total;
        }

        private bool TryEconomicsWithdraw(BasePlayer bp, double amount)
        {
            if (amount <= 0) return true;
            if (Economics == null) return false;
            var ok = Economics.CallHook("Withdraw", bp.userID, amount) as bool?;
            return ok.HasValue && ok.Value;
        }


        private bool TryEconomicsWithdraw(ulong userId, double amount)
        {
            if (amount <= 0) return true;
            if (Economics == null) return false;
            var ok = Economics.CallHook("Withdraw", userId, amount) as bool?;
            return ok.HasValue && ok.Value;
        }

        private bool DoUpgradeCategory(BasePlayer bp, string canonical, int qty, bool chargeEconomics, out int newLevel, out int totalCost, out string bonusMsg, out string error)
        {
            newLevel = 0; totalCost = 0; bonusMsg = ""; error = null;
            var pr = GetProgress(bp.userID);
            var current = GetLevelRef(pr, canonical);

            var cCfg = GetCostCfg(canonical);
            int maxAllowed = cCfg.MaxLevel <= 0 ? int.MaxValue : cCfg.MaxLevel;
            if (current >= maxAllowed)
            {
                error = $"{canonical}: nível máximo ({maxAllowed}).";
                return false;
            }

            int canAdd = Math.Min(qty, maxAllowed - current);
            totalCost = TotalCostForUpgrading(canonical, current, canAdd);

            if (chargeEconomics)
            {
                if (!TryEconomicsWithdraw(bp, totalCost))
                {
                    error = "Economics: saldo insuficiente.";
                    return false;
                }
            }

            newLevel = current + canAdd;
            SetLevelRef(pr, canonical, newLevel);
            SaveProgress(bp.userID);

            bonusMsg = FormatCategoryBonusText(canonical, newLevel);
            return true;
        }

        private bool DoUpgradeCategory(ulong userId, string canonical, int qty, bool chargeEconomics, out int newLevel, out int totalCost, out string bonusMsg, out string error)
        {
            newLevel = 0; totalCost = 0; bonusMsg = ""; error = null;
            var pr = GetProgress(userId);
            var current = GetLevelRef(pr, canonical);

            var cCfg = GetCostCfg(canonical);
            int maxAllowed = cCfg.MaxLevel <= 0 ? int.MaxValue : cCfg.MaxLevel;
            if (current >= maxAllowed)
            {
                error = $"{canonical}: nível máximo ({maxAllowed}).";
                return false;
            }

            int canAdd = Math.Min(qty, maxAllowed - current);
            totalCost = TotalCostForUpgrading(canonical, current, canAdd);

            if (chargeEconomics)
            {
                if (!TryEconomicsWithdraw(userId, totalCost))
                {
                    error = "Economics: saldo insuficiente.";
                    return false;
                }
            }

            newLevel = current + canAdd;
            SetLevelRef(pr, canonical, newLevel);
            SaveProgress(userId);

            bonusMsg = FormatCategoryBonusText(canonical, newLevel);
            return true;
        }


        private void TryUpgradeCategory(IPlayer iPlayer, string categoryInput, int qty)
        {
            var bp = iPlayer.Object as BasePlayer;
            if (bp == null) return;
            if (!ResolveCategory(categoryInput, out var canonical)) return;
            DoUpgradeCategory(bp, canonical, qty, chargeEconomics: true, out _, out _, out _, out _);
        }

        #endregion

        #region Damage + PROC UI

        private static bool MatchesAnyContains(string value, List<string> patterns)
        {
            if (string.IsNullOrEmpty(value) || patterns == null || patterns.Count == 0) return false;
            for (int i = 0; i < patterns.Count; i++)
            {
                var p = patterns[i];
                if (!string.IsNullOrEmpty(p) && value.Contains(p)) return true;
            }
            return false;
        }

        private static bool MatchesAnyExact(string value, List<string> patterns)
        {
            if (string.IsNullOrEmpty(value) || patterns == null || patterns.Count == 0) return false;
            for (int i = 0; i < patterns.Count; i++)
            {
                var p = patterns[i];
                if (!string.IsNullOrEmpty(p) && value == p) return true;
            }
            return false;
        }

        private bool TryGetSinistraOwner(HitInfo info, out BasePlayer owner)
        {
            owner = null;
            if (info == null) return false;

            bool IsForbiddenExplosive(string spnRaw)
            {
                if (string.IsNullOrEmpty(spnRaw)) return false;
                var spn = spnRaw.ToLowerInvariant();
                if (spn.Contains("rocket")) return true;
                if (spn.Contains("explosive.timed")) return true;
                if (spn.Contains("surveycharge")) return true;
                if (spn.Contains("grenade.f1")) return true;
                if (spn.Contains("grenade.beancan")) return true;
                if (spn.Contains("satchel")) return true;
                if (spn.Contains("molotov")) return true;
                return false;
            }

            var player = info.InitiatorPlayer;
            if (player != null)
            {
                string src = string.Empty;

                if (info.WeaponPrefab != null)
                    src = info.WeaponPrefab.ShortPrefabName ?? string.Empty;
                else if (info.Weapon != null)
                    src = info.Weapon.ShortPrefabName ?? string.Empty;
                else if (info.Initiator != null)
                    src = info.Initiator.ShortPrefabName ?? string.Empty;

                if (!IsForbiddenExplosive(src) && IsSinistra(player))
                {
                    owner = player;
                    return true;
                }
            }

            var initiator = info.Initiator;
            if (initiator != null && initiator.OwnerID != 0)
            {
                string src = initiator.ShortPrefabName ?? string.Empty;
                if (!IsForbiddenExplosive(src))
                {
                    var maybe = BasePlayer.FindByID(initiator.OwnerID) ?? BasePlayer.FindSleeping(initiator.OwnerID);
                    if (maybe != null && IsSinistra(maybe))
                    {
                        owner = maybe;
                        return true;
                    }
                }
            }

            return false;
        }
        
		private bool IsNpcTarget(BaseCombatEntity entity)
        {
            if (entity == null) return false;

            if (entity is BasePlayer bp) return bp.IsNpc;
            if (entity is BaseNpc) return true;

            try
            {
                if (entity.GetComponent("Rust.Ai.Gen2.FSMComponent") != null)
                    return true;
            }
            catch
            {
                // ignore
            }

            var tn = entity.GetType().Name;
            if (tn.Contains("NPC2") || tn.Contains("ScientistNPC2") || tn.Contains("BaseNPC2"))
                return true;

            return false;
        }

        private bool IsOutgoingFrom(BasePlayer attacker, HitInfo info)
        {
            if (attacker == null || info == null) return false;

            if (info.InitiatorPlayer == attacker)
                return true;

            var init = info.Initiator;
            if (init != null && init.OwnerID == attacker.userID)
            {
                string n = (init.ShortPrefabName ?? string.Empty).ToLowerInvariant();
                if (init is BaseHelicopter || init is BradleyAPC || init is AutoTurret || init is FlameTurret || init is SamSite)
                    return false;
                if (n.Contains("patrolhelicopter") || n.Contains("helirocket") || n.Contains("helicannon") || n.Contains("napalm") || n.Contains("fireball"))
                    return false;
                return true;
            }

            if (info.Weapon is BaseProjectile wpn && wpn.OwnerID == attacker.userID)
                return true;

            return false;
        }

        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null) return;
            if (!TryGetSinistraOwner(info, out var attacker)) return;

            if (config.OutgoingDamageOnly && !IsOutgoingFrom(attacker, info))
                return;

            if (Math.Abs(config.GlobalDamageMultiplier - 1f) > 0.001f)
                info.damageTypes.ScaleAll(config.GlobalDamageMultiplier);

            float baseMult = 1f; string cat = null;
            if (entity is BasePlayer bpT && !bpT.IsNpc) { baseMult = config.Multipliers.Players; cat = "Players"; }
            else if (IsNpcTarget(entity)) { baseMult = config.Multipliers.Npcs; cat = "Npcs"; }
            else if (entity is BradleyAPC) { baseMult = config.Multipliers.Bradley; cat = "Bradley"; }
            else if (IsPatrolHeli(entity)) { baseMult = config.Multipliers.Helicopter; cat = "Helicopter"; }
            else if (entity is BuildingBlock bb)
            {
                switch (bb.grade)
                {
                    case BuildingGrade.Enum.Twigs: baseMult = config.Multipliers.BuildingTwig; cat = "BuildingTwig"; break;
                    case BuildingGrade.Enum.Wood: baseMult = config.Multipliers.BuildingWood; cat = "BuildingWood"; break;
                    case BuildingGrade.Enum.Stone: baseMult = config.Multipliers.BuildingStone; cat = "BuildingStone"; break;
                    case BuildingGrade.Enum.Metal: baseMult = config.Multipliers.BuildingMetal; cat = "BuildingMetal"; break;
                    case BuildingGrade.Enum.TopTier: baseMult = config.Multipliers.BuildingArmored; cat = "BuildingArmored"; break;
                }
            }
			else if (entity.ShortPrefabName.Contains("wall.external") || entity.ShortPrefabName.Contains("external")) { baseMult = config.Multipliers.BuildingStone; cat = "BuildingStone"; }
            else if (IsDeployableCategory(entity)) { baseMult = config.Multipliers.Deployables; cat = "Deployables"; }

            float mult = baseMult;
            if (!string.IsNullOrEmpty(cat))
            {
                var pr = GetProgress(attacker.userID);
                int lvl = GetLevelRef(pr, cat);
                if (lvl > 0 && Math.Abs(config.PerLevelBonusPercent) > 0.0001f)
                    mult *= (1f + (lvl * config.PerLevelBonusPercent));
            }

            if (Math.Abs(mult - 1f) > 0.001f) info.damageTypes.ScaleAll(mult);

            if (((entity is BasePlayer) || (entity is BaseNpc)) && info.isHeadshot && Math.Abs(config.HeadshotBonusMultiplier - 1f) > 0.001f)
                info.damageTypes.ScaleAll(config.HeadshotBonusMultiplier);

            var prog = GetProgress(attacker.userID);
            float chance = Mathf.Min(prog.Bonus * config.BonusChancePerLevelPercent, config.BonusMaxChancePercent);
            if (chance > 0f && Random.Range(0f, 100f) <= chance)
            {
                info.damageTypes.ScaleAll(1f + Mathf.Max(0f, config.BonusDuplicateMultiplier));
                ShowProcUI(attacker, string.IsNullOrEmpty(config.BonusProcText) ? "x2" : config.BonusProcText);
            }
        }

        private bool IsPatrolHeli(BaseCombatEntity e)
        {
            var name = e?.ShortPrefabName ?? string.Empty;
            return e is BaseHelicopter || name.Contains("patrolhelicopter");
        }

        private bool IsDeployableCategory(BaseCombatEntity entity)
        {
            if (entity == null) return false;

            string spn = (entity.ShortPrefabName ?? string.Empty).ToLowerInvariant();

            if (MatchesAnyExact(spn, config.DeployablesBlacklistExact) ||
                MatchesAnyContains(spn, config.DeployablesBlacklistContains))
                return false;

            if (spn.Contains("crate") || spn.Contains("barrel") || spn.Contains("supply_drop"))
                return false;

            if (entity is AutoTurret || entity is FlameTurret || entity is SamSite)
            {
                if (config.DeployablesIgnoreOwnerForTurrets || entity.OwnerID != 0)
                    return true;
            }

            if (entity is BaseTrap) return true;
            if (entity is BuildingPrivlidge) return true;
            if (entity is Door) return true;

            if (spn.Contains("door") || spn.Contains("shutter") || spn.Contains("window")) return true;
            if (spn.Contains("ladder")) return true;
            if (spn == "woodbox_deployed" || spn.Contains("box.wooden.large")) return true;

            if (MatchesAnyExact(spn, config.DeployablesExtraExact) ||
                MatchesAnyContains(spn, config.DeployablesExtraContains))
                return true;

            if (config.DeployablesPlayerPlaceableOnly && entity.OwnerID == 0)
                return false;

            if (entity is DecayEntity && !(entity is BuildingBlock))
                return true;

            return false;
        }

        private const string UI_PROC = "AKS_PROC";
        private void ShowProcUI(BasePlayer player, string text)
        {
            if (player == null || player.IsDestroyed) return;
            CuiHelper.DestroyUi(player, UI_PROC);
            var elements = new CuiElementContainer();
            var panel = new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.45 0.57", AnchorMax = "0.55 0.63" },
                FadeOut = 0.8f
            };
            elements.Add(panel, "Hud", UI_PROC);
            elements.Add(new CuiLabel
            {
                Text = { Text = string.IsNullOrEmpty(text) ? "x2" : text, FontSize = 24, Align = TextAnchor.MiddleCenter, Color = "1 0 0 1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, UI_PROC);
            CuiHelper.AddUi(player, elements);
            timer.Once(1f, () =>
            {
                if (player != null && !player.IsDestroyed)
                    CuiHelper.DestroyUi(player, UI_PROC);
            });
        }

        #endregion

        #region Cleanup hooks

        private void OnPlayerDisconnected(BasePlayer p) => HideAksButton(p);
        private void OnPlayerRespawned(BasePlayer p)
        {
            if (!IsSinistra(p)) HideAksButton(p);
        }

        #endregion
    }
}
