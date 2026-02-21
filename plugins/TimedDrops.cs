using Oxide.Core;
using System;
using System.Collections.Generic;
using UnityEngine;
using Facepunch;

namespace Oxide.Plugins
{
    [Info("TimedDrops", "Mestre Pardal", "1.2.3")]
    [Description("Controla cooldown de lançamento de Supply Drops e faz refund anti-duplicação com delay de verificação.")]
    public class TimedDrops : RustPlugin
    {
        #region Config & Data

        private PluginConfig C;
        private StoredData D;

        private class PluginConfig
        {
            public string ChatPrefix = "<color=#00C8FF>[TimedDrops]</color>";
            public bool UseChatPrefix = true;

            public string CooldownMessage = "O Drop de \"{0}\" só poderá ser lançado em {1}.";
            public bool SaveCooldownsToData = true;
            public bool Debug = false;

            public double CooldownRefundDelaySeconds = 1.5;

            public bool EnableRightClickCancel = true;
            public double RightClickCancelBlockSeconds = 1.25;
            public string RightClickCancelMessage = "Lançamento cancelado.";
            public double RightClickCancelMessageCooldownSeconds = 0.75;

            public List<GroupConfig> Groups;
            public List<DropConfig> Drops;
        }

        private class GroupConfig
        {
            public string Name;
            public string Permission;
            public int Priority;
        }

        private class DropConfig
        {
            public string DisplayName;
            public string Shortname;
            public ulong SkinId;
            public string EntityShortPrefabName;
            public Dictionary<string, double> CooldownsSecondsByGroup = new Dictionary<string, double>();
        }

        private class StoredData
        {
            public Dictionary<ulong, Dictionary<string, double>> NextAllowed = new Dictionary<ulong, Dictionary<string, double>>();
        }

        private PluginConfig BuildDefaultConfig()
        {
            return new PluginConfig
            {
                Groups = new List<GroupConfig>
                {
                    new GroupConfig { Name = "VIP3", Permission = "timeddrops.vip3", Priority = 30 },
                    new GroupConfig { Name = "VIP2", Permission = "timeddrops.vip2", Priority = 20 },
                    new GroupConfig { Name = "VIP1", Permission = "timeddrops.vip1", Priority = 10 },
                    new GroupConfig { Name = "Default", Permission = "", Priority = 0 },
                },
                Drops = new List<DropConfig>()
            };
        }

        protected override void LoadDefaultConfig() => C = BuildDefaultConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                C = Config.ReadObject<PluginConfig>();
                if (C == null) throw new Exception();
            }
            catch
            {
                C = BuildDefaultConfig();
            }

            if (C.Groups == null) C.Groups = new List<GroupConfig>();
            if (C.Drops == null) C.Drops = new List<DropConfig>();
            if (C.CooldownRefundDelaySeconds < 0) C.CooldownRefundDelaySeconds = 1.5;

            C.Groups.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(C, true);

        private void LoadDataFile()
        {
            try { D = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name) ?? new StoredData(); }
            catch { D = new StoredData(); }
        }

        private void SaveDataFile()
        {
            if (C == null || !C.SaveCooldownsToData) return;
            Interface.Oxide.DataFileSystem.WriteObject(Name, D);
        }

        #endregion

        #region Runtime

        private readonly Dictionary<ulong, double> _cancelNextThrowUntil = new Dictionary<ulong, double>();
        private readonly Dictionary<ulong, double> _lastCancelMsg = new Dictionary<ulong, double>();
        private readonly HashSet<ulong> _cancelHold = new HashSet<ulong>();
        private readonly Dictionary<ulong, float> _ignoreSpawnUntil = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, Dictionary<string, double>> _runtimeNextAllowed = new Dictionary<ulong, Dictionary<string, double>>();

        private class PendingRefund
        {
            public ulong UserId;
            public string Shortname;
            public ulong Skin;
            public int ExpectedCount;
            public Vector3 DropPos;
            public Timer Timer;
        }

        private readonly Dictionary<string, PendingRefund> _pendingRefunds = new Dictionary<string, PendingRefund>();

        #endregion

        #region Init / Unload

        private void Init()
        {
            LoadDataFile();
            if (C?.Groups != null)
            {
                foreach (var g in C.Groups)
                {
                    if (!string.IsNullOrWhiteSpace(g?.Permission))
                        permission.RegisterPermission(g.Permission.Trim(), this);
                }
            }
        }

        private void Unload()
        {
            foreach (var kv in _pendingRefunds)
                kv.Value?.Timer?.Destroy();
            _pendingRefunds.Clear();

            SaveDataFile();
        }

        private void OnServerSave() => SaveDataFile();

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;
            _cancelNextThrowUntil.Remove(player.userID);
            _lastCancelMsg.Remove(player.userID);
            _cancelHold.Remove(player.userID);

            var prefix = player.userID.ToString() + ":";
            var toRemove = new List<string>();
            foreach (var k in _pendingRefunds.Keys)
                if (k.StartsWith(prefix, StringComparison.Ordinal)) toRemove.Add(k);

            foreach (var k in toRemove)
            {
                _pendingRefunds[k]?.Timer?.Destroy();
                _pendingRefunds.Remove(k);
            }
        }

        #endregion

        #region Right Click Cancel (igual ao original)

        private object CanThrowExplosive(BasePlayer player, Item item)
        {
            if (player == null || item == null) return null;
            if (!C.EnableRightClickCancel) return null;

            var drop = FindDropByItem(item);
            if (drop == null) return null;

            if (_cancelHold.Contains(player.userID))
            {
                _cancelNextThrowUntil[player.userID] = Time.realtimeSinceStartup + C.RightClickCancelBlockSeconds;
                return false;
            }

            string group = ResolvePlayerGroup(player);
            double cooldown = GetCooldown(drop, group);
            if (cooldown > 0)
            {
                string dropKey = MakeDropKey(drop.Shortname, drop.SkinId);
                double nextAllowed = GetNextAllowed(player.userID, dropKey);
                if (GetUnixTime() < nextAllowed)
                {
                    SendChat(player, string.Format(C.CooldownMessage, drop.DisplayName, FormatTime(nextAllowed - GetUnixTime())));
                    return false; // aqui NÃO precisa refund (não consumiu o item)
                }
            }

            return null;
        }

        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null || input == null || !C.EnableRightClickCancel) return;

            bool pressedOrHeld = input.WasJustPressed(BUTTON.FIRE_SECONDARY) || input.IsDown(BUTTON.FIRE_SECONDARY);
            if (pressedOrHeld)
            {
                var active = player.GetActiveItem();
                if (active == null || !IsTimedDropItem(active)) return;

                _cancelNextThrowUntil[player.userID] = Time.realtimeSinceStartup + C.RightClickCancelBlockSeconds;

                player.UpdateActiveItem(default(ItemId));
                player.SendNetworkUpdateImmediate();

                if (!string.IsNullOrWhiteSpace(C.RightClickCancelMessage))
                {
                    double now = Time.realtimeSinceStartup;
                    if (!_lastCancelMsg.TryGetValue(player.userID, out var last) || (now - last) > C.RightClickCancelMessageCooldownSeconds)
                    {
                        _lastCancelMsg[player.userID] = now;
                        SendChat(player, C.RightClickCancelMessage);
                    }
                }

                _cancelHold.Add(player.userID);
                timer.Once(0.30f, () => { if (player != null) _cancelHold.Remove(player.userID); });
                return;
            }

            if (input.WasJustReleased(BUTTON.FIRE_SECONDARY))
                _cancelHold.Remove(player.userID);
        }

        #endregion

        #region Detect Throw (igual ao original)

        private object OnExplosiveThrown(BasePlayer player, SupplySignal entity, ThrownWeapon thrown)
        {
            if (player == null || entity == null || thrown == null) return null;

            _ignoreSpawnUntil[player.userID] = Time.realtimeSinceStartup + 0.60f;
            var item = thrown.GetItem();
            var drop = FindDropByItem(item);
            if (drop == null) return null;

            // Cancel manual: refund imediato como sempre foi
            if (C.EnableRightClickCancel && IsCancelBlocked(player.userID))
            {
                try { entity.EntityToCreate = null; } catch { }
                if (!entity.IsDestroyed) entity.Kill();
                RefundDrop(player, drop, item?.skin ?? 0UL);
                return true;
            }

            return HandleThrow(player, entity, item);
        }

        private object HandleThrow(BasePlayer player, BaseEntity entity, Item item)
        {
            if (player == null || entity == null) return null;

            string itemShortname = item?.info?.shortname;
            ulong itemSkin = item?.skin ?? 0UL;
            string entShortPrefab = entity.ShortPrefabName ?? string.Empty;
            ulong entSkin = entity.skinID;

            if (string.IsNullOrWhiteSpace(itemShortname))
            {
                var active = player.GetActiveItem();
                itemShortname = active?.info?.shortname;
                itemSkin = active?.skin ?? 0UL;
            }

            DropConfig drop = FindMatchingDrop(itemShortname, itemSkin, entShortPrefab, entSkin);
            if (drop == null) return null;

            string group = ResolvePlayerGroup(player);
            double cooldown = GetCooldown(drop, group);
            if (cooldown <= 0) return null;

            string dropKey = MakeDropKey(drop.Shortname, drop.SkinId);
            double now = GetUnixTime();
            double nextAllowed = GetNextAllowed(player.userID, dropKey);

            if (now < nextAllowed)
            {
                // AQUI: refund do cooldown agora é ANTI-DUP
                CancelThrowAndRefundCooldown(player, entity, drop, itemSkin != 0UL ? itemSkin : entSkin);
                SendChat(player, string.Format(C.CooldownMessage, drop.DisplayName, FormatTime(nextAllowed - now)));
                return true;
            }

            SetNextAllowed(player.userID, dropKey, now + cooldown);
            return null;
        }

        #endregion

        #region Fallback Spawn (igual ao original)

        private void OnEntitySpawned(BaseEntity entity)
        {
            if (entity == null || !(entity is SupplySignal)) return;
            var signal = entity as SupplySignal;
            if (signal.OwnerID == 0) return;

            var player = BasePlayer.FindByID(signal.OwnerID);
            if (player == null) return;

            if (_ignoreSpawnUntil.TryGetValue(player.userID, out var until) && Time.realtimeSinceStartup < until)
                return;

            DropConfig drop = FindMatchingDrop("supply.signal", signal.skinID, entity.ShortPrefabName, signal.skinID);
            if (drop == null) return;

            // Cancel manual (clique direito): refund imediato como sempre foi
            if (C.EnableRightClickCancel && IsCancelBlocked(player.userID))
            {
                if (C.Debug) Puts("[TimedDrops] Cancel manual detectado via OnEntitySpawned");
                try { signal.EntityToCreate = null; } catch { }
                if (!signal.IsDestroyed) signal.Kill();
                RefundDrop(player, drop, signal.skinID);
                return;
            }

            string group = ResolvePlayerGroup(player);
            double cooldown = GetCooldown(drop, group);
            if (cooldown <= 0) return;

            string dropKey = MakeDropKey(drop.Shortname, drop.SkinId);
            double now = GetUnixTime();
            double nextAllowed = GetNextAllowed(player.userID, dropKey);

            if (now < nextAllowed)
            {
                // cooldown -> refund anti-dup
                CancelThrowAndRefundCooldown(player, signal, drop, signal.skinID);
                SendChat(player, string.Format(C.CooldownMessage, drop.DisplayName, FormatTime(nextAllowed - now)));
            }
            else
            {
                SetNextAllowed(player.userID, dropKey, now + cooldown);
            }
        }

        #endregion

        #region Matching / Groups / Cooldowns (igual ao original)

        private DropConfig FindDropByItem(Item item)
        {
            if (item == null || item.info == null) return null;
            foreach (var d in C.Drops)
            {
                if (d.Shortname.Equals(item.info.shortname, StringComparison.OrdinalIgnoreCase))
                    if (d.SkinId == 0UL || d.SkinId == item.skin) return d;
            }
            return null;
        }

        private bool IsTimedDropItem(Item item) => FindDropByItem(item) != null;

        private DropConfig FindMatchingDrop(string itemShortname, ulong itemSkin, string entShortPrefab, ulong entSkin)
        {
            string entNorm = Normalize(entShortPrefab);
            foreach (var d in C.Drops)
            {
                if (!string.IsNullOrWhiteSpace(itemShortname) && d.Shortname.Equals(itemShortname, StringComparison.OrdinalIgnoreCase))
                    if (d.SkinId == 0UL || d.SkinId == itemSkin || d.SkinId == entSkin) return d;

                if (!string.IsNullOrWhiteSpace(d.EntityShortPrefabName) && Normalize(d.EntityShortPrefabName) == entNorm)
                    if (d.SkinId == 0UL || d.SkinId == entSkin || d.SkinId == itemSkin) return d;
            }
            return null;
        }

        private string ResolvePlayerGroup(BasePlayer player)
        {
            foreach (var g in C.Groups)
            {
                if (string.IsNullOrWhiteSpace(g.Permission) || permission.UserHasPermission(player.UserIDString, g.Permission))
                    return g.Name ?? "Default";
            }
            return "Default";
        }

        private double GetCooldown(DropConfig drop, string group)
        {
            if (drop.CooldownsSecondsByGroup.TryGetValue(group, out var cd)) return cd;
            return drop.CooldownsSecondsByGroup.TryGetValue("Default", out var dcd) ? dcd : 0;
        }

        #endregion

        #region NEW: Anti-dup refund (somente para COOLDOWN)

        private void CancelThrowAndRefundCooldown(BasePlayer player, BaseEntity entity, DropConfig drop, ulong thrownSkin = 0UL)
        {
            if (entity is SupplySignal ss) try { ss.EntityToCreate = null; } catch { }
            if (entity != null && !entity.IsDestroyed) try { entity.Kill(); } catch { }

            QueueCooldownRefund(player, drop, thrownSkin);
        }

        private void QueueCooldownRefund(BasePlayer player, DropConfig drop, ulong thrownSkin)
        {
            if (player == null || drop == null) return;

            ulong refundSkin = (drop.SkinId == 0UL ? thrownSkin : drop.SkinId);
            string key = $"{player.userID}:{drop.Shortname}:{refundSkin}";

            // já tem timer pendente desse item/skin pro player
            if (_pendingRefunds.ContainsKey(key))
                return;

            double delay = Math.Max(0, C.CooldownRefundDelaySeconds);

            // comportamento antigo
            if (delay <= 0)
            {
                RefundDrop(player, drop, refundSkin);
                return;
            }

            // após cancelar o throw, o item geralmente saiu -> esperamos e vemos se já voltou por outro plugin
            int currentCount = CountInventory(player, drop.Shortname, refundSkin);
            int expectedCount = currentCount + 1;

            var pr = new PendingRefund
            {
                UserId = player.userID,
                Shortname = drop.Shortname,
                Skin = refundSkin,
                ExpectedCount = expectedCount,
                DropPos = player.transform.position + Vector3.up * 0.5f
            };

            pr.Timer = timer.Once((float)delay, () =>
            {
                _pendingRefunds.Remove(key);
                TryFinalizeCooldownRefund(pr);
            });

            _pendingRefunds[key] = pr;
        }

        private void TryFinalizeCooldownRefund(PendingRefund pr)
        {
            if (pr == null) return;

            var player = BasePlayer.FindByID(pr.UserId) ?? BasePlayer.FindSleeping(pr.UserId);

            if (player != null)
            {
                int nowCount = CountInventory(player, pr.Shortname, pr.Skin);

                if (nowCount >= pr.ExpectedCount)
                {
                    if (C.Debug) Puts($"[CooldownRefund] Ignorado: já devolvido por outro plugin ({pr.UserId}, {pr.Shortname}, skin {pr.Skin}).");
                    return;
                }

                var it = ItemManager.CreateByName(pr.Shortname, 1, pr.Skin);
                if (it != null && !player.inventory.GiveItem(it))
                    it.Drop(player.transform.position + Vector3.up * 0.5f, Vector3.zero);

                if (C.Debug) Puts($"[CooldownRefund] Devolvido após delay ({pr.UserId}, {pr.Shortname}, skin {pr.Skin}).");
                return;
            }

            var itOff = ItemManager.CreateByName(pr.Shortname, 1, pr.Skin);
            if (itOff != null)
                itOff.Drop(pr.DropPos, Vector3.zero);

            if (C.Debug) Puts($"[CooldownRefund] Player offline, drop no chão ({pr.UserId}, {pr.Shortname}, skin {pr.Skin}).");
        }

        private int CountInventory(BasePlayer player, string shortname, ulong skin)
        {
            if (player == null || player.inventory == null || string.IsNullOrEmpty(shortname))
                return 0;

            int count = 0;

            void CountContainer(ItemContainer container)
            {
                if (container == null || container.itemList == null) return;
                foreach (var item in container.itemList)
                {
                    if (item?.info == null) continue;
                    if (!item.info.shortname.Equals(shortname, StringComparison.OrdinalIgnoreCase)) continue;
                    if (skin != 0UL && item.skin != skin) continue;
                    count += item.amount;
                }
            }

            CountContainer(player.inventory.containerMain);
            CountContainer(player.inventory.containerBelt);
            CountContainer(player.inventory.containerWear);

            return count;
        }

        #endregion

        #region Refund / Cooldown Store / Utils (igual ao original)

        private void RefundDrop(BasePlayer player, DropConfig drop, ulong skin)
        {
            var it = ItemManager.CreateByName(drop.Shortname, 1, drop.SkinId == 0UL ? skin : drop.SkinId);
            if (it != null && !player.inventory.GiveItem(it))
                it.Drop(player.transform.position + Vector3.up * 0.5f, Vector3.zero);
        }

        private bool IsCancelBlocked(ulong userId)
        {
            if (_cancelNextThrowUntil.TryGetValue(userId, out var until))
            {
                if (Time.realtimeSinceStartup < until) return true;
                _cancelNextThrowUntil.Remove(userId);
            }
            return false;
        }

        private double GetNextAllowed(ulong steamId, string dropKey)
        {
            if (C.SaveCooldownsToData)
            {
                if (D.NextAllowed.TryGetValue(steamId, out var dict) && dict.TryGetValue(dropKey, out var t)) return t;
            }
            else
            {
                if (_runtimeNextAllowed.TryGetValue(steamId, out var dict) && dict.TryGetValue(dropKey, out var t)) return t;
            }
            return 0;
        }

		private void SetNextAllowed(ulong steamId, string dropKey, double nextUnix)
		{
			if (C.SaveCooldownsToData)
			{
				if (!D.NextAllowed.TryGetValue(steamId, out var dict))
					D.NextAllowed[steamId] = dict = new Dictionary<string, double>();

				dict[dropKey] = nextUnix;

				Interface.Oxide.DataFileSystem.WriteObject(Name, D);
			}
			else
			{
				if (!_runtimeNextAllowed.TryGetValue(steamId, out var dict))
					_runtimeNextAllowed[steamId] = dict = new Dictionary<string, double>();

				dict[dropKey] = nextUnix;
			}
		}

        private static string Normalize(string s) => string.IsNullOrWhiteSpace(s) ? string.Empty : s.Replace("_", "").Replace("-", "").Replace(" ", "").Trim().ToLowerInvariant();
        private static string MakeDropKey(string sn, ulong skin) => $"{(sn ?? "").ToLowerInvariant()}|{skin}";
        private static double GetUnixTime() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private void SendChat(BasePlayer player, string msg)
        {
            if (player == null || string.IsNullOrWhiteSpace(msg)) return;
            if (C.UseChatPrefix) player.ChatMessage($"{C.ChatPrefix} {msg}");
            else player.ChatMessage(msg);
        }

        private static string FormatTime(double seconds)
        {
            int s = (int)Math.Max(0, Math.Ceiling(seconds));
            return s / 60 > 0 ? $"{s / 60}m {s % 60}s" : $"{s}s";
        }

        #endregion
    }
}
