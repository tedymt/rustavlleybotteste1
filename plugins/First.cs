using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("First", "Mestre Pardal", "2.2.0")]
    [Description("Anuncia globalmente quando um jogador encontra pela primeira vez itens especiais configurados.")]
    public class First : RustPlugin
    {
        #region Config

        private ConfigData _config;

        public class TrackedItem
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Item short name (ex: rifle.ak)")]
            public string ShortName = "rifle.ak";

            [JsonProperty("Item skin id (0 = qualquer skin)")]
            public ulong SkinId = 0;

            [JsonProperty("Required custom name (contém, vazio = ignora nome)")]
            public string RequiredName = "AK Sinistra";

            [JsonProperty("Announcement item name (exibido na HUD)")]
            public string DisplayName = "AK Sinistra";

            [JsonProperty("Item name color in HUD (ex: #FFD700)")]
            public string ItemColorHex = "#FFD700";

            [JsonProperty("Group title (ex: Legendary Weapon)")]
            public string GroupTitle = "";

            [JsonProperty("Group title color (ex: #FFD700)")]
            public string GroupTitleColor = "#FFD700";

            [JsonProperty("Group title font size")]
            public int GroupTitleFontSize = 24;
        }

        public class ConfigData
        {
            [JsonProperty("Announcement duration (seconds)")]
            public float Duration = 8f;

            [JsonProperty("Panel color (RGBA)")]
            public string PanelColor = "0.1 0.1 0.1 0.9";

            [JsonProperty("Text color (RGBA)")]
            public string TextColor = "1 1 1 1";

            [JsonProperty("Sound prefab (empty = sem som)")]
            public string SoundPrefab = "assets/prefabs/misc/halloween/lootbag/effects/gold_open.prefab";

            [JsonProperty("Main HUD message format ({0}=player, {1}=item)")]
            public string MainMessageFormat = "{0} encontrou {1}!";

            [JsonProperty("Tracked items")]
            public List<TrackedItem> Items = new List<TrackedItem>();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<ConfigData>() ?? new ConfigData();
            }
            catch
            {
                PrintError("Falha ao ler config, carregando padrão.");
                LoadDefaultConfig();
            }

            if (_config.Items == null)
                _config.Items = new List<TrackedItem>();

            SanitizeConfig();
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = new ConfigData
            {
                Duration = 8f,
                PanelColor = "0.1 0.1 0.1 0.9",
                TextColor = "1 1 1 1",
                SoundPrefab = "assets/prefabs/misc/halloween/lootbag/effects/gold_open.prefab",
                MainMessageFormat = "{0} encontrou {1}!",
                Items = new List<TrackedItem>
                {
                    new TrackedItem
                    {
                        Enabled      = true,
                        ShortName    = "rifle.ak",
                        SkinId       = 3555523603,
                        RequiredName = "AK Sinistra",
                        DisplayName  = "AK Sinistra",
                        ItemColorHex = "#FFD700",
                        GroupTitle   = "LEGENDARY WEAPON",
                        GroupTitleColor = "#FFD700",
                        GroupTitleFontSize = 24
                    }
                }
            };

            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        private void SanitizeConfig()
        {
            if (_config.Duration <= 0f)
                _config.Duration = 5f;

            if (string.IsNullOrEmpty(_config.MainMessageFormat))
                _config.MainMessageFormat = "{0} encontrou {1}!";

            if (_config.Items == null)
                _config.Items = new List<TrackedItem>();

            foreach (var t in _config.Items)
            {
                if (string.IsNullOrWhiteSpace(t.ShortName))
                    t.Enabled = false;

                if (string.IsNullOrWhiteSpace(t.ItemColorHex))
                    t.ItemColorHex = "#FFD700";

                if (string.IsNullOrWhiteSpace(t.GroupTitleColor))
                    t.GroupTitleColor = "#FFD700";

                if (t.GroupTitleFontSize <= 0)
                    t.GroupTitleFontSize = 24;
            }
        }

        #endregion

        #region Data

        private const string DATA_FILE = "First";

        private class PlayerFirstData
        {
            public Dictionary<string, string> Items = new Dictionary<string, string>();
        }

        private Dictionary<ulong, PlayerFirstData> _playerData = new Dictionary<ulong, PlayerFirstData>();

        private void LoadData()
        {
            try
            {
                _playerData = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, PlayerFirstData>>(DATA_FILE)
                              ?? new Dictionary<ulong, PlayerFirstData>();
            }
            catch
            {
                _playerData = new Dictionary<ulong, PlayerFirstData>();
            }
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(DATA_FILE, _playerData);
        }

        private PlayerFirstData GetPlayerData(ulong id)
        {
            if (!_playerData.TryGetValue(id, out var d) || d == null)
            {
                d = new PlayerFirstData();
                _playerData[id] = d;
            }

            if (d.Items == null)
                d.Items = new Dictionary<string, string>();

            return d;
        }

        #endregion

        #region Cache

        private Dictionary<string, List<TrackedItem>> _itemsByShortName;

        private void BuildCache()
        {
            _itemsByShortName = new Dictionary<string, List<TrackedItem>>(StringComparer.OrdinalIgnoreCase);

            foreach (var it in _config.Items)
            {
                if (!it.Enabled || string.IsNullOrWhiteSpace(it.ShortName))
                    continue;

                var sn = it.ShortName.Trim().ToLowerInvariant();
                if (!_itemsByShortName.TryGetValue(sn, out var list))
                {
                    list = new List<TrackedItem>();
                    _itemsByShortName[sn] = list;
                }

                list.Add(it);
            }
        }

        private static string BuildItemKey(TrackedItem cfg)
        {
            string sn = (cfg.ShortName ?? string.Empty).Trim().ToLowerInvariant();
            string rn = (cfg.RequiredName ?? string.Empty).Trim().ToLowerInvariant();
            return $"{sn}|{cfg.SkinId}|{rn}";
        }

        private static bool MatchesItem(TrackedItem cfg, Item item)
        {
            if (item?.info == null)
                return false;

            if (!string.Equals(item.info.shortname, cfg.ShortName, StringComparison.OrdinalIgnoreCase))
                return false;

            if (cfg.SkinId != 0 && item.skin != cfg.SkinId)
                return false;

            if (!string.IsNullOrEmpty(cfg.RequiredName))
            {
                string required = cfg.RequiredName;
                string candidate = item.name;

                if (string.IsNullOrEmpty(candidate))
                    candidate = item.info.displayName?.english ?? string.Empty;

                if (string.IsNullOrEmpty(candidate))
                    return false;

                if (candidate.IndexOf(required, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }

            return true;
        }

        private TrackedItem FindConfigByKey(string itemKey)
        {
            if (_config.Items == null)
                return null;

            foreach (var cfg in _config.Items)
            {
                if (!cfg.Enabled)
                    continue;

                if (BuildItemKey(cfg) == itemKey)
                    return cfg;
            }

            return null;
        }

        #endregion

        #region UI

        private const string UI_PANEL = "first.announcement.panel";
        private readonly Dictionary<ulong, Timer> _uiTimers = new Dictionary<ulong, Timer>();

        private void DestroyAnnouncement(BasePlayer player)
        {
            if (player == null || !player.IsConnected)
                return;

            CuiHelper.DestroyUi(player, UI_PANEL);

            if (_uiTimers.TryGetValue(player.userID, out var t) && t != null)
            {
                t.Destroy();
                _uiTimers.Remove(player.userID);
            }
        }

        private static string EscapeRich(string s)
        {
            return string.IsNullOrEmpty(s) ? string.Empty : s.Replace("<", "").Replace(">", "");
        }

        private void ShowAnnouncementAll(BasePlayer owner, TrackedItem cfg, Item item)
        {
            if (owner == null || item?.info == null)
                return;

            string itemName = !string.IsNullOrEmpty(cfg.DisplayName)
                ? cfg.DisplayName
                : (item.info.displayName?.english ?? cfg.ShortName ?? "Item");

            string playerColored = $"<color=#00E5FF>{EscapeRich(owner.displayName)}</color>"; // Azul neon
            string itemColor = string.IsNullOrEmpty(cfg.ItemColorHex) ? "#FFFFFF" : cfg.ItemColorHex;
            string itemColored = $"<color={itemColor}>{EscapeRich(itemName)}</color>";

            string message;
            try
            {
                message = string.Format(_config.MainMessageFormat ?? "{0} encontrou {1}!", playerColored, itemColored);
            }
            catch
            {
                message = $"{playerColored} encontrou {itemColored}!";
            }

            string groupTitleRaw = cfg.GroupTitle ?? string.Empty;
            string groupColor = string.IsNullOrEmpty(cfg.GroupTitleColor) ? "#FFFFFF" : cfg.GroupTitleColor;
            string groupColored = string.IsNullOrEmpty(groupTitleRaw)
                ? string.Empty
                : $"<color={groupColor}>{EscapeRich(groupTitleRaw)}</color>";
            int groupFontSize = cfg.GroupTitleFontSize > 0 ? cfg.GroupTitleFontSize : 24;

            foreach (var target in BasePlayer.activePlayerList)
            {
                if (target == null || !target.IsConnected)
                    continue;

                ShowAnnouncementSingle(target, item, message, groupColored, groupFontSize);
                PlaySound(target);
            }
        }

        private void ShowAnnouncementSingle(BasePlayer target, Item item, string message, string groupTitle, int groupFontSize)
        {
            DestroyAnnouncement(target);

            var cont = new CuiElementContainer();

            cont.Add(new CuiElement
            {
                Name = UI_PANEL,
                Parent = "Hud",
                Components =
                {
                    new CuiImageComponent
                    {
                        Color = _config.PanelColor,
                        Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.25 0.82",
                        AnchorMax = "0.75 0.92"
                    }
                }
            });

            cont.Add(new CuiElement
            {
                Parent = UI_PANEL,
                Components =
                {
                    new CuiImageComponent
                    {
                        ItemId = item.info.itemid,
                        SkinId = item.skin,
                        Color  = "1 1 1 1"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.03 -0.4",
                        AnchorMax = "0.22 1.4"
                    }
                }
            });

            // Texto principal (linha de cima)
            cont.Add(new CuiLabel
            {
                RectTransform =
                {
                    AnchorMin = "0.03 0.50",
                    AnchorMax = "0.97 0.95"
                },
                Text =
                {
                    Text     = message,
                    Align    = TextAnchor.MiddleCenter,
                    Color    = _config.TextColor,
                    FontSize = 18
                }
            }, UI_PANEL);

            if (!string.IsNullOrEmpty(groupTitle))
            {
                cont.Add(new CuiLabel
                {
                    RectTransform =
                    {
                        AnchorMin = "0.03 0.05",
                        AnchorMax = "0.97 0.50"
                    },
                    Text =
                    {
                        Text     = groupTitle,
                        Align    = TextAnchor.MiddleCenter,
                        Color    = _config.TextColor,
                        FontSize = groupFontSize
                    }
                }, UI_PANEL);
            }

            CuiHelper.AddUi(target, cont);

            if (_config.Duration > 0f)
            {
                var uid = target.userID;
                _uiTimers[uid] = timer.Once(_config.Duration, () =>
                {
                    var p = BasePlayer.FindByID(uid);
                    if (p != null && p.IsConnected)
                        DestroyAnnouncement(p);
                });
            }
        }

        private void PlaySound(BasePlayer target)
        {
            if (target == null || !target.IsConnected)
                return;

            if (string.IsNullOrEmpty(_config.SoundPrefab))
                return;

            Effect.server.Run(_config.SoundPrefab, target.transform.position);
        }

        #endregion

        #region Commands

        [ChatCommand("firstreset")]
        private void CmdFirstReset(BasePlayer player, string command, string[] args)
        {
            if (player == null)
                return;

            if (!HasAdminPerm(player))
            {
                player.ChatMessage("<color=#ff4444>Você não tem permissão para usar este comando.</color>");
                return;
            }

            if (args.Length == 0)
            {
                player.ChatMessage("<color=#ffff66>Uso correto:</color> /firstreset <playername|id>");
                return;
            }

            string targetArg = args[0];
            BasePlayer target = BasePlayer.Find(targetArg) ??
                                BasePlayer.FindSleeping(targetArg);

            ulong targetId;
            string targetName;

            if (target != null)
            {
                targetId = target.userID;
                targetName = target.displayName;
            }
            else if (ulong.TryParse(targetArg, out targetId))
            {
                if (!_playerData.ContainsKey(targetId))
                {
                    player.ChatMessage($"<color=#ff8888>Não há dados FIRST para o ID '{targetId}'.</color>");
                    return;
                }

                targetName = targetId.ToString();
            }
            else
            {
                player.ChatMessage($"<color=#ff8888>Player '{targetArg}' não encontrado.</color>");
                return;
            }

            var data = GetPlayerData(targetId);
            data.Items.Clear();
            SaveData();

            player.ChatMessage($"<color=#66ff66>Histórico FIRST do jogador {targetName} foi resetado com sucesso!</color>");
        }

        [ChatCommand("firstinfo")]
        private void CmdFirstInfo(BasePlayer player, string command, string[] args)
        {
            if (player == null)
                return;

            if (!HasAdminPerm(player))
            {
                player.ChatMessage("<color=#ff4444>Você não tem permissão para usar este comando.</color>");
                return;
            }

            if (args.Length == 0)
            {
                player.ChatMessage("<color=#ffff66>Uso correto:</color> /firstinfo <playername|id>");
                return;
            }

            string targetArg = args[0];
            BasePlayer target = BasePlayer.Find(targetArg) ??
                                BasePlayer.FindSleeping(targetArg);

            ulong targetId;
            string targetName;

            if (target != null)
            {
                targetId = target.userID;
                targetName = target.displayName;
            }
            else if (ulong.TryParse(targetArg, out targetId))
            {
                if (!_playerData.ContainsKey(targetId))
                {
                    player.ChatMessage($"<color=#ff8888>Não há dados FIRST para o ID '{targetId}'.</color>");
                    return;
                }

                targetName = targetId.ToString();
            }
            else
            {
                player.ChatMessage($"<color=#ff8888>Player '{targetArg}' não encontrado.</color>");
                return;
            }

            var data = GetPlayerData(targetId);
            if (data.Items.Count == 0)
            {
                player.ChatMessage($"<color=#ffcc66>Jogador {targetName} ainda não possui nenhum FIRST registrado.</color>");
                return;
            }

            player.ChatMessage($"<color=#66ffff>FIRST - Histórico de {targetName}:</color>");

            foreach (var kvp in data.Items)
            {
                string key = kvp.Key;
                string when = kvp.Value;

                string sn = key;
                string skinStr = "";
                string reqNameLower = "";

                try
                {
                    var parts = key.Split('|');
                    if (parts.Length >= 1)
                        sn = parts[0];
                    if (parts.Length >= 2)
                        skinStr = parts[1];
                    if (parts.Length >= 3)
                        reqNameLower = parts[2];
                }
                catch
                {
                    // ignore parse errors
                }

                var cfg = FindConfigByKey(key);
                string display = cfg?.DisplayName ?? sn;
                string skinInfo = string.IsNullOrEmpty(skinStr) ? "" : $" (skin {skinStr})";
                string reqInfo = string.IsNullOrEmpty(reqNameLower) ? "" : $" [nome contém: {reqNameLower}]";

                player.ChatMessage($" - <color=#ffffaa>{display}</color>{skinInfo} em <color=#aaaaff>{when}</color>{reqInfo}");
            }
        }

        [ChatCommand("firstresetall")]
        private void CmdFirstResetAll(BasePlayer player, string command, string[] args)
        {
            if (player == null)
                return;

            if (!HasAdminPerm(player))
            {
                player.ChatMessage("<color=#ff4444>Você não tem permissão para usar este comando.</color>");
                return;
            }

            _playerData.Clear();
            SaveData();

            player.ChatMessage("<color=#ff6666>Todos os registros FIRST foram resetados!</color>");
        }

        private bool HasAdminPerm(BasePlayer player)
        {
            return player.IsAdmin || permission.UserHasPermission(player.UserIDString, "first.admin");
        }

        #endregion

        #region Hooks

        private void Init()
        {
            LoadData();
            BuildCache();

            permission.RegisterPermission("first.admin", this);
        }

        private void OnServerInitialized(bool initial)
        {
            BuildCache();
        }

        private void OnServerSave()
        {
            SaveData();
        }

        private void Unload()
        {
            foreach (var p in BasePlayer.activePlayerList)
            {
                if (p != null && p.IsConnected)
                    DestroyAnnouncement(p);
            }

            SaveData();
        }

        private void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            try
            {
                if (container == null || item == null)
                    return;

                var player = container.playerOwner;
                if (player == null || !player.IsConnected)
                    return;

                var def = item.info;
                if (def == null)
                    return;

                var shortName = def.shortname;
                if (string.IsNullOrEmpty(shortName))
                    return;

                if (_itemsByShortName == null || !_itemsByShortName.TryGetValue(shortName, out var list) || list == null || list.Count == 0)
                    return;

                foreach (var cfg in list)
                {
                    if (!cfg.Enabled)
                        continue;

                    if (!MatchesItem(cfg, item))
                        continue;

                    string key = BuildItemKey(cfg);
                    var pdata = GetPlayerData(player.userID);

                    if (pdata.Items.ContainsKey(key))
                        return; // Já anunciado para esse player/esse item

                    string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    pdata.Items[key] = now;
                    SaveData();

                    Puts($"[First] {player.displayName} encontrou pela primeira vez: {cfg.DisplayName} ({shortName}, skin {item.skin}) em {now}");

                    ShowAnnouncementAll(player, cfg, item);
                    return; // só um match por evento
                }
            }
            catch (Exception ex)
            {
                PrintError($"Erro em OnItemAddedToContainer: {ex}");
            }
        }

        #endregion
    }
}
