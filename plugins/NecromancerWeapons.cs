using Oxide.Core;
using Newtonsoft.Json;
using Rust;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("Necromancer Weapons", "Loham", "2.6.3")]
    [Description("Necromancer weapons: per-weapon kill tracking, per-weapon damage bonus and 100% indestructible with correct skin. Auto-refresh all necro items in inventory.")]
    public class NecromancerWeapons : RustPlugin
    {
        private const string PERMISSION_USE = "necromancerweapons.use";
        private const string DATA_FOLDER = "NecromancerWeapons";
        private Configuration config;

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Chat Prefix")]
            public string ChatPrefix { get; set; } = "<color=#8A2BE2>[Necromancer]</color>";

            [JsonProperty("Kill Message (use {0}=Weapon Name, {1}=Kills, {2}=Bonus%)")]
            public string KillMessage { get; set; } = "Your {0} absorbed another soul! Total: {1} kills (+{2}% damage).";

            [JsonProperty("Weapon Name Format (use {name} and {kills})")]
            public string WeaponNameFormat { get; set; } = "{name} [{kills} kills]";

            [JsonProperty("Damage Bonus Per Weapon (0.001 = 0.1%)")]
            public Dictionary<string, float> DamageBonusPerWeapon { get; set; } = new Dictionary<string, float>
            {
                { "rifle.ak", 0.002f },
                { "rifle.m39", 0.007f },
                { "rifle.bolt", 0.04f },
                { "pistol.python", 0.01f },
                { "bow.hunting", 0.01f }
            };

            [JsonProperty("Weapon Skins (by shortname)")]
            public Dictionary<string, ulong> WeaponSkins { get; set; } = new Dictionary<string, ulong>
            {
                { "rifle.ak", 2931815696 },
                { "rifle.m39", 2351756238 },
                { "rifle.bolt", 2558460023 },
                { "pistol.python", 2233653757 },
                { "bow.hunting", 3543091982 }
            };

            [JsonProperty("Custom Weapon Names")]
            public Dictionary<string, string> WeaponNames { get; set; } = new Dictionary<string, string>
            {
                { "rifle.ak", "AK Necromancer" },
                { "rifle.m39", "M39 Necromancer" },
                { "rifle.bolt", "Bolt Necromancer" },
                { "pistol.python", "Python Necromancer" },
                { "bow.hunting", "Bow Necromancer" }
            };

            [JsonProperty("Ignore Trivial Entities")]
            public bool IgnoreTrivial { get; set; } = true;

            [JsonProperty("Indestructible Weapons Enabled")]
            public bool IndestructibleWeapons { get; set; } = true;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null)
                {
                    PrintWarning("Config file is empty or invalid, creating new one...");
                    LoadDefaultConfig();
                }
            }
            catch
            {
                PrintWarning("Config file is corrupt or missing, creating default configuration...");
                LoadDefaultConfig();
            }
        }

        protected override void LoadDefaultConfig()
        {
            config = new Configuration();
            SaveConfig();
        }

        #endregion

        #region Data

        private class PlayerData
        {
            public ulong SteamId;
            public Dictionary<string, int> WeaponKills = new Dictionary<string, int>();
        }

        private string DataDirPath()
        {
            var dir = Path.Combine(Interface.Oxide.DataDirectory, DATA_FOLDER);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        private string DataPathFor(ulong sid) => Path.Combine(DataDirPath(), sid + ".json");

        private PlayerData LoadP(ulong sid)
        {
            try
            {
                var path = DataPathFor(sid);
                if (!File.Exists(path)) return new PlayerData { SteamId = sid };
                var json = File.ReadAllText(path);
                var p = JsonConvert.DeserializeObject<PlayerData>(json) ?? new PlayerData { SteamId = sid };
                if (p.SteamId == 0) p.SteamId = sid;
                if (p.WeaponKills == null) p.WeaponKills = new Dictionary<string, int>();
                return p;
            }
            catch { return new PlayerData { SteamId = sid }; }
        }

        private void SaveP(PlayerData p)
        {
            try { File.WriteAllText(DataPathFor(p.SteamId), JsonConvert.SerializeObject(p, Formatting.Indented)); }
            catch (System.Exception e) { PrintError($"SaveP error {p.SteamId}: {e}"); }
        }

        #endregion

        #region Hooks

        private void Init()
        {
            permission.RegisterPermission(PERMISSION_USE, this);
            cmd.AddChatCommand("mykills", this, nameof(CmdMyKills));
            AddCovalenceCommand("addkills", nameof(ConsoleAddKills));
        }

        private void OnServerInitialized()
        {
            timer.Once(10f, () =>
            {
                Puts("[NecromancerWeapons] Server initialized, refreshing all active players' Necromancer weapons...");

                foreach (var player in BasePlayer.activePlayerList)
                {
                    try
                    {
                        if (player == null) continue;
                        RefreshPlayerNecroItems(player);
                    }
                    catch (System.Exception e)
                    {
                        PrintError($"Error refreshing player {player?.displayName}: {e.Message}");
                    }
                }
            });
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
{
    try
    {
        if (entity == null || info?.InitiatorPlayer == null) return;

        var killer = info.InitiatorPlayer;
        var item = info.Weapon?.GetItem() ?? killer.GetActiveItem();
        if (!IsNecroItem(item)) return;
        if (!permission.UserHasPermission(killer.UserIDString, PERMISSION_USE)) return;

        // ✅ Novo filtro: conta apenas kills em seres vivos
        if (!(entity is BasePlayer) && !(entity is BaseNpc))
            return;

        // Opcional: se quiser ainda evitar contar helicóptero, tanques etc.
        var prefab = entity.PrefabName?.ToLower() ?? "";
        if (prefab.Contains("bradley") || prefab.Contains("patrolhelicopter"))
            return;

        string shortname = item.info.shortname;
        var pd = LoadP(killer.userID);
        if (!pd.WeaponKills.ContainsKey(shortname)) pd.WeaponKills[shortname] = 0;

        pd.WeaponKills[shortname]++;
        SaveP(pd);

        int kills = pd.WeaponKills[shortname];
        float bonusPerKill = GetBonusPerKill(shortname);
        float bonusPercent = kills * bonusPerKill * 100f;

        string weaponName = GetBaseName(shortname);
        killer.ChatMessage($"{config.ChatPrefix} {config.KillMessage.Replace("{0}", weaponName).Replace("{1}", kills.ToString()).Replace("{2}", bonusPercent.ToString("0.##"))}");

        UpdateWeaponName(item, weaponName, kills);
        KeepMaxCondition(item);
        RefreshPlayerNecroItems(killer);
    }
    catch { }
}

        private object OnPlayerAttack(BasePlayer attacker, HitInfo hitInfo)
        {
            try
            {
                if (attacker == null || hitInfo?.Weapon == null) return null;
                var item = hitInfo.Weapon.GetItem();
                if (!IsNecroItem(item)) return null;
                if (!permission.UserHasPermission(attacker.UserIDString, PERMISSION_USE)) return null;

                string shortname = item.info.shortname;
                var pd = LoadP(attacker.userID);
                if (!pd.WeaponKills.TryGetValue(shortname, out int kills) || kills <= 0)
                {
                    KeepMaxCondition(item);
                    return null;
                }

                float bonusPerKill = GetBonusPerKill(shortname);
                float multiplier = 1f + (kills * bonusPerKill);
                hitInfo.damageTypes.ScaleAll(multiplier);
                KeepMaxCondition(item);
                RefreshPlayerNecroItems(attacker);
            }
            catch { }
            return null;
        }

        private void OnLoseCondition(Item item, ref float amount)
        {
            try
            {
                if (!config.IndestructibleWeapons) return;
                if (!IsNecroItem(item)) return;
                amount = 0f;
                KeepMaxCondition(item);
            }
            catch { }
        }

        private void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            try
            {
                if (container == null || item == null || item.info == null) return;
                BasePlayer player = container.playerOwner ?? container.entityOwner as BasePlayer;
                if (player == null) return;

                if (IsNecroItem(item)) RefreshPlayerNecroItems(player);
            }
            catch { }
        }

        private void OnActiveItemChanged(BasePlayer player, Item oldItem, Item newItem)
        {
            try { if (player != null) RefreshPlayerNecroItems(player); } catch { }
        }

        private void OnPlayerInit(BasePlayer player)
        {
            timer.Once(2f, () => { try { if (player != null) RefreshPlayerNecroItems(player); } catch { } });
        }

        #endregion

        #region Helpers

        private void RefreshPlayerNecroItems(BasePlayer player)
        {
            if (player == null || player.inventory == null) return;

            var pd = LoadP(player.userID);
            var all = new List<Item>();

            var inv = player.inventory;
            if (inv.containerMain != null) all.AddRange(inv.containerMain.itemList);
            if (inv.containerBelt != null) all.AddRange(inv.containerBelt.itemList);
            if (inv.containerWear != null) all.AddRange(inv.containerWear.itemList);

            foreach (var it in all)
            {
                if (it == null || it.info == null) continue;
                if (!IsNecroItem(it)) continue;

                string sn = it.info.shortname;
                int kills = pd.WeaponKills.ContainsKey(sn) ? pd.WeaponKills[sn] : 0;
                string baseName = GetBaseName(sn);

                UpdateWeaponName(it, baseName, kills);
                KeepMaxCondition(it);
            }
        }

        private bool IsNecroItem(Item item)
        {
            if (item == null || item.info == null || config == null) return false;
            string sn = item.info.shortname;
            if (!config.WeaponSkins.ContainsKey(sn)) return false;

            if (item.skin == config.WeaponSkins[sn]) return true;

            string n = item.name ?? string.Empty;
            if (n.IndexOf("necromancer", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;

            return false;
        }

        private string GetBaseName(string shortname)
        {
            if (config.WeaponNames != null && config.WeaponNames.TryGetValue(shortname, out var nm) && !string.IsNullOrEmpty(nm))
                return nm;
            return "Necromancer Weapon";
        }

        private float GetBonusPerKill(string shortname)
        {
            if (string.IsNullOrEmpty(shortname)) return 0.001f;
            if (config.DamageBonusPerWeapon != null && config.DamageBonusPerWeapon.TryGetValue(shortname, out var v))
                return Mathf.Max(0f, v);
            return 0.001f;
        }

        private void UpdateWeaponName(Item weapon, string baseName, int kills)
        {
            if (weapon == null || config == null) return;
            string newName = config.WeaponNameFormat.Replace("{name}", baseName).Replace("{kills}", kills.ToString());
            if (weapon.name != newName)
            {
                weapon.name = newName;
                weapon.MarkDirty();
            }
        }

        private void KeepMaxCondition(Item item)
        {
            if (!config.IndestructibleWeapons || item == null) return;
            try { if (item.condition < item.maxCondition) item.condition = item.maxCondition; } catch { }
        }

        #endregion

        #region Commands

        private void CmdMyKills(BasePlayer player, string command, string[] args)
        {
            var pd = LoadP(player.userID);
            player.ChatMessage($"{config.ChatPrefix} Your current weapon kills:");
            foreach (var kv in pd.WeaponKills)
            {
                string name = GetBaseName(kv.Key);
                float bonus = kv.Value * GetBonusPerKill(kv.Key) * 100f;
                player.ChatMessage($"<color=#FFD700>{name}</color>: {kv.Value} kills (+{bonus:0.##}% damage)");
            }
        }

        private void ConsoleAddKills(IPlayer caller, string command, string[] args)
        {
            try
            {
                if (caller == null || (!caller.IsServer && caller.Id != "server_console"))
                {
                    Puts("This command can only be executed from the server console, RCON, or another plugin.");
                    return;
                }

                if (args == null || args.Length < 3)
                {
                    Puts("Usage: addkills <steamid> <weapon_shortname> <amount>");
                    return;
                }

                if (!ulong.TryParse(args[0], out ulong steamId))
                {
                    Puts("Invalid SteamID.");
                    return;
                }

                string shortname = args[1];
                if (string.IsNullOrEmpty(shortname))
                {
                    Puts("Invalid weapon shortname.");
                    return;
                }

                if (config == null)
                {
                    Puts("Configuration not loaded. Reload the plugin and try again.");
                    return;
                }

                if (config.WeaponSkins == null || !config.WeaponSkins.ContainsKey(shortname))
                {
                    Puts($"Invalid weapon shortname '{shortname}'. Check your config file (WeaponSkins section).");
                    return;
                }

                if (!int.TryParse(args[2], out int amount) || amount <= 0)
                {
                    Puts("Invalid amount.");
                    return;
                }

                var playerData = LoadP(steamId);
                if (!playerData.WeaponKills.ContainsKey(shortname)) playerData.WeaponKills[shortname] = 0;

                playerData.WeaponKills[shortname] += amount;
                SaveP(playerData);

                string weaponName = GetBaseName(shortname);
                Puts($"[NecromancerWeapons] Added {amount} kills for {weaponName} to player {steamId}.");
            }
            catch (System.Exception ex)
            {
                PrintError($"Error in ConsoleAddKills: {ex}");
            }
        }

        #endregion
    }
}
