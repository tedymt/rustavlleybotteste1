using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins.PveModeExtensionMethods;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("PveMode", "KpucTaJl", "1.2.7")]
    internal class PveMode : RustPlugin
    {
        #region Config
        private const bool En = true;

        private PluginConfig _config;

        protected override void LoadDefaultConfig()
        {
            Puts("Creating a default config...");
            _config = PluginConfig.DefaultConfig();
            _config.PluginVersion = Version;
            SaveConfig();
            Puts("Creation of the default config completed!");
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<PluginConfig>();
            if (_config.PluginVersion < Version) UpdateConfigValues();
        }

        private void UpdateConfigValues()
        {
            Puts("Config update detected! Updating config values...");
            _config.PluginVersion = Version;
            Puts("Config update completed!");
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        private class PluginConfig
        {
            [JsonProperty(En ? "The time to clear the information of the players' damage to NPC after NPC has take the last damage [sec.]" : "Время очистки информации о нанесенном уроне от игроков к NPC после нанесения последнего урона по NPC [sec.]")] public int TimeLastDamage { get; set; }
            [JsonProperty(En ? "Block a player from entering the event area if he is the owner of another event? [true/false]" : "Запрещать игроку входить внутрь зоны ивента, если он является владельцем другого ивента? [true/false]")] public bool NoEnterAnotherOwner { get; set; }
            [JsonProperty(En ? "Ignore administrators? [true/false]" : "Игнорировать администраторов? [true/false]")] public bool IgnoreAdmin { get; set; }
            [JsonProperty(En ? "Configuration version" : "Версия конфигурации")] public VersionNumber PluginVersion { get; set; }

            public static PluginConfig DefaultConfig()
            {
                return new PluginConfig
                {
                    TimeLastDamage = 300,
                    NoEnterAnotherOwner = false,
                    IgnoreAdmin = false,
                    PluginVersion = new VersionNumber()
                };
            }
        }
        #endregion Config

        #region Lang
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoLootScientist"] = "You <color=#ce3f27>are unable</color> to loot this NPC due to another player doing more damage!",
                ["NoLootCrateEvent"] = "You <color=#ce3f27>cannot</color> loot the crate! You are not the Event Owner and you are not on their team!",
                ["NoHackCrateEvent"] = "You <color=#ce3f27>cannot</color> hack the locked crate! You are not the Event Owner and you are not on their team!",
                ["NoLootScientistEvent"] = "You <color=#ce3f27>cannot</color> loot an NPC's corpse! You are not the Event Owner and you are not on their team!",
                ["NoDamageTankEvent"] = "You <color=#ce3f27>cannot</color> damage Bradley! You are not the Event Owner and you are not on their team!",
                ["NoDamageHelicopterEvent"] = "You <color=#ce3f27>cannot</color> damage Patrol Helicopter! You are not the Event Owner and you are not on their team!",
                ["NoDamageTurretEvent"] = "You <color=#ce3f27>cannot</color> damage Turret! You are not the Event Owner and you are not on their team!",
                ["NoDamageNpcEvent"] = "You <color=#ce3f27>cannot</color> damage NPC! You are not the Event Owner and you are not on their team!",
                ["NoEnterEvent"] = "You <color=#ce3f27>cannot</color> enter the Event zone! You are not the Event Owner and you are not on their team!",
                ["YouOwnerEvent"] = "You are now the <color=#738d43>Event Owner</color>!",
                ["ChangeOwnerEventToFriend"] = "You have exited the <color=#ce3f27>Event Zone</color>. The <color=#738d43>Event owner</color> is now <color=#55aaff>{0}</color>",
                ["TimerStartEvent"] = "You <color=#ce3f27>have left</color> the Event zone. You have to return to the Event zone in <color=#55aaff>{0}</color> or you will lose Event Owner status",
                ["AlertTimerEvent"] = "You have <color=#55aaff>{0}</color> to return to the Event Zone and keep Event Owner status",
                ["YouNonOwnerEvent"] = "You <color=#ce3f27>lost</color> the Event Owner status!",
                ["NoCanActionEvent"] = "You <color=#ce3f27>cannot</color> perform this action! You are not the Event Owner and you are not on their team!",
                ["OwnerEndEvent"] = "Event <color=#55aaff>{0}</color> is over. You were the Event Owner. You can play this event no earlier than in <color=#55aaff>{1}</color>",
                ["PlayerHasCooldownEnter"] = "You have <color=#ce3f27>entered</color> the event area in which you <color=#ce3f27>cannot</color> become the owner (you may <color=#ce3f27>lose loot</color>), you <color=#ce3f27>still have</color> a timer timer for participation in this event. You must wait at least <color=#55aaff>{0}</color> to become owner of this event again",
                ["EventsTime"] = "List of events:\n(If the event is not in the list, then you can become its owner)\n(If the event is marked with <color=#55aaff>*</color>, it means that it is currently active and the cooldown is indicated to get the status of event owner. Otherwise, it is indicated how long ago you were the owner of the event)"
            }, this);

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoLootScientist"] = "Вы <color=#ce3f27>не можете</color> ограбить этого NPC, потому что другой игрок нанес по нему большее количество урона!",
                ["NoLootCrateEvent"] = "Вы <color=#ce3f27>не можете</color> ограбить этот ящик, потому что не являетесь владельцем ивента и не состоите в команде с владельцем ивента!",
                ["NoHackCrateEvent"] = "Вы <color=#ce3f27>не можете</color> начать взлом этого заблокированного ящика, потому что не являетесь владельцем ивента и не состоите в команде с владельцем ивента!",
                ["NoLootScientistEvent"] = "Вы <color=#ce3f27>не можете</color> ограбить этого NPC, потому что не являетесь владельцем ивента и не состоите в команде с владельцем ивента!",
                ["NoDamageTankEvent"] = "Вы <color=#ce3f27>не можете</color> нанести урон этому Bradley, потому что не являетесь владельцем ивента и не состоите в команде с владельцем ивента!",
                ["NoDamageHelicopterEvent"] = "Вы <color=#ce3f27>не можете</color> нанести урон этому вертолету, потому что не являетесь владельцем ивента и не состоите в команде с владельцем ивента!",
                ["NoDamageTurretEvent"] = "Вы <color=#ce3f27>не можете</color> нанести урон этой турели, потому что не являетесь владельцем ивента и не состоите в команде с владельцем ивента!",
                ["NoDamageNpcEvent"] = "Вы <color=#ce3f27>не можете</color> нанести урон этому NPC, потому что не являетесь владельцем ивента и не состоите в команде с владельцем ивента!",
                ["NoEnterEvent"] = "Вы <color=#ce3f27>не можете</color> войти внутрь зоны ивента, потому что не являетесь владельцем ивента и не состоите в команде с владельцем ивента!",
                ["YouOwnerEvent"] = "Вы <color=#738d43>стали</color> владельцем ивента!",
                ["ChangeOwnerEventToFriend"] = "Вы <color=#ce3f27>вышли</color> из зоны ивента. Владелец ивента <color=#738d43>сменился</color> на игрока <color=#55aaff>{0}</color>",
                ["TimerStartEvent"] = "Вы <color=#ce3f27>вышли</color> из зоны ивента. Чтобы не потерять статус владельца ивента вам необходимо вернуться в зону ивента в течении <color=#55aaff>{0}</color>",
                ["AlertTimerEvent"] = "У вас осталось <color=#55aaff>{0}</color> чтобы вернуться в зону ивента и не потерять статус владельца ивента",
                ["YouNonOwnerEvent"] = "Вы <color=#ce3f27>утратили</color> статус владельца ивента!",
                ["NoCanActionEvent"] = "Вы <color=#ce3f27>не можете</color> выполнить это действие, потому что не являетесь владельцем ивента и не состоите в команде с владельцем ивента!",
                ["OwnerEndEvent"] = "Ивент <color=#55aaff>{0}</color> окончен. Вы были владельцем ивента. Участие в данном ивенте возможно не ранее чем через <color=#55aaff>{1}</color>",
                ["PlayerHasCooldownEnter"] = "Вы <color=#ce3f27>вошли</color> в зону ивента, в которой вы <color=#ce3f27>не можете</color> стать владельцем (есть риск <color=#ce3f27>потерять ресурсы</color>), потому что у вас еще <color=#ce3f27>не закончился</color> таймер на участие в ивенте. Участие возможно не ранее чем через <color=#55aaff>{0}</color>",
                ["EventsTime"] = "Список ивентов:\n(Если ивента нет в списке, то вы можете стать его владельцем)\n(Если ивент отмечен знаком <color=#55aaff>*</color>, значит сейчас он активен и указано оставшееся время, чтобы получить статус владельца ивента. Иначе указано сколько времени назад вы были владельцем ивента)"
            }, this, "ru");
        }

        private string GetMessage(string langKey, string userId) => lang.GetMessage(langKey, _ins, userId);

        private string GetMessage(string langKey, string userId, params object[] args) => (args.Length == 0) ? GetMessage(langKey, userId) : string.Format(GetMessage(langKey, userId), args);
        #endregion Lang

        #region Oxide Hooks
        private static PveMode _ins;

        private void Init()
        {
            _ins = this;
            Unsubscribes();
        }

        private void OnServerInitialized() => LoadData();

        private void Unload()
        {
            foreach (KeyValuePair<ulong, ControllerScientist> dic in Scientists) UnityEngine.Object.Destroy(dic.Value);
            foreach (ControllerEvent controllerEvent in Events) UnityEngine.Object.Destroy(controllerEvent.gameObject);
            _ins = null;
        }
        #endregion Oxide Hooks

        #region Team
        [PluginReference] private readonly Plugin Friends, Clans;

        private bool IsTeam(BasePlayer player, ulong targetId)
        {
            if (player == null || targetId == 0) return false;
            if (player.userID == targetId) return true;
            if (player.currentTeam != 0)
            {
                RelationshipManager.PlayerTeam playerTeam = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
                if (playerTeam == null) return false;
                if (playerTeam.members.Contains(targetId)) return true;
            }
            if (plugins.Exists("Friends") && (bool)Friends.Call("AreFriends", (ulong)player.userID, targetId)) return true;
            if (plugins.Exists("Clans") && Clans.Author == "k1lly0u" && (bool)Clans.Call("IsMemberOrAlly", player.UserIDString, targetId.ToString())) return true;
            return false;
        }

        private bool IsTeam(ulong playerId, ulong targetId)
        {
            if (playerId == 0 || targetId == 0) return false;
            if (playerId == targetId) return true;
            RelationshipManager.PlayerTeam playerTeam = RelationshipManager.ServerInstance.FindPlayersTeam(playerId);
            if (playerTeam != null && playerTeam.members.Contains(targetId)) return true;
            if (plugins.Exists("Friends") && (bool)Friends.Call("AreFriends", playerId, targetId)) return true;
            if (plugins.Exists("Clans") && Clans.Author == "k1lly0u" && (bool)Clans.Call("IsMemberOrAlly", playerId.ToString(), targetId.ToString())) return true;
            return false;
        }
        #endregion Team

        #region Scientists
        private void ScientistAddPveMode(ScientistNPC npc)
        {
            if (npc == null || npc.net == null) return;
            Scientists.Add(npc.net.ID.Value, npc.gameObject.AddComponent<ControllerScientist>());
        }

        private void ScientistRemovePveMode(ScientistNPC npc)
        {
            if (npc == null || npc.net == null) return;
            ulong id = npc.net.ID.Value;
            ControllerScientist controllerScientist = null;
            if (Scientists.TryGetValue(id, out controllerScientist))
            {
                Scientists.Remove(id);
                UnityEngine.Object.Destroy(controllerScientist);
            }
        }

        private void CrateAddScientistPveMode(ulong crateId, ulong scientistId)
        {
            if (crateId == 0 || scientistId == 0) return;
            ControllerScientist controllerScientist = null;
            if (Scientists.TryGetValue(scientistId, out controllerScientist)) controllerScientist.CrateId = crateId;
        }

        private Dictionary<ulong, ControllerScientist> Scientists { get; } = new Dictionary<ulong, ControllerScientist>();

        internal class ControllerScientist : FacepunchBehaviour
        {
            private int TimeLastDamage { get; set; } = 0;

            internal ulong CrateId { get; set; } = 0;

            internal Dictionary<ulong, float> Players { get; } = new Dictionary<ulong, float>();

            private void OnDestroy() => CancelInvoke(IncrementTime);

            internal void AddDamage(BasePlayer attacker, float damage)
            {
                if (Players.ContainsKey(attacker.userID)) Players[attacker.userID] += damage;
                else Players.Add(attacker.userID, damage);
                if (TimeLastDamage == 0) InvokeRepeating(IncrementTime, 1f, 1f);
                TimeLastDamage = _ins._config.TimeLastDamage;
            }

            private void IncrementTime()
            {
                TimeLastDamage--;
                if (TimeLastDamage != 0) return;
                Players.Clear();
                CancelInvoke(IncrementTime);
            }

            internal ulong GetWinner => Players.Max(s => s.Value).Key;
        }
        #endregion Scientists

        #region Events
        internal class EventConfig
        {
            public float Damage { get; set; }
            public Dictionary<string, float> ScaleDamage { get; set; }
            public bool LootCrate { get; set; }
            public bool HackCrate { get; set; }
            public bool LootNpc { get; set; }
            public bool DamageNpc { get; set; }
            public bool DamageTank { get; set; }
            public bool DamageHelicopter { get; set; }
            public bool DamageTurret { get; set; }
            public bool TargetNpc { get; set; }
            public bool TargetTank { get; set; }
            public bool TargetHelicopter { get; set; }
            public bool TargetTurret { get; set; }
            public bool CanEnter { get; set; }
            public bool CanEnterCooldownPlayer { get; set; }
            public int TimeExitOwner { get; set; }
            public int AlertTime { get; set; }
            public bool RestoreUponDeath { get; set; }
            public double CooldownOwner { get; set; }
            public int Darkening { get; set; }
        }

        private void EventAddPveMode(string shortname, Dictionary<string, object> config, Vector3 position, float radius, HashSet<ulong> crates, HashSet<ulong> npc, HashSet<ulong> tanks, HashSet<ulong> helicopters, HashSet<ulong> turrets, HashSet<ulong> owners, BasePlayer owner = null)
        {
            ControllerEvent controllerEvent = new GameObject().AddComponent<ControllerEvent>();
            controllerEvent.transform.position = position;
            controllerEvent.ShortName = shortname;
            controllerEvent.Config = new EventConfig
            {
                Damage = (float)config["Damage"],
                ScaleDamage = (Dictionary<string, float>)config["ScaleDamage"],
                LootCrate = (bool)config["LootCrate"],
                HackCrate = (bool)config["HackCrate"],
                LootNpc = (bool)config["LootNpc"],
                DamageNpc = (bool)config["DamageNpc"],
                DamageTank = (bool)config["DamageTank"],
                DamageHelicopter = (bool)config["DamageHelicopter"],
                DamageTurret = (bool)config["DamageTurret"],
                TargetNpc = (bool)config["TargetNpc"],
                TargetTank = (bool)config["TargetTank"],
                TargetHelicopter = (bool)config["TargetHelicopter"],
                TargetTurret = (bool)config["TargetTurret"],
                CanEnter = (bool)config["CanEnter"],
                CanEnterCooldownPlayer = (bool)config["CanEnterCooldownPlayer"],
                TimeExitOwner = (int)config["TimeExitOwner"],
                AlertTime = (int)config["AlertTime"],
                RestoreUponDeath = (bool)config["RestoreUponDeath"],
                CooldownOwner = (double)config["CooldownOwner"],
                Darkening = (int)config["Darkening"]
            };
            controllerEvent.Radius = radius;
            controllerEvent.Crates = crates;
            controllerEvent.Npc = npc;
            controllerEvent.Tanks = tanks;
            controllerEvent.Helicopters = helicopters;
            controllerEvent.Turrets = turrets;
            controllerEvent.Owners = owners;
            if (owner != null) controllerEvent.SetOwner(owner);
            controllerEvent.InitSphere();
            Events.Add(controllerEvent);
            LogToFile("CreateZone", $"[{DateTime.Now.ToShortTimeString()}] The zone {shortname} has been created at {position}", _ins);
            if (Events.Count == 1) Subscribes();
        }

        private void EventRemovePveMode(string shortname, bool addCooldownOwners = true)
        {
            ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.ShortName == shortname);
            if (controllerEvent == null) return;
            if (addCooldownOwners)
            {
                foreach (ulong id in controllerEvent.Owners)
                {
                    PlayerData playerData = PlayersData.FirstOrDefault(x => x.SteamId == id);
                    if (playerData == null) PlayersData.Add(new PlayerData { SteamId = id, LastTime = new Dictionary<string, double> { [shortname] = CurrentTime } });
                    else
                    {
                        if (playerData.LastTime.ContainsKey(shortname)) playerData.LastTime[shortname] = CurrentTime;
                        else playerData.LastTime.Add(shortname, CurrentTime);
                    }
                    BasePlayer player = BasePlayer.FindByID(id);
                    if (player != null) PrintToChat(player, GetMessage("OwnerEndEvent", player.UserIDString, shortname, GetTimeFormat(controllerEvent.Config.CooldownOwner)));
                }
                SaveData();
            }
            Events.Remove(controllerEvent);
            UnityEngine.Object.Destroy(controllerEvent.gameObject);
            LogToFile("DestroyZone", $"[{DateTime.Now.ToShortTimeString()}] The zone {shortname} has been destroyed", _ins);
            if (Events.Count == 0) Unsubscribes();
        }

        private void EventAddCooldown(string shortname, HashSet<ulong> owners, double cooldown)
        {
            foreach (ulong id in owners)
            {
                PlayerData playerData = PlayersData.FirstOrDefault(x => x.SteamId == id);
                if (playerData == null) PlayersData.Add(new PlayerData { SteamId = id, LastTime = new Dictionary<string, double> { [shortname] = CurrentTime } });
                else
                {
                    if (playerData.LastTime.ContainsKey(shortname)) playerData.LastTime[shortname] = CurrentTime;
                    else playerData.LastTime.Add(shortname, CurrentTime);
                }
                BasePlayer player = BasePlayer.FindByID(id);
                if (player != null) PrintToChat(player, GetMessage("OwnerEndEvent", player.UserIDString, shortname, GetTimeFormat(cooldown)));
            }
            SaveData();
        }

        private void EventAddCrates(string shortname, HashSet<ulong> crates)
        {
            ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.ShortName == shortname);
            if (controllerEvent == null) return;
            foreach (ulong id in crates) if (!controllerEvent.Crates.Contains(id)) controllerEvent.Crates.Add(id);
        }

        private void EventAddScientists(string shortname, HashSet<ulong> npc)
        {
            ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.ShortName == shortname);
            if (controllerEvent == null) return;
            foreach (ulong id in npc) if (!controllerEvent.Npc.Contains(id)) controllerEvent.Npc.Add(id);
        }

        private void EventAddTanks(string shortname, HashSet<ulong> tanks)
        {
            ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.ShortName == shortname);
            if (controllerEvent == null) return;
            foreach (ulong id in tanks) if (!controllerEvent.Tanks.Contains(id)) controllerEvent.Tanks.Add(id);
        }

        private void EventAddHelicopters(string shortname, HashSet<ulong> helicopters)
        {
            ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.ShortName == shortname);
            if (controllerEvent == null) return;
            foreach (ulong id in helicopters) if (!controllerEvent.Helicopters.Contains(id)) controllerEvent.Helicopters.Add(id);
        }

        private void EventAddTurrets(string shortname, HashSet<ulong> turrets)
        {
            ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.ShortName == shortname);
            if (controllerEvent == null) return;
            foreach (ulong id in turrets) if (!controllerEvent.Turrets.Contains(id)) controllerEvent.Turrets.Add(id);
        }

        private void SetEventOwner(string shortname, BasePlayer player)
        {
            ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.ShortName == shortname);
            if (controllerEvent == null) return;
            controllerEvent.SetOwner(player);
        }

        private const int TargetLayers = ~(1 << 10 | 1 << 18 | 1 << 28 | 1 << 29);

        private HashSet<ControllerEvent> Events { get; } = new HashSet<ControllerEvent>();

        internal class ControllerEvent : FacepunchBehaviour
        {
            internal string ShortName;

            internal EventConfig Config { get; set; } = null;

            internal float Radius { get; set; } = 0f;

            internal HashSet<ulong> Crates { get; set; } = null;
            internal HashSet<ulong> Backpacks { get; } = new HashSet<ulong>();

            internal HashSet<ulong> Npc { get; set; } = null;
            internal HashSet<ulong> Tanks { get; set; } = null;
            internal HashSet<ulong> Helicopters { get; set; } = null;
            internal HashSet<ulong> Turrets { get; set; } = null;

            internal Dictionary<ulong, float> Players { get; } = new Dictionary<ulong, float>();
            internal ulong Owner { get; set; } = 0;
            private int TimerExitOwner { get; set; } = 0;
            internal HashSet<ulong> Owners { get; set; } = null;

            private SphereCollider SphereCollider { get; set; } = null;
            internal HashSet<BasePlayer> InsidePlayers { get; } = new HashSet<BasePlayer>();

            private HashSet<SphereEntity> Spheres { get; } = new HashSet<SphereEntity>();

            private void OnDestroy()
            {
                CancelInvoke(IncrementTime);
                if (SphereCollider != null) Destroy(SphereCollider);
                foreach (SphereEntity sphere in Spheres) if (sphere.IsExists()) sphere.Kill();
            }

            internal void InitSphere()
            {
                gameObject.layer = 3;
                SphereCollider = gameObject.AddComponent<SphereCollider>();
                SphereCollider.isTrigger = true;
                SphereCollider.radius = Radius;
                CreateDome();
            }

            private void CreateDome()
            {
                if (Config.Darkening == 0) return;
                for (int i = 0; i < Config.Darkening; i++)
                {
                    SphereEntity sphere = GameManager.server.CreateEntity("assets/prefabs/visualization/sphere.prefab", transform.position) as SphereEntity;
                    sphere.currentRadius = Radius * 2;
                    sphere.lerpSpeed = 0f;
                    sphere.enableSaving = false;
                    sphere.Spawn();
                    Spheres.Add(sphere);
                }
            }

            private void OnTriggerEnter(Collider other)
            {
                BasePlayer player = other.GetComponentInParent<BasePlayer>();
                if (player.IsPlayer())
                {
                    if (player.IsAdmin && _ins._config.IgnoreAdmin) return;
                    bool canTimeOwner = _ins.CanTimeOwner(ShortName, player.userID, Config.CooldownOwner);
                    if (!Config.CanEnterCooldownPlayer && !canTimeOwner) KickOutPlayer(player);
                    if (_ins._config.NoEnterAnotherOwner && _ins.Events.Any(x => x.ShortName != ShortName && x.Owners.Contains(player.userID))) KickOutPlayer(player);
                    if (Owner == 0)
                    {
                        if (!canTimeOwner) _ins.PrintToChat(player, _ins.GetMessage("PlayerHasCooldownEnter", player.UserIDString, GetTimeFormat(_ins.PlayersData.FirstOrDefault(x => x.SteamId == player.userID).LastTime[ShortName] + Config.CooldownOwner - CurrentTime)));
                        InsidePlayers.Add(player);
                    }
                    else
                    {
                        if (Config.CanEnter || _ins.IsTeam(player, Owner))
                        {
                            InsidePlayers.Add(player);
                            if (TimerExitOwner > 0 && _ins.IsTeam(player, Owner) && canTimeOwner)
                            {
                                CancelInvoke(IncrementTime);
                                TimerExitOwner = 0;
                                if (Owner != player.userID) SetOwner(player);
                            }
                        }
                        else KickOutPlayer(player);
                    }
                }
                else CheckJetpack(other);
            }

            private void OnTriggerExit(Collider other)
            {
                BasePlayer player = other.GetComponentInParent<BasePlayer>();
                if (player.IsPlayer()) ExitPlayer(player);
            }

            internal void ExitPlayer(BasePlayer player)
            {
                if (InsidePlayers.Contains(player)) InsidePlayers.Remove(player);
                if (player.userID != Owner) return;
                BasePlayer friend = InsidePlayers.FirstOrDefault(x => _ins.IsTeam(x, Owner) && _ins.CanTimeOwner(ShortName, x.userID, Config.CooldownOwner));
                if (friend != null)
                {
                    _ins.PrintToChat(player, _ins.GetMessage("ChangeOwnerEventToFriend", player.UserIDString, friend.displayName));
                    SetOwner(friend);
                }
                else
                {
                    TimerExitOwner = Config.TimeExitOwner;
                    InvokeRepeating(IncrementTime, 1f, 1f);
                    _ins.PrintToChat(player, _ins.GetMessage("TimerStartEvent", player.UserIDString, GetTimeFormat(TimerExitOwner)));
                }
            }

            internal void SetOwner(BasePlayer player)
            {
                if (!player.IsPlayer()) return;
                Owner = player.userID;
                if (!Owners.Contains(player.userID)) Owners.Add(player.userID);
                Interface.Oxide.CallHook("SetOwnerPveMode", ShortName, player);
                _ins.PrintToChat(player, _ins.GetMessage("YouOwnerEvent", player.UserIDString));
                _ins.LogToFile("SetOwner", $"[{DateTime.Now.ToShortTimeString()}] {player.displayName} [{player.userID}] became owner of zone {ShortName}", _ins);
            }

            internal void ClearOwner(BasePlayer player)
            {
                Owner = 0;
                Interface.Oxide.CallHook("ClearOwnerPveMode", ShortName);
                if (player == null) return;
                _ins.PrintToChat(player, _ins.GetMessage("YouNonOwnerEvent", player.UserIDString));
                _ins.LogToFile("ClearOwner", $"[{DateTime.Now.ToShortTimeString()}] {player.displayName} [{player.userID}] became non-owner of zone {ShortName}", _ins);
            }

            private void IncrementTime()
            {
                TimerExitOwner--;
                if (Config.AlertTime > 0 && TimerExitOwner == Config.AlertTime)
                {
                    BasePlayer player = BasePlayer.FindByID(Owner);
                    if (player != null) _ins.PrintToChat(player, _ins.GetMessage("AlertTimerEvent", player.UserIDString, GetTimeFormat(Config.AlertTime)));
                }
                if (TimerExitOwner == 0)
                {
                    CancelInvoke(IncrementTime);
                    ClearOwner(BasePlayer.FindByID(Owner));
                }
            }

            internal void AddDamage(BasePlayer player, float damage)
            {
                if (_ins._config.NoEnterAnotherOwner && _ins.Events.Any(x => x.ShortName != ShortName && x.Owners.Contains(player.userID))) return;

                if (Players.ContainsKey(player.userID)) Players[player.userID] += damage;
                else Players.Add(player.userID, damage);

                if (!_ins.CanTimeOwner(ShortName, player.userID, Config.CooldownOwner)) return;

                if (!InsidePlayers.Contains(player)) return;

                if (Players[player.userID] >= Config.Damage)
                {
                    SetOwner(player);
                    Players.Clear();
                    if (!Config.CanEnter)
                    {
                        foreach (BasePlayer insidePlayer in InsidePlayers.ToHashSet())
                            if (!_ins.IsTeam(insidePlayer, Owner))
                                KickOutPlayer(insidePlayer);
                    }
                }
            }

            internal void KickOutPlayer(BasePlayer player)
            {
                if (player.isMounted)
                {
                    BaseMountable baseMountable = player.GetMounted();

                    BaseVehicle vehicle = baseMountable.VehicleParent();
                    if (vehicle != null)
                    {
                        vehicle.transform.rotation = Quaternion.Euler(vehicle.transform.eulerAngles.x, vehicle.transform.eulerAngles.y - 180f, vehicle.transform.eulerAngles.z);
                        vehicle.rigidBody.velocity *= -2f;
                        return;
                    }

                    baseMountable.DismountPlayer(player);
                }
                Vector3 position = transform.position + ((player.transform.position.XZ3D() - transform.position.XZ3D()).normalized * (Radius + 10f));
                position.y = 500f;
                RaycastHit raycastHit;
                position.y = Physics.Raycast(position, Vector3.down, out raycastHit, 500f, TargetLayers, QueryTriggerInteraction.Ignore) ? raycastHit.point.y : TerrainMeta.HeightMap.GetHeight(position);
                player.MovePosition(position);
                player.ClientRPCPlayer(null, player, "ForcePositionTo", player.transform.position);
                player.SendNetworkUpdateImmediate();
                _ins.PrintToChat(player, _ins.GetMessage("NoEnterEvent", player.UserIDString));
            }

            private void CheckJetpack(Collider collider)
            {
                if (collider == null || collider is CapsuleCollider == false) return;

                DroppedItem droppedItem = collider.GetComponentInParent<DroppedItem>();
                if (!droppedItem.IsExists()) return;

                BaseMountable baseMountable = droppedItem.GetComponentInChildren<BaseMountable>();
                if (!baseMountable.IsExists()) return;

                BasePlayer player = baseMountable._mounted;
                if (player == null || !player.userID.IsSteamId()) return;

                if (player.IsAdmin && _ins._config.IgnoreAdmin) return;

                if (!Config.CanEnterCooldownPlayer && !_ins.CanTimeOwner(ShortName, player.userID, Config.CooldownOwner)) KickJetpack(droppedItem, player);

                if (_ins._config.NoEnterAnotherOwner && _ins.Events.Any(x => x.ShortName != ShortName && x.Owners.Contains(player.userID))) KickJetpack(droppedItem, player);

                if (Owner != 0 && !Config.CanEnter && !_ins.IsTeam(player, Owner)) KickJetpack(droppedItem, player);
            }

            private void KickJetpack(DroppedItem droppedItem, BasePlayer player)
            {
                droppedItem.Kill();
                player.DismountObject();
                KickOutPlayer(player);
            }
        }

        private void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (!player.IsPlayer()) return;
            ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.InsidePlayers.Contains(player));
            if (controllerEvent != null) controllerEvent.ExitPlayer(player);
        }

        private void OnEntityDismounted(BaseMountable entity, BasePlayer player)
        {
            if (!player.IsPlayer()) return;
            if (player.IsAdmin && _config.IgnoreAdmin) return;
            ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.Owner != 0 && Vector3.Distance(x.transform.position, player.transform.position) < x.Radius);
            if (controllerEvent == null) return;
            if (!controllerEvent.Config.CanEnterCooldownPlayer && !CanTimeOwner(controllerEvent.ShortName, player.userID, controllerEvent.Config.CooldownOwner)) controllerEvent.KickOutPlayer(player);
            if (_config.NoEnterAnotherOwner && Events.Any(x => x.ShortName != controllerEvent.ShortName && x.Owners.Contains(player.userID))) controllerEvent.KickOutPlayer(player);
            if (!controllerEvent.Config.CanEnter && !IsTeam(player, controllerEvent.Owner)) controllerEvent.KickOutPlayer(player);
        }

        private object OnRestoreUponDeath(BasePlayer player)
        {
            if (!player.IsPlayer()) return null;
            ControllerEvent controllerEvent = Events.FirstOrDefault(x => Vector3.Distance(x.transform.position, player.transform.position) < x.Radius);
            if (controllerEvent == null) return null;
            if (controllerEvent.Config.RestoreUponDeath) return false;
            else return null;
        }

        private object CanActionEvent(string shortname, BasePlayer player)
        {
            ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.Owner != 0 && x.ShortName == shortname);
            if (controllerEvent == null) return null;
            if (IsTeam(player, controllerEvent.Owner)) return null;
            else
            {
                PrintToChat(player, GetMessage("NoCanActionEvent", player.UserIDString));
                return false;
            }
        }

        private object CanActionEventNoMessage(string shortname, BasePlayer player)
        {
            ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.Owner != 0 && x.ShortName == shortname);
            if (controllerEvent == null) return null;
            if (IsTeam(player, controllerEvent.Owner)) return null;
            else return false;
        }

        private ControllerEvent GetControllerEventAtPosition(Vector3 pos) => Events.FirstOrDefault(x => Vector3.Distance(pos, x.transform.position) <= x.Radius);

        public enum EntityType
        {
            Default,
            Npc,
            Animal,
            Bradley,
            Turret,
            Helicopter
        }

        private bool IsEventTurret(BaseEntity entity)
        {
            if (entity == null || entity.net == null) return false;
            if (entity is not (AutoTurret or FlameTurret or GunTrap or SamSite)) return false;
            return Events.Any(x => x.Turrets.Contains(entity.net.ID.Value));
        }
        #endregion Events

        #region Time
        internal class PlayerData { public ulong SteamId; public Dictionary<string, double> LastTime; }

        private HashSet<PlayerData> PlayersData { get; set; } = null;

        private void LoadData() => PlayersData = Interface.Oxide.DataFileSystem.ReadObject<HashSet<PlayerData>>(Name) ?? new HashSet<PlayerData>();

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, PlayersData);

        private static readonly DateTime Epoch = new DateTime(2024, 1, 1, 0, 0, 0);

        private static double CurrentTime => DateTime.Now.Subtract(Epoch).TotalSeconds;

        private bool CanTimeOwner(string nameEvent, ulong steamId, double cooldown)
        {
            PlayerData playerData = PlayersData.FirstOrDefault(x => x.SteamId == steamId);
            if (playerData == null) return true;
            KeyValuePair<string, double> dic = playerData.LastTime.FirstOrDefault(x => nameEvent.Contains(x.Key));
            if (dic.Equals(default(KeyValuePair<string, double>))) return true;
            return dic.Value + cooldown < CurrentTime;
        }

        [ConsoleCommand("ClearTimePveMode")]
        private void ConsoleClearTimePveMode(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null || arg.Args == null || arg.Args.Length == 0 || arg.Args.Length > 2) return;

            ulong id = Convert.ToUInt64(arg.Args[0]);

            PlayerData playerData = PlayersData.FirstOrDefault(x => x.SteamId == id);
            if (playerData == null)
            {
                Puts($"Player {id} not found in the plugin database");
                return;
            }

            string nameEvent = arg.Args.Length == 2 ? arg.Args[1] : string.Empty;

            if (string.IsNullOrEmpty(nameEvent))
            {
                playerData.LastTime.Clear();
                Puts($"You have cleared the time data from player {id}");
                return;
            }

            if (playerData.LastTime.ContainsKey(nameEvent))
            {
                playerData.LastTime.Remove(nameEvent);
                Puts($"You have cleared the time data for {nameEvent} from player {id}");
            }
            else Puts($"Event {nameEvent} not found in the player database");
        }

        [ConsoleCommand("ClearOwnerPveMode")]
        private void ConsoleClearOwnerPveMode(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null || arg.Args == null || arg.Args.Length == 0 || arg.Args.Length > 2) return;

            ulong id = Convert.ToUInt64(arg.Args[0]);
            BasePlayer player = BasePlayer.FindByID(id);

            string nameEvent = arg.Args.Length == 2 ? arg.Args[1] : string.Empty;

            foreach (ControllerEvent controller in Events)
            {
                if (controller.Owner != id) continue;
                if (string.IsNullOrEmpty(nameEvent) || controller.ShortName == nameEvent)
                {
                    controller.ClearOwner(player);
                    Puts($"You have cleared the owner from event {controller.ShortName}");
                }
            }
        }

        [ChatCommand("EventsTime")]
        private void ChaEventsTime(BasePlayer player)
        {
            PlayerData playerData = PlayersData.FirstOrDefault(x => x.SteamId == player.userID);
            if (playerData == null || playerData.LastTime.Count == 0) return;

            string message = GetMessage("EventsTime", player.UserIDString);

            foreach (KeyValuePair<string, double> dic in playerData.LastTime)
            {
                ControllerEvent controller = Events.FirstOrDefault(x => x.ShortName == dic.Key);
                if (controller == null) message += $"\n- {dic.Key} = {GetTimeFormat(CurrentTime - dic.Value)}";
                else
                {
                    double time = dic.Value + controller.Config.CooldownOwner - CurrentTime;
                    if (time > 0) message += $"\n- {dic.Key}* = {GetTimeFormat(time)}";
                }
            }

            PrintToChat(player, message);
        }

        private const string StrSec = En ? "sec." : "сек.";
        private const string StrMin = En ? "min." : "мин.";
        private const string StrH = En ? "h." : "ч.";

        private static string GetTimeFormat(double time)
        {
            int integer = (int)time;
            if (time <= 60) return $"{integer} {StrSec}";
            else if (time <= 3600)
            {
                int sec = integer % 60;
                int min = (integer - sec) / 60;
                return sec == 0 ? $"{min} {StrMin}" : $"{min} {StrMin} {sec} {StrSec}";
            }
            else
            {
                int hour = (int)(time / 3600);
                time -= hour * 3600;
                integer = (int)time;
                int sec = integer % 60;
                int min = (integer - sec) / 60;
                if (min == 0 && sec == 0) return $"{hour} {StrH}";
                else if (sec == 0) return $"{hour} {StrH} {min} {StrMin}";
                else return $"{hour} {StrH} {min} {StrMin} {sec} {StrSec}";
            }
        }
        #endregion Time

        #region Loot
        private Dictionary<ulong, ulong> СanLootScientist { get; } = new Dictionary<ulong, ulong>();
        private Dictionary<ulong, ulong> CanLootCrateScientist { get; } = new Dictionary<ulong, ulong>();

        private object CanHackCrate(BasePlayer player, HackableLockedCrate crate)
        {
            if (crate == null || crate.net == null || !player.IsPlayer()) return null;
            ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.Owner != 0 && x.Crates.Contains(crate.net.ID.Value));
            if (controllerEvent == null) return null;
            if (controllerEvent.Config.HackCrate || IsTeam(player, controllerEvent.Owner)) return null;
            else
            {
                PrintToChat(player, GetMessage("NoHackCrateEvent", player.UserIDString));
                return true;
            }
        }

        private object CanLootEntity(BasePlayer player, StorageContainer container)
        {
            if (container == null || container.net == null || !player.IsPlayer()) return null;

            ulong id = container.net.ID.Value;

            if (Events.Count > 0)
            {
                ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.Owner != 0 && x.Crates.Contains(id));
                if (controllerEvent != null)
                {
                    if (controllerEvent.Config.LootCrate || IsTeam(player, controllerEvent.Owner)) return null;
                    else
                    {
                        PrintToChat(player, GetMessage("NoLootCrateEvent", player.UserIDString));
                        return true;
                    }
                }
            }

            ulong ownerId = 0;
            if (CanLootCrateScientist.TryGetValue(id, out ownerId))
            {
                if (IsTeam(player, ownerId)) return null;
                else
                {
                    PrintToChat(player, GetMessage("NoLootScientist", player.UserIDString));
                    return true;
                }
            }

            return null;
        }

        private object CanLootEntity(BasePlayer player, NPCPlayerCorpse corpse)
        {
            if (corpse == null || corpse.net == null || !player.IsPlayer()) return null;
            ulong id = corpse.net.ID.Value;
            return CanLootScientist(player, id);
        }

        private object CanLootEntity(BasePlayer player, DroppedItemContainer container)
        {
            if (!player.IsPlayer() || container == null || container.net == null || container.ShortPrefabName != "item_drop_backpack") return null;
            ulong id = container.net.ID.Value;
            return CanLootScientist(player, id);
        }

        private object CanLootScientist(BasePlayer player, ulong targetId)
        {
            if (Events.Count > 0)
            {
                ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.Owner != 0 && x.Backpacks.Contains(targetId));
                if (controllerEvent != null)
                {
                    if (controllerEvent.Config.LootNpc || IsTeam(player, controllerEvent.Owner)) return null;
                    else
                    {
                        PrintToChat(player, GetMessage("NoLootScientistEvent", player.UserIDString));
                        return true;
                    }
                }
            }
            if (СanLootScientist.TryGetValue(targetId, out ulong ownerId))
            {
                if (IsTeam(player, ownerId)) return null;
                else
                {
                    PrintToChat(player, GetMessage("NoLootScientist", player.UserIDString));
                    return true;
                }
            }
            return null;
        }

        private void OnCorpsePopulate(ScientistNPC entity, NPCPlayerCorpse corpse)
        {
            if (entity == null || entity.net == null || corpse == null || corpse.net == null) return;

            ulong id = entity.net.ID.Value;

            if (Events.Count > 0)
            {
                ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.Npc.Contains(id));
                if (controllerEvent != null)
                {
                    controllerEvent.Npc.Remove(id);
                    controllerEvent.Backpacks.Add(corpse.playerSteamID);
                    return;
                }
            }

            ControllerScientist controllerScientist = null;
            if (Scientists.TryGetValue(id, out controllerScientist))
            {
                ulong netId = corpse.net.ID.Value;
                NextTick(() =>
                {
                    if (controllerScientist.Players.Count != 0)
                    {
                        ulong winner = controllerScientist.GetWinner;
                        СanLootScientist.Add(netId, winner);
                        if (controllerScientist.CrateId != 0) CanLootCrateScientist.Add(controllerScientist.CrateId, winner);
                    }
                    Scientists.Remove(id);
                    UnityEngine.Object.Destroy(controllerScientist);
                });
                return;
            }
        }

        private object CanCustomAnimalSpawnCorpse(BaseAnimalNPC entity)
        {
            if (entity == null || entity.net == null) return null;
            ulong id = entity.net.ID.Value;
            ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.Npc.Contains(id));
            if (controllerEvent != null) controllerEvent.Npc.Remove(id);
            return null;
        }

        private HashSet<CorpseInfo> CorpseBuffer { get; set; } = new HashSet<CorpseInfo>();

        public class CorpseInfo
        {
            public Vector3 Position { get; set; }
            public string PlayerName { get; set; }
            public ulong PlayerSteamID { get; set; }
            public ulong NetId { get; set; }
            public ulong Winner { get; set; }

            public CorpseInfo(NPCPlayerCorpse corpse, ulong netId, ulong winner)
            {
                Position = corpse.transform.position;
                PlayerName = corpse.playerName;
                PlayerSteamID = corpse.playerSteamID;
                NetId = netId;
                Winner = winner;
            }
        }

        private void OnEntityKill(NPCPlayerCorpse corpse)
        {
            if (corpse == null || corpse.net == null) return;
            ulong netId = corpse.net.ID.Value;
            if (!СanLootScientist.TryGetValue(netId, out ulong winner)) return;
            CorpseBuffer.Add(new CorpseInfo(corpse, netId, winner));
        }

        private void OnEntitySpawned(DroppedItemContainer container)
        {
            if (container == null || container.net == null || container.ShortPrefabName != "item_drop_backpack") return;
            NextTick(() =>
            {
                CorpseInfo info = CorpseBuffer.FirstOrDefault(x => x.PlayerName == container.playerName && x.PlayerSteamID == container.playerSteamID && Vector3.Distance(x.Position, container.transform.position) < 1.5f);
                if (info == null) return;

                CorpseBuffer.Remove(info);

                СanLootScientist.Remove(info.NetId);
                СanLootScientist.Add(container.net.ID.Value, info.Winner);
            });
        }

        private void OnEntityKill(StorageContainer crate)
        {
            if (crate == null || crate.net == null) return;
            ulong id = crate.net.ID.Value;
            if (Events.Count > 0)
            {
                ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.Crates.Contains(id));
                if (controllerEvent != null) controllerEvent.Crates.Remove(id);
            }
            if (CanLootCrateScientist.ContainsKey(id)) CanLootCrateScientist.Remove(id);
        }

        private void OnEntityKill(DroppedItemContainer container)
        {
            if (container == null || container.net == null || container.ShortPrefabName != "item_drop_backpack") return;
            ulong id = container.net.ID.Value;
            if (СanLootScientist.ContainsKey(id)) СanLootScientist.Remove(id);
        }

        private object OnLootLockedEntity(BasePlayer player, NPCPlayerCorpse corpse)
        {
            if (corpse == null || corpse.net == null || !player.IsPlayer()) return null;
            ulong id = corpse.net.ID.Value;
            return CanLootScientist(player, id) == null ? null : (object)false;
        }

        private object OnLootLockedEntity(BasePlayer player, DroppedItemContainer container)
        {
            if (!player.IsPlayer() || container == null || container.net == null || container.ShortPrefabName != "item_drop_backpack") return null;
            ulong id = container.net.ID.Value;
            return CanLootScientist(player, id) == null ? null : (object)false;
        }

        private object OnDispenserGather(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            if (dispenser == null || !player.IsPlayer() || Events.Count == 0) return null;
            if (!dispenser.name.Contains("servergibs_bradley") && !dispenser.name.Contains("servergibs_patrolhelicopter")) return null;
            ControllerEvent controller = GetControllerEventAtPosition(dispenser.transform.position);
            if (controller == null || controller.Owner == 0) return null;
            if (IsTeam(player, controller.Owner)) return null;
            else return true;
        }
        #endregion Loot

        #region Damage
        private object OnEventEntityTakeDamage(BaseCombatEntity entity, HitInfo info, bool addDamage = true, bool sendMessage = true)
        {
            if (entity == null || entity.net == null || info == null) return null;

            EntityType type = EntityType.Default;
            ControllerEvent controllerEvent = null;

            switch (entity)
            {
                case ScientistNPC _:
                    controllerEvent = Events.FirstOrDefault(x => x.Npc.Contains(entity.net.ID.Value));
                    type = EntityType.Npc;
                    break;
                case BaseAnimalNPC _:
                    controllerEvent = Events.FirstOrDefault(x => x.Npc.Contains(entity.net.ID.Value));
                    type = EntityType.Animal;
                    break;
                case BradleyAPC _:
                    controllerEvent = Events.FirstOrDefault(x => x.Tanks.Contains(entity.net.ID.Value));
                    type = EntityType.Bradley;
                    break;
                case PatrolHelicopter _:
                    controllerEvent = Events.FirstOrDefault(x => x.Helicopters.Contains(entity.net.ID.Value));
                    type = EntityType.Helicopter;
                    break;
                case AutoTurret _:
                case FlameTurret _:
                case GunTrap _:
                case SamSite _:
                    controllerEvent = Events.FirstOrDefault(x => x.Turrets.Contains(entity.net.ID.Value));
                    type = EntityType.Turret;
                    break;
            }

            if (controllerEvent == null) return null;

            BasePlayer attackerPlayer = info.InitiatorPlayer;
            bool isPlayer = attackerPlayer.IsPlayer();

            if (controllerEvent.Owner == 0)
            {
                if (isPlayer && addDamage)
                {
                    float scale = 0f;
                    controllerEvent.Config.ScaleDamage.TryGetValue(type.ToString(), out scale);
                    controllerEvent.AddDamage(attackerPlayer, info.damageTypes.Total() * scale);
                }
                return null;
            }

            switch (type)
            {
                case EntityType.Npc:
                case EntityType.Animal:
                    {
                        if (controllerEvent.Config.DamageNpc) return null;
                        break;
                    }
                case EntityType.Bradley when controllerEvent.Config.DamageTank:
                case EntityType.Helicopter when controllerEvent.Config.DamageHelicopter:
                case EntityType.Turret when controllerEvent.Config.DamageTurret:
                    return null;
            }

            if (isPlayer)
            {
                if (IsTeam(attackerPlayer, controllerEvent.Owner)) return null;
                else
                {
                    if (!sendMessage) return true;
                    switch (type)
                    {
                        case EntityType.Npc:
                        case EntityType.Animal:
                            PrintToChat(attackerPlayer, GetMessage("NoDamageNpcEvent", attackerPlayer.UserIDString));
                            break;
                        case EntityType.Bradley:
                            PrintToChat(attackerPlayer, GetMessage("NoDamageTankEvent", attackerPlayer.UserIDString));
                            break;
                        case EntityType.Helicopter:
                            PrintToChat(attackerPlayer, GetMessage("NoDamageHelicopterEvent", attackerPlayer.UserIDString));
                            break;
                        case EntityType.Turret:
                            PrintToChat(attackerPlayer, GetMessage("NoDamageTurretEvent", attackerPlayer.UserIDString));
                            break;
                    }
                    return true;
                }
            }
            else
            {
                BaseEntity attacker = info.Initiator;
                if (attacker != null && IsTeam(attacker.OwnerID, controllerEvent.Owner)) return null;
                else
                {
                    if (type == EntityType.Helicopter && attacker == null && info.damageTypes.GetMajorityDamageType() == Rust.DamageType.Generic) return null;
                    return true;
                }
            }
        }

        private object OnEventInitiatorNullTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (info.Initiator != null) return null;

            BaseEntity weaponPrefab = info.WeaponPrefab;
            if (weaponPrefab == null) return null;

            EntityType type = EntityType.Default;
            switch (weaponPrefab.ShortPrefabName)
            {
                case "maincannonshell":
                    type = EntityType.Bradley;
                    break;
                case "rocket_heli":
                case "rocket_heli_napalm":
                    type = EntityType.Helicopter;
                    break;
            }
            if (type == EntityType.Default) return null;

            ControllerEvent controllerEvent = GetControllerEventAtPosition(entity.transform.position);
            if (controllerEvent == null || controllerEvent.Owner == 0) return null;

            switch (type)
            {
                case EntityType.Bradley when controllerEvent.Tanks.Count == 0:
                case EntityType.Helicopter when controllerEvent.Helicopters.Count == 0:
                    return null;
            }

            BasePlayer targetPlayer = entity as BasePlayer;

            if (targetPlayer.IsPlayer())
            {
                switch (type)
                {
                    case EntityType.Bradley when controllerEvent.Config.TargetTank:
                    case EntityType.Helicopter when controllerEvent.Config.TargetHelicopter:
                        return null;
                }
                if (IsTeam(targetPlayer, controllerEvent.Owner)) return null;
                else return true;
            }
            else
            {
                if (IsTeam(entity.OwnerID, controllerEvent.Owner)) return null;
                else return true;
            }
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || entity.net == null || info == null) return null;

            if (Scientists.Count > 0 && entity is ScientistNPC npc)
            {
                if (Scientists.TryGetValue(npc.net.ID.Value, out ControllerScientist controllerScientist))
                {
                    BasePlayer attacker = info.InitiatorPlayer;
                    if (attacker.IsPlayer()) controllerScientist.AddDamage(attacker, info.damageTypes.Total());
                    return null;
                }
            }

            if (Events.Count > 0)
            {
                if (OnEventEntityTakeDamage(entity, info) is bool || OnEventEntityTarget(info.Initiator, entity) is bool || OnEventInitiatorNullTakeDamage(entity, info) is bool)
                    return CanBlockDamageByApi(entity, info) is bool ? true : null;
            }

            return null;
        }

        private object OnPlayerAttack(BasePlayer attacker, HitInfo info) => info == null ? null : OnEventEntityTakeDamage(info.HitEntity as PatrolHelicopter, info);

        private object CanEntityTakeDamage(ScientistNPC npc, HitInfo info) => OnEventEntityTakeDamage(npc, info, false, false) is bool ? CanBlockDamageByApi(npc, info) : null;
        private object CanEntityTakeDamage(BaseAnimalNPC animal, HitInfo info) => OnEventEntityTakeDamage(animal, info, false, false) is bool ? CanBlockDamageByApi(animal, info) : null;

        private object CanEntityTakeDamage(BradleyAPC bradley, HitInfo info) => OnEventEntityTakeDamage(bradley, info, false, false) is bool ? CanBlockDamageByApi(bradley, info) : null;

        private object CanEntityTakeDamage(AutoTurret turret, HitInfo info)
        {
            if (OnEventEntityTakeDamage(turret, info, false, false) is bool) return CanBlockDamageByApi(turret, info);
            else return IsEventTurret(turret) ? CanAllowDamageByApi(turret, info) : null;
        }
        private object CanEntityTakeDamage(FlameTurret turret, HitInfo info)
        {
            if (OnEventEntityTakeDamage(turret, info, false, false) is bool) return CanBlockDamageByApi(turret, info);
            else return IsEventTurret(turret) ? CanAllowDamageByApi(turret, info) : null;
        }
        private object CanEntityTakeDamage(GunTrap turret, HitInfo info)
        {
            if (OnEventEntityTakeDamage(turret, info, false, false) is bool) return CanBlockDamageByApi(turret, info);
            else return IsEventTurret(turret) ? CanAllowDamageByApi(turret, info) : null;
        }
        private object CanEntityTakeDamage(SamSite turret, HitInfo info)
        {
            if (OnEventEntityTakeDamage(turret, info, false, false) is bool) return CanBlockDamageByApi(turret, info);
            else return IsEventTurret(turret) ? CanAllowDamageByApi(turret, info) : null;
        }

        private static object CanBlockDamageByApi(BaseNetworkable entity, HitInfo info)
        {
            if (Interface.CallHook("CanPveModeBlockDamage", entity, info) is bool) return null;
            else return false;
        }

        private static object CanAllowDamageByApi(BaseNetworkable entity, HitInfo info)
        {
            if (Interface.CallHook("CanPveModeAllowDamage", entity, info) is bool) return null;
            else return true;
        }

        private void OnEntityKill(BradleyAPC bradley)
        {
            if (bradley == null || bradley.net == null || Events.Count == 0) return;
            ulong id = bradley.net.ID.Value;
            ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.Tanks.Contains(id));
            if (controllerEvent != null) controllerEvent.Tanks.Remove(id);
        }

        private void OnEntityKill(PatrolHelicopter heli)
        {
            if (heli == null || heli.net == null || Events.Count == 0) return;
            ulong id = heli.net.ID.Value;
            ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.Helicopters.Contains(id));
            if (controllerEvent != null) controllerEvent.Helicopters.Remove(id);
        }

        private void OnEntityKill(AutoTurret turret)
        {
            if (turret == null || turret.net == null || Events.Count == 0) return;
            OnTurretKill(turret.net.ID.Value);
        }

        private void OnEntityKill(FlameTurret turret)
        {
            if (turret == null || turret.net == null || Events.Count == 0) return;
            OnTurretKill(turret.net.ID.Value);
        }

        private void OnEntityKill(GunTrap turret)
        {
            if (turret == null || turret.net == null || Events.Count == 0) return;
            OnTurretKill(turret.net.ID.Value);
        }

        private void OnEntityKill(SamSite turret)
        {
            if (turret == null || turret.net == null || Events.Count == 0) return;
            OnTurretKill(turret.net.ID.Value);
        }

        private void OnTurretKill(ulong id)
        {
            ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.Turrets.Contains(id));
            if (controllerEvent != null) controllerEvent.Turrets.Remove(id);
        }
        #endregion Damage

        #region Target
        private object OnEventEntityTarget(BaseNetworkable attacker, BaseEntity target)
        {
            if (attacker == null || attacker.net == null || target == null) return null;

            EntityType type = EntityType.Default;
            ControllerEvent controllerEvent = null;

            switch (attacker)
            {
                case ScientistNPC _:
                    controllerEvent = Events.FirstOrDefault(x => x.Owner != 0 && x.Npc.Contains(attacker.net.ID.Value));
                    type = EntityType.Npc;
                    break;
                case BaseAnimalNPC _:
                    controllerEvent = Events.FirstOrDefault(x => x.Owner != 0 && x.Npc.Contains(attacker.net.ID.Value));
                    type = EntityType.Animal;
                    break;
                case BradleyAPC _:
                    controllerEvent = Events.FirstOrDefault(x => x.Owner != 0 && x.Tanks.Contains(attacker.net.ID.Value));
                    type = EntityType.Bradley;
                    break;
                case AutoTurret _:
                case FlameTurret _:
                case GunTrap _:
                case SamSite _:
                    controllerEvent = Events.FirstOrDefault(x => x.Owner != 0 && x.Turrets.Contains(attacker.net.ID.Value));
                    type = EntityType.Turret;
                    break;
            }

            if (controllerEvent == null) return null;

            switch (type)
            {
                case EntityType.Npc:
                case EntityType.Animal:
                    {
                        if (controllerEvent.Config.TargetNpc) return null;
                        break;
                    }
                case EntityType.Bradley when controllerEvent.Config.TargetTank:
                case EntityType.Turret when controllerEvent.Config.TargetTurret:
                    return null;
            }

            if (type == EntityType.Npc)
            {
                if (target is ScientistNPC && target.net != null && controllerEvent.Npc.Contains(target.net.ID.Value)) return null;
                if (target is BasePlayer && target.skinID is 19395142091920 or 8151920175) return null;
            }

            BasePlayer targetPlayer = target as BasePlayer;

            if (targetPlayer.IsPlayer())
            {
                if (IsTeam(targetPlayer, controllerEvent.Owner)) return null;
                else return true;
            }
            else
            {
                if (IsTeam(target.OwnerID, controllerEvent.Owner)) return null;
                else return true;
            }
        }
        
        private object OnNpcTarget(ScientistNPC attacker, BaseEntity target) => OnEventEntityTarget(attacker, target) is bool ? CanBlockTargetByApi(target, attacker) is bool ? true : null : null;
        private object OnNpcTarget(BaseAnimalNPC attacker, BaseEntity target) => OnEventEntityTarget(attacker, target) is bool ? CanBlockTargetByApi(target, attacker) is bool ? true : null : null;
        private object OnCustomNpcTarget(ScientistNPC attacker, BasePlayer target) => OnEventEntityTarget(attacker, target) is bool ? CanBlockTargetByApi(target, attacker) : null;
        private object OnCustomAnimalTarget(BaseAnimalNPC attacker, BaseEntity target) => OnEventEntityTarget(attacker, target) is bool ? CanBlockTargetByApi(target, attacker) : null;

        private object CanBradleyApcTarget(BradleyAPC attacker, BaseEntity target) => OnEventEntityTarget(attacker, target) is bool ? CanBlockTargetByApi(target, attacker) : null;
        
        private object OnEntityEnter(TargetTrigger trigger, BasePlayer target)
        {
            if (trigger == null || !target.IsPlayer()) return null;

            DecayEntity attacker = trigger.GetComponentInParent<DecayEntity>();
            if (attacker == null) return null;

            if (!IsEventTurret(attacker)) return null;

            return OnEventEntityTarget(attacker, target) is bool ? CanBlockTargetByApi(target, attacker) is bool ? true : null : null;
        }

        private object OnSamSiteTarget(SamSite attacker, BaseEntity target) => OnEventEntityTarget(attacker, target) is bool ? CanBlockTargetByApi(target, attacker) is bool ? true : null : null;
        
        private object CanHelicopterTarget(PatrolHelicopterAI heli, BasePlayer player)
        {
            if (heli == null || !player.IsPlayer()) return null;

            PatrolHelicopter helicopter = heli.helicopterBase;
            if (helicopter == null || helicopter.net == null) return null;

            ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.Owner != 0 && x.Helicopters.Contains(helicopter.net.ID.Value));
            if (controllerEvent == null || controllerEvent.Config.TargetHelicopter) return null;

            if (IsTeam(player, controllerEvent.Owner)) return null;
            else return CanBlockTargetByApi(player, helicopter);
        }

        private object CanHelicopterStrafeTarget(PatrolHelicopterAI heli, BasePlayer player) => CanHelicopterTarget(heli, player);

        private object OnHelicopterTarget(HelicopterTurret turret, BasePlayer player)
        {
            if (turret == null || turret._heliAI == null || !player.IsPlayer()) return null;

            PatrolHelicopter helicopter = turret._heliAI.helicopterBase;
            if (helicopter == null || helicopter.net == null) return null;

            ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.Owner != 0 && x.Helicopters.Contains(helicopter.net.ID.Value));
            if (controllerEvent == null || controllerEvent.Config.TargetHelicopter) return null;

            if (IsTeam(player, controllerEvent.Owner)) return null;
            else return CanBlockTargetByApi(player, helicopter) is bool ? true : null;
        }

        private object CanEntityBeTargeted(BaseEntity target, SamSite attacker)
        {
            if (OnEventEntityTarget(attacker, target) is bool) return CanBlockTargetByApi(target, attacker);
            else return IsEventTurret(attacker) ? CanAllowTargetByApi(target, attacker) : null;
        }

        private object CanEntityBeTargeted(BasePlayer target, BaseEntity attacker)
        {
            if (OnEventEntityTarget(attacker, target) is bool) return CanBlockTargetByApi(target, attacker);
            else return IsEventTurret(attacker) ? CanAllowTargetByApi(target, attacker) : null;
        }

        private static object CanBlockTargetByApi(BaseNetworkable target, BaseNetworkable attacker)
        {
            if (Interface.CallHook("CanPveModeBlockTarget", target, attacker) is bool) return null;
            else return false;
        }

        private static object CanAllowTargetByApi(BaseNetworkable target, BaseNetworkable attacker)
        {
            if (Interface.CallHook("CanPveModeAllowTarget", target, attacker) is bool) return null;
            else return true;
        }
        #endregion Target

        #region Hooks
        private readonly HashSet<string> _hooks = new HashSet<string>()
        {
            "CanEntityBeTargeted",
            "OnHelicopterTarget",
            "CanHelicopterStrafeTarget",
            "CanHelicopterTarget",
            "OnSamSiteTarget",
            "OnEntityEnter",
            "CanBradleyApcTarget",
            "OnCustomAnimalTarget",
            "OnCustomNpcTarget",
            "OnNpcTarget",
            "CanEntityTakeDamage",
            "OnPlayerAttack",
            "OnDispenserGather",
            "CanCustomAnimalSpawnCorpse",
            "CanHackCrate",
            "OnRestoreUponDeath",
            "OnEntityDismounted",
            "OnPlayerDeath"
        };

        private void Unsubscribes() { foreach (string hook in _hooks) Unsubscribe(hook); }

        private void Subscribes() { foreach (string hook in _hooks) Subscribe(hook); }
        #endregion Hooks

        #region API
        private bool IsPlayerInEventZone(ulong id) => Events.Any(x => x.InsidePlayers.Any(y => y.userID == id));

        private HashSet<string> GetEventsPlayer(ulong id)
        {
            HashSet<string> result = new HashSet<string>();
            foreach (ControllerEvent controller in Events) if (controller.InsidePlayers.Any(x => x.userID == id)) result.Add(controller.ShortName);
            return result;
        }

        private Dictionary<string, double> GetTimesPlayer(ulong id)
        {
            PlayerData playerData = PlayersData.FirstOrDefault(x => x.SteamId == id);
            if (playerData == null) return null;
            Dictionary<string, double> result = new Dictionary<string, double>();
            foreach (KeyValuePair<string, double> dic in playerData.LastTime) result.Add(dic.Key, CurrentTime - dic.Value);
            return result;
        }

        private ulong GetEventOwner(string shortname)
        {
            ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.ShortName == shortname);
            return controllerEvent == null ? 0 : controllerEvent.Owner;
        }

        private HashSet<ulong> GetEventOwners(string shortname)
        {
            ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.ShortName == shortname);
            return controllerEvent == null ? null : controllerEvent.Owners;
        }

        private Dictionary<ulong, float> GetScientistPlayerDamageMap(ulong netId) => Scientists.TryGetValue(netId, out ControllerScientist controller) ? controller.Players : null;
        #endregion API
    }
}

namespace Oxide.Plugins.PveModeExtensionMethods
{
    public static class ExtensionMethods
    {
        public static bool Any<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) if (predicate(enumerator.Current)) return true;
            return false;
        }

        public static TSource FirstOrDefault<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) if (predicate(enumerator.Current)) return enumerator.Current;
            return default(TSource);
        }

        public static HashSet<TSource> ToHashSet<TSource>(this IEnumerable<TSource> source)
        {
            HashSet<TSource> result = new HashSet<TSource>();
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) result.Add(enumerator.Current);
            return result;
        }

        public static TSource Max<TSource>(this IEnumerable<TSource> source, Func<TSource, float> predicate)
        {
            TSource result = default(TSource);
            float resultValue = float.MinValue;
            using (var enumerator = source.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    TSource element = enumerator.Current;
                    float elementValue = predicate(element);
                    if (elementValue > resultValue)
                    {
                        result = element;
                        resultValue = elementValue;
                    }
                }
            }
            return result;
        }

        public static bool IsPlayer(this BasePlayer player) => player != null && player.userID.IsSteamId();

        public static bool IsExists(this BaseNetworkable entity) => entity != null && !entity.IsDestroyed;
    }
}