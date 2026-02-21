using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
	[Info("VirtualRecycler", "M&B-Studios & Mevent", "1.0.6")]
	internal class VirtualRecycler : RustPlugin
	{
		#region Fields

		[PluginReference] private Plugin 
			Notify = null,
			UINotify = null,
			RaidableBases = null;
		
		private const string 
			PERM_USE = "virtualrecycler.use",
			PERM_RAIDABLEBASES = "virtualrecycler.raidablebases";
		
		private readonly Dictionary<ulong, EntityAndPlayer> _recyclers = new();
		private Dictionary<string, ItemContainer> recyclerContainer = new();

		#endregion

		#region Config

		private ConfigData cfg;

		public class ConfigData
		{
			[JsonProperty("Work with Notify?")] public bool UseNotify { get; set; }
			
			[JsonProperty("Commands")] public List<string> Commands { get; set; }

			[JsonProperty("DefaultPermission")] public string DefaultPermission { get; set; }

			[JsonProperty("DefaultSpeed")] public float DefaultSpeed { get; set; }

			[JsonProperty("AutoStart")] public bool AutoStart { get; set; }

			[JsonProperty("PermissionSpeeds")] public Dictionary<string, float> PermissionSpeeds { get; set; }

			[JsonProperty("StaticRecyclerSpeeds")] public Dictionary<string, float> StaticRecyclerSpeeds { get; set; }
		}

		protected override void LoadDefaultConfig()
		{
			var config = new ConfigData
			{
				UseNotify = false,
				Commands = new List<string> {"vrec", "vr", "virtualrec", "vrecycler", "virtualrecycler"},
				DefaultPermission = PERM_USE,
				DefaultSpeed = 1f,
				AutoStart = false,
				PermissionSpeeds = new Dictionary<string, float>
				{
					{"virtualrecycler.admin", 0.1f},
					{"virtualrecycler.vip", 0.5f},
					{PERM_RAIDABLEBASES, 0.5f}
				},

				StaticRecyclerSpeeds = new Dictionary<string, float>
				{
					{"virtualrecycler.static1", 1.0f},
					{"virtualrecycler.static2", 0.5f},
					{"virtualrecycler.vipstatic", 0.1f}
				}
			};
			SaveConfig(config);
		}

		protected override void LoadConfig()
		{
			base.LoadConfig();
			cfg = Config.ReadObject<ConfigData>();
			SaveConfig(cfg);
		}

		private void SaveConfig(object config)
		{
			Config.WriteObject(config, true);
		}

		#endregion
		
		#region Hooks

		private void OnServerInitialized()
		{
			RegisterPermissions();
			
			RegisterCommands();
		}

		private void Unload()
		{
			DestroyRecyclers();
		}

		private void Init()
		{
			Unsubscribe(nameof(CanNetworkTo));
		}

		private object CanNetworkTo(BaseNetworkable e, BasePlayer p)
		{
			if (e == null || p == null || p == e || p.IsAdmin) return null;

			if (IsRecycleBox(e))
				return (PlayerFromRecycler(e.net.ID.Value)?.userID ?? 0) == p.userID;
			return null;
		}

		private object OnEntityVisibilityCheck(BaseEntity ent, BasePlayer player, uint id, string debugName,
			float maximumDistance)
		{
			var recycler = ent as Recycler;
			if (recycler == null) return null;

			var playerFromRec = PlayerFromRecycler(ent.net.ID.Value);

			if (playerFromRec != null && player == playerFromRec) return true;

			return null;
		}

		private object OnEntityDistanceCheck(BaseEntity ent, BasePlayer player, uint id, string debugName,
			float maximumDistance)
		{
			var recycler = ent as Recycler;
			if (recycler == null) return null;

			var playerFromRec = PlayerFromRecycler(ent.net.ID.Value);

			if (playerFromRec != null && playerFromRec == player) return true;

			return null;
		}

		private object OnLootEntityEnd(BasePlayer player, Recycler recycler)
		{
			var playerRecycler = RecyclerFromPlayer(player.userID);

			if (playerRecycler == null || playerRecycler != recycler)
				return null;

			var itemsToMove = recycler.inventory.itemList.ToList();

			foreach (var item in itemsToMove)
				if (!item.MoveToContainer(player.inventory.containerMain) &&
				    !item.MoveToContainer(player.inventory.containerBelt))
					item.Drop(player.GetDropPosition(), player.GetDropVelocity());

			DestroyRecycler(recycler);
			if (recyclerContainer.ContainsKey(player.UserIDString))
				recyclerContainer.Remove(player.UserIDString);

			return null;
		}

		private void OnRecyclerToggle(Recycler recycler, BasePlayer player)
		{
			if (recycler == null || player == null || recycler.skinID != 1231323323123) return;
			
			NextTick(() =>
			{
				if (recycler.IsOn())
				{
					var recyclerSpeed = GetRecyclerSpeed(player.UserIDString);

					recycler.CancelInvoke(nameof(recycler.RecycleThink));

					recycler.InvokeRepeating(recycler.RecycleThink, Mathf.Max(recyclerSpeed - 0.1f, 0f), recyclerSpeed);
				}
			});
		}

		private void StartRecycling(Recycler recycler, BasePlayer player)
		{
			recycler.CancelInvoke(nameof(recycler.RecycleThink));

			var recyclerSpeed = GetRecyclerSpeed(player.UserIDString);
			
			timer.In(0.1f, () => recycler.InvokeRepeating(recycler.RecycleThink, recyclerSpeed - 0.1f, recyclerSpeed));
		}

		private void OnItemAddedToContainer(ItemContainer container, Item item)
		{
			ItemContainer recyclerItemContainer = null;
			BasePlayer player = null;
			foreach (var c in recyclerContainer)
				if (c.Value == container)
				{
					recyclerItemContainer = c.Value;
					BasePlayer.TryFindByID(ulong.Parse(c.Key), out player);
					break;
				}

			if (recyclerItemContainer != null && player != null)
			{
				var recycler = recyclerItemContainer.entityOwner as Recycler;
				if (cfg.AutoStart)
					StartRecycling(recycler, player);
			}


			/* if (container == null || item == null)
			     return;

			 BaseEntity entity = container.entityOwner;
			 if (entity == null || !(entity is Recycler))
			     return;

			 Recycler recycler = entity as Recycler;
			 BasePlayer player = BasePlayer.FindByID(recycler.OwnerID);
			 if (player == null || !cfg.AutoStart)
			     return;*/

			// StartRecycling(recycler, player);
		}

		#endregion

		#region Methods

		public BaseEntity RecyclerFromPlayer(ulong uid)
		{
			foreach (var eap in _recyclers.Values)
				if (eap.Player.userID == uid)
					return eap.Entity;
			return null;
		}

		private void OpenRecycler(BasePlayer player)
		{
			if (player == null) return;

			if (permission.UserHasPermission(player.UserIDString, PERM_RAIDABLEBASES) &&
			    IsRaidableBasesEventTerritory(player.transform.position))
			{
				Reply(player, VirtualRecyclerRaidableBase);
				return;
			}

			if (RecyclerFromPlayer(player.userID) is Recycler existingRecycler && existingRecycler != null)
			{
				MoveItemsToPlayerInventory(player, existingRecycler);
				DestroyRecycler(existingRecycler);
			}

			CreateRecycler(player);
		}

		private void MoveItemsToPlayerInventory(BasePlayer player, Recycler recycler)
		{
			if (recycler == null) return;

			var itemsToMove = recycler.inventory.itemList.ToList();
			foreach (var item in itemsToMove)
				if (!item.MoveToContainer(player.inventory.containerMain) &&
				    !item.MoveToContainer(player.inventory.containerBelt))
					item.Drop(player.GetDropPosition(), player.GetDropVelocity());
		}

		private void CreateRecycler(BasePlayer p)
		{
			var recycler = GameManager.server.CreateEntity("assets/bundled/prefabs/static/recycler_static.prefab",
				p.transform.position + Vector3.down * 500) as Recycler;

			if (recycler == null) return;

			recycler.enableSaving = false;
			recycler.globalBroadcast = true;
			recycler.skinID = 1231323323123;
			
			// CustomSpawn(ref recycler);
			
			recycler.Spawn();

			recycler.SetFlag(BaseEntity.Flags.Locked, true);
			recycler.UpdateNetworkGroup();

			if (!recycler.isSpawned) return;
			
			recycler.gameObject.layer = 0;
			recycler.SendNetworkUpdateImmediate();
			Subscribe(nameof(CanNetworkTo));
			OpenContainer(p, recycler);

			_recyclers.Add(recycler.net.ID.Value, new EntityAndPlayer {Entity = recycler, Player = p});
		}
		
		public BasePlayer PlayerFromRecycler(ulong netID)
		{
			return !IsRecycler(netID) ? null : _recyclers[netID].Player;
		}

		public bool IsRecycler(ulong netID)
		{
			return _recyclers.ContainsKey(netID);
		}

		public bool IsRecycleBox(BaseNetworkable e)
		{
			if (e == null || e.net == null) return false;
			return IsRecycler(e.net.ID.Value);
		}

		private void DestroyRecycler(BaseEntity e)
		{
			if (IsRecycleBox(e))
			{
				_recyclers.Remove(e.net.ID.Value);
				e.Kill();
			}

			if (_recyclers.Count == 0)
				Unsubscribe(nameof(CanNetworkTo));
		}

		private void DestroyRecyclers()
		{
			while (_recyclers.Count > 0)
				DestroyRecycler(_recyclers.FirstOrDefault().Value.Entity);

			Unsubscribe(nameof(CanNetworkTo));
			_recyclers.Clear();
		}

		private void OpenContainer(BasePlayer p, StorageContainer con)
		{
			timer.In(.1f, () =>
			{
				p.EndLooting();
				if (!p.inventory.loot.StartLootingEntity(con, false)) return;
				p.inventory.loot.AddContainer(con.inventory);
				p.inventory.loot.SendImmediate();
				p.ClientRPCPlayer(null, p, "RPC_OpenLootPanel", con.panelName);
				p.SendNetworkUpdate();
				if (!recyclerContainer.ContainsKey(p.UserIDString))
					recyclerContainer.Add(p.UserIDString, con.inventory);
			});
		}

		#endregion

		#region Commands

		private void CmdOpenVirtualRecycler(BasePlayer player)
		{
			if (!permission.UserHasPermission(player.UserIDString, cfg.DefaultPermission))
			{
				Reply(player, VirtualRecyclerNoPermission);
				return;
			}

			OpenRecycler(player);
		}

		#endregion

		#region Utils

		private float GetRecyclerSpeed(string userID)
		{
			var recyclerSpeed = cfg.DefaultSpeed;

			foreach (var (perm, speed) in cfg.StaticRecyclerSpeeds)
				if (permission.UserHasPermission(userID, perm))
				{
					recyclerSpeed = speed;
					break;
				}
			
			return recyclerSpeed;
		}
		
		private void RegisterCommands()
		{
			foreach (var x in cfg.Commands)
				cmd.AddChatCommand(x, this, nameof(CmdOpenVirtualRecycler));
		}

		private void RegisterPermissions()
		{
			var targetPerms = new HashSet<string>();
			
			TryAddPerm(cfg.DefaultPermission);

			foreach (var perm in cfg.PermissionSpeeds.Keys) TryAddPerm(perm);

			foreach (var perm in cfg.StaticRecyclerSpeeds.Keys) TryAddPerm(perm);

			foreach (var targetPerm in targetPerms)
				if (!permission.PermissionExists(targetPerm))
					permission.RegisterPermission(targetPerm, this);
			
			void TryAddPerm(string perm)
			{
				perm = perm.ToLowerInvariant();
				
				if (!string.IsNullOrWhiteSpace(perm))
					targetPerms.Add(perm);
			}
		}

		private bool IsRaidableBasesEventTerritory(Vector3 pos)
		{
			return Convert.ToBoolean(RaidableBases?.Call("EventTerritory", pos));
		}
		
		#region Classes

		public struct EntityAndPlayer
		{
			public BaseEntity Entity;
			public BasePlayer Player;
		}

		#endregion

		#endregion

		#region Lang

		private const string 
			VirtualRecyclerRaidableBase = "VirtualRecyclerRaidableBase",
			VirtualRecyclerNoPermission = "VirtualRecyclerNoPermission";

		protected override void LoadDefaultMessages()
		{
			lang.RegisterMessages(new Dictionary<string, string>
			{
				[VirtualRecyclerRaidableBase] = "You can only use the virtual recycler within Raidable Bases.",
				[VirtualRecyclerNoPermission] = "You do not have permission to use the virtual recycler."
			}, this);
		}

		private string Msg(string key, string userid = null, params object[] obj)
		{
			return string.Format(lang.GetMessage(key, this, userid), obj);
		}

		private string Msg(BasePlayer player, string key, params object[] obj)
		{
			return string.Format(lang.GetMessage(key, this, player.UserIDString), obj);
		}

		private void Reply(BasePlayer player, string key, params object[] obj)
		{
			SendReply(player, Msg(player, key, obj));
		}

		private void SendNotify(BasePlayer player, string key, int type, params object[] obj)
		{
			if (cfg.UseNotify && (Notify != null || UINotify != null))
				Interface.Oxide.CallHook("SendNotify", player, type, Msg(player, key, obj));
			else
				Reply(player, key, obj);
		}

		#endregion

		#region API

		private bool IsVirtualRecycler(Recycler recycler)
		{
			return recycler.skinID == 1231323323123;
		}

		private bool IsVirtualRecycler(BasePlayer player)
		{
			return recyclerContainer.ContainsKey(player.UserIDString);
		}

		#endregion
	}
}