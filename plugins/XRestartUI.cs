using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("XRestartUI", "Monster", "1.0.4")]
    class XRestartUI : RustPlugin
    {
        private const bool LanguageEnglish = true;

        private Coroutine _coroutine;
        private bool _pluginsUnloaded;

        #region Configuration

        private RestartConfig config;

        private class RestartConfig
        {
            internal class GeneralSetting
            {
                [JsonProperty(LanguageEnglish ? "Use chat messages" : "Использовать сообщения в чате")] public bool Message;
                [JsonProperty(LanguageEnglish ? "Use UI notifications" : "Использовать UI уведомления")] public bool UI;
                [JsonProperty(LanguageEnglish ? "Use GameTip notifications" : "Использовать GameTip уведомления")] public bool GameTip;
                [JsonProperty(LanguageEnglish ? "Use the tick effect" : "Использовать эффект тика")] public bool EffectTickUse;
                [JsonProperty(LanguageEnglish ? "Use warning effect" : "Использовать эффект предупреждения")] public bool EffectWarningUse;
                [JsonProperty(LanguageEnglish ? "Tick effect used" : "Используемый эффект тика")] public string EffectTick;
                [JsonProperty(LanguageEnglish ? "Warning effect used" : "Используемый эффект предупреждения")] public string EffectWarning;
                [JsonProperty(LanguageEnglish ? "SteamID of the profile for the custom avatar" : "SteamID профиля для кастомной аватарки")] public ulong SteamID;
                [JsonProperty(LanguageEnglish ? "Skip restart if there are more than N players online. [ Restart warnings are disabled ]" : "Пропустить рестарт если онлайн игроков больше N. [ Предупреждения о рестарте отключены ]")] public bool SkipRestart;
                [JsonProperty(LanguageEnglish ? "Number of online players - to skip restart" : "Кол-во онлайн игроков - для пропуска рестарта")] public int OnlineToSkip;
            }

            internal class GUISetting
            {
                public string AnchorMin = "0 0.85";
                public string AnchorMax = "1 0.85";
                public string OffsetMin = "0 -25";
                public string OffsetMax = "0 25";
            }

            [JsonProperty(LanguageEnglish ? "General settings" : "Общие настройки")]
            public GeneralSetting Setting = new GeneralSetting();
            [JsonProperty(LanguageEnglish ? "GUI settings" : "Настройки GUI")]
            public GUISetting GUI = new GUISetting();
            [JsonProperty(LanguageEnglish ? "List of unique names(keys) of restart reasons - [ Setting up text in lang ]" : "Список уникальных имен(ключей) причин рестарта - [ Настройка текста в lang ]")]
            public List<string> ListMessage = new List<string>();
            [JsonProperty(LanguageEnglish ? "Configuring scheduled restarts [ Any command can be scheduled at any time ]" : "Настройка рестартов по расписанию [ Можно запланировать любую команду в любое время ]")]
            public Dictionary<string, string> ARestart = new Dictionary<string, string>();
            [JsonProperty(LanguageEnglish ? "Setting warnings N minutes before restart" : "Настройка предупреждений за N минут до рестарта")]
            public List<int> Warning = new List<int>();

            // NOVO: plugins para descarregar e tempo antes do restart
            [JsonProperty(LanguageEnglish ? "Plugins to unload before restart" : "Плагины для выгрузки перед рестартом")]
            public List<string> PluginsToUnload = new List<string>();

            [JsonProperty(LanguageEnglish ? "Seconds before restart to unload plugins" : "За сколько секунд до рестарта выгружать плагины")]
            public int UnloadPluginsBeforeSeconds = 300;

            public static RestartConfig GetNewConfiguration()
            {
                return new RestartConfig
                {
                    Setting = new GeneralSetting
                    {
                        Message = true,
                        UI = true,
                        GameTip = false,
                        EffectTickUse = true,
                        EffectWarningUse = true,
                        EffectTick = "assets/bundled/prefabs/fx/notice/loot.drag.dropsuccess.fx.prefab",
                        EffectWarning = "assets/bundled/prefabs/fx/item_unlock.prefab",
                        SteamID = 0,
                        SkipRestart = false,
                        OnlineToSkip = 100
                    },
                    GUI = new GUISetting
                    {
                        AnchorMin = "0 0.85",
                        AnchorMax = "1 0.85",
                        OffsetMin = "0 -25",
                        OffsetMax = "0 25"
                    },
                    ListMessage = new List<string>
                    {
                        "M_DEFAULT", "M_1", "M_2"
                    },
                    ARestart = new Dictionary<string, string>
                    {
                        ["08:00"] = "restart 300",
                        ["21:00"] = "restart 300 M_1"
                    },
                    Warning = new List<int>
                    {
                        60,
                        45,
                        30,
                        15,
                        10,
                        5
                    },
                    // Defaults que você pediu
                    PluginsToUnload = new List<string>
                    {
                        "Backpacks",
                        "PortableLocker"
                    },
                    UnloadPluginsBeforeSeconds = 300
                };
            }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<RestartConfig>();
            }
            catch
            {
                PrintWarning(LanguageEnglish ? "Configuration read error! Creating a default configuration!" : "Ошибка чтения конфигурации! Создание дефолтной конфигурации!");
                LoadDefaultConfig();
            }

            SaveConfig();
        }
        protected override void LoadDefaultConfig() => config = RestartConfig.GetNewConfiguration();
        protected override void SaveConfig() => Config.WriteObject(config);

        #endregion

        private const double _offset = 41260528.1;

        #region Hooks

        private void OnServerInitialized()
        {
            PrintWarning("\n-----------------------------\n" +
            "     Author - Monster\n" +
            "     VK - vk.com/idannopol\n" +
            "     Discord - Monster#4837\n" +
            "-----------------------------");

            InitializeLang();
            timer.Every(60, () => AutoRestart());
        }

        private void Unload()
        {
            if (_coroutine != null)
                ServerMgr.Instance.StopCoroutine(_coroutine);

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, ".TimerGUI");
                player.SendConsoleCommand("gametip.hidegametip");
            }
        }

        private void AutoRestart()
        {
            string time = DateTime.Now.ToString("t");

            if (!config.Setting.SkipRestart)
                foreach (int minute in config.Warning)
                {
                    string newtime = DateTime.Now.AddMinutes(minute).ToString("t");

                    if (config.ARestart.ContainsKey(newtime) && config.ARestart[newtime].Contains("restart"))
                    {
                        TimeSpan t = TimeSpan.FromSeconds(minute * 60);

                        if (config.Setting.Message)
                        {
                            BasePlayer.activePlayerList.ToList().ForEach(x => Player.Reply(x, string.Format(lang.GetMessage("CHAT_WARNING_RESTART", this, x.UserIDString), t.Hours, t.Minutes, t.Seconds), config.Setting.SteamID));
                            PrintWarning(LanguageEnglish ? string.Format("SERVER RESTART WILL START IN: {0} HR. {1} MIN. {2} SEC.", t.Hours, t.Minutes, t.Seconds) : string.Format("РЕСТАРТ СЕРВЕРА НАЧНЕТСЯ ЧЕРЕЗ: {0} ЧАС. {1} МИН. {2} СЕК.", t.Hours, t.Minutes, t.Seconds));
                        }

                        foreach (BasePlayer player in BasePlayer.activePlayerList)
                        {
                            if (config.Setting.UI)
                                WarningGUI(player, t);
                            if (config.Setting.GameTip)
                                WarningGameTip(player, t);

                            if (config.Setting.EffectWarningUse)
                                EffectNetwork.Send(new Effect(config.Setting.EffectWarning, player, 0, new Vector3(), new Vector3()), player.Connection);
                        }
                        break;
                    }
                }

            if (config.ARestart.ContainsKey(time))
            {
                if (!(config.Setting.SkipRestart && config.ARestart[time].Contains("restart") && BasePlayer.activePlayerList.Count > config.Setting.OnlineToSkip))
                    Server.Command(config.ARestart[time]);
                else
                    PrintError(LanguageEnglish ? $"RESTART CANCELED! ONLINE PLAYERS MORE THAN {config.Setting.OnlineToSkip}!" : $"РЕСТАРТ ОТМЕНЕН! ОНЛАЙН ИГРОКОВ БОЛЕЕ {config.Setting.OnlineToSkip}!");
            }
        }

        private object OnServerRestart(string message, int seconds)
        {
            if (_coroutine != null)
                ServerMgr.Instance.StopCoroutine(_coroutine);

            if (seconds > 0)
            {
                message = String.IsNullOrEmpty(message) ? "M_DEFAULT" : message;
                _pluginsUnloaded = false;
                _coroutine = ServerMgr.Instance.StartCoroutine(Restart(message, seconds));
            }
            else if (_coroutine != null)
            {
                if (config.Setting.Message)
                {
                    BasePlayer.activePlayerList.ToList().ForEach(x => Player.Reply(x, lang.GetMessage("CANCELED_RESTART", this, x.UserIDString), config.Setting.SteamID));
                    PrintWarning(LanguageEnglish ? "RESTART CANCELED" : "РЕСТАРТ ОТМЕНЕН");
                }
                Unload();

                _coroutine = null;
            }

            return true;
        }

        private IEnumerator Restart(string message, int seconds)
        {
            if (config.Setting.Message)
            {
                TimeSpan t = TimeSpan.FromSeconds(seconds);
                BasePlayer.activePlayerList.ToList().ForEach(x => Player.Reply(x, string.Format(lang.GetMessage("CHAT_RESTART", this, x.UserIDString), t.Hours, t.Minutes, t.Seconds, lang.GetMessage(message, this, x.UserIDString)), config.Setting.SteamID));
                PrintWarning(LanguageEnglish ? string.Format("SERVER RESTART THROUGH: {0} HR. {1} MIN. {2} SEC.   -   {3}", t.Hours, t.Minutes, t.Seconds, message) : string.Format("РЕСТАРТ СЕРВЕРА ЧЕРЕЗ: {0} ЧАС. {1} МИН. {2} СЕК.   -   {3}", t.Hours, t.Minutes, t.Seconds, message));
            }

            for (int i = 0; i <= seconds; i++)
            {
                int sec = seconds - i;

                // NOVO: descarregar plugins N segundos antes do restart
                if (!_pluginsUnloaded &&
                    config.UnloadPluginsBeforeSeconds > 0 &&
                    sec <= config.UnloadPluginsBeforeSeconds &&
                    config.PluginsToUnload != null &&
                    config.PluginsToUnload.Count > 0)
                {
                    _pluginsUnloaded = true;
                    foreach (var pluginName in config.PluginsToUnload)
                    {
                        if (string.IsNullOrEmpty(pluginName))
                            continue;

                        PrintWarning($"[XRestartUI] Unloading plugin before restart: {pluginName}");
                        Server.Command($"o.unload {pluginName}");
                    }
                }

                foreach (BasePlayer player in BasePlayer.activePlayerList)
                {
                    if (config.Setting.UI)
                        TimerGUI(player, message, sec);
                    if (config.Setting.GameTip)
                        TimerGameTip(player, message, sec);

                    if (config.Setting.EffectTickUse)
                        EffectNetwork.Send(new Effect(config.Setting.EffectTick, player, 0, new Vector3(), new Vector3()), player.Connection);
                }

                yield return CoroutineEx.waitForSeconds(1);
            }

            // GARANTIR SAVE ANTES DO QUIT → evita duplicação com Backpacks/PortableLocker
            PrintWarning("[XRestartUI] Forcing server.save and oxide.save before quit to avoid dupes.");
            ConsoleSystem.Run(ConsoleSystem.Option.Server, "server.save", Array.Empty<object>());
            ConsoleSystem.Run(ConsoleSystem.Option.Server, "oxide.save", Array.Empty<object>());

            yield return CoroutineEx.waitForSeconds(2);

            BasePlayer.activePlayerList.ToList().ForEach(x => x.Kick("Server Restarting"));

            yield return CoroutineEx.waitForSeconds(2);

            ConsoleSystem.Run(ConsoleSystem.Option.Server, "quit", Array.Empty<object>());

            yield break;
        }

        #endregion

        #region GUI

        private void TimerGUI(BasePlayer player, string message, int seconds)
        {
            TimeSpan t = TimeSpan.FromSeconds(seconds);

            CuiElementContainer container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = config.GUI.AnchorMin, AnchorMax = config.GUI.AnchorMax, OffsetMin = config.GUI.OffsetMin, OffsetMax = config.GUI.OffsetMax },
                Image = { Color = "0.217 0.221 0.209 0.4", Material = "assets/icons/greyout.mat" }
            }, "Hud", ".TimerGUI", ".TimerGUI");

            container.Add(new CuiElement
            {
                Parent = ".TimerGUI",
                Components =
                {
                    new CuiTextComponent { Text = string.Format(lang.GetMessage("RESTART", this, player.UserIDString), t.Hours, t.Minutes, t.Seconds, lang.GetMessage(message, this, player.UserIDString)), Align = TextAnchor.MiddleCenter, Font = "robotocondensed-regular.ttf", FontSize = 18, Color = "1 1 1 0.75" },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMax = "0 0" },
                    new CuiOutlineComponent { Color = "0 0 0 1", Distance = "0.2 -0.2" }
                }
            });

            CuiHelper.AddUi(player, container);
        }

        private void WarningGUI(BasePlayer player, TimeSpan time)
        {
            CuiElementContainer container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                FadeOut = 2.5f,
                RectTransform = { AnchorMin = config.GUI.AnchorMin, AnchorMax = config.GUI.AnchorMax, OffsetMin = config.GUI.OffsetMin, OffsetMax = config.GUI.OffsetMax },
                Image = { Color = "0.217 0.221 0.209 0.4", Material = "assets/icons/greyout.mat", FadeIn = 2.5f }
            }, "Hud", ".TimerGUI", ".TimerGUI");

            container.Add(new CuiElement
            {
                Parent = ".TimerGUI",
                Name = ".TimerGUIText",
                FadeOut = 2.5f,
                Components =
                {
                    new CuiTextComponent { Text = string.Format(lang.GetMessage("WARNING_RESTART", this, player.UserIDString), time.Hours, time.Minutes, time.Seconds), Align = TextAnchor.MiddleCenter, Font = "robotocondensed-regular.ttf", FontSize = 20, Color = "1 1 1 0.75", FadeIn = 2.5f },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMax = "0 0" },
                    new CuiOutlineComponent { Color = "0 0 0 1", Distance = "0.2 -0.2" }
                }
            });

            CuiHelper.AddUi(player, container);
            player.Invoke(() => { CuiHelper.DestroyUi(player, ".TimerGUI"); CuiHelper.DestroyUi(player, ".TimerGUIText"); }, 15.0f);
        }

        #endregion

        #region Message

        private void TimerGameTip(BasePlayer player, string message, int seconds)
        {
            TimeSpan t = TimeSpan.FromSeconds(seconds);
            player.SendConsoleCommand("gametip.showtoast", new object[] { 0, string.Format(lang.GetMessage("RESTART", this, player.UserIDString), t.Hours, t.Minutes, t.Seconds, lang.GetMessage(message, this, player.UserIDString)) });
        }

        private void WarningGameTip(BasePlayer player, TimeSpan time)
        {
            player.SendConsoleCommand("gametip.showtoast", new object[] { 1, string.Format(lang.GetMessage("WARNING_RESTART", this, player.UserIDString), time.Hours, time.Minutes, time.Seconds) });
            timer.Once(15f, () => player.SendConsoleCommand("gametip.hidegametip"));
        }

        #endregion

        #region Lang

        private void InitializeLang()
        {
            Dictionary<string, string> langen = new Dictionary<string, string>
            {
                ["RESTART"] = "SERVER RESTART THROUGH: {0} HR. {1} MIN. {2} SEC.\n<size=12>{3}</size>",
                ["CHAT_RESTART"] = "<color=#a3f0ff>SERVER RESTART THROUGH</color>: <color=orange>{0} HR. {1} MIN. {2} SEC.</color>\n<size=10>{3}</size>",
                ["WARNING_RESTART"] = "SERVER RESTART WILL START IN: {0} HR. {1} MIN. {2} SEC.",
                ["CHAT_WARNING_RESTART"] = "<size=13><color=#a3f0ff>SERVER RESTART WILL START IN</color>: <color=orange>{0} HR. {1} MIN. {2} SEC.</color></size>",
                ["CANCELED_RESTART"] = "<color=#a3f0ff>RESTART CANCELED</color>"
            };

            Dictionary<string, string> langru = new Dictionary<string, string>
            {
                ["RESTART"] = "РЕСТАРТ СЕРВЕРА ЧЕРЕЗ: {0} ЧАС. {1} МИН. {2} СЕК.\n<size=12>{3}</size>",
                ["CHAT_RESTART"] = "<color=#a3f0ff>РЕСТАРТ СЕРВЕРА ЧЕРЕЗ</color>: <color=orange>{0} ЧАС. {1} МИН. {2} СЕК.</color>\n<size=10>{3}</size>",
                ["WARNING_RESTART"] = "РЕСТАРТ СЕРВЕРА НАЧНЕТСЯ ЧЕРЕЗ: {0} ЧАС. {1} МИН. {2} СЕК.",
                ["CHAT_WARNING_RESTART"] = "<size=13><color=#a3f0ff>РЕСТАРТ СЕРВЕРА НАЧНЕТСЯ ЧЕРЕЗ</color>: <color=orange>{0} ЧАС. {1} МИН. {2} СЕК.</color></size>",
                ["CANCELED_RESTART"] = "<color=#a3f0ff>РЕСТАРТ ОТМЕНЕН</color>"
            };

            Dictionary<string, string> languk = new Dictionary<string, string>
            {
                ["RESTART"] = "РЕСТАРТ СЕРВЕРА ЧЕРЕЗ: {0} ГОД. {1} ХВ. {2} СЕК.\n<size=12>{3}</size>",
                ["CHAT_RESTART"] = "<color=#a3f0ff>РЕСТАРТ СЕРВЕРА ЧЕРЕЗ</color>: <color=orange>{0} ГОД. {1} ХВ. {2} СЕК.</color>\n<size=10>{3}</size>",
                ["WARNING_RESTART"] = "РЕСТАРТ СЕРВЕРА РОЗПОЧНЕТЬСЯ ЧЕРЕЗ: {0} ГОД. {1} ХВ. {2} СЕК.",
                ["CHAT_WARNING_RESTART"] = "<size=13><color=#a3f0ff>РЕСТАРТ СЕРВЕРА РОЗПОЧНЕТЬСЯ ЧЕРЕЗ</color>: <color=orange>{0} ГОД. {1} ХВ. {2} СЕК.</color></size>",
                ["CANCELED_RESTART"] = "<color=#a3f0ff>РЕСТАРТ СКАСОВАНО</color>"
            };

            Dictionary<string, string> langes = new Dictionary<string, string>
            {
                ["RESTART"] = "REINICIAR EL SERVIDOR A TRAVÉS: {0} HR. {1} MIN. {2} SEG.\n<size=12>{3}</size>",
                ["CHAT_RESTART"] = "<color=#a3f0ff>REINICIAR EL SERVIDOR A TRAVÉS</color>: <color=orange>{0} HR. {1} MIN. {2} SEG.</color>\n<size=10>{3}</size>",
                ["WARNING_RESTART"] = "EL REINICIO DEL SERVIDOR COMENZARÁ EN: {0} HR. {1} MIN. {2} SEG.",
                ["CHAT_WARNING_RESTART"] = "<size=13><color=#a3f0ff>EL REINICIO DEL SERVIDOR COMENZARÁ EN</color>: <color=orange>{0} HR. {1} MIN. {2} SEG.</color></size>",
                ["CANCELED_RESTART"] = "<color=#a3f0ff>REINICIO CANCELADO</color>"
            };

            foreach (var message in config.ListMessage)
            {
                langen.Add(message, "RESTART RESTART RESTART");
                langru.Add(message, "РЕСТАРТ РЕСТАРТ РЕСТАРТ");
                languk.Add(message, "РЕСТАРТ РЕСТАРТ РЕСТАРТ");
                langes.Add(message, "REINICIAR REINICIAR REINICIAR");
            }

            lang.RegisterMessages(langen, this);
            lang.RegisterMessages(langru, this, "ru");
            lang.RegisterMessages(languk, this, "uk");
            lang.RegisterMessages(langes, this, "es-ES");
        }

        #endregion
    }
}
