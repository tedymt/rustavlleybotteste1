using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("MPButtons", "Mestre Pardal", "2.9.0")]
    [Description("HUD de botões com ImageLibrary robusto e Layout Adaptativo.")]
    public class MPButtons : RustPlugin
    {
        [PluginReference] private Plugin ImageLibrary;

        private const string UI_PREFIX = "MPButtonsUI";     
        private const string PERM_TOGGLE = "mpbuttons.toggle";
        private const float WATCHDOG_SECONDS = 15f;
        private const int WATCHDOG_MAX_RETRY = 12;

        private ConfigData _config;
        private readonly Dictionary<ulong, bool> _visible = new();
        private bool _ilReady;
        private bool _bootDrawScheduled;

        private readonly Dictionary<string, int> _retry = new();

        private void EnsureRegister(string perm)
        {
            if (string.IsNullOrEmpty(perm)) return;
            if (!permission.PermissionExists(perm, this))
                permission.RegisterPermission(perm, this);
        }

        #region Config
        private class ButtonCfg
        {
            public string Nome = "Btn";
            public string Tipo = "imagem"; // imagem | toggle | botao
            
            public string AnchorMin = "0 0"; 
            public string AnchorMax = "0 0";

            public string Comando = "";
            public bool EnviarComoConsole = false;
            public string Permissao = "";

            public string CorBotao = "0 0 0 0";
            public string CorTexto = "1 1 1 1";
            public int FontSize = 16;
            public string Texto = "";

            public string UrlImagem = null;
            public string UrlImagemOn = null;
            public string UrlImagemOff = null;
            public string CorFundoOn = "0 0 0 0";
            public string CorFundoOff = "0 0 0 0";
            public string CorFundo = "0 0 0 0";
        }

        private class GlobalCfg
        {
            public bool VisivelPorPadrao = true;
            public string ToggleConsoleCommand = "mpbuttons";
            public string ToggleChatCommand = "mpbuttons";
            public string AutoBindKey = "";
            public int DefaultFontSize = 16;
            public bool LogDebug = false;

            public bool FixarLayout1920x1080 = true;
        }

        private class ConfigData
        {
            public GlobalCfg Global = new GlobalCfg();
            public List<ButtonCfg> Botoes = new List<ButtonCfg>();
        }

        protected override void LoadDefaultConfig()
        {
            _config = new ConfigData
            {
                Global = new GlobalCfg
                {
                    VisivelPorPadrao = true,
                    ToggleConsoleCommand = "mpbuttons",
                    ToggleChatCommand = "mpbuttons",
                    AutoBindKey = "",
                    DefaultFontSize = 16,
                    LogDebug = false,
                    FixarLayout1920x1080 = true
                },
                Botoes = new List<ButtonCfg>
                {
                    new ButtonCfg {
                        Nome = "ToggleUI", Tipo = "toggle",
                        AnchorMin = "0.25 0.93", AnchorMax = "0.29 0.97",
                        Permissao = PERM_TOGGLE,
                        UrlImagemOn = "https://i.ibb.co/yk4x8d0/pause.png",
                        UrlImagemOff = "https://i.ibb.co/yk4x8d0/play.png",
                        CorFundoOn = "1 1 1 0",
                        CorFundoOff = "1 1 1 0"
                    }
                }
            };
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<ConfigData>();
            if (_config?.Botoes == null) LoadDefaultConfig();
            
            if (_config.Global == null) _config.Global = new GlobalCfg();
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);
        #endregion

        #region Lifecycle
        private void Init()
        {
            EnsureRegister(PERM_TOGGLE);
            if (_config?.Botoes != null)
            {
                foreach (var b in _config.Botoes)
                {
                    if (!string.IsNullOrEmpty(b.Permissao)) EnsureRegister(b.Permissao);
                    if (b.FontSize <= 0) b.FontSize = _config.Global.DefaultFontSize;
                }
            }
        }

        private void OnServerInitialized()
        {
            WaitForImageLibrary(() =>
            {
                _ilReady = true;
                PrecacheImages();
                RefreshAll();
                timer.Every(WATCHDOG_SECONDS, WatchdogTick);
            });

            if (!_bootDrawScheduled)
            {
                _bootDrawScheduled = true;
                timer.Once(4f, RefreshAll);
            }
        }

        private void OnPlayerConnected(BasePlayer p) => timer.Once(1f, () =>
        {
            if (p == null || !p.IsConnected) return;
            if (!_visible.ContainsKey(p.userID))
                _visible[p.userID] = _config.Global.VisivelPorPadrao;
            AutoBind(p);
            Draw(p);
        });

        private void OnPlayerRespawned(BasePlayer p) => Draw(p);
        private void OnPlayerDisconnected(BasePlayer p) => DestroyUI(p);
        private void Unload()
        {
            foreach (var p in BasePlayer.activePlayerList) DestroyUI(p);
        }

        private void RefreshAll()
        {
            foreach (var p in BasePlayer.activePlayerList)
            {
                if (!_visible.ContainsKey(p.userID))
                    _visible[p.userID] = _config.Global.VisivelPorPadrao;
                AutoBind(p);
                Draw(p);
            }
        }
        #endregion

        #region ImageLibrary
        private void WaitForImageLibrary(Action onReady)
        {
            bool Ready()
            {
                if (ImageLibrary == null) return false;
                try { return ImageLibrary.Call("IsReady") is bool b && b; } catch { return false; }
            }

            if (Ready()) { onReady?.Invoke(); return; }

            bool loggedWaiting = false;
            bool called = false;
            int tries = 0;

            timer.Repeat(1f, 180, () =>
            {
                if (called) return;
                tries++;
                if (!loggedWaiting && tries == 1)
                {
                    PrintWarning("[MPButtons] Aguardando ImageLibrary...");
                    loggedWaiting = true;
                }
                if (!Ready()) return;
                called = true;
                if (_config.Global.LogDebug) Puts($"[MPButtons] ImageLibrary pronto após {tries}s.");
                onReady?.Invoke();
            });
        }

        private static string HashKey(string url)
        {
            var bytes = Encoding.UTF8.GetBytes(url ?? "");
            using var crc32 = new Crc32();
            return $"mpb_{BitConverter.ToUInt32(crc32.ComputeHash(bytes), 0)}";
        }

        private void PrecacheImages()
        {
            if (ImageLibrary == null || _config?.Botoes == null) return;
            void Enqueue(string url)
            {
                if (string.IsNullOrEmpty(url)) return;
                var key = HashKey(url);
                _retry[key] = 0;
                try { ImageLibrary.Call("AddImage", url, key, 0UL); } catch { }
            }
            foreach (var b in _config.Botoes)
            {
                Enqueue(b.UrlImagem); Enqueue(b.UrlImagemOn); Enqueue(b.UrlImagemOff);
            }
        }

        private bool TryGetPng(string url, out string png)
        {
            png = null;
            if (ImageLibrary == null || string.IsNullOrEmpty(url)) return false;
            var key = HashKey(url);
            try
            {
                png = ImageLibrary.Call("GetImage", key) as string;
                if (!string.IsNullOrEmpty(png)) return true;
                ImageLibrary.Call("AddImage", url, key, 0UL);
            }
            catch {}
            return false;
        }

        private void WatchdogTick()
        {
            if (ImageLibrary == null || _config?.Botoes == null) return;
            void Check(string url)
            {
                if (string.IsNullOrEmpty(url)) return;
                var key = HashKey(url);
                if (!TryGetPng(url, out _))
                {
                    _retry.TryGetValue(key, out var r);
                    if (r < WATCHDOG_MAX_RETRY)
                    {
                        _retry[key] = r + 1;
                        try { ImageLibrary.Call("AddImage", url, key, 0UL); } catch { }
                    }
                }
            }
            foreach (var b in _config.Botoes) { Check(b.UrlImagem); Check(b.UrlImagemOn); Check(b.UrlImagemOff); }
        }
        #endregion

        #region Helper de Posicionamento
        private void CalculateAdaptivePosition(string aMinStr, string aMaxStr, out string anchorMin, out string anchorMax, out string offsetMin, out string offsetMax)
        {
            anchorMin = aMinStr;
            anchorMax = aMaxStr;
            offsetMin = "0 0";
            offsetMax = "0 0";

            if (!_config.Global.FixarLayout1920x1080) return;

            try
            {
                var minParts = aMinStr.Split(' ');
                var maxParts = aMaxStr.Split(' ');
                float minX = float.Parse(minParts[0]);
                float minY = float.Parse(minParts[1]);
                float maxX = float.Parse(maxParts[0]);
                float maxY = float.Parse(maxParts[1]);

                float refW = 1920f;
                float refH = 1080f;

                float pixelMinX = minX * refW;
                float pixelMinY = minY * refH;
                float pixelMaxX = maxX * refW;
                float pixelMaxY = maxY * refH;

                float centerX = refW / 2f;
                float centerY = refH / 2f;

                
                anchorMin = "0.5 0.0";
                anchorMax = "0.5 0.0";

                
                float offMinX = pixelMinX - centerX;
                float offMinY = pixelMinY; // ancora Y é 0
                
                float offMaxX = pixelMaxX - centerX;
                float offMaxY = pixelMaxY; // ancora Y é 0

                offsetMin = $"{offMinX:0.##} {offMinY:0.##}";
                offsetMax = $"{offMaxX:0.##} {offMaxY:0.##}";
            }
            catch
            {
                // Em caso de erro de parse, mantém o original
            }
        }
        #endregion

        #region UI
        private void DestroyUI(BasePlayer p)
        {
            if (p == null || _config?.Botoes == null) return;
            foreach (var b in _config.Botoes) CuiHelper.DestroyUi(p, $"{UI_PREFIX}_{b.Nome}");
        }

        private void Draw(BasePlayer p)
        {
            if (p == null || _config?.Botoes == null) return;
            DestroyUI(p);

            bool visible = _visible.TryGetValue(p.userID, out var v) ? v : _config.Global.VisivelPorPadrao;
            var ui = new CuiElementContainer();

            foreach (var b in _config.Botoes)
            {
                if (!string.IsNullOrEmpty(b.Permissao) && !permission.UserHasPermission(p.UserIDString, b.Permissao))
                    continue;

                if (b.Tipo != "toggle" && !visible) continue;

                string parent = "Hud.Menu"; 
                string name = $"{UI_PREFIX}_{b.Nome}";

                string url = b.UrlImagem;
                string bg = b.CorFundo;

                if (b.Tipo == "toggle")
                {
                    if (visible) { url = b.UrlImagemOn ?? b.UrlImagem ?? b.UrlImagemOff; bg = b.CorFundoOn ?? b.CorFundo; }
                    else { url = b.UrlImagemOff ?? b.UrlImagem ?? b.UrlImagemOn; bg = b.CorFundoOff ?? b.CorFundo; }
                }

                CalculateAdaptivePosition(b.AnchorMin, b.AnchorMax, out string aMin, out string aMax, out string oMin, out string oMax);

                ui.Add(new CuiPanel
                {
                    Image = { Color = bg ?? "0 0 0 0" },
                    RectTransform = { AnchorMin = aMin, AnchorMax = aMax, OffsetMin = oMin, OffsetMax = oMax },
                    CursorEnabled = false
                }, parent, name);

                var fullComp = new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" };

                if (!string.IsNullOrEmpty(url))
                {
                    if (_ilReady && TryGetPng(url, out var png))
                        ui.Add(new CuiElement { Parent = name, Components = { new CuiRawImageComponent { Png = png, Color = "1 1 1 1" }, fullComp } });
                    else
                        ui.Add(new CuiElement { Parent = name, Components = { new CuiRawImageComponent { Url = url, Color = "1 1 1 1" }, fullComp } });
                }

                if (b.Tipo == "toggle")
                {
                    ui.Add(new CuiButton
                    {
                        Button = { Color = "0 0 0 0", Command = _config.Global.ToggleConsoleCommand },
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                        Text = { Text = "", FontSize = 0 }
                    }, name);
                }
                else if (b.Tipo == "imagem")
                {
                    if (!string.IsNullOrEmpty(b.Comando))
                    {
                        var cmd = b.EnviarComoConsole ? b.Comando : $"chat.say {b.Comando}";
                        ui.Add(new CuiButton { Button = { Color = "0 0 0 0", Command = cmd }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, Text = { Text = "" } }, name);
                    }
                }
                else 
                {
                    var cmd = b.EnviarComoConsole ? b.Comando : $"chat.say {b.Comando}";
                    ui.Add(new CuiButton
                    {
                        Button = { Color = b.CorBotao, Command = cmd },
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                        Text = { Text = b.Texto, FontSize = b.FontSize, Align = TextAnchor.MiddleCenter, Color = b.CorTexto }
                    }, name);
                }
            }

            CuiHelper.AddUi(p, ui);
        }
        #endregion

        #region Commands
        [ConsoleCommand("mpbuttons")]
        private void CmdToggle(ConsoleSystem.Arg arg) { if (arg.Player() != null) DoToggle(arg.Player()); }

        [ChatCommand("mpbuttons")]
        private void CmdToggleChat(BasePlayer player, string command, string[] args) { if (player != null) DoToggle(player); }

        private void DoToggle(BasePlayer p)
        {
            if (!permission.UserHasPermission(p.UserIDString, PERM_TOGGLE))
            {
                p.ChatMessage("<color=#ffdb00>Sem permissão para alternar a HUD.</color>");
                return;
            }
            bool current = _visible.TryGetValue(p.userID, out var v) ? v : _config.Global.VisivelPorPadrao;
            _visible[p.userID] = !current;
            Draw(p);
        }

        [ConsoleCommand("mpbuttons.reloadimg")]
        private void CmdReloadImg(ConsoleSystem.Arg arg)
        {
            PrecacheImages();
            RefreshAll();
            Puts("[MPButtons] Imagens re-enfileiradas.");
        }
        #endregion

        #region Utils & CRC32
        private void AutoBind(BasePlayer p)
        {
            if (!string.IsNullOrEmpty(_config.Global.AutoBindKey))
                p.SendConsoleCommand($"bind {_config.Global.AutoBindKey} {_config.Global.ToggleConsoleCommand}");
        }

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
            protected override void HashCore(byte[] array, int ibStart, int cbSize) => _hash = CalculateHash(_table, _hash, array, ibStart, cbSize);
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
                for (uint i = 0; i < 256; i++)
                {
                    uint entry = i;
                    for (int j = 0; j < 8; j++) entry = (entry & 1) == 1 ? (entry >> 1) ^ polynomial : entry >> 1;
                    createTable[i] = entry;
                }
                return createTable;
            }
            private static uint CalculateHash(uint[] table, uint seed, IList<byte> buffer, int start, int size)
            {
                var crc = seed;
                for (int i = start; i < start + size; i++) crc = (crc >> 8) ^ table[(buffer[i] ^ crc) & 0xFF];
                return crc;
            }
            private static byte[] UInt32ToBigEndianBytes(uint x) => new[] { (byte)((x >> 24) & 0xff), (byte)((x >> 16) & 0xff), (byte)((x >> 8) & 0xff), (byte)(x & 0xff) };
        }
        #endregion
    }
}