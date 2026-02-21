using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;
using System;

namespace Oxide.Plugins
{
    [Info("Damage UI", "Math", "2.3.2")]
    [Description("Mostra uma barra de vida para todas as entidades de combate.")]
    class DamageUI : RustPlugin
    {
        private const string UIPanelName = "UIHealthBarPanel";

        #region Configuração e Dados
        class Configuration
        {
            public float HideDelay = 5.0f;
            public BarConfig MainBar = new BarConfig();
            public BarConfig RotorBar = new BarConfig();
            public UIPositions Positions = new UIPositions();
            public List<string> BlacklistedEntityPrefabs = new List<string>();

            public class BarConfig
            {
                public string BackgroundColor = "0.2 0.2 0.2 0.8";
                public string HealthColor_High = "0 0.8 0 1";
                public string HealthColor_Medium = "1 1 0 1";
                public string HealthColor_Low = "1 0 0 1";
                public int FontSize = 12;
                public string TextShadowColor = "0 0 0 0.8";
            }

            public class UIPositions
            {
                public string MainBarAnchorMin = "0.37 0.90";
                public string MainBarAnchorMax = "0.63 0.92";
                public string RotorBarAnchorMin = "0.37 0.87";
                public string RotorBarAnchorMax = "0.498 0.89";
                public string RotorBarAnchorMin2 = "0.502 0.87";
                public string RotorBarAnchorMax2 = "0.63 0.89";
            }
        }
        #endregion

        private Configuration config;
        private readonly Dictionary<BasePlayer, long> lastHits = new Dictionary<BasePlayer, long>();
        private readonly List<ulong> disabledHudPlayers = new List<ulong>();

        #region Hooks do Oxide
        void Init()
        {
            Puts("Plugin 'DamageUI v2.3.2' carregado com sucesso.");
            AddCovalenceCommand("dhud", "CmdHud");

            timer.Every(1f, () =>
            {
                var playersToDelete = new List<BasePlayer>();
                long currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                foreach (var entry in lastHits)
                {
                    var player = entry.Key;
                    if (player == null || !player.IsConnected || (currentTime - entry.Value) > config.HideDelay)
                    {
                        if (player != null) CuiHelper.DestroyUi(player, UIPanelName);
                        playersToDelete.Add(player);
                    }
                }

                foreach (var player in playersToDelete) lastHits.Remove(player);
            });
        }
        
        void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player != null && player.IsConnected)
                {
                    CuiHelper.DestroyUi(player, UIPanelName);
                }
            }
            lastHits.Clear();
        }

        void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var player = info.Initiator as BasePlayer;
            if (player == null || !player.IsConnected || disabledHudPlayers.Contains(player.userID)) return;
            
            if (entity.Health() - info.damageTypes.Total() <= 0)
            {
                return;
            }

            if (ShouldTrackEntity(entity))
            {
                lastHits[player] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                NextTick(() =>
                {
                    if (player != null && player.IsConnected && entity != null && !entity.IsDestroyed)
                    {
                        ShowHealthUI(player, entity);
                    }
                });
            }
        }

        void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (info == null) return;

            var player = info.Initiator as BasePlayer;
            if (player == null || !player.IsConnected || disabledHudPlayers.Contains(player.userID)) return;

            if (ShouldTrackEntity(entity))
            {
                lastHits[player] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                
                string prefabName = entity.ShortPrefabName;
                bool isHeli = entity is PatrolHelicopter;

                NextTick(() =>
                {
                    if (player != null && player.IsConnected)
                    {
                        ShowDeathUI(player, prefabName, isHeli);
                    }
                });
            }
        }
        #endregion

        #region Lógica da UI
        void ShowHealthUI(BasePlayer player, BaseCombatEntity target, bool isDead = false)
        {
            // MUDANÇA: Verifica se o objeto target é nulo antes de usá-lo
            if (player == null || !player.IsConnected || target == null || target.IsDestroyed) return;

            CuiHelper.DestroyUi(player, UIPanelName);
            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Image = { Color = "0 0 0 0" },
                CursorEnabled = false
            }, "Hud", UIPanelName);
            
            if (target is PatrolHelicopter heli)
            {
                AddHealthBar(container, UIPanelName, lang.GetMessage("HeliBody", this, player.UserIDString), heli, config.Positions.MainBarAnchorMin, config.Positions.MainBarAnchorMax, config.MainBar, isDead);
                var mainRotor = heli.weakspots.FirstOrDefault(x => x.bonenames.Any(name => name.Contains("main_rotor")));
                if (mainRotor != null) AddHealthBarForWeakspot(container, UIPanelName, lang.GetMessage("HeliMainRotor", this, player.UserIDString), mainRotor, config.Positions.RotorBarAnchorMin, config.Positions.RotorBarAnchorMax, config.RotorBar, isDead);
                var tailRotor = heli.weakspots.FirstOrDefault(x => x.bonenames.Any(name => name.Contains("tail_rotor")));
                if (tailRotor != null) AddHealthBarForWeakspot(container, UIPanelName, lang.GetMessage("HeliTailRotor", this, player.UserIDString), tailRotor, config.Positions.RotorBarAnchorMin2, config.Positions.RotorBarAnchorMax2, config.RotorBar, isDead);
            }
            else
            {
                string label = GetCleanPrefabName(target.ShortPrefabName);
                AddHealthBar(container, UIPanelName, label, target, config.Positions.MainBarAnchorMin, config.Positions.MainBarAnchorMax, config.MainBar, isDead);
            }

            CuiHelper.AddUi(player, container);
        }
        
        void ShowDeathUI(BasePlayer player, string prefabName, bool isHeli)
        {
            if (player == null || !player.IsConnected) return;

            CuiHelper.DestroyUi(player, UIPanelName);
            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Image = { Color = "0 0 0 0" },
                CursorEnabled = false
            }, "Hud", UIPanelName);

            if (isHeli)
            {
                AddBarElement(container, UIPanelName, lang.GetMessage("HeliBody", this, player.UserIDString), 0f, 1f, 0f, config.Positions.MainBarAnchorMin, config.Positions.MainBarAnchorMax, config.MainBar, true);
                AddBarElement(container, UIPanelName, lang.GetMessage("HeliMainRotor", this, player.UserIDString), 0f, 1f, 0f, config.Positions.RotorBarAnchorMin, config.Positions.RotorBarAnchorMax, config.RotorBar, true);
                AddBarElement(container, UIPanelName, lang.GetMessage("HeliTailRotor", this, player.UserIDString), 0f, 1f, 0f, config.Positions.RotorBarAnchorMin2, config.Positions.RotorBarAnchorMax2, config.RotorBar, true);
            }
            else
            {
                string label = GetCleanPrefabName(prefabName);
                AddBarElement(container, UIPanelName, label, 0f, 1f, 0f, config.Positions.MainBarAnchorMin, config.Positions.MainBarAnchorMax, config.MainBar, true);
            }

            CuiHelper.AddUi(player, container);
        }

        void AddBarElement(CuiElementContainer container, string parent, string label, float health, float maxHealth, float healthPercentage, string anchorMin, string anchorMax, Configuration.BarConfig uiConfig, bool isDead = false)
        {
            string barPanelName = UIPanelName + label.Replace(" ", "");

            container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                Image = { Color = uiConfig.BackgroundColor }
            }, parent, barPanelName);

            float displayHealth = isDead ? 0f : health;
            float displayHealthPercentage = isDead ? 0f : healthPercentage;

            if (displayHealth <= 0) displayHealthPercentage = 0;

            string healthColor = uiConfig.HealthColor_High;
            if (displayHealthPercentage <= 0.5f) healthColor = uiConfig.HealthColor_Medium;
            if (displayHealthPercentage <= 0.25f) healthColor = uiConfig.HealthColor_Low;
            
            container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = "0 0", AnchorMax = $"{displayHealthPercentage} 1" },
                Image = { Color = healthColor }
            }, barPanelName);

            container.Add(new CuiLabel { RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "1 -1", OffsetMax = "1 -1" }, Text = { Text = $"{label}: {Mathf.RoundToInt(displayHealth)}", Align = TextAnchor.MiddleCenter, FontSize = uiConfig.FontSize, Color = uiConfig.TextShadowColor } }, barPanelName);
            
            container.Add(new CuiLabel { RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, Text = { Text = $"{label}: {Mathf.RoundToInt(displayHealth)}", Align = TextAnchor.MiddleCenter, FontSize = uiConfig.FontSize, Color = "1 1 1 1" } }, barPanelName);
        }
        
        void AddHealthBar(CuiElementContainer container, string parentName, string label, BaseCombatEntity target, string anchorMin, string anchorMax, Configuration.BarConfig uiConfig, bool isDead = false) => AddBarElement(container, parentName, label, target.Health(), target.MaxHealth(), target.Health() / target.MaxHealth(), anchorMin, anchorMax, uiConfig, isDead);
        void AddHealthBarForWeakspot(CuiElementContainer container, string parentName, string label, PatrolHelicopter.weakspot weakspot, string anchorMin, string anchorMax, Configuration.BarConfig uiConfig, bool isDead = false) => AddBarElement(container, parentName, label, weakspot.health, weakspot.maxHealth, weakspot.HealthFraction(), anchorMin, anchorMax, uiConfig, isDead);
        #endregion

        #region Funções Auxiliares e Comandos
        private bool ShouldTrackEntity(BaseCombatEntity entity) => entity != null && (config.BlacklistedEntityPrefabs == null || !config.BlacklistedEntityPrefabs.Contains(entity.ShortPrefabName));
        private string GetCleanPrefabName(string prefabName) => prefabName.Replace(".prefab", "").Replace("_", " ").ToUpper();
        
        [Command("dhud")]
        void CmdHud(BasePlayer player, string command, string[] args)
        {
            if (disabledHudPlayers.Contains(player.userID))
            {
                disabledHudPlayers.Remove(player.userID);
                player.ChatMessage(lang.GetMessage("HUD Ativada", this, player.UserIDString));
            }
            else
            {
                disabledHudPlayers.Add(player.userID);
                CuiHelper.DestroyUi(player, UIPanelName);
                player.ChatMessage(lang.GetMessage("HUD Desativada", this, player.UserIDString));
            }
        }
        
        #region Config e Mensagens
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["HUD Ativada"] = "<color=#82d15a>HUD de dano ativada.</color> Use <color=#f2d15a>/dhud</color> para desativar.",
                ["HUD Desativada"] = "<color=#d15a5a>HUD de dano desativada.</color> Use <color=#f2d15a>/dhud</color> para ativar.",
                ["HeliBody"] = "Corpo",
                ["HeliMainRotor"] = "Hélice Principal",
                ["HeliTailRotor"] = "Hélice Traseira"
            }, this, "pt-BR");
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["HUD Ativada"] = "<color=#82d15a>Damage HUD enabled.</color> Use <color=#f2d15a>/dhud</color> to disable.",
                ["HUD Desativada"] = "<color=#d15a5a>Damage HUD disabled.</color> Use <color=#f2d15a>/dhud</color> to enable.",
                ["HeliBody"] = "Body",
                ["HeliMainRotor"] = "Main Rotor",
                ["HeliTailRotor"] = "Tail Rotor"
            }, this, "en");
        }
        protected override void LoadConfig() { base.LoadConfig(); try { config = Config.ReadObject<Configuration>(); if (config == null) throw new Exception(); } catch { LoadDefaultConfig(); } }
        protected override void LoadDefaultConfig() { config = new Configuration { BlacklistedEntityPrefabs = new List<string> { "player" } }; Puts("Criando um novo arquivo de configuração com uma blacklist de exemplo (jogadores)."); }
        protected override void SaveConfig() => Config.WriteObject(config);
        #endregion
        #endregion
    }
}