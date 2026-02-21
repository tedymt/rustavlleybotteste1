using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Game.Rust.Cui;

namespace Oxide.Plugins
{
    [Info("Shield Backpack", "Finn", "1.6.1")]
    [Description("Cria um escudo de energia ao equipar um item, que absorve dano e mostra uma GUI otimizada com botão On/Off.")]
    public class ShieldBackpack : RustPlugin
    {
        #region Configuração
        private static Configuration _config;

        public class ShieldTier
        {
            [JsonProperty("Vida Mínima para este Nível")]
            public float MinimumHealth { get; set; }
            [JsonProperty("Cor do Escudo (normal, azul, verde, vermelha, roxa, ciano, amarela)")]
            public string Color { get; set; }
        }

        public class IconConfig
        {
            [JsonProperty("Ativado")]
            public bool IsEnabled { get; set; } = true;
            [JsonProperty("URL do Ícone")]
            public string IconUrl { get; set; } = "https://i.imgur.com/S5nB9Gv.png";
            [JsonProperty("Âncora Mínima (X Y)")]
            public string AnchorMin { get; set; } = "0.5 0.0";
            [JsonProperty("Âncora Máxima (X Y)")]
            public string AnchorMax { get; set; } = "0.5 0.0";
            [JsonProperty("Posição (X Y)")]
            public string Position { get; set; } = "-115 20";
            [JsonProperty("Largura")]
            public float Width { get; set; } = 40f;
            [JsonProperty("Altura")]
            public float Height { get; set; } = 40f;
        }

        public class BarConfig
        {
            [JsonProperty("Ativada")]
            public bool IsEnabled { get; set; } = true;
            [JsonProperty("Âncora Mínima (X Y)")]
            public string AnchorMin { get; set; } = "0.5 0.0";
            [JsonProperty("Âncora Máxima (X Y)")]
            public string AnchorMax { get; set; } = "0.5 0.0";
            [JsonProperty("Posição (X Y)")]
            public string Position { get; set; } = "0 20";
            [JsonProperty("Largura")]
            public float Width { get; set; } = 200f;
            [JsonProperty("Altura")]
            public float Height { get; set; } = 40f;
        }

        public class GuiConfig
        {
            [JsonProperty("Configurações do Ícone")]
            public IconConfig Icone { get; set; } = new IconConfig();
            [JsonProperty("Configurações da Barra")]
            public BarConfig Barra { get; set; } = new BarConfig();
        }

        private class Configuration
        {
            [JsonProperty("Shortname do Item Ativador")]
            public string ActivatorItemShortname { get; set; } = "largebackpack";
            [JsonProperty("ID da Skin Ativadora (0 para qualquer uma)")]
            public ulong ActivatorSkinID { get; set; }
            [JsonProperty("Nome Personalizado da Mochila (deixe em branco para não alterar)")]
            public string CustomBackpackName { get; set; } = "Escudo de Força";
            [JsonProperty("Permissão Necessária")]
            public string PermissionName { get; set; } = "shieldbackpack.use";
            [JsonProperty("Vida do Escudo (Valor por bateria)")]
            public float ShieldHealthPerBattery { get; set; } = 1000f;
            [JsonProperty("Shortname do Item de Energia")]
            public string PowerItemShortname { get; set; } = "battery.small";
            [JsonProperty("Tamanho do Escudo")]
            public float ShieldSize { get; set; } = 2.0f;
            [JsonProperty("Deslocamento Vertical da Esfera")]
            public float ShieldVerticalOffset { get; set; } = 1.0f;
            [JsonProperty("Níveis de Cor do Escudo")]
            public List<ShieldTier> ShieldTiers { get; set; }
            [JsonProperty("Intervalo de Salvamento Automático (em segundos)")]
            public float SaveInterval { get; set; } = 300f;
            [JsonProperty("Efeito Sonoro ao Ligar (requer .prefab)")]
            public string TurnOnSoundEffect { get; set; }
            [JsonProperty("Efeito Sonoro ao Desligar (requer .prefab)")]
            public string TurnOffSoundEffect { get; set; }
            [JsonProperty("Efeito Sonoro Contínuo (Loop - requer .prefab)")]
            public string ActiveLoopSoundEffect { get; set; }
            [JsonProperty("Efeito Sonoro ao Carregar com Bateria (requer .prefab)")]
            public string ChargeSoundEffect { get; set; }
            [JsonProperty("Configurações da GUI")]
            public GuiConfig Gui { get; set; } = new GuiConfig();
            [JsonProperty("Ignorar Dano de Jogadores Reais (true para ignorar, false para absorver)")]
            public bool IgnorePlayerDamageOnly { get; set; } = true;

            [JsonProperty("Cooldown de Atualização da GUI (segundos)")]
            public float GuiUpdateCooldown { get; set; } = 0.25f;
        }

        protected override void LoadDefaultConfig()
        {
            _config = new Configuration
            {
                ActivatorItemShortname = "largebackpack",
                ActivatorSkinID = 3432703437,
                CustomBackpackName = "Escudo de Força",
                PermissionName = "shieldbackpack.use",
                ShieldHealthPerBattery = 1000f,
                PowerItemShortname = "battery.small",
                ShieldSize = 2.0f,
                ShieldVerticalOffset = 1.0f,
                ShieldTiers = new List<ShieldTier>
                {
                    new ShieldTier { MinimumHealth = 1.0f, Color = "normal" },
                    new ShieldTier { MinimumHealth = 5000.0f, Color = "normal" },
                    new ShieldTier { MinimumHealth = 10000.0f, Color = "normal" },
                    new ShieldTier { MinimumHealth = 15000.0f, Color = "normal" },
                    new ShieldTier { MinimumHealth = 20000.0f, Color = "normal" },
                    new ShieldTier { MinimumHealth = 25000.0f, Color = "normal" },
                    new ShieldTier { MinimumHealth = 30000.0f, Color = "normal" }
                },
                SaveInterval = 300f,
                TurnOnSoundEffect = "assets/prefabs/npc/autoturret/effects/online.prefab",
                TurnOffSoundEffect = "assets/prefabs/npc/autoturret/effects/offline.prefab",
                ActiveLoopSoundEffect = "",
                ChargeSoundEffect = "assets/prefabs/npc/autoturret/effects/reload.prefab",
                Gui = new GuiConfig
                {
                    Icone = new IconConfig
                    {
                        IsEnabled = true,
                        IconUrl = "https://i.imgur.com/yPKuFTl.png",
                        AnchorMin = "0.5 0.0",
                        AnchorMax = "0.5 0.0",
                        Position = "-220 78",
                        Width = 40f,
                        Height = 40f
                    },
                    Barra = new BarConfig
                    {
                        IsEnabled = true,
                        AnchorMin = "0.5 0.0",
                        AnchorMax = "0.5 0.0",
                        Position = "-10 78",
                        Width = 380f,
                        Height = 8f
                    }
                },
                IgnorePlayerDamageOnly = true,
                GuiUpdateCooldown = 0.25f
            };
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) throw new Exception();
                if (_config.Gui == null) _config.Gui = new GuiConfig();
                if (_config.Gui.Icone == null) _config.Gui.Icone = new IconConfig();
                if (_config.Gui.Barra == null) _config.Gui.Barra = new BarConfig();
                if (_config.ShieldTiers == null || _config.ShieldTiers.Count == 0) LoadDefaultConfig();
            }
            catch
            {
                Puts("A carregar configuração padrão, o ficheiro de configuração parece estar em falta ou corrompido.");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_config);
        #endregion

        #region Lang
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["ShieldEquipped"] = "Campo de Força equipado!",
                ["ShieldEquippedNoPower"] = "Campo de Força equipado, mas sem energia. Adicione uma pilha para o carregar.",
                ["ShieldUnequipped"] = "Campo de Força desequipado.",
                ["BatteryConsumed"] = "Pilha consumida! Vida do escudo aumentada para <color=orange>{0}</color>.",
                ["ShieldDepleted"] = "A energia do seu escudo esgotou-se! Adicione mais pilhas para o recarregar.",
                ["NoPermission"] = "Não tem permissão para usar este comando.",
                ["ShieldHealthStatus"] = "A vida atual do seu escudo é: <color=orange>{0}</color>.",
                ["NoShieldActive"] = "Não possui um escudo de força ativo.",
                ["ShieldToggledOff"] = "Escudo <color=red>desativado</color>. Clique no ícone ou use /shield para ligar.",
                ["ShieldToggledOn"] = "Escudo <color=green>ativado</color>."
            }, this, "pt-BR");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["ShieldEquipped"] = "Force Field equipped!",
                ["ShieldEquippedNoPower"] = "Force Field equipped, but has no power. Add a battery to charge it.",
                ["ShieldUnequipped"] = "Force Field unequipped.",
                ["BatteryConsumed"] = "Battery consumed! Shield health increased to <color=orange>{0}</color>.",
                ["ShieldDepleted"] = "Your shield's energy has been depleted! Add more batteries to recharge it.",
                ["NoPermission"] = "You do not have permission to use this command.",
                ["ShieldHealthStatus"] = "Your current shield health is: <color=orange>{0}</color>.",
                ["NoShieldActive"] = "You do not have an active force shield.",
                ["ShieldToggledOff"] = "Shield <color=red>disabled</color>. Click the icon or use /shield to turn on.",
                ["ShieldToggledOn"] = "Shield <color=green>enabled</color>."
            }, this);
        }

        private string GetLang(string userId, string key, params object[] args) => string.Format(lang.GetMessage(key, this, userId), args);
        #endregion

        #region Dados
        private StoredData _storedData;

        private class StoredData
        {
            public Dictionary<ulong, float> ShieldHealth = new Dictionary<ulong, float>();
            public Dictionary<ulong, float> ShieldMaxHealth = new Dictionary<ulong, float>();
            public HashSet<ulong> ShieldsToggledOn = new HashSet<ulong>();
        }

        private void LoadData()
        {
            try
            {
                _storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);
                if (_storedData.ShieldMaxHealth == null) _storedData.ShieldMaxHealth = new Dictionary<ulong, float>();

                if (_storedData.ShieldsToggledOn == null) _storedData.ShieldsToggledOn = new HashSet<ulong>();
            }
            catch
            {
                _storedData = new StoredData();
            }
        }

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, _storedData);
        #endregion

        #region Comandos

        private void ToggleShield(BasePlayer player)
        {
            if (player == null || _config == null || _storedData == null) return;

            if (!IsCorrectItem(player, FindActivatorItem(player)))
            {
                player.ChatMessage(GetLang(player.UserIDString, "NoShieldActive"));
                return;
            }

            if (IsShieldToggledOn(player.userID))
            {
                _storedData.ShieldsToggledOn.Remove(player.userID);
                player.ChatMessage(GetLang(player.UserIDString, "ShieldToggledOff"));
            }
            else
            {
                _storedData.ShieldsToggledOn.Add(player.userID);
                player.ChatMessage(GetLang(player.UserIDString, "ShieldToggledOn"));
            }

            RefreshShieldState(player);
        }

        [ConsoleCommand("shieldbackpack.toggle")]
        private void ToggleShieldCommand(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            ToggleShield(player);
        }

        [ChatCommand("shield")]
        private void ShieldToggleChat(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, _config.PermissionName))
            {
                player.ChatMessage(GetLang(player.UserIDString, "NoPermission"));
                return;
            }

            ToggleShield(player);
        }

        [ChatCommand("escudo")]
        private void ShieldChatCommand(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, _config.PermissionName))
            {
                player.ChatMessage(GetLang(player.UserIDString, "NoPermission"));
                return;
            }

            if (_storedData.ShieldHealth.TryGetValue(player.userID, out float health) && health > 0)
            {
                player.ChatMessage(GetLang(player.UserIDString, "ShieldHealthStatus", $"{health:F0}"));
            }
            else
            {
                player.ChatMessage(GetLang(player.UserIDString, "NoShieldActive"));
            }
        }
        #endregion

        #region GUI
        private const string GuiIconPanelName = "ShieldBackpackIconPanel";
        private const string GuiIconButtonName = "ShieldBackpackIconButton";
        private const string GuiIconImageName = "ShieldBackpackIconImage";
        private const string GuiBarPanelName = "ShieldBackpackBarPanel";
        private const string GuiBarForegroundName = "ShieldBarForeground";
        private const string GuiBarTextName = "ShieldBarText";
        private readonly HashSet<ulong> _playersWithGui = new HashSet<ulong>();

        private void ShowShieldGui(BasePlayer player, float currentHealth, float maxHealth)
        {
            if (player == null || _playersWithGui.Contains(player.userID)) return;

            var iconConfig = _config.Gui.Icone;
            if (iconConfig.IsEnabled)
            {
                var iconContainer = new CuiElementContainer();
                var iconPos = iconConfig.Position.Split(' ');
                float iconPosX = float.Parse(iconPos[0]);
                float iconPosY = float.Parse(iconPos[1]);
                string iconOffsetMin = $"{iconPosX - (iconConfig.Width / 2)} {iconPosY}";
                string iconOffsetMax = $"{iconPosX + (iconConfig.Width / 2)} {iconPosY + iconConfig.Height}";

                iconContainer.Add(new CuiPanel
                {
                    Image = { Color = "0 0 0 0" },
                    RectTransform = { AnchorMin = iconConfig.AnchorMin, AnchorMax = iconConfig.AnchorMax, OffsetMin = iconOffsetMin, OffsetMax = iconOffsetMax }
                }, "Hud", GuiIconPanelName);

                iconContainer.Add(new CuiButton
                {
                    Button = { Color = "0 0 0 0", Command = "shieldbackpack.toggle" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    Text = { Text = "" }
                }, GuiIconPanelName, GuiIconButtonName);

                string iconColor = IsShieldToggledOn(player.userID) ? "1 1 1 1" : "1 1 1 0.3";
                iconContainer.Add(new CuiElement
                {
                    Name = GuiIconImageName,
                    Parent = GuiIconButtonName,
                    Components =
                    {
                        new CuiRawImageComponent { Url = iconConfig.IconUrl, Color = iconColor },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                    }
                });

                CuiHelper.AddUi(player, iconContainer);
            }

            var barConfig = _config.Gui.Barra;
            if (barConfig.IsEnabled)
            {
                var barContainer = new CuiElementContainer();
                var barPos = barConfig.Position.Split(' ');
                float barPosX = float.Parse(barPos[0]);
                float barPosY = float.Parse(barPos[1]);
                string barOffsetMin = $"{barPosX - (barConfig.Width / 2)} {barPosY}";
                string barOffsetMax = $"{barPosX + (barConfig.Width / 2)} {barPosY + barConfig.Height}";

                barContainer.Add(new CuiPanel { Image = { Color = "0 0 0 0" }, RectTransform = { AnchorMin = barConfig.AnchorMin, AnchorMax = barConfig.AnchorMax, OffsetMin = barOffsetMin, OffsetMax = barOffsetMax } }, "Hud", GuiBarPanelName);
                barContainer.Add(new CuiElement { Name = $"{GuiBarPanelName}_Background", Parent = GuiBarPanelName, Components = { new CuiImageComponent { Color = "0.1 0.1 0.1 0.8" }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" } } });
                CuiHelper.AddUi(player, barContainer);
            }

            _playersWithGui.Add(player.userID);
            UpdateShieldBar(player, currentHealth, maxHealth);
        }

        private void UpdateShieldIcon(BasePlayer player, bool isToggledOn)
        {
            if (player == null || !_playersWithGui.Contains(player.userID)) return;

            CuiHelper.DestroyUi(player, GuiIconImageName);

            var container = new CuiElementContainer();
            var iconConfig = _config.Gui.Icone;
            string iconColor = isToggledOn ? "1 1 1 1" : "1 1 1 0.3";

            container.Add(new CuiElement
            {
                Name = GuiIconImageName,
                Parent = GuiIconButtonName,
                Components =
                {
                    new CuiRawImageComponent { Url = iconConfig.IconUrl, Color = iconColor },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                }
            });
            CuiHelper.AddUi(player, container);
        }

        private void UpdateShieldBar(BasePlayer player, float currentHealth, float maxHealth)
        {
            if (player == null || !_playersWithGui.Contains(player.userID)) return;

            var barConfig = _config.Gui.Barra;
            if (!barConfig.IsEnabled) return;

            CuiHelper.DestroyUi(player, GuiBarForegroundName);
            CuiHelper.DestroyUi(player, GuiBarTextName);

            var container = new CuiElementContainer();

            float healthFraction = maxHealth > 0 ? Mathf.Clamp01(currentHealth / maxHealth) : 0;
            int fontSize = Math.Max(8, (int)(barConfig.Height * 0.35f));

            container.Add(new CuiElement
            {
                Name = GuiBarForegroundName,
                Parent = $"{GuiBarPanelName}_Background",
                Components = { new CuiImageComponent { Color = "0.2 0.5 1 0.9" }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = $"{healthFraction} 1" } }
            });

            container.Add(new CuiElement
            {
                Name = GuiBarTextName,
                Parent = $"{GuiBarPanelName}_Background",
                Components = { new CuiTextComponent { Text = $"{currentHealth:F0} / {maxHealth:F0}", FontSize = fontSize, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" } }
            });

            CuiHelper.AddUi(player, container);
        }

        private void DestroyShieldGui(BasePlayer player)
        {
            if (player != null && _playersWithGui.Contains(player.userID))
            {
                CuiHelper.DestroyUi(player, GuiIconPanelName);
                CuiHelper.DestroyUi(player, GuiBarPanelName);
                _playersWithGui.Remove(player.userID);

                if (_lastGuiUpdate.ContainsKey(player.userID))
                    _lastGuiUpdate.Remove(player.userID);
            }
        }
        #endregion

        private readonly Dictionary<string, string[]> ColorPrefabMap = new Dictionary<string, string[]>
        {
            ["normal"] = new string[] { "assets/prefabs/visualization/sphere.prefab" },
            ["azul"] = new string[] { "assets/bundled/prefabs/modding/events/twitch/br_sphere.prefab" },
            ["verde"] = new string[] { "assets/bundled/prefabs/modding/events/twitch/br_sphere_green.prefab" },
            ["roxa"] = new string[] { "assets/bundled/prefabs/modding/events/twitch/br_sphere_purple.prefab" },
            ["vermelha"] = new string[] { "assets/bundled/prefabs/modding/events/twitch/br_sphere_red.prefab" },
            ["ciano"] = new string[] { "assets/bundled/prefabs/modding/events/twitch/br_sphere.prefab", "assets/bundled/prefabs/modding/events/twitch/br_sphere_green.prefab" },
            ["amarela"] = new string[] { "assets/bundled/prefabs/modding/events/twitch/br_sphere_red.prefab", "assets/bundled/prefabs/modding/events/twitch/br_sphere_green.prefab" }
        };

        private readonly Dictionary<ulong, List<SphereEntity>> _shieldEffects = new Dictionary<ulong, List<SphereEntity>>();
        private readonly Dictionary<ulong, BaseEntity> _loopingSoundEffects = new Dictionary<ulong, BaseEntity>();
        private readonly Dictionary<ulong, string> _currentShieldColors = new Dictionary<ulong, string>();

        private readonly Dictionary<ulong, float> _lastGuiUpdate = new Dictionary<ulong, float>();

        private int _powerItemId;

        private void OnServerInitialized()
        {
            LoadData();
            permission.RegisterPermission(_config.PermissionName, this);

            var powerItemDef = ItemManager.FindItemDefinition(_config.PowerItemShortname);
            if (powerItemDef == null)
            {
                PrintError($"Item de energia com shortname '{_config.PowerItemShortname}' não foi encontrado!");
                return;
            }
            _powerItemId = powerItemDef.itemid;

            timer.Every(_config.SaveInterval, SaveData);

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player != null && player.IsConnected)
                {
                    NextTick(() =>
                    {
                        if (player != null && !player.IsDestroyed)
                            RefreshShieldState(player);
                    });
                }
            }
        }

        private void Unload()
        {
            SaveData();
            foreach (var player in BasePlayer.activePlayerList.ToList())
            {
                DestroyShieldEffect(player, false);
                DestroyShieldGui(player);
            }

            if (_shieldEffects.Count > 0)
            {
                Unsubscribe(nameof(OnEntityTakeDamage));
                _shieldEffects.Clear();
            }
        }

        void OnPlayerInit(BasePlayer player)
        {
            NextTick(() => { if (player != null && !player.IsDestroyed) RefreshShieldState(player); });
        }

        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            DestroyShieldGui(player);
        }

        private bool IsCorrectItem(BasePlayer player, Item item)
        {
            if (player == null || item == null) return false;
            if (item.info.shortname != _config.ActivatorItemShortname) return false;
            if (_config.ActivatorSkinID != 0 && item.skin != _config.ActivatorSkinID) return false;
            if (!permission.UserHasPermission(player.UserIDString, _config.PermissionName)) return false;
            return true;
        }

        void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            if (container == null || item == null || _config == null || _storedData == null) return;

            if (item.info.shortname == _config.ActivatorItemShortname)
            {
                var player = container.GetOwnerPlayer();
                if (player != null && container == player.inventory.containerWear)
                {
                    if (!_storedData.ShieldsToggledOn.Contains(player.userID))
                        _storedData.ShieldsToggledOn.Add(player.userID);

                    RefreshShieldState(player);
                }
                return;
            }

            if (item.info.itemid == _powerItemId)
            {
                var ownerItem = container.parent;
                if (ownerItem != null && ownerItem.info.shortname == _config.ActivatorItemShortname)
                {
                    var player = ownerItem.GetOwnerPlayer();
                    if (player == null) return;

                    if (ownerItem.parent == player.inventory.containerWear && IsCorrectItem(player, ownerItem))
                    {
                        int batteryAmount = item.amount;
                        item.Remove(0f);

                        _storedData.ShieldHealth.TryGetValue(player.userID, out float currentHealth);

                        float totalHealthToAdd = _config.ShieldHealthPerBattery * batteryAmount;
                        currentHealth += totalHealthToAdd;

                        _storedData.ShieldHealth[player.userID] = currentHealth;
                        _storedData.ShieldMaxHealth[player.userID] = currentHealth;

                        if (!string.IsNullOrEmpty(_config.ChargeSoundEffect)) Effect.server.Run(_config.ChargeSoundEffect, player.transform.position);

                        player.ChatMessage(GetLang(player.UserIDString, "BatteryConsumed", $"{currentHealth:F0}"));

                        RefreshShieldState(player);
                    }
                }
            }
        }

        void OnItemRemovedFromContainer(ItemContainer container, Item item)
        {
            if (item?.info.shortname != _config.ActivatorItemShortname) return;

            var player = container.GetOwnerPlayer();
            if (player == null || container != player.inventory.containerWear) return;

            NextTick(() => { if (player != null && !player.IsDestroyed) RefreshShieldState(player); });
        }

        void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (info == null || !(entity is BasePlayer player) || !_shieldEffects.ContainsKey(player.userID) || !IsShieldToggledOn(player.userID)) return;

            if (_config.IgnorePlayerDamageOnly)
            {
                if (info.Initiator is BasePlayer)
                {
                    BasePlayer initiatorPlayer = info.Initiator as BasePlayer;
                    if (!initiatorPlayer.IsNpc)
                    {
                        return;
                    }
                }
            }

            if (!_storedData.ShieldHealth.TryGetValue(player.userID, out float shieldHealth) || shieldHealth <= 0) return;

            float totalDamage = info.damageTypes.Total();
            if (totalDamage <= 0) return;

            float absorbedDamage = Mathf.Min(shieldHealth, totalDamage);
            _storedData.ShieldHealth[player.userID] -= absorbedDamage;
            info.damageTypes.ScaleAll(1f - (absorbedDamage / totalDamage));

            if (_storedData.ShieldHealth[player.userID] <= 0)
            {
                _storedData.ShieldHealth[player.userID] = 0;
                player.ChatMessage(GetLang(player.UserIDString, "ShieldDepleted"));
                RefreshShieldState(player);

                if (_lastGuiUpdate.ContainsKey(player.userID))
                    _lastGuiUpdate.Remove(player.userID);
            }
            else
            {
                float now = Time.realtimeSinceStartup;

                if (!_lastGuiUpdate.TryGetValue(player.userID, out float lastUpdate) || now - lastUpdate > _config.GuiUpdateCooldown)
                {
                    _storedData.ShieldMaxHealth.TryGetValue(player.userID, out float maxHealth);
                    UpdateShieldBar(player, _storedData.ShieldHealth[player.userID], maxHealth);
                    _lastGuiUpdate[player.userID] = now;
                }
            }
        }

        private bool IsShieldToggledOn(ulong playerId)
        {
            return _storedData.ShieldsToggledOn.Contains(playerId);
        }

        private void RefreshShieldState(BasePlayer player)
        {
            if (_config == null || _storedData == null || player == null || player.IsDestroyed) return;

            Item activatorItem = FindActivatorItem(player);
            bool isWearingCorrectItem = IsCorrectItem(player, activatorItem);
            bool guiIsVisible = _playersWithGui.Contains(player.userID);

            if (isWearingCorrectItem && !guiIsVisible)
            {
                _storedData.ShieldHealth.TryGetValue(player.userID, out float h);
                _storedData.ShieldMaxHealth.TryGetValue(player.userID, out float m);
                ShowShieldGui(player, h, m);
            }
            else if (!isWearingCorrectItem && guiIsVisible)
            {
                DestroyShieldGui(player);
            }

            _storedData.ShieldHealth.TryGetValue(player.userID, out float health);
            bool hasHealth = health > 0;
            bool isToggledOn = IsShieldToggledOn(player.userID);

            if (isWearingCorrectItem)
            {
                UpdateShieldIcon(player, isToggledOn);
                _storedData.ShieldMaxHealth.TryGetValue(player.userID, out float maxHealth);
                if (maxHealth < health) maxHealth = health;
                UpdateShieldBar(player, health, maxHealth);
            }

            bool effectIsVisible = _shieldEffects.ContainsKey(player.userID);

            if (isWearingCorrectItem && activatorItem != null && !string.IsNullOrEmpty(_config.CustomBackpackName))
            {
                if (activatorItem.name != _config.CustomBackpackName)
                {
                    activatorItem.name = _config.CustomBackpackName;
                    activatorItem.MarkDirty();
                }
            }

            bool shouldBeVisible = isWearingCorrectItem && hasHealth && isToggledOn;

            if (shouldBeVisible)
            {
                string requiredColor = GetColorNameForHealth(health);
                _currentShieldColors.TryGetValue(player.userID, out string currentColor);

                if (!effectIsVisible)
                {
                    SpawnShieldEffect(player, requiredColor);
                }
                else if (requiredColor != currentColor)
                {
                    DestroyShieldEffect(player, false);
                    SpawnShieldEffect(player, requiredColor);
                }
            }
            else
            {
                if (effectIsVisible) DestroyShieldEffect(player, true);

                if (isWearingCorrectItem && permission.UserHasPermission(player.UserIDString, _config.PermissionName))
                {
                    if (!isToggledOn)
                    {
                    }
                    else if (!hasHealth)
                    {
                        player.ChatMessage(GetLang(player.UserIDString, "ShieldEquippedNoPower"));
                    }
                }
            }
        }

        private Item FindActivatorItem(BasePlayer player)
        {
            foreach (var item in player.inventory.containerWear.itemList)
            {
                if (item.info.shortname == _config.ActivatorItemShortname) return item;
            }
            return null;
        }

        private string GetColorNameForHealth(float health)
        {
            var tiers = _config.ShieldTiers.OrderByDescending(t => t.MinimumHealth);
            foreach (var tier in tiers)
            {
                if (health >= tier.MinimumHealth) return tier.Color.ToLower();
            }
            return "normal";
        }

        private void SpawnShieldEffect(BasePlayer player, string colorName)
        {
            if (_shieldEffects.ContainsKey(player.userID)) return;
            if (!ColorPrefabMap.TryGetValue(colorName, out string[] prefabs))
            {
                prefabs = ColorPrefabMap["normal"];
                PrintError($"Cor '{colorName}' não encontrada no mapa de prefabs. Usando a cor normal.");
            }

            if (_shieldEffects.Count == 0)
            {
                Subscribe(nameof(OnEntityTakeDamage));
            }

            if (!string.IsNullOrEmpty(_config.TurnOnSoundEffect)) Effect.server.Run(_config.TurnOnSoundEffect, player.transform.position);

            var sphereList = new List<SphereEntity>();
            foreach (var prefab in prefabs)
            {
                var sphereEntity = GameManager.server.CreateEntity(prefab, player.transform.position, new Quaternion(), true) as SphereEntity;
                if (sphereEntity != null)
                {
                    sphereEntity.SetParent(player);
                    sphereEntity.transform.localPosition = new Vector3(0, _config.ShieldVerticalOffset, 0);
                    sphereEntity.currentRadius = _config.ShieldSize;
                    sphereEntity.lerpSpeed = 0f;
                    sphereEntity.Spawn();
                    sphereList.Add(sphereEntity);
                }
            }

            if (!string.IsNullOrEmpty(_config.ActiveLoopSoundEffect))
            {
                var soundEntity = GameManager.server.CreateEntity(_config.ActiveLoopSoundEffect, player.transform.position);
                if (soundEntity != null)
                {
                    soundEntity.SetParent(player);
                    soundEntity.Spawn();
                    _loopingSoundEffects[player.userID] = soundEntity;
                }
            }

            _shieldEffects[player.userID] = sphereList;
            _currentShieldColors[player.userID] = colorName;

            if (permission.UserHasPermission(player.UserIDString, _config.PermissionName) && IsShieldToggledOn(player.userID))
            {
                player.ChatMessage(GetLang(player.UserIDString, "ShieldEquipped"));
            }
        }

        private void DestroyShieldEffect(BasePlayer player, bool playSound)
        {
            if (_shieldEffects.TryGetValue(player.userID, out var effectList))
            {
                foreach (var sphere in effectList)
                {
                    if (sphere != null && !sphere.IsDestroyed) sphere.Kill();
                }
                _shieldEffects.Remove(player.userID);
                _currentShieldColors.Remove(player.userID);

                if (_shieldEffects.Count == 0)
                {
                    Unsubscribe(nameof(OnEntityTakeDamage));
                }
                if (playSound && permission.UserHasPermission(player.UserIDString, _config.PermissionName) && IsShieldToggledOn(player.userID))
                {
                    player.ChatMessage(GetLang(player.UserIDString, "ShieldUnequipped"));
                }
            }

            if (_loopingSoundEffects.TryGetValue(player.userID, out var soundEntity))
            {
                if (soundEntity != null && !soundEntity.IsDestroyed) soundEntity.Kill();
                _loopingSoundEffects.Remove(player.userID);
            }

            if (playSound && !string.IsNullOrEmpty(_config.TurnOffSoundEffect))
            {
                Effect.server.Run(_config.TurnOffSoundEffect, player.transform.position);
            }
        }
    }
}
