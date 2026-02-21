/*
*  <----- End-User License Agreement ----->
*  
*  You may not merge, publish, distribute, sublicense, or sell copies of This Software without the Developer’s consent. Copy or modify is allowed for personal use only.
*  
*  THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, 
*  THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS 
*  BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE 
*  GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT 
*  LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*
*  Developer: nivex (mswenson82@yahoo.com)
*
*  Copyright © nivex. All rights reserved.

███▄    ██ ██▓ ██▒   ██▒▓█████ ██▓    ██▓
 ██ ▀█  ░█ ▓██▒▓██░   ██▒▓█  ▀   ▓██▒██▓
▓██  ▀█ ██▒▒██▒ ▓██  █▒░▒████     ▒██▒
▓██▒  ▐▌██▒░██░  ▒██ █░░▒▓█  ▄   ░██░██░
▒██░   ▓██░░██░   ▒▀█░  ░▒████▒░██░   ██▓
░ ▒░   ▒ ▒ ░▓     ░ ▐░  ░░ ▒░ ░░▓     ▓ ░
░ ░░   ░ ▒░ ▒ ░   ░ ░░   ░ ░  ░ ▒ ░   ▒ ░ 
   ░   ░ ░  ▒ ░     ░░     ░    ▒ ░   ▒
         ░  ░        ░     ░  ░ ░     ░
*/

using Facepunch;
using Facepunch.Math;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust;
using Oxide.Game.Rust.Cui;
using Rust;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using static Oxide.Plugins.AbandonedBasesExtensionMethods.ExtensionMethods;

namespace Oxide.Plugins
{
    [Info("Abandoned Bases", "nivex", "2.2.6")]
    [Description("Allows bases to become raidable when the owner becomes inactive")]
    public class AbandonedBases : RustPlugin
    {
        #region Variables
        [PluginReference] Plugin Backpacks, Economics, ServerRewards, IQEconomic, RaidableBases, Clans, Friends, Notify, AdvancedAlerts, ZoneManager, SkillTree, LimitEntities, XLevels;

        private new const string Name = "Abandoned Bases";
        private List<string> ID_FLOORS = new() { "floor", "floor.frame", "floor.grill", "floor.ladder.hatch", "floor.triangle", "floor.triangle.frame", "floor.triangle.grill", "floor.triangle.ladder.hatch" };
        private List<string> TrueDamage = new() { "Barricade", "SimpleBuildingBlock", "IceFence", "TeslaCoil", "BaseTrap", "GunTrap", "FlameTurret", "FogMachine", "SamSite", "AutoTurret", "Landmine" };
        private Coroutine reportCoroutine;

        private bool isLoaded { get; set; }
        private bool DebugMode { get; set; }
        private bool newSave { get; set; }
        private bool IsPurgeEnabled { get; set; }
        private StoredData data { get; set; } = new();
        private StringBuilder _sb { get; set; } = new();
        private Coroutine abandonedCoroutine { get; set; }
        private List<string> _waitingList { get; set; } = new();
        private List<UserConversion> _conversions { get; set; } = new();
        private List<AbandonedBuilding> AbandonedBuildings { get; set; } = new();
        private Dictionary<ulong, DelaySettings> PvpDelay { get; set; } = new();
        private Dictionary<ulong, List<Notification>> _notifications { get; set; } = new();
        private Dictionary<ulong, AbandonedBuilding> AbandonedReferences { get; set; } = new();
        public enum ConversionType { Attack, Automated, SAR, Type4 }
        public enum EventType { Base, LegacyShelter, Tugboat }
        public enum SphereColor { None, Blue, Cyan, Green, Magenta, Purple, Red, Yellow }
        public enum SkippedReason { None, Ally, CannotPurgeYet, EventInProgress, Excluded, NewlyAdded, NoPermission, NoPlayerOwner, NoPrivilege, NoPurge, Null, ZoneManager }
        private readonly IPlayer _consolePlayer = new Game.Rust.Libraries.Covalence.RustConsolePlayer();

        public class UserConversion
        {
            public string userid;
            public Coroutine co;
            public HashSet<ulong> owners;
            public UserConversion(string userid, HashSet<ulong> owners, Coroutine co)
            {
                this.owners = owners;
                this.userid = userid;
                this.co = co;
            }
            public bool Exists(IPlayer user, HashSet<ulong> owners)
            {
                if (co == null)
                {
                    return false;
                }
                return userid == user.Id || this.owners.Overlaps(owners);
            }
        }

        public class Notification
        {
            public BasePlayer player;
            public string messageEx;
        }

        private class StoredData
        {
            [JsonProperty("Last Seen", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<ulong, int> LastSeen { get; set; } = new();

            [JsonProperty("Cooldown Between Conversion", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<ulong, DateTime> CooldownBetweenConversion { get; set; } = new();

            [JsonProperty("Cooldown Between Cancel", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<ulong, DateTime> CooldownBetweenCancel { get; set; } = new();

            [JsonProperty("Cooldown Between Events", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<ulong, DateTime> CooldownBetweenEvents { get; set; } = new();

            [JsonProperty("Activities", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ActivityInfo> Activity { get; set; } = new();

            public DateTime LastRunTime { get; set; } = DateTime.MinValue;

            public int protocol;

            public StoredData() { }
        }

        public class Payment
        {
            public Payment(BasePlayer player, double cost = 0, List<CustomCostOptions> options = null)
            {
                displayName = player.displayName;
                userId = player.userID;
                this.options = options;
                this.player = player;
                this.cost = cost;
            }

            public double cost { get; set; }
            public ulong userId { get; set; }
            public string displayName { get; set; }
            public List<CustomCostOptions> options { get; set; }
            public BasePlayer player { get; set; }
        }

        internal class ActivityInfo
        {
            [JsonProperty(PropertyName = "owners", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public HashSet<ulong> owners { get; set; } = new();

            [JsonProperty(PropertyName = "permission")]
            public string perm { get; set; } = string.Empty;

            [JsonProperty(PropertyName = "total")]
            public int total { get; set; }

            internal ActivityInfo() { }

            public bool SameOwners(HashSet<ulong> owners) => owners.All(this.owners.Contains);

            internal int GetLimit(Configuration config)
            {
                if (string.IsNullOrEmpty(perm) || !perm.PermissionExists() || !owners.Exists(owner => owner.HasPermission(perm)))
                {
                    CheckPermission(config);
                }
                foreach (var purge in config.Purges)
                {
                    if (perm == purge.Permission)
                    {
                        return purge.NoPurge ? 0 : purge.Limit;
                    }
                }
                return 0;
            }

            internal void CheckPermission(Configuration config)
            {
                var limit = int.MinValue;
                foreach (var owner in this.owners)
                {
                    var purge = PurgeSettings.Find(config, owner);
                    if (purge == null || string.IsNullOrEmpty(purge.Permission))
                    {
                        continue;
                    }
                    if (purge.Limit > limit)
                    {
                        perm = purge.Permission;
                        limit = purge.Limit;
                    }
                    if (purge.Limit <= 0)
                    {
                        perm = purge.Permission;
                        break;
                    }
                }
            }
        }

        public class DelaySettings
        {
            internal AbandonedBuilding Building { get; set; }
            public Timer Timer { get; set; }
            public float time { get; set; }
            public bool valid => Building != null && !Building.isDestroyed;
            public void Destroy()
            {
                if (Timer != null && !Timer.Destroyed)
                {
                    Timer.Callback();
                    Timer.Destroy();
                }
            }
        }

        internal class AbandonedBuilding : FacepunchBehaviour
        {
            public bool Equals(AbandonedBuilding other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return _guid.Equals(other._guid);
            }

            public override bool Equals(object obj)
            {
                return obj is AbandonedBuilding other && Equals(other);
            }

            public override int GetHashCode()
            {
                return _guid.GetHashCode();
            }

            public static bool operator ==(AbandonedBuilding left, AbandonedBuilding right)
            {
                if (ReferenceEquals(left, null))
                    return ReferenceEquals(right, null);
                return left.Equals(right);
            }

            public static bool operator !=(AbandonedBuilding left, AbandonedBuilding right)
            {
                return !(left == right);
            }

            internal class EntityOwner
            {
                public BaseEntity Entity;
                public ulong OwnerID;
                public EntityOwner(BaseEntity entity)
                {
                    Entity = entity;
                    OwnerID = entity.OwnerID;
                }
            }

            internal class Raider
            {
                public ulong userid;
                public string username;
                public bool IsParticipant;
                public bool HasEntered;
                public bool IsAdmin;
                public float activeDuration;
                public float lastActiveTime;
                public Vector3 lastPosition;
                public BasePlayer _player;
                public BasePlayer player { get { if (_player == null) { _player = RustCore.FindPlayerById(userid); } return _player; } }
                public Raider(BasePlayer target, float duration)
                {
                    activeDuration = duration * 60f;
                    _player = target;
                    IsAdmin = target.IsAdmin;
                    userid = target.userID;
                    username = target.displayName;
                }
                public float PlayerActivityTimeLeft()
                {
                    if (activeDuration <= 0f)
                    {
                        return float.PositiveInfinity;
                    }
                    return activeDuration - (Time.time - lastActiveTime);
                }
            }

            internal List<ulong> IsAllowed { get; set; } = new();
            internal List<ulong> Messages { get; set; } = new();
            internal HashSet<uint> buildingIDs { get; set; } = new();
            internal List<BuildingPrivlidge> privs { get; set; } = new();
            internal BuildingPrivlidge primaryPriv { get; set; }
            internal HashSet<ulong> owners { get; set; } = new();
            internal HashSet<ulong> others { get; set; } = new();
            internal List<BaseEntity> entities { get; set; } = new();
            internal List<SphereEntity> spheres { get; set; } = new();
            internal List<Vector3> compound { get; set; } = new();
            internal List<Vector3> foundations { get; set; } = new();
            internal Dictionary<ulong, BasePlayer> intruders { get; set; } = new();
            internal Dictionary<ulong, Raider> raiders { get; set; } = new();
            internal List<StorageContainer> containers { get; set; } = new();
            internal Dictionary<ulong, EntityOwner> EntityOwners { get; set; } = new();
            internal MapMarkerGenericRadius genericMarker;
            internal VendingMachineMapMarker vendingMarker;
            internal BuildingPrivlidge mainPriv;
            internal bool IsOwnerLocked { get; set; }
            internal bool IsDamaged { get; set; }
            internal bool DisableEventCooldown { get; set; }
            internal PluginTimers timer { get; set; }
            internal BaseCombatEntity anchor { get; set; }
            internal Tugboat tugboat { get; set; }
            internal SimplePrivilege simplePriv { get; set; }
            internal DateTime DespawnDateTime { get; set; }
            internal float radius { get; set; }
            internal bool AutomatedEvent { get; set; }
            internal bool AttackEvent { get; set; }
            internal bool markerCreated { get; set; }
            internal bool isDestroyed { get; set; }
            internal bool IsClaimed { get; set; }
            internal bool CanShowDiscordEnd { get; set; } = true;
            internal bool AllowPVP { get; set; }
            internal bool IsExpired { get; set; }
            internal SphereCollider _collider { get; set; }
            internal Payment payment { get; set; }
            internal List<string> groups { get; set; } = new();
            internal GameObject go { get; set; }
            internal float cancelCooldownTime { get; set; }
            internal bool isCanceled { get; set; }
            internal string raiderName { get; set; }
            internal ulong raiderId { get; set; }
            internal string currentName { get; set; }
            internal ulong currentId { get; set; }
            internal string previousName { get; set; }
            internal ulong previousId { get; set; }
            internal bool privSpawned;
            internal bool canReassign { get; set; } = true;
            internal ActivityInfo activity { get; set; }
            internal int lootAmount { get; set; }
            internal Coroutine _coroutine { get; set; }
            internal AbandonedBases Instance { get; set; }
            internal bool LockBaseToFirstAttacker => AllowPVP ? config.Abandoned.LockBaseToFirstAttackerPVP : config.Abandoned.LockBaseToFirstAttackerPVE;
            internal bool EjectFromLockedBase;
            internal string GetGrid() => PositionToGrid(center);
            internal Vector3 _center { get; set; }
            public EventType eventType;
            private Configuration config => Instance.config;
            private StoredData data => Instance.data;
            public bool HasEventCooldown(BasePlayer player, bool reply = true) => Instance.HasEventCooldown(player, this, reply);
            public bool HasCooldown(BasePlayer player, string perm) => Instance.HasCooldown(player, player.userID, perm, data.CooldownBetweenEvents);
            public void Message(BasePlayer player, string key, params object[] args) => Instance.Message(player, key, args);
            public void LogToFile(string filename, string text) => Instance.LogToFile(filename, text, Instance, false, true);

            internal Vector3 center
            {
                get
                {
                    if (!anchor.IsKilled())
                    {
                        _center = anchor.transform.position;
                    }
                    return _center;
                }
                set
                {
                    _center = value;
                }
            }

            public bool IsAbandonedSleeper(BasePlayer victim)
            {
                if (!config.PlayersCanKillAbandonedSleepers || !AutomatedEvent || victim.IsConnected) return false;
                return owners.Contains(victim.userID) || (!Instance.IsUserExcluded(victim.userID) && Instance.CanPurge(victim.userID, Epoch.Current));
            }

            public bool InRange2D(Vector3 from, float x = 0f) => AbandonedBases.InRange2D(from, center, radius + x);

            public bool InRange3D(Vector3 from, float x = 0f) => AbandonedBases.InRange3D(from, center, radius + x);

            public bool NearCompound(Vector3 from) => compound.Exists(to => AbandonedBases.InRange3D(from, to, 3f));

            public bool NearFoundation3D(Vector3 from, float dist = 3f) => foundations.Exists(to => AbandonedBases.InRange3D(from, to, dist));

            public bool NearFoundation2D(Vector3 from, float dist = 3f) => Mathf.Abs(center.y - from.y) <= radius && foundations.Exists(to => AbandonedBases.InRange2D(from, to, dist));

            public bool NearFoundation3D(BasePlayer player, float radius = 3f)
            {
                if (player.HasParent() && player.GetParentEntity() is Tugboat tugboat)
                {
                    return Instance.GetVehiclePrivilege(tugboat.children) == simplePriv;
                }
                return NearFoundation3D(player.transform.position, radius);
            }

            public Raider GetRaider(BasePlayer player)
            {
                if (!raiders.TryGetValue(player.userID, out var ri))
                {
                    raiders[player.userID] = ri = new(player, InactivePlayerActivityTime);
                }
                return ri;
            }

            private List<Raider> _raiders = new();
            public List<Raider> GetRaiders()
            {
                _raiders.Clear();
                foreach (var x in raiders.Values)
                {
                    if (IsEligible(x.player))
                    {
                        _raiders.Add(x);
                    }
                }
                return _raiders;
            }

            private List<BasePlayer> _intruders = new();
            public List<BasePlayer> GetIntruders()
            {
                _intruders.Clear();
                foreach (var x in intruders.Values)
                {
                    if (IsEligible(x))
                    {
                        _intruders.Add(x);
                    }
                }
                return _intruders;
            }

            private List<ulong> _intruderIds = new();
            public List<ulong> GetIntruderIds()
            {
                _intruderIds.Clear();
                foreach (var x in GetIntruders())
                {
                    _intruderIds.Add(x.userID);
                }
                return _intruderIds;
            }

            private List<ulong> _participantIds = new();
            public List<ulong> GetParticipantIds()
            {
                _participantIds.Clear();
                foreach (var x in GetEligibleParticipants())
                {
                    _participantIds.Add(x.userid);
                }
                return _participantIds;
            }

            private List<BasePlayer> _participants = new();
            public List<BasePlayer> GetParticipants()
            {
                _participants.Clear();
                foreach (var raider in GetEligibleParticipants())
                {
                    if (raider.player != null)
                    {
                        _participants.Add(raider.player);
                    }
                }
                return _participants;
            }

            private List<Raider> _eligible = new();
            public List<Raider> GetEligibleParticipants()
            {
                _eligible.Clear();
                foreach (var raider in GetRaiders())
                {
                    bool isEligible = raider switch
                    {
                        { IsParticipant: false } => false,
                        { IsAdmin: true } when config.Abandoned.RemoveAdminRaiders => false,
                        { player: null } => true,
                        { player: { IsFlying: true, limitNetworking: true } } => false,
                        _ => true
                    };
                    if (isEligible)
                    {
                        _eligible.Add(raider);
                    }
                }
                return _eligible;
            }

            private bool IsEligible(BasePlayer player) => player switch
            {
                null or { IsDestroyed: true } => false,
                { IsAdmin: true } when config.Abandoned.RemoveAdminRaiders => false,
                { IsFlying: true, limitNetworking: true } => false,
                _ => true
            };

            public bool IsParticipant(ulong userid) => raiders.TryGetValue(userid, out var raider) && raider.IsParticipant;

            public void HandleTurretSight(BasePlayer player)
            {
                if (turrets.Count > 0)
                {
                    turrets.RemoveAll(x => x == null || x.turret.IsKilled());
                    foreach (var x in turrets)
                    {
                        if (x.turret.sightRange > config.Abandoned.AutoTurret.SightRange)
                        {
                            SetupSightRange(x.turret, config.Abandoned.AutoTurret.SightRange);
                        }
                        if (x.turret.target != null && x.turret.target == player)
                        {
                            x.turret.SetTarget(null);
                        }
                    }
                }
            }

            public void AddEntity(BaseEntity entity)
            {
                Instance.AbandonedReferences[entity.net.ID.Value] = this;

                if (owners.Contains(entity.OwnerID))
                {
                    entities.Add(entity);
                }
            }

            public static bool AddRange(AbandonedBases m, List<BaseEntity> entities, List<StorageContainer> containers, List<Vector3> foundations, List<Vector3> floors, List<Vector3> compound, List<Vector3> walls, IPlayer user, ulong userid, ConversionType type, int minLoot, ref int loot)
            {
                for (int i = entities.Count - 1; i >= 0; i--)
                {
                    BaseEntity e = entities[i];
                    if (e.IsKilled())
                    {
                        entities.Remove(e);
                        continue;
                    }

                    var position = e.transform.position;
                    if (e is BuildingBlock)
                    {
                        compound.Add(position);
                    }

                    if (e.ShortPrefabName == "foundation" || e.ShortPrefabName == "foundation.triangle")
                    {
                        if (m.config.Abandoned.Twig || e is BuildingBlock block && block.grade != BuildingGrade.Enum.Twigs)
                        {
                            foundations.Add(position);
                        }
                    }
                    else if (e.ShortPrefabName.Contains("external.high"))
                    {
                        compound.Add(position);
                    }
                    else if (e.ShortPrefabName == "wall" || e.ShortPrefabName == "wall.half" || e.ShortPrefabName == "wall.window")
                    {
                        if (m.config.Abandoned.Twig || e is BuildingBlock block && block.grade != BuildingGrade.Enum.Twigs)
                        {
                            walls.Add(position);
                        }
                    }
                    else if (m.ID_FLOORS.Contains(e.ShortPrefabName))
                    {
                        if (!e.children.IsNullOrEmpty() && e.children.Exists(x => x is CollectibleEntity col && col != null && col.itemList == null))
                        {
                            foundations.Add(position);
                            compound.Add(position);
                        }
                        floors.Add(position);
                    }

                    if (minLoot > 0)
                    {
                        var container = e as IItemContainerEntity;

                        if (container?.inventory?.itemList != null)
                        {
                            loot += container.inventory.itemList.Count;
                        }
                    }
                }

                int foundationLimit = type == ConversionType.SAR ? m.config.Abandoned.FoundationLimitSAR : m.config.Abandoned.FoundationLimit;
                int wallLimit = type == ConversionType.SAR ? m.config.Abandoned.WallLimitSAR : m.config.Abandoned.WallLimit; 

                if (foundations.Count < foundationLimit || walls.Count < wallLimit)
                {
                    return m.IsPurgeEnabled && m.config.Abandoned.GetDespawnSeconds(type == ConversionType.Automated || type == ConversionType.Attack) <= 0f && user == null && !m.CanPurge(userid, Epoch.Current); // allow when auto converting bases that are built during purge
                }

                AddContainers(entities, containers, foundations);

                return minLoot <= 0 || loot >= minLoot;
            }

            public static bool AddRange(List<BaseEntity> entities, List<StorageContainer> containers, List<Vector3> compound, int minLoot, ref int loot)
            {
                List<BaseEntity.Slot> _checkSlots = new() { BaseEntity.Slot.Lock, BaseEntity.Slot.UpperModifier, BaseEntity.Slot.MiddleModifier, BaseEntity.Slot.LowerModifier };

                for (int i = entities.Count - 1; i >= 0; i--)
                {
                    BaseEntity e = entities[i];
                    if (e.IsKilled())
                    {
                        entities.Remove(e);
                        continue;
                    }

                    foreach (var checkSlot in _checkSlots)
                    {
                        var slot = e.GetSlot(checkSlot);
                        if (slot == null) continue;
                        if (entities.Contains(slot)) continue;
                        entities.Add(slot);
                    }

                    if (minLoot > 0)
                    {
                        var container = e as IItemContainerEntity;

                        if (container?.inventory?.itemList != null)
                        {
                            loot += container.inventory.itemList.Count;
                        }
                    }

                    compound.Add(e.transform.position);
                }

                AddContainers(entities, containers, compound);

                return minLoot <= 0 || loot >= minLoot;
            }

            private static void AddContainers(List<BaseEntity> entities, List<StorageContainer> containers, List<Vector3> foundations)
            {
                if (foundations.Count > 0)
                {
                    Vector3 lowestFoundation = foundations[0];

                    foreach (var foundation in foundations)
                    {
                        if (foundation.y < lowestFoundation.y)
                        {
                            lowestFoundation = foundation;
                        }
                    }

                    foreach (var e in entities)
                    {
                        if (!(e is StorageContainer container))
                        {
                            continue;
                        }
                        if (!IsBox(e) && !(e is BuildingPrivlidge))
                        {
                            continue;
                        }
                        if (e == null || e.IsDestroyed || !e.isSpawned || e.transform.position.y + 3f < lowestFoundation.y)
                        {
                            continue;
                        }
                        containers.Add(container);
                    }
                }
            }

            public void TrySetOwner(IPlayer user)
            {
                if (user == null || user.IsServer)
                {
                    return;
                }
                var player = user.Object as BasePlayer;
                if (player == null || !IsEligible(player))
                {
                    return;
                }
                GetRaider(player).IsParticipant = true;
                SetOwner(player.userID, player.displayName, player.userID, player.displayName);
            }

            public void ResetOwner()
            {
                raiders.Remove(previousId);
                IsOwnerLocked = false;
                canReassign = true;
                raiderId = 0uL;
                raiderName = null; 
                SetOwner(previousId, previousName, previousId, previousName);
                UpdateMarkers();
            }

            public void TrySetOwnerLock(BasePlayer attacker, bool canEarnParticipation)
            {
                if (!attacker.IsHuman() || !IsEligible(attacker))
                {
                    return;
                }
                if (IsOwnerLocked && !CanBypass(attacker) && !IsAlly(attacker))
                {
                    TryEjectFromLockedBase(attacker);
                    return;
                }
                if (IsEventCompleted && IsParticipant(attacker.userID))
                {
                    return;
                }
                if (HasEventCooldown(attacker))
                {
                    return;
                }
                if (canEarnParticipation)
                {
                    GetRaider(attacker).IsParticipant = true;
                }
                if (owners.Contains(attacker.userID) || IsAlly(attacker))
                {
                    return;
                }
                if (string.IsNullOrEmpty(raiderName))
                {
                    raiderName = attacker.displayName;
                    raiderId = attacker.userID;
                    GetRaider(attacker).IsParticipant = true;
                }
                if (canReassign && LockBaseToFirstAttacker)
                {
                    IsOwnerLocked = true;
                    canReassign = false;
                    SetOwner(attacker.userID, attacker.displayName, currentId, currentName);
                    GetRaider(attacker).IsParticipant = true;
                }
                Invoke(UpdateMarkers, 0f);
            }

            public static bool CanBypass(BasePlayer player)
            {
                return !player.IsHuman() || player.IsFlying || player.limitNetworking || player.HasPermission("abandonedbases.canbypass");
            }

            public bool CanDamageBuilding(BasePlayer player)
            {
                return owners.Contains(player.userID) || IsEventCompleted && IsParticipant(player.userID);
            }

            public bool IsOwner(BasePlayer player)
            {
                return owners.Contains(player.userID) || CanBypass(player) || IsAlly(player);
            }

            public void SetOwner(ulong newid, string newname, ulong currid, string currname)
            {
                previousName = currname;
                previousId = currid;
                currentName = newname;
                currentId = newid;
            }

            public bool EnforceCooldownBetweenEvents(BasePlayer player)
            {
                return !IsParticipant(player.userID) && Instance.EnforceCooldownBetweenEvents(player);
            }

            public void TryEjectFromLockedBase(BasePlayer player)
            {
                if (!EjectFromLockedBase || !IsOwnerLocked || owners.Contains(player.userID) && !EnforceCooldownBetweenEvents(player))
                {
                    return;
                }
                if (anchor is not Tugboat && !NearFoundation2D(player.transform.position))
                {
                    return;
                }
                if (anchor is Tugboat && !(player.GetParentEntity() is Tugboat))
                {
                    return;
                }
                RemovePlayer(player);
            }

            public static bool IsWearingJetpack(BasePlayer player) => player != null && player.GetMounted() is BaseMountable m && IsJetpack(m);

            public static bool IsJetpack(BaseMountable m) => (m.ShortPrefabName == "testseat" || m.ShortPrefabName == "standingdriver") && m.GetParentEntity() is DroppedItem;

            public void RemovePlayer(BasePlayer player)
            {
                bool jetpack = IsWearingJetpack(player);
                var m = player.GetMounted();
                if (m != null)
                {
                    m.DismountPlayer(player, true);
                }
                if (jetpack)
                {
                    player.DismountObject();
                }
                else if (player.HasParent())
                {
                    player.SetParent(null);
                }
                var position = GetEjectLocation(player.transform.position, 10f, center, radius);
                player.Teleport(player.IsFlying ? position.WithY(player.transform.position.y) : position);
                player.SendNetworkUpdateImmediate();
                intruders.Remove(player.userID);
            }

            public static Vector3 GetEjectLocation(Vector3 a, float distance, Vector3 target, float radius)
            {
                var position = ((a.XZ3D() - target.XZ3D()).normalized * (radius + distance)) + target; // credits ZoneManager
                float y = TerrainMeta.HighestPoint.y + 250f;

                if (Physics.Raycast(position + new Vector3(0f, y), Vector3.down, out var hit, Mathf.Infinity, 10551313, QueryTriggerInteraction.Ignore))
                {
                    position.y = hit.point.y + 0.75f;
                }
                else position.y = Mathf.Max(TerrainMeta.HeightMap.GetHeight(position), TerrainMeta.WaterMap.GetHeight(position)) + 0.75f;

                return position;
            }

            public void TryMessage(BasePlayer player, float time, string key, params object[] args)
            {
                if (Messages.Contains(player.userID)) return;
                ulong userid = player.userID;
                Messages.Add(userid);
                timer.Once(time, () => Messages.Remove(userid));
                Message(player, key, args);
            }

            internal readonly Guid _guid;

            public AbandonedBuilding()
            {
                _guid = Guid.NewGuid();
            }

            public void Setup(AbandonedBases m, IPlayer user, PluginTimers timer, List<Vector3> compound, List<Vector3> foundations, List<BaseEntity> entities, List<StorageContainer> containers, HashSet<ulong> owners, HashSet<ulong> others, Vector3 center, Payment payment, float radius, bool anon, bool allowPVP, ConversionType type)
            {
                Instance = m;
                if (!anon)
                {
                    TrySetOwner(user);
                }
                AttackEvent = type == ConversionType.Attack;
                AutomatedEvent = AttackEvent || type == ConversionType.Automated;
                if (m.IsPurgeEnabled)
                {
                    AutomatedEvent = false; // GRIMM: Force purge events to be treated as manual
                }
                markerCreated = user == null && !config.Abandoned.AutoMarkers || user != null && !config.Abandoned.ManualMarkers;
                canReassign = currentId == 0 || string.IsNullOrEmpty(currentName);
                EjectFromLockedBase = allowPVP ? config.Abandoned.EjectLockedPVP : config.Abandoned.EjectLockedPVE;

                this.Instance.AbandonedBuildings.Add(this);
                this.AllowPVP = allowPVP;
                this.compound = compound;
                this.foundations = foundations;
                this.containers = containers;
                this.entities = entities;
                this.payment = payment;
                this.owners = owners;
                this.others = others;
                this.radius = radius;
                this.timer = timer;
                this.center = center;

                others.RemoveWhere(owners.Contains);

                Interface.Oxide.NextTick(() =>
                {
                    if (m.CanDebug(user))
                    {
                        user.Message($"Conversion finalizing event...");
                    }

                    if (!SetupCollider())
                    {
                        DestroyMe();
                        return;
                    }

                    TryInvokeMethod(SetupEntities);
                    TryInvokeMethod(CompleteConvertPayment);
                    TryInvokeMethod(Announce);
                    TryInvokeMethod(InvokeDespawnInactive);
                    TryInvokeMethod(SpawnNpcs);
                    TryInvokeMethod(SetupSleepers);
                    UpdateActivity(true);

                    if (m.CanDebug(user))
                    {
                        user.Message($"Conversion setup complete. Event has started");
                    }

                    cancelCooldownTime = Time.time + config.Abandoned.CancelCooldown;

                    if (EjectFromLockedBase)
                    {
                        InvokeRepeating(Protector, 1f, 1f);
                    }

                    if (tugboat != null)
                    {
                        tugboat.shoreDriftTimer = 0f;
                    }
                });
            }

            private void Update()
            {
                if (tugboat != null)
                {
                    tugboat.shoreDriftTimer = 0f;
                }
            }

            private void OnDestroy()
            {
                if (groups.Count > 0)
                {
                    Plugin BotReSpawn = Interface.Oxide.RootPluginManager.GetPlugin("BotReSpawn");
                    groups.ForEach(group => BotReSpawn?.Call("RemoveGroupSpawn", group));
                }
                if (!isDestroyed)
                {
                    DestroyMe();
                }
            }

            private bool IsAlly(BasePlayer player) => Instance.IsAlly(player.userID, currentId);

            private void Protector()
            {
                if (isDestroyed)
                {
                    return;
                }
                if (tugboat != null)
                {
                    tugboat.shoreDriftTimer = 0f;
                }
                if (!IsPlayerActive(currentId))
                {
                    ResetOwner();
                }
                if (intruders.Count == 0)
                {
                    return;
                }
                using var players = intruders.ToPooledList();
                foreach (var (userid, intruder) in players)
                {
                    if (intruder.IsKilled())
                    {
                        intruders.Remove(userid);
                        continue;
                    }
                    if (IsAllowed.Contains(userid))
                    {
                        UpdateIntruderActivity(intruder);
                        continue;
                    }
                    if (!IsParticipant(userid) && EnforceCooldownBetweenEvents(intruder))
                    {
                        TryEjectFromLockedBase(intruder);
                        continue;
                    }
                    if (!IsOwnerLocked)
                    {
                        UpdateIntruderActivity(intruder);
                        continue;
                    }
                    if (IsOwner(intruder))
                    {
                        UpdateIntruderActivity(intruder);
                        IsAllowed.Add(userid);
                        continue;
                    }
                    TryEjectFromLockedBase(intruder);
                }
            }

            private void OnTriggerEnter(Collider collider)
            {
                if (collider == null)
                {
                    return;
                }

                var entity = collider.ToBaseEntity();

                if (entity is BasePlayer player)
                {
                    if (player.IsHuman() && !intruders.ContainsKey(player.userID))
                    {
                        if (HasEventCooldown(player, config.Messages.HasEventCooldown) && config.Abandoned.EjectFromEvent(AllowPVP) && !CanDamageBuilding(player))
                        {
                            RemovePlayer(player);
                            return;
                        }

                        intruders[player.userID] = player;
                        var raider = GetRaider(player);
                        
                        raider.HasEntered = true;

                        if (InactivePlayerActivityTime > 0f)
                        {
                            raider.lastActiveTime = Time.time;
                        }

                        Message(player, AllowPVP ? "OnPlayerEntered" : "OnPlayerEnteredPVE");

                        Interface.CallHook("OnPlayerEnteredAbandonedBase", new object[12] { player, center, radius, AllowPVP, GetIntruders(), GetIntruderIds(), entities, privs, CanDropBackpack, AutomatedEvent, AttackEvent, _guid });
                        OnPlayerEnter(player);
                        UpdateTime(player, true);
                    }
                }
                else if (entity is BaseMountable m)
                {
                    GetMountedPlayers(m).ForEach(player =>
                    {
                        if (!intruders.ContainsKey(player.userID))
                        {
                            if (HasEventCooldown(player, config.Messages.HasEventCooldown) && config.Abandoned.EjectFromEvent(AllowPVP) && !CanDamageBuilding(player))
                            {
                                RemovePlayer(player);
                                return;
                            }

                            intruders[player.userID] = player;
                            var raider = GetRaider(player);
                            
                            raider.HasEntered = true;

                            if (LockBaseToFirstAttacker)
                            {
                                raider.lastActiveTime = Time.time;
                            }

                            Message(player, AllowPVP ? "OnPlayerEntered" : "OnPlayerEnteredPVE");

                            Interface.CallHook("OnPlayerEnteredAbandonedBase", new object[12] { player, center, radius, AllowPVP, GetIntruders(), GetIntruderIds(), entities, privs, CanDropBackpack, AutomatedEvent, AttackEvent, _guid });
                            UpdateTime(player, true);
                        }
                    });
                }
                else if (entity is DroppedItemContainer)
                {
                    Interface.CallHook("OnEntityEnteredAbandonedBase", new object[12] { entity, center, radius, AllowPVP, GetIntruders(), GetIntruderIds(), entities, privs, CanDropBackpack, AutomatedEvent, AttackEvent, _guid });
                }
            }

            private void OnTriggerExit(Collider collider)
            {
                if (collider == null)
                {
                    return;
                }

                var entity = collider.ToBaseEntity();

                if (entity is BasePlayer player)
                {
                    OnPlayerExit(player, player.IsDead());
                }
                else if (entity is BaseMountable m)
                {
                    GetMountedPlayers(m).ForEach(player => OnPlayerExit(player, player.IsDead()));
                }
            }

            public void DestroyMe()
            {
                isDestroyed = true;
                TryInvokeMethod(CancelEntitySetup);
                TryInvokeMethod(KillMarkers);
                TryInvokeMethod(DestroySpheres);
                TryInvokeMethod(RewardPlayers);
                TryInvokeMethod(PowerDownAutoTurrets);
                TryInvokeMethod(RestoreEntityOwners);
                TryInvokeMethod(RemoveReferences);
                TryInvokeMethod(CancelInvokes);
                if (eventType == EventType.Base)
                {
                    Destroy(go);
                }
                Destroy(this);
            }

            public void OnClaimed(BasePlayer player, string hook)
            {
                Interface.CallHook(hook, new object[12] { player, center, radius, AllowPVP, GetParticipants(), GetParticipantIds(), entities, privs, CanDropBackpack, AutomatedEvent, AttackEvent, _guid });
            }

            private void SetupSleepers()
            {
                if (!config.MoveInventory)
                {
                    return;
                }
                Instance.SleeperKillList.RemoveAll(x => x == null || x.IsConnected || !x.IsSleeping());
                var boxes = containers.Where(x => !x.IsKilled() && IsBox(x) && !x.inventory.IsFull());
                if (boxes.Count == 0)
                {
                    return;
                }
                foreach (var entity in entities)
                {
                    if (entity == null) continue;
                    var target = entity as BasePlayer;
                    if (target.IsKilled()) continue;
                    if (!target.IsSleeping() || target.IsConnected) continue;
                    using var items = target.GetPlayerItems();
                    while (items.Count > 0 && boxes.Count > 0)
                    {
                        var box = boxes.GetRandom();
                        if (box.inventory.IsFull())
                        {
                            boxes.Remove(box);
                            continue;
                        }
                        Item item = items[0];
                        items.Remove(item);
                        if (item == null)
                        {
                            continue;
                        }
                        if (config.MoveInventoryBlacklist.Contains(item.info.shortname) || !item.MoveToContainer(box.inventory))
                        {
                            item.RemoveFromContainer();
                            item.Remove(0f);
                        }
                    }
                    if (Instance.SleeperKillList.Remove(target))
                    {
                        target.Die(new HitInfo(target, target, DamageType.Suicide, 1000f));
                    }
                }
            }

            private bool rewarded;
            public void RewardPlayers()
            {
                if (IsClaimed || rewarded)
                {
                    return;
                }
                rewarded = true;
                var raiders = GetEligibleParticipants();
                foreach (var raider in raiders)
                {
                    if (!isCanceled)
                    {
                        Instance.GiveRewards(raider.player, raider.userid, raiders.Count, eventType);
                    }
                    if (!DisableEventCooldown && config.Abandoned.ResetEventCooldownInRewards)
                    {
                        SetEventCooldown(raider.userid);
                    }
                }
                if (isCanceled || Instance.IsUnloading)
                {
                    return;
                }
                ShowRaidCompletedMessages(raiders);
            }

            private bool shownRaidCompletedMessages;
            private void ShowRaidCompletedMessages(List<Raider> raiders = null)
            {
                if (shownRaidCompletedMessages) return;
                shownRaidCompletedMessages = true;
                if (raiders == null) raiders = GetEligibleParticipants();
                var activeId = raiderId == 0uL ? currentId : raiderId;
                var activeName = string.IsNullOrEmpty(raiderName) ? currentName : raiderName;
                var players = string.Join(", ", raiders.Select(x => x.username));
                string text;
                if (raiders.Count == 0) text = Instance.GetMessage("Output Uncontested", null)
                    .Replace("{activeName}", activeName ?? Instance.GetUserName(activeId) ?? "UNKNOWN")
                    .Replace("{activeId}", activeId.ToString())
                    .Replace("{center}", center.ToString())
                    .Replace("{grid}", GetGrid())
                    .Replace("{previousName}", previousName ?? Instance.GetUserName(previousId) ?? "UNKNOWN")
                    .Replace("{previousId}", previousId.ToString());
                else text = Instance.GetMessage("Output Completed", null)
                    .Replace("{activeName}", activeName ?? Instance.GetUserName(activeId) ?? "UNKNOWN")
                    .Replace("{activeId}", activeId.ToString())
                    .Replace("{players}", players)
                    .Replace("{center}", center.ToString())
                    .Replace("{grid}", GetGrid())
                    .Replace("{previousName}", previousName ?? Instance.GetUserName(previousId) ?? "UNKNOWN")
                    .Replace("{previousId}", previousId.ToString());
                if (CanShowDiscordEnd && config.Discord.End)
                {
                    Instance.SendDiscordMessage(center, text);
                }
                if (config.UseLogFile && activeName != previousName)
                {
                    LogToFile("sar", text);
                }
                Puts(text);
            }

            private void RestoreEntityOwners()
            {
                UpdateExpirationStatus();

                if (IsExpired || IsClaimed || EntityOwners.Count == 0)
                {
                    return;
                }

                foreach (var (userid, eo) in EntityOwners.ToList())
                {
                    if (eo.Entity.IsKilled()) continue;
                    eo.Entity.OwnerID = eo.OwnerID;
                }
            }

            private void RemoveReferences() => Instance.RemoveReferences(this, entities);

            private void CancelInvokes() { try { CancelInvoke(DestroyAll); } catch { } }

            public void KillMarkers()
            {
                vendingMarker.SafelyKill();
                genericMarker.SafelyKill();
            }

            public void DestroySpheres()
            {
                spheres.ForEach(SafelyKill);
            }

            public void KillCollider()
            {
                if (_collider != null)
                {
                    DestroyImmediate(_collider);
                }
            }

            private void CompleteConvertPayment() => CompletePayment(payment, false);

            public void CompleteCancelPayment(Payment payment) => CompletePayment(payment, true);

            public void SetConversionCooldown(ulong userid)
            {
                if (!Instance.IgnoreConversionCooldowns() && config.Abandoned.CooldownBetweenConversion > 0 && !userid.HasPermission("abandonedbases.convert.nocooldown"))
                {
                    data.CooldownBetweenConversion[userid] = DateTime.Now.AddSeconds(config.Abandoned.CooldownBetweenConversion);
                }
            }

            public void SetCancelCooldown(ulong userid)
            {
                if (!Instance.IgnoreCancelCooldowns() && config.Abandoned.CooldownBetweenCancel > 0 && !userid.HasPermission("abandonedbases.convert.cancel.nocooldown"))
                {
                    data.CooldownBetweenCancel[userid] = DateTime.Now.AddSeconds(config.Abandoned.CooldownBetweenCancel);
                }
            }

            public void SetEventCooldown(ulong userid)
            {
                if (!Instance.IgnoreEventCooldowns() && config.Abandoned.CooldownBetweenEvents > 0 && !userid.HasPermission("abandonedbases.noeventcooldown"))
                {
                    data.CooldownBetweenEvents[userid] = DateTime.Now.AddSeconds(config.Abandoned.CooldownBetweenEvents);
                }
            }

            public void SetEventCooldowns()
            {
                if (!DisableEventCooldown)
                {
                    GetEligibleParticipants().ForEach(raider => SetEventCooldown(raider.userid));
                }
            }

            private void CompletePayment(Payment payment, bool cancel)
            {
                if (cancel)
                {
                    isCanceled = Instance.GetRewards(eventType).Cancel;
                }

                if (payment == null || payment.cost == 0 && !payment.options.IsValid())
                {
                    return;
                }

                var eco = cancel ? config.Abandoned.EconomicsCancel : config.Abandoned.Economics;

                if (eco > 0 && Instance.Economics.CanCall())
                {
                    if (Convert.ToBoolean(Instance.Economics?.Call("Withdraw", payment.userId, payment.cost)))
                    {
                        if (payment.player.IsValid())
                        {
                            Message(payment.player, cancel ? "EconomicsWithdrawCancel" : "EconomicsWithdraw", payment.cost);
                        }
                    }
                }

                if (eco > 0 && Instance.IQEconomic.CanCall())
                {
                    Instance.IQEconomic?.Call("API_REMOVE_BALANCE", payment.userId, (int)payment.cost);
                    if (payment.player.IsValid())
                    {
                        Message(payment.player, cancel ? "EconomicsWithdrawCancel" : "EconomicsWithdraw", payment.cost);
                    }
                }

                var rp = cancel ? config.Abandoned.ServerRewardsCancel : config.Abandoned.ServerRewards;

                if (rp > 0 && Instance.ServerRewards.CanCall())
                {
                    if (Convert.ToBoolean(Instance.ServerRewards?.Call("TakePoints", payment.userId, (int)payment.cost)))
                    {
                        if (payment.player.IsValid())
                        {
                            Message(payment.player, cancel ? "ServerRewardPointsTakenCancel" : "ServerRewardPointsTaken", (int)payment.cost);
                        }
                    }
                }

                if (payment.options.IsValid())
                {
                    TakeCustomCost(payment.player, payment.options, cancel);
                }
            }

            private void TakeCustomCost(BasePlayer player, List<CustomCostOptions> options, bool cancel)
            {
                var sb = new StringBuilder();

                foreach (var option in options)
                {
                    if (option.Amount <= 0) continue;
                    using var slots = DisposableList<Item>();
                    player.inventory.FindItemsByItemID(slots, option.Definition.itemid);
                    var amountLeft = option.Amount;

                    foreach (var slot in slots)
                    {
                        if (slot == null || option.Skin != 0 && slot.skin != option.Skin)
                        {
                            continue;
                        }

                        var taken = slot.amount > amountLeft ? slot.SplitItem(amountLeft) : slot;

                        if (taken == null)
                        {
                            continue;
                        }

                        taken.RemoveFromContainer();
                        taken.Remove(0f);

                        amountLeft -= taken.amount;

                        if (amountLeft <= 0)
                        {
                            string name = string.IsNullOrEmpty(option.Name) ? slot.info.displayName.english : option.Name;
                            sb.Append(string.Format("{0} {1}", option.Amount, name)).Append(", ");
                            break;
                        }
                    }
                }

                if (sb.Length > 2)
                {
                    sb.Length -= 2;

                    Message(player, cancel ? "CustomCostTakenCancel" : "CustomCostTaken", sb.ToString());
                }
            }

            public void Announce()
            {
                if (Instance.IsPurgeEnabled)
                {
                    return;
                }

                var grid = GetGrid();

                foreach (var target in BasePlayer.activePlayerList)
                {
                    if (IsManual() && target.HasPermission("abandonedbases.manualnotices"))
                    {
                        Message(target, "Abandoned Manual", payment.displayName ?? raiderName, grid);
                        continue;
                    }
                    if (target.HasPermission("abandonedbases.notices"))
                    {
                        Message(target, "Abandoned", grid);
                    }
                }
            }

            public bool IsAttached(BaseEntity entity)
            {
                if (NearCompound(entity.transform.position))
                {
                    return true;
                }
                if (entity is DecayEntity decayEntity && buildingIDs.Contains(decayEntity.buildingID))
                {
                    return true;
                }
                if (entity.GetBuildingPrivilege() is BuildingPrivlidge priv && buildingIDs.Contains(priv.buildingID))
                {
                    return true;
                }
                return false;
            }

            public void RememberOwner(BaseEntity entity)
            {
                if (entity.OwnerID == 0 || owners.Contains(entity.OwnerID))
                {
                    Instance.AbandonedReferences[entity.net.ID.Value] = this;
                    EntityOwners[entity.net.ID.Value] = new(entity);
                    var ice = entity as IItemContainerEntity;
                    if (ice == null || ice.inventory == null) return;
                    lootAmount += ice.inventory.itemList.Count;
                }
            }

            private void CancelEntitySetup()
            {
                Interface.CallHook("OnAbandonedBaseEnded", new object[11] { center, radius, AllowPVP, GetParticipants(), GetParticipantIds(), entities, privs, CanDropBackpack, AutomatedEvent, AttackEvent, _guid });
                
                if (_coroutine != null)
                {
                    ServerMgr.Instance.StopCoroutine(_coroutine);
                    _coroutine = null;
                }
            }

            private IEnumerator EntitySetup()
            {
                yield return CoroutineEx.waitForSeconds(0.25f);
                
                Interface.CallHook("OnAbandonedBaseStart", new object[10] { center, radius, AllowPVP, GetIntruders(), GetIntruderIds(), entities, CanDropBackpack, AutomatedEvent, AttackEvent, _guid });

                int checks = 0;
                foreach (var e in entities.ToList())
                {
                    if (++checks % 100 == 0)
                    {
                        yield return CoroutineEx.waitForSeconds(0.025f);
                    }
                    if (ShouldIgnore(e))
                    {
                        entities.Remove(e);
                        continue;
                    }
                    RememberOwner(e);
                    if (e is LegacyShelter shelter && shelter.GetEntityBuildingPrivilege() is SimplePrivilege priv3) SetCurrentNameFromVehicle(priv3);
                    if (e is Tugboat && Instance.GetVehiclePrivilege(e.children) is SimplePrivilege priv2) SetCurrentNameFromVehicle(priv2);
                    if (e is BuildingPrivlidge priv) SetCurrentNameFromBuilding(priv);
                    if (e is DecayEntity decayEntity) SetupDecayEntity(decayEntity);
                    if (e is AutoTurret turret) SetupTurret(turret);
                    if (e is BuildingBlock) SetCurrentNameFromBuilding(e);
                    if (config.Abandoned.LimitEntities.Enabled && Instance.LimitEntities.CanCall()) e.OwnerID = 0;
                    if (config.Abandoned.GetDespawnSeconds(AutomatedEvent) <= 0f && !config.RemoveOwnershipZero) continue;
                    if (config.RemoveOwnership || config.RemoveOwnershipFromContainers && e is StorageContainer) e.OwnerID = 0;
                }

                BuildingManager.Building primaryBuilding = null;
                foreach (var priv in privs)
                {
                    if (priv == null) continue;
                    var building = priv.GetBuilding();
                    if (building == null || !building.HasDecayEntities()) continue;
                    if (primaryBuilding == null || primaryBuilding.decayEntities.Count < building.decayEntities.Count)
                    {
                        primaryPriv = priv;
                        primaryBuilding = building;
                    }
                }

                var type = anchor.IsKilled() ? "BUILDING" : $"{anchor.GetType().Name.ToUpper()}";

                if (!string.IsNullOrEmpty(previousName))
                {
                    string text = string.Format("{0} - {1} ({2}) at {3} with {4} entities and {5} items ({6})", type, previousName, previousId, center, entities.Count, lootAmount, string.Join(", ", owners));
                    Puts(text);
                    LogToFile("start", text);
                }
                else
                {
                    string text = string.Format("{0} - {1} with {2} entities and {3} items ({4})", type, center, entities.Count, lootAmount, string.Join(", ", owners));
                    Puts(text);
                    LogToFile("start", text);
                }

                if (turrets.Count > 0)
                {
                    yield return TurretsCoroutine();
                }

                Interface.CallHook("OnAbandonedBaseStarted", new object[11] { center, radius, AllowPVP, GetIntruders(), GetIntruderIds(), entities, privs, CanDropBackpack, AutomatedEvent, AttackEvent, _guid });

                _coroutine = null;
            }

            private bool ShouldIgnore(BaseEntity e) => e.IsKilled() || e.net == null || config.Abandoned.IgnoredPrefabs.Exists(e.ShortPrefabName.Contains) || e.OwnerID == 0 && e is StorageContainer || e.OwnerID == 0 && e is LootContainer && !NearFoundation2D(e.transform.position) || e.OwnerID != 0 && !owners.Contains(e.OwnerID);

            private void SetCurrentNameFromBuilding(BuildingPrivlidge priv)
            {
                if (string.IsNullOrEmpty(currentName) && priv.OwnerID.IsSteamId() && owners.Contains(priv.OwnerID))
                {
                    var username = Instance.GetUserName(priv.OwnerID);
                    SetOwner(priv.OwnerID, username, priv.OwnerID, username);
                }
                if (!privs.Contains(priv))
                {
                    if (mainPriv == null || IsHigherCount(priv, mainPriv))
                    {
                        mainPriv = priv;
                    }
                    privs.Add(priv);
                    privSpawned = true;
                }
            }

            private void SetCurrentNameFromBuilding(BaseEntity entity)
            {
                if (!string.IsNullOrEmpty(currentName) && privSpawned)
                {
                    return;
                }
                if (entity.OwnerID.IsSteamId() && owners.Contains(entity.OwnerID))
                {
                    string username = Instance.GetUserName(entity.OwnerID);
                    SetOwner(entity.OwnerID, username, entity.OwnerID, username);
                }
                BuildingPrivlidge priv = entity as BuildingPrivlidge;
                if (priv != null)
                {
                    privs.Add(priv);
                    privSpawned = true;
                }
            }

            private void SetCurrentNameFromVehicle(SimplePrivilege priv)
            {
                if (!string.IsNullOrEmpty(currentName)) return;
                foreach (var userid in priv.authorizedPlayers)
                {
                    var username = Instance.GetUserName(userid);
                    if (string.IsNullOrEmpty(username)) continue;
                    SetOwner(userid, username, userid, username);
                    break;
                }
            }

            private void SetupEntities()
            {
                _coroutine = ServerMgr.Instance.StartCoroutine(EntitySetup());
                Instance.Subscribe();
            }

            private void SetupDecayEntity(DecayEntity e)
            {
                buildingIDs.Add(e.buildingID);
            }

            public class TurretInfo
            {
                public Item weapon;
                public AutoTurret turret;
                public List<Item> items = new();
            }

            internal List<TurretInfo> turrets = new();

            private void SetupTurret(AutoTurret turret)
            {
                if (!config.Abandoned.AutoTurret.Enabled)
                {
                    return;
                }

                SetupIO(turret);
                turret.InitializeHealth(config.Abandoned.AutoTurret.Health, config.Abandoned.AutoTurret.Health);
                turret.sightRange = config.Abandoned.AutoTurret.SightRange;
                turret.aimCone = config.Abandoned.AutoTurret.AimCone;

                if (config.Abandoned.AutoTurret.RemoveWeapon)
                {
                    turret.AttachedWeapon = null;
                    Item slot = turret.inventory.GetSlot(0);

                    if (slot != null && (slot.info.category == ItemCategory.Weapon || slot.info.category == ItemCategory.Fun))
                    {
                        slot.RemoveFromContainer();
                        slot.Remove();
                    }
                }

                if (config.Abandoned.AutoTurret.InfiniteAmmo)
                {
                    turret.inventory.onPreItemRemove += new Action<Item>(OnWeaponItemPreRemove);
                }

                if (config.Abandoned.AutoTurret.Hostile)
                {
                    turret.SetPeacekeepermode(false);
                }

                TurretInfo ti = new()
                {
                    turret = turret
                };

                turrets.Add(ti);
            }

            private readonly Dictionary<NetworkableId, SphereCollider> _turretColliders = new();

            public void SetupSightRange(AutoTurret turret, float sightRange, int multi = 1)
            {
                if (turret.net != null && turret.targetTrigger != null)
                {
                    if (!_turretColliders.TryGetValue(turret.net.ID, out var collider) && turret.targetTrigger.TryGetComponent<SphereCollider>(out var val))
                    {
                        _turretColliders[turret.net.ID] = collider = val;
                    }
                    if (collider != null)
                    {
                        if (multi > 1)
                        {
                            turret.Invoke(() =>
                            {
                                if (collider != null) collider.radius = sightRange;
                                if (turret != null) turret.sightRange = sightRange;
                            }, 15f);
                        }
                        collider.radius = sightRange * multi;
                    }
                }
                turret.sightRange = sightRange * multi;
            }

            private IEnumerator TurretsCoroutine()
            {
                using var tmp = turrets.ToPooledList();

                foreach (var ti in tmp)
                {
                    yield return CoroutineEx.waitForSeconds(0.025f);

                    EquipTurretWeapon(ti);

                    yield return CoroutineEx.waitForSeconds(0.025f);

                    UpdateAttachedWeapon(ti.turret);

                    yield return CoroutineEx.waitForSeconds(0.025f);

                    InitiateStartup(ti.turret);

                    yield return CoroutineEx.waitForSeconds(0.025f);

                    FillAmmoTurret(ti);

                    if (ti.turret != null && ti.turret.HasFlag(BaseEntity.Flags.OnFire))
                    {
                        ti.turret.SetFlag(BaseEntity.Flags.OnFire, false);
                    }
                }

                Interface.CallHook("OnAbandonedBaseTurretsInitialized", new object[11] { turrets, center, radius, AllowPVP, raiderId, CanDropBackpack, entities, privs, AutomatedEvent, AttackEvent, _guid });
            }

            private void EquipTurretWeapon(TurretInfo ti)
            {
                var turret = ti.turret;
                if (config.Abandoned.AutoTurret.Shortnames.Count > 0 && !turret.IsKilled() && turret.AttachedWeapon == null)
                {
                    var shortname = config.Abandoned.AutoTurret.Shortnames.GetRandom();
                    var itemToCreate = ItemManager.FindItemDefinition(shortname);

                    if (itemToCreate != null)
                    {
                        Item item = ItemManager.Create(itemToCreate, 1, 0);

                        if (item.MoveToContainer(turret.inventory, 0, false))
                        {
                            item.SwitchOnOff(true);
                            ti.weapon = item;
                        }
                        else
                        {
                            item.Remove();
                        }
                    }
                }
            }

            private void UpdateAttachedWeapon(AutoTurret turret)
            {
                if (!turret.IsKilled())
                {
                    turret.UpdateAttachedWeapon();
                }
            }

            private void InitiateStartup(AutoTurret turret)
            {
                if (!config.Abandoned.AutoTurret.RequiresPower && !turret.IsKilled())
                {
                    turret.InitiateStartup();
                }
            }

            private void PowerDownAutoTurrets()
            {
                foreach (var ti in turrets)
                {
                    if (ti == null || ti.turret.IsKilled())
                    {
                        continue;
                    }
                    if (GetConnectedInput(ti.turret) == null)
                    {
                        ti.turret.InitiateShutdown();
                    }
                    foreach (Item item in ti.items.ToArray())
                    {
                        if (item != null && item.parent != null && item.parent == ti.turret.inventory)
                        {
                            item.RemoveFromContainer();
                            item.Remove(0f);
                        }
                    }
                    if (ti.weapon != null && ti.weapon.parent != null && ti.weapon.parent == ti.turret.inventory)
                    {
                        ti.weapon.GetHeldEntity().SafelyKill();
                        ti.weapon.RemoveFromContainer();
                        ti.weapon.Remove();
                    }
                }
            }

            private IOEntity GetConnectedInput(IOEntity io)
            {
                if (io == null || io.inputs == null)
                {
                    return null;
                }

                foreach (var input in io.inputs)
                {
                    var e = input?.connectedTo?.Get(true);

                    if (e.IsValid())
                    {
                        return e;
                    }
                }

                return null;
            }

            private void OnWeaponItemPreRemove(Item item)
            {
                if (isAuthorized)
                {
                    return;
                }

                var weapon = item.parent?.entityOwner;
                if (weapon is AutoTurret turret)
                {
                    var index = turrets.FindIndex(x => x.turret == turret);
                    if (index != -1)
                    {
                        var ti = turrets[index];
                        weapon.Invoke(() => FillAmmoTurret(ti), 0.1f);
                    }
                }
            }

            private bool isAuthorized;

            private void FillAmmoTurret(TurretInfo ti)
            {
                if (isAuthorized || ti == null || ti.turret.IsKilled())
                {
                    return;
                }

                var attachedWeapon = ti.turret.GetAttachedWeapon();

                if (attachedWeapon == null)
                {
                    ti.turret.Invoke(() => FillAmmoTurret(ti), 0.2f);
                    return;
                }

                if (_coroutine == null)
                {
                    foreach (var id in ti.turret.authorizedPlayers)
                    {
                        if (id.IsSteamId())
                        {
                            isAuthorized = true;
                            return;
                        }
                    }
                }

                int p = Math.Max(config.Abandoned.AutoTurret.Ammo, attachedWeapon.primaryMagazine.capacity);
                Item ammo = ItemManager.Create(attachedWeapon.primaryMagazine.ammoType, p, 0uL);
                if (!ammo.MoveToContainer(ti.turret.inventory, -1, true, true, null, true)) ammo.Remove();
                attachedWeapon.primaryMagazine.contents = attachedWeapon.primaryMagazine.capacity;
                attachedWeapon.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                ti.turret.Invoke(() =>
                {
                    if (ti != null && !ti.turret.IsKilled())
                    {
                        ti.items = new();
                        ti.turret.UpdateTotalAmmo();
                        ti.items.Add(ammo);
                    }
                }, 0.25f);
            }

            private void SetupIO(ContainerIOEntity io)
            {
                io.dropsLoot = false;
                io.inventory.SetFlag(ItemContainer.Flag.NoItemInput, true);

                if (config.Abandoned.AutoTurret.HasPower)
                {
                    io.SetFlag(BaseEntity.Flags.Reserved8, true, false, true);
                }
            }

            private bool SetupCollider()
            {
                if (go == null)
                {
                    return false;
                }

                _collider = go.GetComponent<SphereCollider>() ?? go.AddComponent<SphereCollider>();

                if (_collider != null)
                {
                    _collider.radius = radius;
                    _collider.isTrigger = true;
                    _collider.center = Vector3.zero;
                }

                go.layer = (int)Layer.Trigger;
                go.transform.position = center;

                if (eventType == EventType.Tugboat)
                {
                    return true;
                }

                if (!go.TryGetComponent<Rigidbody>(out var rigidbody))
                {
                    rigidbody = go.AddComponent<Rigidbody>();
                }

                if (rigidbody != null)
                {
                    rigidbody.isKinematic = true;
                    rigidbody.useGravity = false;
                    rigidbody.detectCollisions = true;
                    rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
                }

                return true;
            }

            private float lastDespawnUpdateTime;

            public void InvokeDespawn(float seconds, HitInfo info)
            {
                if (seconds > 0f)
                {
                    if (IsInvoking(DestroyAll))
                    {
                        CancelInvoke(DestroyAll);
                    }
                    Invoke(DestroyAll, seconds);
                    DespawnDateTime = DateTime.Now.AddSeconds(seconds);
                    float currentDespawnUpdateTime = Time.time;
                    if (currentDespawnUpdateTime - lastDespawnUpdateTime >= 0.1f || info != null && !info.IsMajorityDamage(DamageType.Heat))
                    {
                        lastDespawnUpdateTime = currentDespawnUpdateTime;
                        Interface.CallHook("OnRaidableDespawnUpdate", new object[13] { center, radius, AllowPVP, raiderId, DespawnDateTime, GetIntruders(), GetIntruderIds(), entities, privs, CanDropBackpack, AutomatedEvent, AttackEvent, _guid });
                    }
                }

                CreateMarkers();
            }

            public void InvokeDespawnInactive() => InvokeDespawn(config.Abandoned.GetDespawnSeconds(AutomatedEvent), null);

            public void InvokeDespawnInactive(HitInfo info) => InvokeDespawn(config.Abandoned.GetDespawnSeconds(AutomatedEvent), info);

            public void InvokeDespawnLooted(HitInfo info) => InvokeDespawn(config.Abandoned.DespawnSecondsLooted, info);

            public void TryResetDespawn(HitInfo info)
            {
                if (config.Abandoned.DespawnSecondsInactiveReset && !IsEventCompleted)
                {
                    InvokeDespawnInactive(info);
                }
                if (config.Abandoned.DespawnSecondsReset && IsEventCompleted)
                {
                    InvokeDespawnLooted(info);
                }
            }

            public void CancelAutomatedEvent(BasePlayer owner)
            {
                if (config.Messages.CancelAutomatedEvent)
                {
                    foreach (var target in GetIntruders())
                    {
                        Message(target, "OnEventAutomatedCancel");
                    }
                }
                var raiders = GetEligibleParticipants();
                foreach (var raider in raiders)
                {
                    Instance.GiveRewards(raider.player, raider.userid, raiders.Count, eventType);
                }
                var text = Instance.GetMessage("Output Cancel", null)
                    .Replace("{displayName}", owner.displayName)
                    .Replace("{userID}", owner.UserIDString)
                    .Replace("{center}", center.ToString())
                    .Replace("{grid}", GetGrid());
                if (config.Discord.Cancel)
                {
                    Instance.SendDiscordMessage(center, text);
                }
                CanShowDiscordEnd = false;
                LogToFile("sar", text);
                Puts(text);
                isCanceled = true;
            }

            public void RemoveExpiration()
            {
                if (activity != null && --activity.total <= 0)
                {
                    data.Activity.Remove(activity);
                }
                IsExpired = false;
            }

            private float InactivePlayerActivityTime => LockBaseToFirstAttacker ? config.Abandoned.InactivePlayerActivityTime : 0f;

            public bool IsPlayerActive(ulong userid)
            {
                if (userid == 0 || previousId == currentId)
                {
                    return true;
                }
                if (InactivePlayerActivityTime <= 0f)
                {
                    return true;
                }
                if (!raiders.TryGetValue(userid, out var raider))
                {
                    return false;
                }
                if (intruders.ContainsKey(userid))
                {
                    UpdateRaiderActivity(raider);
                }
                return raider.PlayerActivityTimeLeft() > 0f;
            }

            public void UpdateIntruderActivity(BasePlayer intruder)
            {
                if (InactivePlayerActivityTime <= 0f)
                {
                    return;
                }
                if (raiders.TryGetValue(intruder.userID, out var raider))
                {
                    UpdateRaiderActivity(raider);
                }
            }

            public void UpdateRaiderActivity(Raider raider)
            {
                if (raider.player.IsKilled())
                {
                    return;
                }
                var position = raider.player.transform.position;
                if (position != raider.lastPosition)
                {
                    raider.lastPosition = position;
                    raider.lastActiveTime = Time.time;
                }
            }

            public void UpdateActivity(bool increment)
            {
                if (owners == null || config.Abandoned.DoNotDestroy && !IsManual() || config.Abandoned.DoNotDestroyManual && IsManual() || config.Abandoned.DoNotDestroyPurge && Instance.IsPurgeEnabled)
                {
                    IsExpired = false;
                    return;
                }

                activity = Instance.FindActivityInfo(owners);

                if (increment)
                {
                    activity.total++;
                }
                else
                {
                    activity.total--;
                }

                if (!data.Activity.Contains(activity))
                {
                    data.Activity.Add(activity);
                }

                if (!increment)
                {
                    return;
                }

                var limit = activity.GetLimit(config);

                if (IsExpired = limit > 0 && activity.total >= limit)
                {
                    data.Activity.Remove(activity);
                }
            }

            private bool IsManual() => payment != null;

            private void SpawnNpcs()
            {
                var profiles = config.Abandoned.BotSpawnProfileNames.ToList();

                profiles.Remove("profile_name_1");
                profiles.Remove("profile_name_2");

                if (profiles.Count == 0)
                {
                    return;
                }

                string group = Guid.NewGuid().ToString();

                Plugin BotReSpawn = Interface.Oxide.RootPluginManager.GetPlugin("BotReSpawn");

                if (BotReSpawn == null)
                {
                    return;
                }

                groups.Add(group);
                BotReSpawn?.Call("AddGroupSpawn", center, profiles.GetRandom(), group, 0);
            }

            private void CreateMarkers()
            {
                if (!markerCreated)
                {
                    markerCreated = true;
                    genericMarker = Instance.CreateGenericMarker(center, anchor);
                    vendingMarker = Instance.CreateVendingMarker(center, anchor);
                    UpdateMarkers();
                }
            }

            public void MarkIsDamaged(BasePlayer attacker)
            {
                if (IsDamaged || !config.Abandoned.ChangeColor || Instance.IsAlly(attacker.userID, previousId))
                {
                    return;
                }
                IsDamaged = true;
                if (genericMarker != null)
                {
                    genericMarker.color1 = ColorUtility.TryParseHtmlString(config.Abandoned.ChangedMarkerColor, out var color) ? color : Color.grey;
                    genericMarker.color2 = genericMarker.color1;
                    genericMarker.SendUpdate();
                }
            }

            public SphereColor GetSphereColor(SphereColorSettings sc)
            {
                if (sc.Unlocked != SphereColor.None && !IsOwnerLocked && LockBaseToFirstAttacker) return sc.Unlocked;
                if (sc.Locked != SphereColor.None && IsOwnerLocked && LockBaseToFirstAttacker) return sc.Locked;
                if (sc.PVPState != SphereColor.None && AllowPVP) return sc.PVPState;
                if (sc.PVEState != SphereColor.None && !AllowPVP) return sc.PVEState;
                return GetRaiders().Count > 0 ? sc.Active : sc.Inactive;
            }

            internal SphereColor CurrentSphereColor;

            public static string RemoveFormatting(string source) => source.Contains('>') ? Regex.Replace(source, "<.*?>", string.Empty) : source;

            public string GetAllowKey() => RemoveFormatting(Instance.GetMessage(AllowPVP ? "PVPFlag" : "PVEFlag", null)).TrimEnd();

            public void UpdateMarkers()
            {
                if (isDestroyed)
                {
                    return;
                }

                Instance.CreateSpheres(this);

                if (genericMarker != null)
                {
                    genericMarker.SendUpdate();
                }

                if (vendingMarker != null)
                {
                    if (DespawnDateTime == DateTime.MinValue)
                    {
                        vendingMarker.markerShopName = GetMarkerName(config.Abandoned.MarkerShopNameSeconds.Replace(" [{time}m]", string.Empty).Replace("{PVX}", GetAllowKey()));
                        Invoke(UpdateMarkers, 10f);
                        return;
                    }

                    var ts = DespawnDateTime.Subtract(DateTime.Now);

                    if (ts.TotalMinutes >= 1)
                    {
                        vendingMarker.markerShopName = GetMarkerName(config.Abandoned.MarkerShopName.Replace("{time}", Math.Ceiling(ts.TotalMinutes).ToString()).Replace("{PVX}", GetAllowKey()));
                        Invoke(UpdateMarkers, 10f);
                    }
                    else
                    {
                        vendingMarker.markerShopName = GetMarkerName(config.Abandoned.MarkerShopNameSeconds.Replace("{time}", Math.Ceiling(ts.TotalSeconds).ToString()).Replace("{PVX}", GetAllowKey()));
                        Invoke(UpdateMarkers, 1f);
                    }

                    vendingMarker.transform.position = center;
                    vendingMarker.SendNetworkUpdate();
                }
            }

            private string GetMarkerPlayerName()
            {
                if (config.Abandoned.ShowRaidersName && !string.IsNullOrEmpty(raiderName)) return raiderName;
                if (config.Abandoned.ShowOwnersName && !string.IsNullOrEmpty(previousName)) return previousName;
                return string.Empty;
            }

            private string GetMarkerName(string time)
            {
                string markerName = GetMarkerPlayerName();
                try
                {
                    if (string.IsNullOrEmpty(markerName))
                    {
                        return time;
                    }
                    if (currentId != previousId || !LockBaseToFirstAttacker && raiderId != 0)
                    {
                        if (!config.Abandoned.MarkerNameRaiderFormat.Contains("{1}"))
                        {
                            return markerName;
                        }
                        return string.Format(config.Abandoned.MarkerNameRaiderFormat, markerName, time);
                    }
                    if (!config.Abandoned.MarkerNameOwnerFormat.Contains("{1}"))
                    {
                        return markerName;
                    }
                    return string.Format(config.Abandoned.MarkerNameOwnerFormat, markerName, time);
                }
                catch
                {
                    return string.Format("{0} {1}", markerName, time);
                }
            }

            public void DestroyAll()
            {
                KillMarkers();
                DestroySpheres();
                UpdateExpirationStatus();

                if (IsExpired && !IsClaimed)
                {
                    entities.RemoveAll(x => x.IsKilled() || !x.IsValid());

                    //Instance.RemoveBuildingOwner(this);

                    Instance.UndoLoop(entities, 10, hookObjects);
                }

                DestroyMe();
            }

            public bool CanDropRustBackpack(ulong userid)
            {
                if (AllowPVP ? config.Abandoned.RustBackpacksPVP : config.Abandoned.RustBackpacksPVE)
                {
                    return !userid.HasPermission("abandonedbases.keepbackpackrust") && raiders.TryGetValue(userid, out var ri);
                }
                return false;
            }

            internal bool CorpsesLootable => AllowPVP ? config.Abandoned.CorpsesLootedPVP : config.Abandoned.CorpsesLootedPVE;

            internal bool CanDropBackpack => AllowPVP ? config.Abandoned.BackpacksPVP : config.Abandoned.BackpacksPVE;

            private float nextHookTime;
            private object[] _hookObjects;
            internal object[] hookObjects
            {
                get
                {
                    float time = Time.time;
                    if (time > nextHookTime)
                    {
                        nextHookTime = time + 0.1f;
                        _hookObjects = new object[11] { center, radius, AllowPVP, GetIntruders(), GetIntruderIds(), entities, privs, CanDropBackpack, AutomatedEvent, AttackEvent, _guid }; 
                    }
                    return _hookObjects;
                }
            }
            
            internal object[] GetPrivHookObjects(BuildingPrivlidge priv) => new object[13] { center, radius, AllowPVP, GetIntruders(), GetIntruderIds(), entities, privs, CanDropBackpack, AutomatedEvent, AttackEvent, priv, AreCupboardsTaken(priv), _guid };

            private bool InTugboatAnchorRange(BasePlayer player)
            {
                return eventType == EventType.Tugboat && !anchor.IsKilled() && AbandonedBases.InRange3D(player.transform.position, anchor.transform.position, anchor.bounds.extents.Max() * 1.25f);
            }

            public void UpdateTime(BasePlayer player, bool state)
            {
                if (!player.IsConnected || !player.HasPermission("abandonedbases.time"))
                {
                    return;
                }

                int time = state ? config.Abandoned.ForcedTime : -1;

                if (player.IsAdmin)
                {
                    player.SendConsoleCommand("admintime", time);
                }
                else if (!player.IsFlying)
                {
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                    player.SendNetworkUpdateImmediate();
                    player.SendConsoleCommand("admintime", time);
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                }

                player.SendNetworkUpdateImmediate();
            }

            public void OnPlayerEnter(BasePlayer player)
            {
                if (!AllowPVP || !config.Abandoned.Holster || !player.svActiveItemID.IsValid || Instance.HasPVPDelay(player.userID))
                {
                    return;
                }
                player.equippingBlocked = true;
                player.UpdateActiveItem(default);
                player.Invoke(() =>
                {
                    player.equippingBlocked = false;
                }, 0.2f);
                Message(player, "Ready your weapons!");
            }

            public void OnPlayerExit(BasePlayer player, bool skipDelay)
            {
                if (!player.IsHuman())
                {
                    return;
                }

                if (AllowPVP)
                {
                    player.equippingBlocked = false;
                }

                if (!skipDelay && InTugboatAnchorRange(player))
                {
                    if (AllowPVP && config.Abandoned.Tugboats.Delay)
                    {
                        Instance.AddDelay(this, player, null, true);
                    }
                    return;
                }

                if (!intruders.Remove(player.userID))
                {
                    return;
                }

                if (InactivePlayerActivityTime > 0f)
                {
                    GetRaider(player).lastActiveTime = Time.time;
                }

                Interface.CallHook("OnPlayerExitAbandonedBase", new object[12] { player, center, radius, AllowPVP, GetIntruders(), GetIntruderIds(), entities, privs, CanDropBackpack, AutomatedEvent, AttackEvent, _guid });

                UpdateTime(player, false);

                if (skipDelay || !AllowPVP)
                {
                    Message(player, AllowPVP ? "OnPlayerExit" : "OnPlayerExitPVE");
                    return;
                }

                Instance.AddDelay(this, player, null, false);
            }

            public static List<BasePlayer> GetMountedPlayers(BaseMountable m)
            {
                BaseVehicle vehicle = m.HasParent() ? m.VehicleParent() : m as BaseVehicle;
                List<BasePlayer> players = new();

                if (!vehicle.IsRealNull())
                {
                    vehicle.GetMountedPlayers(players);
                    return players.Where(x => x.IsHuman());
                }

                var player = m.GetMounted();

                if (player.IsHuman())
                {
                    players.Add(player);
                }

                return players;
            }

            private static bool IsBox(BaseEntity entity)
            {
                switch (entity.ShortPrefabName)
                {
                    case "krieg_storage_vertical":
                    case "krieg_storage_horizontal":
                    case "abyss_barrel_horizontal":
                    case "abyss_barrel_verticle":
                    case "medieval.box.wooden.large":
                    case "box.wooden.large":
                    case "woodbox_deployed":
                    case "coffinstorage":
                    case "storage_barrel_a":
                    case "storage_barrel_b":
                    case "storage_barrel_c":
                    case "wicker_barrel":
                    case "bamboo_barrel":
                        return true;
                    default:
                        return false;
                }
            }

            public bool HasCancelCooldown(BasePlayer player)
            {
                if (cancelCooldownTime > Time.time)
                {
                    Message(player, "CancelCooldown", Math.Ceiling(cancelCooldownTime - Time.time));
                    return true;
                }

                return false;
            }

            public bool IsEventCompleted;

            public void CheckEventCompletion(HitInfo info)
            {
                if (IsEventCompleted || !IsCompleted())
                {
                    return;
                }
                cancelCooldownTime = Time.time;
                IsEventCompleted = true;
                InvokeDespawnLooted(info);
                SetEventCooldowns();
                if (config.Abandoned.DestroyMarkerUponCompletion)
                {
                    KillMarkers();
                }
                if (config.Messages.LocalCompletion > 0 && !string.IsNullOrEmpty(raiderName))
                {
                    var center = this.center;
                    foreach (var target in BasePlayer.activePlayerList)
                    {
                        if (target.IsKilled() || config.Messages.LocalCompletion < World.Size && !AbandonedBases.InRange3D(target.transform.position, center, config.Messages.LocalCompletion))
                        {
                            continue;
                        }
                        if (config.Messages.RaidCompletion && intruders.ContainsKey(target.userID) && target.HasPermission("abandonedbases.convert"))
                        {
                            continue;
                        }
                        if (raiderName == previousName)
                        {
                            Message(target, "OnEventCompletedLocal", raiderName, GetGrid());
                        }
                        else
                        {
                            Message(target, "OnEventCompletedLocalOwned", raiderName, previousName, GetGrid());
                        }
                    }
                }
                if (config.Messages.RaidCompletion)
                {
                    foreach (var target in GetIntruders().Where(x => x.HasPermission("abandonedbases.convert")))
                    {
                        if (target.HasPermission("abandonedbases.convert.cancel") && target.HasPermission("abandonedbases.convert.claim"))
                        {
                            Message(target, "OnEventCompletedClaimCancel");
                        }
                        else if (target.HasPermission("abandonedbases.convert.cancel"))
                        {
                            Message(target, "OnEventCompletedCancel");
                        }
                        else if (target.HasPermission("abandonedbases.convert.claim"))
                        {
                            Message(target, "OnEventCompletedClaim");
                        }
                        else Message(target, "OnEventCompleted");
                    }
                }
                ShowRaidCompletedMessages();
                if (config.Abandoned.RewardsUponCompletion && !rewarded)
                {
                    RewardPlayers();
                }
                Interface.CallHook("OnAbandonedBaseCompleted", hookObjects);
            }

            public bool AreCupboardsTaken(BuildingPrivlidge exclude = null)
            {
                if (!privSpawned) return false;
                foreach (var priv in privs)
                {
                    if (exclude != null && priv == exclude)
                    {
                        continue;
                    }
                    if (!priv.IsKilled() && !priv.inventory.IsEmpty())
                    {
                        return false;
                    }
                }
                return true;
            }

            public bool AreCupboardsDestroyed
            {
                get
                {
                    if (privs.Count == 0) return true;
                    foreach (var priv in privs)
                    {
                        if (!priv.IsKilled())
                        {
                            return false;
                        }
                    }
                    return true;
                }
            }

            public bool IsPrimaryCupboardTaken => privSpawned && (primaryPriv.IsKilled() || primaryPriv.inventory.IsEmpty());
            
            public bool ShouldPreventBaseDestruction => eventType == EventType.Base && IsExpired && !IsClaimed && config.Abandoned.DestroyRequiresCupboards && !AreCupboardsDestroyed;

            private void UpdateExpirationStatus()
            {
                if (ShouldPreventBaseDestruction)
                {
                    IsExpired = false;
                    UpdateActivity(false);
                }
            }

            public bool IsCompleted()
            {
                if (eventType == EventType.LegacyShelter && (anchor == null || anchor.lifestate == BaseCombatEntity.LifeState.Dead))
                {
                    return true;
                }

                if (config.Abandoned.OnlyCupboardsAreRequired && eventType == EventType.Base && AreCupboardsTaken())
                {
                    return true;
                }

                if (config.Abandoned.OnlyPrimaryCupboardIsRequired && eventType == EventType.Base && IsPrimaryCupboardTaken)
                {
                    return true;
                }

                foreach (var container in containers)
                {
                    if (!container.IsKilled() && !container.inventory.IsEmpty())
                    {
                        return false;
                    }
                }

                return true;
            }

            public object OnLootEntityInternal(BasePlayer player, BaseEntity entity)
            {
                if (entity is BaseMountable)
                {
                    return null;
                }
                DroppedItemContainer backpack = entity as DroppedItemContainer;
                if (backpack != null && backpack.ShortPrefabName != "item_drop" && (!backpack.playerSteamID.IsSteamId() || Instance.IsAlly(player.userID, backpack.playerSteamID)))
                {
                    return null;
                }
                LootableCorpse corpse = entity as LootableCorpse;
                if (corpse != null && (!corpse.playerSteamID.IsSteamId() || Instance.IsAlly(player.userID, corpse.playerSteamID)))
                {
                    return null;
                }
                if (config.Abandoned.BlacklistedPickupItems.Exists(value => !string.IsNullOrEmpty(value) && entity.ShortPrefabName.Contains(value, CompareOptions.OrdinalIgnoreCase)))
                {
                    Message(player, "Blacklisted Pickup Item", entity.ShortPrefabName);
                    return false;
                }
                if (!canReassign && LockBaseToFirstAttacker && IsOwnerLocked && !IsOwner(player))
                {
                    Message(player, "Not An Ally"); 
                    TryEjectFromLockedBase(player);
                    return false;
                }
                if (!CanDamageBuilding(player) && HasEventCooldown(player))
                {
                    return false;
                }
                if (!IsParticipant(player.userID))
                {
                    if (EnforceCooldownBetweenEvents(player))
                    {
                        return false;
                    }
                    if ((entity is BoxStorage || entity is BuildingPrivlidge))
                    {
                        Rewards rewards = Instance.GetRewards(eventType);

                        if (!rewards.OnLootEntity.HasValue || rewards.OnLootEntity.Value)
                        {
                            GetRaider(player).IsParticipant = true;
                        }
                    }
                }
                UpdateMarkers();
                return null;
            }
        }

        #endregion

        #region Hooks

        private void OnNewSave(string filename)
        {
            newSave = true;
        }

        private void Init()
        {
            TryInvokeMethod(Unsubscribe);
            TryInvokeMethod(LoadData);
            Unsubscribe(nameof(OnUserBanned));
            Unsubscribe(nameof(OnLootEntity));
            Unsubscribe(nameof(OnLootEntityEnd));
            Unsubscribe(nameof(OnCupboardAssign));
        }

        private void OnServerInitialized(bool isStartup)
        {
            isLoaded = true;
            TryInvokeMethod(CheckData);
            TryInvokeMethod(RegisterPermissions);
            TryInvokeMethod(RegisterCommands);
            TryInvokeMethod(() => config.Validate(this));
            TryInvokeMethod(StartNotificationTimer);
            TryInvokeMethod(() => SetupZoneManager(true));
            SetupAbandonedRoutine();
            UpdateLastSeen();
            timer.Every(60f, UpdateLastSeen);
            InitializeDefinitions();
            if (config.Abandoned.Privilege.Ui)
            {
                Subscribe(nameof(OnLootEntity));
                Subscribe(nameof(OnLootEntityEnd));
            }
            if (config.Abandoned.BlockToolCupboardAuthFriend)
            {
                Subscribe(nameof(OnCupboardAssign));
            }
            if (config.Abandoned.Banned.Enabled)
            {
                Subscribe(nameof(OnUserBanned));
            }
            CheckDefaultGroup(_consolePlayer);
        }

        private object OnEngineStart(Tugboat tugboat, BasePlayer player)
        {
            if (tugboat.IsValid() && AbandonedReferences.ContainsKey(tugboat.net.ID.Value))
            {
                Message(player, "Engine failure");
                return true;
            }
            return null;
        }

        private object OnCupboardAssign(BuildingPrivlidge priv, ulong userid, BasePlayer player)
        {
            if (player != null && userid != (ulong)player.userID) return true;
            return null;
        }

        private void OnLootEntity(BasePlayer player, BuildingPrivlidge priv)
        {
            ShowAbandonedLimitUi(player, priv);
        }

        private void OnLootEntityEnd(BasePlayer player, StorageContainer container)
        {
            if (config.Abandoned.Privilege.Ui && container is BuildingPrivlidge)
            {
                DestroyAbandonedLimitUi(player);
            }

            if (AbandonedBuildings.Count == 0 || container.IsKilled())
            {
                return;
            }

            if (GetAbandonedBuilding(container.transform.position, out var abandonedBuilding))
            {
                abandonedBuilding.CheckEventCompletion(null);
            }
        
        }

        private object CanLootEntity(BasePlayer player, BaseEntity entity) // BaseRidableAnimal, ContainerIOEntity, DroppedItemContainer, IndustrialCrafter, LootableCorpse, ResourceContainer, StorageContainer
        {
            if (player.IsKilled() || entity.IsKilled()) return null;
            if (!GetAbandonedBuilding(entity.transform.position, out var abandonedBuilding)) return null;
            if (entity is StorageContainer container && CanLootStorage(player, container, abandonedBuilding)) return null;
            if (entity.OwnerID.IsSteamId() && !abandonedBuilding.owners.Contains(entity.OwnerID)) return null;
            return abandonedBuilding.OnLootEntityInternal(player, entity) == null ? (object)null : true;
        }

        private object CanPickupEntity(BasePlayer player, BaseEntity entity)
        {
            if (player.IsKilled() || entity.IsKilled()) return null;
            if (!GetAbandonedBuilding(entity.transform.position, out var abandonedBuilding)) return null;
            if (entity.OwnerID.IsSteamId() && !abandonedBuilding.owners.Contains(entity.OwnerID)) return null;
            return abandonedBuilding.OnLootEntityInternal(player, entity);
        }

        private bool CanLootStorage(BasePlayer player, StorageContainer container, AbandonedBuilding abandonedBuilding)
        {
            if (container.HasParent() && container.GetParentEntity() is BaseMountable m && player.userID == m.OwnerID) return true;
            if (container.OwnerID.IsSteamId() && !abandonedBuilding.owners.Contains(container.OwnerID)) return true;
            if (player.userID == container.OwnerID) return true;
            return false;
        }

        private bool IsServerShuttingDown;
        private bool IsUnloading;

        private void OnServerShutdown()
        {
            IsServerShuttingDown = true;
        }

        private void TryDisableEventCooldowns() // allow plugin to be reloaded without penalizing everyone, but prevent exploiting this by enforcing a cooldown on a server restart
        {
            if (!IsServerShuttingDown)
            {
                foreach (var abandonedBuilding in AbandonedBuildings.ToList())
                {
                    abandonedBuilding.DisableEventCooldown = true;
                }
            }
        }

        private void Unload()
        {
            IsUnloading = true;
            TryInvokeMethod(TryDisableEventCooldowns);
            TryInvokeMethod(StopAbandonedCoroutine);
            TryInvokeMethod(DestroyAll);
            TryInvokeMethod(SaveData);
            AbandonedBasesExtensionMethods.ExtensionMethods.permission = null;
            if (config.Abandoned.Privilege.Ui)
            {
                BasePlayer.activePlayerList.ToList().ForEach(DestroyAbandonedLimitUi);
            }
        }

        private void OnUserBanned(string username, string steamid, string address, string reason, long expiry)
        {
            if (!config.Abandoned.Banned.Ignored.Contains(steamid) && ulong.TryParse(steamid, out var userid))
            {
                UpdateLastSeen(userid, Epoch.Current - (86400 * 365));
                //if (config.Abandoned.Banned.Delay > 0f)
                //{
                //    timer.Once(config.Abandoned.Banned.Delay, () => ConvertBannedUser(userid));
                //}
            }
        }

        public void ConvertBannedUser(ulong searchForId)
        {
            IEnumerator co = ConvertBannedUserCo(searchForId);

            _conversions.Add(new(searchForId.ToString(), new() { searchForId }, ServerMgr.Instance.StartCoroutine(co)));
        }

        private IEnumerator ConvertBannedUserCo(ulong searchForId)
        {
            float waitTime = config.Abandoned.WaitTime < 2f ? 2f : config.Abandoned.WaitTime;
            int timestamp = Epoch.Current;
            foreach (var building in BuildingManager.server.buildingDictionary.Values)
            {
                if (!building.HasDecayEntities())
                    continue;

                if (searchForId.IsSteamId() && !building.decayEntities.Exists(x => x.OwnerID == searchForId) && (!building.HasBuildingPrivileges() || !building.buildingPrivileges.Exists(x => x.AnyAuthed() && x.IsAuthed(searchForId))))
                    continue;

                yield return AbandonedBuildingCo(null, timestamp, waitTime, true, ConversionType.Automated, null, false, building);
            }
            _conversions.RemoveAll(x => x.userid == searchForId.ToString());
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player.IsConnected)
            {
                UpdateLastSeen(player, Epoch.Current);
                TryEndAutomatedEvent(player);
            }
        }

        private void OnPlayerSleep(BasePlayer player)
        {
            if (player.IsConnected)
            {
                UpdateLastSeen(player, Epoch.Current);
            }
        }

        private void OnPlayerSleepEnded(BasePlayer player)
        {
            if (PvpDelay.Remove(player.userID, out var ds))
            {
                ds.Destroy();
            }
            if (player.IsConnected)
            {
                UpdateLastSeen(player, Epoch.Current);
            }
        }

        private void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (!player.IsHuman())
            {
                return;
            }

            if (player.IsConnected)
            {
                UpdateLastSeen(player, Epoch.Current);
            }

            if (!GetAbandonedBuilding(player, info, out var abandonedBuilding))
            {
                return;
            }

            abandonedBuilding.OnPlayerExit(player, true);
            abandonedBuilding.HandleTurretSight(player);

            if (CanDropPlayerBackpack(player, abandonedBuilding) && (abandonedBuilding.IsParticipant(player.userID) || abandonedBuilding.IsAbandonedSleeper(player) || HasPVPDelay(player.userID)))
            {
                Backpacks?.Call("API_DropBackpack", player);
            }
        }

        private object OnBackpackDrop(Item backpack, PlayerInventory inv)
        {
            if (backpack == null || inv == null || inv.baseEntity == null) return null;
            BasePlayer player = inv.baseEntity;
            if (!player.IsHuman() || !GetAbandonedBuilding(player, null, out var abandonedBuilding)) return null;
            if (abandonedBuilding.CanDropRustBackpack(player.userID))
            {
                backpack.RemoveFromContainer();
                backpack.Drop(player.GetDropPosition() + new Vector3(0f, 0.035f), player.GetDropVelocity());
                return null;
            }
            return true;
        }

        private void DropRustBackpack(PlayerCorpse corpse)
        {
            if (corpse?.containers != null)
            {
                var position = corpse.GetDropPosition() + new Vector3(0f, 0.035f);
                var velocity = corpse.GetDropVelocity();
                foreach (var container in corpse.containers)
                {
                    if (container != null && container.itemList != null)
                    {
                        for (int i = container.itemList.Count - 1; i >= 0; i--)
                        {
                            Item item = container.itemList[i];
                            if (item != null && item.IsBackpack())
                            {
                                item.RemoveFromContainer();
                                item.Drop(position, velocity);
                            }
                        }
                    }
                }
            }
        }

        private void DropRustBackpack(DroppedItemContainer backpack)
        {
            if (backpack?.inventory?.itemList != null)
            {
                var position = backpack.GetDropPosition() + new Vector3(0f, 0.035f);
                var velocity = backpack.GetDropVelocity();
                for (int i = backpack.inventory.itemList.Count - 1; i >= 0; i--)
                {
                    Item item = backpack.inventory.itemList[i];
                    if (item != null && item.IsBackpack())
                    {
                        item.RemoveFromContainer();
                        item.Drop(position, velocity);
                    }
                }
            }
        }

        private bool CanDropPlayerBackpack(BasePlayer player, AbandonedBuilding abandonedBuilding)
        {
            if (TryGetDelayValue(player.userID, out var ds) && ds.Building.CanDropBackpack)
            {
                return true;
            }

            return abandonedBuilding != null && abandonedBuilding.CanDropBackpack;
        }

        private void OnEntitySpawned(DroppedItemContainer backpack)
        {
            if (backpack == null || backpack.ShortPrefabName != "item_drop_backpack")
            {
                return;
            }

            bool isFromCorpse = backpack.playerSteamID == 0uL;

            NextTick(() =>
            {
                if (backpack.IsKilled() || !GetAbandonedBuilding(backpack, backpack.playerSteamID, out var abandonedBuilding))
                {
                    return;
                }

                if (!abandonedBuilding.AllowPVP && !abandonedBuilding.owners.Contains(backpack.playerSteamID))
                {
                    return;
                }

                if (abandonedBuilding.CanDropRustBackpack(backpack.playerSteamID))
                {
                    DropRustBackpack(backpack);
                }

                if (isFromCorpse ? abandonedBuilding.CorpsesLootable : abandonedBuilding.CanDropBackpack)
                {
                    backpack.playerSteamID = 0;
                }
            });
        }

        private void OnEntitySpawned(PlayerCorpse corpse)
        {
            if (corpse.IsKilled())
            {
                return;
            }

            ulong playerSteamID = corpse.playerSteamID;
            if (!GetAbandonedBuilding(corpse, playerSteamID, out var abandonedBuilding))
            {
                return;
            }

            if (abandonedBuilding.CanDropRustBackpack(playerSteamID))
            {
                DropRustBackpack(corpse);
            }

            if (abandonedBuilding.CorpsesLootable)
            {
                corpse.playerSteamID = 0;
            }
        }

        private void OnEntitySpawned(MLRSRocket rocket)
        {
            if (rocket.IsKilled())
            {
                return;
            }
            using var systems = FindEntitiesOfType<MLRS>(rocket.transform.position, 15f);
            if (systems.Count == 0)
            {
                return;
            }
            if (!GetAbandonedBuilding(systems[0].TrueHitPos, 0f, out var abandonedBuilding))
            {
                return;
            }
            var owner = systems[0].rocketOwnerRef.Get(true) as BasePlayer;
            if (owner != null)
            {
                rocket.creatorEntity = config.Abandoned.MLRS ? owner : null;
                rocket.OwnerID = config.Abandoned.MLRS ? owner.userID : 0uL;
                abandonedBuilding.UpdateIntruderActivity(owner);
            }
        }

        private void OnEntitySpawned(BaseEntity entity)
        {
            if (!entity.IsValid() || entity.IsDestroyed || !entity.OwnerID.IsSteamId())
            {
                return;
            }

            if (!GetAbandonedBuilding(entity.transform.position, out var abandonedBuilding))
            {
                return;
            }

            if (!abandonedBuilding.owners.Contains(entity.OwnerID))
            {
                return;
            }

            if (abandonedBuilding.AllowPVP || abandonedBuilding.IsAttached(entity))
            {
                abandonedBuilding.RememberOwner(entity);
            }
        }

        private object OnLifeSupportSavingLife(BasePlayer player)
        {
            return PlayerInEvent(player) ? true : (object)null;
        }

        private object OnPreventLooting(BasePlayer player, BaseEntity entity)
        {
            return AbandonedReferences.ContainsKey(entity.net.ID.Value) ? true : (object)null;
        }

        private object OnRestoreUponDeath(BasePlayer player)
        {
            if (!GetAbandonedBuilding(player, null, out var abandonedBuilding))
            {
                return null;
            }

            if (config.Abandoned.BlockRestoreSleepers && abandonedBuilding.IsAbandonedSleeper(player))
            {
                return true;
            }

            return (abandonedBuilding.AllowPVP ? config.Abandoned.BlockRestorePVP : config.Abandoned.BlockRestorePVE) ? true : (object)null;
        }

        private object CanRevivePlayer(BasePlayer player, Vector3 pos)
        {
            if (!GetAbandonedBuilding(player, null, out var abandonedBuilding))
            {
                return null;
            }

            return (abandonedBuilding.AllowPVP ? config.Abandoned.BlockRevivePVP : config.Abandoned.BlockRevivePVE) ? true : (object)null;
        }

        private object CanTeleport(BasePlayer player, Vector3 to)
        {
            if (player.HasPermission("abandonedbases.teleport"))
            {
                return null;
            }

            if (Interface.CallHook("OnBlockRaidableBasesTeleport", player, to) is object obj)
            {
                if (obj is string str && !string.IsNullOrWhiteSpace(str)) return str;
                if (obj is bool val && val) return null;
            }

            return PlayerInEvent(player) || EventTerritory(to) ? GetMessage("CannotTeleport", player.UserIDString) : null;
        }

        private object OnReflectDamage(BasePlayer victim, BasePlayer attacker)
        {
            return PlayerInEvent(victim) || PlayerInEvent(attacker) ? true : (object)null;
        }

        private object CanOpenBackpack(BasePlayer looter, ulong backpackOwnerID)
        {
            return !config.Abandoned.Backpacks && PlayerInEvent(looter) ? GetMessage("CommandNotAllowed", looter.UserIDString) : (object)null;
        }

        private object CanEntityBeTargeted(BaseEntity target, BaseEntity entity)
        {
            if (!entity.IsValid() || !AbandonedReferences.ContainsKey(entity.net.ID.Value)) return null;
            if (entity is SamSite && target is BasePlayer player && IsOwnerOrAuthed(player, entity)) return false;
            return config.Abandoned.AutoTurret.CanDamagePlayers || !(entity is AutoTurret);
        }

        private bool IsOwnerOrAuthed(BasePlayer player, BaseEntity entity)
        {
            if (entity.IsDestroyed) return false;
            if (player.userID == entity.OwnerID) return true;
            return player.IsBuildingAuthed(entity.WorldSpaceBounds(), true);
        }

        private object CanEntityTrapTrigger(BaseTrap trap, BasePlayer player)
        {
            return trap.IsValid() && AbandonedReferences.ContainsKey(trap.net.ID.Value) ? true : (object)null;
        }

        private List<DamageType> _damageTypes = new() { DamageType.ElectricShock, DamageType.Decay };

        private bool IsIgnored(HitInfo info) => _damageTypes.Contains(info.damageTypes.GetMajorityDamageType()) || info.Initiator != null && info.Initiator.skinID == 755446;

        private bool IsIgnored(BaseEntity entity) => entity.IsKilled() || entity.net == null || entity.OwnerID == 1337422 || entity.skinID == 755446 || IsInZone(entity.transform.position) || IsIgnoredPrefab(entity.ShortPrefabName);

        private object CanEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (info == null || IsIgnored(entity) || IsIgnored(info))
            {
                return null;
            }

            if (entity is BasePlayer victim)
            {
                return ProcessVictim(victim, info);
            }

            if (info.Initiator.IsValid() && AbandonedReferences.TryGetValue(info.Initiator.net.ID.Value, out var abandonedBuilding) && !CheckParentEntity(info.Initiator))
            {
                return IsEntityDamageAllowed(entity, info, abandonedBuilding);
            }

            if (entity.IsValid() && AbandonedReferences.TryGetValue(entity.net.ID.Value, out var abandonedBuilding2) && !CheckParentEntity(entity))
            {
                return ProcessAbandonedBuilding(entity, info, abandonedBuilding2);
            }

            if (entity.OwnerID.IsSteamId() || (entity is Tugboat or LegacyShelter))
            {
                return ProcessPlayerEntity(entity, info);
            }

            return null;
        }

        private bool CheckParentEntity(BaseEntity entity)
        {
            return entity.HasParent() && (entity.GetParentEntity() is BradleyAPC or TravellingVendor or PatrolHelicopter);
        }

        private object OnRaidingUltimateTargetAcquire(BasePlayer player, Vector3 targetPoint)
        {
            return config.Abandoned.MLRS || !EventTerritory(targetPoint) ? (object)null : true;
        }

        private object CanLootPlayer(BasePlayer target, BasePlayer looter)
        {
            if (target == null || looter == null || !GetAbandonedBuilding(target.transform.position, out var abandonedBuilding))
            {
                return null;
            }

            if (abandonedBuilding.IsAbandonedSleeper(target))
            {
                return null;
            }

            //if (config.PlayersCanKillAnySleepers && target.IsSleeping() && (abandonedBuilding.NearFoundation(target, 15f) || abandonedBuilding.IsParticipant(target.userID)))
            //{
            //    return null;
            //}

            if (target.IsWounded())
            {
                return !config.Abandoned.CannotLootWoundedPlayers || IsAlly(target, looter.userID) ? (object)null : false;
            }

            if (config.Abandoned.CanLootPlayerAlive && abandonedBuilding.AllowPVP && abandonedBuilding.IsParticipant(target.userID))
            {
                return null;
            }

            return IsAlly(target, looter.userID) ? (object)null : false;
        }

        private void OnEntityDeath(BaseEntity entity, HitInfo info)
        {
            if (!entity.IsValid() || !AbandonedReferences.Remove(entity.net.ID.Value, out var abandonedBuilding))
            {
                return;
            }
            if (!entity.OwnerID.IsSteamId() && abandonedBuilding.EntityOwners.TryGetValue(entity.net.ID.Value, out var eo))
            {
                entity.OwnerID = eo.OwnerID;
                LimitEntities?.Call("OnEntityKill", entity);
            }
            if (entity.OwnerID.IsSteamId())
            {
                Interface.CallHook("RemovePlayerEntity", entity.OwnerID, entity.ShortPrefabName);
            }
            if (entity is BuildingPrivlidge priv)
            {
                Interface.CallHook("OnAbandonedBasePrivilegeDestroyed", abandonedBuilding.GetPrivHookObjects(priv)); // multi tc is possible
            }
            bool primaryDeathEntity = entity is IItemContainerEntity || entity is LegacyShelter || entity is BuildingBlock;
            if (primaryDeathEntity)
            {
                NextTick(() =>
                {
                    if (abandonedBuilding == null) return;
                    abandonedBuilding.CheckEventCompletion(info);
                    abandonedBuilding.UpdateMarkers();
                });
            }
            if (info != null && info.Initiator is BasePlayer attacker && attacker.IsConnected)
            {
                if (!abandonedBuilding.IsDamaged)
                {
                    abandonedBuilding.MarkIsDamaged(attacker);
                }
                Rewards rewards = GetRewards(abandonedBuilding.eventType);
                bool onEntityDeath = !rewards.OnEntityDeath.HasValue || rewards.OnEntityDeath.Value;
                abandonedBuilding.TrySetOwnerLock(attacker, primaryDeathEntity && onEntityDeath);
            }
        }

        private void OnEntityKill(BaseEntity entity) => OnEntityDeath(entity, null);

        private void OnEntityBuilt(Planner planner, GameObject go)
        {
            if (go == null)
            {
                return;
            }

            var e = go.ToBaseEntity();

            if (e == null)
            {
                return;
            }

            var player = planner.GetOwnerPlayer();

            if (player == null)
            {
                return;
            }

            if (!GetAbandonedBuilding(e.transform.position, out var abandonedBuilding))
            {
                //if (IsPurgeEnabled) AbandonedReferences[e.net.ID.Value] = null;
                return;
            }

            bool nearFoundation = abandonedBuilding.NearFoundation3D(player);

            if (nearFoundation && abandonedBuilding.OnLootEntityInternal(player, e) is bool val && !val)
            {
                NextTick(e.SafelyKill);
                return;
            }

            if (e is Barricade)
            {
                abandonedBuilding.AddEntity(e);
                return;
            }

            if (e is BaseLadder)
            {
                if (!nearFoundation)
                {
                    return;
                }
                if (!config.Abandoned.AllowLadders)
                {
                    Message(player, "CannotBuildLadders");
                    e.Invoke(e.SafelyKill, 0.1f);
                }
                else
                {
                    abandonedBuilding.AddEntity(e);
                }
                return;
            }

            if (!config.Abandoned.AllowBuilding && (nearFoundation || !abandonedBuilding.others.Contains(player.userID)))
            {
                Message(player, "CannotBuild");
                e.Invoke(e.SafelyKill, 0.1f);
                return;
            }

            if (nearFoundation)
            {
                abandonedBuilding.AddEntity(e);

                if (e is BuildingPrivlidge)
                {
                    if (!player.HasPermission("abandonedbases.convert") || !player.HasPermission("abandonedbases.convert.claim"))
                    {
                        Message(player, "OnBuiltPrivilegeNone");
                        return;
                    }

                    if (config.Abandoned.RequireEventFinished)
                    {
                        Message(player, "OnBuiltPrivilegeEx");
                    }
                    else Message(player, "OnBuiltPrivilege");
                }
            }
        }

        private object OnPlayerCommand(BasePlayer player, string command, string[] args)
        {
            if (player == null || !EventTerritory(player.transform.position))
            {
                return null;
            }

            foreach (var value in config.Abandoned.BlacklistedCommands)
            {
                if (command.Equals(value, StringComparison.OrdinalIgnoreCase))
                {
                    Message(player, "CommandNotAllowed");
                    return true;
                }
            }

            return null;
        }

        private object OnServerCommand(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player == null || !EventTerritory(player.transform.position))
            {
                return null;
            }

            foreach (var value in config.Abandoned.BlacklistedCommands)
            {
                if (arg.cmd.FullName.EndsWith(value, StringComparison.OrdinalIgnoreCase))
                {
                    Message(player, "CommandNotAllowed");
                    return true;
                }
            }

            return null;
        }

        #endregion Hooks

        #region Helpers

        private readonly Regex NonStandardCharactersRegex = new("[^a-zA-Z0-9!@#$%^&*()_+\\-=\\[\\]{};:'\",.<>?/\\\\| ]", RegexOptions.Compiled);

        public string RemoveNonStandardCharacters(string input) => NonStandardCharactersRegex.Replace(input, "?");

        private static void SafelyKill(BaseEntity entity) => entity.SafelyKill();

        private bool IgnoreEventCooldowns() => IsPurgeEnabled && config.Abandoned.IgnorePurgeEventCooldown;

        private bool IgnoreCancelCooldowns() => IsPurgeEnabled && config.Abandoned.IgnorePurgeCancelCooldown;

        private bool IgnoreConversionCooldowns() => IsPurgeEnabled && config.Abandoned.IgnorePurgeConversionCooldown;

        private bool IsInZone(Vector3 position)
        {
            if (blockedZones.Count > 0)
            {
                foreach (var zone in blockedZones)
                {
                    if (zone.IsPositionInZone(position))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool IsIgnoredPrefab(string shortname)
        {
            if (config.Abandoned.IgnoredPrefabs.Count > 0)
            {
                foreach (var x in config.Abandoned.IgnoredPrefabs)
                {
                    if (x.Equals(shortname, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool DefaultHasPermission()
        {
            foreach (var perm in config.PurgePermissions)
            {
                if (permission.GroupHasPermission("default", perm))
                {
                    return true;
                }
            }
            return false;
        }

        private bool DefaultNoPurge(out string res)
        {
            if (permission.GroupHasPermission("default", "abandonedbases.exclude"))
            {
                res = "abandonedbases.exclude";
                return true;
            }
            foreach (var perm in config.NoPurgePermissions)
            {
                if (permission.GroupHasPermission("default", perm))
                {
                    res = perm;
                    return true;
                }
            }
            res = null;
            return false;
        }
        
        private bool IsUserExcluded(ulong userid)
        {
            if (userid.HasPermission("abandonedbases.exclude"))
            {
                AddOrUpdateUserExclusion("abandonedbases.exclude", userid);
                return true;
            }
            return false;
        }

        private bool IsUserImmune(ulong userid) => PurgeSettings.IsImmune(config, userid);

        internal ActivityInfo FindActivityInfo(HashSet<ulong> owners)
        {
            ActivityInfo activity = null;
            int j = int.MinValue;

            foreach (var other in data.Activity)
            {
                if (other.SameOwners(owners))
                {
                    int k = other.GetLimit(config);

                    if (k > j)
                    {
                        activity = other;
                        j = k;
                    }
                }
            }

            if (activity == null)
            {
                activity = new();
                activity.owners = owners;
                activity.CheckPermission(config);
            }

            return activity;
        }

        private MapMarkerGenericRadius CreateGenericMarker(Vector3 center, BaseEntity parent)
        {
            MapMarkerGenericRadius genericMarker = GameManager.server.CreateEntity(StringPool.Get(2849728229), center) as MapMarkerGenericRadius;
            genericMarker.alpha = 0.75f;
            genericMarker.color1 = ColorUtility.TryParseHtmlString(config.Abandoned.DefaultMarkerColor, out var color) ? color : Color.magenta;
            genericMarker.color2 = genericMarker.color1;
            genericMarker.radius = Mathf.Min(2.5f, World.Size <= 3600 ? config.Abandoned.MarkerSubRadius : config.Abandoned.MarkerRadius);
            genericMarker.Spawn();
            if (parent) genericMarker.SetParent(parent, true, true);
            //MapMarker.serverMapMarkers.Remove(genericMarker);
            genericMarker.SendUpdate();
            return genericMarker;
        }

        private VendingMachineMapMarker CreateVendingMarker(Vector3 center, BaseEntity parent)
        {
            VendingMachineMapMarker vendingMarker = GameManager.server.CreateEntity(StringPool.Get(3459945130), center) as VendingMachineMapMarker;
            vendingMarker.enabled = false;
            vendingMarker.Spawn();
            if (parent) vendingMarker.SetParent(parent, true, true);
            return vendingMarker;
        }

        private void CreateSpheres(AbandonedBuilding abandonedBuilding)
        {
            SphereColor sphereColor = abandonedBuilding.GetSphereColor(config.Abandoned.SphereColor);

            if (abandonedBuilding.CurrentSphereColor != SphereColor.None && abandonedBuilding.CurrentSphereColor == sphereColor)
            {
                return;
            }

            if (abandonedBuilding.CurrentSphereColor == SphereColor.None && sphereColor == SphereColor.None && abandonedBuilding.spheres.Count > 0)
            {
                return;
            }

            abandonedBuilding.DestroySpheres();

            void SpawnSphere(string prefab)
            {
                if (StringPool.toNumber.ContainsKey(prefab) && GameManager.server.CreateEntity(prefab, abandonedBuilding.center) is SphereEntity sphere)
                {
                    sphere.currentRadius = 1f;
                    sphere.enableSaving = false;
                    sphere.Spawn();
                    if (abandonedBuilding.anchor != null) sphere.SetParent(abandonedBuilding.anchor, true, true);
                    sphere?.LerpRadiusTo(abandonedBuilding.radius * 2f, abandonedBuilding.radius * 0.75f);
                    abandonedBuilding.spheres.Add(sphere);
                }
            }

            List<string> prefabs = sphereColor switch
            {
                SphereColor.Blue => new() { "assets/bundled/prefabs/modding/events/twitch/br_sphere.prefab" },
                SphereColor.Cyan => new() { "assets/bundled/prefabs/modding/events/twitch/br_sphere.prefab", "assets/bundled/prefabs/modding/events/twitch/br_sphere_green.prefab" },
                SphereColor.Green => new() { "assets/bundled/prefabs/modding/events/twitch/br_sphere_green.prefab" },
                SphereColor.Magenta => new() { "assets/bundled/prefabs/modding/events/twitch/br_sphere_purple.prefab", "assets/bundled/prefabs/modding/events/twitch/br_sphere_red.prefab" },
                SphereColor.Purple => new() { "assets/bundled/prefabs/modding/events/twitch/br_sphere_purple.prefab" },
                SphereColor.Red => new() { "assets/bundled/prefabs/modding/events/twitch/br_sphere_red.prefab" },
                SphereColor.Yellow => new() { "assets/bundled/prefabs/modding/events/twitch/br_sphere_red.prefab", "assets/bundled/prefabs/modding/events/twitch/br_sphere_green.prefab" },
                _ => new(),
            };

            if (prefabs.Count > 0)
            {
                prefabs.ForEach(SpawnSphere);
            }

            if (config.Abandoned.SphereAmount > 0)
            {
                for (int i = 0; i < config.Abandoned.SphereAmount; i++)
                {
                    SpawnSphere("assets/prefabs/visualization/sphere.prefab");
                }
            }

            abandonedBuilding.CurrentSphereColor = sphereColor;
        }

        internal bool GetAbandonedBuilding(BaseEntity entity, ulong playerSteamID, out AbandonedBuilding abandonedBuilding)
        {
            if (playerSteamID.IsSteamId() && GetPVPDelay(playerSteamID, out DelaySettings ds) > 0f && ds.Building != null)
            {
                abandonedBuilding = ds.Building;
                return true;
            }
            return GetAbandonedBuilding(entity.transform.position, out abandonedBuilding);
        }

        internal bool GetAbandonedBuilding(BasePlayer victim, HitInfo info, out AbandonedBuilding abandonedBuilding)
        {
            if (TryGetDelayValue(victim.userID, out var ds))
            {
                if (GetAbandonedBuilding(info, out var hitInfoBuilding) && CompareBuildingTo(ds.Building, hitInfoBuilding, out abandonedBuilding))
                {
                    return true;
                }

                abandonedBuilding = ds.Building;
                return true;
            }

            GetAbandonedBuilding(victim.transform.position, out var victimBuilding);
            GetAbandonedBuilding(info, out var hitInfoBuildingResult);

            return CompareBuildingTo(victimBuilding, hitInfoBuildingResult, out abandonedBuilding);
        }

        internal static bool CompareBuildingTo(AbandonedBuilding victimBuilding, AbandonedBuilding attackerBuilding, out AbandonedBuilding result)
        {
            if (victimBuilding == null)
            {
                result = attackerBuilding;
                return result != null;
            }

            if (attackerBuilding == null)
            {
                result = victimBuilding;
                return result != null;
            }

            if (victimBuilding == attackerBuilding)
            {
                result = victimBuilding;
                return true;
            }

            result = null;
            return false;
        }

        internal bool GetAbandonedBuilding(HitInfo info, out AbandonedBuilding abandonedBuilding)
        {
            abandonedBuilding = null;

            if (info == null)
            {
                return false;
            }

            if (!info.Initiator.IsKilled())
            {
                if (GetAbandonedBuilding(info.Initiator.transform.position, out abandonedBuilding))
                {
                    return true;
                }
            }

            if (!info.WeaponPrefab.IsKilled())
            {
                if (GetAbandonedBuilding(info.WeaponPrefab.transform.position, out abandonedBuilding))
                {
                    return true;
                }
            }

            if (!info.Weapon.IsKilled())
            {
                return GetAbandonedBuilding(info.Weapon.transform.position, out abandonedBuilding);
            }

            return false;
        }

        internal bool GetAbandonedBuilding(Vector3 target, out AbandonedBuilding abandonedBuilding)
        {
            return GetAbandonedBuilding(target, 0f, out abandonedBuilding);
        }

        internal bool GetAbandonedBuilding(Vector3 target, float x, out AbandonedBuilding abandonedBuilding)
        {
            abandonedBuilding = null;

            foreach (var otherBuilding in AbandonedBuildings)
            {
                if (otherBuilding.InRange3D(target, x))
                {
                    abandonedBuilding = otherBuilding;
                    return true;
                }
            }

            return false;
        }

        internal void AddDelay(AbandonedBuilding abandonedBuilding, BasePlayer player, HitInfo info, bool check)
        {
            if (config.Abandoned.PVPDelay <= 0f)
            {
                Message(player, abandonedBuilding.AllowPVP ? "OnPlayerExit" : "OnPlayerExitPVE");

                return;
            }

            AddDelayInternal(abandonedBuilding, player, info);

            if (!check)
            {
                Message(player, "DoomAndGloom", GetMessage("PVPFlag", player.UserIDString), config.Abandoned.PVPDelay);
            }
            else
            {
                abandonedBuilding.TryMessage(player, 5f, "DoomAndGloom", GetMessage("PVPFlag", player.UserIDString), config.Abandoned.PVPDelay);
            }
        }

        internal void AddDelayInternal(AbandonedBuilding abandonedBuilding, BasePlayer player, HitInfo info)
        {
            if (config.Abandoned.PVPDelay <= 0f || !abandonedBuilding.AllowPVP)
            {
                return;
            }

            ulong userid = player.userID;

            if (!TryGetDelayValue(userid, out var ds))
            {
                Interface.CallHook("OnPlayerPvpDelayStart", new object[12] { player, userid, abandonedBuilding.center, abandonedBuilding.radius, abandonedBuilding.GetIntruders(), abandonedBuilding.GetIntruderIds(), abandonedBuilding.entities, abandonedBuilding.privs, abandonedBuilding.CanDropBackpack, abandonedBuilding.AutomatedEvent, abandonedBuilding.AttackEvent, abandonedBuilding._guid });
            }
            else
            {
                float currentDealtDamageTime = Time.time;
                if (Time.time - player.lastDealtDamageTime >= 0.1f || info != null && !info.IsMajorityDamage(DamageType.Heat))
                {
                    Interface.CallHook("OnPlayerPvpDelayReset", new object[12] { player, userid, abandonedBuilding.center, abandonedBuilding.radius, abandonedBuilding.GetIntruders(), abandonedBuilding.GetIntruderIds(), abandonedBuilding.entities, abandonedBuilding.privs, abandonedBuilding.CanDropBackpack, abandonedBuilding.AutomatedEvent, abandonedBuilding.AttackEvent, abandonedBuilding._guid });
                    player.lastDealtDamageTime = currentDealtDamageTime;
                }
                ds.Timer.Destroy();
            }

            PvpDelay[userid] = ds = new()
            {
                Timer = abandonedBuilding.timer.Once(config.Abandoned.PVPDelay, () =>
                {
                    Interface.CallHook("OnPlayerPvpDelayExpiredII", new object[12] { player, userid, abandonedBuilding.center, abandonedBuilding.radius, abandonedBuilding.GetIntruders(), abandonedBuilding.GetIntruderIds(), abandonedBuilding.entities, abandonedBuilding.privs, abandonedBuilding.CanDropBackpack, abandonedBuilding.AutomatedEvent, abandonedBuilding.AttackEvent, abandonedBuilding._guid });
                    PvpDelay.Remove(userid);
                }),
                Building = abandonedBuilding,
                time = Time.time + config.Abandoned.PVPDelay
            };
        }

        internal bool TryGetDelayValue(ulong userid, out DelaySettings ds)
        {
            return PvpDelay.TryGetValue(userid, out ds) && ds.valid;
        }

        private void TryEndAutomatedEvent(BasePlayer player)
        {
            if (IsPurgeEnabled || !config.Abandoned.CancelAutomatedEvent)
            {
                return;
            }
            using var tmp = AbandonedBuildings.ToPooledList();
            foreach (var abandonedBuilding in tmp)
            {
                if (abandonedBuilding.AutomatedEvent && abandonedBuilding.owners.Contains(player.userID))
                {
                    abandonedBuilding.CancelAutomatedEvent(player);
                    abandonedBuilding.RemoveExpiration();
                    abandonedBuilding.KillCollider();
                    abandonedBuilding.DestroyMe();
                }
            }
        }

        public Rewards GetRewards(EventType eventType) => eventType switch
        {
            EventType.Base => config.Abandoned.BaseRewards,
            EventType.LegacyShelter => config.Abandoned.LegacyShelterRewards,
            EventType.Tugboat or _ => config.Abandoned.TugboatRewards,
        };

        public void GiveRewards(BasePlayer player, ulong userid, int total, EventType eventType)
        {
            Rewards rewards = GetRewards(eventType);

            if (rewards.Money > 0 && Economics != null)
            {
                double money = rewards.DivideRewards ? rewards.Money / total : rewards.Money;
                Economics?.Call("Deposit", userid, money);
                Message(player, "EconomicsDeposit", money);
            }

            if (rewards.Money > 0 && IQEconomic != null)
            {
                int money = Convert.ToInt32(rewards.DivideRewards ? rewards.Money / (double)total : rewards.Money);
                IQEconomic?.Call("API_SET_BALANCE", userid, money);
                Message(player, "EconomicsDeposit", money);
            }

            if (rewards.Points > 0 && ServerRewards != null)
            {
                int points = rewards.DivideRewards ? rewards.Points / total : rewards.Points;
                ServerRewards?.Call("AddPoints", userid, points);
                Message(player, "ServerRewardPoints", points);
            }

            if (rewards.XP > 0 && SkillTree != null && player != null)
            {
                double xp = rewards.DivideRewards ? rewards.XP / (double)total : rewards.XP;
                SkillTree?.Call("AwardXP", player, xp);
                Message(player, "SkillTreeXP", xp);
            }

            if (rewards.XP > 0 && XLevels != null && player != null)
            {
                double xp = rewards.DivideRewards ? rewards.XP / (double)total : rewards.XP;
                XLevels?.Call("API_GiveXP", player, (float)xp); 
                Message(player, "SkillTreeXP", xp);
            }
        }

        private static void TryInvokeMethod(Action action)
        {
            try
            {
                action.Invoke();
            }
            catch (Exception ex)
            {
                Puts("{0} ERROR: {1}", action.Method.Name, ex);
            }
        }

        protected new static void Puts(string format, params object[] args)
        {
            Interface.Oxide.LogInfo("[{0}] {1}", Name, (args.Length != 0) ? string.Format(format, args) : format);
        }

        private string GetUserName(ulong userid)
        {
            var player = BasePlayer.FindAwakeOrSleepingByID(userid);
            if (player != null) return player.displayName;
            var playerName = ServerMgr.Instance.persistance.GetPlayerName(userid);
            if (playerName != null) return playerName;
            if (reportCoroutine == null) return null;
            var user = covalence.Players.FindPlayerById(userid.ToString());
            if (user != null) return user.Name;
            return null;
        }

        private IEnumerator ShowDataReportRoutine()
        {
            UpdateLastSeen();

            var total = data.LastSeen.ToList().Sum(x => GetRealtime(x.Value));
            var average = total / data.LastSeen.Count;

            yield return CoroutineEx.waitForSeconds(0.015f);

            Interface.Oxide.LogInfo("\nGenerating report...");
            Interface.Oxide.LogInfo("\nAverage offline time of {0} users: {1}", data.LastSeen.Count, FormatTime(TimeSpan.FromSeconds(average)));
            Interface.Oxide.LogInfo("\n\nCompiling rest of report...");

            var lastSeen = data.LastSeen.ToList();

            lastSeen.Sort((x, y) => x.Value.CompareTo(y.Value));

            foreach (var element in lastSeen)
            {
                string username = RemoveNonStandardCharacters(GetUserName(element.Key) ?? "[unknown]");

                _sb.AppendLine().AppendFormat("{0} ({1}) was last seen {2} ago", username, element.Key, FormatTime(TimeSpan.FromSeconds(GetRealtime(element.Value))));

                yield return CoroutineEx.waitForSeconds(0.015f);
            }

            Interface.Oxide.LogInfo(_sb.ToString());
            Interface.Oxide.LogInfo("\n\nReport finished.");

            reportCoroutine = null;
            _sb.Clear();
        }

        private string GetMessage(string key, string userid, params object[] args)
        {
            string message = lang.GetMessage(key, this, userid);

            if (userid == "server_console")
            {
                message = AbandonedBuilding.RemoveFormatting(message);
            }
            
            return args.Length > 0 ? string.Format(message, args) : message;
        }

        private string FormatTime(TimeSpan ts)
        {
            return $"{ts.Days:00}d {ts.Hours:00}h {ts.Minutes:00}m {ts.Seconds:00}s";
        }

        private int GetRealtime(int timestamp)
        {
            return Epoch.Current - timestamp;
        }

        private void RemoveReferences(AbandonedBuilding abandonedBuilding, List<BaseEntity> entities)
        {
            AbandonedReferences.RemoveAll((uid, x) => x == null || x.center == abandonedBuilding.center);
            entities.Where(e => e.IsValid()).ToList().ForEach(e => AbandonedReferences.Remove(e.net.ID.Value));
            if (AbandonedBuildings.Remove(abandonedBuilding) && AbandonedReferences.Count == 0) Unsubscribe();
        }

        private bool EnforceCooldownBetweenEvents(BasePlayer player)
        {
            return config.Abandoned.NoBypassCooldownBetweenEvents && HasEventCooldown(player);
        }

        private object ProcessAbandonedBuilding(BaseCombatEntity entity, HitInfo info, AbandonedBuilding abandonedBuilding)
        {
            if (!abandonedBuilding.InRange3D(entity.transform.position) || !IsBuildingDamageAllowed(entity, info, abandonedBuilding))
            {
                CancelDamage(info);
                return false;
            }

            bool isHuman = GetInitiatorPlayer(info, entity, false, out var attacker) && attacker.IsHuman();

            if (abandonedBuilding.IsOwnerLocked && isHuman && !abandonedBuilding.IsOwner(attacker))
            {
                abandonedBuilding.TryEjectFromLockedBase(attacker);
                abandonedBuilding.TryMessage(attacker, 10f, "Not An Ally");
                return false;
            }

            if (isHuman && !abandonedBuilding.IsParticipant(attacker.userID) && EnforceCooldownBetweenEvents(attacker))
            {
                abandonedBuilding.TryEjectFromLockedBase(attacker);
                return false;
            }

            if (entity is BaseMountable)
            {
                return abandonedBuilding.AllowPVP ? config.Abandoned.MountDamageFromPlayersPVP : config.Abandoned.MountDamageFromPlayersPVE;
            }

            if (info.WeaponPrefab is MLRSRocket)
            {
                return config.Abandoned.MLRS;
            }

            if (entity is AutoTurret turret)
            {
                if (config.Abandoned.AutoTurret.Enabled && config.Abandoned.AutoTurret.AutoAdjust && turret.sightRange < config.Abandoned.AutoTurret.SightRange * 2f)
                {
                    abandonedBuilding.SetupSightRange(turret, config.Abandoned.AutoTurret.SightRange, 2);
                }
                return true;
            }

            if (!isHuman)
            {
                return null;
            }

            if (config.Abandoned.BlockOutsideDamageBase && !abandonedBuilding.InRange3D(attacker.transform.position))
            {
                CancelDamage(info);
                return false;
            }

            if (config.Abandoned.CooldownBetweenEventsBlocksBaseDamage && !abandonedBuilding.CanDamageBuilding(attacker) && HasEventCooldown(attacker, abandonedBuilding))
            {
                CancelDamage(info);
                return false;
            }

            if (IsLootingWeapon(info) && attacker.IsConnected)
            {
                if (abandonedBuilding.InRange3D(attacker.transform.position))
                {
                    Rewards rewards = GetRewards(abandonedBuilding.eventType);
                    abandonedBuilding.TrySetOwnerLock(attacker, !rewards.OnEntityTakeDamage.HasValue || rewards.OnEntityTakeDamage.Value);
                }
                abandonedBuilding.TryResetDespawn(info);
            }

            return true;
        }

        private object IsEntityDamageAllowed(BaseEntity entity, HitInfo info, AbandonedBuilding abandonedBuilding)
        {
            if (IsDeployableEntity(entity))
            {
                if (EventTerritory(entity.transform.position))
                {
                    if (abandonedBuilding.owners.Contains(entity.OwnerID))
                    {
                        return true;
                    }
                    if (abandonedBuilding.IsParticipant(entity.OwnerID))
                    {
                        return true;
                    }
                }
                return null; //fire?
            }
            return IsBuildingDamageAllowed(entity, info, abandonedBuilding);
        }

        private bool IsBuildingDamageAllowed(BaseEntity entity, HitInfo info, AbandonedBuilding abandonedBuilding)
        {
            if (config.Abandoned.BlocksImmune && entity is BuildingBlock)
            {
                return false;
            }

            if (config.Abandoned.TwigImmune && entity is BuildingBlock block && block.grade == BuildingGrade.Enum.Twigs)
            {
                return false;
            }

            return !entity.OwnerID.IsSteamId() || abandonedBuilding.owners.Contains(entity.OwnerID) || abandonedBuilding.IsParticipant(entity.OwnerID);
        }

        private const ulong TUGME_PLUGIN_OWNERID = 76561199381312678;

        private object ProcessPlayerEntity(BaseEntity entity, HitInfo info)
        {
            if (entity.OwnerID == TUGME_PLUGIN_OWNERID || entity is BradleyAPC || entity is PatrolHelicopter || entity is BaseMountable)
            {
                return null;
            }

            if (!config.Abandoned.Twig && entity is BuildingBlock block && block.grade == BuildingGrade.Enum.Twigs)
            {
                return null;
            }

            if (Interface.CallHook("OnProcessPlayerEntity", entity, info) != null)
            {
                return null;
            }

            if (info.Initiator is not BasePlayer attacker || attacker.IPlayer == null)
            {
                return null;
            }

            if (attacker.HasPermission("abandonedbases.attack") && !IsAlly(attacker.userID, entity.OwnerID) && !IsOnWaitingList(attacker.UserIDString))
            {
                ShowTimeLeft(attacker, entity, CanDrawEntity(attacker));
            }

            return null;
        }

        public bool CanDrawEntity(BasePlayer player)
        {
            if (player == null || !player.IsAdmin)
            {
                return false;
            }
            return player.HasPermission("abandonedbases.admindrawentity");
        }

        public bool IsOnWaitingList(string userid)
        {
            if (config.WaitingListTime > 0)
            {
                if (_waitingList.Contains(userid))
                {
                    return true;
                }
                _waitingList.Add(userid);
                timer.Once(config.WaitingListTime, () => _waitingList.Remove(userid));
            }
            return false;
        }

        private const float DIST_OFFSET = 0.5f;

        private object ProcessVictim(BasePlayer victim, HitInfo info)
        {
            if (!victim.userID.IsSteamId() || CheckParentEntity(victim))
            {
                return null;
            }

            if (!GetAbandonedBuilding(victim, info, out var abandonedBuilding))
            {
                return null;
            }
            
            if (victim.EqualNetID(info.Initiator))
            {
                return true;
            }

            if (abandonedBuilding.AllowPVP && GetPVPDelay(victim.userID, out var ds1) > 0f)
            {
                if (GetInitiatorPlayer(info, victim, true, out var attacker1) && victim.userID != attacker1.userID && attacker1.userID.IsSteamId())
                {
                    if (RaidableBaseTerritory(attacker1.transform.position))
                    {
                        return null;
                    }

                    if (config.Abandoned.CooldownBetweenEventsBlocksPVPDamage && !abandonedBuilding.CanDamageBuilding(attacker1) && HasEventCooldown(attacker1, abandonedBuilding))
                    {
                        CancelDamage(info);
                        return false;
                    }

                    if (!abandonedBuilding.IsParticipant(attacker1.userID) && EnforceCooldownBetweenEvents(attacker1))
                    {
                        CancelDamage(info);
                        return false;
                    }

                    if (abandonedBuilding.InRange3D(attacker1.transform.position, DIST_OFFSET))
                    {
                        AddDelayInternal(abandonedBuilding, attacker1, info);
                        return true;
                    }

                    if (config.Abandoned.PVPDelayAnywhere && HasPVPDelay(attacker1.userID))
                    {
                        AddDelayInternal(abandonedBuilding, attacker1, info);
                        return true;
                    }

                    if (config.Abandoned.PVPDelayDamageInside && GetPVPDelay(attacker1.userID, out var ds2) > 0 && ds1.Building == ds2.Building && abandonedBuilding.InRange3D(victim.transform.position, DIST_OFFSET))
                    {
                        AddDelayInternal(abandonedBuilding, attacker1, info);
                        return true;
                    }

                    return false;
                }
                if (!info.Initiator.IsKilled() && abandonedBuilding.InRange3D(info.Initiator.transform.position))
                {
                    return true;
                }
                return null;
            }

            if (info.Initiator is AutoTurret && !info.Initiator.IsDestroyed && EventTerritory(info.Initiator.transform.position))
            {
                if (config.Abandoned.AutoTurret.Enabled) // as-is this applies to current and newly deployed turrets which keeps it a level playing field
                { 
                    info.damageTypes.Scale(DamageType.Bullet, UnityEngine.Random.Range(config.Abandoned.AutoTurret.Min, config.Abandoned.AutoTurret.Max));
                }
                return config.Abandoned.AutoTurret.CanDamagePlayers; 
            }

            if (info.WeaponPrefab is MLRSRocket && EventTerritory(victim.transform.position))
            {
                return config.Abandoned.MLRS;
            }

            var weapon = info.Initiator ?? info.WeaponPrefab ?? info.Weapon;

            if (weapon.IsKilled() || weapon.skinID == 14922524)
            {
                return null;
            }

            if (abandonedBuilding.AllowPVP ? config.Abandoned.MountDamageFromPlayersPVP : config.Abandoned.MountDamageFromPlayersPVE)
            {
                if (weapon.ShortPrefabName == "turret_attackheli" && weapon.GetParentEntity() is BaseMountable m && m.IsValid())
                {
                    AbandonedReferences[m.net.ID.Value] = abandonedBuilding;
                }
            }

            if (!GetInitiatorPlayer(info, victim, true, out var attacker2) || !attacker2.IsHuman() || RaidableBaseTerritory(attacker2.transform.position))
            {
                if (IsTrueDamage(weapon))
                {
                    if (abandonedBuilding.EntityOwners.TryGetValue(weapon.net.ID.Value, out var eo)) return abandonedBuilding.owners.Contains(eo.OwnerID) || abandonedBuilding.AllowPVP;
                    if (abandonedBuilding.owners.Contains(weapon.OwnerID) || EventTerritory(victim.transform.position)) return abandonedBuilding.AllowPVP;
                }

                return null;
            }

            if (abandonedBuilding.AllowPVP ? config.Abandoned.MountDamageFromPlayersPVP : config.Abandoned.MountDamageFromPlayersPVE)
            {
                BaseMountable m = attacker2.GetMounted() ?? attacker2.GetParentEntity() as BaseMountable;
                if (m.IsValid())
                {
                    BaseVehicle vehicle = m.HasParent() ? m.VehicleParent() : m as BaseVehicle;
                    if (vehicle.IsValid())
                    {
                        AbandonedReferences[vehicle.net.ID.Value] = abandonedBuilding;
                    }
                    else
                    {
                        AbandonedReferences[m.net.ID.Value] = abandonedBuilding;
                    }
                }
            }

            if (victim.IsHuman() && !abandonedBuilding.IsParticipant(attacker2.userID) && EnforceCooldownBetweenEvents(attacker2))
            {
                CancelDamage(info);
                return false;
            }

            if (config.Abandoned.CooldownBetweenEventsBlocksPVPDamage && !abandonedBuilding.CanDamageBuilding(attacker2) && HasEventCooldown(attacker2, abandonedBuilding))
            {
                CancelDamage(info);
                return false;
            }

            if (config.Abandoned.BlockOutsideDamagePlayer && !abandonedBuilding.InRange3D(attacker2.transform.position, DIST_OFFSET))
            {
                CancelDamage(info);
                return false;
            }

            if (abandonedBuilding.IsAbandonedSleeper(victim))
            {
                AddDelayInternal(abandonedBuilding, attacker2, info);
                return true;
            }

            if (!abandonedBuilding.AllowPVP || !abandonedBuilding.InRange3D(victim.transform.position, DIST_OFFSET) || !abandonedBuilding.InRange3D(attacker2.transform.position, DIST_OFFSET))
            {
                CancelDamage(info);
                return null;
            }

            if (abandonedBuilding.anchor.IsKilled() || abandonedBuilding.eventType == EventType.LegacyShelter)
            {
                AddDelayInternal(abandonedBuilding, attacker2, info);
                return true;
            }

            if (attacker2.GetParentEntity() is Tugboat && victim.GetParentEntity() is Tugboat)
            {
                AddDelayInternal(abandonedBuilding, attacker2, info);
                return true;
            }

            return null;
        }

        public bool GetInitiatorPlayer(HitInfo info, BaseCombatEntity entity, bool heat, out BasePlayer target)
        {
            var weapon = info.Initiator ?? info.Weapon ?? info.WeaponPrefab;

            target = weapon switch
            {
                BasePlayer player => player,
                { creatorEntity: BasePlayer player } => player,
                { parentEntity: EntityRef parentEntity } when parentEntity.Get(true) is BasePlayer player => player,
                _ => heat && info.damageTypes.Has(DamageType.Heat) ? entity.lastAttacker as BasePlayer : null
            };

            return !target.IsKilled();
        }

        private bool IsTrueDamage(BaseEntity e)
        {
            if (!e.IsValid())
            {
                return false;
            }
            return AbandonedReferences.ContainsKey(e.net.ID.Value) || e.skinID == 1587601905 || e.ShortPrefabName == "spikes.floor" || TrueDamage.Contains(e.GetType().Name);
        }

        private bool IsAlly(BasePlayer player, ulong targetId)
        {
            if (config.Abandoned.NoBypassCooldownBetweenEvents && HasEventCooldown(player))
            {
                return false;
            }

            return IsAlly(player.userID, targetId);
        }

        private bool IsAlly(ulong playerId, ulong targetId)
        {
            if (playerId == targetId)
            {
                return true;
            }

            if (RelationshipManager.ServerInstance.playerToTeam.TryGetValue(playerId, out var team) && team.members.Contains(targetId))
            {
                return true;
            }

            if (Clans.CanCall() && Convert.ToBoolean(Clans?.Call("IsMemberOrAlly", playerId, targetId)))
            {
                return true;
            }

            if (Friends.CanCall() && Convert.ToBoolean(Friends?.Call("AreFriends", playerId.ToString(), targetId.ToString())))
            {
                return true;
            }

            return false;
        }

        private void ShowTimeLeft(BasePlayer player, BaseEntity entity, bool canDrawEntity)
        {
            if (IsIgnoredPrefab(entity.ShortPrefabName) || IsInZone(entity.transform.position) || IsAbandoned(entity))
            {
                return;
            }

            if (config.Abandoned.TooClose && EventTerritory(entity.transform.position))
            {
                return;
            }

            var reason = SkippedReason.None;
            var timestamp = Epoch.Current;
            var owners = new HashSet<ulong>();
            var disabled = config.Abandoned.Disabled.Exists(x => x.CanBlockAutomaticConversion());
            var tugboat = entity.HasParent() && entity.GetParentEntity() is Tugboat tugboat1 ? tugboat1 : entity as Tugboat;
            var shelter = entity.HasParent() && entity.GetParentEntity() is LegacyShelter shelter1 ? shelter1 : entity as LegacyShelter;

            if (config.Abandoned.PreventHogging && IsHogging(player, null, true, "HoggingFinishYourRaid"))
            {
                disabled = true;
            }

            SimplePrivilege sp = tugboat ? GetVehiclePrivilege(tugboat.children) : shelter ? shelter.GetEntityBuildingPrivilege() : null;
            var user = player.IPlayer;

            if (sp != null && CanConvert(user, entity, sp, owners, timestamp, player.userID, out reason) && !disabled)
            {
                TryConvertEntity(user, entity, sp, owners, false, config.Abandoned.AllowPVPAttack == true, ConversionType.Attack);
                return;
            }
            else
            {
                var priv = entity.GetBuildingPrivilege(entity.WorldSpaceBounds());

                if (priv == null)
                {
                    if (!EventTerritory(entity.transform.position))
                    {
                        Message(user, "No privilege found");
                    }

                    return;
                }

                var building = priv.GetBuilding();
                if (building == null || building.decayEntities == null)
                {
                    Message(player, "NotCloseEnough");
                    return;
                }

                var entities = building.decayEntities.ToList<BaseEntity>();
                if (CanConvert(user, player, entities, priv, owners, timestamp, canDrawEntity, true, true, out var position, out var ownerid, out reason) && !disabled)
                {
                    TryConvertCompound(user, entities, owners, position, ownerid, false, config.Abandoned.AllowPVPAttack == true, ConversionType.Attack);

                    return;
                }
            }

            if (reason == SkippedReason.Ally || !player.HasPermission("abandonedbases.attack.time") || EventTerritory(entity.transform.position))
            {
                return;
            }

            var timeLeft = double.MinValue;

            foreach (var owner in owners)
            {
                var purge = PurgeSettings.Find(config, owner);

                if (purge == null)
                {
                    continue;
                }

                if (purge.NoPurge)
                {
                    timeLeft = double.MinValue;
                    break;
                }

                if (!data.LastSeen.TryGetValue(owner, out var lastSeen))
                {
                    continue;
                }

                timeLeft = Math.Max(timeLeft, lastSeen + purge.Lifetime - timestamp);
            }

            if (timeLeft != double.MinValue)
            {
                if (disabled && timeLeft <= 0) Message(player, "TimeLeftDisabled");
                else Message(player, "TimeLeft", FormatTime(player.UserIDString, timeLeft < 0 ? 0 : timeLeft));
            }

            if (reason == SkippedReason.ZoneManager)
            {
                Message(player, "IsInBlockedZone");
            }
        }

        private void DestroyAbandonedLimitUi(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "AbandonedLimitPanel");
        }

        private void ShowAbandonedLimitUi(BasePlayer player, BuildingPrivlidge priv)
        {
            if (player == null || priv == null)
            {
                return;
            }

            var owners = new HashSet<ulong>();

            if (priv.OwnerID.IsSteamId() && (!config.Abandoned.Privilege.SkipCheats || priv.OwnerID != player.userID || (!player.IsFlying && !player.limitNetworking)))
            {
                owners.Add(priv.OwnerID);
            }

            foreach (var userid in priv.authorizedPlayers)
            {
                if (config.Abandoned.Privilege.SkipCheats && userid == player.userID && (player.IsFlying || player.limitNetworking))
                {
                    continue;
                }
                if (userid.IsSteamId())
                {
                    owners.Add(userid);
                }
            }

            var building = priv.GetBuilding();
            var entities = building?.decayEntities?.ToList<BaseEntity>();

            if (entities != null)
            {
                foreach (var entity in entities)
                {
                    if (entity.OwnerID.IsSteamId())
                    {
                        owners.Add(entity.OwnerID);
                    }
                }
            }

            var lifetime = double.MinValue;
            var limit = "N/A";

            foreach (var owner in owners)
            {
                if (config.Abandoned.Privilege.SkipCheats && owner == player.userID && (player.IsFlying || player.limitNetworking))
                {
                    continue;
                }

                var purge = PurgeSettings.Find(config, owner);

                if (purge == null)
                {
                    limit = "∞";
                    continue;
                }

                if (purge.NoPurge)
                {
                    lifetime = double.MinValue;
                    limit = "∞";
                    break;
                }

                if (purge.Lifetime > lifetime)
                {
                    lifetime = purge.Lifetime;
                    limit = purge.LifetimeRaw;
                }
            }

            string text;

            try
            {
                text = GetMessage(limit == "∞" || limit == "N/A" ? "Limit" : "Limit Days", player.UserIDString, limit);
            }
            catch
            {
                text = string.Format(limit == "∞" || limit == "N/A" ? "Abandoned Base Limit: {0}" : "Abandoned Base Limit: {0} days", player.UserIDString, limit);

                Puts("Your French language file needs to be updated from within the Abandoned Bases zip archive!");
            }

            var container = new CuiElementContainer();

            container.Add(new CuiElement
            {
                Name = "AbandonedLimitPanel",
                Parent = "Overlay",
                DestroyUi = "AbandonedLimitPanel",
                Components =
                {
                    new CuiRectTransformComponent{ AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = config.Abandoned.Privilege.OffsetMin, OffsetMax = config.Abandoned.Privilege.OffsetMax }
                }
            });

            container.Add(new CuiElement
            {
                DestroyUi = "AbandonedLimitText",
                Name = "AbandonedLimitText",
                Parent = "AbandonedLimitPanel",
                Components = {
                    new CuiTextComponent { Text = text, Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleRight, Color = AbandonedPrivilegeUi.ConvertHexToRGBA(config.Abandoned.Privilege.TextColor, config.Abandoned.Privilege.Alpha) },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-158.851 -11.772", OffsetMax = "158.849 11.772" }
                }
            });

            CuiHelper.AddUi(player, container);
        }

        private void RegisterPermissions()
        {
            permission.RegisterPermission("abandonedbases.admin", this);
            permission.RegisterPermission("abandonedbases.keepbackpackrust", this);
            permission.RegisterPermission("abandonedbases.noentitylimit", this);
            permission.RegisterPermission("abandonedbases.canbypass", this);
            permission.RegisterPermission("abandonedbases.admindrawentity", this);
            permission.RegisterPermission("abandonedbases.convert.free", this);
            permission.RegisterPermission("abandonedbases.attack", this);
            permission.RegisterPermission("abandonedbases.attack.time", this);
            permission.RegisterPermission("abandonedbases.attack.lastseen", this);
            permission.RegisterPermission("abandonedbases.convert.cancel", this);
            permission.RegisterPermission("abandonedbases.convert.claim", this);
            permission.RegisterPermission("abandonedbases.convert.nocooldown", this);
            permission.RegisterPermission("abandonedbases.convert.cancel.nocooldown", this);
            permission.RegisterPermission("abandonedbases.convert.cancel.manual", this);
            permission.RegisterPermission("abandonedbases.noeventcooldown", this);
            permission.RegisterPermission("abandonedbases.convert", this);
            permission.RegisterPermission("abandonedbases.exclude", this);
            permission.RegisterPermission("abandonedbases.notices", this);
            permission.RegisterPermission("abandonedbases.manualnotices", this);
            permission.RegisterPermission("abandonedbases.purgeday", this);
            permission.RegisterPermission("abandonedbases.report", this);
            permission.RegisterPermission("abandonedbases.teleport", this);
            permission.RegisterPermission("abandonedbases.time", this);
        }

        private void RegisterCommands()
        {
            AddCovalenceCommand("ab.report", "CommandReport");
            AddCovalenceCommand("ab.debug", "CommandDebug");
            AddCovalenceCommand("sab", "CommandStart");
            AddCovalenceCommand("sar", "CommandConvert");
            AddCovalenceCommand("saranon", "CommandConvert");
            AddCovalenceCommand("abclaim", "CommandClaim");
            AddCovalenceCommand("abcooldown", "CommandCooldown");
        }

        private void StartNotificationTimer()
        {
            timer.Repeat(Mathf.Clamp(config.Messages.Interval, 1f, 60f), 0, CheckNotifications);
        }

        private void Unsubscribe()
        {
            Unsubscribe(nameof(OnBackpackDrop));
            Unsubscribe(nameof(OnEngineStart));
            Unsubscribe(nameof(OnEntitySpawned));
            Unsubscribe(nameof(OnEntityDeath));
            Unsubscribe(nameof(OnEntityKill));
            if (!IsPurgeEnabled)
            {
                Unsubscribe(nameof(OnEntityBuilt));
            }
            Unsubscribe(nameof(OnPlayerSleepEnded));
            Unsubscribe(nameof(OnServerCommand));
            Unsubscribe(nameof(OnPlayerCommand));
            Unsubscribe(nameof(CanLootPlayer));
            Unsubscribe(nameof(CanTeleport));
            Unsubscribe(nameof(CanEntityBeTargeted));
            Unsubscribe(nameof(CanEntityTrapTrigger));
            Unsubscribe(nameof(CanOpenBackpack));
            Unsubscribe(nameof(CanRevivePlayer));
            Unsubscribe(nameof(OnRestoreUponDeath));
            Unsubscribe(nameof(CanLootEntity));
            Unsubscribe(nameof(CanPickupEntity));
        }

        private void Subscribe()
        {
            if (Manager == null)
            {
                return;
            }

            if (config.Abandoned.BlacklistedCommands.Count > 0 && !config.Abandoned.BlacklistedCommands.All(DefaultBlacklistCommands().Contains))
            {
                Subscribe(nameof(OnServerCommand));
                Subscribe(nameof(OnPlayerCommand));
            }

            if (config.Messages.LocalCompletion > 0 || config.Messages.RaidCompletion)
            {
                Subscribe(nameof(OnLootEntityEnd));
            }

            if (config.Abandoned.Tugboats.Engine)
            {
                Subscribe(nameof(OnEngineStart));
            }

            if (!config.Abandoned.Backpacks)
            {
                Subscribe(nameof(CanOpenBackpack));
            }

            if (config.Abandoned.CannotLootWoundedPlayers || config.Abandoned.CanLootPlayerAlive || config.PlayersCanKillAbandonedSleepers)
            {
                Subscribe(nameof(CanLootPlayer));
            }

            if (config.Abandoned.BlacklistedPickupItems.Count > 0)
            {
                Subscribe(nameof(CanPickupEntity));
            }

            if (!config.Abandoned.AllowTeleport)
            {
                Subscribe(nameof(CanTeleport));
            }

            Subscribe(nameof(OnBackpackDrop));
            Subscribe(nameof(CanLootEntity));
            Subscribe(nameof(OnEntitySpawned));
            Subscribe(nameof(OnEntityDeath));
            Subscribe(nameof(OnEntityKill));
            Subscribe(nameof(OnEntityBuilt));
            Subscribe(nameof(OnPlayerSleepEnded));
            Subscribe(nameof(CanEntityBeTargeted));
            Subscribe(nameof(CanEntityTrapTrigger));
            Subscribe(nameof(OnRestoreUponDeath));
            Subscribe(nameof(CanRevivePlayer));
        }

        private void SetupAbandonedRoutine()
        {
            if (config.Startup)
            {
                StartAbandonedRoutine(null, false, true);
            }

            if (config.Delay > 0f)
            {
                timer.Every(Math.Max(900f, config.Delay), StartAbandonedRoutine);
            }
        }

        private void StopAbandonedCoroutine()
        {
            if (reportCoroutine != null)
            {
                ServerMgr.Instance.StopCoroutine(reportCoroutine);
                reportCoroutine = null;
            }

            if (abandonedCoroutine != null)
            {
                ServerMgr.Instance.StopCoroutine(abandonedCoroutine);
                abandonedCoroutine = null;
            }
        }

        private void StartAbandonedRoutine()
        {
            StartAbandonedRoutine(null, false, true);
        }

        private void StartAbandonedRoutine(IPlayer user, bool canBypass, bool anon)
        {
            if (!canBypass && config.Abandoned.Disabled.Exists(x => x.CanBlockAutomaticConversion()))
            {
                return;
            }
            if (!CheckDefaultGroup(user ?? _consolePlayer))
            {
                return;
            }
            if (abandonedCoroutine == null)
            {
                Message(user, "StartScan");
                abandonedCoroutine = ServerMgr.Instance.StartCoroutine(AbandonedCoroutine(user, anon));
            }
            else if (user != null)
            {
                ServerMgr.Instance.StopCoroutine(abandonedCoroutine);
                abandonedCoroutine = null;
                Message(user, "You have aborted the scan.");
            }
        }

        private Dictionary<string, int> _results = new();
        private Dictionary<string, Dictionary<ulong, int>> _exclusions = new();

        private void AddResults(string key)
        {
            if (!_results.TryGetValue(key, out int value))
            {
                _results[key] = value = 0;
            }
            _results[key] = value + 1;
        }

        private string GetResults()
        {
            return string.Join(", ", _results.Select(x => $"{x.Key} ({x.Value})"));
        }

        private IEnumerator AbandonedCoroutine(IPlayer user, bool anon)
        {
            if (user == null && config.Abandoned.MinimumOnlinePlayers > 0)
            {
                while (BasePlayer.activePlayerList.Count < config.Abandoned.MinimumOnlinePlayers)
                {
                    if (DebugMode) Puts("Insufficient amount of players online {0}/{1}", BasePlayer.activePlayerList.Count, config.Abandoned.MinimumOnlinePlayers);
                    yield return CoroutineEx.waitForSeconds(1f);
                }
            }

            float waitTime = config.Abandoned.WaitTime < 2f ? 2f : config.Abandoned.WaitTime;
            int timestamp = Epoch.Current;

            _results.Clear();
            _exclusions.Clear();

            UpdateLastSeen();
            KillInactiveSleepers(timestamp);

            yield return AbandonedBuildingCo(user, timestamp, waitTime, anon, !IsPurgeEnabled ? ConversionType.Automated : ConversionType.Type4);

            if (!config.Abandoned.Tugboats.ManualOnly || !config.Abandoned.Shelters.ManualOnly)
            {
                yield return AbandonedEntityCo(timestamp, waitTime, anon, ConversionType.Automated);
            }

            if (user != null)
            {
                if (DebugMode)
                {
                    foreach (var (permission, users) in _exclusions)
                    {
                        if (user == null) Puts("{0} user(s) with {1} in the search: {2}", users.Count, permission, string.Join(", ", users.Select(u => $"{u.Key} ({u.Value} hits)")));
                        else user.Message(string.Format("{0} user(s) with {1} in the search: {2}", users.Count, permission, string.Join(", ", users.Select(u => $"{u.Key} ({u.Value} hits)"))));
                    }
                }

                if (_results.Count > 0)
                {
                    Message(user, "EndScan", GetResults());
                }
            }
            
            SleeperKillList.RemoveAll(x => x == null || x.IsConnected || !x.IsSleeping() || x.IsDead());
            foreach (var sleeper in SleeperKillList)
            {
                sleeper.Die(new HitInfo(sleeper, sleeper, DamageType.Suicide, 1000f));
            }

            SleeperKillList.Clear();
            _results.Clear();
            _exclusions.Clear();
            abandonedCoroutine = null;
        }

        private IEnumerator AbandonedBuildingCo(IPlayer user, int timestamp, float waitTime, bool anon, ConversionType type)
        {
            var player = user.ToPlayer();
            bool canDrawEntity = CanDrawEntity(player);

            foreach (var building in BuildingManager.server.buildingDictionary.Values)
            {
                yield return AbandonedBuildingCo(user, timestamp, waitTime, anon, type, player, canDrawEntity, building);
            }
        }

        private IEnumerator AbandonedBuildingCo(IPlayer user, int timestamp, float waitTime, bool anon, ConversionType type, BasePlayer player, bool canDrawEntity, BuildingManager.Building building)
        {
            if (building.decayEntities.IsNullOrEmpty())
            {
                yield break;
            }

            var wait = false;
            var owners = new HashSet<ulong>();
            var priv = building.GetDominatingBuildingPrivilege();
            var entities = building.decayEntities.ToList<BaseEntity>();
            var canMessage = CanDebug(user);

            if (CanConvert(user, player, entities, priv, owners, timestamp, canDrawEntity, false, canMessage, out var position, out var ownerid, out var reason))
            {
                if (wait)
                {
                    yield return CoroutineEx.waitForSeconds(waitTime);
                    wait = false;
                }

                yield return TryConvertCompoundCo(user, entities, owners, position, ownerid, anon, true, config.Abandoned.AllowPVP, type);
                wait = true;
            }
            else
            {
                if (canMessage && reason == SkippedReason.ZoneManager)
                {
                    Message(user, "IsInBlockedZone");
                    AddResults("Skipping zone");
                }
                else AddResults(reason.ToString());
                if (type == ConversionType.Automated) OnBaseSkipped(position, ownerid, owners, reason, building);
            }

            yield return CoroutineEx.waitForSeconds(0.1f);
        }

        public void OnBaseSkipped(Vector3 v, ulong userid, HashSet<ulong> owners, SkippedReason reason, BuildingManager.Building building)
        {
            if (reason == SkippedReason.None || reason == SkippedReason.EventInProgress) return;
            Interface.CallHook("OnAbandonedBaseSkipped", v, userid, owners, reason.ToString(), building);
        }

        public void OnTugboatSkipped(Vector3 v, ulong userid, HashSet<ulong> owners, SkippedReason reason, BaseEntity entity)
        {
            if (reason == SkippedReason.None || reason == SkippedReason.EventInProgress) return;
            Interface.CallHook("OnAbandonedTugboatSkipped", v, userid, owners, reason.ToString(), entity);
        }

        public void OnShelterSkipped(Vector3 v, ulong userid, HashSet<ulong> owners, SkippedReason reason, BaseEntity entity)
        {
            if (reason == SkippedReason.None || reason == SkippedReason.EventInProgress) return;
            Interface.CallHook("OnAbandonedShelterSkipped", v, userid, owners, reason.ToString(), entity);
        }

        private IEnumerator AbandonedEntityCo(int timestamp, float waitTime, bool anon, ConversionType type, ulong searchForId = 0)
        {
            bool wait = false;

            foreach (var ent in BaseNetworkable.serverEntities)
            {
                BaseEntity entity = ent as BaseEntity;
                if (entity == null || searchForId != 0 && entity.OwnerID != searchForId)
                {
                    continue;
                }

                SimplePrivilege priv = null;

                if (entity is Tugboat tugboat)
                {
                    if (config.Abandoned.Tugboats.ManualOnly || IsAbandoned(tugboat))
                    {
                        continue;
                    }

                    priv = GetVehiclePrivilege(tugboat.children);
                }
                else if (entity is LegacyShelter shelter)
                {
                    if (config.Abandoned.Shelters.ManualOnly || IsAbandoned(shelter))
                    {
                        continue;
                    }

                    priv = shelter.GetEntityBuildingPrivilege();
                }

                if (priv == null)
                {
                    continue;
                }

                if ((entity.transform.position - Vector3.zero).sqrMagnitude < 25f)
                {
                    continue;
                }

                var owners = new HashSet<ulong>();
                var baseEntity = entity as BaseEntity;
                if (CanConvert(null, baseEntity, priv, owners, timestamp, 0uL, out SkippedReason reason))
                {
                    if (wait)
                    {
                        yield return CoroutineEx.waitForSeconds(waitTime);
                        wait = false;
                    }

                    yield return TryConvertEntityCo(null, baseEntity, priv, owners, anon, entity is Tugboat ? !config.Abandoned.Tugboats.Unlock : !config.Abandoned.Shelters.Unlock, config.Abandoned.AllowPVP, type);
                    wait = true;
                }

                if (reason != SkippedReason.None && type == ConversionType.Automated)
                {
                    if (entity is Tugboat) OnTugboatSkipped(entity.transform.position, entity.OwnerID, owners, reason, entity);
                    if (entity is LegacyShelter) OnShelterSkipped(entity.transform.position, entity.OwnerID, owners, reason, entity);
                }

                yield return CoroutineEx.waitForSeconds(0.1f);
            }
        }

        public SimplePrivilege GetVehiclePrivilege(List<BaseEntity> children)
        {
            foreach (BaseEntity child in children)
            {
                var vehiclePrivilege = child as VehiclePrivilege;
                if (vehiclePrivilege != null)
                {
                    return vehiclePrivilege;
                }
            }
            return null;
        }

        private bool CanConvert(IPlayer user, BasePlayer player, List<BaseEntity> entities, BuildingPrivlidge priv, HashSet<ulong> owners, int timestamp, bool canDrawEntity, bool hasPrivilege, bool canMessage, out Vector3 position, out ulong ownerid, out SkippedReason reason)
        {
            position = default;
            ownerid = default;
            Vector3 a = default;

            if (!priv.IsKilled())
            {
                hasPrivilege = true;
                ownerid = priv.OwnerID;
                position = priv.transform.position;
            }

            AddFromAuthed(user, priv, owners, player, canDrawEntity, ref hasPrivilege);

            foreach (var entity in entities)
            {
                if (entity.IsKilled() || entity.net == null || !config.Abandoned.Twig && entity is BuildingBlock block && block.grade == BuildingGrade.Enum.Twigs)
                {
                    continue;
                }

                a = entity.transform.position;
                
                if (IsPurgeEnabled)
                {
                    position = a;
                }

                if (AbandonedReferences.ContainsKey(entity.net.ID.Value))
                {
                    reason = SkippedReason.EventInProgress;
                    return false;
                }

                if (IsInZone(a))
                {
                    reason = SkippedReason.ZoneManager;
                    return false;
                }

                AdminDrawEntity(user, entity);

                if (canDrawEntity && player != null && entity.OwnerID == player.userID)
                {
                    player.SendConsoleCommand("ddraw.text", 10f, Color.cyan, entity.CenterPoint(), GetMessage("Drawn Entity", player.UserIDString));
                }

                if (entity.OwnerID.IsSteamId())
                {
                    owners.Add(entity.OwnerID);
                }
                
                if (entity is BuildingPrivlidge priv2)
                {
                    AddFromAuthed(user, priv2, owners, player, canDrawEntity, ref hasPrivilege);

                    if (hasPrivilege && (priv.IsKilled() || IsHigherCount(priv2, priv)))
                    {
                        priv = priv2;
                        ownerid = priv2.OwnerID;
                        position = priv2.transform.position;
                    }
                }
            }

            if (!owners.Exists(owner => owner.IsSteamId()))
            {
                if (CanDebug(user)) Message(user, $"Building {a} is not owned by any players");

                reason = SkippedReason.NoPlayerOwner;
                return false;
            }

            if (!CanPurge(owners, timestamp))
            {
                if (canMessage) Message(user, owners);

                reason = SkippedReason.CannotPurgeYet;
                return false;
            }

            if (!hasPrivilege && !IsPurgeEnabled)
            {
                if (canMessage) Message(user, $"No privilege found : {a}");

                reason = SkippedReason.NoPrivilege;
                return false;
            }

            reason = SkippedReason.None;
            return true;
        }

        private static bool IsHigherCount(BuildingPrivlidge other, BuildingPrivlidge current)
        {
            var otherBuilding = other.GetBuilding();
            if (otherBuilding == null || !otherBuilding.HasDecayEntities())
            {
                return false;
            }
            var currentBuilding = current.GetBuilding();
            if (currentBuilding == null || !currentBuilding.HasDecayEntities())
            {
                return false;
            }
            return otherBuilding.decayEntities.Count > currentBuilding.decayEntities.Count;
        }

        private void AddFromAuthed(IPlayer user, BuildingPrivlidge priv, HashSet<ulong> owners, BasePlayer player, bool canDrawPrivilege, ref bool hasPrivilege)
        {
            if (priv.IsValid())
            {
                foreach (var userid in priv.authorizedPlayers)
                {
                    if (userid.IsSteamId())
                    {
                        owners.Add(userid);
                    }
                }

                AdminDrawEntity(user, priv);

                if (canDrawPrivilege && priv.AnyAuthed() && priv.IsAuthed(player))
                {
                    player.SendConsoleCommand("ddraw.text", 10f, Color.cyan, priv.CenterPoint(), GetMessage("Drawn Authed", player.UserIDString));
                }

                hasPrivilege = true;
            }
        }

        private bool CanConvert<T>(IPlayer user, T entity, SimplePrivilege priv, HashSet<ulong> owners, int timestamp, ulong userid, out SkippedReason reason) where T : BaseEntity
        {
            reason = SkippedReason.None;
            
            if (priv == null || entity == null || entity.children == null)
            {
                reason = SkippedReason.Null;
                return false;
            }

            if (!priv.AnyAuthed() && !entity.children.Exists(child => child.OwnerID.IsSteamId()))
            {
                reason = SkippedReason.NoPlayerOwner;
                return false;
            }

            foreach (var id in priv.authorizedPlayers)
            {
                if (userid != 0 && reason != SkippedReason.Ally && IsAlly(id, userid))
                {
                    reason = SkippedReason.Ally;
                    return false;
                }

                if (id.IsSteamId())
                {
                    owners.Add(id);
                }
            }

            foreach (var child in entity.children)
            {
                if (child.IsKilled() || child.net == null || child.OwnerID == 0)
                {
                    continue;
                }

                if (AbandonedReferences.ContainsKey(child.net.ID.Value))
                {
                    reason = SkippedReason.EventInProgress;
                    return false;
                }

                if (userid != 0 && reason != SkippedReason.Ally && IsAlly(child.OwnerID, userid))
                {
                    reason = SkippedReason.Ally;
                    return false;
                }

                if (IsInZone(child.transform.position))
                {
                    reason = SkippedReason.ZoneManager;
                    return false;
                }

                if (child.OwnerID.IsSteamId())
                {
                    owners.Add(child.OwnerID);
                }

                AdminDrawEntity(user, child);
            }

            if (!owners.Exists(owner => owner.IsSteamId()))
            {
                Message(user, entity is LegacyShelter ? "Shelter Player Requirement" : "Tugboat Player Requirement");
                reason = SkippedReason.NoPlayerOwner;
                return false;
            }

            if (!CanPurge(owners, timestamp))
            {
                Message(user, owners);
                reason = SkippedReason.CannotPurgeYet;
                return false;
            }

            reason = SkippedReason.None;
            return true;
        }

        private void Message(IPlayer user, HashSet<ulong> owners)
        {
            if (user == null)
            {
                return;
            }

            if (config.Messages.BaseIsActive)
            {
                Message(user, "Base is active");
                if (CanDebug(user))
                {
                    HashSet<string> perms = new();
                    foreach (var owner in owners.Select(x => x.ToString()))
                    {
                        perms.UnionWith(permission.GetUserPermissions(owner).Where(x => x.StartsWith("abandonedbases.")));
                        if (perms.Count == 0) perms.Add("No players have any inherited permissions!");
                        Message(user, $"Permissions for {owner}: {string.Join(", ", perms)}");
                        perms.Clear();
                    }
                }
            }

            if (!user.HasPermission("abandonedbases.attack.lastseen"))
            {
                return;
            }

            int num = 0;
            
            owners.ForEach(userid =>
            {
                if (userid.HasPermission("abandonedbases.exclude") && !user.IsAdmin && userid.ToString() != user.Id)
                {
                    return;
                }
                var username = GetUserName(userid) ?? "unknown";
                if (BasePlayer.activePlayerList.Exists(x => x.userID == userid))
                {
                    string message = GetMessage("FormatOnline", user.Id).Replace("{index}", $"{++num}").Replace("{username}", username).Replace("{userid}", $"{userid}");
                    user.Message(user.IsServer ? AbandonedBuilding.RemoveFormatting(message) : message);
                    return;
                }
                if (data.LastSeen.TryGetValue(userid, out var lastSeen))
                {
                    string message = GetMessage("FormatLastSeen", user.Id).Replace("{index}", $"{++num}").Replace("{username}", username).Replace("{userid}", $"{userid}").Replace("{time}", FormatTime(user.Id, GetRealtime(lastSeen)));
                    user.Message(user.IsServer ? AbandonedBuilding.RemoveFormatting(message) : message);
                }
                else
                {
                    string message = GetMessage("FormatLastSeenUnknown", user.Id).Replace("{index}", $"{++num}").Replace("{username}", username).Replace("{userid}", $"{userid}");
                    user.Message(user.IsServer ? AbandonedBuilding.RemoveFormatting(message) : message);
                }
            });
        }

        private void AdminDrawEntity(IPlayer user, BaseEntity entity)
        {
            var player = user.ToPlayer();
            if (!CanDrawEntity(player))
            {
                return;
            }
            if (entity is BuildingPrivlidge priv)
            {
                foreach (var x in priv.authorizedPlayers)
                {
                    if (config.Purges.FindIndex(purge => x.HasPermission(purge.Permission)) != -1)
                    {
                        player.SendConsoleCommand("ddraw.text", 30f, entity.OwnerID == player.userID ? Color.green : Color.magenta, entity.transform.position, "A");
                        return;
                    }
                }
            }
            else if (entity is VehiclePrivilege vpriv)
            {
                foreach (var x in vpriv.authorizedPlayers)
                {
                    if (config.Purges.FindIndex(purge => x.HasPermission(purge.Permission)) != -1)
                    {
                        player.SendConsoleCommand("ddraw.text", 30f, entity.OwnerID == player.userID ? Color.green : Color.magenta, entity.transform.position, "A");
                        return;
                    }
                }
            }
            if (config.Purges.FindIndex(purge => entity.OwnerID.HasPermission(purge.Permission)) != -1)
            {
                player.SendConsoleCommand("ddraw.text", 30f, entity.OwnerID == player.userID ? Color.green : Color.magenta, entity.transform.position, "X");
            }
        }

        private List<BasePlayer> SleeperKillList = new();
        private void KillInactiveSleepers(int timestamp)
        {
            Subscribe(nameof(OnEntitySpawned));

            foreach (var target in BasePlayer.sleepingPlayerList)
            {
                if (!target.IsConnected && !IsUserExcluded(target.userID) && CanPurge(target.userID, timestamp))
                {
                    if (config.PlayersCanKillAbandonedSleepers && config.MoveInventory && (target.GetBuildingPrivilege() != null || target.HasPrivilegeFromOther()))
                    {
                        continue;
                    }

                    if (config.KillInactiveSleepers)
                    {
                        SleeperKillList.Add(target);
                    }
                }
            }
        }

        private void AddOrUpdateUserExclusion(string perm, ulong userid)
        {
            perm = string.IsNullOrEmpty(perm) ? "abandonedbases.immune" : perm;
            if (!_exclusions.TryGetValue(perm, out var users))
            {
                _exclusions[perm] = users = new();
            }
            users.TryAdd(userid, 0);
            users[userid] += 1;
        }

        private bool CanPurge(ulong userid, int timestamp) // Credits: misticos (used with permission)
        {
            var purge = PurgeSettings.Find(config, userid);

            if (!isLoaded || purge == null)
            {
                return false;
            }

            if (purge.NoPurge)
            {
                AddOrUpdateUserExclusion(purge.Permission, userid);
                return false;
            }

            if (!data.LastSeen.TryGetValue(userid, out var lastSeen))
            {
                UpdateLastSeen(userid, timestamp);
                return false;
            }

            return IsUserExcluded(userid) || purge.Lifetime > 0 && timestamp > lastSeen + purge.Lifetime;
        }

        private bool CanPurge(ulong userid, int timestamp, out SkippedReason reason)
        {
            var purge = PurgeSettings.Find(config, userid);

            if (!isLoaded || purge == null)
            {
                reason = SkippedReason.NoPermission;
                return false;
            }

            if (purge.NoPurge)
            {
                AddOrUpdateUserExclusion(purge.Permission, userid);
                reason = SkippedReason.NoPurge;
                return false;
            }

            if (!data.LastSeen.TryGetValue(userid, out var lastSeen))
            {
                UpdateLastSeen(userid, timestamp);
                reason = SkippedReason.NewlyAdded;
                return false;
            }

            if (IsUserExcluded(userid))
            {
                reason = SkippedReason.Excluded;
                return true;
            }

            if (purge.Lifetime > 0 && timestamp > lastSeen + purge.Lifetime)
            {
                reason = SkippedReason.None;
                return true;
            }

            reason = SkippedReason.CannotPurgeYet;
            return false;
        }

        private bool CanPurge(HashSet<ulong> owners, int timestamp)
        {
            if (IsPurgeEnabled) return true;
            if (owners.Count == 0) return false;
            if (owners.All(IsUserExcluded)) return false;
            foreach (var owner in owners)
            {
                if (!CanPurge(owner, timestamp))
                {
                    return false;
                }
            }
            return true;
        }

        public void UpdateLastSeen(ulong userid, int time)
        {
            if (data != null && data.LastSeen != null)
            {
                data.LastSeen[userid] = time;
            }
        }
        
        private void UpdateLastSeen()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player.IsConnected)
                {
                    UpdateLastSeen(player, Epoch.Current);
                }
            }

            SaveData();
        }

        public void UpdateLastSeen(BasePlayer player, int timestamp)
        {
            if (player.IsHuman())
            {
                UpdateLastSeen(player.userID, timestamp);
            }
        }

        private void CancelDamage(HitInfo info)
        {
            if (info != null && info.damageTypes != null)
            {
                info.damageTypes.Clear();
                info.DoHitEffects = false;
                info.DidHit = false;
            }
        }

        private void DestroyAll()
        {
            foreach (var abandonedBuilding in AbandonedBuildings.ToList())
            {
                if (abandonedBuilding == null) continue;
                abandonedBuilding.RemoveExpiration();
                abandonedBuilding.DestroyMe();
            }
        }

        private static bool InRange3D(Vector3 a, Vector3 b, float distance)
        {
            return (a - b).sqrMagnitude <= distance * distance;
        }

        private static bool InRange2D(Vector3 a, Vector3 b, float distance)
        {
            return (a.XZ2D() - b.XZ2D()).sqrMagnitude <= distance * distance;
        }

        private bool IsTooClose(Vector3 position)
        {
            if (config.Abandoned.TooClose)
            {
                foreach (var x in AbandonedBuildings)
                {
                    if (!x.isDestroyed && x.InRange3D(position))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool IsTooClose(IPlayer user, List<BaseEntity> entities, bool check, out bool territory)
        {
            foreach (var entity in entities)
            {
                if (RaidableBaseTerritory(entity.transform.position))
                {
                    Message(user, "Near Raidable Event");
                    territory = true;
                    return true;
                }

                if (check && IsTooClose(entity.transform.position))
                {
                    Message(user, "Near Event Base");
                    territory = false;
                    return true;
                }
            }

            territory = false;
            return false;
        }

        [HookMethod("EventTerritory")]
        public bool EventTerritory(Vector3 position, float x = 0f)
        {
            for (int i = 0; i < AbandonedBuildings.Count; i++)
            {
                AbandonedBuilding abandonedBuilding = AbandonedBuildings[i];
                if (InRange3D(abandonedBuilding.center, position, abandonedBuilding.radius + x))
                {
                    return true;
                }
            }
            return false;
        }

        [HookMethod("EventTerritoryAny")]
        public bool EventTerritoryAny(Vector3[] positions, float x = 0f)
        {
            for (int j = 0; j < AbandonedBuildings.Count; j++)
            {
                for (int k = 0; k < positions.Length; k++)
                {
                    AbandonedBuilding abandonedBuilding = AbandonedBuildings[j];
                    if (!abandonedBuilding.isDestroyed && InRange3D(abandonedBuilding.center, positions[k], abandonedBuilding.radius + x))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        [HookMethod("EventTerritoryAll")]
        public bool EventTerritoryAll(Vector3[] positions, float x = 0f)
        {
            for (int k = 0; k < positions.Length; k++)
            {
                bool isEventTerritory = false;
                for (int j = 0; j < AbandonedBuildings.Count; j++)
                {
                    AbandonedBuilding abandonedBuilding = AbandonedBuildings[j];
                    if (InRange3D(abandonedBuilding.center, positions[k], abandonedBuilding.radius + x))
                    {
                        isEventTerritory = true;
                        break;
                    }
                }
                if (!isEventTerritory)
                {
                    return false;
                }
            }
            return true;
        }

        [HookMethod("isAbandoned")]
        public bool IsAbandoned(BaseEntity entity) => entity.IsValid() && AbandonedReferences.ContainsKey(entity.net.ID.Value);

        private void StopManualConversion(IPlayer user)
        {
            if (user != null)
            {
                _conversions.RemoveAll(uc => uc.userid == user.Id);
            }
        }

        private void TryConvertCompound(IPlayer user, List<BaseEntity> entities, HashSet<ulong> owners, Vector3 position, ulong ownerid, bool anon, bool pvp, ConversionType type, Payment payment = null, float radius = 0f)
        {
            if (!_conversions.Exists(uc => uc.Exists(user, owners)))
            {
                IEnumerator co = TryConvertCompoundCo(user, entities, owners, position, ownerid, anon, false, pvp, type, payment, radius);

                if (CanDebug(user))
                {
                    user.Message("Conversion coroutine starting...");
                }

                _conversions.Add(new(user.Id, owners, ServerMgr.Instance.StartCoroutine(co)));
            }
        }

        private IEnumerator TryConvertCompoundCo(IPlayer user, List<BaseEntity> entities, HashSet<ulong> owners, Vector3 position, ulong ownerid, bool anon, bool delete, bool allowPVP, ConversionType type, Payment payment = null, float radius = 0f)
        {
            var others = new HashSet<ulong>();
            var foundations = new List<Vector3>();
            var isCustomRadius = user != null && !user.IsServer;
            var scanRadius = config.Abandoned.Dynamic ? config.Abandoned.MaxDynamicScanRadius : config.Abandoned.SphereRadius;

            if (isCustomRadius && scanRadius < config.Abandoned.MinCustomSphereRadius)
            {
                scanRadius = config.Abandoned.MinCustomSphereRadius;
            }

            if (scanRadius < 15f)
            {
                scanRadius = 15f;
            }

            if (CanDebug(user))
            {
                user.Message($"Starting compound search with {scanRadius} radius...");
            }

            yield return FindCompoundEntities(user, entities, owners, others, position, scanRadius, type);

            entities.RemoveAll(x => x.IsKilled());

            if (entities.Count == 0)
            {
                StopManualConversion(user);
                yield break;
            }

            if (IsTooClose(user, entities, !IsPurgeEnabled, out bool territory))
            {
                StopManualConversion(user);
                AddResults(territory ? "RB event is too close" : "Too close to AbandonedBase");
                yield break;
            }

            var containers = new List<StorageContainer>();
            var compound = new List<Vector3>();
            var floors = new List<Vector3>();
            var walls = new List<Vector3>();
            var loot = 0;
            var baseLoot = type == ConversionType.SAR ? config.Abandoned.BaseLootSAR : config.Abandoned.BaseLoot;
            var wallLimit = type == ConversionType.SAR ? config.Abandoned.WallLimitSAR : config.Abandoned.WallLimit;
            var foundationLimit = type == ConversionType.SAR ? config.Abandoned.FoundationLimitSAR : config.Abandoned.FoundationLimit;

            if (!AbandonedBuilding.AddRange(this, entities, containers, foundations, floors, compound, walls, user, ownerid, type, baseLoot, ref loot))
            {
                if (CanDebug(user) || (type == ConversionType.SAR || type == ConversionType.Attack) && user != null && !user.IsServer)
                {
                    Message(user, "Base Requirements", foundations.Count, foundationLimit, walls.Count, wallLimit);
                }

                StopManualConversion(user);

                if (delete)
                {
                    if (!config.Abandoned.DoNotDestroyPurge || !IsPurgeEnabled)
                    {
                        UndoLoop(entities);
                        AddResults("Limit not met, deleting base");
                    }
                }
                else if (user == null || user.IsServer)
                {
                    if (entities.Count > 0)
                    {
                        AddResults("Limit not met, skipping base");
                    }
                    yield break;
                }
                else if (baseLoot > 0 && loot < baseLoot)
                {
                    Message(user, "Base Loot Requirement", loot, baseLoot);
                }

                yield break;
            }

            if (CanDebug(user))
            {
                user.Message($"Conversion added... encapsulating event");
            }

            yield return CoroutineEx.waitForSeconds(0.25f);

            var bounds = new Bounds(compound[0], Vector3.zero);

            for (int i = 1; i < compound.Count; i++)
            {
                bounds.Encapsulate(compound[i]);
            }

            for (int i = 0; i < floors.Count; i++)
            {
                var floor = floors[i];
                if (!IsAboveFoundation(foundations, floor.XZ2D()))
                {
                    bounds.Encapsulate(floor);
                }
            }

            if (config.Abandoned.Dynamic && radius == 0f)
            {
                radius = Mathf.Min(config.Abandoned.MaxDynamicRadius, bounds.extents.Max() + Mathf.Max(9f, config.Abandoned.Padding));
            }

            if (radius == 0f)
            {
                if (isCustomRadius) radius = Mathf.Max(config.Abandoned.SphereRadius, config.Abandoned.MinCustomSphereRadius);
                else radius = config.Abandoned.SphereRadius;
            }

            if (isCustomRadius && radius < config.Abandoned.MinCustomSphereRadius)
            {
                radius = config.Abandoned.MinCustomSphereRadius;
            }

            if (CanDebug(user))
            {
                user.Message($"Conversion setup initializing...");
            }

            var center = bounds.center;

            if ((center - Vector3.zero).sqrMagnitude < 25f)
            {
                if (CanDebug(user))
                {
                    user.Message($"Base at 0,0,0, aborting... server is having a stroke");
                }
                yield break;
            }

            if (radius - 9f < bounds.size.y && Physics.Raycast(center + new Vector3(0f, 75f), Vector3.down, out var hit, 200f, Layers.Mask.World | Layers.Mask.Default | Layers.Mask.Terrain))
            {
                if (hit.point.y > TerrainMeta.HeightMap.GetHeight(bounds.center))
                {
                    radius = bounds.size.y + 9f;
                }
            }

            var go = new GameObject("AbandonedBase");
            var abandonedBuilding = go.AddComponent<AbandonedBuilding>();

            abandonedBuilding.go = go;
            abandonedBuilding.Setup(this, user, timer, compound, foundations, entities, containers, owners, others, center, payment, radius, anon, allowPVP, type);
            abandonedBuilding.eventType = EventType.Base;

            if (user != null && !anon && type == ConversionType.Automated && ulong.TryParse(user.Id, out var userid))
            {
                abandonedBuilding.raiderId = userid;
                abandonedBuilding.raiderName = user.Name;
            }

            if (CanDebug(user))
            {
                user.Message($"Conversion success, initializing setup and starting event.");
            }

            LogUserAction(user, abandonedBuilding, type == ConversionType.Automated ? "sab" : type == ConversionType.SAR ? "sar" : type == ConversionType.Attack ? "attack" : "none");
        }

        private bool IsAboveFoundation(List<Vector3> compound, Vector2 a)
        {
            return compound.Exists(b => (a - b.XZ2D()).sqrMagnitude <= 4f);
        }

        private bool CanDebug(IPlayer user)
        {
            if (user == null) return false;
            return DebugMode && user.IsAdmin || DebugMode && user.HasPermission("abandonedbases.admin");
        }

        private IEnumerator TryConvertEntityCo(IPlayer user, BaseEntity host, SimplePrivilege priv, HashSet<ulong> owners, bool anon, bool delete, bool allowPVP, ConversionType type, Payment payment = null, float radius = 0f)
        {
            if (priv.IsKilled()) 
            {
                if (CanDebug(user))
                {
                    user.Message("Conversion failed due to no privilege being found - this could have happened if the entity died naturally");
                }
                AddResults("TC is dead");
                yield break; 
            }

            if (host.IsKilled())
            {
                if (CanDebug(user))
                {
                    user.Message("Conversion failed due to the host entity being killed - this could have happened if the entity died naturally");
                }
                AddResults("Host entity is dead");
                yield break;
            }

            if (host.children.IsNullOrEmpty())
            {
                if (CanDebug(user))
                {
                    user.Message("Conversion failed due to the host entity children being killed - this could have happened if the entity died naturally");
                }
                AddResults("Entity children is null, this can be normal");
                yield break;
            }

            if (RaidableBaseTerritory(host.transform.position))
            {
                if (CanDebug(user) || type != ConversionType.Automated && type != ConversionType.Type4 && user != null && !user.IsServer)
                {
                    Message(user, "Near Raidable Event");
                }
                AddResults("RB event is too close");
                yield break;
            }

            string hostName = host switch
            {
                LegacyShelter => "Shelter",
                Tugboat => "Tugboat",
                _ => host.ShortPrefabName
            };

            List<BaseEntity> entities = new();
            HashSet<ulong> others = new();

            bool canChangeRadius = true;

            if (host is Tugboat) entities.AddRange(host.children.Where(entity =>
            {
                if (entity.IsKilled() || entity is BasePlayer)
                {
                    return false;
                }
                if (!entity.ShortPrefabName.Contains(hostName, StringComparison.CurrentCultureIgnoreCase))
                {
                    others.Add(entity.OwnerID);
                    return true;
                }
                return false;
            }));
            else
            {
                if (radius == 0f && host is LegacyShelter)
                {
                    radius = config.Abandoned.ShelterSphereRadius;
                    canChangeRadius = false;
                }

                using var serverEntities = FindEntitiesOfType<BaseEntity>(host.transform.position, 15f, Layers.Mask.Deployed | Layers.Mask.Player_Server);

                foreach (var entity in serverEntities)
                {
                    if (entity is BasePlayer player)
                    {
                        if (!player.userID.IsSteamId() || player.IsConnected)
                        {
                            continue;
                        }
                        if (config.KillInactiveSleepers)
                        {
                            entities.Add(player);
                            owners.Add(player.userID);
                        }
                    }
                    else
                    {
                        others.Add(entity.OwnerID);
                    }
                    if (!entities.Contains(entity))
                    {
                        entities.Add(entity);
                    }
                }
            }

            var containers = new List<StorageContainer>();
            var compound = new List<Vector3>();
            var loot = 0;

            var minLoot = host switch
            {
                LegacyShelter => type == ConversionType.SAR ? config.Abandoned.Shelters.LootSAR : config.Abandoned.Shelters.Loot,
                Tugboat => type == ConversionType.SAR ? config.Abandoned.Tugboats.LootSAR : config.Abandoned.Tugboats.Loot,
                _ => type == ConversionType.SAR ? config.Abandoned.BaseLootSAR : config.Abandoned.BaseLoot,
            };

            if (!AbandonedBuilding.AddRange(entities, containers, compound, minLoot, ref loot))
            {
                StopManualConversion(user);

                if (delete)
                {
                    if (!config.Abandoned.DoNotDestroyPurge || !IsPurgeEnabled)
                    {
                        if (host is Tugboat tugboat1) SinkTugboat(tugboat1);
                        else DeleteHost(host);
                    }
                }
                else if (user == null || user.IsServer)
                {
                    Unlock(user, host, priv, entities, $"{hostName} Unlocked", loot, minLoot);
                }
                else if (minLoot > 0 && loot < minLoot && user != null)
                {
                    Message(user, $"{hostName} Loot Requirement", loot, minLoot);
                }

                yield break;
            }

            yield return CoroutineEx.waitForSeconds(0.25f);

            if (radius == 0f || radius < config.Abandoned.MinCustomSphereRadius && canChangeRadius)
            {
                radius = Mathf.Max(config.Abandoned.SphereRadius, config.Abandoned.MinCustomSphereRadius, 25f);
            }

            var go = host.gameObject;
            var abandonedBuilding = go.AddComponent<AbandonedBuilding>();
            var foundations = new List<Vector3> { host.transform.position };
            
            abandonedBuilding.go = go;

            if (!entities.Contains(host))
            {
                entities.Add(host);
            }

            if (host is LegacyShelter shelter)
            {
                abandonedBuilding.anchor = shelter;
                abandonedBuilding.eventType = EventType.LegacyShelter;
            }
            else if (host is Tugboat tugboat2)
            {
                if (config.Abandoned.Tugboats.Engine)
                {
                    tugboat2.SetFlag(BaseEntity.Flags.Reserved1, false, true);
                }
                abandonedBuilding.tugboat = tugboat2;
                abandonedBuilding.anchor = tugboat2;
                abandonedBuilding.simplePriv = priv;
                abandonedBuilding.eventType = EventType.Tugboat;
            }
            else abandonedBuilding.eventType = EventType.Base;

            abandonedBuilding.Setup(this, user, timer, compound, foundations, entities, containers, owners, others, host.transform.position, payment, radius, anon, allowPVP, type);

            if (user != null && !anon && !(type == ConversionType.SAR || type == ConversionType.Attack) && ulong.TryParse(user.Id, out var userid))
            {
                abandonedBuilding.raiderId = userid;
                abandonedBuilding.raiderName = user.Name;
            }

            LogUserAction(user, abandonedBuilding, type == ConversionType.Automated ? "sab" : type == ConversionType.SAR ? "sar" : type == ConversionType.Attack ? "attack" : "none");
        }

        private void TryConvertEntity(IPlayer user, BaseEntity host, SimplePrivilege priv, HashSet<ulong> owners, bool anon, bool pvp, ConversionType type, Payment payment = null, float radius = 0f)
        {
            if (!_conversions.Exists(uc => uc.Exists(user, owners)))
            {
                IEnumerator co = TryConvertEntityCo(user, host, priv, owners, false, anon, pvp, type, payment, radius);

                _conversions.Add(new(user.Id, owners, ServerMgr.Instance.StartCoroutine(co)));
            }
        }

        private void DeleteHost(BaseEntity host)
        {
            AddResults($"Delete {(host?.ShortPrefabName ?? "LegacyShelter")}");
            host.SafelyKill();
        }

        private void SinkTugboat(Tugboat tugboat)
        {
            if (!tugboat.IsDying)
            {
                tugboat.health = 0;
                tugboat.OnDied(null);
                AddResults($"Sunk Tugboat");
            }
        }

        private void Unlock(IPlayer user, BaseEntity host, SimplePrivilege priv, List<BaseEntity> entities, string key, int loot, int minLoot)
        {
            if (!config.Abandoned.Shelters.Unlock && host is LegacyShelter)
            {
                return;
            }

            if (!config.Abandoned.Tugboats.Unlock && host is Tugboat)
            {
                return;
            }

            Message(user, key, loot, minLoot);

            entities.ToList().ForEach(ResetLock);
            priv.authorizedPlayers.Clear();
            priv.UpdateMaxAuthCapacity();
            priv.SendNetworkUpdate();
            AddResults(key);
        }

        private void LogUserAction(IPlayer user, AbandonedBuilding abandonedBuilding, string command)
        {
            AddResults("Converted");

            StopManualConversion(user);

            if (user == null || user.IsServer)
            {
                return;
            }

            abandonedBuilding.canReassign = config.Abandoned.AllowManualClaims;

            var player = user.ToPlayer();
            string text = GetMessage("Output Execute", null)
                .Replace("{displayName}", player.displayName)
                .Replace("{userID}", player.UserIDString)
                .Replace("{command}", command)
                .Replace("{position}", player.transform.position.ToString())
                .Replace("{grid}", abandonedBuilding.GetGrid().ToLower())
                .Replace("{time}", DateTime.Now.ToString());

            Puts(text);

            if (config.Discord.Start)
            {
                SendDiscordMessage(abandonedBuilding.center, text);
            }

            if (config.UseLogFile)
            {
                LogToFile("sar", text, this, false);
            }

            Message(user, "Start");

            abandonedBuilding.SetConversionCooldown(player.userID);
        }

        private void ResetLock(BaseEntity entity)
        {
            if (entity.IsKilled())
            {
                return;
            }
            if (entity is Door door)
            {
                BaseEntity slot = door.GetSlot(BaseEntity.Slot.Lock);
                ResetLock(slot);
                return;
            }
            if (entity is CodeLock codeLock)
            {
                ResetCodeLock(codeLock);
            }
            else if (entity is KeyLock keyLock)
            {
                ResetKeyLock(keyLock);
            }
        }

        private void ResetCodeLock(CodeLock codeLock)
        {
            codeLock.SetFlag(BaseEntity.Flags.Locked, false);
            codeLock.ClearCodeEntryBlocked();
            codeLock.whitelistPlayers.Clear();
            codeLock.guestPlayers.Clear();
            codeLock.hasGuestCode = false;
            codeLock.hasCode = false;
            codeLock.guestCode = "";
            codeLock.code = "";
            codeLock.OwnerID = 0;
            codeLock.SendNetworkUpdate();
        }

        private void ResetKeyLock(KeyLock keyLock)
        {
            keyLock.SetFlag(BaseEntity.Flags.Locked, false);
            keyLock.firstKeyCreated = false;
            keyLock.keyCode = 0;
            keyLock.OwnerID = 0;
            keyLock.SendNetworkUpdate();
        }

        private bool RaidableBaseTerritory(Vector3 position) => RaidableBases != null && Convert.ToBoolean(RaidableBases?.Call("EventTerritory", position, 100f));

        private IEnumerator FindCompoundEntities(IPlayer user, List<BaseEntity> source, HashSet<ulong> owners, HashSet<ulong> others, Vector3 position, float radius, ConversionType type)
        {
            using var serverEntities = FindEntitiesOfType<BaseNetworkable>(position, radius);
            HashSet<ulong> builders = new();
            int timestamp = Epoch.Current;
            int checks = 0;

            foreach (var serverEntity in serverEntities)
            {
                if (++checks > 500)
                {
                    checks = 0;
                    yield return null;
                }

                if (source.Count == 0)
                {
                    if (CanDebug(user)) user.Message("Building does not share same building owners");

                    yield break;
                }

                if (serverEntity.IsKilled())
                {
                    continue;
                }
                
                switch (serverEntity)
                {
                    case DecayEntity decayEntity:
                        HandleDecayEntity(user, source, owners, others, decayEntity, builders, type, timestamp);
                        break;

                    case BasePlayer player:
                        HandleBasePlayer(source, owners, others, player);
                        break;

                    case BaseCombatEntity baseCombatEntity:
                        HandleBaseCombatEntity(user, source, owners, others, baseCombatEntity);
                        break;
                }
            }
        }

        private PooledList<BaseEntity> GetDecayEntities(BuildingManager.Building building)
        {
            var entities = DisposableList<BaseEntity>();
            if (building != null && building.decayEntities != null)
            {
                entities.AddRange(building.decayEntities);
            }
            return entities;
        }

        private void HandleDecayEntity(IPlayer user, List<BaseEntity> source, HashSet<ulong> owners, HashSet<ulong> others, DecayEntity decayEntity, HashSet<ulong> builders, ConversionType type, int timestamp)
        {
            others.Add(decayEntity.OwnerID);

            if (source.Contains(decayEntity) || (decayEntity.OwnerID.IsSteamId() && !owners.Contains(decayEntity.OwnerID)))
            {
                return;
            }

            var building = decayEntity.GetBuilding();

            if (decayEntity is BuildingPrivlidge priv && priv.AnyAuthed())
            {
                builders.Clear();

                using var entities = GetDecayEntities(building);
                if (entities != null)
                {
                    foreach (var baseEntity in entities)
                    {
                        if (baseEntity.OwnerID.IsSteamId())
                        {
                            builders.Add(baseEntity.OwnerID);
                        }
                    }
                }

                foreach (var id in priv.authorizedPlayers)
                {
                    if (id.IsSteamId())
                    {
                        builders.Add(id);
                    }
                }

                foreach (var id in priv.authorizedPlayers)
                {
                    if (!id.IsSteamId() || owners.Contains(id))
                    {
                        continue;
                    }

                    if (CanPurge(id, timestamp, out SkippedReason cannotPurgeReason))
                    {
                        owners.Add(id);
                        continue;
                    }

                    if (type == ConversionType.SAR || type == ConversionType.Attack)
                    {
                        if (CanDebug(user))
                        {
                            user.Message($"User '{id}' is unauthorized and too close to this compound");
                        }

                        if (entities != null)
                        {
                            source.RemoveAll(x => x == null || entities.Contains(x));
                        }

                        continue;
                    }

                    if (CanDebug(user))
                    {
                        user.Message($"Cannot purge authorized user {id}");
                    }

                    var intersect = builders.Intersect(owners).ToList();

                    if (intersect.Count > 0)
                    {
                        HandleOwnershipIntersection(user, priv, intersect, type == ConversionType.SAR || type == ConversionType.Attack);
                        source.Clear();
                        AddResults($"cannot purge {id}: {cannotPurgeReason}");
                    }
                    else
                    {
                        source.RemoveAll(x => x == null || builders.Contains(x.OwnerID));
                    }
                    
                    return;
                }
            }

            if (building == null || !building.HasDecayEntities() || SameOwners(user, building, owners))
            {
                source.Add(decayEntity);
            }
        }

        private void HandleBasePlayer(List<BaseEntity> source, HashSet<ulong> owners, HashSet<ulong> others, BasePlayer player)
        {
            if (config.KillInactiveSleepers && owners.Contains(player.userID) && !player.IsConnected)
            {
                if (!source.Contains(player))
                {
                    source.Add(player);
                }
                others.Add(player.userID);
            }
        }

        private void HandleBaseCombatEntity(IPlayer user, List<BaseEntity> source, HashSet<ulong> owners, HashSet<ulong> others, BaseCombatEntity baseCombatEntity)
        {
            if (source.Contains(baseCombatEntity) || IsServerEntity(baseCombatEntity) || IsEntityNotAssociated(owners, baseCombatEntity))
            {
                return;
            }

            var building = baseCombatEntity.GetBuildingPrivilege(baseCombatEntity.WorldSpaceBounds())?.GetBuilding();

            if (building == null || SameOwners(user, building, owners))
            {
                source.Add(baseCombatEntity);
            }

            others.Add(baseCombatEntity.OwnerID);
        }

        private void HandleOwnershipIntersection(IPlayer user, BuildingPrivlidge priv, List<ulong> intersect, bool canMessage)
        {
            if (CanDebug(user))
            {
                string users = string.Join(", ", intersect);
                user.Message($"Conversion failed -- these players do not meet abandoned status yet: {users}");
                user.ToPlayer()?.SendConsoleCommand("ddraw.text", 30f, Color.red, priv.transform.position, $"<size=18>{users}</size>");
            }
        }

        private bool IsEntityNotAssociated(HashSet<ulong> owners, BaseCombatEntity? entity)
        {
            return entity.OwnerID.IsSteamId() && !owners.Contains(entity.OwnerID);
        }

        private bool IsServerEntity(BaseCombatEntity entity)
        {
            return !entity.OwnerID.IsSteamId() && !IsPlayerEntity(entity);
        }

        public void UndoLoop(ListHashSet<DecayEntity> decayEntities, int limit = 10, object[] hookObjects = null)
        {
            List<BaseEntity> entities = new(decayEntities);

            UndoLoop(entities, limit, hookObjects);
        }

        public void UndoLoop(List<BaseEntity> entities, int limit = 10, object[] hookObjects = null)
        {
            if (entities != null && entities.Count > 0)
            {
                ServerMgr.Instance.StartCoroutine(UndoLoopCo(entities, limit, hookObjects));
            }
        }

        private bool KeepVehicle(BaseEntity entity)
        {
            if (config.Abandoned.DespawnMounts) return false;
            if (entity is Tugboat) return false;
            if (entity is BaseMountable) return true;
            return entity.GetParentEntity() is BaseMountable;
        }

        private IEnumerator UndoLoopCo(List<BaseEntity> entities, int limit, object[] hookObjects)
        {
            entities.RemoveAll(e => e.IsKilled() || e.HasParent() || e.ShortPrefabName == "item_drop_backpack" || KeepVehicle(e));

            entities.Sort((x, y) => (x is BuildingBlock).CompareTo(y is BuildingBlock));

            WaitForSeconds instruction = CoroutineEx.waitForSeconds(0.0625f);

            int threshold = limit;

            int checks = 0;

            if (hookObjects != null)
            {
                Interface.CallHook("OnAbandonedBaseDespawn", hookObjects);
            }
            else
            {
                Interface.CallHook("OnAbandonedBaseDespawn", entities);
            }

            while (entities.Count > 0)
            {
                BaseEntity entity = entities[0];

                entities.RemoveAt(0);

                if (entity.IsKilled())
                {
                    continue;
                }

                if (entity is BasePlayer player && player.IsConnected)
                {
                    continue;
                }

                if (entity is IOEntity io)
                {
                    try { io.ClearConnections(); } catch { }

                    if (entity is SamSite ss)
                    {
                        ss.staticRespawn = false;
                    }
                }

                if (entity is IItemContainerEntity ice && ice != null && ice.inventory != null)
                {
                    ice.inventory.Clear();
                    ItemManager.DoRemoves();
                }

                entity.SafelyKill();

                if (++checks >= threshold)
                {
                    checks = 0;
                    threshold = Performance.report.frameRate < 15 ? 1 : limit;
                    yield return instruction;
                }
            }
        }

        private HashSet<string> _deployables = new();
        private Dictionary<uint, ItemDefinition> _deployablesU = new();
        private Dictionary<ItemDefinition, uint> _deployablesD = new();

        private void InitializeDefinitions()
        {
            foreach (var def in ItemManager.GetItemDefinitions())
            {
                if (def.TryGetComponent<ItemModDeployable>(out var imd))
                {
                    _deployables.Add(imd.entityPrefab.resourcePath);
                    _deployablesU[imd.entityPrefab.resourceID] = def;
                    _deployablesD[def] = imd.entityPrefab.resourceID;
                }
            }
        }

        private bool IsDeployableEntity(BaseEntity entity)
        {
            return _deployables.Contains(entity.PrefabName);
        }

        private bool IsPlayerEntity(BaseEntity entity)
        {
            return entity.PrefabName.Contains("building") || entity is BaseMountable || IsDeployableEntity(entity);
        }

        private bool SameOwners(IPlayer user, BuildingManager.Building building, HashSet<ulong> owners)
        {
            if (IsPurgeEnabled)
            {
                return true;
            }

            if (building.HasBuildingPrivileges())
            {
                foreach (var priv in building.buildingPrivileges)
                {
                    if (priv.authorizedPlayers.Count != 0)
                    {
                        bool found = false;
                        foreach (var id in priv.authorizedPlayers)
                        {
                            if (owners.Contains(id))
                            {
                                found = true;
                                break;
                            }
                        }
                        if (!found)
                        {
                            return false;
                        }
                    }
                }
            }

            if (building.HasDecayEntities())
            {
                foreach (var entity in building.decayEntities)
                {
                    if (!(entity.OwnerID == 0uL || owners.Contains(entity.OwnerID)))
                    {
                        return false;
                    }
                }
            }

            return true;
        }


        private static PooledList<T> FindEntitiesOfType<T>(Vector3 a, float n, int m = -1) where T : BaseNetworkable
        {
            PooledList<T> entities = DisposableList<T>();
            Vis.Entities(a, n, entities, m, QueryTriggerInteraction.Ignore);
            entities.RemoveAll(x => x == null || x.IsDestroyed || x is TravellingVendor || x is BradleyAPC || x is PatrolHelicopter || x is ModularCar mc && !mc.spawnSettings.useSpawnSettings);
            return entities;
        }

        private bool IsLootingWeapon(HitInfo info)
        {
            if (info == null || info.damageTypes == null)
            {
                return false;
            }

            return info.damageTypes.Has(DamageType.Explosion) || info.damageTypes.Has(DamageType.Heat) || info.damageTypes.IsMeleeType() || info.WeaponPrefab is TimedExplosive;
        }
        
        private string FormatTime(string userid, double seconds) // Credits: MoNaH
        {
            var dd = string.Join("\\", $"<color=#FFA500>*</color>".ToCharArray()).Replace("\\*", "dd");
            var hh = string.Join("\\", $"<color=#FFA500>*</color>".ToCharArray()).Replace("\\*", "hh");
            var mm = string.Join("\\", $"<color=#FFA500>*</color>".ToCharArray()).Replace("\\*", "mm");
            var ss = string.Join("\\", $"<color=#FFA500>*</color>".ToCharArray()).Replace("\\*", "ss");
            var ddt = string.Join("\\", GetMessage("Days", userid).ToCharArray());
            var hht = string.Join("\\", GetMessage("Hours", userid).ToCharArray());
            var mmt = string.Join("\\", GetMessage("Minutes", userid).ToCharArray());
            var sst = string.Join("\\", GetMessage("Seconds", userid).ToCharArray());
            var tFormat = string.Empty;

            if (seconds >= 86400)
            {
                tFormat = "\\" + dd + "\\ \\" + ddt + "\\ \\" + hh + "\\ \\" + hht + "\\ \\" + mm + "\\ \\" + mmt + "\\ \\" + ss + "\\ \\" + sst;
            }
            else if (seconds >= 3600)
            {
                tFormat = "\\" + hh + "\\ \\" + hht + "\\ \\" + mm + "\\ \\" + mmt + "\\ \\" + ss + "\\ \\" + sst;
            }
            else if (seconds >= 60)
            {
                tFormat = "\\" + mm + "\\ \\" + mmt + "\\ \\" + ss + "\\ \\" + sst;
            }
            else if (seconds >= 0)
            {
                tFormat = "\\" + ss + "\\ \\" + sst;
            }

            return TimeSpan.FromSeconds(seconds).ToString(@"" + tFormat);
        }

        private float GetPVPDelay(ulong userid, out DelaySettings ds)
        {
            return PvpDelay.TryGetValue(userid, out ds) && ds != null && ds.time > Time.time ? ds.time : 0f;
        }

        [HookMethod("GetPVPDelay")]
        public float GetPVPDelay(ulong userid)
        {
            return GetPVPDelay(userid, out var ds) > 0f ? ds.time : 0f;
        }

        private float GetMaxPVPDelay()
        {
            return config.Abandoned.PVPDelay;
        }

        [HookMethod("HasPVPDelay")]
        public bool HasPVPDelay(ulong userid)
        {
            return GetPVPDelay(userid, out _) > 0f;
        }

        [HookMethod("PlayerInEvent")]
        public bool PlayerInEvent(BasePlayer player)
        {
            return !player.IsKilled() && (HasPVPDelay(player.userID) || EventTerritory(player.transform.position));
        }

        private void SaveData()
        {
            var now = DateTime.Now;

            data.LastRunTime = now;

            try { data.CooldownBetweenEvents.RemoveAll((id, date) => date < now); } catch { }
            try { data.CooldownBetweenCancel.RemoveAll((id, date) => date < now); } catch { }
            try { data.CooldownBetweenConversion.RemoveAll((id, date) => date < now); } catch { }

            Interface.Oxide.DataFileSystem.WriteObject(Name, data);
        }

        private void LoadData()
        {
            try { data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name); } catch (Exception ex) { Puts(ex.ToString()); }
            if (data == null)
            {
                Puts("Data is NULL (corrupted?) and has been reset.");
                data = new();
                data.protocol = Protocol.save;
            }
            if (data.LastSeen == null)
            {
                Puts("LastSeen is NULL (corrupted?) and has been reset.");
                data.LastSeen = new();
            }
            if (data.LastRunTime != DateTime.MinValue && DateTime.Now.Subtract(data.LastRunTime).TotalHours >= 24)
            {
                Puts("Data wiped due to plugin not being loaded for {0} day(s).", DateTime.Now.Subtract(data.LastRunTime).Days);
                data = new();
            }
            if (data.protocol != Protocol.save)
            {
                if (data.protocol != 0)
                {
                    Puts("Data wiped; new Rust protocol {0}/{1}", data.protocol, Protocol.save);
                }
                data = new();
            }
            data.protocol = Protocol.save;
            SaveData();
        }

        private void CheckData()
        {
            if (newSave)
            {
                Puts("New save detected; wiped data.");
                data = new();
                data.protocol = Rust.Protocol.save;
                SaveData();
            }
            else if (IsEmptyMap())
            {
                Puts("New save or map detected; wiped data.");
                data = new();
                data.protocol = Rust.Protocol.save;
                SaveData();
            }
        }

        private bool IsEmptyMap()
        {
            foreach (var b in BuildingManager.server.buildingDictionary)
            {
                if (b.Value.HasDecayEntities() && b.Value.decayEntities.ToList().Exists(de => de != null && de.OwnerID.IsSteamId()))
                {
                    return false;
                }
            }
            return true;
        }

        private Payment TrySetPayment(BasePlayer player, bool cancel)
        {
            if (player.HasPermission("abandonedbases.convert.free"))
            {
                return new(player);
            }

            if (cancel ? config.Abandoned.EconomicsCancel > 0 : config.Abandoned.Economics > 0)
            {
                return TrySetEconomicsPayment(player, cancel);
            }

            if (cancel ? config.Abandoned.ServerRewardsCancel > 0 : config.Abandoned.ServerRewards > 0)
            {
                return TrySetServerRewardsPayment(player, cancel);
            }

            if (cancel ? config.Abandoned.CustomCancel.IsValid() : config.Abandoned.Custom.IsValid())
            {
                return TrySetCustomCostPayment(player, cancel);
            }

            return new(player);
        }

        private Payment TrySetCustomCostPayment(BasePlayer player, bool cancel)
        {
            var options = cancel ? config.Abandoned.CustomCancel : config.Abandoned.Custom;

            foreach (var option in options)
            {
                using var slots = DisposableList<Item>();
                player.inventory.FindItemsByItemID(slots, option.Definition.itemid);
                int amount = 0;

                foreach (var slot in slots)
                {
                    if (option.Skin != 0 && slot.skin != option.Skin)
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(option.Name) && slot.name != option.Name)
                    {
                        continue;
                    }

                    amount += slot.amount;

                    if (amount >= option.Amount)
                    {
                        break;
                    }
                }

                if (amount < option.Amount)
                {
                    string name = string.IsNullOrEmpty(option.Name) ? option.Shortname : option.Name;
                    Message(player, cancel ? "CustomCostFailedCancel" : "CustomCostFailed", option.Amount, name);
                    return null;
                }
            }

            return new(player, 0, options);
        }

        private Payment TrySetServerRewardsPayment(BasePlayer player, bool cancel)
        {
            var cost = cancel ? config.Abandoned.ServerRewardsCancel : config.Abandoned.ServerRewards;

            if (cost > 0 && ServerRewards.CanCall())
            {
                var points = Convert.ToInt32(ServerRewards?.Call("CheckPoints", (ulong)player.userID));

                if (points > 0 && points - cost >= 0)
                {
                    return new(player, cost);
                }
                else
                {
                    Message(player, cancel ? "ServerRewardPointsFailedCancel" : "ServerRewardPointsFailed", cost);
                    return null;
                }
            }

            return new(player, cost);
        }

        private Payment TrySetEconomicsPayment(BasePlayer player, bool cancel)
        {
            var cost = cancel ? config.Abandoned.EconomicsCancel : config.Abandoned.Economics;

            if (cost > 0 && Economics.CanCall())
            {
                var points = Convert.ToDouble(Economics?.Call("Balance", (ulong)player.userID));

                if (points > 0 && points - cost >= 0)
                {
                    return new(player, cost);
                }
                else
                {
                    Message(player, cancel ? "EconomicsWithdrawFailedCancel" : "EconomicsWithdrawFailed", cost);
                    return null;
                }
            }

            if (cost > 0 && IQEconomic.CanCall())
            {
                var points = Convert.ToDouble(IQEconomic?.Call("API_GET_BALANCE", (ulong)player.userID));

                if (points > 0 && points - cost >= 0)
                {
                    return new(player, cost);
                }
                else
                {
                    Message(player, cancel ? "EconomicsWithdrawFailedCancel" : "EconomicsWithdrawFailed", cost);
                    return null;
                }
            }

            return new(player, cost);
        }

        private bool IsHogging(BasePlayer player, AbandonedBuilding abandonedBuilding, bool reply, string key)
        {
            if (!config.Abandoned.PreventHogging || IsPurgeEnabled && config.Abandoned.IgnorePurgeHogging)
            {
                return false;
            }
            foreach (var otherBuilding in AbandonedBuildings)
            {
                if (abandonedBuilding != null && otherBuilding.center == abandonedBuilding.center)
                {
                    continue;
                }
                if (!IsHogging(otherBuilding, player))
                {
                    continue;
                }
                if (reply)
                {
                    otherBuilding.TryMessage(player, 5f, key, otherBuilding.GetGrid());
                }
                return true;
            }
            return false;
        }

        private bool IsHogging(AbandonedBuilding otherBuilding, BasePlayer player)
        {
            if (otherBuilding.IsEventCompleted) return false;
            if (otherBuilding.intruders.ContainsKey(player.userID)) return true;
            if (otherBuilding.IsParticipant(player.userID)) return true;
            if (!config.Abandoned.PreventAllyHogging) return false;
            if (otherBuilding.currentId == otherBuilding.previousId) return false;
            if (otherBuilding.canReassign) return false;
            if (IsAlly(player.userID, otherBuilding.currentId)) return true;
            foreach (var x in otherBuilding.GetIntruders())
            {
                if (IsAlly(player.userID, x.userID))
                {
                    return true;
                }
            }
            return false;
        }

        public bool HasEventCooldown(BasePlayer player, bool reply = true)
        {
            return HasCooldown(player, player.userID, "abandonedbases.noeventcooldown", data.CooldownBetweenEvents, reply, "Event Cooldown");
        }

        private bool HasEventCooldown(BasePlayer player, AbandonedBuilding abandonedBuilding, bool reply)
        {
            ulong userid = player.userID;
            if (reply) reply = !abandonedBuilding.Messages.Contains(userid);
            if (IsHogging(player, abandonedBuilding, reply, "HoggingFinishYourRaid"))
            {
                if (reply)
                {
                    abandonedBuilding.Messages.Add(userid);
                    abandonedBuilding.timer.Once(10f, () => abandonedBuilding.Messages.Remove(userid));
                }
                return true;
            }
            if (HasEventCooldown(player, true))
            {
                if (reply)
                {
                    abandonedBuilding.Messages.Add(userid);
                    abandonedBuilding.timer.Once(10f, () => abandonedBuilding.Messages.Remove(userid));
                }
                return true;
            }
            return false;
        }

        private bool HasCooldown(BasePlayer player, ulong userid, string bypass, Dictionary<ulong, DateTime> cooldowns, bool reply = true, string key = "Cooldown")
        {
            if (!userid.HasPermission(bypass) && cooldowns.TryGetValue(userid, out var date))
            {
                double cooldown = date.Subtract(DateTime.Now).TotalSeconds;

                if (cooldown > 0)
                {
                    if (reply)
                    {
                        Message(player, key, FormatTime(player.UserIDString, cooldown));
                    }
                    return true;
                }
            }
            cooldowns.Remove(userid);
            return false;
        }

        private void SetupZoneManager(bool message)
        {
            blockedZones.Clear();

            if (config.AllowedZones.Count > 0)
            {
                timer.Once(30f, () => SetupZoneManager(false));
            }

            if (ZoneManager == null || !ZoneManager.IsLoaded)
            {
                return;
            }

            var zoneIds = ZoneManager?.Call("GetZoneIDs") as string[];

            if (zoneIds.IsNullOrEmpty())
            {
                return;
            }

            foreach (string zoneId in zoneIds)
            {
                var zoneLoc = ZoneManager.Call("GetZoneLocation", zoneId);

                if (zoneLoc is not Vector3 origin || origin == default)
                {
                    continue;
                }

                var zoneName = Convert.ToString(ZoneManager.Call("GetZoneName", zoneId));

                if (config.AllowedZones.Exists(zone => zone == "*" || zone == zoneId || !string.IsNullOrEmpty(zoneName) && !string.IsNullOrEmpty(zone) && zoneName.Contains(zone, CompareOptions.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var radius = ZoneManager.Call("GetZoneRadius", zoneId);
                var size = ZoneManager.Call("GetZoneSize", zoneId);

                blockedZones.Add(new(origin, radius, size));
            }

            if (message && blockedZones.Count > 0)
            {
                Puts(GetMessage("BlockedZones", null, blockedZones.Count));
            }
        }

        private List<ZoneInfo> blockedZones = new();

        public class ZoneInfo
        {
            internal Vector3 origin;
            internal Vector3 extents;
            internal float radius;

            public ZoneInfo(Vector3 origin, object radius, object size)
            {
                this.origin = origin;

                if (radius is float r)
                {
                    this.radius = r;
                }

                if (size is Vector3 sz && !size.Equals(Vector3.zero))
                {
                    extents = sz * 0.5f;
                }
            }

            public bool IsPositionInZone(Vector3 point)
            {
                if (extents != Vector3.zero)
                {
                    Vector3 v = Quaternion.Inverse(Quaternion.identity) * (point - origin);

                    return v.x <= extents.x && v.x > -extents.x && v.y <= extents.y && v.y > -extents.y && v.z <= extents.z && v.z > -extents.z;
                }
                return InRange2D(origin, point, radius);
            }

            private bool InRange2D(Vector3 a, Vector3 b, float distance)
            {
                return (new Vector3(a.x, 0f, a.z) - new Vector3(b.x, 0f, b.z)).sqrMagnitude <= distance * distance;
            }
        }

        public static PooledList<T> DisposableList<T>() => Pool.Get<PooledList<T>>();

        #endregion Helpers

        #region Commands

        private bool CanUseCancel(IPlayer user, AbandonedBuilding abandonedBuilding)
        {
            if (user.IsServer) return false;
            if (user.HasPermission("abandonedbases.convert.cancel")) return true;
            return !abandonedBuilding.AutomatedEvent && user.HasPermission("abandonedbases.convert.cancel.manual");
        }

        private void CommandCancel(IPlayer user, string command, string[] args)
        {
            var player = user.ToPlayer();

            if (HasCooldown(player, player.userID, "abandonedbases.convert.cancel.nocooldown", data.CooldownBetweenCancel))
            {
                return;
            }

            if (!GetAbandonedBuilding(player.transform.position, out var abandonedBuilding))
            {
                Message(user, "Nothing");
                return;
            }

            if (!CanUseCancel(user, abandonedBuilding))
            {
                Message(user, "No Permission");
                return;
            }

            if (abandonedBuilding.IsOwnerLocked && !abandonedBuilding.IsOwner(player))
            {
                abandonedBuilding.TryEjectFromLockedBase(player);
                Message(user, "Not An Ally");
                return;
            }

            if (config.Abandoned.RequireEventFinished && !AbandonedBuilding.CanBypass(player) && !abandonedBuilding.IsCompleted())
            {
                Message(user, config.Abandoned.OnlyCupboardsAreRequired ? "MustFinishCupboards" : "MustFinish");
                return;
            }

            if (config.Abandoned.CancelCooldown > 0 && !AbandonedBuilding.CanBypass(player) && abandonedBuilding.HasCancelCooldown(player))
            {
                return;
            }

            Payment payment = TrySetPayment(player, true);

            if (payment == null)
            {
                return;
            }

            abandonedBuilding.CanShowDiscordEnd = false;
            abandonedBuilding.RemoveExpiration();
            abandonedBuilding.SetCancelCooldown(player.userID);
            abandonedBuilding.CompleteCancelPayment(payment);
            abandonedBuilding.KillCollider();
            abandonedBuilding.DestroyMe();
            Message(user, "Cancelled");

            if (config.Discord.Cancel)
            {
                string text = GetMessage("Output Execute", null)
                .Replace("{displayName}", player.displayName)
                .Replace("{userID}", player.UserIDString)
                .Replace("{command}", "sar claim")
                .Replace("{position}", player.transform.position.ToString())
                .Replace("{grid}", abandonedBuilding.GetGrid().ToLower())
                .Replace("{time}", DateTime.Now.ToString());

                SendDiscordMessage(abandonedBuilding.center, text);
            }
        }

        private void CommandReset(IPlayer user, string command, string[] args)
        {
            if (!user.IsAdmin && !user.HasPermission("abandonedbases.admin"))
            {
                Message(user, "No Permission");
                return;
            }

            if (args.Length < 2)
            {
                Message(user, "Reset", "Invalid syntax");
                return;
            }

            if (ulong.TryParse(args[1], out var userid)) 
            {
                Reset(userid);
                return;
            }

            var target = BasePlayer.FindAwakeOrSleeping(args[1]);

            if (target == null)
            {
                Message(user, "Reset", "Not found");
                return;
            }

            Reset(target.userID);

            void Reset(ulong userid)
            {
                data.CooldownBetweenCancel.Remove(userid);
                data.CooldownBetweenEvents.Remove(userid);
                data.CooldownBetweenConversion.Remove(userid);
                Message(user, "Reset", userid);
            }
        }

        private void CommandUnlock(IPlayer user, string command, string[] args)
        {
            if (!user.HasPermission("abandonedbases.admin") || user.IsServer)
            {
                Message(user, "No Permission");
                return;
            }

            var player = user.Object as BasePlayer;
            
            if (player == null || !GetAbandonedBuilding(player.transform.position, out var abandonedBuilding))
            {
                Message(user, "Nothing");
                return;
            }

            abandonedBuilding.IsAllowed.Clear();
            abandonedBuilding.IsOwnerLocked = false;
            abandonedBuilding.canReassign = true;
            abandonedBuilding.raiderName = null;
            abandonedBuilding.raiderId = 0uL;
            abandonedBuilding.currentId = abandonedBuilding.previousId;
            abandonedBuilding.currentName = abandonedBuilding.previousName;
            abandonedBuilding.Invoke(abandonedBuilding.UpdateMarkers, 0f);

            var position = abandonedBuilding.center;
            var grid = abandonedBuilding.GetGrid();
            var text = GetMessage("Output Unlock", null)
                .Replace("{displayName}", player.displayName)
                .Replace("{userID}", player.UserIDString)
                .Replace("{position}", position.ToString())
                .Replace("{grid}", grid)
                .Replace("{previousName}", abandonedBuilding.previousName)
                .Replace("{previousId}", abandonedBuilding.previousId.ToString());

            if (config.Discord.Unlock)
            {
                SendDiscordMessage(abandonedBuilding.center, text);
            }

            if (config.UseLogFile)
            {
                LogToFile("sar", text, this, false);
            }

            Puts(text);
            Message(user, "Unlocked");
        }

        private void CommandCooldown(IPlayer user, string command, string[] args)
        {
            bool hasCooldown = false;
            var player = user.ToPlayer();
            ulong userid = player.userID;
            if (args.Length > 0 && player.IsAdmin)
            {
                if (ulong.TryParse(args[0], out var result)) userid = result;
                else if (BasePlayer.FindAwakeOrSleeping(args[0]) is BasePlayer target) userid = target.userID;
                if (userid != player.userID) Message(player, $"{userid}:");
                if (args.Length > 1 && float.TryParse(args[1], out float time))
                {
                    data.CooldownBetweenEvents[userid] = DateTime.Now.AddSeconds(time);
                    data.CooldownBetweenCancel[userid] = DateTime.Now.AddSeconds(time);
                    data.CooldownBetweenConversion[userid] = DateTime.Now.AddSeconds(time);
                    Message(player, $"All cooldowns for {userid} set to {time} seconds.");
                    return;
                }
            }
            hasCooldown |= HasCooldown(player, userid, "abandonedbases.nope", data.CooldownBetweenEvents, true, "Event Cooldown");
            hasCooldown |= HasCooldown(player, userid, "abandonedbases.nope", data.CooldownBetweenCancel, true);
            hasCooldown |= HasCooldown(player, userid, "abandonedbases.nope", data.CooldownBetweenConversion, true);
            if (!hasCooldown) Message(player, "You have no cooldowns.");
        }

        private void CommandClaim(IPlayer user, string command, string[] args)
        {
            if (!user.HasPermission("abandonedbases.convert") || !user.HasPermission("abandonedbases.convert.claim") || user.IsServer)
            {
                Message(user, "No Permission");
                return;
            }

            var player = user.ToPlayer();

            if (config.Abandoned.Shelters.CanClaim(player, out var shelter1))
            {
                CommandClaimShelter(user, player, shelter1);
                return;
            }

            if (!GetAbandonedBuilding(player.transform.position, out var abandonedBuilding))
            {
                Message(user, "Nothing");
                return;
            }

            if (!abandonedBuilding.IsParticipant(player.userID) && HasEventCooldown(player))
            {
                return;
            }

            if (abandonedBuilding.IsOwnerLocked && !abandonedBuilding.IsOwner(player))
            {
                abandonedBuilding.TryEjectFromLockedBase(player);
                Message(user, "Not An Ally");
                return;
            }

            if (config.Abandoned.RequireEventFinished && !AbandonedBuilding.CanBypass(player) && !abandonedBuilding.IsCompleted())
            {
                Message(user, config.Abandoned.OnlyCupboardsAreRequired ? "MustFinishCupboards" : "MustFinish");
                return;
            }

            if (abandonedBuilding.eventType == EventType.LegacyShelter)
            {
                if (!config.Abandoned.Shelters.Claim)
                {
                    Message(player, "Disabled");
                    return;
                }
                if (abandonedBuilding.anchor is LegacyShelter shelter2 && !config.Abandoned.Shelters.IsAuthed(player, shelter2))
                {
                    Message(player, "Authorize");
                    return;
                }
            }
            else if (config.Abandoned.ClaimRequiresCupboardAccess)
            {
                if (player.GetParentEntity() is Tugboat tugboat)
                {
                    if (GetVehiclePrivilege(tugboat.children) is not SimplePrivilege priv || !priv.AnyAuthed() || !priv.IsAuthed(player))
                    {
                        Message(player, "Authorize");
                        return;
                    }
                }
                else if (player.GetBuildingPrivilege() is not BuildingPrivlidge priv || !priv.AnyAuthed() || !priv.IsAuthed(player))
                {
                    Message(player, "Authorize");
                    return;
                }
            }

            CommandClaim(user, player, abandonedBuilding);
        }

        private void CommandClaim(IPlayer user, BasePlayer player, AbandonedBuilding abandonedBuilding)
        {
            abandonedBuilding.entities.RemoveAll(entity =>
            {
                if (entity.IsKilled() || !entity.IsValid())
                {
                    return true;
                }
                if (abandonedBuilding.EntityOwners.TryGetValue(entity.net.ID.Value, out var eo))
                {
                    entity.OwnerID = eo.OwnerID;
                    Interface.CallHook("RemovePlayerEntity", eo.OwnerID, entity.ShortPrefabName, abandonedBuilding._guid);
                }
                return false;
            });

            bool canChangeOwner = TryChangeOwner(player, abandonedBuilding);
            var position = abandonedBuilding.center;
            var grid = abandonedBuilding.GetGrid();
            var text = GetMessage("Output Claim", null)
                .Replace("{displayName}", player.displayName)
                .Replace("{userID}", player.UserIDString)
                .Replace("{position}", position.ToString())
                .Replace("{grid}", grid)
                .Replace("{previousName}", abandonedBuilding.previousName)
                .Replace("{previousId}", abandonedBuilding.previousId.ToString());

            abandonedBuilding.CanShowDiscordEnd = false;

            if (canChangeOwner)
            {
                abandonedBuilding.RewardPlayers();
            }

            abandonedBuilding.SetEventCooldowns();
            abandonedBuilding.SetEventCooldown(player.userID);
            abandonedBuilding.currentId = player.userID;
            abandonedBuilding.currentName = player.displayName;
            abandonedBuilding.raiderName = player.displayName;
            abandonedBuilding.raiderId = player.userID;
            abandonedBuilding.IsClaimed = canChangeOwner;

            if (canChangeOwner)
            {
                abandonedBuilding.KillCollider();
                abandonedBuilding.DestroyMe();
                abandonedBuilding.OnClaimed(player, "OnAbandonedBaseClaimed");

                Puts(text);
                Message(user, "Claimed");
            }
            else
            {
                abandonedBuilding.OnClaimed(player, "OnAbandonedBaseClaimFailed");
                abandonedBuilding.DestroyAll();
            }

            if (config.UseLogFile)
            {
                LogToFile("sar", text, this, false);
            }

            if (!canChangeOwner)
            {
                return;
            }

            if (config.Discord.Claim)
            {
                SendDiscordMessage(abandonedBuilding.center, text);
            }

            if (config.Messages.Global)
            {
                foreach (var target in BasePlayer.activePlayerList)
                {
                    Message(target, "GlobalClaim", player.displayName, grid);
                }
            }
        }

        private void CommandClaimShelter(IPlayer user, BasePlayer player, LegacyShelter shelter)
        {
            var text1 = GetMessage("Output Claim", null)
                                .Replace("{displayName}", player.displayName)
                                .Replace("{userID}", player.UserIDString)
                                .Replace("{position}", shelter.transform.position.ToString())
                                .Replace("{grid}", PositionToGrid(shelter.transform.position))
                                .Replace("{previousName}", shelter.OwnerID.ToString())
                                .Replace("{previousId}", shelter.OwnerID.ToString());

            Puts(text1);
            Message(user, "Claimed");

            if (config.Discord.Claim)
            {
                SendDiscordMessage(shelter.transform.position, text1);
            }

            using var entities = DisposableList<BaseEntity>();
            Vis.Entities(shelter.transform.position, 15f, entities, Layers.Mask.Deployed);
            ChangeOwner(player, entities, new(), true);
        }

        private static bool IsLegacyShelterUnlocked(LegacyShelter shelter)
        {
            if (shelter.GetChildDoor() is LegacyShelterDoor door && door.children != null)
            {
                foreach (var child in door.children)
                {
                    if (child is KeyLock keyLock1)
                    {
                        return !keyLock1.IsLocked();
                    }
                }
                return true;
            }
            return false;
        }

        private static bool GetLegacyShelter(Vector3 a, out LegacyShelter sh)
        {
            foreach (var (userid, shelters) in LegacyShelter.SheltersPerPlayer)
            {
                foreach (var shelter in shelters)
                {
                    if (Vector3.Distance(shelter.transform.position, a) <= 15f)
                    {
                        sh = shelter;
                        return true;
                    }
                }
            }
            sh = null;
            return false;
        }

        private bool TryChangeOwner(BasePlayer player, AbandonedBuilding abandonedBuilding)
        {
            bool canChangeOwner = ChangeBuildingOwner(player, abandonedBuilding, out HashSet<ulong> blockedEntityIds);
            blockedEntityIds ??= new();

            if (!canChangeOwner || config.Abandoned.LimitEntities.Enabled && config.Abandoned.LimitEntities.Always)
            {
                blockedEntityIds.UnionWith(abandonedBuilding.entities.Where(x => x.IsValid() && _deployablesU.ContainsKey(x.prefabID)).Select(y => y.net.ID.Value));
            }

            ChangeOwner(player, abandonedBuilding.entities, blockedEntityIds, canChangeOwner);

            return canChangeOwner;
        }

        private bool ChangeBuildingOwner(BasePlayer player, AbandonedBuilding abandonedBuilding, out HashSet<ulong> blockedEntityIds)
        {
            blockedEntityIds = new();
            if (config.Abandoned.LimitEntities.Enabled && LimitEntities != null && LimitEntities.IsLoaded)
            {
                if (LimitEntities.Call("API_ChangeBuildingOwner", abandonedBuilding.entities, (ulong)player.userID) is HashSet<ulong> limitedEntitiesIds)
                {
                    blockedEntityIds = limitedEntitiesIds;
                    return true;
                }
                else
                {
                    Message(player, "Building Limit");
                    return false;
                }
            }
            return true;
        }

        private void ChangeOwner(BasePlayer player, List<BaseEntity> entities, HashSet<ulong> blockedEntityIds, bool canChangeOwner)
        {
            var deployables = new List<BaseEntity>();
            entities.RemoveAll(x => x.IsKilled() || x.net == null);

            if (canChangeOwner)
            {
                //RemoveBuildingOwner(entities);
            }

            foreach (var entity in entities.ToList())
            {
                if (_deployablesU.ContainsKey(entity.prefabID) && blockedEntityIds.Contains(entity.net.ID.Value))
                {
                    if (config.Abandoned.LimitEntities.CrateDeployables) deployables.Add(entity);
                    else if (config.Abandoned.LimitEntities.KillDeployables)
                    {
                        entities.Remove(entity);
                        NextTick(entity.SafelyKill);
                    }
                    continue;
                }
                if (canChangeOwner)
                {
                    entity.OwnerID = player.userID;
                    SetAuthOwner(player, entity);
                }
            }

            if (deployables.Count > 0)
            {
                SpawnBackpacksForDeployables(player, deployables);
                deployables.ForEach(SafelyKill);
            }
        }

        private void RemoveBuildingOwner(List<BaseEntity> entities)
        {
            if (LimitEntities != null && LimitEntities.IsLoaded)
            {
                LimitEntities.Call("API_RemoveBuildingOwner", entities);
            }
        }

        private void SpawnBackpacksForDeployables(BasePlayer player, List<BaseEntity> deployables, int capacityPerBackpack = 48)
        {
            List<(ItemDefinition def, uint prefabID, ulong skin, int amount)> deployableItems = new();

            foreach (var entity in deployables)
            {
                if (_deployablesU.TryGetValue(entity.prefabID, out var def))
                {
                    ExtractDeployableItem(deployableItems, entity.prefabID, entity.skinID, def);

                    if (entity is IItemContainerEntity ice && ice != null && ice.inventory != null && ice.inventory.itemList != null)
                    {
                        foreach (var item in ice.inventory.itemList)
                        {
                            if (_deployablesD.TryGetValue(item.info, out var prefabID))
                            {
                                ExtractDeployableItem(deployableItems, prefabID, item.skin, item.info);
                            }
                        }
                    }
                }
            }

            int numOfBackpacks = (deployableItems.Count + capacityPerBackpack - 1) / capacityPerBackpack;
            int givenItems = 0;

            for (int i = 0; i < numOfBackpacks; i++)
            {
                var backpack = GameManager.server.CreateEntity("assets/prefabs/misc/item drop/item_drop_backpack.prefab", player.GetDropPosition()) as DroppedItemContainer;
                backpack.maxItemCount = 48;
                backpack.lootPanelName = "generic_resizable";
                backpack.playerName = player.displayName;
                backpack.playerSteamID = player.userID;
                backpack.OwnerID = player.userID;
                Unsubscribe(nameof(OnEntitySpawned));
                backpack.Spawn();
                Subscribe(nameof(OnEntitySpawned));
                backpack.inventory = new ItemContainer();
                backpack.inventory.ServerInitialize(null, Mathf.Min(deployableItems.Count, backpack.maxItemCount));
                backpack.inventory.GiveUID();
                backpack.inventory.entityOwner = backpack;
                backpack.inventory.SetFlag(ItemContainer.Flag.NoItemInput, b: true);

                using var tmp = deployableItems.TakePooledList(capacityPerBackpack);
                foreach (var itemInfo in tmp)
                {
                    deployableItems.Remove(itemInfo);
                    Item item = ItemManager.CreateByItemID(itemInfo.def.itemid, itemInfo.amount, itemInfo.skin);
                    if (item == null)
                    {
                        continue;
                    }
                    if (backpack == null || backpack.inventory == null || !item.MoveToContainer(backpack.inventory, -1, false))
                    {
                        item.DropAndTossUpwards(player.GetDropPosition());
                    }
                    givenItems += itemInfo.amount;
                }
            }

            Message(player, $"You've been given {givenItems} deployable items.");
        }

        private static void ExtractDeployableItem(List<(ItemDefinition def, uint prefabID, ulong skin, int amount)> deployableItems, uint prefabID, ulong skinID, ItemDefinition def)
        {
            int index = deployableItems.FindIndex(x => x.prefabID == prefabID && x.skin == skinID && x.amount + 1 <= def.stackable);

            if (index != -1)
            {
                deployableItems[index] = (def, prefabID, skinID, deployableItems[index].amount + 1);
            }
            else
            {
                deployableItems.Add((def, prefabID, skinID, 1));
            }
        }

        private void SetAuthOwner(BasePlayer player, BaseEntity entity)
        {
            if (entity is PoweredRemoteControlEntity rce)
            {
                rce.rcIdentifier = "";
                rce.SendNetworkUpdate();
                return;
            }
            if (entity is LegacyShelter shelter)
            {
                if (shelter.GetChildDoor() is LegacyShelterDoor door && door.children != null)
                {
                    foreach (var child in door.children)
                    {
                        if (child is KeyLock keyLock1)
                        {
                            keyLock1.OwnerID = player.userID;
                            keyLock1.keyCode = UnityEngine.Random.Range(1, 100000);
                            keyLock1.SetFlag(BaseEntity.Flags.Locked, b: true);
                            keyLock1.SendNetworkUpdate();
                        }
                    }
                }
                if (shelter.GetEntityBuildingPrivilege() is SimplePrivilege priv)
                {
                    priv.authorizedPlayers.RemoveWhere(id => !IsAlly(player.userID, id));
                    if (!priv.AnyAuthed() || !priv.IsAuthed(player))
                    {
                        priv.authorizedPlayers.Add(player.userID);
                        priv.SendNetworkUpdate();
                    }
                }
                return;
            }
            if (entity is AutoTurret turret)
            {
                turret.authorizedPlayers.RemoveWhere(id => !IsAlly(player.userID, id));
                if (!turret.AnyAuthed() || !turret.IsAuthed(player))
                {
                    turret.authorizedPlayers.Add(player.userID);
                }
            }
            else if (entity is BuildingPrivlidge priv)
            {
                priv.authorizedPlayers.RemoveWhere(id => !IsAlly(player.userID, id));
                if (!priv.AnyAuthed() || !priv.IsAuthed(player))
                {
                    priv.authorizedPlayers.Add(player.userID);
                    priv.SendNetworkUpdate();
                }
            }
            if (entity.GetSlot(BaseEntity.Slot.Lock) is BaseEntity baseLock2)
            {
                if (baseLock2 is CodeLock codeLock)
                {
                    codeLock.OwnerID = player.userID;
                    codeLock.guestPlayers.Clear();
                    codeLock.whitelistPlayers.RemoveAll(userid => !IsAlly(player.userID, userid));
                    if (!codeLock.whitelistPlayers.Contains(player.userID))
                    {
                        codeLock.whitelistPlayers.Add(player.userID);
                    }
                    codeLock.SetFlag(BaseEntity.Flags.Locked, b: true);
                    codeLock.SendNetworkUpdateImmediate();
                }
                if (baseLock2 is KeyLock keyLock2)
                {
                    keyLock2.OwnerID = player.userID;
                    keyLock2.firstKeyCreated = false;
                    keyLock2.keyCode = UnityEngine.Random.Range(1, 100000);
                    keyLock2.SetFlag(BaseEntity.Flags.Locked, b: true);
                    keyLock2.SendNetworkUpdate();
                }
            }
        }

        private Dictionary<ulong, string> confirmations = new();
        private HashSet<ulong> blocked = new();

        private void CommandConvert(IPlayer user, string command, string[] args)
        {
            command = command.ToLower();
            bool anon = command == "saranon";
            if (!user.HasPermission("abandonedbases.convert") || anon && !user.HasPermission("abandonedbases.admin"))
            {
                Message(user, "No Permission");
                return;
            }

            var player = user.ToPlayer();

            if (args.Length >= 1)
            {
                switch (args[0].ToLower())
                {
                    case "where":
                        {
                            if (GetAbandonedBuilding(player.transform.position, out var abandonedBuilding))
                            {
                                foreach (var target in BasePlayer.activePlayerList)
                                {
                                    if (target.IsAdmin)
                                    {
                                        target.SendConsoleCommand("ddraw.text", 30f, Color.red, abandonedBuilding.center, $"<size=24>{player.displayName} ({player.userID}) {player.Distance(abandonedBuilding.center):N02}m</size>");
                                        target.ChatMessage($"{player.displayName} ({player.userID}) has used the /sar where command and the location of their abandoned building has been drawn on your screen.");
                                        target.ChatMessage($"{player.transform.position} origin\n{abandonedBuilding.center} destination");
                                    }
                                }
                            }
                            return;
                        }
                    case "show":
                        {
                            if (player.IsAdmin)
                            {
                                if (GetAbandonedBuilding(player.transform.position, 100f, out var abandonedBuilding))
                                {
                                    int num = 0;

                                    foreach (var container in abandonedBuilding.containers)
                                    {
                                        if (!container.IsKilled() && !container.inventory.IsEmpty())
                                        {
                                            if (player.IsFlying) player.SendConsoleCommand("ddraw.text", 30f, container.inventory.IsEmpty() ? Color.red : Color.green, container.transform.position, $"<size=24>{container.ShortPrefabName} {container.GetType().Name} {container.inventory.itemList.Count} items</size>");
                                            else player.SendConsoleCommand("ddraw.text", 30f, container.inventory.IsEmpty() ? Color.red : Color.green, container.transform.position, $"<size=24>{container.inventory.itemList.Count} items</size>");
                                            num++;
                                        }
                                    }

                                    if (num > 0)
                                    {
                                        player.ChatMessage($"Containers at this abandoned building have been drawn on your screen.");
                                    }
                                    else
                                    {
                                        player.ChatMessage("There are no containers left at this abandoned building.");
                                    }

                                    abandonedBuilding.CheckEventCompletion(null);
                                }
                                else player.ChatMessage("<color=#C0C0C0>You must be within 100m of an abandoned base to use this command.</color>");
                            }
                            return;
                        }
                    case "cooldown":
                        {
                            CommandCooldown(user, command, args);
                            return;
                        }
                    case "purge":
                        {
                            if (user.HasPermission("abandonedbases.admin") && user.HasPermission("abandonedbases.purgeday"))
                            {
                                Message(user, "Purge");
                                StopAbandonedCoroutine();
                                IsPurgeEnabled = true;
                            }
                            else Message(user, "No Permission");

                            return;
                        }
                    case "cancel":
                        {
                            CommandCancel(user, "cancel", args);
                            return;
                        }
                    case "claim":
                        {
                            CommandClaim(user, "claim", args);
                            return;
                        }
                    case "unlock":
                        {
                            CommandUnlock(user, "unlock", args);
                            return;
                        }
                    case "reset":
                        {
                            CommandReset(user, "reset", args);
                            return;
                        }
                }
            }

            if (user.IsServer)
            {
                return;
            }

            ulong userid = player.userID;

            if (HasCooldown(player, userid, "abandonedbases.convert.nocooldown", data.CooldownBetweenConversion))
            {
                return;
            }

            data.CooldownBetweenConversion[userid] = DateTime.Now.AddSeconds(2f);

            if (GetLegacyShelter(player.transform.position, out var shelter))
            {
                if (blocked.Contains(userid) || config.Abandoned.Shelters.IsBlocked(player, IsAlly))
                {
                    Message(player, "CommandNotAllowed");
                    if (blocked.Add(userid)) timer.Once(5f, () => blocked.Remove(userid));
                    return;
                }
                if (config.Abandoned.Shelters.Convert)
                {
                    CommandEntity(user, player, shelter, anon, args);
                }
                else Message(player, "Disabled");
                return;
            }
            
            if (player.GetParentEntity() is Tugboat tugboat)
            {
                CommandEntity(user, player, tugboat, anon, args);
                return;
            }

            var priv = player.GetBuildingPrivilege();
            if (priv == null || !anon && !priv.AnyAuthed() || !anon && !priv.IsAuthed(player))
            {
                Message(player, "Authorize");
                return;
            }

            var entities = priv.GetBuilding()?.decayEntities?.ToList<BaseEntity>();
            if (entities == null || entities.Count == 0)
            {
                Message(player, "NotCloseEnough");
                return;
            }

            if (AbandonedBuildings.Exists(x => x.entities.Contains(priv)))
            {
                Message(user, "Abandoned Base Already");
                return;
            }

            if (config.Abandoned.Sar.Confirm && !anon)
            {
                if (!confirmations.TryGetValue(userid, out var code))
                {
                    confirmations[userid] = code = UnityEngine.Random.Range(1000, 9999).ToString();
                    timer.Once(60f, () => confirmations.Remove(userid));
                }
                if (!args.Contains(code))
                {
                    Message(user, "Sar confirm", code);
                    float secs = config.Abandoned.GetDespawnSeconds(false);
                    if (config.Abandoned.Sar.Delete && !config.Abandoned.DoNotDestroyManual && secs > 0f) Message(user, "Sar delete", secs);
                    if (config.Abandoned.Sar.Reset && !config.Abandoned.DoNotDestroyManual && config.Abandoned.DespawnSecondsInactiveReset && secs > 0f) Message(user, "Sar reset");
                    return;
                }
            }

            Payment payment = TrySetPayment(player, false);
            if (payment == null)
            {
                return;
            }

            float radius = 0f;
            if (args.Length == 1 && float.TryParse(args[0], out radius))
            {
                radius = Mathf.Clamp(radius, config.Abandoned.MinCustomSphereRadius, config.Abandoned.MaxCustomSphereRadius);
            }

            HashSet<ulong> owners = new(entities.Where(x => x.OwnerID.IsSteamId()).Select(x => x.OwnerID));
            owners.Add(userid);
            owners.Remove(0uL);

            if (CanDebug(user))
            {
                user.Message("Trying to convert compound...");
            }

            TryConvertCompound(user, entities, owners, priv.transform.position, priv.OwnerID, anon, args.Contains("pvp") || config.Abandoned.AllowPVPSAR == true, ConversionType.SAR, payment, radius);
        }

        private void CommandEntity<T>(IPlayer user, BasePlayer player, T entity, bool anon, string[] args) where T : BaseEntity
        {
            var tugboat = entity as Tugboat;
            var shelter = entity as LegacyShelter;
            var priv = tugboat != null ? GetVehiclePrivilege(tugboat.children) : shelter != null ? shelter.GetEntityBuildingPrivilege() : null;
            var isAuthorized = tugboat ? priv != null && priv.AnyAuthed() && priv.IsAuthed(player) : priv != null && priv.IsAuthed(player);

            if (!isAuthorized)
            {
                Message(player, "Authorize");
                return;
            }
            
            if (AbandonedBuildings.Exists(x => x.entities.Contains(entity)))
            {
                Message(user, "Abandoned Base Already");
                return;
            }

            Payment payment = TrySetPayment(player, false);

            if (payment == null)
            {
                return;
            }

            float radius = 0f;
            if (args.Length == 1 && float.TryParse(args[0], out radius))
            {
                radius = Mathf.Clamp(radius, config.Abandoned.MinCustomSphereRadius, config.Abandoned.MaxCustomSphereRadius);
            }

            HashSet<ulong> owners = new(entity.children.Where(x => x.OwnerID.IsSteamId()).Select(x => x.OwnerID));

            owners.Add(player.userID);
            owners.Remove(0uL);

            TryConvertEntity(user, entity, priv, owners, anon, config.Abandoned.AllowPVPSAR == true, ConversionType.SAR, payment, radius);
        }

        private void CommandReport(IPlayer user, string command, string[] args)
        {
            if (!user.IsAdmin && !user.HasPermission("abandonedbases.report"))
            {
                return;
            }

            if (reportCoroutine != null)
            {
                ServerMgr.Instance.StopCoroutine(reportCoroutine);
            }

            reportCoroutine = ServerMgr.Instance.StartCoroutine(ShowDataReportRoutine());
        }

        private void CommandDebug(IPlayer user, string command, string[] args)
        {
            if (!user.HasPermission("abandonedbases.admin"))
            {
                Message(user, "No Permission");
                return;
            }

            if (!CheckDefaultGroup(user))
            {
                return;
            }

            DebugMode = !DebugMode;
            user.Reply($"Debug mode: {DebugMode}");
        }

        private bool CheckDefaultGroup(IPlayer user)
        {
            if (!DefaultHasPermission())
            {
                if (config.PurgePermissions.Count == 0)
                {
                    user.Message("Invalid configuration! You must create at least one permission using a lifetime.");
                }
                else
                {
                    user.Message("Default group has not been granted any purge permission:");
                    user.Message(string.Join(", ", config.PurgePermissions));
                }
                DebugMode = false;
                return false;
            }
            if (DefaultNoPurge(out string res))
            {
                user.Message($"Invalid permission set for default group: {res}");
                DebugMode = false;
                return false;
            }
            return true;
        }

        private void TryForceAddUser(ulong userid, ref int count)
        {
            if (!userid.IsSteamId() || data.LastSeen.ContainsKey(userid) || IsUserExcluded(userid)) return;
            UpdateLastSeen(userid, Epoch.Current);
            count++;
        }

        private void CommandStart(IPlayer user, string command, string[] args)
        {
            if (!user.HasPermission("abandonedbases.admin"))
            {
                Message(user, "No Permission");
                return;
            }

            if (args.Length > 0 && args[0] == "force_add_offline")
            {
                int count = 0;

                foreach (var entity in BaseNetworkable.serverEntities.OfType<BaseEntity>())
                {
                    if (entity is CodeLock codeLock)
                    {
                        codeLock.guestPlayers.ForEach(id => TryForceAddUser(id, ref count));
                        codeLock.whitelistPlayers.ForEach(id => TryForceAddUser(id, ref count));
                    }
                    else if (entity is BuildingPrivlidge priv)
                    {
                        priv.authorizedPlayers.ForEach(x => TryForceAddUser(x, ref count));
                    }
                    else if (entity is AutoTurret turret)
                    {
                        turret.authorizedPlayers.ForEach(x => TryForceAddUser(x, ref count));
                    }
                    TryForceAddUser(entity.OwnerID, ref count);
                }

                if (count > 0) SaveData();
                Message(user, "ForceAddOffline", count);
                return;
            }

            if (args.Contains("secret_expire") && args.Length == 2)
            {
                if (ulong.TryParse(args[1], out ulong userid))
                {
                    UpdateLastSeen(userid, Facepunch.Math.Epoch.Current - (86400 * 365));
                    user.Message($"Time expired for {userid}");
                }
                else if (BasePlayer.FindAwakeOrSleeping(args[1]) is BasePlayer target && target != null)
                {
                    UpdateLastSeen(target.userID, Facepunch.Math.Epoch.Current - (86400 * 365));
                    user.Message($"Time expired for {target.userID}");
                }
                return;
            }

            StartAbandonedRoutine(user, true, true);
        }

        #endregion Commands

        #region Configuration

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new()
            {
                {"No Permission", "You don't have permission to use this command!"},
                {"CommandNotAllowed", "You are not allowed to use this command right now."},
                {"Not An Ally", "You must be an ally of the raid!"},
                {"Abandoned", "An abandoned player's base is now raidable at {0}"},
                {"Abandoned Manual", "An abandoned player's base started by {0} is now raidable at {1}"},
                {"PVPFlag", "[<color=#FF0000>PVP</color>] "},
                {"PVEFlag", "[<color=#008000>PVE</color>] "},
                {"DoomAndGloom", "{0} <color=#FF0000>You have left a PVP zone and can be attacked for another {1} seconds!</color>"},
                {"CannotTeleport", "You are not allowed to teleport from this event"},
                {"NotCloseEnough", "You are not close enough to a building"},
                {"Authorize", "You must be authorized on the cupboard to use this command"},
                {"Cooldown", "You must wait {0} seconds to use this command!"},
                {"HoggingFinishYourRaid", "<color=#FF0000>You must finish your last raid at {0} before joining another.</color>"},
                {"Event Cooldown", "You must wait {0} seconds before you can become owner of this event."},
                {"Purge", "Purge enabled. Type /sab to start converting all bases."},
                {"Base Loot Requirement", "Base has insufficient loot ({0}/{1}) and cannot be converted!"},
                {"Shelter Unlocked", "Shelter has insufficient loot ({0}/{1}) and has been unlocked!"},
                {"Shelter Loot Requirement", "Shelter has insufficient loot ({0}/{1}) and cannot be converted!"},
                {"Shelter Player Requirement", "Shelter does not belong to a player!"},
                {"Tugboat Unlocked", "Tugboat has insufficient loot ({0}/{1}) and has been unlocked!"},
                {"Tugboat Loot Requirement", "Tugboat has insufficient loot ({0}/{1}) and cannot be converted!"},
                {"Tugboat Player Requirement", "Tugboat does not belong to a player!"},
                {"Base Requirements", "Base does not meet requirements: {0}/{1} foundations, {2}/{3} walls. Twig does NOT qualify."},
                {"Abandoned Base Already", "This is an abandoned base already!"},
                {"Near Event Base", "This building is too close to another base event!"},
                {"Near Raidable Event", "This building is too close to a Raidable Base Event!"},
                {"Start", "Starting abandoned base event..."},
                {"CancelCooldown", "You must wait <color=#FF0000>{0}</color> seconds to cancel this event." },
                {"EconomicsWithdraw", "You have paid <color=#FFFF00>${0}</color> to convert your base!"},
                {"EconomicsWithdrawFailed", "You do not have <color=#FFFF00>${0}</color> to convert your base!"},
                {"EconomicsWithdrawCancel", "You have paid <color=#FFFF00>${0}</color> to cancel your converted base!"},
                {"EconomicsWithdrawFailedCancel", "You do not have <color=#FFFF00>${0}</color> to cancel your converted base!"},
                {"ServerRewardPointsTaken", "You have paid <color=#FFFF00>{0} RP</color> to convert your base!"},
                {"ServerRewardPointsFailed", "You do not have <color=#FFFF00>{0} RP</color> to convert your base!"},
                {"ServerRewardPointsTakenCancel", "You have paid <color=#FFFF00>{0} RP</color> to cancel your converted base!"},
                {"ServerRewardPointsFailedCancel", "You do not have <color=#FFFF00>{0} RP</color> to cancel your converted base!"},
                {"CustomCostTaken", "You have paid <color=#FFFF00>{0}</color> to convert your base!"},
                {"CustomCostFailed", "You do not have <color=#FFFF00>{0} {1}</color> to convert your base!"},
                {"CustomCostTakenCancel", "You have paid <color=#FFFF00>{0}</color> to cancel your converted base!"},
                {"CustomCostFailedCancel", "You do not have <color=#FFFF00>{0} {1}</color> to cancel your converted base!"},
                {"StartScan", "Starting manual scan for abandoned bases... this could take a while..."},
                {"EndScan", "Abandoned base scan finished: {0}"},
                {"IsInBlockedZone", "This building is in a blocked zone."},
                {"TimeLeft", "This building will become abandoned in {0}"},
                {"TimeLeftDisabled", "This building is eligible to convert during normal raiding hours."},
                {"No privilege found", "This building is not eligible as it does not have a tool cupboard in range."},
                {"Base is active", "This building is not eligible as it has at least one active user."},
                {"Nothing", "No abandoned event found. You must use this command inside of an event."},
                {"MustFinishCupboards", "This event cannot be canceled until you have looted every TC!"},
                {"MustFinish", "This event cannot be canceled until all boxes and TC are looted!"},
                {"Cancelled", "You have cancelled this event."},
                {"Claimed", "You have claimed this base as your own."},
                {"Unlocked", "You have reset the event status of this base."},
                {"GlobalClaim", "{0} has claimed the base in {1}."},
                {"OnEventCompletedLocalOwned", "<color=#FFFF00>{0}</color> has completed the abandoned event owned by <color=#FFFF00>{1}</color> in <color=#FFFF00>{2}</color>!"},
                {"OnEventCompletedLocal", "<color=#FFFF00>{0}</color> has completed the abandoned event in <color=#FFFF00>{1}</color>!"},
                {"OnEventCompleted", "You have completed the event."},
                {"OnEventCompletedClaim", "You have completed the event. You may type <color=#FF0000>/sar claim</color> to take over this base."},
                {"OnEventCompletedCancel", "You have completed the event. You may type <color=#FF0000>/sar cancel</color> to end the event."},
                {"OnEventCompletedClaimCancel", "You have completed the event. You may type <color=#FF0000>/sar claim</color> to take over this base, or <color=#FF0000>/sar cancel</color> to end the event."},
                {"OnEventAutomatedCancel", "The owner of this event has come online and this automated event has been canceled."},
                {"OnBuiltPrivilege", "You cannot claim a base by placing a tool cupboard. You must type /sar claim."},
                {"OnBuiltPrivilegeEx", "You cannot claim a base by placing a tool cupboard. You must type /sar claim, when the raid is fully looted."},
                {"OnBuiltPrivilegeNone", "You cannot claim a base by placing a tool cupboard because you do not have the required permissions to do so."},
                {"OnPlayerExit", "<color=#FF0000>You have left a raidable PVP base!</color>"},
                {"OnPlayerExitPVE", "<color=#FF0000>You have left a raidable PVE base!</color>"},
                {"OnPlayerEntered", "<color=#FF0000>You have entered a raidable PVP base!</color>"},
                {"OnPlayerEnteredPVE", "<color=#FF0000>You have entered a raidable PVE base!</color>"},
                {"ForceAddOffline", "{0} players have been added to the database."},
                {"BlockedZones", "Blocked spawn points in {0} zones."},
                {"SkillTreeXP", "You have received <color=#FFFF00>{0} XP</color> from this event!"},
                {"ServerRewardPoints", "You have received <color=#FFFF00>{0} RP</color> from this event!"},
                {"EconomicsDeposit", "You have received <color=#FFFF00>${0}</color> from this event!"},
                {"CannotBuild", "<color=#FF0000>You are not allowed to build here!</color>"},
                {"CannotBuildLadders", "<color=#FF0000>You are not allowed to build ladders here!</color>"},
                {"FormatOnline", "<color=#c70000>{index}</color>. <color=#A8A7AE>{username}</color> (<color=#A8A7AE>{userid}</color>) is <color=#00FF00>online</color>" },
                {"FormatLastSeen", "<color=#c70000>{index}</color>. <color=#A8A7AE>{username}</color> (<color=#A8A7AE>{userid}</color>) was last seen {time} ago" },
                {"FormatLastSeenUnknown", "<color=#c70000>{index}</color>. <color=#A8A7AE>{username}</color> (<color=#A8A7AE>{userid}</color>)" },
                {"Engine failure", "<color=#c70000>You are not allowed to start the engine during this event!</color>" },
                {"Drawn Authed","<size=22>AUTHED</size>"},
                {"Drawn Entity","<size=22>OWNER</size>"},
                {"Reset", "Reset cooldowns for {0}" },
                {"Output Completed", "{activeName} ({activeId}) has raided the base with {players} participating at {center} ({grid}) owned by {previousName} ({previousId})"},
                {"Output Uncontested", "{activeName} ({activeId}) base has been abandoned at {center} ({grid}) by {previousName} ({previousId})"},
                {"Output Cancel", "Abandoneded owner {displayName} ({userID}) has come online; automated event canceled at {center} ({grid})"},
                {"Output Execute", "{displayName} ({userID}) executed command /{command} at {position} in {grid} at time {time}"},
                {"Output Unlock", "{displayName} ({userID}) has unlocked the base at {position} ({grid}) from {previousName} ({previousId})"},
                {"Output Claim", "{displayName} ({userID}) has claimed the base at {position} ({grid}) from {previousName} ({previousId})"},
                {"Building Limit", "You cannot claim this base without exceeding the building limit!"},
                {"Sar confirm", "Warning! Using this command will convert your base so that anyone can raid it. Type <color=#FFFF00>/sar {0}</color> to confirm."},
                {"Sar delete", "Bases will be deleted <color=#FFFF00>{0}</color> seconds after conversion."},
                {"Sar reset", "This time is reset every time the base is damaged."},
                {"Limit", "Abandoned Base Limit: {0}"},
                {"Limit Days", "Abandoned Base Limit: {0} days"},
                {"Disabled", "This command has been disabled by the server owner." },
                {"Blacklisted Pickup Item", "{0} is blacklisted from pickup!" },
            }, this);
            
            lang.RegisterMessages(new()
            {
                {"No Permission", "У вас нет привилегии для использовании этой команды!"},
                {"CommandNotAllowed", "You are not allowed to use this command right now."},
                {"Not An Ally", "Вы должны быть союзником рейда!"},
                {"Abandoned", "Заброшенная база игрока теперь доступна для рейда. Квадрат: <color=#ff8833>{0}</color>"},
                {"Abandoned Manual", "Заброшенная база игрока, начатая {0}, теперь доступна для рейда в {1}" },
                {"PVPFlag", "[<color=#FF0000>ПВП</color>] "},
                {"PVEFlag", "[<color=#008000>ПВЕ</color>] "},
                {"DoomAndGloom", "{0} <color=#FF0000>Внимание!</color> Вы покинули зону ПВП и сможете получать урон в течении {1} сек!"},
                {"CannotTeleport", "Вы не можете телепортироваться в зоне заброшенной базы"},
                {"NotCloseEnough", "Вы находитесь недостаточно близко к зданию"},
                {"Authorize", "Вы должны быть авторизованны в шкафу для использования этой команды"},
                {"Cooldown", "Вы должны подождать {0} секунд, чтобы использовать эту команду!"},
                {"Event Cooldown", "Вы должны подождать {0} секунд, прежде чем станете владельцем этого события."},
                {"Purge", "Очистка включена. Введите /sab, чтобы начать преобразование всех баз."},
                {"Base Loot Requirement", "На базе недостаточно добычи ({0}/{1}) и она не может быть преобразована!"},
                {"Shelter Unlocked", "Приют разблокирован. Приют имеет недостаточное количество добычи ({0}/{1}) и был разблокирован!"},
                {"Shelter Loot Requirement", "Приют имеет недостаточное количество добычи ({0}/{1}) и не может быть конвертирован!"},
                {"Shelter Player Requirement", "Приют не принадлежит игроку!"},
                {"Tugboat Unlocked", "Теплоход разблокирован. Теплоход имеет недостаточное количество добычи ({0}/{1}) и был разблокирован!"},
                {"Tugboat Loot Requirement", "Теплоход имеет недостаточное количество добычи ({0}/{1}) и не может быть конвертирован!"},
                {"Tugboat Player Requirement", "Теплоход не принадлежит игроку!"},
                {"Base Requirements", "Основание не соответствует требованиям: {0}/{1} фундаменты, {2}/{3} стены. Солома НЕ подходит."},
                {"Near Event Base", "Это здание слишком близко к другой базе!"},
                {"Near Raidable Event", "This building is too close to a Raidable Base Event!"},
                {"Start", "Запуск рейда заброшенных баз..."},
                {"EconomicsWithdraw", "Вы заплатили <color=#FFFF00>${0}</color> за конвертацию вашей базы!"},
                {"EconomicsWithdrawFailed", "У вас нет <color=#FFFF00>${0}</color> для преобразования вашей базы!"},
                {"ServerRewardPointsTaken", "Вы заплатили <color=#FFFF00>{0} RP</color> за конвертацию вашей базы!"},
                {"ServerRewardPointsFailed", "У вас нет <color=#FFFF00>{0} RP</color> для конвертации вашей базы!"},
                {"StartScan", "Запускаю ручное сканирование на предмет заброшенных баз... это может занять некоторое время..."},
                {"EndScan", "Сканирование заброшенной базы завершено: {0}"},
                {"IsInBlockedZone", "Это здание находится в заблокированной зоне."},
                {"TimeLeft", "Это здание станет заброшенным в {0}"},
                {"TimeLeftDisabled", "Это здание можно конвертировать в обычное время рейдов."},
                {"No privilege found", "Это здание не подходит, так как в нем нет шкафа с инструментами."},
                {"Base is active", "Это здание не подходит, так как в нем есть по крайней мере один активный пользователь."},
                {"Nothing", "Заброшенное событие не найдено. Вы должны использовать эту команду внутри события."},
                {"MustFinishCupboards", "This event cannot be canceled until you have looted every TC first!"},
                {"MustFinish", "Это событие не может быть отменено до тех пор, пока все ящики и шкаф не будут разграблены!"},
                {"Cancelled", "Вы отменили это мероприятие."},
                {"Claimed", "Вы заявили, что эта база принадлежит вам."},
                {"Unlocked", "Вы сбросили статус события этой базы."},
                {"GlobalClaim", "{0} забрал базу в {1}."},
                {"OnEventCompletedLocalOwned", "<color=#FFFF00>{0}</color> завершил заброшенное мероприятие, принадлежащее <color=#FFFF00>{1}</color> в <color=#FFFF00>{2}</color>!"},
                {"OnEventCompletedLocal", "<color=#FFFF00>{0}</color> завершил заброшенное мероприятие в <color=#FFFF00>{1}</color>!"},
                {"OnEventCompleted", "Вы завершили событие."},
                {"OnEventCompletedClaim", "Вы завершили событие. Вы можете набрать <color=#FF0000>/sar claim</color>, чтобы завладеть этой базой."},
                {"OnEventCompletedCancel", "Вы завершили событие. Вы можете ввести <color=#FF0000>/sar cancel</color>, чтобы завершить событие."},
                {"OnEventCompletedClaimCancel", "Вы завершили событие. Вы можете ввести <color=#FF0000>/sar claim</color>, чтобы захватить эту базу, или <color=#FF0000>/sar cancel</color>, чтобы завершить событие."},
                {"OnEventAutomatedCancel", "Владелец этого события в сети, и это автоматическое событие было отменено."},
                {"OnBuiltPrivilege", "Вы не можете претендовать на базу, разместив шкаф для инструментов. Вы должны ввести /sar для отправки заявки."},
                {"OnBuiltPrivilegeEx", "Вы не можете претендовать на базу, разместив шкаф для инструментов. Вы должны ввести /sar для отправки заявки, когда рейд будет полностью разграблен."},
                {"OnBuiltPrivilegeNone", "Вы не можете претендовать на базу, разместив шкаф для инструментов, поскольку у вас нет необходимых разрешений для этого."},
                {"OnPlayerExit", "<color=#FF0000>Внимание!</color> Вы покинули базу PVP с возможностью рейда!"},
                {"OnPlayerExitPVE", "<color=#FF0000>Внимание!</color> Вы покинули базу PVE, доступную для рейдов!"},
                {"OnPlayerEntered", "<color=#FF0000>Внимание!</color> Вы вошли на базу PVP, доступную для рейдов!"},
                {"OnPlayerEnteredPVE", "<color=#FF0000>Внимание!</color> Вы вошли на базу PVE, доступную для рейдов!"},
                {"BlockedZones", "Заблокированные точки появления {0} зон."},
                {"CannotBuild", "<color=#FF0000>Вам не разрешается строить здесь</color>"},
                {"CannotBuildLadders", "<color=#FF0000>You are not allowed to build ladders here!</color>"},
                {"FormatOnline", "<color=#c70000>{index}</color>. <color=#A8A7AE>{username}</color> (<color=#A8A7AE>{userid}</color>) is <color=#00FF00>online</color>" },
                {"FormatLastSeen", "<color=#c70000>{index}</color>. <color=#A8A7AE>{username}</color> (<color=#A8A7AE>{userid}</color>) was last seen {time} ago" },
                {"FormatLastSeenUnknown", "<color=#c70000>{index}</color>. <color=#A8A7AE>{username}</color> (<color=#A8A7AE>{userid}</color>)" },
                {"Engine failure", "<color=#c70000>You are not allowed to start the engine during this event!</color>" },
                {"Drawn Authed","<size=22>AUTHED</size>"},
                {"Drawn Entity","<size=22>OWNER</size>"},
                {"Reset", "Сбросить время восстановления для {0}" },
                {"Output Completed", "{activeName} ({activeId}) совершил рейд на базу с участием {players} игроков в {center} ({grid}), принадлежащую {previousName} ({previousId})"},
                {"Output Uncontested", "База {activeName} ({activeId}) была брошена в {center} ({grid}) игроком {previousName} ({previousId})"},
                {"Output Cancel", "Abandoned Base События: Владелец, бросивший свою базу {displayName} ({userID}), зашёл в игру; автоматическое мероприятие отменено в {center} ({grid})"},
                {"Output Execute", "{displayName} ({userID}) выполнил команду /{command} в позиции {position} в {grid} во время {time}"},
                {"Output Unlock", "{displayName} ({userID}) открыл доступ к базе в {position} ({grid}) от {previousName} ({previousId})"},
                {"Output Claim", "{displayName} ({userID}) завладел базой в {position} ({grid}) от {previousName} ({previousId})"},
                {"Building Limit", "Вы не можете заявить права на эту базу, не превысив лимит строительства!"},
                {"Sar confirm", "Внимание! Использование этой команды преобразует вашу базу так, чтобы кто угодно мог её атаковать. Введите /sar <color=#FFFF00>{0}</color> для подтверждения."},
                {"Sar delete", "Базы будут удалены через <color=#FFFF00>{0}</color> секунд после преобразования."},
                {"Sar reset", "Это время сбрасывается каждый раз, когда база повреждается."},
                {"Limit", "Abandoned Base Лимит: {0}"},
                {"Limit Days", "Abandoned Base Лимит: {0} Дни"},
                {"Disabled", "Эта команда была отключена владельцем сервера."},
                {"Blacklisted Pickup Item", "{0} в черном списке для самовывоза!"},
            }, this, "ru");

            lang.RegisterMessages(new()
            {
                {"No Permission", "No tienes permiso para usar este comando!"},
                {"CommandNotAllowed", "You are not allowed to use this command right now."},
                {"Not An Ally", "Debes ser un aliado de la redada!"},
                {"Abandoned", "Se ha convertido una base en {0}"},
                {"Abandoned Manual", "La base abandonada del jugador iniciada por {0} ahora es saqueable en {1}" },
                {"PVPFlag", "[<color=#FF0000>JcJ</color>] "},
                {"PVEFlag", "[<color=#008000>JcE</color>] "},
                {"DoomAndGloom", "{0} <color=#FF0000>Has dejado una zona PVP y podrás ser atacado durante {1} segundos!</color>"},
                {"CannotTeleport", "No tienes permiso para teleport desde este evento"},
                {"NotCloseEnough", "No estas lo suficientemente cerca para construir"},
                {"Authorize", "Debes de estar autorizado en el armario para usar este comando"},
                {"Cooldown", "Debes esperar {0} segundos para usar este comando"},
                {"Event Cooldown", "Debes esperar {0} segundos antes de poder ser el propietario de este evento."},
                {"Purge", "Purga Activada. Escribe /sab para empezar la conversión de todas las bases."},
                {"Base Loot Requirement", "¡La base no tiene suficiente botín ({0}/{1}) y no se puede convertir!"},
                {"Shelter Unlocked", "Refugio ha sido desbloqueado, pero tiene un botín insuficiente ({0}/{1})"},
                {"Shelter Loot Requirement", "Refugio tiene un botín insuficiente ({0}/{1}) y no se puede convertir"},
                {"Shelter Player Requirement", "Refugio no pertenece a ningún jugador"},
                {"Tugboat Unlocked", "El Barco Remolcador ha sido desbloqueado, pero tiene un botín insuficiente ({0}/{1})"},
                {"Tugboat Loot Requirement", "El Barco Remolcador tiene un botín insuficiente ({0}/{1}) y no se puede convertir"},
                {"Tugboat Player Requirement", "El Barco Remolcador no pertenece a ningún jugador"},
                {"Base Requirements", "La base no cumple los requisitos: {0}/{1} cimientos, {2}/{3} muros. La paja no cuenta...."},
                {"Near Event Base", "Este edificio está demasiado cerca de otra base!"},
                {"Near Raidable Event", "This building is too close to a Raidable Base Event!"},
                {"Start", "Iniciando el evento de bases abandonadas"},
                {"EconomicsWithdraw", "Has pagado <color=#FFFF00>${0}</color> para convertir tu base!"},
                {"EconomicsWithdrawFailed", "Tu no tienes <color=#FFFF00>${0}</color> para convertir tu base!"},
                {"ServerRewardPointsTaken", "Has pagado <color=#FFFF00>{0} RP</color> para convertir tu base!"},
                {"ServerRewardPointsFailed", "Tu no tienes <color=#FFFF00>{0} RP</color> para convertir tu base!"},
                {"StartScan", "Iniciando el escaneo para las bases abandonadas... esto puede tardar un poco, paciencia..."},
                {"EndScan", "Escaneo de bases abandonadas finalizado: {0}"},
                {"IsInBlockedZone", "Este edificio está en una zona bloqueada."},
                {"TimeLeft", "Este edificio se convertirá en base abandonada en {0}"},
                {"TimeLeftDisabled", "Este edificio es elegible para convertirse durante las horas normales de saqueo"},
                {"No privilege found", "Este edificio no cumple los requisitos, no tiene un armario en rango."},
                {"Base is active", "Este edificio no cumple los requisitos, tiene al menos un usuario activo"},
                {"Nothing", "No se ha encontrado ningun evento de abandono. Debes usar este comando dentro de un evento"},
                {"MustFinishCupboards", "This event cannot be canceled until you have looted every TC first!"},
                {"MustFinish", "Este evento no puede ser cancelado hasta que todas las cajas y armario se hayan looteado!"},
                {"Cancelled", "Has cancelado este evento."},
                {"Claimed", "Has reclamado esta base como tuya."},
                {"Unlocked", "Has restablecido el estado del evento de esta base."},
                {"GlobalClaim", "{0} ha reclamado la base en {1}."},
                {"OnEventCompletedLocalOwned", "<color=#FFFF00>{0}</color> ha completado el evento abandonado propiedad de <color=#FFFF00>{1}</color> en <color=#FFFF00>{2}</color>!"},
                {"OnEventCompletedLocal", "<color=#FFFF00>{0}</color> ha completado el evento abandonado en <color=#FFFF00>{1}</color>!"},
                {"OnEventCompleted", "Has completado el evento."},
                {"OnEventCompletedClaim", "Has completado el evento. Puede escribir <color=#FF0000>/sar claim</color> para hacerse cargo de esta base."},
                {"OnEventCompletedCancel", "Has completado el evento. Puede escribir <color=#FF0000>/sar cancel</color> para terminar el evento."},
                {"OnEventCompletedClaimCancel", "Has completado el evento. Puede escribir <color=#FF0000>/sar claim</color> para hacerse cargo de esta base, o <color=#FF0000>/sar cancel</color> para terminar el evento."},
                {"OnEventAutomatedCancel", "El propietario de este evento ha iniciado sesión y este evento automatizado ha sido cancelado"},
                {"OnBuiltPrivilege", "Tu no puedes reclamar una base. Debes teclear /sar para reclamar."},
                {"OnBuiltPrivilegeEx", "Tu no puedes reclamar una base. Debes teclear /sar para reclamar, cuando la raid haya sido saqueada completamente."},
                {"OnBuiltPrivilegeNone", "Tu no puedes reclamar una base porque no tienes los permisos requeridos para hacerlo."},
                {"OnPlayerExit", "<color=#FF0000>Has salido de una base raidable PVP !</color>"},
                {"OnPlayerExitPVE", "<color=#FF0000>Has salido de una base raidable PVE !</color>"},
                {"OnPlayerEntered", "<color=#FF0000>Has entrado en una base PVP!</color>"},
                {"OnPlayerEnteredPVE", "<color=#FF0000>Has entrado en una base PVE </color>"},
                {"BlockedZones", "Puntos de generación bloqueados en {0} zonas."},
                {"CannotBuild", "<color=#FF0000>No se le permite construir aquí</color>"},
                {"FormatOnline", "<color=#c70000>{index}</color>. <color=#A8A7AE>{username}</color> (<color=#A8A7AE>{userid}</color>) is <color=#00FF00>online</color>" },
                {"FormatLastSeen", "<color=#c70000>{index}</color>. <color=#A8A7AE>{username}</color> (<color=#A8A7AE>{userid}</color>) was last seen {time} ago" },
                {"FormatLastSeenUnknown", "<color=#c70000>{index}</color>. <color=#A8A7AE>{username}</color> (<color=#A8A7AE>{userid}</color>)" },
                {"Engine failure", "<color=#c70000>You are not allowed to start the engine during this event!</color>" },
                {"Drawn Authed","<size=22>AUTHED</size>"},
                {"Drawn Entity","<size=22>OWNER</size>"},
                {"Reset", "Reiniciar los tiempos de reutilización para {0}" },
                {"Output Completed", "{activeName} ({activeId}) ha asaltado la base con {players} participantes en {center} ({grid}) propiedad de {previousName} ({previousId})"},
                {"Output Uncontested", "{activeName} ({activeId}) ha abandonado la base en {center} ({grid}) anteriormente propiedad de {previousName} ({previousId})"},
                {"Output Cancel", "Cancelación Automatizada de Salida: 'Abandoned Base' {displayName} ({userID}) se ha conectado; evento automático cancelado en {center} ({grid})"},
                {"Output Execute", "{displayName} ({userID}) ejecutó el comando /{command} en {position} en {grid} a las {time}"},
                {"Output Unlock", "{displayName} ({userID}) ha desbloqueado la base en {position} ({grid}) de {previousName} ({previousId})"},
                {"Output Claim", "{displayName} ({userID}) ha reclamado la base en {position} ({grid}) de {previousName} ({previousId})"},
                {"Building Limit", "No puedes reclamar esta base sin exceder el límite de construcción!"},
                {"Sar confirm", "¡Atención! Usar este comando convertirá tu base para que cualquiera pueda atacarla. Escribe /sar <color=#FFFF00>{0}</color> para confirmar."},
                {"Sar delete", "Las bases serán eliminadas <color=#FFFF00>{0}</color> segundos después de la conversión."},
                {"Sar reset", "Este tiempo se reinicia cada vez que la base sufre daños."},
                {"Limit", "Abandoned Base Límite: {0}"},
                {"Limit Days", "Abandoned Base Límite: {0} Días"},
                {"Disabled", "Este comando ha sido deshabilitado por el propietario del servidor." },
                {"Blacklisted Pickup Item", "¡{0} está en la lista negra para recoger!"},
            }, this, "es-ES");
        }

        private static string PositionToGrid(Vector3 position) => MapHelper.PositionToString(position);

        private void Message(IPlayer user, string key, params object[] args)
        {
            if (user == null)
            {
                return;
            }

            if (user.Object is BasePlayer)
            {
                Message(user.ToPlayer(), key, args);
            }
            else user.Message(GetMessage(key, user.Id, args));
        }

        private void Message(BasePlayer player, string key, params object[] args)
        {
            if (player == null)
            {
                return;
            }

            string message = GetMessage(key, player.UserIDString, args);

            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            if (config.Messages.Message)
            {
                Player.Message(player, message, config.ChatID);
            }

            if (config.Messages.AA.Enabled || config.Messages.NotifyType != -1)
            {
                if (!_notifications.TryGetValue(player.userID, out var notifications))
                {
                    _notifications[player.userID] = notifications = new();
                }

                notifications.Add(new()
                {
                    player = player,
                    messageEx = message
                });
            }
        }

        private void CheckNotifications()
        {
            if (_notifications.Count > 0)
            {
                using var tmp = _notifications.ToPooledList();
                foreach (var entry in tmp)
                {
                    var notification = entry.Value.ElementAt(0);

                    SendNotification(notification);

                    entry.Value.Remove(notification);

                    if (entry.Value.Count == 0)
                    {
                        _notifications.Remove(entry.Key);
                    }
                }
            }
        }

        private void SendNotification(Notification notification)
        {
            if (!notification.player.IsReallyConnected())
            {
                return;
            }

            if (config.Messages.AA.Enabled && AdvancedAlerts.CanCall())
            {
                AdvancedAlerts?.Call("SpawnAlert", notification.player, "hook", notification.messageEx, config.Messages.AA.AnchorMin, config.Messages.AA.AnchorMax, config.Messages.AA.Time);
            }

            if (config.Messages.NotifyType != -1 && Notify.CanCall())
            {
                Notify?.Call("SendNotify", notification.player, config.Messages.NotifyType, notification.messageEx);
            }
        }

        private void SendDiscordMessage(Vector3 v, string text)
        {
            if (string.IsNullOrEmpty(config.Discord.Webhook) || config.Discord.Webhook == "https://support.discordapp.com/hc/en-us/articles/228383668-Intro-to-Webhooks")
            {
                return;
            }

            var embed = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = Name,
                        fields = new[]
                        {
                            new { name = "Location", value = $"{v} @ {PositionToGrid(v)}", inline = true },
                            new { name = "Message", value = text, inline = false },
                            new { name = "Server", value = $"{ConVar.Server.hostname}", inline = false }
                        },
                        color = 15844367
                    }
                }
            };

            string jsonPayload = JsonConvert.SerializeObject(embed);

            discordMessages.Add(jsonPayload);

            if (discordMessages.Count == 1)
            {
                timer.Once(1f, CheckDiscordMessages);
            }
        }

        private void CheckDiscordMessages()
        {   
            string json = discordMessages[0];
            discordMessages.RemoveAt(0);
            if (discordMessages.Count > 0) timer.Once(1f, CheckDiscordMessages);

            webrequest.Enqueue(config.Discord.Webhook, json, (code, response) =>
            {
                if (code != 200 && code != 204)
                {
                    Puts($"Failed to send message to Discord. HTTP Code: {code}, Response: {response}, Payload: {json}");
                }
            }, this, Core.Libraries.RequestMethod.POST, headers);
        }

        private Dictionary<string, string> headers = new() { { "Content-Type", "application/json" } };

        private List<string> discordMessages = new();

        private Configuration config;

        private static List<PurgeSettings> DefaultPurgeSettings()
        {
            return new()
            {
                new()
                {
                    LifetimeRaw = "7",
                    Permission = "abandonedbases.vip"
                },
                new()
                {
                    LifetimeRaw = "5",
                    Permission = "abandonedbases.veteran"
                },
                new()
                {
                    LifetimeRaw = "3",
                    Permission = "abandonedbases.basic"
                },
            };
        }

        private static List<string> DefaultBlacklistCommands()
        {
            return new() { "command1", "command2", "command3" };
        }

        public class BuildingOptionsAutoTurrets
        {
            [JsonProperty(PropertyName = en ? "Turrets Can Hurt Players" : "Турели могут наносить урон игрокам")]
            public bool CanDamagePlayers { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Enabled" : "Включено", NullValueHandling = NullValueHandling.Ignore)]
            public bool? _Enabled { get; set; } = null;

            [JsonProperty(PropertyName = en ? "Enable The Following Options" : "Включите следующие параметры")]
            public bool Enabled { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Aim Cone" : "Конус Прицеливания")]
            public float AimCone { get; set; } = 5f;

            [JsonProperty(PropertyName = en ? "Ammo" : "Боеприпасы")]
            public int Ammo { get; set; } = 256;

            [JsonProperty(PropertyName = en ? "Infinite Ammo" : "Неограниченные Боеприпасы")]
            public bool InfiniteAmmo { get; set; }

            [JsonProperty(PropertyName = en ? "Minimum Damage Modifier" : "Минимальный Модификатор Урона")]
            public float Min { get; set; } = 1f;

            [JsonProperty(PropertyName = en ? "Maximum Damage Modifier" : "Максимальный Модификатор Урона")]
            public float Max { get; set; } = 1f;

            [JsonProperty(PropertyName = en ? "Start Health" : "Начальное Здоровье")]
            public float Health { get; set; } = 1000f;

            [JsonProperty(PropertyName = en ? "Sight Range" : "Дальность Обзора")]
            public float SightRange { get; set; } = 30f;

            [JsonProperty(PropertyName = en ? "Double Sight Range When Shot" : "Удвоение Дальности Обзора При Выстреле")]
            public bool AutoAdjust { get; set; }

            [JsonProperty(PropertyName = en ? "Set Hostile (False = Do Not Set Any Mode)" : "Установить Как Враждебный (False = Не Устанавливать Режим)")]
            public bool Hostile { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Has Power" : "Имеется Питание")]
            public bool HasPower { get; set; }

            [JsonProperty(PropertyName = en ? "Requires Power Source" : "Требует Источник Питания")]
            public bool RequiresPower { get; set; }

            [JsonProperty(PropertyName = en ? "Remove Equipped Weapon" : "Снять Установленное Оружие")]
            public bool RemoveWeapon { get; set; }

            [JsonProperty(PropertyName = en ? "Random Weapons To Equip When Unequipped" : "Случайные Оружия Для Экипировки При Разгрузке", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Shortnames { get; set; } = new() { "rifle.ak" };
        }

        public class BannedSettings
        {
            [JsonProperty(PropertyName = en ? "Enabled" : "Включено")]
            public bool Enabled { get; set; }
            
            //[JsonProperty(PropertyName = en ? "Convert After X Seconds (0 = Use `Run Every X Seconds` Instead)" : "Конвертировать через X секунд (0 = использовать вместо этого `Запускать каждые X секунд`)")]
            //public float Delay { get; set; }

            [JsonProperty(PropertyName = en ? "Ignored UserID (Server Owner, etc)" : "Игнорируются идентификаторы пользователей (владелец сервера и т. д.)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Ignored { get; set; } = new();
        }

        public class AbandonedDisabledSettings
        {
            [JsonProperty(PropertyName = en ? "Enabled" : "Включено")]
            public bool Enabled { get; set; }

            [JsonProperty(PropertyName = en ? "Start Time" : "Время Начала")]
            public string Start { get; set; }

            [JsonProperty(PropertyName = en ? "End Time" : "Время Окончания")]
            public string End { get; set; }

            public AbandonedDisabledSettings() { }

            public AbandonedDisabledSettings(string start, string end)
            {
                Start = start;
                End = end;
            }

            public bool CanBlockAutomaticConversion()
            {
                if (!Enabled)
                {
                    return false;
                }
                if (!DateTime.TryParseExact(Start, "HH:mm", null, System.Globalization.DateTimeStyles.None, out var start))
                {
                    Puts("Invalid datetime format in config: {0}", Start);
                    Enabled = false;
                    return false;
                }
                if (!DateTime.TryParseExact(End, "HH:mm", null, System.Globalization.DateTimeStyles.None, out var end))
                {
                    Puts("Invalid datetime format in config: {0}", End);
                    Enabled = false;
                    return false;
                }
                return DateTime.Now >= start && DateTime.Now <= end;
            }
        }

        public static List<AbandonedDisabledSettings> DefaultDisabledSettings
        {
            get
            {
                return new()
                {
                    new("00:00", "12:00"),
                    new("12:00", "23:59"),
                };
            }
        }

        public static List<CustomCostOptions> DefaultCustomCost
        {
            get
            {
                return new()
                {
                    new(0)
                };
            }
        }

        public class CustomCostOptions
        {
            [JsonProperty(PropertyName = en ? "Item Shortname" : "Краткое Название Предмета")]
            public string Shortname { get; set; } = "scrap";

            [JsonProperty(PropertyName = en ? "Item Name" : "Название Предмета")]
            public string Name { get; set; } = null;

            [JsonProperty(PropertyName = en ? "Amount" : "Количество")]
            public int Amount { get; set; }

            [JsonProperty(PropertyName = en ? "Skin" : "Скин")]
            public ulong Skin { get; set; }

            [JsonIgnore]
            public ItemDefinition Definition { get; set; }

            public bool IsValid()
            {
                if (!string.IsNullOrEmpty(Shortname) && Amount > 0)
                {
                    if (Definition == null)
                    {
                        Definition = ItemManager.FindItemDefinition(Shortname);
                    }

                    return Definition != null;
                }

                return false;
            }

            public CustomCostOptions(int amount)
            {
                Amount = amount;
            }
        }

        public class SphereColorSettings
        {
            [JsonProperty(PropertyName = en ? "When Locked" : "Когда заблокировано")]
            public SphereColor Locked;

            [JsonProperty(PropertyName = en ? "When Unlocked" : "Когда разблокировано")]
            public SphereColor Unlocked;

            [JsonProperty(PropertyName = en ? "When PVP" : "Когда PVP")]
            public SphereColor PVPState;

            [JsonProperty(PropertyName = en ? "When PVE" : "Когда PVE")]
            public SphereColor PVEState;

            [JsonProperty(PropertyName = en ? "When Active" : "Когда активно")]
            public SphereColor Active;

            [JsonProperty(PropertyName = en ? "When Inactive" : "Когда неактивно")]
            public SphereColor Inactive;
        }

        public class AbandonedSettings
        {
            [JsonProperty(PropertyName = en ? "Sphere Colors (0 None, 1 Blue, 2 Cyan, 3 Green, 4 Magenta, 5 Purple, 6 Red, 7 Yellow)" : "Цвета сфер (0 Нет, 1 Синий, 2 Голубой, 3 Зеленый, 4 Пурпурный, 5 Фиолетовый, 6 Красный, 7 Желтый)")]
            public SphereColorSettings SphereColor = new();

            [JsonProperty(PropertyName = "SAR")]
            public SarSettings Sar { get; set; } = new();

            [JsonProperty(PropertyName = en ? "Automatic Conversions Disabled Between These Times" : "Автоматические Конверсии Отключены В Эти Времена", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<AbandonedDisabledSettings> Disabled { get; set; } = DefaultDisabledSettings;

            [JsonProperty(PropertyName = en ? "Auto Turrets" : "Автотурели")]
            public BuildingOptionsAutoTurrets AutoTurret { get; set; } = new();

            internal Rewards GetRewards(EventType type) => type switch
            {
                EventType.Base => BaseRewards,
                EventType.LegacyShelter => LegacyShelterRewards,
                EventType.Tugboat or _ => TugboatRewards,
            };

            [JsonProperty(PropertyName = en ? "Banned Players" : "Banned Players")]
            public BannedSettings Banned { get; set; } = new();

            [JsonProperty(PropertyName = en ? "Rewards" : "Награды")]
            public Rewards BaseRewards { get; set; } = new() { OnEntityDeath = true, OnEntityTakeDamage = true, OnLootEntity = true };

            [JsonProperty(PropertyName = en ? "Rewards (Legacy Shelter)" : "Награды (Legacy Shelter)")]
            public Rewards LegacyShelterRewards { get; set; } = new();

            [JsonProperty(PropertyName = en ? "Rewards (Tugboat)" : "Награды (Tugboat)")]
            public Rewards TugboatRewards { get; set; } = new();

            [JsonProperty(PropertyName = en ? "Blacklisted Commands" : "Запрещенные Команды", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> BlacklistedCommands { get; set; } = DefaultBlacklistCommands();

            [JsonProperty(PropertyName = "LimitEntities (Plugin)")]
            public EntityLimitSettings LimitEntities { get; set; } = new();

            [JsonProperty(PropertyName = en ? "Entities Not Allowed To Be Picked Up" : "Сущности, Которые Нельзя Подбирать", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> BlacklistedPickupItems { get; set; } = new();

            [JsonProperty(PropertyName = en ? "Ignored Prefabs" : "Игнорируемые Префабы", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> IgnoredPrefabs { get; set; } = new() { "sleepingbag_leather_deployed", "bed_deployed", "bucket_lift", "cave_lift" };

            [JsonProperty(PropertyName = en ? "BotSpawn Profile Names" : "Имена Профилей BotSpawn", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> BotSpawnProfileNames { get; set; } = new() { "profile_name_1", "profile_name_2" };

            [JsonProperty(PropertyName = "Legacy Shelter")]
            public ShelterSettings Shelters { get; set; } = new();

            [JsonProperty(PropertyName = en ? "Tugboats" : "Буксиры")]
            public TugboatSettings Tugboats { get; set; } = new();

            [JsonProperty(PropertyName = en ? "Custom Cost To Manually Convert (0 = disabled)" : "Пользовательская Стоимость Для Ручной Конверсии (0 = отключено)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<CustomCostOptions> Custom { get; set; } = DefaultCustomCost;

            [JsonProperty(PropertyName = en ? "Custom Cost To Cancel Conversion (0 = disabled)" : "Пользовательская Стоимость Для Отмены Конверсии (0 = отключено)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<CustomCostOptions> CustomCancel { get; set; } = DefaultCustomCost;

            [JsonProperty(PropertyName = en ? "Economics Cost To Manually Convert (0 = disabled)" : "Стоимость По Экономике Для Ручной Конверсии (0 = отключено)")]
            public double Economics { get; set; }

            [JsonProperty(PropertyName = en ? "Economics Cost To Cancel Conversion (0 = disabled)" : "Стоимость По Экономике Для Отмены Конверсии (0 = отключено)")]
            public double EconomicsCancel { get; set; }

            [JsonProperty(PropertyName = en ? "ServerRewards Cost To Manually Convert (0 = disabled)" : "Стоимость ServerRewards Для Ручной Конверсии (0 = отключено)")]
            public int ServerRewards { get; set; }

            [JsonProperty(PropertyName = en ? "ServerRewards Cost To Cancel Conversion (0 = disabled)" : "Стоимость ServerRewards Для Отмены Конверсии (0 = отключено)")]
            public int ServerRewardsCancel { get; set; }

            [JsonProperty(PropertyName = en ? "Cancel Automated Events If Abandoned Owner Comes Online" : "Отмена Автоматизированных Событий Если Владелец Покинул Сервер Вернется В Сеть")]
            public bool CancelAutomatedEvent { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Force Time In Dome To (requires abandonedbases.time)" : "Принудительно установить время в куполе на (требуется abandonedbases.time)")]
            public int ForcedTime = -1;

            [JsonProperty(PropertyName = en ? "Allow Teleport" : "Разрешить Телепортацию")]
            public bool AllowTeleport { get; set; }

            [JsonProperty(PropertyName = en ? "Briefly Holster Weapon To Prevent Camping The Entrance Of Events" : "Кратковременно уберите оружие в кобуру, чтобы предотвратить кемпинг у входа на мероприятия")]
            public bool Holster { get; set; }

            [JsonProperty(PropertyName = en ? "Allow PVP" : "Разрешить PVP")]
            public bool AllowPVP { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Allow PVP (when manually converted with SAR command)" : "Разрешить PVP (при ручной конверсии с командой SAR)")]
            public bool? AllowPVPSAR { get; set; } = null;

            [JsonProperty(PropertyName = en ? "Allow PVP (when manually converted with attack permission)" : "Разрешить PVP (при ручной конверсии с разрешением на атаку)")]
            public bool? AllowPVPAttack { get; set; } = null;

            [JsonProperty(PropertyName = en ? "Allow Players To Build" : "Разрешить Игрокам Строить")]
            public bool AllowBuilding { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Allow Players To Build Ladders" : "Разрешить Игрокам Строить Лестницы")]
            public bool AllowLadders { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Allow Players To Use MLRS" : "Разрешить Игрокам Использовать MLRS")]
            public bool MLRS { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Minimum Required Players Online" : "Минимальное Требуемое Количество Игроков Онлайн")]
            public int MinimumOnlinePlayers { get; set; } = 1;

            [JsonProperty(PropertyName = en ? "Block Tool Cupboard Auth Friend" : "Заблокировать Tool Cupboard Авторизовать Друг")]
            public bool BlockToolCupboardAuthFriend { get; set; }

            [JsonProperty(PropertyName = en ? "Block Damage From Outside To Base" : "Блокировка Урона Снаружи Базы")]
            public bool BlockOutsideDamageBase { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Block Damage From Outside To Player" : "Блокировать урон игроку извне")]
            public bool BlockOutsideDamagePlayer { get; set; }

            [JsonProperty(PropertyName = en ? "Block RevivePlayer Plugin For PVP Bases" : "Блокировка Плагина RevivePlayer Для Баз PVP")]
            public bool BlockRevivePVP { get; set; }

            [JsonProperty(PropertyName = en ? "Block RevivePlayer Plugin For PVE Bases" : "Блокировка Плагина RevivePlayer Для Баз PVE")]
            public bool BlockRevivePVE { get; set; }

            [JsonProperty(PropertyName = en ? "Block RestoreUponDeath Plugin For PVP Bases" : "Блокировка Плагина RestoreUponDeath Для Баз PVP")]
            public bool BlockRestorePVP { get; set; }

            [JsonProperty(PropertyName = en ? "Block RestoreUponDeath Plugin For PVE Bases" : "Блокировка Плагина RestoreUponDeath Для Баз PVE")]
            public bool BlockRestorePVE { get; set; }

            [JsonProperty(PropertyName = en ? "Block RestoreUponDeath Plugin For Sleepers" : "Блокировка Плагина RestoreUponDeath Для Спящих")]
            public bool BlockRestoreSleepers { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Building Blocks Are Immune To Damage" : "Блоки Строительства Невосприимчивы К Урону")]
            public bool BlocksImmune { get; set; }

            [JsonProperty(PropertyName = en ? "Building Blocks Are Immune To Damage (Twig Only)" : "Блоки Строительства Невосприимчивы К Урону (Только Twig)")]
            public bool TwigImmune { get; set; }

            [JsonProperty(PropertyName = en ? "Mounts Can Take Damage From Players (PVP)" : "Транспортные Средства Могут Получать Урон от Игроков (PVP)")]
            public bool MountDamageFromPlayersPVP { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Mounts Can Take Damage From Players (PVE)" : "Транспортные Средства Могут Получать Урон от Игроков (PVE)")]
            public bool MountDamageFromPlayersPVE { get; set; }

            [JsonProperty(PropertyName = en ? "Prevent Players From Hogging Raids" : "Предотвратить Захват Рейдов Игроками")]
            public bool PreventHogging { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Prevent Ally From Hogging Raids" : "Предотвратить Захват Рейдов Союзниками")]
            public bool PreventAllyHogging { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Prevent Hogging Ignored During Purge" : "Игнорирование Захвата Во Время Чистки")]
            public bool IgnorePurgeHogging { get; set; }

            [JsonProperty(PropertyName = en ? "Cooldown Between Conversions" : "Перерыв Между Конверсиями")]
            public float CooldownBetweenConversion { get; set; } = 3600f;

            [JsonProperty(PropertyName = en ? "Cooldown Between Cancel" : "Перерыв Между Отменами")]
            public float CooldownBetweenCancel { get; set; } = 3600f;

            [JsonProperty(PropertyName = en ? "Cooldown Between Events" : "Перерыв Между Событиями")]
            public float CooldownBetweenEvents { get; set; } = 3600f;

            [JsonProperty(PropertyName = en ? "Cooldown Between Events Resets When Given Rewards" : "Перезарядка между событиями сбрасывается после получения награды.")]
            public bool ResetEventCooldownInRewards;

            [JsonProperty(PropertyName = en ? "Cooldown Between Events Blocks Joining Ally" : "Перерыв Между Событиями мешает присоединиться к союзнику")]
            public bool NoBypassCooldownBetweenEvents { get; set; }

            [JsonProperty(PropertyName = en ? "Cooldown Between Events Blocks Base Damage" : "Перерыв Между Событиями Блокирует Урон (Base)")]
            public bool CooldownBetweenEventsBlocksBaseDamage { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Cooldown Between Events Blocks PVP Damage" : "Перерыв Между Событиями Блокирует Урон (PVP)")]
            public bool CooldownBetweenEventsBlocksPVPDamage { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Cooldown Between Conversions Ignored During Purge" : "Игнорирование Перерыва Между Конверсиями Во Время Чистки")]
            public bool IgnorePurgeConversionCooldown { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Cooldown Between Cancel Ignored During Purge" : "Игнорирование Перерыва Между Отменами Во Время Чистки")]
            public bool IgnorePurgeCancelCooldown { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Cooldown Between Events Ignored During Purge" : "Игнорирование Перерыва Между Событиями Во Время Чистки")]
            public bool IgnorePurgeEventCooldown { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Destroy Marker Upon Event Completion" : "Уничтожьте маркер после завершения события")]
            public bool DestroyMarkerUponCompletion;

            [JsonProperty(PropertyName = en ? "Reward Players Upon Event Completion" : "Награды игрокам будут выданы после завершения мероприятия")]
            public bool RewardsUponCompletion;

            [JsonProperty(PropertyName = en ? "Marker Name (Minutes)" : "Название Маркера (Минуты)")]
            public string MarkerShopName { get; set; } = "{PVX} Abandoned Player Base [{time}m]";

            [JsonProperty(PropertyName = en ? "Marker Name (Seconds)" : "Название Маркера (Секунды)")]
            public string MarkerShopNameSeconds { get; set; } = "{PVX} Abandoned Player Base [{time}s]";

            [JsonProperty(PropertyName = en ? "Marker Format With Owner Name" : "Формат Маркера С Именем Владельца")]
            public string MarkerNameOwnerFormat { get; set; } = "[Owner] {0} {1}";

            [JsonProperty(PropertyName = en ? "Marker Format With Raider Name" : "Формат Маркера С Именем Рейдера")]
            public string MarkerNameRaiderFormat { get; set; } = "[Raider] {0} {1}";

            [JsonProperty(PropertyName = en ? "Show Owners Name On Map Marker" : "Показывать Имя Владельца На Маркере Карты")]
            public bool ShowOwnersName { get; set; }

            [JsonProperty(PropertyName = en ? "Show Raiders Name On Map Marker" : "Показывать Имя Рейдера На Маркере Карты")]
            public bool ShowRaidersName { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Loot Required" : "Требуется Добыча")]
            public int BaseLoot { get; set; }

            [JsonProperty(PropertyName = en ? "Loot Required (SAR)" : "Требуется Добыча (SAR)")]
            public int BaseLootSAR { get; set; } = int.MaxValue;

            [JsonProperty(PropertyName = en ? "Foundations Required" : "Требуемое Количество Фундаментов")]
            public int FoundationLimit { get; set; } = 4;

            [JsonProperty(PropertyName = en ? "Foundations Required (SAR)" : "Требуемое Количество Фундаментов (SAR)")]
            public int FoundationLimitSAR { get; set; } = int.MaxValue;

            [JsonProperty(PropertyName = en ? "Walls Required" : "Требуемое Количество Стен")]
            public int WallLimit { get; set; } = 3;

            [JsonProperty(PropertyName = en ? "Walls Required (SAR)" : "Требуемое Количество Стен (SAR)")]
            public int WallLimitSAR { get; set; } = int.MaxValue;

            [JsonProperty(PropertyName = en ? "Include Twig Structures" : "Включить Структуры Из Twig")]
            public bool Twig { get; set; }

            [JsonProperty(PropertyName = en ? "Sphere Amount" : "Количество Сфер")]
            public int SphereAmount { get; set; } = 10;

            [JsonProperty(PropertyName = en ? "Sphere Radius" : "Радиус Сферы")]
            public float SphereRadius { get; set; } = 50f;

            [JsonProperty(PropertyName = en ? "Sphere Radius (Legacy Shelter)" : "Радиус Сферы (Legacy Shelter)")]
            public float ShelterSphereRadius { get; set; } = 25f;

            [JsonProperty(PropertyName = en ? "Use Dynamic Sphere Radius" : "Использовать Динамический Радиус Сферы")]
            public bool Dynamic { get; set; }

            [JsonProperty(PropertyName = en ? "Max Dynamic Radius" : "Максимальный Динамический Радиус")]
            public float MaxDynamicRadius { get; set; } = 75f;

            [JsonProperty(PropertyName = en ? "Dynamic Scan Radius" : "Радиус динамического сканирования")]
            public float MaxDynamicScanRadius { get; set; } = 50f;

            [JsonProperty(PropertyName = en ? "Padding Added Onto Dynamic Radius" : "Добавленная Величина К Динамическому Радиусу")]
            public float Padding { get; set; } = 9f;

            [JsonProperty(PropertyName = en ? "Min Custom Sphere Radius" : "Минимальный Пользовательский Радиус Сферы")]
            public float MinCustomSphereRadius { get; set; } = 25f;

            [JsonProperty(PropertyName = en ? "Max Custom Sphere Radius" : "Максимальный Пользовательский Радиус Сферы")]
            public float MaxCustomSphereRadius { get; set; } = 75f;

            [JsonProperty(PropertyName = en ? "Seconds Until Event Can Be Canceled" : "Секунды До Возможности Отмены События")]
            public float CancelCooldown { get; set; }

            [JsonProperty(PropertyName = en ? "PVP Delay" : "Задержка PVP")]
            public float PVPDelay { get; set; } = 15f;

            [JsonProperty(PropertyName = en ? "Players With PVP Delay Can Damage Anything Inside Zone" : "Игроки с Задержкой PVP Могут Наносить Урон Любому Объекту в Зоне")]
            public bool PVPDelayDamageInside;

            [JsonProperty(PropertyName = en ? "Players With PVP Delay Can Damage Other Players With PVP Delay Anywhere" : "Игроки с Задержкой PVP Могут Везде Наносить Урон Другим Игрокам с Задержкой PVP")]
            public bool PVPDelayAnywhere;

            [JsonProperty(PropertyName = en ? "Despawn Abandoned Vehicles" : "Удалять Транспортные Средства при Деспавне")]
            public bool DespawnMounts = true;

            [JsonProperty(PropertyName = en ? "Despawn Timer" : "Таймер Исчезновения")]
            public float DespawnSecondsInactiveAuto { get; set; } = 1800f;

            [JsonProperty(PropertyName = en ? "Despawn Timer (SAR)" : "Таймер Исчезновения (SAR)")]
            public float? DespawnSecondsInactiveSAR { get; set; } = null;

            [JsonProperty(PropertyName = en ? "Despawn Timer Resets When Base Is Attacked" : "Сброс Таймера Исчезновения При Атаке Базы")]
            public bool DespawnSecondsInactiveReset { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Seconds Until Despawn After Looting" : "Секунд До Исчезновения После Обыска")]
            public float DespawnSecondsLooted { get; set; } = 600f;

            [JsonProperty(PropertyName = en ? "Seconds Until Despawn After Looting Resets When Damaged" : "Сброс Секунд До Исчезновения После Обыска При Повреждении")]
            public bool DespawnSecondsReset { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Do Not Destroy Base Unless All Cupboards Are Destroyed" : "Не Уничтожать Базу, Если Все Инструментальные Шкафы Не Уничтожены")]
            public bool DestroyRequiresCupboards { get; set; }

            [JsonProperty(PropertyName = en ? "Do Not Destroy Base When Despawn Timer Expires" : "Не Уничтожать Базу При Истечении Таймера Исчезновения")]
            public bool DoNotDestroy { get; set; }

            [JsonProperty(PropertyName = en ? "Do Not Destroy Manually Converted Base When Despawn Timer Expires" : "Не Уничтожать Вручную Конвертированную Базу При Истечении Таймера Исчезновения")]
            public bool DoNotDestroyManual { get; set; }

            [JsonProperty(PropertyName = en ? "Do Not Destroy During Purge" : "Не Уничтожать Во Время События Пурж")]
            public bool DoNotDestroyPurge { get; set; }

            [JsonProperty(PropertyName = en ? "Time To Wait Between Spawns" : "Время Ожидания Между Появлениями")]
            public float WaitTime { get; set; } = 15f;

            [JsonProperty(PropertyName = en ? "Use Map Marker For Automatic" : "Использовать Маркер На Карте Для Автоматического")]
            public bool AutoMarkers { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Use Map Marker For Manual" : "Использовать Маркер На Карте Для Ручного")]
            public bool ManualMarkers { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Map Marker Radius" : "Радиус Маркера На Карте")]
            public float MarkerRadius { get; set; } = 0.25f;

            [JsonProperty(PropertyName = en ? "Map Marker Radius (Map Size 3600 Or Less)" : "Радиус Маркера На Карте (Размер Карты 3600 Или Меньше)")]
            public float MarkerSubRadius { get; set; } = 0.4f;

            [JsonProperty(PropertyName = en ? "Allow Manually Converted Bases To Be Claimed" : "Разрешить Претендовать На Вручную Конвертированные Базы")]
            public bool AllowManualClaims { get; set; }

            [JsonProperty(PropertyName = en ? "Require Event Be Finished Before It Can Be Canceled" : "Требовать Завершения События Перед Отменой")]
            public bool RequireEventFinished { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Require Cupboard Access To Claim" : "Требовать Доступа К Шкафу Для Претензии")]
            public bool ClaimRequiresCupboardAccess { get; set; }

            [JsonProperty(PropertyName = en ? "Only Cupboards Are Required To Cancel An Event" : "Только Шкафы Необходимы Для Отмены События")]
            public bool OnlyCupboardsAreRequired { get; set; }

            [JsonProperty(PropertyName = en ? "Only Primary Cupboard Is Required To Cancel An Event" : "Для отмены события требуется только основной шкаф")]
            public bool OnlyPrimaryCupboardIsRequired { get; set; }

            [JsonProperty(PropertyName = en ? "Check If Abandoned Bases Are Too Close Together" : "Проверить, Не Слишком Ли Близко Расположены Брошенные Базы")]
            public bool TooClose { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Remove Admins From Raiders List" : "Исключить Админов Из Списка Рейдеров")]
            public bool RemoveAdminRaiders { get; set; }

            [JsonProperty(PropertyName = en ? "Change Marker Color On First Entity Destroyed" : "Изменить Цвет Маркера При Уничтожении Первого Объекта")]
            public bool ChangeColor { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Changed Marker Color" : "Измененный Цвет Маркера")]
            public string ChangedMarkerColor { get; set; } = "#800080";

            [JsonProperty(PropertyName = en ? "Default Marker Color" : "Цвет Маркера По Умолчанию")]
            public string DefaultMarkerColor { get; set; } = "#FF00FF";

            [JsonProperty(PropertyName = en ? "Lock Base To First Attacker (PVE)" : "Заблокировать Базу Для Первого Атакующего (PVE)")]
            public bool LockBaseToFirstAttackerPVE { get; set; }

            [JsonProperty(PropertyName = en ? "Lock Base To First Attacker (PVP)" : "Заблокировать Базу Для Первого Атакующего (PVP)")]
            public bool LockBaseToFirstAttackerPVP { get; set; }

            [JsonProperty(PropertyName = en ? "[Experimental] Lock Base Expires After Inactive Time (Minutes)" : "[Experimental] Блокировка Базы Истекает После Времени Бездействия (в Минутах)")]
            public float InactivePlayerActivityTime;

            [JsonProperty(PropertyName = en ? "Eject Enemies From Locked Raids (PVE)" : "Вытеснить Врагов Из Заблокированных Рейдов (PVE)")]
            public bool EjectLockedPVE { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Eject Enemies From Locked Raids (PVP)" : "Вытеснить Врагов Из Заблокированных Рейдов (PVP)")]
            public bool EjectLockedPVP { get; set; }

            [JsonProperty(PropertyName = en ? "Eject Players When Entering Dome On Event Cooldown (PVE)" : "Вытеснить Игроков При Входе В Сферу С Перезарядкой События (PVE)")]
            public bool EjectEventCooldownPVE { get; set; }

            [JsonProperty(PropertyName = en ? "Eject Players When Entering Dome On Event Cooldown (PVP)" : "Вытеснить Игроков При Входе В Сферу С Перезарядкой События (PVP)")]
            public bool EjectEventCooldownPVP { get; set; }

            [JsonProperty(PropertyName = en ? "Backpacks Can Be Opened" : "Рюкзаки Могут Быть Открыты")]
            public bool Backpacks { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Rust Backpacks Drop At PVE Bases" : "(Rust) Рюкзаки выпадают на PVE базах")]
            public bool RustBackpacksPVE { get; set; }

            [JsonProperty(PropertyName = en ? "Rust Backpacks Drop At PVP Bases" : "(Rust) Рюкзаки выпадают на PVP базах")]
            public bool RustBackpacksPVP { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Backpacks Plugin Drops At PVE Bases" : "Падение Рюкзаков От Плагина На Базах PVE")]
            public bool BackpacksPVE { get; set; }

            [JsonProperty(PropertyName = en ? "Backpacks Plugin Drops At PVP Bases" : "Падение Рюкзаков От Плагина На Базах PVP")]
            public bool BackpacksPVP { get; set; }

            [JsonProperty(PropertyName = en ? "Players Can Loot Alive Players (PVP)" : "Игроки могут лутать живых игроков (PVP)")]
            public bool CanLootPlayerAlive { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Players Cannot Loot Wounded Players" : "Игроки Не Могут Обыскивать Раненых Игроков")]
            public bool CannotLootWoundedPlayers { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Corpses Can Be Looted By Anyone (PVE)" : "Трупы Могут Обыскивать Все (PVE)")]
            public bool CorpsesLootedPVE { get; set; }

            [JsonProperty(PropertyName = en ? "Corpses Can Be Looted By Anyone (PVP)" : "Трупы Могут Обыскивать Все (PVP)")]
            public bool CorpsesLootedPVP { get; set; } = true;

            [JsonProperty(PropertyName = "Abandoned Privilege Ui")]
            public AbandonedPrivilegeUi Privilege { get; set; } = new();

            internal bool EjectFromEvent(bool pvp) => pvp ? EjectEventCooldownPVP : EjectEventCooldownPVE;

            public float GetDespawnSeconds(bool auto) => auto || !DespawnSecondsInactiveSAR.HasValue ? DespawnSecondsInactiveAuto : DespawnSecondsInactiveSAR.Value;
        }

        public class AbandonedPrivilegeUi
        {
            [JsonProperty(PropertyName = en ? "Show Time Limit UI When TC Is Accessed" : "Показывать интерфейс ограничения по времени при доступе к TC")]
            public bool Ui { get; set; }

            [JsonProperty(PropertyName = "Do not calculate your time when using noclip or vanish")]
            public bool SkipCheats { get; set; } = true;

            [JsonProperty(PropertyName = "Text Color")]
            public string TextColor { get; set; } = "#FFFFFF";

            [JsonProperty(PropertyName = "Text Alpha")]
            public float Alpha { get; set; } = 1f;

            [JsonProperty(PropertyName = "Offset Min")]
            public string OffsetMin { get; set; } = "-473.401 -269.437";

            [JsonProperty(PropertyName = "Offset Max")]
            public string OffsetMax { get; set; } = "-261.599 -253.963";

            internal static double ParseHexToDecimal(string hex, int j, int k) => int.TryParse(hex.TrimStart('#').Substring(j, k), NumberStyles.AllowHexSpecifier, NumberFormatInfo.CurrentInfo, out var num) ? num : 1;

            internal static string ConvertHexToRGBA(string hex, float a) => $"{ParseHexToDecimal(hex, 0, 2) / 255} {ParseHexToDecimal(hex, 2, 2) / 255} {ParseHexToDecimal(hex, 4, 2) / 255} {Mathf.Clamp(a, 0f, 1f)}";
        }

        public class SarSettings
        {
            [JsonProperty(PropertyName = en ? "Require confirmation code to use SAR command" : "Требуется код подтверждения для команды SAR.")]
            public bool Confirm { get; set; } = true;

            [JsonProperty(PropertyName = "Show despawn timer warning message when requiring confirmation")]
            public bool Delete { get; set; } = true;

            [JsonProperty(PropertyName = "Show reset despawn timer message when requiring confirmation")]
            public bool Reset { get; set; } = true;
        }

        public class EntityLimitSettings
        {
            [JsonProperty(PropertyName = "Enabled")]
            public bool Enabled { get; set; }

            [JsonProperty(PropertyName = en ? "Kill deployed entity when claiming a base that exceeds the limit" : "Уничтожить развернутый объект, когда достигнут лимит и происходит претензия на базу")]
            public bool KillDeployables { get; set; }

            [JsonProperty(PropertyName = en ? "Store limited entities in a box when claiming a base" : "Хранить сущности в коробке при достижении лимита и претензии на базу")]
            public bool CrateDeployables { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Always store deployables in a box when claiming a base" : "Всегда помещайте развертываемые предметы в коробку при претензии на базу")]
            public bool Always { get; set; }
        }

        public class ShelterSettings
        {
            [JsonProperty(PropertyName = en ? "Unlock Instead Of Destroying During Scans" : "Разблокировать Вместо Уничтожения Во Время Сканирования")]
            public bool Unlock { get; set; }

            [JsonProperty(PropertyName = en ? "Loot Required" : "Требуется Добыча")]
            public int Loot { get; set; } = 6;

            [JsonProperty(PropertyName = en ? "Loot Required (SAR)" : "Требуется Добыча (SAR)")]
            public int LootSAR { get; set; } = int.MaxValue;

            [JsonProperty(PropertyName = en ? "Allow Claim Of Unlocked Shelters" : "Разрешить право на SAR на все разблокированные Legacy Shelters")]
            public bool Claim { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Allow Conversion With SAR" : "Разрешить преобразование с SAR")]
            public bool Convert { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Block Conversions Within X Meters Of Enemy Player" : "Блокировать конверсии в пределах X метров от вражеского игрока")]
            public float BlockRadius { get; set; } = 150f;

            [JsonProperty(PropertyName = en ? "Manual Conversions Only" : "Только Ручные Конверсии")]
            public bool ManualOnly { get; set; }

            internal bool CanClaim(BasePlayer player, out LegacyShelter shelter)
            {
                shelter = null;
                return Claim && GetLegacyShelter(player.transform.position, out shelter) && IsLegacyShelterUnlocked(shelter) && shelter.GetEntityBuildingPrivilege() is SimplePrivilege priv1 && !priv1.AnyAuthed();
            }

            internal bool IsAuthed(BasePlayer player, LegacyShelter shelter) => IsLegacyShelterUnlocked(shelter) || shelter.GetEntityBuildingPrivilege() is SimplePrivilege priv && priv.IsAuthed(player);

            internal bool IsBlocked(BasePlayer player, Func<ulong, ulong, bool> isAlly) => BlockRadius > 0 && BasePlayer.activePlayerList.Exists(x => !x.IsKilled() && x != player && x.Distance(player) <= BlockRadius && !isAlly(x.userID, player.userID));
        }

        public class TugboatSettings
        {
            [JsonProperty(PropertyName = en ? "Leaving Tugboat Triggers PVP Delay" : "Покидание Буксира Включает Задержку PVP")]
            public bool Delay { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Unlock Instead Of Destroying During Scans" : "Разблокировать Вместо Уничтожения Во Время Сканирования")]
            public bool Unlock { get; set; }

            [JsonProperty(PropertyName = en ? "Prevent Engine Starting During An Event" : "Предотвратить Запуск Двигателя Во Время События")]
            public bool Engine { get; set; }

            [JsonProperty(PropertyName = en ? "Loot Required" : "Требуется Добыча")]
            public int Loot { get; set; } = 6;

            [JsonProperty(PropertyName = en ? "Loot Required (SAR)" : "Требуется Добыча (SAR)")]
            public int LootSAR { get; set; } = int.MaxValue;

            [JsonProperty(PropertyName = en ? "Manual Conversions Only" : "Только Ручные Конверсии")]
            public bool ManualOnly { get; set; }
        }

        public class Rewards
        {
            [JsonProperty(PropertyName = en ? "Economics Money" : "Деньги Economics")]
            public double Money { get; set; }

            [JsonProperty(PropertyName = en ? "ServerRewards Points" : "Очки ServerRewards")]
            public int Points { get; set; }

            [JsonProperty(PropertyName = en ? "SkillTree XP" : "Опыт SkillTree")]
            public double XP { get; set; }

            [JsonProperty(PropertyName = en ? "Do Not Reward Canceled Events" : "Не Награждать За Отмененные События")]
            public bool Cancel { get; set; }

            [JsonProperty(PropertyName = en ? "Divide Rewards Among All Raiders" : "Разделить Награды Между Всеми Рейдерами")]
            public bool DivideRewards { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Priority Looting Earns Participation (TC, Box)" : "Приоритетный грабёж приносит участие (TC, Box)", NullValueHandling = NullValueHandling.Ignore)]
            public bool? OnLootEntity { get; set; }

            [JsonProperty(PropertyName = en ? "Priority Kills Earns Participation (TC, Box, Building Block)" : "Приоритетное убийство приносит участие (TC, Box, Building Block)", NullValueHandling = NullValueHandling.Ignore)]
            public bool? OnEntityDeath { get; set; }

            [JsonProperty(PropertyName = en ? "Priority Damage Earns Participation (TC, Box, Building Block)" : "Урон с приоритетом приносит участие (TC, Box, Building Block)", NullValueHandling = NullValueHandling.Ignore)]
            public bool? OnEntityTakeDamage { get; set; }
        }

        public class PurgeSettings
        {
            [JsonProperty(PropertyName = en ? "Permission" : "Разрешение")]
            public string Permission { get; set; } = "";

            [JsonProperty(PropertyName = en ? "Lifetime (Days)" : "Срок Службы (Дни)")]
            public string LifetimeRaw { get; set; } = "none";

            [JsonProperty(PropertyName = en ? "Conversions Before Destroying Base" : "Конверсии Перед Уничтожением Базы")]
            public int Limit { get; set; } = 1;

            [JsonIgnore]
            public double Lifetime = 0;

            [JsonIgnore]
            public bool NoPurge { get; set; } = false;

            internal static bool IsImmune(Configuration config, ulong userid)
            {
                return config.Purges.Exists(purge => purge.NoPurge && userid.HasPermission(purge.Permission));
            }

            internal static PurgeSettings Find(Configuration config, ulong userid)
            {
                if (!userid.IsSteamId())
                {
                    return null;
                }

                PurgeSettings best = null;

                foreach (var purge in config.Purges)
                {
                    if (!userid.HasPermission(purge.Permission))
                    {
                        continue;
                    }

                    if (purge.NoPurge)
                    {
                        return purge;
                    }

                    if (best == null || best.Lifetime < purge.Lifetime)
                    {
                        best = purge;
                    }
                }

                return best;
            }
        }

        public class UIAdvancedAlertSettings
        {
            [JsonProperty(PropertyName = en ? "Enabled" : "Включено")]
            public bool Enabled { get; set; } = true;

            [JsonProperty(PropertyName = "Anchor Min")]
            public string AnchorMin { get; set; } = "0.35 0.85";

            [JsonProperty(PropertyName = "Anchor Max")]
            public string AnchorMax { get; set; } = "0.65 0.95";

            [JsonProperty(PropertyName = en ? "Time Shown" : "Время показано")]
            public float Time { get; set; } = 5f;
        }

        public class ConfigurationNotifications
        {
            [JsonProperty(PropertyName = "Advanced Alerts UI")]
            public UIAdvancedAlertSettings AA { get; set; } = new();

            [JsonProperty(PropertyName = en ? "Notify Plugin - Type (-1 = disabled)" : "Notify Plugin - Тип (-1 = отключено)")]
            public int NotifyType { get; set; }

            [JsonProperty(PropertyName = en ? "UI Popup Interval" : "UI Popup Интервал")]
            public float Interval { get; set; } = 1f;

            [JsonProperty(PropertyName = en ? "Send Messages To Player" : "Отправлять Сообщения Игроку")]
            public bool Message { get; set; } = true;

            [JsonProperty(PropertyName = "Send Base Is Active Message")]
            public bool BaseIsActive { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Send Global Message When Players Claim A Base" : "Отправлять Глобальное Сообщение Когда Игроки Захватывают Базу")]
            public bool Global { get; set; }

            [JsonProperty(PropertyName = en ? "Message Raiders When An Event Is Completed" : "Сообщить Рейдерам При Завершении События")]
            public bool RaidCompletion { get; set; }

            [JsonProperty(PropertyName = en ? "Message Raiders When Event Ends During Automated Cancellation" : "Сообщить Рейдерам При Окончании События Во Время Автоматической Отмены")]
            public bool CancelAutomatedEvent { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Message Players Within X Meters When An Event Is Completed" : "Сообщить Игрокам В Радиусе X Метров При Завершении События")]
            public float LocalCompletion { get; set; } = 8000f;

            [JsonProperty(PropertyName = en ? "Message Entering Players When They Have An Event Cooldown" : "Сообщение игрокам о наличии у них ограничения на событие (кд)")]
            public bool HasEventCooldown { get; set; } = true;
        }

        internal class DiscordSettings
        {
            [JsonProperty(PropertyName = "Webhook URL")]
            public string Webhook = "https://support.discordapp.com/hc/en-us/articles/228383668-Intro-to-Webhooks";

            [JsonProperty(PropertyName = "On Event Start")]
            public bool Start;

            [JsonProperty(PropertyName = "On Event End")]
            public bool End;

            [JsonProperty(PropertyName = "On Event Claim")]
            public bool Claim;

            [JsonProperty(PropertyName = "On Event Cancel")]
            public bool Cancel;

            [JsonProperty(PropertyName = "On Event Unlock")]
            public bool Unlock;
        }

        internal class Configuration
        {
            [JsonProperty(PropertyName = en ? "Purge Settings" : "Настройки Purge", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<PurgeSettings> Purges { get; set; } = DefaultPurgeSettings();

            [JsonProperty(PropertyName = en ? "Abandoned Settings" : "Настройки Abandoned")]
            public AbandonedSettings Abandoned { get; set; } = new();

            [JsonProperty(PropertyName = en ? "Messages" : "Сообщения")]
            public ConfigurationNotifications Messages { get; set; } = new();

            [JsonProperty(PropertyName = "Discord Messages Plugin Settings")]
            public DiscordSettings Discord { get; set; } = new();

            [JsonProperty(PropertyName = en ? "Run Once On Server Startup" : "Запустить Один Раз При Запуске Сервера")]
            public bool Startup { get; set; }

            [JsonProperty(PropertyName = en ? "Run Every X Seconds" : "Запускать Каждые X Секунд")]
            public float Delay { get; set; } = 3600;

            [JsonProperty(PropertyName = en ? "Time Between Attack Messages (abandonedbases.attack)" : "Время между сообщениями об атаках (abandonedbases.attack)")]
            public float WaitingListTime { get; set; } = 10f;

            [JsonProperty(PropertyName = en ? "Kill Inactive Sleepers" : "Убить Неактивных Спящих")]
            public bool KillInactiveSleepers { get; set; }

            [JsonProperty(PropertyName = en ? "Move Inventory To Boxes Before Kill Inactive Sleepers" : "Переместить Инвентарь В Ящики Перед Убийством Неактивных Спящих")]
            public bool MoveInventory { get; set; }

            [JsonProperty(PropertyName = en ? "Move Inventory Blacklist Shortnames" : "Черный Список Коротких Имен Для Перемещения Инвентаря", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> MoveInventoryBlacklist { get; set; } = new() { "rock", "torch" };

            [JsonProperty(PropertyName = en ? "Let Players Kill Abandoned Sleepers" : "Позволить Игрокам Убивать Брошенных Спящих")]
            public bool PlayersCanKillAbandonedSleepers { get; set; }

            [JsonProperty(PropertyName = en ? "Remove Ownership From Bases" : "Удалить Владение Базами")]
            public bool RemoveOwnership { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Remove Ownership From Containers" : "Удалить Владение Контейнерами")]
            public bool RemoveOwnershipFromContainers { get; set; } = true;

            [JsonProperty(PropertyName = en ? "Remove Ownership When Despawn Timer Is Zero" : "Удалить Владение Когда Таймер Исчезновения На Нуле")]
            public bool RemoveOwnershipZero { get; set; }

            [JsonProperty(PropertyName = en ? "Steam Chat ID" : "ID Чата Steam")]
            public ulong ChatID { get; set; } = 76561199564930233;

            [JsonProperty(PropertyName = en ? "Use Log File" : "Использовать Файл Журнала")]
            public bool UseLogFile { get; set; }

            [JsonProperty(PropertyName = en ? "Extended Distance To Spawn Away From Zone Manager Zones" : "Расширенное Расстояние Для Появления Вне Zone Manager Zones")]
            public float ZoneDistance { get; set; } = 25f;

            [JsonProperty(PropertyName = en ? "Allowed Zone Manager Zones" : "Разрешенные Zone Manager Zones", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> AllowedZones { get; set; } = new() { "pvp", "99999999" };

            internal List<string> PurgePermissions = new();
            internal List<string> NoPurgePermissions = new();

            public void Validate(AbandonedBases m)
            {
                var permission = m.permission;
                bool immune = false;
                foreach (var purge in Purges)
                {
                    if (!permission.PermissionExists(purge.Permission))
                    {
                        permission.RegisterPermission(purge.Permission, m);
                    }
                    if (double.TryParse(purge.LifetimeRaw, out purge.Lifetime) && purge.Lifetime > 0)
                    {
                        purge.Lifetime *= 86400;
                        PurgePermissions.Add(purge.Permission);
                    }
                    else
                    {
                        NoPurgePermissions.Add(purge.Permission);
                        purge.NoPurge = true;
                        immune = true;
                    }
                }
                if (!immune)
                {
                    if (!permission.PermissionExists("abandonedbases.immune"))
                    {
                        permission.RegisterPermission("abandonedbases.immune", m);
                    }
                    Purges.Add(new()
                    {
                        LifetimeRaw = "none",
                        NoPurge = true,
                        Permission = "abandonedbases.immune"
                    });
                }
                foreach (var value in Abandoned.BlacklistedCommands.ToList())
                {
                    if (value.StartsWith('/'))
                    {
                        Abandoned.BlacklistedCommands.Remove(value);
                        Abandoned.BlacklistedCommands.Add(value[1..]);
                    }
                }
                if (PurgePermissions.Count > 1)
                {
                    PurgePermissions.Sort();
                }
                m.SaveConfig();
            }
        }

        private bool allowSaveConfig = true;
        private const bool en = true;

        protected override void LoadConfig()
        {
            base.LoadConfig();
            allowSaveConfig = false;
            try
            {
                if (en && Config.Get("Настройки Abandoned") != null || !en && Config.Get("Abandoned Settings") != null)
                {
                    Puts("Creating backup of config file:");
                    Puts(Manager.ConfigPath + System.IO.Path.DirectorySeparatorChar + "AbandonedBases.backup");
                    Config.Save(Manager.ConfigPath + System.IO.Path.DirectorySeparatorChar + "AbandonedBases.backup");
                }
                config = Config.ReadObject<Configuration>();
                config ??= new();
                CheckConfig();
                allowSaveConfig = true;
                SaveConfig();
            }
            catch (JsonException ex)
            {
                Puts(ex.ToString());
                LoadDefaultConfig();
            }
            config.Abandoned.IgnoredPrefabs.RemoveAll(string.IsNullOrWhiteSpace);
        }

        private void CheckConfig()
        {
            foreach (var x in config.Abandoned.Disabled.ToList())
            {
                if (x.Start == "12:00" && x.End == "00:00")
                {
                    x.Start = "12:00";
                    x.End = "23:59";
                }
            }
            if (!config.Abandoned.AllowPVPSAR.HasValue)
            {
                config.Abandoned.AllowPVPSAR = config.Abandoned.AllowPVP;
            }
            if (!config.Abandoned.AllowPVPAttack.HasValue)
            {
                config.Abandoned.AllowPVPAttack = config.Abandoned.AllowPVP;
            }
            if (!config.Abandoned.DespawnSecondsInactiveSAR.HasValue)
            {
                config.Abandoned.DespawnSecondsInactiveSAR = config.Abandoned.DespawnSecondsInactiveAuto;
            }
            if (config.Abandoned.Privilege == null)
            {
                config.Abandoned.Privilege = new();
            }
            if (config.Abandoned.AutoTurret._Enabled.HasValue)
            {
                config.Abandoned.AutoTurret.Enabled = config.Abandoned.AutoTurret._Enabled.Value;
                config.Abandoned.AutoTurret._Enabled = null;
            }
            if (config.Abandoned.BaseLootSAR == int.MaxValue) config.Abandoned.BaseLootSAR = config.Abandoned.BaseLoot;
            if (config.Abandoned.WallLimitSAR == int.MaxValue) config.Abandoned.WallLimitSAR = config.Abandoned.WallLimit;
            if (config.Abandoned.Shelters.LootSAR == int.MaxValue) config.Abandoned.Shelters.LootSAR = config.Abandoned.Shelters.Loot;
            if (config.Abandoned.Tugboats.LootSAR == int.MaxValue) config.Abandoned.Tugboats.LootSAR = config.Abandoned.Tugboats.Loot;
            if (config.Abandoned.FoundationLimitSAR == int.MaxValue) config.Abandoned.FoundationLimitSAR = config.Abandoned.FoundationLimit;
            config.AllowedZones.RemoveAll(string.IsNullOrEmpty);
            config.Abandoned.Disabled.RemoveAll(x => x == null);
        }

        private List<string> _buildingBlocks = new()
        {
            "block.stair.lshape", "block.stair.lshape", "block.stair.spiral", "block.stair.spiral.triangle", "block.stair.ushape", "door.hinged.wood", "door.hinged.metal", "door.hinged.toptier", "door.double.hinged.wood", "door.double.hinged.stone", "door.double.hinged.toptier", "door.hinged.industrial.a", "door.hinged.industrial.d", "floor", "floor.frame", "floor.grill", "floor.triangle.grill", "floor.triangle.ladder", "floor.triangle", "floor.triangle.frame", "foundation", "foundation.steps", "foundation.triangle", "gates.external.high.wood", "gates.external.high.stone", "ramp", "roof", "roof.triangle", "shutter.metal.embrasure.a", "shutter.metal.embrasure.b", "shutter.wood.a", "wall.external.high.ice", "wall.external.high.stone", "wall.external.high.wood", "wall.frame.cell", "wall.frame.fence", "wall.frame.garagedoor", "wall.frame.netting", "wall.frame.shopfront", "wall.window.bars", "wall", "wall.doorway", "wall.frame", "wall.half", "wall.low", "wall.window"
        };

        protected override void SaveConfig()
        {
            if (allowSaveConfig)
            {
                Config.WriteObject(config);
            }
        }

        protected override void LoadDefaultConfig() => config = new();

        #endregion Configuration
    }
}

namespace Oxide.Plugins.AbandonedBasesExtensionMethods
{
    public static class ExtensionMethods
    {
        public static PooledList<T> TakePooledList<T>(this IEnumerable<T> a, int n) { var b = Facepunch.Pool.Get<PooledList<T>>(); if (a != null) { foreach (var d in a) { b.Add(d); if (b.Count >= n) { break; } } } return b; }
        public static PooledList<T> ToPooledList<T>(this IEnumerable<T> a) { var b = Facepunch.Pool.Get<PooledList<T>>(); if (a != null) b.AddRange(a); return b; }
        public static PooledList<Item> GetPlayerItems(this BasePlayer a) { var b = Facepunch.Pool.Get<PooledList<Item>>(); a?.inventory?.GetAllItems(b); return b; }
        internal static Core.Libraries.Permission _permission;
        internal static Core.Libraries.Permission permission { get { if (_permission == null) { _permission = Interface.Oxide.GetLibrary<Core.Libraries.Permission>(null); } return _permission; } set { _permission = value; } }
        public static bool All<T>(this IEnumerable<T> a, Func<T, bool> b) { bool any = false; foreach (T o in a) { any = true; if (!b(o)) { return false; } } return any; }
        public static bool Exists<T>(this IEnumerable<T> a, Func<T, bool> b = null) { using var c = a.GetEnumerator(); while (c.MoveNext()) { if (b == null || b(c.Current)) { return true; } } return false; }
        public static T ElementAt<T>(this IEnumerable<T> a, int b) { if (a is IList<T> c) { return c[b]; } using IEnumerator<T> d = a.GetEnumerator(); while (d.MoveNext()) { if (b == 0) { return d.Current; } b--; } return default; }
        public static T FirstOrDefault<T>(this IEnumerable<T> a, Func<T, bool> b = null) { using (var c = a.GetEnumerator()) { while (c.MoveNext()) { if (b == null || b(c.Current)) { return c.Current; } } } return default; }
        public static void ForEach<T>(this IEnumerable<T> a, Action<T> action) { foreach (T n in a) { action(n); } }
        public static IEnumerable<T> Intersect<T>(this IEnumerable<T> a, IEnumerable<T> b) { HashSet<T> c = new(b); foreach (var d in a) { if (c.Remove(d)) { yield return d; } } }
        public static IEnumerable<V> Select<T, V>(this IEnumerable<T> a, Func<T, V> b) { var c = new List<V>(); using (var d = a.GetEnumerator()) { while (d.MoveNext()) { c.Add(b(d.Current)); } } return c; }
        public static List<T> ToList<T>(this IEnumerable<T> a) => a == null ? new() : new(a);
        public static bool IsHuman(this BasePlayer a) { if (a.IsKilled() || !a.userID.IsSteamId()) { return false; } return true; }
        public static int RemoveAll<TKey, TValue>(this IDictionary<TKey, TValue> c, Func<TKey, TValue, bool> d) { int a = 0; using var t = c.ToPooledList(); foreach (var b in t) { if (d(b.Key, b.Value)) { c.Remove(b.Key); a++; } } return a; }
        public static List<T> OfType<T>(this IEnumerable<BaseNetworkable> a) where T : BaseEntity { var b = new List<T>(); using (var c = a.GetEnumerator()) { while (c.MoveNext()) { if (c.Current is T) { b.Add(c.Current as T); } } } return b; }
        public static int Sum<T>(this IList<T> a, Func<T, int> b) { int c = 0; for (int i = 0; i < a.Count; i++) { var d = b(a[i]); if (float.IsNaN(d)) { continue; } c += d; } return c; }
        public static List<T> Where<T>(this IEnumerable<T> a, Func<T, bool> b) { List<T> c = new(a is ICollection<T> n ? n.Count : 4); foreach (var d in a) { if (b(d)) { c.Add(d); } } return c; }
        public static bool HasPermission(this string a, string b) { return !string.IsNullOrEmpty(a) && permission.UserHasPermission(a, b); }
        public static bool HasPermission(this BasePlayer a, string b) { return a != null && a.userID.IsSteamId() && a.UserIDString.HasPermission(b); }
        public static bool HasPermission(this ulong a, string b) { return a.IsSteamId() && a.ToString().HasPermission(b); }
        public static bool PermissionExists(this string a) { return permission.PermissionExists(a); }
        public static bool IsNetworked(this BaseNetworkable a) { return !(a == null || a.IsDestroyed || a.net == null); }
        public static bool IsReallyConnected(this BasePlayer a) { return a.IsNetworked() && a.net.connection != null; }
        public static bool IsKilled(this BaseNetworkable a) => a == null || a.IsDestroyed || !a.IsFullySpawned();
        public static void SafelyKill(this BaseNetworkable a) { try { if (!a.IsKilled()) a.Kill(BaseNetworkable.DestroyMode.None); } catch { } }
        public static bool CanCall(this Plugin a) { return a != null && a.IsLoaded; }
        public static bool IsMajorityDamage(this HitInfo info, DamageType damageType) => info?.damageTypes?.GetMajorityDamageType() == damageType;
        public static BasePlayer ToPlayer(this IPlayer user) { return user?.Object as BasePlayer; }
        public static bool IsValid(this List<AbandonedBases.CustomCostOptions> options) => options != null && options.Exists(o => o.IsValid());
    }
}