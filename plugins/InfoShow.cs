using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using System.Security.Cryptography;

namespace Oxide.Plugins
{
    [Info("InfoShow", "Mestre Pardal", "3.3.2")]
    [Description("HUD refinada: launcher sempre visível (Hud.Menu) e posição default no canto superior esquerdo.")]
    public class InfoShow : RustPlugin
    {
        #region References
        [PluginReference] private Plugin ImageLibrary;
        #endregion

        #region Config
        private ConfigData _config;
        private const string PERM = "infoshow.use";

        private class ConfigData
        {
            public bool RequirePermission = false;
            public string PermissionName = PERM;

            public string AnchorMin = "0.06 0.06";
            public string AnchorMax = "0.94 0.94";
            public string PanelColor = "0.10 0.10 0.10 0.98";
            public bool BlockInput = true;

            public TitleBarUI TitleBar = new TitleBarUI();
            public CloseBtnUI CloseBtn = new CloseBtnUI();
            public GridUI Grid = new GridUI();
            public LauncherUI Launcher = new LauncherUI();
            public LoadingUI Loading = new LoadingUI();
            public PaginationUI Pagination = new PaginationUI();

            public List<GridItem> Items = new List<GridItem>();

            public bool DebugImageLibrary = false;

            public class TitleBarUI
            {
                public string ShowTitle = "CENTRAL DE INFORMAÇÕES";
                public string AnchorMin = "0 0.92";
                public string AnchorMax = "1 1";
                public string Bg = "0.15 0.15 0.15 1";
                public string TextColor = "0.9 0.9 0.9 1";
                public int TextSize = 22;
            }

            public class CloseBtnUI
            {
                public string AnchorMin = "0.965 0.935";
                public string AnchorMax = "0.995 0.985";
                public string Bg = "0.8 0.2 0.2 0.8";
                public string Text = "×";
                public string TextColor = "1 1 1 1";
                public int TextSize = 18;
            }

            public class GridUI
            {
                public int Cols = 5;
                public int Rows = 2;
                public float Padding = 0.04f;
                public string CellBg = "0.18 0.18 0.18 0.9";

                public string TitleColor = "1 1 1 1";
                public int TitleSize = 13;
                public string SubColor = "0.7 0.7 0.7 1";
                public int SubSize = 10;
                public float ThumbBottom = 0.20f;

                public string HighlightTint = "0.2 0.6 1.0 0.25";
                public float ClickFlashSeconds = 0.15f;
            }

            public class PaginationUI
            {
                public bool Enabled = true;
                public string AnchorMin = "0 0.00";
                public string AnchorMax = "1 0.06";
                public string Bg = "0.12 0.12 0.12 0.95";
                public string TextColor = "0.4 0.8 1.0 1";
                public int TextSize = 14;
                public string BtnBg = "0.25 0.25 0.25 1";
                public string BtnTextColor = "1 1 1 1";
            }

            public class LauncherUI
            {
                public bool Enabled = true;

                public string AnchorMin = "0.010 0.945";
                public string AnchorMax = "0.040 0.990";

                public string Bg = "0 0 0 0";
                public string IconUrl = "https://i.ibb.co/99M2Rzxp/Tutorials.png";
            }

            public class LoadingUI
            {
                public string AnchorMin = "0.30 0.30";
                public string AnchorMax = "0.70 0.70";
                public string Bg = "0.1 0.1 0.1 0.98";
                public string ScrimColor = "0 0 0 0.85";

                public string LoadingTitle = "VÍDEO CARREGANDO...";
                public string WarningText = "Alguns vídeos demoram mais para carregar.\nCaso já tenha assistido, é só fechar essa janela.";
                public string TextColor = "1 1 1 1";
                public string WarningColor = "0.7 0.7 0.7 1";

                public bool ShowProgress = true;
                public string BarBgColor = "0.2 0.2 0.2 1";
                public string BarFillColor = "0.2 0.6 1.0 1";
                public float MarqueeChunkWidth = 0.3f;
                public float MarqueeSpeed = 0.03f;
                public float MarqueeTickSeconds = 0.03f;
                public float ProgressSeconds = 2.5f;

                public string PreviewThumbAnchorMin = "0.415 0.55";
                public string PreviewThumbAnchorMax = "0.585 0.85";
            }

            public class GridItem
            {
                public string Title = "Título do Vídeo";
                public string Subtitle = "Descrição curta do conteúdo";
                public string IconUrl = "";
                public string OpenUrl = "";
            }
        }

        protected override void LoadDefaultConfig()
        {
            _config = new ConfigData();
            _config.Items = new List<ConfigData.GridItem>();

            string[] titulosExemplo = {
                "Bem-vindo ao Servidor", "Regras da Comunidade", "Tutoriais de Construção",
                "Eventos Especiais", "Loja VIP", "Sistema de Clãs",
                "Farm e Recursos", "Raids e Defesa", "Dicas de PvP", "Outros"
            };

            for (int i = 0; i < 10; i++)
            {
                _config.Items.Add(new ConfigData.GridItem
                {
                    Title = titulosExemplo[i],
                    Subtitle = "Clique para assistir agora",
                    IconUrl = "https://i.ibb.co/nszRgShY/Porta5.png",
                    OpenUrl = "https://filesamples.com/samples/video/mp4/sample_640x360.mp4"
                });
            }
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try { _config = Config.ReadObject<ConfigData>(); }
            catch { LoadDefaultConfig(); }
            if (_config == null) LoadDefaultConfig();
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);
        #endregion

        #region State
        private const string UI_MAIN = "InfoShow.Main";
        private const string UI_LOADER = "InfoShow.Loader";
        private const string UI_LOADER_BOX = "InfoShow.Loader.Box";
        private const string UI_LOADER_BARBG = "InfoShow.Loader.BarBg";
        private const string UI_LOADER_BARFILL = "InfoShow.Loader.BarFill";
        private const string UI_LAUNCHER = "InfoShow.Launcher";
        private const string UI_PAGBAR = "InfoShow.Pagination";

        private readonly Dictionary<ulong, int> _pageByUser = new Dictionary<ulong, int>();
        private readonly HashSet<ulong> _isLoading = new HashSet<ulong>();
        private readonly HashSet<ulong> _isHudOpen = new HashSet<ulong>();
        private readonly HashSet<ulong> _hasLauncherOpen = new HashSet<ulong>();
        private readonly Dictionary<ulong, Timer> _animTimers = new Dictionary<ulong, Timer>();
        private readonly Dictionary<ulong, Timer> _endTimers = new Dictionary<ulong, Timer>();
        private readonly Dictionary<ulong, ConfigData.GridItem> _lastItemByUser = new Dictionary<ulong, ConfigData.GridItem>();

        private bool _ilReady = false;
        #endregion

        #region Lifecycle & Hooks
        private void Init()
        {
            permission.RegisterPermission(PERM, this);
            AddCovalenceCommand("infoshow", nameof(CmdInfoShow));
            AddCovalenceCommand("is", nameof(CmdInfoShow));
            AddCovalenceCommand("infoshow.closeloading", nameof(CmdCloseLoading));
        }

        private void OnServerInitialized()
        {
            CheckImageLibraryLoop();
            foreach (var bp in BasePlayer.activePlayerList) ShowLauncher(bp);
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin != null && plugin.Name == "ImageLibrary")
            {
                _ilReady = false;
                CheckImageLibraryLoop();
            }
        }

        private void OnPluginUnloaded(Plugin plugin)
        {
            if (plugin != null && plugin.Name == "ImageLibrary") _ilReady = false;
        }

        private void OnPlayerConnected(BasePlayer bp)
        {
            timer.Once(3f, () => {
                if (bp != null && bp.IsConnected) ShowLauncher(bp);
            });
        }

        private void Unload()
        {
            foreach (var bp in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(bp, UI_MAIN);
                HideLoading(bp);
                CuiHelper.DestroyUi(bp, UI_LAUNCHER);
            }
        }
        #endregion

        #region Commands
        private void CmdInfoShow(IPlayer iplayer, string command, string[] args)
        {
            var bp = iplayer?.Object as BasePlayer;
            if (bp == null) return;

            if (_config.RequirePermission && !permission.UserHasPermission(bp.UserIDString, _config.PermissionName))
            {
                bp.ChatMessage("<color=#ff5555>Você não tem permissão.</color>");
                return;
            }
            _pageByUser[bp.userID] = 0;
            ShowHUD(bp, 0);
        }

        [ConsoleCommand("infoshow.show")]
        private void CCmd_Show(ConsoleSystem.Arg arg)
        {
            var bp = arg?.Player(); if (bp == null) return;
            _pageByUser[bp.userID] = 0;
            ShowHUD(bp, 0);
        }

        [ConsoleCommand("infoshow.open")]
        private void CCmd_OpenIndex(ConsoleSystem.Arg arg)
        {
            var bp = arg?.Player();
            if (bp == null || arg.Args == null || arg.Args.Length == 0) return;
            if (!int.TryParse(arg.Args[0], out var idxInPage)) return;

            int page = _pageByUser.TryGetValue(bp.userID, out var p) ? p : 0;
            int capacity = CapacityPerPage();
            int absolute = page * capacity + idxInPage;

            if (absolute < 0 || absolute >= _config.Items.Count) return;
            var it = _config.Items[absolute];
            if (string.IsNullOrEmpty(it.OpenUrl)) return;

            FlashThumb(bp, absolute);
            PlayVideoWithLoading(bp, it.OpenUrl, it);
        }

        [ConsoleCommand("infoshow.close")]
        private void CCmd_Close(ConsoleSystem.Arg arg)
        {
            var bp = arg?.Player();
            if (bp == null) return;
            CloseHUD(bp);
        }

        private void CmdCloseLoading(IPlayer iplayer, string command, string[] args)
        {
            var bp = iplayer?.Object as BasePlayer;
            if (bp == null) return;

            HideLoading(bp);
            bp.SendConsoleCommand("client.stopvideo");
        }

        [ConsoleCommand("infoshow.page")]
        private void CCmd_Page(ConsoleSystem.Arg arg)
        {
            var bp = arg?.Player();
            if (bp == null || arg.Args == null || arg.Args.Length == 0) return;
            int current = _pageByUser.TryGetValue(bp.userID, out var p) ? p : 0;
            int totalPages = Mathf.CeilToInt((float)_config.Items.Count / CapacityPerPage());
            var token = arg.Args[0].ToLower();
            if (token == "next") current++;
            else if (token == "prev") current--;
            current = Mathf.Clamp(current, 0, totalPages - 1);
            _pageByUser[bp.userID] = current;
            ShowHUD(bp, current);
        }
        #endregion

        #region UI Implementation (Main Grid)
        private int CapacityPerPage() => Mathf.Max(1, _config.Grid.Cols) * Mathf.Max(1, _config.Grid.Rows);

        private void ShowHUD(BasePlayer bp, int page)
        {
            HideLauncher(bp);
            CuiHelper.DestroyUi(bp, UI_MAIN);
            HideLoading(bp);

            _isHudOpen.Add(bp.userID);

            int cols = _config.Grid.Cols;
            int rows = _config.Grid.Rows;
            int capacity = cols * rows;

            var ui = new CuiElementContainer();

            var main = ui.Add(new CuiPanel
            {
                Image = { Color = _config.PanelColor },
                RectTransform = { AnchorMin = _config.AnchorMin, AnchorMax = _config.AnchorMax },
                CursorEnabled = true
            }, "Overlay", UI_MAIN);

            if (_config.BlockInput)
            {
                ui.Add(new CuiElement
                {
                    Parent = main,
                    Name = $"{UI_MAIN}.Blocker",
                    Components = {
                        new CuiRawImageComponent { Color = "0 0 0 0" },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" },
                        new CuiNeedsCursorComponent()
                    }
                });
            }

            var titleBar = ui.Add(new CuiPanel
            {
                Image = { Color = _config.TitleBar.Bg },
                RectTransform = { AnchorMin = _config.TitleBar.AnchorMin, AnchorMax = _config.TitleBar.AnchorMax }
            }, main);

            ui.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "0.03 0", AnchorMax = "0.90 1" },
                Text = { Text = _config.TitleBar.ShowTitle.ToUpper(), FontSize = _config.TitleBar.TextSize, Align = TextAnchor.MiddleLeft, Color = _config.TitleBar.TextColor, Font = "robotocondensed-bold.ttf" }
            }, titleBar);

            ui.Add(new CuiButton
            {
                RectTransform = { AnchorMin = _config.CloseBtn.AnchorMin, AnchorMax = _config.CloseBtn.AnchorMax },
                Button = { Color = _config.CloseBtn.Bg, Command = "infoshow.close" },
                Text = { Text = _config.CloseBtn.Text, FontSize = _config.CloseBtn.TextSize, Align = TextAnchor.MiddleCenter, Color = _config.CloseBtn.TextColor }
            }, main);

            float topCut = 0.90f;
            float bottomCut = _config.Pagination.Enabled ? 0.08f : 0.02f;
            float usableHeight = topCut - bottomCut;
            int start = page * capacity;
            int end = Mathf.Min(start + capacity, _config.Items.Count);
            float cellW = 1f / cols;
            float cellH = usableHeight / rows;

            for (int slot = 0, i = start; i < end; i++, slot++)
            {
                var item = _config.Items[i];
                int r = slot / cols;
                int c = slot % cols;

                float minX = c * cellW + _config.Grid.Padding * 0.5f;
                float maxX = (c + 1) * cellW - _config.Grid.Padding * 0.5f;
                float minY = bottomCut + (rows - r - 1) * cellH + _config.Grid.Padding * 0.5f;
                float maxY = minY + cellH - _config.Grid.Padding * 0.5f;

                string blockName = $"{UI_MAIN}.Block.{i}";

                ui.Add(new CuiPanel
                {
                    Image = { Color = _config.Grid.CellBg },
                    RectTransform = { AnchorMin = $"{minX} {minY}", AnchorMax = $"{maxX} {maxY}" }
                }, main, blockName);

                var thumbMin = $"0 {_config.Grid.ThumbBottom}";
                var thumbMax = "1 1";

                if (!string.IsNullOrEmpty(item.IconUrl))
                {
                    string thumbParentName = $"{blockName}.Thumb";
                    string imgName = $"{thumbParentName}.img";

                    ui.Add(new CuiElement {
                        Parent = blockName, Name = thumbParentName,
                        Components = { new CuiRawImageComponent{ Color = "0 0 0 0" }, new CuiRectTransformComponent{ AnchorMin = thumbMin, AnchorMax = thumbMax } }
                    });

                    string png = null;
                    if (_ilReady && ImageLibrary != null) png = ImageLibrary.Call<string>("GetImage", HashKey(item.IconUrl));

                    if (!string.IsNullOrEmpty(png)) {
                        ui.Add(new CuiElement { Parent = thumbParentName, Name = imgName, Components = { new CuiRawImageComponent { Png = png }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" } } });
                    } else {
                        ui.Add(new CuiElement { Parent = thumbParentName, Name = imgName, Components = { new CuiRawImageComponent { Url = item.IconUrl }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" } } });
                        if (_ilReady) {
                            ImageLibrary.Call("AddImage", item.IconUrl, HashKey(item.IconUrl), 0UL);
                            timer.Once(1.5f, () => {
                                if (_isHudOpen.Contains(bp.userID))
                                    AttachImageWithRetry(bp, thumbParentName, item.IconUrl, "0 0", "1 1");
                            });
                        }
                    }
                }

                ui.Add(new CuiButton
                {
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    Button = { Color = "0 0 0 0", Command = $"infoshow.open {slot}" },
                    Text = { Text = "" }
                }, blockName);

                ui.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0.05 0.10", AnchorMax = "0.95 0.23" },
                    Text = { Text = item.Title ?? "", Align = TextAnchor.MiddleLeft, Color = _config.Grid.TitleColor, FontSize = _config.Grid.TitleSize, Font = "robotocondensed-bold.ttf" }
                }, blockName);

                ui.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0.05 0.02", AnchorMax = "0.95 0.13" },
                    Text = { Text = item.Subtitle ?? "", Align = TextAnchor.UpperLeft, Color = _config.Grid.SubColor, FontSize = _config.Grid.SubSize }
                }, blockName);
            }

            if (_config.Pagination.Enabled)
            {
                int totalPages = Mathf.CeilToInt((float)_config.Items.Count / capacity);
                if (totalPages < 1) totalPages = 1;

                var bar = ui.Add(new CuiPanel
                {
                    Image = { Color = _config.Pagination.Bg },
                    RectTransform = { AnchorMin = _config.Pagination.AnchorMin, AnchorMax = _config.Pagination.AnchorMax }
                }, main, UI_PAGBAR);

                ui.Add(new CuiButton
                {
                    RectTransform = { AnchorMin = "0.01 0.10", AnchorMax = "0.06 0.90" },
                    Button = { Color = _config.Pagination.BtnBg, Command = "infoshow.page prev" },
                    Text = { Text = "<", Align = TextAnchor.MiddleCenter, Color = _config.Pagination.BtnTextColor, FontSize = 16 }
                }, bar);

                ui.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0.10 0.10", AnchorMax = "0.90 0.90" },
                    Text = { Text = $"PÁGINA {page + 1} DE {totalPages}", Align = TextAnchor.MiddleCenter, Color = _config.Pagination.TextColor, FontSize = _config.Pagination.TextSize, Font = "robotocondensed-regular.ttf" }
                }, bar);

                ui.Add(new CuiButton
                {
                    RectTransform = { AnchorMin = "0.94 0.10", AnchorMax = "0.99 0.90" },
                    Button = { Color = _config.Pagination.BtnBg, Command = "infoshow.page next" },
                    Text = { Text = ">", Align = TextAnchor.MiddleCenter, Color = _config.Pagination.BtnTextColor, FontSize = 16 }
                }, bar);
            }

            CuiHelper.AddUi(bp, ui);
            if (bp != null && bp.IsConnected) bp.SendConsoleCommand("lockcursor true");
        }

        private void CloseHUD(BasePlayer bp)
        {
            _isHudOpen.Remove(bp.userID);
            CuiHelper.DestroyUi(bp, UI_MAIN);
            CuiHelper.DestroyUi(bp, $"{UI_MAIN}.Blocker");
            HideLoading(bp);
            _lastItemByUser.Remove(bp.userID);
            if (bp != null && bp.IsConnected) bp.SendConsoleCommand("lockcursor false");

            ShowLauncher(bp);
        }
        #endregion

        #region Loading Screen (Painel Novo)
        private void PlayVideoWithLoading(BasePlayer bp, string url, ConfigData.GridItem item)
        {
            if (bp == null || string.IsNullOrEmpty(url)) return;

            _isLoading.Add(bp.userID);
            if (item != null) _lastItemByUser[bp.userID] = item;

            ShowLoading(bp);
            StartMarquee(bp);

            var safeUrl = url.Replace("\"", "%22");
            bp.SendConsoleCommand($"client.playvideo \"{safeUrl}\"");

            CancelEndTimer(bp);
            _endTimers[bp.userID] = timer.Once(_config.Loading.ProgressSeconds, () => {
                _isLoading.Remove(bp.userID);
            });
        }

        private void ShowLoading(BasePlayer bp)
        {
            HideLoading(bp);

            var ui = new CuiElementContainer();

            var scrim = ui.Add(new CuiPanel
            {
                Image = { Color = _config.Loading.ScrimColor },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", UI_LOADER);

            var box = ui.Add(new CuiPanel
            {
                Image = { Color = _config.Loading.Bg },
                RectTransform = { AnchorMin = _config.Loading.AnchorMin, AnchorMax = _config.Loading.AnchorMax }
            }, scrim, UI_LOADER_BOX);

            ui.Add(new CuiButton
            {
                RectTransform = { AnchorMin = "0.94 0.92", AnchorMax = "0.99 0.98" },
                Button = { Color = "0.8 0.2 0.2 0.9", Command = "infoshow.closeloading" },
                Text = { Text = "X", FontSize = 14, Align = TextAnchor.MiddleCenter }
            }, box);

            if (_lastItemByUser.TryGetValue(bp.userID, out var item) && item != null)
            {
                string prevName = $"{UI_LOADER}.Preview";
                ui.Add(new CuiElement {
                    Parent = box, Name = prevName,
                    Components = { new CuiRawImageComponent{ Color = "0 0 0 0" }, new CuiRectTransformComponent{ AnchorMin = _config.Loading.PreviewThumbAnchorMin, AnchorMax = _config.Loading.PreviewThumbAnchorMax } }
                });

                ui.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0.05 0.45", AnchorMax = "0.95 0.52" },
                    Text = { Text = item.Title ?? "", Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", FontSize = 16, Font = "robotocondensed-bold.ttf" }
                }, box);

                ui.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0.05 0.38", AnchorMax = "0.95 0.44" },
                    Text = { Text = item.Subtitle ?? "", Align = TextAnchor.MiddleCenter, Color = "0.7 0.7 0.7 1", FontSize = 12 }
                }, box);

                if (!string.IsNullOrEmpty(item.IconUrl))
                {
                    string imgName = $"{prevName}.img";
                    string png = null;
                    if (_ilReady && ImageLibrary != null) png = ImageLibrary.Call<string>("GetImage", HashKey(item.IconUrl));

                    if (!string.IsNullOrEmpty(png)) {
                        ui.Add(new CuiElement { Parent = prevName, Name = imgName, Components = { new CuiRawImageComponent { Png = png }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" } } });
                    } else {
                        ui.Add(new CuiElement { Parent = prevName, Name = imgName, Components = { new CuiRawImageComponent { Url = item.IconUrl }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" } } });
                        if (_ilReady) {
                            timer.Once(1f, () => {
                                if (_isLoading.Contains(bp.userID))
                                    AttachImageWithRetry(bp, prevName, item.IconUrl, "0 0", "1 1");
                            });
                        }
                    }
                }
            }

            ui.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "0.05 0.25", AnchorMax = "0.95 0.30" },
                Text = { Text = _config.Loading.LoadingTitle, Align = TextAnchor.MiddleCenter, Color = _config.Loading.TextColor, FontSize = 14, Font = "robotocondensed-bold.ttf" }
            }, box);

            var barBg = ui.Add(new CuiPanel
            {
                Image = { Color = _config.Loading.BarBgColor },
                RectTransform = { AnchorMin = "0.1 0.20", AnchorMax = "0.9 0.23" }
            }, box, UI_LOADER_BARBG);

            ui.Add(new CuiPanel
            {
                Image = { Color = _config.Loading.BarFillColor },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0.05 1" }
            }, barBg, UI_LOADER_BARFILL);

            ui.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "0.05 0.02", AnchorMax = "0.95 0.15" },
                Text = { Text = _config.Loading.WarningText, Align = TextAnchor.MiddleCenter, Color = _config.Loading.WarningColor, FontSize = 11 }
            }, box);

            CuiHelper.AddUi(bp, ui);
        }

        private void StartMarquee(BasePlayer bp)
        {
            CancelAnimTimer(bp);
            if (bp == null) return;
            float pos = 0f; bool forward = true;
            float chunk = _config.Loading.MarqueeChunkWidth;
            float step = _config.Loading.MarqueeSpeed;

            _animTimers[bp.userID] = timer.Repeat(_config.Loading.MarqueeTickSeconds, 0, () =>
            {
                if (bp == null || !bp.IsConnected) { CancelAnimTimer(bp); return; }
                float maxPos = 1f - chunk;
                pos += (forward ? step : -step);
                if (pos <= 0f) { pos = 0f; forward = true; }
                if (pos >= maxPos) { pos = maxPos; forward = false; }
                CuiHelper.DestroyUi(bp, UI_LOADER_BARFILL);
                var ui = new CuiElementContainer();
                ui.Add(new CuiPanel {
                    Image = { Color = _config.Loading.BarFillColor },
                    RectTransform = { AnchorMin = $"{pos} 0", AnchorMax = $"{pos + chunk} 1" }
                }, UI_LOADER_BARBG, UI_LOADER_BARFILL);
                CuiHelper.AddUi(bp, ui);
            });
        }

        private void HideLoading(BasePlayer bp)
        {
            CancelAnimTimer(bp); CancelEndTimer(bp);
            _isLoading.Remove(bp?.userID ?? 0);
            if (bp != null) CuiHelper.DestroyUi(bp, UI_LOADER);
        }

        private void CancelAnimTimer(BasePlayer bp) { if (bp != null && _animTimers.TryGetValue(bp.userID, out var t)) { t?.Destroy(); _animTimers.Remove(bp.userID); } }
        private void CancelEndTimer(BasePlayer bp) { if (bp != null && _endTimers.TryGetValue(bp.userID, out var t)) { t?.Destroy(); _endTimers.Remove(bp.userID); } }
        #endregion

        #region Helpers: Flash & ImageLibrary
        private void FlashThumb(BasePlayer bp, int absIndex)
        {
            if (bp == null) return;
            string blockName = $"{UI_MAIN}.Block.{absIndex}";
            string flashName = $"{blockName}.Flash";
            CuiHelper.DestroyUi(bp, flashName);
            var ui = new CuiElementContainer();
            ui.Add(new CuiPanel { Image = { Color = _config.Grid.HighlightTint }, RectTransform = { AnchorMin = $"0 {_config.Grid.ThumbBottom}", AnchorMax = "1 1" } }, blockName, flashName);
            CuiHelper.AddUi(bp, ui);
            timer.Once(_config.Grid.ClickFlashSeconds, () => CuiHelper.DestroyUi(bp, flashName));
        }

        private void CheckImageLibraryLoop()
        {
            if (ImageLibrary == null)
            {
                timer.Once(1f, CheckImageLibraryLoop);
                return;
            }

            bool ready = false;
            try { ready = (bool)ImageLibrary.Call("IsReady"); } catch {}

            if (!ready)
            {
                timer.Once(1f, CheckImageLibraryLoop);
                return;
            }

            _ilReady = true;
            PrecacheAllImages();
            ReloadActiveLaunchers();
        }

        private void ReloadActiveLaunchers()
        {
            foreach (var bp in BasePlayer.activePlayerList)
            {
                ShowLauncher(bp);
            }
        }

        private void PrecacheAllImages()
        {
            if (!_ilReady || ImageLibrary == null) return;
            void Add(string url) { if (!string.IsNullOrEmpty(url)) ImageLibrary.Call("AddImage", url, HashKey(url), 0UL); }
            Add(_config.Launcher.IconUrl);
            foreach (var item in _config.Items) Add(item.IconUrl);
        }

        private static string HashKey(string url)
        {
            using (var crc = new Crc32()) {
                var hash = crc.ComputeHash(Encoding.UTF8.GetBytes(url));
                return "is_" + BitConverter.ToUInt32(hash, 0);
            }
        }

        private void AttachImageWithRetry(BasePlayer bp, string parent, string url, string min, string max, int attempt = 0)
        {
            if (bp == null || string.IsNullOrEmpty(url)) return;
            string imgName = parent + ".img";
            string png = null;
            if (_ilReady && ImageLibrary != null) png = ImageLibrary.Call<string>("GetImage", HashKey(url));

            if (!string.IsNullOrEmpty(png)) {
                CuiHelper.DestroyUi(bp, imgName);
                var ui = new CuiElementContainer();
                ui.Add(new CuiElement { Parent = parent, Name = imgName, Components = { new CuiRawImageComponent { Png = png }, new CuiRectTransformComponent { AnchorMin = min, AnchorMax = max } } });
                CuiHelper.AddUi(bp, ui);
            } else {
                if (_ilReady && attempt < 10) {
                    ImageLibrary.Call("AddImage", url, HashKey(url), 0UL);
                    timer.Once(1f, () => {
                        if (parent.Contains("Block") && !_isHudOpen.Contains(bp.userID)) return;
                        if (parent.Contains("Loader") && !_isLoading.Contains(bp.userID)) return;

                        AttachImageWithRetry(bp, parent, url, min, max, attempt + 1);
                    });
                }
            }
        }
        #endregion

        #region Launcher
        private void ShowLauncher(BasePlayer bp)
        {
            if (bp == null || !bp.IsConnected) return;
            HideLauncher(bp);
            if (!_config.Launcher.Enabled) return;

            if (_isHudOpen.Contains(bp.userID)) return;

            _hasLauncherOpen.Add(bp.userID);

            var ui = new CuiElementContainer();

            var root = ui.Add(new CuiPanel
            {
                Image = { Color = _config.Launcher.Bg },
                RectTransform = { AnchorMin = _config.Launcher.AnchorMin, AnchorMax = _config.Launcher.AnchorMax },
                CursorEnabled = false
            }, "Hud.Menu", UI_LAUNCHER);

            string iconPanel = $"{UI_LAUNCHER}.IconPanel";
            ui.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, root, iconPanel);

            ui.Add(new CuiButton
            {
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Button = { Color = "0 0 0 0", Command = "infoshow.show" },
                Text = { Text = "" }
            }, root);

            string png = null;
            string launcherImgName = $"{iconPanel}.img";
            if (_ilReady && ImageLibrary != null) png = ImageLibrary.Call<string>("GetImage", HashKey(_config.Launcher.IconUrl));

            if (!string.IsNullOrEmpty(png))
            {
                ui.Add(new CuiElement { Parent = iconPanel, Name = launcherImgName, Components = { new CuiRawImageComponent { Png = png }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" } } });
            }
            else if (!string.IsNullOrEmpty(_config.Launcher.IconUrl))
            {
                ui.Add(new CuiElement { Parent = iconPanel, Name = launcherImgName, Components = { new CuiRawImageComponent { Url = _config.Launcher.IconUrl }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" } } });
                if (_ilReady) timer.Once(1.5f, () => AttachImageWithRetry(bp, iconPanel, _config.Launcher.IconUrl, "0 0", "1 1"));
            }

            CuiHelper.AddUi(bp, ui);
        }

        private void HideLauncher(BasePlayer bp)
        {
            if (bp != null)
            {
                CuiHelper.DestroyUi(bp, UI_LAUNCHER);
                _hasLauncherOpen.Remove(bp.userID);
            }
        }
        #endregion

        #region CRC32 Helper
        private class Crc32 : HashAlgorithm
        {
            private const uint DefaultPolynomial = 0xEDB88320u;
            private const uint DefaultSeed = 0xFFFFFFFFu;
            private uint _hash;
            private readonly uint _seed;
            private readonly uint[] _table;
            public Crc32() { _table = InitializeTable(DefaultPolynomial); _seed = DefaultSeed; Initialize(); }
            public override void Initialize() => _hash = _seed;
            protected override void HashCore(byte[] array, int ibStart, int cbSize) { _hash = CalculateHash(_table, _hash, array, ibStart, cbSize); }
            protected override byte[] HashFinal() { var hashBuffer = UInt32ToBigEndianBytes(~_hash); HashValue = hashBuffer; return hashBuffer; }
            public override int HashSize => 32;
            private static uint[] InitializeTable(uint polynomial) {
                var createTable = new uint[256];
                for (var i = 0; i < 256; i++) { var entry = (uint)i; for (var j = 0; j < 8; j++) entry = (entry & 1) == 1 ? (entry >> 1) ^ polynomial : entry >> 1; createTable[i] = entry; }
                return createTable;
            }
            private static uint CalculateHash(uint[] table, uint seed, IList<byte> buffer, int start, int size) {
                var crc = seed; for (var i = start; i < start + size; i++) unchecked { crc = (crc >> 1) ^ table[buffer[i] ^ (crc & 0xff)]; } return crc;
            }
            private static byte[] UInt32ToBigEndianBytes(uint x) => new[] { (byte)((x >> 24) & 0xff), (byte)((x >> 16) & 0xff), (byte)((x >> 8) & 0xff), (byte)(x & 0xff) };
        }
        #endregion
    }
}
