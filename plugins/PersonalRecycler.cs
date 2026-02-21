using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Rust;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/* Update 1.0.19
 * Fixed an issue with redeemrecycler command still working for the first time when start recyclers is set to 0.
 */

namespace Oxide.Plugins
{
    [Info("PersonalRecycler", "imthenewguy", "1.0.19")]
    [Description("Personal recycler")]
    class PersonalRecycler : RustPlugin
    {
        #region Config

        private Configuration config;
        public class Configuration
        {
            [JsonProperty("How many recyclers would you like players with permissions to start with?")]
            public int start_recyclers = 1;

            [JsonProperty("Maximum recyclers that a player can deploy [0 = no limit]")]
            public int max_deployed = 0;

            [JsonProperty("Permission based overrides for maximum recyclers [perm | amount]")]
            public Dictionary<string, int> max_override = new Dictionary<string, int>();

            [JsonProperty("Only allow the player who deployed the recycler to access it?")]
            public bool restrict_usage = true;

            [JsonProperty("Allow team mates to access each others recyclers?")]
            public bool allow_team = false;

            [JsonProperty("Allow for recycler credits to carry over across a map wipe?")]
            public bool recycler_credits_persist = true;

            [JsonProperty("Recycler pickup options")]
            public PickupOptions pickup_options = new PickupOptions();

            [JsonProperty("Damage caused to the item when picked up?")]
            public float pickup_damage = 20;

            [JsonProperty("Item starting health")]
            public float start_health = 100;

            [JsonProperty("Destroy the recycler if it's parented ground entity is removed?")]
            public bool use_ground_watch = true;

            [JsonProperty("How often should ground watch check to see if a recycler is floating? (seconds)")]
            public float ground_watch_check_delay = 3f;

            [JsonProperty("Drop the recycler on the ground when killed by ground watch?")]
            public bool drop_on_ground_missing = true;

            [JsonProperty("Require the player to be holding a hammer when using the crecycler command?")]
            public bool require_hammer_command = false;

            [JsonProperty("Play an effect when a recycler redemption is added to a player via console?")]
            public bool credit_sound = true;

            [JsonProperty("Auto start the recycler when an item is added?")]
            public bool auto_start = true;

            [JsonProperty("Recycler efficiency")]
            public float efficiency = 0.6f;

            [JsonProperty("Prevent deployment of a recycler unless it's on a building block?")]
            public bool preventDeploymentOnGround = false;

            [JsonProperty("Recycler Item")]
            public RecyclerItem recyclerItem = new RecyclerItem();

            [JsonProperty("List of items that cannot be recycled by personal recyclers")]
            public List<string> blacklist = new List<string>();

            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonConvert.DeserializeObject<Dictionary<string, object>>(ToJson());
        }

        public class RecyclerItem
        {
            public ulong recycler_skin = 2531319393;
            public string recycler_short = "box.wooden.large";
            public string recycler_name = "Recycler";
        }

        public class PickupOptions 
        {
            [JsonProperty("Allow players to pickup their recyclers?")]
            public bool allow_pickup = true;

            [JsonProperty("Allow players to pickup their recyclers by hitting it with a hammer?")]
            public bool hammer_hit = true;

            [JsonProperty("Allow players to pickup their recyclers by targeting it with a hammer and pressing fire_3?")]
            public bool mouse_3 = true;
        }
        

        protected override void LoadDefaultConfig() => config = new Configuration();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null)
                {
                    throw new JsonException();
                }

                if (!config.ToDictionary().Keys.SequenceEqual(Config.ToDictionary(x => x.Key, x => x.Value).Keys))
                {
                    PrintToConsole("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch
            {
                PrintToConsole($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void SaveConfig()
        {
            PrintToConsole($"Configuration changes saved to {Name}.json");
            Config.WriteObject(config, true);
        }

        #endregion

        #region Data

        PCDATA pcdData;
        private DynamicConfigFile PCDDATA;
        public static PersonalRecycler Instance;

        void Init()
        {
            Instance = this;
            permission.RegisterPermission("personalrecycler.place", this);
            permission.RegisterPermission("personalrecycler.use", this);
            permission.RegisterPermission("personalrecycler.admin", this);
            PCDDATA = Interface.Oxide.DataFileSystem.GetFile("PersonalRecycler");            

            if (!config.pickup_options.allow_pickup)
            {
                Unsubscribe("OnHammerHit");
                Unsubscribe("OnPlayerInput");
            }
            else if (!config.pickup_options.hammer_hit) Unsubscribe("OnHammerHit");
            else if (!config.pickup_options.mouse_3) Unsubscribe("OnPlayerInput");
        }

        void Unload()
        {
            SaveData();

            if (config.blacklist.Count > 0)
            {
                foreach (var comp in BlacklistComponents)
                {
                    if (comp.Value != null) GameObject.Destroy(comp.Value);
                }
            }
            foreach (var recycler in BaseEntity.serverEntities.OfType<Recycler>())
            {
                DestroyGroundWatch(recycler);
            }
            RemoveActions();
        }

        void Loaded()
        {
            LoadData();
        }

        void SaveData()
        {
            PCDDATA.WriteObject(pcdData);
        }

        void LoadData()
        {
            try
            {
                pcdData = Interface.GetMod().DataFileSystem.ReadObject<PCDATA>("PersonalRecycler");
            }
            catch
            {
                Puts("Couldn't load player data, creating new Playerfile");
                pcdData = new PCDATA();
            }
        }
        class PCDATA
        {
            public Dictionary<ulong, PlayerInfo> pentity = new Dictionary<ulong, PlayerInfo>();
            public Dictionary<ulong, RecyclersInfo> private_recyclers = new Dictionary<ulong, RecyclersInfo>();
        }

        public class RecyclersInfo
        {
            public ulong playerid;
            public float condition;
            public bool groundWatch;
            public RecyclersInfo(ulong playerid, float condition, bool groundWatch)
            {
                this.playerid = playerid;
                this.condition = condition;
                this.groundWatch = groundWatch;
            }
        }

        public class PlayerInfo
        {
            public string name;
            public int available_recyclers;
            public HashSet<ulong> recycler_ids = new HashSet<ulong>();
            public PlayerInfo(string name)
            {
                this.name = name;
            }

            public HashSet<float> stored_recyclers = new HashSet<float>();
        }

        #endregion

        #region localization

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["decreaserecyclers_success"] = "Removed 1 recycler from each player.",
                ["NoRecyclers"] = "You have no recyclers to redeem.",
                ["RecyclersLeft"] = "You have {0} recyclers left to redeem. Type /redeemrecycler to redeem one into your inventory.",
                ["NoPerms"] = "You do not have permission to access this command.",
                ["RecyclerRedeemed"] = "Redeemed recycler. You have {0} recyclers remaining.",
                ["Addrecycler_usage"] = "Usage: /addrecycler <player name>",
                ["AddedRecycler_success"] = "Added a recycler for {0}",
                ["AddedRecycler_received"] = "You were given a recycler by {0}. Redeemable: {1}",
                ["Clearrecyclers_usage"] = "Usage: /clearrecyclers <name>",
                ["Clearrecyclers_nodata"] = "No data found for {0}",
                ["Clearrecyclers_success"] = "Cleared all recycler data for {0}",
                ["clearrecyclerdata_success"] = "Cleared all recycler data.",
                ["crecycler_NotEmpty"] = "You cannot pickup your recycler with items inside of it. Empty it first.",
                ["crecycler_Notyours"] = "You cannot pickup this recycler!",
                ["CannotLoot"] = "You cannot loot someones personal recycler.",
                ["RecyclerBroken"] = "Your recycler has broken!",
                ["FindPlayerByName_TooMany"] = "More than one player found: {0}",
                ["FindPlayerByName_NoMatch"] = "No player was found that matched: {0}",
                ["ConsoleRedeemed"] = "You redeemed a Recycler token. Your new recycler balance is {0}",
                ["MaxReached"] = "You have already reached the maximum amount of recyclers that can be deployed: {0}",
                ["NoPermsPlace"] = "You do not have permission to place this recycler.",
                ["PlaceOnBuildingBlocks"] = "You can only place this entity on a building block.",
                ["SkinboxReturn"] = "You cannot skin this item/entity with the recycler skin.",
            }, this);
        }

        #endregion

        #region Chat Command

        [ChatCommand("decreaserecyclers")]
        void DecreaseRecyclers(BasePlayer player)
        {
            if (!permission.UserHasPermission(player.UserIDString, "personalrecycler.admin")) return;
            foreach (KeyValuePair<ulong, PlayerInfo> kvp in pcdData.pentity)
            {
                if (kvp.Value.available_recyclers > 1)
                {
                    kvp.Value.available_recyclers--;
                    if (kvp.Value.available_recyclers < 0) kvp.Value.available_recyclers = 0;
                }
            }
            PrintToChat(player, lang.GetMessage("decreaserecyclers_success", this, player.UserIDString));
        }

        [ChatCommand("recyclers")]
        void RecyclerBalance(BasePlayer player)
        {
            PlayerInfo pi;
            if (!pcdData.pentity.TryGetValue(player.userID, out pi))
            {
                if (!permission.UserHasPermission(player.UserIDString, "personalrecycler.use"))
                {
                    PrintToChat(player, lang.GetMessage("NoRecyclers", this, player.UserIDString));
                    return;
                }
                else
                {
                    pcdData.pentity.Add(player.userID, pi = new PlayerInfo(player.displayName));
                    pi.name = player.displayName;
                    pi.available_recyclers = config.start_recyclers;
                    PrintToChat(player, string.Format(lang.GetMessage("RecyclersLeft", this, player.UserIDString), pi.available_recyclers));
                    return;
                }
            }
            if (pi.available_recyclers <= 0) PrintToChat(player, lang.GetMessage("NoRecyclers", this, player.UserIDString));
            else PrintToChat(player, string.Format(lang.GetMessage("RecyclersLeft", this, player.UserIDString), pi.available_recyclers));
        }

        const string perm_use = "personalrecycler.use";
        const string perm_admin = "personalrecycler.admin";

        [ChatCommand("redeemrecycler")]
        void HandleRecyclerRedemption(BasePlayer player)
        {
            PlayerInfo pi;

            bool isAdmin = permission.UserHasPermission(player.UserIDString, perm_admin);

            if (!permission.UserHasPermission(player.UserIDString, perm_use) && !isAdmin)
            {
                if (pcdData.pentity.TryGetValue(player.userID, out pi) && pi.available_recyclers <= 0)
                {
                    PrintToChat(player, lang.GetMessage("NoRecyclers", this, player.UserIDString));
                    return;
                }
            }
            else
            {
                if (pcdData.pentity.TryGetValue(player.userID, out pi))
                {
                    if (pi.available_recyclers <= 0 && !isAdmin)
                    {
                        PrintToChat(player, lang.GetMessage("NoRecyclers", this, player.UserIDString));
                        return;
                    }
                }
                else
                {
                    pcdData.pentity.Add(player.userID, pi = new PlayerInfo(player.displayName));
                    pi.available_recyclers = config.start_recyclers;
                }
            }

            if ((pi == null || pi.available_recyclers <= 0) && !isAdmin)
            {
                PrintToChat(player, "You do not have any recyclers to redeem.");
                return;
            }
            if (!isAdmin) pi.available_recyclers--;
            if (pi.available_recyclers < 0) pi.available_recyclers = 0;
            SpawnRecycler(player);
            PrintToChat(player, string.Format(lang.GetMessage("RecyclerRedeemed", this, player.UserIDString), pi.available_recyclers));
        }

        [ChatCommand("addrecycler")]
        void AddRecycler(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, "personalrecycler.admin")) return;
            if (args.Length != 1)
            {
                PrintToChat(player, lang.GetMessage("Addrecycler_usage", this, player.UserIDString));
                return;
            }
            var target = FindPlayerByName(args[0]);
            if (target == null) return;
            PlayerInfo pi;
            if (!pcdData.pentity.TryGetValue(target.userID, out pi))
            {
                pcdData.pentity.Add(target.userID, pi = new PlayerInfo(player.displayName));
                pi.name = target.displayName;
            }
            pi.available_recyclers++;
            PrintToChat(player, string.Format(lang.GetMessage("AddedRecycler_success", this, player.UserIDString), target.displayName));
            PrintToChat(player, string.Format(lang.GetMessage("AddedRecycler_received", this, player.UserIDString), player.displayName, pi.available_recyclers));
        }

        [ChatCommand("clearrecyclers")]
        void ClearData(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, "personalrecycler.admin")) return;
            if (args.Length != 1)
            {
                PrintToChat(player, lang.GetMessage("Clearrecyclers_usage", this, player.UserIDString));
                return;
            }
            var target = FindPlayerByName(args[0]);
            if (target == null) return;
            PlayerInfo pi;
            if (!pcdData.pentity.TryGetValue(target.userID, out pi))
            {
                PrintToChat(player, string.Format(lang.GetMessage("Clearrecyclers_nodata", this, player.UserIDString), target.displayName));
                return;
            }
            foreach (var id in pi.recycler_ids)
            {
                if (pcdData.private_recyclers.ContainsKey(id)) pcdData.private_recyclers.Remove(id);
            }
            pi.recycler_ids.Clear();
            PrintToChat(player, string.Format(lang.GetMessage("Clearrecyclers_success", this, player.UserIDString), target.displayName));
        }

        [ChatCommand("clearrecyclerdata")]
        void ClearRecyclerData(BasePlayer player)
        {
            if (!permission.UserHasPermission(player.UserIDString, "personalrecycler.admin")) return;
            foreach (KeyValuePair<ulong, PlayerInfo> kvp in pcdData.pentity)
            {
                kvp.Value.recycler_ids.Clear();
            }
            pcdData.private_recyclers.Clear();
            PrintToChat(player, lang.GetMessage("clearrecyclerdata_success", this, player.UserIDString));
        }

        [ConsoleCommand("resetavailablerecyclers")]
        void ResetAvailableRecyclers(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null && !permission.UserHasPermission(player.UserIDString, perm_admin)) return;

            foreach (var kvp in pcdData.pentity)
                kvp.Value.available_recyclers = 0;

            arg.ReplyWith("Reset all available recyclers to 0.");
        }

        [ChatCommand("crecycler")]
        void CollectRecycler(BasePlayer player)
        {
            var recycler = GetTargetEntity(player) as Recycler;
            if (recycler == null || recycler.OwnerID == 0)
            {
                var nearbyRecyclers = FindEntitiesOfType<Recycler>(player.transform.position, 10f);
                foreach (var rec in nearbyRecyclers)
                {
                    if (rec.OwnerID == player.userID)
                    {
                        recycler = rec;
                        break;
                    }
                }
                Pool.FreeUnmanaged(ref nearbyRecyclers);
                if (recycler == null || recycler.OwnerID == 0)
                {
                    var parent = player.GetParentEntity();
                    if (parent != null && parent.ShortPrefabName == "tugboat")
                    {
                        foreach (var child in parent.children)
                        {
                            if (child is Recycler && child.OwnerID == player.userID)
                            {
                                recycler = child as Recycler;
                                break;
                            }
                        }
                    }
                    if (recycler == null || recycler.OwnerID == 0) return;
                }
            }
            var tool = player.GetActiveItem();
            if (config.require_hammer_command && tool?.info.shortname != "toolgun" && tool?.info.shortname != "hammer")
            {
                return;
            }

            if (recycler.inventory.itemList != null && recycler.inventory.itemList.Count > 0)
            {
                PrintToChat(player, lang.GetMessage("crecycler_NotEmpty", this, player.UserIDString));
                return;
            }

            if (player.userID != recycler.OwnerID && (!config.allow_team || !player.Team.members.Contains(recycler.OwnerID)))
            {
                PrintToChat(player, lang.GetMessage("crecycler_Notyours", this, player.UserIDString));
                return;
            }

            PickupRecycler(player, recycler);            
        }

        private static List<T> FindEntitiesOfType<T>(Vector3 pos, float radius, int m = -1) where T : BaseEntity
        {
            int hits = Physics.OverlapSphereNonAlloc(pos, radius, Vis.colBuffer, m, QueryTriggerInteraction.Collide);
            List<T> entities = Pool.Get<List<T>>();
            for (int i = 0; i < hits; i++)
            {
                var entity = Vis.colBuffer[i]?.ToBaseEntity();
                if (entity is T) entities.Add(entity as T);
                Vis.colBuffer[i] = null;
            }
            return entities;
        }



        #endregion

        #region Hooks

        object OnHammerHit(BasePlayer player, HitInfo info)
        {
            var recycler = info?.HitEntity as Recycler;
            if (recycler == null || recycler.OwnerID == 0) return null;

            if (recycler.inventory.itemList != null && recycler.inventory.itemList.Count > 0)
            {
                PrintToChat(player, lang.GetMessage("crecycler_NotEmpty", this, player.UserIDString));
                return null;
            }

            if (player.userID != recycler.OwnerID && (!config.allow_team || player.Team == null || player.Team.members == null || !player.Team.members.Contains(recycler.OwnerID)))
            {
                PrintToChat(player, lang.GetMessage("crecycler_Notyours", this, player.UserIDString));
                return null;
            }

            PickupRecycler(player, recycler);

            return null;
        }

        void PickupRecycler(BasePlayer player, Recycler recycler)
        {
            PlayerInfo pi;
            if (!pcdData.pentity.TryGetValue(player.userID, out pi)) pcdData.pentity.Add(player.userID, pi = new PlayerInfo(player.displayName));

            RecyclersInfo ri;
            pcdData.private_recyclers.TryGetValue(recycler.net.ID.Value, out ri);            

            pi.recycler_ids.Remove(recycler.net.ID.Value);
            SpawnRecycler(player, ri != null ? ri.condition - config.pickup_damage : config.start_health - config.pickup_damage);

            pcdData.private_recyclers.Remove(recycler.net.ID.Value);

            recycler.StopRecycling();
            Unsubscribe("OnEntityKill");
            recycler.KillMessage();
            Subscribe("OnEntityKill");
        }

        void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (!input.WasJustReleased(BUTTON.FIRE_THIRD) || player == null || player.GetActiveItem() == null) return;
            var recycler = GetTargetEntity(player) as Recycler;
            if (recycler == null || recycler.OwnerID == 0) return;
            var tool = player.GetActiveItem();
            if (tool.info.shortname != "toolgun" && tool.info.shortname != "hammer") return;

            if (recycler.inventory.itemList != null && recycler.inventory.itemList.Count > 0)
            {
                PrintToChat(player, lang.GetMessage("crecycler_NotEmpty", this, player.UserIDString));
                return;
            }

            if (player.userID != recycler.OwnerID && (!config.allow_team || !player.Team.members.Contains(recycler.OwnerID)))
            {
                PrintToChat(player, lang.GetMessage("crecycler_Notyours", this, player.UserIDString));
                return;
            }

            PickupRecycler(player, recycler);            
        }

        void OnEntityKill(Recycler entity)
        {
            if (entity == null || entity.OwnerID == 0) return;
            foreach (var pi in pcdData.pentity)
            {
                if (pi.Value.recycler_ids.Contains(entity.net.ID.Value))
                {
                    AutoStartAction.Remove(entity.net.ID.Value);
                    List<Item> items = Pool.Get<List<Item>>();
                    items.AddRange(entity.inventory?.itemList);
                    foreach (var _item in items)
                    {
                        _item.DropAndTossUpwards(entity.transform.position, UnityEngine.Random.Range(1f, 3f));
                    }
                    Pool.FreeUnmanaged(ref items);
                    RecyclersInfo ri;
                    if (pcdData.private_recyclers.TryGetValue(entity.net.ID.Value, out ri) && ri.condition - config.pickup_damage > 0)
                    {
                        var item = ItemManager.CreateByName(config.recyclerItem.recycler_short, 1, config.recyclerItem.recycler_skin);
                        item.name = config.recyclerItem.recycler_name;
                        var condition = ri.condition - config.pickup_damage;
                        item.maxCondition = condition;
                        item.DropAndTossUpwards(entity.transform.position);
                        pcdData.private_recyclers.Remove(entity.net.ID.Value);
                    }

                    pi.Value.recycler_ids.Remove(entity.net.ID.Value);
                    break;
                }
            }

            pcdData.private_recyclers.Remove(entity.net.ID.Value);
        }

        void OnServerSave()
        {
            SaveData();
        }

        void OnNewSave(string filename)
        {
            List<ulong> delete_users = Pool.Get<List<ulong>>();
            foreach (KeyValuePair<ulong, PlayerInfo> kvp in pcdData.pentity)
            {
                kvp.Value.recycler_ids.Clear();
                if (permission.UserHasPermission(kvp.Key.ToString(), "personalrecycler.use") || permission.UserHasPermission(kvp.Key.ToString(), "personalrecycler.admin"))
                {
                    if (kvp.Value.available_recyclers < 0) kvp.Value.available_recyclers = 0;
                    if (config.recycler_credits_persist) kvp.Value.available_recyclers = kvp.Value.available_recyclers + config.start_recyclers;
                    else kvp.Value.available_recyclers = config.start_recyclers;
                }
                else if (kvp.Value.available_recyclers == 0 || !config.recycler_credits_persist) delete_users.Add(kvp.Key);
            }
            pcdData.private_recyclers.Clear();
            if (delete_users.Count > 0) foreach (var id in delete_users) pcdData.pentity.Remove(id);
            Pool.FreeUnmanaged(ref delete_users);
            SaveData();
        }

        void OnServerInitialized(bool initial)
        {
            List<KeyValuePair<string, int>> Fixed = Pool.Get<List<KeyValuePair<string, int>>>();
            foreach (var kvp in config.max_override)
            {
                if (kvp.Key.StartsWith("personalrecycler.", StringComparison.OrdinalIgnoreCase))
                {
                    permission.RegisterPermission(kvp.Key, this);
                    continue;
                }
                Fixed.Add(kvp);
            }
            if (Fixed.Count > 0)
            {
                foreach (var kvp in Fixed)
                {
                    config.max_override.Remove(kvp.Key);
                    config.max_override.Add("personalrecycler." + kvp.Key, kvp.Value);
                    permission.RegisterPermission("personalrecycler." + kvp.Key, this);
                }
                SaveConfig();
            }
            Pool.FreeUnmanaged(ref Fixed);
            
            List<Recycler> recyclers = Pool.Get<List<Recycler>>();
            foreach (var recycler in BaseNetworkable.serverEntities.OfType<Recycler>())
            {
                if (recycler.OwnerID > 0) recyclers.Add(recycler);
            }
            List<ulong> not_found_ids = Pool.Get<List<ulong>>();
            delayValue = config.ground_watch_check_delay;
            foreach (var kvp in pcdData.private_recyclers)
            {
                var found = false;
                foreach (var recycler in recyclers)
                {
                    if (recycler.net.ID.Value == kvp.Key)
                    {
                        foreach (var comp in recycler.GetComponents<Component>())
                        {
                            if (comp.GetType().ToString().Contains("PRBL"))
                            {
                                GameObject.DestroyImmediate(comp);
                            }
                        }
                        if (config.blacklist.Count > 0) AddBlacklistBehavior(recycler);
                        var groundWatch = SetGroundWatch(recycler, false);
                        if (groundWatch) kvp.Value.groundWatch = true;
                        if (kvp.Value.groundWatch) AddGroundWatch(recycler);
                        found = true;
                        break;
                    }
                }
                if (!found) not_found_ids.Add(kvp.Key);
            }

            if (not_found_ids.Count > 0)
            {
                Puts($"Found {not_found_ids.Count} invalid entries. Cleaning up.");
                foreach (var kvp in pcdData.pentity)
                {
                    if (kvp.Value.recycler_ids.Count == 0) continue;
                    foreach (var id in not_found_ids)
                    {
                        kvp.Value.recycler_ids.Remove(id);
                    }
                }

                foreach (var id in not_found_ids)
                {
                    pcdData.private_recyclers.Remove(id);
                }
            }

            Pool.FreeUnmanaged(ref recyclers);
            Pool.FreeUnmanaged(ref not_found_ids);

            foreach (var kvp in pcdData.pentity)
                if (kvp.Value.available_recyclers < 0) kvp.Value.available_recyclers = 0;

            if (config.auto_start) AddAction();
        }

        private bool SetGroundWatch(Recycler recycler, bool addComponent = true)
        {
            if (!config.use_ground_watch) return false;
            RaycastHit rhit;
            var cast = Physics.Raycast(recycler.transform.position + new Vector3(0, 0.1f, 0), Vector3.down,
                out rhit, 1f, LayerMask.GetMask("Construction"));

            var entity = rhit.collider?.ToBaseEntity();
            if (entity != null)
            {
                if (addComponent) AddGroundWatch(recycler);
                return true;
            }
            return false;
        }

        void OnEntityDeath(Tugboat entity, HitInfo info)
        {
            List<Recycler> recyclers = Pool.Get<List<Recycler>>();
            foreach (var child in entity.children)
            {
                if (child == null) continue;
                if (child is Recycler)
                {
                    recyclers.Add(child as Recycler);
                    //child.Invoke(child.KillMessage, 0.05f, 49334);
                }
            }

            foreach (var recycler in recyclers)
            {
                var item = ConvertRecyclerToItem(recycler);
                if (item != null) item.DropAndTossUpwards(recycler.transform.position);
                recycler.Invoke(recycler.KillMessage, 0.05f);
            }

            Pool.FreeUnmanaged(ref recyclers);
        }

        Item ConvertRecyclerToItem(Recycler recycler)
        {
            RecyclersInfo recyclerData;
            if (!pcdData.private_recyclers.TryGetValue(recycler.net.ID.Value, out recyclerData) || recycler.OwnerID == 0) return null;

            PlayerInfo ownerData;
            if (!pcdData.pentity.TryGetValue(recycler.OwnerID, out ownerData)) pcdData.pentity.Add(recycler.OwnerID, ownerData = new PlayerInfo(BasePlayer.Find(recycler.OwnerID.ToString())?.displayName ?? String.Empty));

            ownerData.recycler_ids.Remove(recycler.net.ID.Value);

            var item = SpawnRecycler(recyclerData != null ? recyclerData.condition - config.pickup_damage : config.start_health - config.pickup_damage);
            return item;
        }

        int GetMaxDeployableRecyclers(BasePlayer player)
        {
            if (config.max_deployed == 0) return 0;
            var max = config.max_deployed;
            foreach (var value in config.max_override)
            {
                if (value.Value > max && permission.UserHasPermission(player.UserIDString, value.Key))
                    max = value.Value;
                if (max == 0) return 0;
            }
            return max;
        }

        object CanBuild(Planner planner, Construction prefab, Construction.Target target)
        {
            var player = planner?.GetOwnerPlayer();
            if (player == null) return null;
            var item = planner?.GetItem();
            if (item == null || item.skin != config.recyclerItem.recycler_skin || item.info.shortname != config.recyclerItem.recycler_short) return null;
            if (item.name != config.recyclerItem.recycler_name)
            {
                item.skin = 0;
                item.MarkDirty();
                return null;
            }
            if (!permission.UserHasPermission(player.UserIDString, "personalrecycler.place"))
            {
                PrintToChat(player, lang.GetMessage("NoPermsPlace", this, player.UserIDString));
                return true;
            }
            if (config.preventDeploymentOnGround && !PassGroundCheck(player, target.position))
            {
                PrintToChat(player, lang.GetMessage("PlaceOnBuildingBlocks", this, player.UserIDString));
                return true;
            }
            if (!pcdData.pentity.TryGetValue(player.userID, out var playerData)) return null;
            var max = GetMaxDeployableRecyclers(player);
            if (max == 0 || playerData.recycler_ids.Count < max) return null;

            PrintToChat(player, string.Format(lang.GetMessage("MaxReached", this, player.UserIDString), max));

            return true;
        }

        bool PassGroundCheck(BasePlayer player, Vector3 pos)
        {
            var cast = Physics.Raycast(pos + new Vector3(0, 0.1f, 0), Vector3.down, out var rhit, 1, Layers.Mask.Construction);
            var entity = rhit.collider?.ToBaseEntity();
            return entity != null && entity is BuildingBlock;
        }

        void OnEntityBuilt(Planner plan, GameObject go)
        {
            if (go == null) return;
            var entity = go.ToBaseEntity();
            if (entity == null) return;
            if (entity.skinID != config.recyclerItem.recycler_skin) return;
            if (plan == null || plan.GetOwnerPlayer() == null) return;
            var player = plan.GetOwnerPlayer();
            PlayerInfo pi;
            if (!pcdData.pentity.TryGetValue(player.userID, out pi))
            {
                pcdData.pentity.Add(player.userID, pi = new PlayerInfo(player.displayName));
            }
            var rot = entity.transform.rotation;
            var recycler = GameManager.server.CreateEntity("assets/bundled/prefabs/static/recycler_static.prefab", entity.transform.position, rot, true) as Recycler;
            
            recycler.Spawn();
            recycler.radtownRecycleEfficiency = config.efficiency;
            recycler.safezoneRecycleEfficiency = config.efficiency;

            AddActionToRecycler(recycler);

            if (config.blacklist.Count > 0) AddBlacklistBehavior(recycler);

            recycler.OwnerID = player.userID;
            var item = plan.GetItem();
            pi.recycler_ids.Add(recycler.net.ID.Value);

            timer.Once(0.5f, () =>
            {
                entity.Kill(BaseNetworkable.DestroyMode.None);
            });

            //timer.Once(0.1f, () =>
            //{
            //    entity.Kill(BaseNetworkable.DestroyMode.None);
            //});
            //
            NextTick(() =>
            {
                var parent = entity.GetParentEntity();
                if (parent == null || parent.ShortPrefabName != "tugboat")
                {
                    var groundWatch = SetGroundWatch(recycler);
                    pcdData.private_recyclers.Add(recycler.net.ID.Value, new RecyclersInfo(player.userID, item != null ? item.maxCondition : config.start_health, groundWatch));
                    return;
                }
                recycler.SetParent(parent, true, true);
                pcdData.private_recyclers.Add(recycler.net.ID.Value, new RecyclersInfo(player.userID, item != null ? item.maxCondition : config.start_health, false));

            });
        }

        object CanLootEntity(BasePlayer player, StorageContainer container)
        {
            if (!config.restrict_usage) return null;
            var recycler = container.GetEntity() as Recycler;
            if (player == null || container == null || recycler == null) return null;            

            if (recycler.OwnerID == 0 || !pcdData.private_recyclers.ContainsKey(recycler.net.ID.Value)) return null;

            if (player.userID != recycler.OwnerID && !permission.UserHasPermission(player.UserIDString, "personalrecycler.admin"))
            {
                if (!config.allow_team || (player.Team == null || player.Team.members == null || !player.Team.members.Contains(recycler.OwnerID)))
                {
                    PrintToChat(player, lang.GetMessage("CannotLoot", this, player.UserIDString));
                    return false;
                }
            }

            return null;            
        }

        #endregion

        #region Helpers

        void SpawnRecycler(BasePlayer player, float condition = -100)
        {
            var item = CreateRecyclerItem(condition, out var broken);
            if (broken) PrintToChat(player, lang.GetMessage("RecyclerBroken", this, player.UserIDString));
            player.GiveItem(item);
        }

        Item CreateRecyclerItem(float condition, out bool broken)
        {
            var item = ItemManager.CreateByName(config.recyclerItem.recycler_short, 1, config.recyclerItem.recycler_skin);
            item.name = config.recyclerItem.recycler_name;
            broken = false;
            if (condition == -100) item.maxCondition = config.start_health;
            else if (condition <= 0)
            {
                item.maxCondition = condition;
                item.condition = 0;
                broken = true;
            }
            else item.maxCondition = condition;
            return item;
        }

        Item SpawnRecycler(float condition = -100)
        {
            var item = ItemManager.CreateByName(config.recyclerItem.recycler_short, 1, config.recyclerItem.recycler_skin);
            item.name = config.recyclerItem.recycler_name;
            if (condition == -100) item.maxCondition = config.start_health;
            else if (condition <= 0)
            {
                item.maxCondition = condition;
                item.condition = 0;                
            }
            else item.maxCondition = condition;
            return item;
        }

        private BasePlayer FindPlayerByName(string Playername, BasePlayer SearchingPlayer = null)
        {
            var targetList = BasePlayer.allPlayerList.Where(x => x.displayName.ToLower().Contains(Playername.ToLower())).OrderBy(x => x.displayName.Length);
            if (targetList.Count() == 1) return targetList.First();
            if (targetList.Count() > 1)
            {
                if (targetList.First().displayName.ToLower() == Playername.ToLower()) return targetList.First();
                if (SearchingPlayer != null) PrintToChat(SearchingPlayer, string.Format(lang.GetMessage("FindPlayerByName_TooMany", this, SearchingPlayer.UserIDString), String.Join(",", targetList.Select(x => x.displayName))));
                return null;
            }
            if (targetList.Count() == 0)
            {
                if (SearchingPlayer != null) PrintToChat(SearchingPlayer, string.Format(lang.GetMessage("FindPlayerByName_NoMatch", this, SearchingPlayer.UserIDString), Playername));
                return null;
            }
            return null;
        }

        private BasePlayer FindPlayerByID(ulong id)
        {
            return BasePlayer.allPlayerList.FirstOrDefault(x => x.userID == id) ?? null;           
        }

        private const int LAYER_TARGET = ~(1 << 2 | 1 << 3 | 1 << 4 | 1 << 10 | 1 << 18 | 1 << 28 | 1 << 29);
        private BaseEntity GetTargetEntity(BasePlayer player, float dist = 5f)
        {
            RaycastHit raycastHit;
            bool flag = Physics.Raycast(player.eyes.HeadRay(), out raycastHit, 100f, LAYER_TARGET);
            var targetEntity = flag ? raycastHit.GetEntity() : null;
            return targetEntity;
        }

        #endregion        

        #region API

        [ConsoleCommand("addrecycler")]
        void UseToken(ConsoleSystem.Arg arg)
        {
            // addrecycler id quantity
            var admin = arg.Player();
            if (admin != null && !permission.UserHasPermission(admin.UserIDString, perm_admin)) return;
            if (arg == null || arg.Args == null) return;
            if (arg.Args.Length != 2) return;
            if (!ulong.TryParse(arg.Args[0], out var id)) return;
            if (!id.IsSteamId()) return;
            if (!int.TryParse(arg.Args[1], out var amount)) return;
            var player = FindPlayerByID(id);
            PlayerInfo pi;
            if (!pcdData.pentity.TryGetValue(id, out pi)) pcdData.pentity.Add(id, pi = new PlayerInfo(player != null ? player.displayName : null));
            pi.available_recyclers += amount;
            if (player != null && player.IsConnected) PrintToChat(player, string.Format(lang.GetMessage("ConsoleRedeemed", this, player.UserIDString), pi.available_recyclers));
            if (config.credit_sound) EffectNetwork.Send(new Effect("assets/prefabs/deployable/vendingmachine/effects/vending-machine-purchase-human.prefab", player.transform.position, player.transform.position), player.net.connection);
            Puts($"Adding {amount} recycler(s) for {id}");
        }

        [ConsoleCommand("subtractrecycler")]
        void RemoveRecycler(ConsoleSystem.Arg arg)
        {
            var admin = arg.Player();
            if (admin != null && !permission.UserHasPermission(admin.UserIDString, perm_admin)) return;

            if (arg.Args == null || arg.Args.Length == 0)
            {
                arg.ReplyWith("Usage: subtractrecycler <target> <optional: amount>");
                return;
            }
            ulong id = 0;
            if (!ulong.TryParse(arg.Args[0], out id))
            {
                List<BasePlayer> foundPlayers = Pool.Get<List<BasePlayer>>();
                try
                {
                    foreach (var player in BasePlayer.allPlayerList)
                    {
                        if (player.displayName.Contains("foundPlayers", StringComparison.OrdinalIgnoreCase)) foundPlayers.Add(player);
                    }
                    if (foundPlayers.Count == 0)
                    {
                        arg.ReplyWith($"Found no players that matched: {arg.Args[0]}");
                        return;
                    }
                    if (foundPlayers.Count > 1)
                    {
                        arg.ReplyWith($"Found more than 1 match for {arg.Args[0]}.\nMatches: {string.Join(", ", foundPlayers.Select(x=>x.displayName))}");
                        return;
                    }
                    id = foundPlayers[0].userID;
                }
                finally
                {
                    Pool.FreeUnmanaged(ref foundPlayers);
                }

                if (id == 0) return;
            }
            if (!pcdData.pentity.TryGetValue(id, out var playerData))
            {
                arg.ReplyWith($"Found no data for {id}.");
                return;
            }

            if (playerData.available_recyclers <= 0)
            {
                arg.ReplyWith($"There are no recyclers left to move to {id}");
                return;
            }

            int amount = 1;
            if (int.TryParse(arg.Args[arg.Args.Length - 1], out amount) && amount <= 0) amount = 1;
            playerData.available_recyclers -= amount;
            arg.ReplyWith($"Removed {amount} recyclers from {id}.");
        }

        object SB_CanReskinItem(BasePlayer player, Item item, ulong newSkinID)
        {
            if (newSkinID != config.recyclerItem.recycler_skin) return null;
            return lang.GetMessage("SkinboxReturn", this, player.UserIDString);
        }

        object SB_CanReskinDeployableWith(BasePlayer player, BaseEntity targetEntity, ItemDefinition targetItemDefintion, ulong newSkinID)
        {
            if (newSkinID != config.recyclerItem.recycler_skin) return null;
            return lang.GetMessage("SkinboxReturn", this, player.UserIDString);
        }

        #endregion

        #region MonoBehaviour

        public static float delayValue;

        public void AddGroundWatch(BaseEntity entity)
        {
            var gameObject = entity.GetComponent<Ground_Watch>();
            if (gameObject != null) DestroyGroundWatch(entity);
            entity.gameObject.AddComponent<Ground_Watch>();
        }

        public static void DestroyRecycler(BaseEntity entity)
        {
            if (entity == null || entity.net == null) return;
            DestroyGroundWatch(entity);
            if (Instance.pcdData.pentity.TryGetValue(entity.OwnerID, out var playerData))
            {
                playerData.recycler_ids.Remove(entity.net.ID.Value);                
            }
            if (Instance.config.drop_on_ground_missing && Instance.pcdData.private_recyclers.TryGetValue(entity.net.ID.Value, out var ri) && ri != null)
            {
                var item = Instance.CreateRecyclerItem(ri.condition - Instance.config.pickup_damage, out var broken);
                item.DropAndTossUpwards(entity.transform.position);
            }
            entity.KillMessage();
        }

        public static void DestroyGroundWatch(BaseEntity entity)
        {            
            if (entity == null) return;
            var gameObject = entity.GetComponent<Ground_Watch>();
            if (gameObject != null) GameObject.Destroy(gameObject);
        }

        public class Ground_Watch : MonoBehaviour
        {
            private BaseEntity entity;
            private float groundCheckDelay;

            // Awake() is part of the Monobehaviour class.
            private void Awake()
            {
                entity = GetComponent<BaseEntity>();
                groundCheckDelay = Time.time + 5f;
            }

            // FixedUpdate() is also part of the monobehaviour class.
            public void FixedUpdate()
            {
                if (entity == null) return;
                if (groundCheckDelay < Time.time)
                {
                    groundCheckDelay = Time.time + delayValue;
                    DoGroundCheck();
                }
            }

            public void DoGroundCheck()
            {
                if (entity == null || entity.IsDestroyed || !entity.IsFullySpawned()) return;
                RaycastHit rhit;
                var cast = Physics.Raycast(entity.transform.position + new Vector3(0, 0.1f, 0), Vector3.down,
                    out rhit, 1f, LayerMask.GetMask("Construction"));

                var ent = rhit.collider?.ToBaseEntity();
                if (ent == null) DestroyRecycler(entity);
            }

            // OnDestroy() built into the monobehaviour class.
            private void OnDestroy()
            {
                enabled = false;
                CancelInvoke();
            }
        }

        Dictionary<ulong, Blacklist> BlacklistComponents = new Dictionary<ulong, Blacklist>();

        void AddBlacklistBehavior(Recycler recycler)
        {
            var comp = recycler.gameObject.AddComponent<Blacklist>();
            comp.gameObject.name = "PRBL";
            comp.Setup(recycler);
            BlacklistComponents.Add(recycler.net.ID.Value, comp);
        }

        public class Blacklist : FacepunchBehaviour
        {
            private Recycler recycler;
            private Func<Item, int, bool> canAcceptItem;

            public void Setup(Recycler recycler)
            {
                if (recycler == null || recycler.inventory == null) return;
                this.recycler = recycler;
                canAcceptItem = CheckBlacklist;
                recycler.inventory.canAcceptItem += canAcceptItem;
            }

            public bool CheckBlacklist(Item item, int targetPos)
            {
                if (Instance.config.blacklist.Contains(item.info.shortname)) return false;
                return true;
            }

            private void OnDestroy()
            {
                if (recycler?.inventory != null && canAcceptItem != null)
                {
                    recycler.inventory.canAcceptItem -= canAcceptItem;
                }
                enabled = false;
                CancelInvoke();
            }
        }

        #endregion

        #region Autostart

        Dictionary<ulong, (Action<Item, bool>, Recycler)> AutoStartAction = new Dictionary<ulong, (Action<Item, bool>, Recycler)>();
        void AddAction()
        {
            foreach (var recycler in BaseNetworkable.serverEntities.OfType<Recycler>())
            {
                AddActionToRecycler(recycler);
            }
        }

        void AddActionToRecycler(Recycler recycler)
        {
            if (!config.auto_start) return;
            if (recycler.OwnerID == 0) return;
            Action<Item, bool> handler = (item1, added) => TheAction(item1, added, recycler);
            recycler.inventory.onItemAddedRemoved += handler;
            AutoStartAction.Add(recycler.net.ID.Value, (handler, recycler));
        }

        void RemoveActions()
        {
            if (!config.auto_start) return;
            foreach (var kvp in AutoStartAction)
            {
                var recycler = kvp.Value.Item2;
                var handler = kvp.Value.Item1;

                if (recycler == null || recycler.inventory == null) continue;
                recycler.inventory.onItemAddedRemoved -= handler;
            }

            AutoStartAction.Clear();
        }
               

        void TheAction(Item item, bool added, Recycler recycler)
        {
            if (!added) return;
            if (!recycler.HasRecyclable()) return;
            recycler.StartRecycling();
        }

        #endregion
    }
}
 