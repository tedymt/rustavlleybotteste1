using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Linq;
using Oxide.Core;
using Oxide.Plugins;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using System;
using System.Threading.Tasks;

namespace Oxide.Plugins
{	
    [Info("GlitchedItemRetriever", "21mac21", "1.4.2")]
    [Description("Retrieves items that glitched under the ground after death")]
    internal class GlitchedItemRetriever : CovalencePlugin
    {
		/*  Changelog
			1.0.0
			initial release
			
			1.1.0
			Changed "OnPlayerDeath" to check for human players only

			1.2.0
			Added configuration option to set wether the items will be slowly lifted up or teleported instantly
			
			1.3.0
			Changed async methods to a monobehavior script attached to the droppedItems, which should boost the performance and eleminate FPS drops

			1.4.0
			Added permission and command to disable/enable the retrieve logic. also, items will not be retrieved if admins throw items under the terrain on purpose

			1.4.1
			Bugfix: Flipped "greater than" to "lower than" check on players position in relation to the ground

			1.4.2
			Feature/Bugfix: separated the permission (controlled by admin, oxide, permission mnanager plugins) from a setting (controlled by the player via chat command), since the permission set in a user group (mostly "Default") cannot be revoked by a player itself.
			Feature: The loot bag (which is dropped when the corpse despawns) is now supported.
			Feature: Loot dropped from a destroyed loot container (barrel etc.) is now supported.
			
		*/

        #region Fields 
		
        private static GlitchedItemRetriever _instance;
		private static int layerMaskTerrain = (1 << 23);
		public const string PermissionActive = "GlitchedItemRetriever.active";
		PluginData pluginData;

		#endregion


		private void Init()
        {
			permission.RegisterPermission(PermissionActive, this);
			AddCovalenceCommand("gir.toggle", nameof(ToggleEnabledDisabled));
            _instance = this;
		}

		private void OnServerInitialized()
		{			
			pluginData = LoadDataFile<PluginData>("ExcludedPlayers");

			if( pluginData == null) pluginData = new PluginData();
		}

		# region Config

		private static PluginConfig _config;

        protected override void LoadDefaultConfig()
        {
            Puts("Creating a default config...");
            _config = PluginConfig.DefaultConfig();
                         
            SaveConfig();
            Puts("Creation of the default config completed!");
        }

		protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<PluginConfig>();
                if (_config == null)
                {
                    throw new JsonException();
                }

                if (!_config.ToDictionary().Keys.SequenceEqual(Config.ToDictionary(x => x.Key, x => x.Value).Keys))
                {
                    Puts("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch
            {
                Puts($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

		#endregion

		#region Classes

		private class LiftToGroundScript : MonoBehaviour
        {
            private Coroutine _liftToGroundCoroutine;
			private static LiftToGroundScript _scriptInstance;
			private BaseEntity _entity;
			private BasePlayer _basePlayer;
			private Rigidbody _rigidBody;
			
			public void SetEntity(BaseEntity entity)
			{
				_entity = entity;
				_rigidBody = entity.GetComponent<Rigidbody>();
			}
			
			public void SetBasePlayer(BasePlayer basePlayer)
			{
				_basePlayer = basePlayer;
			}
			
			public void SetBasePlayer(ulong basePlayerUID)
			{
				_basePlayer = BasePlayer.FindByID(basePlayerUID);
			}
			
			public void StartGroundCheck() {
				//_instance.timer.In(2f, () =>
                //{
                    _liftToGroundCoroutine = StartCoroutine(LiftDroppedItemToGround());
                //});
			}
			
			public void StopGroundCheck() {
			   if (_liftToGroundCoroutine != null)
					StopCoroutine(_liftToGroundCoroutine);
			}

            protected void OnDestroy()
            {
				if(_scriptInstance)
				{
					_scriptInstance.StopGroundCheck();                
					_scriptInstance = null;
				}
            }            

            public static void OnUnload() 
            {
				if(_scriptInstance)
				{
					_scriptInstance.StopGroundCheck();                
					_scriptInstance = null;
				}
            }
            
            private IEnumerator LiftDroppedItemToGround()
			{	
						
				if(_entity == null || _entity is DroppedItem dp1 && dp1.item == null ) yield break;
				
				Vector3 entityPos = _entity.transform.position;
				Vector3 groundPos = entityPos;
				groundPos.y = TerrainMeta.HeightMap.GetHeight(groundPos);
				Vector3 targetPos = groundPos + (Vector3.up * 2.0f);
				
				if(entityPos.y < groundPos.y)
				{
					if(_entity is DroppedItem dp) _basePlayer?.ChatMessage($"Item {dp.item.info.shortname} fell through the ground and was lifted up onto the ground.");
					
					if(_entity is DroppedItemContainer) _basePlayer?.ChatMessage($"Your corpse's loot bag fell through the ground and was lifted up onto the ground.");

					_rigidBody.isKinematic = true;
					_rigidBody.detectCollisions = false;					
		
					if(_config.LiftItemsSlowly) {
						while (_entity.transform.position.y < targetPos.y)
						{
							entityPos = entityPos + (Vector3.up * 0.1f);
							_entity.transform.position = entityPos;
						
							_entity.SendNetworkUpdateImmediate();
							_entity.UpdateNetworkGroup();
						
							yield return new WaitForEndOfFrame();
						}
					} else {
						_entity.transform.position = targetPos;
						_entity.SendNetworkUpdateImmediate();
						_entity.UpdateNetworkGroup();
					}
					
					_rigidBody.isKinematic = false;
					_rigidBody.detectCollisions = true;
				}
				
				yield break;
			}
        }
		
		public class PluginData
		{
			 public List<ulong> ExcludedPlayers;

			 public PluginData() {
				ExcludedPlayers = new List<ulong>();
			 }
		}

		private class PluginConfig
        {
            [JsonProperty(PropertyName = "Lift glitched items slowly up instead of teleporting them instantly")]
			public bool LiftItemsSlowly { get; set; }

            public string ToJson() => JsonConvert.SerializeObject(this);
            public Dictionary<string, object> ToDictionary() => JsonConvert.DeserializeObject<Dictionary<string, object>>(ToJson());

            public static PluginConfig DefaultConfig() 
            {
                return new PluginConfig()
                {
                    LiftItemsSlowly = false,
                };
            }
        }
		
		#endregion

        #region Hooks
		
		private void Unload()
        {
			SaveDataFile("ExcludedPlayers", pluginData);
        }
		
		private void OnServerShutdown() 
		{
			SaveDataFile("ExcludedPlayers", pluginData);
		}

		private void OnServerSave()
        {
			SaveDataFile("ExcludedPlayers", pluginData);
        }

		private void OnEntitySpawned(BaseNetworkable baseNetworkable)
		{	
			BaseEntity baseEntity = baseNetworkable as BaseEntity;			

			DroppedItemContainer droppedItemContainer = baseEntity as DroppedItemContainer;
			if(droppedItemContainer == null) return;
			
			if(!permission.UserHasPermission(droppedItemContainer.playerSteamID.ToString(), PermissionActive)) return;

			if(pluginData.ExcludedPlayers.Contains(droppedItemContainer.playerSteamID)) return;
	
			LiftToGroundScript liftToGroundScript = baseEntity.gameObject.AddComponent<LiftToGroundScript>();
			liftToGroundScript.SetBasePlayer(droppedItemContainer.playerSteamID);
			liftToGroundScript.SetEntity(droppedItemContainer);
			liftToGroundScript.StartGroundCheck();
		}

		private void OnItemDropped(Item item, BaseEntity entity) 
		{
			DroppedItem droppedItem = entity as DroppedItem;
			if(droppedItem == null) return;
			
			BasePlayer basePlayer = BasePlayer.FindByID(droppedItem.DroppedBy);
			if(!basePlayer) return;

			if(!permission.UserHasPermission(droppedItem.DroppedBy.ToString(), PermissionActive)) return;

			if(pluginData.ExcludedPlayers.Contains(droppedItem.DroppedBy)) return;
	
			if(basePlayer.transform.position.y < (TerrainMeta.HeightMap.GetHeight(basePlayer.transform.position) - 3.0f)) return;

			LiftToGroundScript liftToGroundScript = entity.gameObject.AddComponent<LiftToGroundScript>();
			liftToGroundScript.SetBasePlayer(droppedItem.DroppedBy);
			liftToGroundScript.SetEntity(droppedItem);
			liftToGroundScript.StartGroundCheck();
		}
		
        #endregion	

		#region Commands
		
		private void ToggleEnabledDisabled(IPlayer player, string command, string[] args) {
			BasePlayer basePlayer = (BasePlayer)player.Object;

			if(!permission.UserHasPermission(basePlayer.UserIDString, PermissionActive))
			{
				basePlayer.ChatMessage($"You don't have the permission to change this setting.");
				return;
			}

			if(pluginData.ExcludedPlayers.Contains(basePlayer.userID.Get()))
			{
				pluginData.ExcludedPlayers.Remove(basePlayer.userID.Get());
				basePlayer.ChatMessage($"Glitched items will now be retrieved.");
			} else 
			{
				pluginData.ExcludedPlayers.Add(basePlayer.userID.Get());
				basePlayer.ChatMessage($"Glitched items will no longer be retrieved.");
			}
		}

		#endregion	

		#region oxide

		private void SaveDataFile<T>(string fileName, T data)
        {
            Interface.Oxide.DataFileSystem.WriteObject($"{Name}/{fileName}", data);		
        }

		private T LoadDataFile<T>(string fileName)
		{
			try
			{   
                T dataFile = Interface.Oxide.DataFileSystem.ReadObject<T>($"{Name}/{fileName}");
                return dataFile;
			}
			catch (Exception ex)
			{
				PrintError(ex.ToString());
				return default(T);
			}
		}

		#endregion

    }
}  