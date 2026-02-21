using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("AKSinistraHUD", "Mestre Pardal", "1.6.6")]
    [Description("HUD da AKSinistra com ImageLibrary espelhado do MPButtons (hash CRC32, IsReady wait, retry estendido e fallback opcional).")]
    public class AKSinistraHUD : CovalencePlugin
    {
        private const string UiRoot = "AKS_UI_ROOT";
        private const string UiBg   = "AKS_UI_BG";
        private const string PermUse = "aksinistrahud.use";
        private const string AkDataFolder = "AKSinistra";

        private const int IL_MAX_TRIES = 60;
        private const float IL_RETRY_SECONDS = 0.25f;

        // NOVO: watcher de readiness do ImageLibrary (até ~2 minutos = 480 * 0.25s)
        private const int IL_READY_MAX_TRIES = 480;

        private ConfigData config;

        [PluginReference] private Plugin ImageLibrary;

        private bool _ilReady;

        private class AkPerCategoryCost
        {
            [JsonProperty(PropertyName = "Cost base per level (1..10, 11..20, etc.)")]
            public int CostBasePerLevel = 100;

            [JsonProperty(PropertyName = "Cost step added every 10 levels (e.g., +50)")]
            public int CostStepEvery10 = 50;

            [JsonProperty(PropertyName = "Max level (0 = unlimited)")]
            public int MaxLevel = 0;
        }

        private class AkConfigMirror
        {
            [JsonProperty(PropertyName = "Costs per Category (same base/step pattern but per item)")]
            public Dictionary<string, AkPerCategoryCost> Costs = new Dictionary<string, AkPerCategoryCost>();

            [JsonProperty(PropertyName = "Per-level bonus percent (ex.: 0.05 = +5% por nível)")]
            public float? BonusPercentPT;

            [JsonProperty(PropertyName = "Per-level bonus percent (0.05 = +5%/lvl)")]
            public float? BonusPercentEN;

            [JsonProperty(PropertyName = "Per-level magazine add (balas por nível)")]
            public int? MagAddPT;

            [JsonProperty(PropertyName = "Per-level magazine add (bullets per level)")]
            public int? MagAddEN;
        }

        private AkConfigMirror _akCfg;

        public class ConfigData
        {
            [JsonProperty(PropertyName = "Require permission (aksinistrahud.use) to use /aks")]
            public bool RequirePermission = true;

            [JsonProperty(PropertyName = "Use centered size mode")]
            public bool UseCenteredSize = true;

            [JsonProperty(PropertyName = "HUD Width (0..1)")]
            public float HudWidth = 0.70f;

            [JsonProperty(PropertyName = "HUD Height (0..1)")]
            public float HudHeight = 0.70f;

            [JsonProperty(PropertyName = "HUD CenterX (0..1)")]
            public float HudCenterX = 0.50f;

            [JsonProperty(PropertyName = "HUD CenterY (0..1)")]
            public float HudCenterY = 0.50f;

            [JsonProperty(PropertyName = "HUD - AnchorMin (x y)")]
            public string HudAnchorMin = "0.62 0.70";

            [JsonProperty(PropertyName = "HUD - AnchorMax (x y)")]
            public string HudAnchorMax = "0.98 0.96";

            [JsonProperty(PropertyName = "HUD - Background color (RGBA)")]
            public string HudColor = "0 0 0 0.85";

            [JsonProperty(PropertyName = "HUD - Background URL (PNG/JPG)")]
            public string HudBackgroundUrl = "https://i.ibb.co/NnNL215y/AK-Sinistra-2.png";

            [JsonProperty(PropertyName = "HUD - Background sprite (optional)")]
            public string HudBackgroundImage = "aks_bg";

            [JsonProperty(PropertyName = "Close button - AnchorMin")] public string CloseAnchorMin = "0.975 0.955";
            [JsonProperty(PropertyName = "Close button - AnchorMax")] public string CloseAnchorMax = "0.995 0.985";
            [JsonProperty(PropertyName = "Close button - Color")] public string CloseColor = "0.9 0.1 0.1 0.95";
            [JsonProperty(PropertyName = "Close button - Text")] public string CloseText = "X";

            [JsonProperty(PropertyName = "Auto refresh seconds (0 = off)")]
            public float AutoRefreshSeconds = 0f;

            [JsonProperty(PropertyName = "Per-level bonus percent (fallback)")] public float FallbackPerLevelBonusPercent = 0.05f;
            [JsonProperty(PropertyName = "Per-level magazine add (fallback)")] public int FallbackPerLevelMagazineAdd = 1;
            [JsonProperty(PropertyName = "Cost base for levels 1-10 (fallback)")] public int CostBasePerLevel = 100;
            [JsonProperty(PropertyName = "Cost step added every 10 levels (+50 padrão) (fallback)")] public int CostStepEvery10 = 50;

            [JsonProperty(PropertyName = "Show Players")] public bool ShowPlayers = false;
            [JsonProperty(PropertyName = "Players - AnchorMin")] public string PlayersMin = "0.334 0.635";
            [JsonProperty(PropertyName = "Players - AnchorMax")] public string PlayersMax = "0.804 0.785";

            [JsonProperty(PropertyName = "Show NPCs")] public bool ShowNpcs = true;
            [JsonProperty(PropertyName = "NPCs - AnchorMin")] public string NpcsMin = "0.4284 0.606";
            [JsonProperty(PropertyName = "NPCs - AnchorMax")] public string NpcsMax = "0.8984 0.756";

            [JsonProperty(PropertyName = "Show Helicopter")] public bool ShowHeli = true;
            [JsonProperty(PropertyName = "Helicopter - AnchorMin")] public string HeliMin = "0.805 0.55";
            [JsonProperty(PropertyName = "Helicopter - AnchorMax")] public string HeliMax = "1.285 0.70";

            [JsonProperty(PropertyName = "Show Bradley")] public bool ShowBrad = true;
            [JsonProperty(PropertyName = "Bradley - AnchorMin")] public string BradMin = "0.805 0.27";
            [JsonProperty(PropertyName = "Bradley - AnchorMax")] public string BradMax = "1.285 0.42";

            [JsonProperty(PropertyName = "Show Twig")] public bool ShowTwig = false;
            [JsonProperty(PropertyName = "Twig - AnchorMin")] public string TwigMin = "0.02 0.46";
            [JsonProperty(PropertyName = "Twig - AnchorMax")] public string TwigMax = "0.48 0.61";

            [JsonProperty(PropertyName = "Show Wood")] public bool ShowWood = false;
            [JsonProperty(PropertyName = "Wood - AnchorMin")] public string WoodMin = "0.055 -0.015";
            [JsonProperty(PropertyName = "Wood - AnchorMax")] public string WoodMax = "0.535 0.135";

            [JsonProperty(PropertyName = "Show Stone")] public bool ShowStone = true;
            [JsonProperty(PropertyName = "Stone - AnchorMin")] public string StoneMin = "0.052 -0.015";
            [JsonProperty(PropertyName = "Stone - AnchorMax")] public string StoneMax = "0.522 0.135";

            [JsonProperty(PropertyName = "Show Metal")] public bool ShowMetal = true;
            [JsonProperty(PropertyName = "Metal - AnchorMin")] public string MetalMin = "0.298 -0.015";
            [JsonProperty(PropertyName = "Metal - AnchorMax")] public string MetalMax = "0.768 0.135";

            [JsonProperty(PropertyName = "Show Armored")] public bool ShowArmored = true;
            [JsonProperty(PropertyName = "Armored - AnchorMin")] public string ArmoredMin = "0.541 -0.015";
            [JsonProperty(PropertyName = "Armored - AnchorMax")] public string ArmoredMax = "1.021 0.135";

            [JsonProperty(PropertyName = "Show Magazine")] public bool ShowMag = true;
            [JsonProperty(PropertyName = "Magazine - AnchorMin")] public string MagMin = "0.17 0.26";
            [JsonProperty(PropertyName = "Magazine - AnchorMax")] public string MagMax = "0.64 0.41";

            [JsonProperty(PropertyName = "Show Bonus")] public bool ShowBonus = true;
            [JsonProperty(PropertyName = "Bonus - AnchorMin")] public string BonusMin = "0.052 0.537";
            [JsonProperty(PropertyName = "Bonus - AnchorMax")] public string BonusMax = "0.522 0.687";

            [JsonProperty(PropertyName = "Show Deployables")] public bool ShowDeployables = true;
            [JsonProperty(PropertyName = "Deployables - AnchorMin")] public string DeployMin = "0.79 -0.015";
            [JsonProperty(PropertyName = "Deployables - AnchorMax")] public string DeployMax = "1.27 0.135";

            [JsonProperty(PropertyName = "Entry - Label color (azul)")] public string LabelColor = "0.1 0.75 1 0.95";
            [JsonProperty(PropertyName = "Entry - Label font size")] public int LabelSize = 14;
            [JsonProperty(PropertyName = "Entry - Value color (amarelo)")] public string ValueColor = "1 0.9 0.1 0.95";
            [JsonProperty(PropertyName = "Entry - Value font size")] public int ValueSize = 14;
            [JsonProperty(PropertyName = "Entry - Cost color")] public string CostColor = "1 1 1 0.9";
            [JsonProperty(PropertyName = "Entry - Cost font size")] public int CostSize = 12;
            [JsonProperty(PropertyName = "Entry - Button color")] public string ButtonColor = "0.2 0.9 0.2 0.95";
            [JsonProperty(PropertyName = "Entry - Button text")] public string ButtonText = "UP+";
            [JsonProperty(PropertyName = "Entry - Button text color")] public string ButtonTextColor = "0 0 0 0.95";
            [JsonProperty(PropertyName = "Entry - Button font size")] public int ButtonSize = 12;

            [JsonProperty(PropertyName = "Compact - Title label XMax")] public float CompactTitleLabelXMax = 0.32f;
            [JsonProperty(PropertyName = "Compact - Title value XMin")] public float CompactTitleValueXMin = 0.32f;
            [JsonProperty(PropertyName = "Compact - Title value XMax")] public float CompactTitleValueXMax = 0.52f;
            [JsonProperty(PropertyName = "Compact - Button XMin")] public float CompactButtonXMin = 0.42f;
            [JsonProperty(PropertyName = "Compact - Button XMax")] public float CompactButtonXMax = 0.52f;

            [JsonProperty(PropertyName = "Label - Players")]  public string LabelPlayers  = "PLAYERS:";
            [JsonProperty(PropertyName = "Label - NPCs")]     public string LabelNpcs     = "NPCs:";
            [JsonProperty(PropertyName = "Label - Helicopter")] public string LabelHeli  = "HELI:";
            [JsonProperty(PropertyName = "Label - Bradley")]  public string LabelBrad     = "BRADLEY:";
            [JsonProperty(PropertyName = "Label - Twig")]     public string LabelTwig     = "TWIG:";
            [JsonProperty(PropertyName = "Label - Wood")]     public string LabelWood     = "WOOD:";
            [JsonProperty(PropertyName = "Label - Stone")]    public string LabelStone    = "STONE:";
            [JsonProperty(PropertyName = "Label - Metal")]    public string LabelMetal    = "METAL:";
            [JsonProperty(PropertyName = "Label - Armored")]  public string LabelArmored  = "HQ_METAL:";
            [JsonProperty(PropertyName = "Label - Magazine")] public string LabelMag      = "MAGAZINE:";
            [JsonProperty(PropertyName = "Label - Bonus")]    public string LabelBonus    = "CRITICAL:";
            [JsonProperty(PropertyName = "Label - Deployables")] public string LabelDeploy = "DEPLOY:";

            [JsonProperty(PropertyName = "Fallback para URL se IL falhar (true=liga, false=desliga)")]
            public bool FallbackToUrlIfImageLibraryUnavailable = false; // padrão: desligado para evitar "NO IMAGE FOUND"

            [JsonProperty(PropertyName = "Log de debug do ImageLibrary (true=verbose)")]
            public bool DebugImageLibrary = false;
        }

        protected override void LoadDefaultConfig() { config = new ConfigData(); SaveConfig(); }
        protected override void LoadConfig()
        {
            base.LoadConfig();
            try { config = Config.ReadObject<ConfigData>() ?? new ConfigData(); }
            catch { PrintError("Config inválida, recriando padrão."); LoadDefaultConfig(); }
        }
        protected override void SaveConfig() => Config.WriteObject(config, true);

        private class AKPlayerProgress
        {
            public int Players, Npcs, Helicopter, Bradley;
            public int BuildingTwig, BuildingWood, BuildingStone, BuildingMetal, BuildingArmored;
            public int Deployables;
            public int Magazine;
            public int Bonus;
        }
        private AKPlayerProgress ReadAKProgress(ulong uid)
        {
            try
            {
                var path = Path.Combine(Interface.Oxide.DataDirectory, AkDataFolder, $"{uid}.json");
                if (!File.Exists(path)) return new AKPlayerProgress();
                return JsonConvert.DeserializeObject<AKPlayerProgress>(File.ReadAllText(path)) ?? new AKPlayerProgress();
            }
            catch { return new AKPlayerProgress(); }
        }

        private readonly Dictionary<ulong, Timer> refreshTimers = new Dictionary<ulong, Timer>();

        private void Init()
        {
            permission.RegisterPermission(PermUse, this);
            AddCovalenceCommand("aks", nameof(CmdOpen));
            AddCovalenceCommand("aks.close", nameof(CmdClose));
            AddCovalenceCommand("aksui.up", nameof(CmdUp));
            LoadAkConfigFromDisk();
        }

        private void OnServerInitialized()
        {
            // Agora espera de verdade o ImageLibrary ficar pronto ou pelo menos carregar,
            // em vez de assumir IL "ok" quando ainda está null.
            WaitForImageLibrary(() =>
            {
                _ilReady = true;
                PrecacheBackground(true);
            });
        }

        private void OnImageLibraryReady()
        {
            _ilReady = true;
            PrecacheBackground(true);
        }

        private void Unload()
        {
            foreach (var p in BasePlayer.activePlayerList) HideUI(p);
        }

        private void LoadAkConfigFromDisk()
        {
            try
            {
                var cfgPath = Path.Combine(Interface.Oxide.ConfigDirectory, "AKSinistra.json");
                if (!File.Exists(cfgPath))
                {
                    _akCfg = null;
                    PrintWarning("[AKS HUD] AKSinistra.json não encontrado; usando fallbacks (HUD).");
                    return;
                }
                var json = File.ReadAllText(cfgPath);
                _akCfg = JsonConvert.DeserializeObject<AkConfigMirror>(json);
            }
            catch (Exception e)
            {
                _akCfg = null;
                PrintWarning($"[AKS HUD] Erro lendo AKSinistra.json: {e.Message}");
            }
        }

        private void CmdOpen(IPlayer iPlayer, string cmd, string[] args)
        {
            if (!iPlayer.IsConnected) return;
            if (config.RequirePermission && !iPlayer.HasPermission(PermUse))
            {
                iPlayer.Reply("Sem permissão para usar /aks.");
                return;
            }
            var bp = iPlayer.Object as BasePlayer;
            if (bp == null) return;

            ShowUI(bp);

            if (config.AutoRefreshSeconds > 0f)
            {
                if (refreshTimers.TryGetValue(bp.userID, out var t)) { t.Destroy(); refreshTimers.Remove(bp.userID); }
                refreshTimers[bp.userID] = timer.Repeat(config.AutoRefreshSeconds, 0, () =>
                {
                    if (bp == null || !bp.IsConnected) return;
                    RefreshAllEntries(bp);
                });
            }
        }

        private void CmdClose(IPlayer iPlayer, string cmd, string[] args)
        {
            if (!iPlayer.IsConnected) return;
            var bp = iPlayer.Object as BasePlayer;
            if (bp == null) return;
            HideUI(bp);
        }

        private void CmdUp(IPlayer iPlayer, string cmd, string[] args)
        {
            if (!iPlayer.IsConnected || args == null || args.Length < 1) return;
            if (config.RequirePermission && !iPlayer.HasPermission(PermUse)) return;

            var key = args[0].ToLowerInvariant();
            iPlayer.Command($"aksinistra {key}");

            timer.Once(0.15f, () =>
            {
                var bp = iPlayer.Object as BasePlayer;
                if (bp != null && bp.IsConnected) RefreshEntry(bp, key);
            });
        }

        [ConsoleCommand("akshud.reloadimg")]
        private void CmdReloadImg(ConsoleSystem.Arg arg)
        {
            PrecacheBackground(true);
            foreach (var p in BasePlayer.activePlayerList)
                AttachBackgroundOrRetry(p, 0, true);
            Puts("[AKSinistraHUD] Imagem re-enfileirada no ImageLibrary.");
        }

        private void ShowUI(BasePlayer player)
        {
            if (player == null) return;
            HideUI(player);
            var ui = new CuiElementContainer();

            string rootMin, rootMax;
            if (config.UseCenteredSize)
            {
                float w = Mathf.Clamp01(config.HudWidth);
                float h = Mathf.Clamp01(config.HudHeight);
                float cx = Mathf.Clamp01(config.HudCenterX);
                float cy = Mathf.Clamp01(config.HudCenterY);

                float minX = Mathf.Clamp01(cx - w / 2f);
                float minY = Mathf.Clamp01(cy - h / 2f);
                float maxX = Mathf.Clamp01(cx + w / 2f);
                float maxY = Mathf.Clamp01(cy + h / 2f);

                rootMin = $"{minX:0.##} {minY:0.##}";
                rootMax = $"{maxX:0.##} {maxY:0.##}";
            }
            else
            {
                rootMin = config.HudAnchorMin;
                rootMax = config.HudAnchorMax;
            }

            ui.Add(new CuiPanel
            {
                Image = { Color = config.HudColor }, // sempre cor base para não ficar invisível
                RectTransform = { AnchorMin = rootMin, AnchorMax = rootMax },
                CursorEnabled = true
            }, "Overlay", UiRoot);

            ui.Add(new CuiPanel
            {
                Image = { Color = "1 1 1 0" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, UiRoot, UiBg);

            AddButton(ui, UiRoot, config.CloseText, null, config.CloseColor,
                      config.CloseAnchorMin, config.CloseAnchorMax, 14, "1 1 1 0.95", closeTarget: UiRoot);

            BuildOrRefreshAllBlocks(ui, player);

            CuiHelper.AddUi(player, ui);

            // Fundo
            AttachBackgroundOrRetry(player, 0, true);
        }

        private void HideUI(BasePlayer player)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, UiRoot);
            if (refreshTimers.TryGetValue(player.userID, out var t)) { t.Destroy(); refreshTimers.Remove(player.userID); }
        }

        private void BuildOrRefreshAllBlocks(CuiElementContainer ui, BasePlayer player)
        {
            var prog = ReadAKProgress(player.userID);

            if (config.ShowPlayers)      BuildEntry(ui, "players",   config.LabelPlayers,   prog.Players,          config.PlayersMin,  config.PlayersMax);
            if (config.ShowNpcs)         BuildEntry(ui, "npcs",      config.LabelNpcs,      prog.Npcs,             config.NpcsMin,     config.NpcsMax);
            if (config.ShowHeli)         BuildEntry(ui, "heli",      config.LabelHeli,      prog.Helicopter,       config.HeliMin,     config.HeliMax);
            if (config.ShowBrad)         BuildEntry(ui, "bradley",   config.LabelBrad,      prog.Bradley,          config.BradMin,     config.BradMax);
            if (config.ShowTwig)         BuildEntry(ui, "twig",      config.LabelTwig,      prog.BuildingTwig,     config.TwigMin,     config.TwigMax);
            if (config.ShowWood)         BuildEntry(ui, "wood",      config.LabelWood,      prog.BuildingWood,     config.WoodMin,     config.WoodMax);
            if (config.ShowStone)        BuildEntry(ui, "stone",     config.LabelStone,     prog.BuildingStone,    config.StoneMin,    config.StoneMax);
            if (config.ShowMetal)        BuildEntry(ui, "metal",     config.LabelMetal,     prog.BuildingMetal,    config.MetalMin,    config.MetalMax);
            if (config.ShowArmored)      BuildEntry(ui, "armored",   config.LabelArmored,   prog.BuildingArmored,  config.ArmoredMin,  config.ArmoredMax);
            if (config.ShowMag)          BuildEntry(ui, "mag",       config.LabelMag,       prog.Magazine,         config.MagMin,      config.MagMax, isMagazine: true);

            if (config.ShowBonus)        BuildEntry(ui, "bonus",     config.LabelBonus,     prog.Bonus,            config.BonusMin,    config.BonusMax);
            if (config.ShowDeployables)  BuildEntry(ui, "deploy",    config.LabelDeploy,    prog.Deployables,      config.DeployMin,   config.DeployMax);
        }

        private void RefreshAllEntries(BasePlayer player)
        {
            RefreshEntry(player, "players");
            RefreshEntry(player, "npcs");
            RefreshEntry(player, "heli");
            RefreshEntry(player, "bradley");
            RefreshEntry(player, "twig");
            RefreshEntry(player, "wood");
            RefreshEntry(player, "stone");
            RefreshEntry(player, "metal");
            RefreshEntry(player, "armored");
            RefreshEntry(player, "mag");
            RefreshEntry(player, "bonus");
            RefreshEntry(player, "deploy");
        }

        private void RefreshEntry(BasePlayer player, string key)
        {
            var entryName = EntryName(key);
            CuiHelper.DestroyUi(player, entryName);

            var ui = new CuiElementContainer();
            var prog = ReadAKProgress(player.userID);

            switch (key)
            {
                case "players":  if (config.ShowPlayers)      BuildEntry(ui, "players",   config.LabelPlayers,   prog.Players,         config.PlayersMin,  config.PlayersMax); break;
                case "npcs":     if (config.ShowNpcs)         BuildEntry(ui, "npcs",      config.LabelNpcs,      prog.Npcs,            config.NpcsMin,     config.NpcsMax); break;
                case "heli":     if (config.ShowHeli)         BuildEntry(ui, "heli",      config.LabelHeli,      prog.Helicopter,      config.HeliMin,     config.HeliMax); break;
                case "bradley":  if (config.ShowBrad)         BuildEntry(ui, "bradley",   config.LabelBrad,      prog.Bradley,         config.BradMin,     config.BradMax); break;
                case "twig":     if (config.ShowTwig)         BuildEntry(ui, "twig",      config.LabelTwig,      prog.BuildingTwig,    config.TwigMin,     config.TwigMax); break;
                case "wood":     if (config.ShowWood)         BuildEntry(ui, "wood",      config.LabelWood,      prog.BuildingWood,    config.WoodMin,     config.WoodMax); break;
                case "stone":    if (config.ShowStone)        BuildEntry(ui, "stone",     config.LabelStone,     prog.BuildingStone,   config.StoneMin,    config.StoneMax); break;
                case "metal":    if (config.ShowMetal)        BuildEntry(ui, "metal",     config.LabelMetal,     prog.BuildingMetal,   config.MetalMin,    config.MetalMax); break;
                case "armored":  if (config.ShowArmored)      BuildEntry(ui, "armored",   config.LabelArmored,   prog.BuildingArmored, config.ArmoredMin,  config.ArmoredMax); break;
                case "mag":      if (config.ShowMag)          BuildEntry(ui, "mag",       config.LabelMag,       prog.Magazine,        config.MagMin,      config.MagMax, isMagazine: true); break;
                case "bonus":    if (config.ShowBonus)        BuildEntry(ui, "bonus",     config.LabelBonus,     prog.Bonus,           config.BonusMin,    config.BonusMax); break;
                case "deploy":   if (config.ShowDeployables)  BuildEntry(ui, "deploy",    config.LabelDeploy,    prog.Deployables,     config.DeployMin,   config.DeployMax); break;
            }

            if (ui.Count > 0) CuiHelper.AddUi(player, ui);
        }

        private string EntryName(string key) => $"AKS_ENTRY_{key.ToUpper()}";

        // ---------- BLOCO ----------
        private void BuildEntry(CuiElementContainer ui, string key, string label, int currentLevel,
                                string anchorMin, string anchorMax, bool isMagazine = false)
        {
            var entry = new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
            };
            var entryName = EntryName(key);
            ui.Add(entry, UiRoot, entryName);

            AddText(ui, entryName, label, config.LabelColor,
                    "0.00 0.35", "0.20 1.00",
                    Mathf.Max(18, config.LabelSize - 4), TextAnchor.UpperLeft);

            float bonusPct = GetEffectiveBonusPercent();
            int magAddPerLvl = GetEffectiveMagAdd();

            string valueText;
            if (!isMagazine)
            {
                bool isBonus = CanonicalFromHudKey(key) == "Bonus";
                if (isBonus)
                {
                    int pct = Mathf.Clamp(currentLevel, 0, 100);
                    valueText = $"{pct}%";
                }
                else
                {
                    float total = 1f + (currentLevel * bonusPct);
                    int pct = Mathf.RoundToInt(total * 100f);
                    valueText = $"{pct}%";
                }
            }
            else
            {
                int extra = currentLevel * magAddPerLvl;
                valueText = $"{38 + extra}";
            }

            AddText(ui, entryName, valueText, config.ValueColor,
                    "0.20 0.35", "0.35 1.00",
                    Mathf.Max(18, config.ValueSize - 2), TextAnchor.UpperRight);

            int nextCost = NextLevelCostFromAK(key, currentLevel);
            string costText = $"$ {nextCost.ToString("N2", new CultureInfo("pt-BR"))}";
            AddText(ui, entryName, costText, config.CostColor,
                    "0.00 0.1", "0.35 1.0",
                    Mathf.Max(14, config.CostSize - 2), TextAnchor.MiddleLeft);

            AddButton(ui, entryName, config.ButtonText, $"aksui.up {key}",
                      config.ButtonColor,
                      "0.25 0.45", "0.35 0.66",
                      Mathf.Max(14, config.ButtonSize - 2), config.ButtonTextColor);
        }

        private int NextLevelCostFromAK(string hudKey, int currentLevel)
        {
            string canonical = CanonicalFromHudKey(hudKey);
            int nextLevel = Mathf.Max(1, currentLevel + 1);

            if (_akCfg != null && _akCfg.Costs != null && _akCfg.Costs.TryGetValue(canonical, out var p))
            {
                int block = (nextLevel - 1) / 10;
                return p.CostBasePerLevel + (p.CostStepEvery10 * block);
            }

            int fbBlock = (nextLevel - 1) / 10;
            return config.CostBasePerLevel + (config.CostStepEvery10 * fbBlock);
        }

        private float GetEffectiveBonusPercent()
        {
            if (_akCfg != null)
            {
                if (_akCfg.BonusPercentPT.HasValue) return _akCfg.BonusPercentPT.Value;
                if (_akCfg.BonusPercentEN.HasValue) return _akCfg.BonusPercentEN.Value;
            }
            return config.FallbackPerLevelBonusPercent;
        }

        private int GetEffectiveMagAdd()
        {
            if (_akCfg != null)
            {
                if (_akCfg.MagAddPT.HasValue) return _akCfg.MagAddPT.Value;
                if (_akCfg.MagAddEN.HasValue) return _akCfg.MagAddEN.Value;
            }
            return config.FallbackPerLevelMagazineAdd;
        }

        private string CanonicalFromHudKey(string key)
        {
            switch (key)
            {
                case "players":  return "Players";
                case "npcs":     return "Npcs";
                case "heli":     return "Helicopter";
                case "bradley":  return "Bradley";
                case "twig":     return "BuildingTwig";
                case "wood":     return "BuildingWood";
                case "stone":    return "BuildingStone";
                case "metal":    return "BuildingMetal";
                case "armored":  return "BuildingArmored";
                case "deploy":   return "Deployables";
                case "mag":      return "Magazine";
                case "bonus":    return "Bonus";
                default:         return key;
            }
        }

        // Helpers de UI
        private void AddText(CuiElementContainer ui, string parent, string text, string color,
                             string anchorMin, string anchorMax, int size, TextAnchor align)
        {
            ui.Add(new CuiLabel
            {
                Text = { Text = text, FontSize = size, Align = align, Color = color },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
            }, parent);
        }

        private void AddButton(CuiElementContainer ui, string parent, string text, string command,
                               string color, string anchorMin, string anchorMax, int size, string textColor,
                               string closeTarget = null)
        {
            ui.Add(new CuiButton
            {
                Button = { Color = color, Command = command ?? "", Close = closeTarget ?? "" },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                Text = { Text = text, FontSize = size, Align = TextAnchor.MiddleCenter, Color = textColor }
            }, parent);
        }

        // ======= ImageLibrary (hash CRC32 + wait + enqueue) =======
        private void WaitForImageLibrary(Action onReady)
        {
            int tries = 0;

            bool Ready()
            {
                if (ImageLibrary == null)
                    return false;

                try
                {
                    var o = ImageLibrary.Call("IsReady");
                    return o is bool b && b;
                }
                catch
                {
                    return false;
                }
            }

            // Se já estiver pronto na hora que o servidor terminou de iniciar
            if (Ready())
            {
                if (config.DebugImageLibrary) Puts("[AKS HUD] ImageLibrary já pronto na inicialização.");
                onReady?.Invoke();
                return;
            }

            // Caso contrário, vamos ficar esperando até IL_READY_MAX_TRIES * IL_RETRY_SECONDS
            Timer ilTimer = null;
            ilTimer = timer.Repeat(IL_RETRY_SECONDS, IL_READY_MAX_TRIES, () =>
            {
                tries++;

                if (Ready())
                {
                    if (config.DebugImageLibrary)
                        Puts($"[AKS HUD] ImageLibrary pronto após {tries * IL_RETRY_SECONDS:0.##} segundos.");

                    ilTimer?.Destroy();
                    onReady?.Invoke();
                    return;
                }

                if (tries == 1 && config.DebugImageLibrary)
                    Puts("[AKS HUD] Aguardando ImageLibrary carregar e ficar pronto...");

                if (tries >= IL_READY_MAX_TRIES)
                {
                    PrintWarning("[AKSinistraHUD] ImageLibrary não ficou pronto dentro do tempo limite; HUD usará fallback (URL se habilitado).");
                    ilTimer?.Destroy();
                }
            });
        }

        private static string HashKey(string url)
        {
            var bytes = Encoding.UTF8.GetBytes(url ?? "");
            using var crc32 = new Crc32();
            var hash = crc32.ComputeHash(bytes);
            uint val = BitConverter.ToUInt32(hash, 0);
            return $"mpb_{val}"; // mesma família de keys do MPButtons
        }

        private void PrecacheBackground(bool force = false)
        {
            if (ImageLibrary == null) return;
            var url = config?.HudBackgroundUrl;
            if (string.IsNullOrEmpty(url)) return;
            try
            {
                var key = HashKey(url);
                if (force)
                {
                    ImageLibrary.Call("AddImage", url, key, 0UL);
                    if (config.DebugImageLibrary) Puts($"[AKS HUD] AddImage force {key} <= {url}");
                }
                else
                {
                    var got = ImageLibrary.Call<string>("GetImage", key);
                    if (string.IsNullOrEmpty(got))
                    {
                        ImageLibrary.Call("AddImage", url, key, 0UL);
                        if (config.DebugImageLibrary) Puts($"[AKS HUD] AddImage {key} <= {url}");
                    }
                }
            }
            catch (System.Exception e)
            {
                PrintWarning($"[AKS HUD] PrecacheBackground error: {e.Message}");
            }
        }

        private bool TryGetPng(string url, out string png)
        {
            png = null;
            if (ImageLibrary == null || string.IsNullOrEmpty(url)) return false;

            var key = HashKey(url);
            try
            {
                var got = ImageLibrary.Call<string>("GetImage", key);
                if (!string.IsNullOrEmpty(got))
                {
                    png = got;
                    if (config.DebugImageLibrary) Puts($"[AKS HUD] PNG OK {key} (len {got.Length})");
                    return true;
                }

                ImageLibrary.Call("AddImage", url, key, 0UL);
                if (config.DebugImageLibrary) Puts($"[AKS HUD] PNG pendente {key} (enfileirado)");
            }
            catch (System.Exception ex)
            {
                PrintWarning($"[AKS HUD] TryGetPng erro {url}: {ex.Message}");
            }
            return false;
        }

        private void AddRawImagePng(CuiElementContainer ui, string parent, string png)
        {
            ui.Add(new CuiElement
            {
                Parent = parent,
                Components =
                {
                    new CuiRawImageComponent { Png = png, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                }
            });
        }

        private void AddRawImageUrl(CuiElementContainer ui, string parent, string url)
        {
            ui.Add(new CuiElement
            {
                Parent = parent,
                Components =
                {
                    new CuiRawImageComponent { Url = url, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                }
            });
        }

        private string BgUrl => (config?.HudBackgroundUrl ?? "").Trim();

        private void AttachBackgroundOrRetry(BasePlayer bp, int attempt = 0, bool firstCall = false)
        {
            if (bp == null || !bp.IsConnected || string.IsNullOrEmpty(BgUrl)) return;

            // Se IL não está pronto, espera sem gastar tentativas
            if (!_ilReady && ImageLibrary != null)
            {
                if (attempt == 0 && config.DebugImageLibrary) Puts("[AKS HUD] aguardando IL ficar pronto...");
                timer.Once(IL_RETRY_SECONDS, () => AttachBackgroundOrRetry(bp, attempt, false));
                return;
            }

            if (TryGetPng(BgUrl, out var png))
            {
                var ui = new CuiElementContainer();
                AddRawImagePng(ui, UiBg, png);
                CuiHelper.AddUi(bp, ui);
                return;
            }

            if (attempt >= IL_MAX_TRIES)
            {
                if (config.FallbackToUrlIfImageLibraryUnavailable)
                {
                    var ui = new CuiElementContainer();
                    AddRawImageUrl(ui, UiBg, BgUrl);
                    CuiHelper.AddUi(bp, ui);
                    if (config.DebugImageLibrary) Puts("[AKS HUD] Fallback por URL aplicado.");
                }
                else if (config.DebugImageLibrary)
                {
                    Puts("[AKS HUD] Esgotou retries sem PNG; fallback por URL desligado.");
                }
                return;
            }

            if (config.DebugImageLibrary && (attempt % 10 == 0 || firstCall))
                Puts($"[AKS HUD] aguardando PNG do IL... tentativa {attempt+1}/{IL_MAX_TRIES}");

            PrecacheBackground(); // garante enfileiramento
            timer.Once(IL_RETRY_SECONDS, () => AttachBackgroundOrRetry(bp, attempt + 1, false));
        }

        // ===== CRC32 (igual MPButtons) =====
        private class Crc32 : HashAlgorithm
        {
            public const uint DefaultPolynomial = 0xEDB88320u;
            public const uint DefaultSeed = 0xFFFFFFFFu;

            private uint _hash;
            private readonly uint _seed;
            private readonly uint[] _table;

            public Crc32() : this(DefaultPolynomial, DefaultSeed) { }

            public Crc32(uint polynomial, uint seed)
            {
                _table = InitializeTable(polynomial);
                _seed = seed;
                Initialize();
            }

            public override void Initialize() => _hash = _seed;

            protected override void HashCore(byte[] array, int ibStart, int cbSize)
            {
                _hash = CalculateHash(_table, _hash, array, ibStart, cbSize);
            }

            protected override byte[] HashFinal()
            {
                var hashBuffer = UInt32ToBigEndianBytes(~_hash);
                HashValue = hashBuffer;
                return hashBuffer;
            }

            public override int HashSize => 32;

            private static uint[] InitializeTable(uint polynomial)
            {
                var createTable = new uint[256];
                for (var i = 0; i < 256; i++)
                {
                    var entry = (uint)i;
                    for (var j = 0; j < 8; j++)
                        entry = (entry & 1) == 1 ? (entry >> 1) ^ polynomial : entry >> 1;
                    createTable[i] = entry;
                }
                return createTable;
            }

            private static uint CalculateHash(uint[] table, uint seed, IList<byte> buffer, int start, int size)
            {
                var crc = seed;
                for (var i = start; i < start + size; i++)
                    unchecked { crc = (crc >> 1) ^ table[buffer[i] ^ (crc & 0xff)]; }
                return crc;
            }

            private static byte[] UInt32ToBigEndianBytes(uint x)
            {
                return new[] { (byte)((x >> 24) & 0xff), (byte)((x >> 16) & 0xff), (byte)((x >> 8) & 0xff), (byte)(x & 0xff) };
            }
        }
    }
}
