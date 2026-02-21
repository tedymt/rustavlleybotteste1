using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using Facepunch;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using UnityEngine.UI;
using Oxide.Core.Libraries.Covalence;
using Facepunch.Extend;

#if CARBON
	using Carbon.Base;
	using Carbon.Modules;
#endif

namespace Oxide.Plugins
{
    [Info("EventsManager", "Mevent & Qbis", "3.3.7")]
    internal class EventsManager : RustPlugin
    {
        #region Fields

        [PluginReference]
        private Plugin ImageLibrary = null;

#if CARBON
        private ImageDatabaseModule imageDatabase;
#endif

        private const string PERM_USE = "EventsManager.use";

        private const string NewDot = ", ";
        private static EventsManager instance;
        private EventController controller;

        private HashSet<EventSchedule> disabledEvents = new();

        private const string
            imageDefault = "https://i.ibb.co/Df3JWnx4/default-icon.png",
            imageRandom = "https://i.ibb.co/r26hSsqL/random-icon.pngR";

        #endregion

        #region Classes

        private class Event
        {
            public string displayName = "Enter event name...";
            public string pluginName = "Enter event plugin name...";
            public string command = "Enter command...";
            public string creator = "Choose event creator...";

            public int eventId = UnityEngine.Random.Range(0, int.MaxValue);

            [JsonIgnore] public bool isLoaded;
        }

        private class EventTime : ICloneable
        {
            [JsonConverter(typeof(StringEnumConverter))]
            public EventType type = EventType.Static;

            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public HashSet<DayOfWeek> activeDays = new HashSet<DayOfWeek>();

            public int hour;
            public int minute;
            public int interval;
            public int offsetMin;
            public int offsetMax;
            public int minPlayers;
            public int maxPlayers;
            public bool isRandom;

            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<int> eventsId = new List<int>();

            [JsonIgnore] public DateTime lastRun = DateTime.Now;
            [JsonIgnore] public int nextOffset = 0;
            [JsonIgnore] public string displayInfo;

            public void UpdateOffset() => nextOffset = offsetMin != 0 && offsetMax != 0 && offsetMax >= offsetMin ? UnityEngine.Random.Range(offsetMin, offsetMax + 1) : 0;

            public void UpdateInfo()
            {
                var sb = Pool.Get<StringBuilder>();

                switch (type)
                {
                    case EventType.Static:
                        {
                            sb.AppendFormat("{0:00}:{1:00} - ", hour, minute);
                            GetDays(ref sb);
                            break;
                        }

                    case EventType.Every:
                        {
                            sb.AppendFormat("Every {0} minutes", interval);
                            break;
                        }

                    case EventType.Random:
                        {
                            sb.AppendFormat($"Every {offsetMin} - {offsetMax} minutes");
                            break;
                        }
                }

                displayInfo = sb.ToString();
                Pool.FreeUnmanaged(ref sb);
            }


            public void GetDays(ref StringBuilder sb)
            {
                switch (activeDays.Count)
                {
                    case 3:
                    case 4:
                        {
                            foreach (var d in activeDays)
                                sb.Append(d.ToString().Substring(0, 3)).Append(NewDot);

                            sb.Length -= 2;
                            break;
                        }

                    case 5:
                    case 6:
                        {
                            foreach (var d in activeDays)
                                sb.Append(d.ToString().Substring(0, 1)).Append(NewDot);

                            sb.Length -= 2;
                            break;
                        }

                    case 7:
                    case 0:
                        {
                            sb.Append("Everyday");
                            break;
                        }

                    default:
                        {
                            foreach (var d in activeDays)
                                sb.Append(d.ToString()).Append(NewDot);

                            sb.Length -= 2;
                            break;
                        }
                }
            }

            public object Clone()
            {
                return MemberwiseClone();
            }
        }

        private enum EventType
        {
            Static,
            Every,
            Random
        }

        #region DiscordEmbedMessage
        public class DiscordMessage
        {
            public string content { get; set; }
            public DiscordEmbed[] embeds { get; set; }

            public DiscordMessage(string Content, DiscordEmbed[] Embeds = null)
            {
                content = Content;
                embeds = Embeds;
            }

            public void Send(string url)
            {
                instance.webrequest.Enqueue(url, JsonConvert.SerializeObject(this), (code, response) =>
                {
                    if (code == 200)
                    {
                        return;
                    }

                }, instance, Core.Libraries.RequestMethod.POST, _Headeers, 30f);
            }

            public static Dictionary<string, string> _Headeers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json"
            };
        }
        public class DiscordEmbed
        {
            public string title { get; set; }
            public string description { get; set; }
            public int? color { get; set; }
            public DiscordField[] fields { get; set; }
            public DiscordFooter footer { get; set; }
            public DiscordAuthor author { get; set; }

            public DiscordEmbed(string Title, string Description, int? Color = null, DiscordField[] Fields = null, DiscordFooter Footer = null, DiscordAuthor Author = null)
            {
                title = Title;
                description = Description;
                color = Color;
                fields = Fields;
                footer = Footer;
                author = Author;
            }
        }
        public class DiscordFooter
        {
            public string text { get; set; }
            public string icon_url { get; set; }
            public string proxy_icon_url { get; set; }

            public DiscordFooter(string Text, string Icon_url, string Proxy_icon_url = null)
            {
                text = Text;
                icon_url = Icon_url;
                proxy_icon_url = Proxy_icon_url;
            }
        }
        public class DiscordAuthor
        {
            public string name { get; set; }
            public string url { get; set; }
            public string icon_url { get; set; }
            public string proxy_icon_url { get; set; }

            public DiscordAuthor(string Name, string Url, string Icon_url, string Proxy_icon_url = null)
            {
                name = Name;
                url = Url;
                icon_url = Icon_url;
                proxy_icon_url = Proxy_icon_url;
            }
        }
        public class DiscordField
        {
            public string name { get; set; }
            public string value { get; set; }
            public bool inline { get; set; }

            public DiscordField(string Name, string Value, bool Inline = false)
            {
                name = Name;
                value = Value;
                inline = Inline;
            }

        }
        #endregion DiscordEmbedMessage
        #endregion

        #region Configuration

        private Configuration _config;

        private class Configuration
        {
            [JsonProperty(PropertyName = "Commands", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public string[] Commands = new string[]
            {
                "em",
            };

            [JsonProperty(PropertyName = "Disable vanilla auto events?")]
            public bool DisableVanillaAutoEvents = true;

            [JsonProperty(PropertyName = "Allowed list of vanilla events", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> VanillaEvents = new List<string>
            {
                "event_f15e"
            };

            [JsonProperty(PropertyName = "Discord web hook")]
            public string DiscordWebHook = string.Empty;

            [JsonProperty(PropertyName = "Enable event logging to file")]
            public bool EnableEventLogging = false;

            public VersionNumber Version;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) throw new JsonException();

                if (_config.Version < Version)
                    UpdateConfigValues();

                SaveConfig();
            }
            catch (Exception ex)
            {
                PrintError("Your configuration file contains an error!");
                Debug.LogException(ex);
            }
        }

        protected override void SaveConfig()
        {
            Config?.WriteObject(_config, true);
        }

        protected override void LoadDefaultConfig()
        {
            _config = new Configuration();
        }

        private void UpdateConfigValues()
        {
            PrintWarning("Config update detected! Updating config values...");
            if (_config.Version == default || _config.Version < new VersionNumber(3, 2, 0))
            {
                _config.DiscordWebHook = string.Empty;

                if (data == null) LoadData();

                var anyChanged = false;
                foreach (var eventData in data.eventTimes)
                {
                    eventData.isRandom = eventData.eventsId?.Count > 1;
                    if (eventData.isRandom) anyChanged = true;
                }

                if (anyChanged) data?.SaveTimes(false);

                _config.Version = Version;
            }

            if (_config.Version != default)
            {
                if (_config.Version < new VersionNumber(3, 3, 3))
                {
                    if (data == null) LoadData();

                    data.creators.Add("Mevent");

                    var collection = new List<Event>()
                    {
                        new Event()
                        {
                            displayName = "Collection Resources",
                            command = "collectionresources.start",
                            creator = "Mevent"
                        },
                        new Event()
                        {
                            displayName = "Foundation Drop",
                            command = "foundationdrop.start",
                            creator = "Mevent"
                        },
                        new Event()
                        {
                            displayName = "Gold Rush",
                            command = "goldrush.start",
                            creator = "Mevent"
                        },
                        new Event()
                        {
                            displayName = "Helicopter Pet",
                            command = "helicopterpet.start",
                            creator = "Mevent"
                        },
                        new Event()
                        {
                            displayName = "Hunt Animal",
                            command = "huntanimal.start",
                            creator = "Mevent"
                        },
                        new Event()
                        {
                            displayName = "King Of Hill",
                            command = "kingofhill.start",
                            creator = "Mevent"
                        },
                        new Event()
                        {
                            displayName = "Looking Loot",
                            command = "lookingloot.start",
                            creator = "Mevent"
                        },
                        new Event()
                        {
                            displayName = "Special Cargo",
                            command = "specialcargo.start",
                            creator = "Mevent"
                        }
                    };

                    data.events.AddRange(collection);

                    SaveConfig();
                    data.SaveCreators();
                    data.SaveEvents();

                    data.eventTimes?.ForEach(e =>
                    {
                        e.UpdateInfo();
                        e.UpdateOffset();
                    });
                }

                if (_config.Version < new VersionNumber(3, 3, 5))
                {
                    if (data == null) LoadData();

                    try
                    {
                        foreach (var eventData in data?.events)
                        {
                            if (eventData.command.Contains("assets/prefabs/misc/supply drop/supply_drop.prefab"))
                            {
                                eventData.command = eventData.command.Replace("assets/prefabs/misc/supply drop/supply_drop.prefab", "assets/prefabs/npc/cargo plane/cargo_plane.prefab");
                            }
                        }
                    }
                    finally
                    {
                        data?.SaveEvents();

                        data.eventTimes?.ForEach(e =>
                        {
                            e.UpdateInfo();
                            e.UpdateOffset();
                        });
                    }
                }
            }

            PrintWarning("Config update completed!");
        }

        #endregion

        #region Hooks

        private void OnServerInitialized()
        {
            instance = this;

            AddCovalenceCommand(_config.Commands, nameof(CmdNewEventManager));

            permission.RegisterPermission(PERM_USE, this);

            LoadData();

            data?.CheckEmptyData();

            DisableVanillaEvents();

            CreateController();

            UpdateEventList();

            LoadImages();

            CheckIsEventsLoaded();
        }

        private void Unload()
        {
            DestroyController();

            EnableVanillaEvents();

            DestroyUI();

            instance = null;
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;

            _interface.Remove(player.userID);
        }

        #endregion

        #region Utils

        private void LoadImages()
        {
#if CARBON
			imageDatabase = BaseModule.GetModule<ImageDatabaseModule>();
#endif

            var imagesList = new Dictionary<string, string>()
            {
                [imageDefault] = imageDefault,
                [imageRandom] = imageRandom
            };

#if CARBON
            imageDatabase.Queue(false, imagesList);
#else
            timer.In(1f, () =>
            {
                if (ImageLibrary is not { IsLoaded: true })
                {
                    PrintError("IMAGE LIBRARY IS NOT INSTALLED!");
                    return;
                }

                ImageLibrary?.Call("ImportImageList", Title, imagesList, 0UL, true);
            });
#endif
        }

        private string GetImage(string name)
        {
#if CARBON
			return imageDatabase.GetImageString(name);
#else
            return Convert.ToString(ImageLibrary?.Call("GetImage", name));
#endif
        }

        private void DisableVanillaEvents()
        {
            if (!_config.DisableVanillaAutoEvents) return;

            var eventSchedules = EventSchedule.allEvents.ToArray();

            for (var i = eventSchedules.Length - 1; i >= 0; i--)
            {
                var eventSchedule = eventSchedules[i];

                if (_config.VanillaEvents.Contains(eventSchedule.GetName())) continue;

                eventSchedule.enabled = false;

                disabledEvents.Add(eventSchedule);
            }

            var sb = Pool.Get<StringBuilder>();
            try
            {
                sb.Append("Vanilla events are disabled: ");
                foreach (var ev in disabledEvents)
                    sb.Append(ev.GetName()).Append(", ");

                sb.Length -= 2;

                Puts(sb.ToString());
            }
            finally
            {
                Pool.FreeUnmanaged(ref sb);
            }
        }

        private void EnableVanillaEvents()
        {
            if (_config.DisableVanillaAutoEvents)
                foreach (var ev in disabledEvents)
                    ev.enabled = true;
        }

        private void CreateController()
        {
            controller = ServerMgr.Instance.gameObject.AddComponent<EventController>();
        }

        private void DestroyController()
        {
            if (controller != null)
                UnityEngine.Object.Destroy(controller);
        }

        private void DestroyUI()
        {
            foreach (var userId in _interface.Keys)
            {
                var player = BasePlayer.FindByID(userId);
                if (player == null) continue;
                CuiHelper.DestroyUi(player, LayerBackground);
            }
        }

        private void CheckIsEventsLoaded()
        {
            foreach (var @event in data.events)
            {
                if (@event.creator == "Facepunch")
                {
                    @event.isLoaded = true;
                    continue;
                }

                @event.isLoaded = instance.plugins.Exists(@event.pluginName);
            }
        }

        private void CheckIsEventsLoaded(string eventName)
        {
            var @event = data.events.Find(e => e.displayName == eventName);
            if (@event == null) return;

            @event.isLoaded = instance.plugins.Exists(@event.pluginName);

        }

        private void SendLogToDiscord(string eventName)
        {
            DiscordEmbed embed = new DiscordEmbed($"Event Manager", "", 0x1F8B4C, null, new DiscordFooter($"{eventName}", "https://i.ibb.co/pvTs1Wcf/free-icon-schedule-6829928.png"), null);
            DiscordMessage req = new DiscordMessage(null, new DiscordEmbed[1] { embed });

            req.Send(_config.DiscordWebHook);
        }
        #endregion

        #region Data

        private PluginData data;

        private void LoadData() => data = new PluginData();

        private class PluginData
        {
            public List<string> creators = new List<string>();
            public List<Event> events = new List<Event>();
            public List<EventTime> eventTimes = new List<EventTime>();

            public PluginData() { LoadData(); }

            public void SaveData()
            {
                SaveCreators();
                SaveEvents();
                SaveTimes();
            }

            public void SaveCreators() => Interface.Oxide.DataFileSystem.WriteObject("EventManager" + Path.DirectorySeparatorChar + "CreatorsSettings", creators);

            public void SaveEvents() => Interface.Oxide.DataFileSystem.WriteObject("EventManager" + Path.DirectorySeparatorChar + "EventsSettings", events);

            public void SaveTimes(bool needUpdateEvents = true)
            {
                Interface.Oxide.DataFileSystem.WriteObject("EventManager" + Path.DirectorySeparatorChar + "TimesSettings", eventTimes);

                if (needUpdateEvents) instance.UpdateEventList();
            }

            public void LoadData()
            {
                try
                {
                    creators = Interface.Oxide.DataFileSystem.ReadObject<List<string>>("EventManager" + Path.DirectorySeparatorChar + "CreatorsSettings");

                    events = Interface.Oxide.DataFileSystem.ReadObject<List<Event>>("EventManager" + Path.DirectorySeparatorChar + "EventsSettings");

                    eventTimes = Interface.Oxide.DataFileSystem.ReadObject<List<EventTime>>("EventManager" + Path.DirectorySeparatorChar + "TimesSettings");


                }
                catch (Exception e)
                {
                    instance.PrintError(e.ToString());
                }

                eventTimes?.ForEach(e =>
                {
                    e.UpdateInfo();
                    e.UpdateOffset();
                });
            }

            public void CheckEmptyData()
            {
                if (creators.Count == 0 && events.Count == 0 && eventTimes.Count == 0)
                {
                    ResetToDefault();

                    instance.PrintWarning("Load default data for Event Manager");
                }
            }

            public void ResetToDefault()
            {
                LoadDefaultEvents();
                LoadDefaultCreators();
                eventTimes.Clear();

                SaveData();
                instance.UpdateEventList();
            }

            public void LoadDefaultCreators()
            {
                creators = new()
                {
                    "Facepunch",
                    "Mevent",
                    "KpucTaJl",
                    "Adem",
                    "Mercury",
                    "Yac Vaguer",
                    "DezLife",
                    "Cashr",
                    "Nikedemos",
                    "Ridamees",
                    "Fruster",
                    "Bazz3l",
                    "Nivex",
                    "Dana",
                    "Razor",
                    "ThePitereq",
                    "k1lly0u",
                    "Wrecks"
                };
            }

            public void LoadDefaultEvents()
            {
                events = new()
                {
                    //Facepunch
                    new Event()
                    {
                        displayName = "Helicopter",
                        command = "em_spawn assets/prefabs/npc/patrol helicopter/patrolhelicopter.prefab",
                        creator = "Facepunch"
                    },
                    new Event()
                    {
                        displayName = "Airdrop",
                        command = "em_spawn assets/prefabs/npc/cargo plane/cargo_plane.prefab",
                        creator = "Facepunch"
                    },
                    new Event()
                    {
                        displayName = "Chinook 47",
                        command = "em_spawn assets/prefabs/npc/ch47/ch47scientists.entity.prefab",
                        creator = "Facepunch"
                    },
                    new Event()
                    {
                        displayName = "Cargoship",
                        command = "em_spawn assets/content/vehicles/boats/cargoship/cargoshiptest.prefab",
                        creator = "Facepunch"
                    },
                    //Mevent
                    new Event()
                    {
                        displayName = "Collection Resources",
                        command = "collectionresources.start",
                        creator = "Mevent"
                    },
                    new Event()
                    {
                        displayName = "Foundation Drop",
                        command = "foundationdrop.start",
                        creator = "Mevent"
                    },
                    new Event()
                    {
                        displayName = "Gold Rush",
                        command = "goldrush.start",
                        creator = "Mevent"
                    },
                    new Event()
                    {
                        displayName = "Helicopter Pet",
                        command = "helicopterpet.start",
                        creator = "Mevent"
                    },
                    new Event()
                    {
                        displayName = "Hunt Animal",
                        command = "huntanimal.start",
                        creator = "Mevent"
                    },
                    new Event()
                    {
                        displayName = "King Of Hill",
                        command = "kingofhill.start",
                        creator = "Mevent"
                    },
                    new Event()
                    {
                        displayName = "Looking Loot",
                        command = "lookingloot.start",
                        creator = "Mevent"
                    },
                    new Event()
                    {
                        displayName = "Special Cargo",
                        command = "specialcargo.start",
                        creator = "Mevent"
                    },
                    //KpucTaJl
                    new Event()
                    {
                        displayName = "Air Event",
                        command = "airstart",
                        creator = "KpucTaJl"
                    },
                    new Event()
                    {
                        displayName = "Water Event",
                        command = "waterstart",
                        creator = "KpucTaJl"
                    },
                    new Event()
                    {
                        displayName = "Arctic Base Event",
                        command = "abstart",
                        creator = "KpucTaJl"
                    },
                    new Event()
                    {
                         displayName = "Satellite Dish Event",
                        command = "satdishstart",
                        creator = "KpucTaJl"
                    },
                    new Event()
                    {
                       displayName = "Junkyard Event",
                           command = "jstart",
                        creator = "KpucTaJl"
                    },
                    new Event()
                    {
                        displayName = "Power Plant Event",
                            command = "ppstart",
                        creator = "KpucTaJl"
                    },
                    new Event()
                    {
                       displayName = "Harbor Event",
                            command = "harborstart",
                        creator = "KpucTaJl"
                    },
                    new Event()
                    {
                      displayName = "Squid Game",
                            command = "rlglstart",
                        creator = "KpucTaJl"
                    },
                    new Event()
                    {
                         displayName = "Triangulation",
                            command = "tstart",
                        creator = "KpucTaJl"
                    },
                    new Event()
                    {
                        displayName = "Supermarket Event",
                            command = "supermarketstart",
                        creator = "KpucTaJl"
                    },
                    new Event()
                    {
                         displayName = "Ferry Terminal Event",
                            command = "ftstart",
                        creator = "KpucTaJl"
                    },
                    new Event()
                    {
                         displayName = "GasStation Event",
                            command = "gsstart",
                        creator = "KpucTaJl"
                    },
                    new Event()
                    {
                        displayName = "Defendable-Bases Start",
                        command = "warstart",
                        creator = "KpucTaJl"
                    },
                    new Event()
                    {
                        displayName = "Defendable-Bases Stop",
                        command = "warstop",
                        creator = "KpucTaJl"
                    },
                    //Adem
                    new Event()
                    {
                        displayName = "Sputnik",
                        command = "sputnikstart",
                        creator = "Adem"
                    },
                    new Event()
                    {
                        displayName = "Armored Train",
                        command = "atrainstart",
                        creator = "Adem"
                    },
                    new Event()
                    {
                        displayName = "Space",
                        command = "spacestart",
                        creator = "Adem"
                    },
                    new Event()
                    {
                        displayName = "Convoy",
                        command = "convoystart",
                        creator = "Adem"
                    },
                    new Event()
                    {
                        displayName = "Shipwreck",
                        command = "shipwreckstart",
                        creator = "Adem"
                    },
                    new Event()
                    {
                        displayName = "Caravan",
                        command = "caravanstart",
                        creator = "Adem"
                    },
                    //Mercury
                    new Event()
                    {
                        displayName = "IQSphereEvent",
                        command = "iqsp start",
                        creator = "Mercury"
                    },
                    //Yac Vaguer
                     new Event()
                    {
                        displayName = "Water-Treatment-Showdown",
                        command = "wtestart",
                        creator = "Yac Vaguer"
                    },
                     //DezLife
                     new Event()
                    {
                        displayName = "Cobalt Laboratory",
                        command = "cl start",
                        creator = "DezLife"
                    },
                     new Event()
                    {
                        displayName = "XDChinookEvent",
                        command = "chinook call",
                        creator = "DezLife"
                    },
                     //Cashr
                     new Event()
                    {
                        displayName = "Rocket Event",
                        command = "rocket ||",
                        creator = "Cashr"
                    },
                     //Nikedemos
                     new Event()
                    {
                        displayName = "CargoTrainEvent",
                        command = "trainevent_now",
                        creator = "Nikedemos"
                    },
                     //Ridamees
                     new Event()
                    {
                        displayName = "Stone Event",
                        command = "StoneStart",
                        creator = "Ridamees"
                    },
                     new Event()
                    {
                        displayName = "Metal Event",
                        command = "MetalStart",
                        creator = "Ridamees"
                    },
                     new Event()
                    {
                        displayName = "Sulfur Event",
                        command = "SulfurStart",
                        creator = "Ridamees"
                    },
                     new Event()
                    {
                        displayName = "RaidRush Event",
                        command = "raidrush.start",
                        creator = "Ridamees"
                    },
                     new Event()
                    {
                        displayName = "BarrelBash Event",
                        command = "StartBarrelBash",
                        creator = "Ridamees"
                    },
                     new Event()
                    {
                        displayName = "BoxBattle Event",
                        command = "StartBoxBattle",
                        creator = "Ridamees"
                    },
                     //Fruster
                     new Event()
                    {
                        displayName = "CargoPlaneEvent",
                        command = "callcargoplane",
                        creator = "Fruster"
                    },
                     new Event()
                    {
                        displayName = "AirfieldEvent",
                        command = "afestart",
                        creator = "Fruster"
                    },
                     new Event()
                    {
                        displayName = "HelipadEvent",
                        command = "hpestart",
                        creator = "Fruster"
                    },
                     //Bazz3l
                     new Event()
                    {
                        displayName = "GuardedCrate Easy",
                        command = "gcrate start Easy",
                        creator = "Bazz3l"
                    },
                     new Event()
                    {
                        displayName = "GuardedCrate Medium",
                        command = "gcrate start Medium",
                        creator = "Bazz3l"
                    },
                     new Event()
                    {
                        displayName = "GuardedCrate Hard",
                        command = "gcrate start Hard",
                        creator = "Bazz3l"
                    },
                     new Event()
                    {
                        displayName = "GuardedCrate Elite",
                        command = "gcrate start Elite",
                        creator = "Bazz3l"
                    },
                     //Nivex
                     new Event()
                    {
                        displayName = "Dangerous Treasures",
                        command = "dtevent",
                        creator = "Nivex"
                    },
                     new Event()
                    {
                        displayName = "RaidableBases Easy",
                        command = "rbevent easy",
                        creator = "Nivex"
                    },
                     new Event()
                    {
                        displayName = "RaidableBases Medium",
                        command = "rbevent medium",
                        creator = "Nivex"
                    },
                     new Event()
                    {
                        displayName = "RaidableBases Hard",
                        command = "rbevent hard",
                        creator = "Nivex"
                    },
                     new Event()
                    {
                        displayName = "RaidableBases Nightmare",
                        command = "rbevent nightmare",
                        creator = "Nivex"
                    },
                     //Dana
                     new Event()
                    {
                        displayName = "Heli Regular",
                        command = "heli.call Regular",
                        creator = "Dana"
                    },
                     new Event()
                    {
                        displayName = "Heli Millitary",
                        command = "heli.call Millitary",
                        creator = "Dana"
                    },
                     new Event()
                    {
                        displayName = "Heli Elite",
                        command = "heli.call Elite",
                        creator = "Dana"
                    },
                     //Razor
                     new Event()
                    {
                        displayName = "Jet Event",
                        command = "jet event 2 2 10",
                        creator = "Razor"
                    },
                     //ThePitereq
                     new Event()
                    {
                        displayName = "Meteor-Event-Kill",
                        command = "msadmin kill",
                        creator = "ThePitereq"
                    },
                     new Event()
                    {
                        displayName = "Meteor-Event-Start-Amount-10",
                        command = "msadmin run default 10",
                        creator = "ThePitereq"
                    },
                     new Event()
                    {
                        displayName = "Meteor-Event-Start-Amount-20",
                        command = "msadmin run default 20",
                        creator = "ThePitereq"
                    },
                     new Event()
                    {
                        displayName = "Meteor-Event-Start-Amount-30",
                        command = "msadmin run default 30",
                        creator = "ThePitereq"
                    },
                     new Event()
                    {
                        displayName = "Meteor-Event-Start-Amount-40",
                        command = "msadmin run default 40",
                        creator = "ThePitereq"
                    },
                     new Event()
                    {
                        displayName = "Meteor-Event-Start-Amount-50",
                        command = "msadmin run default 50",
                        creator = "ThePitereq"
                    },
                     //k1lly0u
                     new Event()
                    {
                        displayName = "PilotEjec",
                        command = "pe call",
                        creator = "k1lly0u"
                    },
                     new Event()
                    {
                        displayName = "HeliRefuel",
                        command = "hr call",
                        creator = "k1lly0u"
                    },
                     //Wrecks
                     new Event()
                    {
                        displayName = "Eradication Event",
                        command = "Erad",
                        creator = "Wrecks"
                    },
                     new Event()
                    {
                        displayName = "Bot-Purge-Event",
                        command = "Purge",
                        creator = "Wrecks"
                    }
                };

                foreach (var @event in events)
                {
                    if (@event.creator == "Facepunch") continue;

                    if (@event.displayName.Contains("Defendable-Bases"))
                        @event.pluginName = "DefendableBases";

                    else if (@event.displayName.Contains("GuardedCrate"))
                        @event.pluginName = "GuardedCrate";

                    else if (@event.displayName.Contains("RaidableBases"))
                        @event.pluginName = "RaidableBases";

                    else if (@event.displayName.Contains("Meteor-Event"))
                        @event.pluginName = "MeteorEvent";

                    else
                    {
                        var @string = Regex.Replace(@event.displayName, @"[^\w\^0-9a-zA-Z]", "");
                        @event.pluginName = @string;
                    }
                }
                instance.CheckIsEventsLoaded();
            }
        }
        #endregion

        #region Controller
        private class EventController : FacepunchBehaviour
        {
            private void Awake()
            {
                InvokeRepeating(EventTimer, 10, 10);
            }

            private void EventTimer()
            {
                var now = DateTime.Now;
                var hour = now.Hour;
                var minute = now.Minute;
                var day = now.DayOfWeek;

                var playersCount = BasePlayer.activePlayerList.Count;

                foreach (var eventTime in instance.data.eventTimes)
                {
                    if (eventTime.eventsId.IsEmpty() || eventTime.minPlayers > playersCount || (eventTime.maxPlayers > 0 && eventTime.maxPlayers < playersCount)) continue;

                    switch (eventTime.type)
                    {
                        case EventType.Static:
                            {
                                if (!eventTime.activeDays.IsEmpty() && !eventTime.activeDays.Contains(DateTime.Now.DayOfWeek)) continue;

                                now = new DateTime(now.Year, now.Month, now.Day, eventTime.hour == 24 ? 0 : eventTime.hour, eventTime.minute, 0).AddMinutes(eventTime.nextOffset);

                                if (eventTime.lastRun.DayOfWeek == day &&
                                        eventTime.lastRun.Hour == hour &&
                                        eventTime.lastRun.Minute == minute)
                                    continue;

                                if (hour == now.Hour && minute == now.Minute)
                                {
                                    var id = eventTime.eventsId.GetRandom();

                                    var startEvent = instance.data.events.Find(e => e.eventId == id);
                                    if (startEvent == null) continue;

                                    instance.Server.Command(startEvent.command);
                                    eventTime.lastRun = DateTime.Now;

                                    CallEventManagerStartedEvent(startEvent.displayName);

                                    if (!string.IsNullOrEmpty(instance._config.DiscordWebHook)) instance.SendLogToDiscord(startEvent.displayName);
                                }
                                break;
                            }

                        case EventType.Every:
                        case EventType.Random:
                            {
                                if (!eventTime.activeDays.IsEmpty() && !eventTime.activeDays.Contains(DateTime.Now.DayOfWeek)) continue;

                                var nextRunTime = eventTime.lastRun.AddMinutes(eventTime.interval + eventTime.nextOffset);
                                var startHour = nextRunTime.Hour;
                                var startMinute = nextRunTime.Minute;

                                if (hour == startHour && minute == startMinute)
                                {
                                    var id = eventTime.eventsId[UnityEngine.Random.Range(0, eventTime.eventsId.Count)];
                                    var startEvent = instance.data.events.Find(e => e.eventId == id);
                                    if (startEvent == null) continue;
                                    instance.Server.Command(startEvent.command);
                                    eventTime.lastRun = DateTime.Now;
                                    eventTime.UpdateOffset();

                                    CallEventManagerStartedEvent(startEvent.displayName);

                                    if (!string.IsNullOrEmpty(instance._config.DiscordWebHook)) instance.SendLogToDiscord(startEvent.displayName);
                                }
                                break;
                            }
                    }
                }
            }

            private void OnDestroy()
            {
                CancelInvoke(EventTimer);
            }
        }

        #endregion

        #region API

        private static void CallEventManagerStartedEvent(string eventName)
        {
            Interface.Oxide.CallHook("OnEventStarted", eventName);

            if (instance._config.EnableEventLogging) instance.LogToFile("events", $"[{DateTime.Now}] Event started: {eventName}", instance);
        }

        private void API_AddEvent(string creatorName, string displayName, string command, string pluginName)
        {
            var @event = data.events.Find(e => e.displayName == displayName);
            if (@event != null) return;

            if (!data.creators.Contains(creatorName)) data.creators.Add(creatorName);

            data.events.Add(new Event()
            {
                creator = creatorName,
                displayName = displayName,
                command = command,
                pluginName = pluginName
            });

            CheckIsEventsLoaded(displayName);

            PrintWarning($"Event '{displayName}' added successfully under creator '{creatorName}'.");

        }

        public void UpdateEventList()
        {
            var list = Pool.Get<List<Dictionary<string, object>>>();
            try
            {
                foreach (var ev in data.eventTimes)
                {
                    if (ev.type != EventType.Static) continue;

                    var events = data.events.FindAll(e => ev.eventsId.Contains(e.eventId));
                    if (events.IsNullOrEmpty()) continue;

                    var description = $"description.{string.Join(", ", events.Select(e => e.displayName))}";

                    list.Add(new Dictionary<string, object>
                    {
                        ["Name"] = description,
                        ["Description"] = description,
                        ["Hour"] = ev.hour,
                        ["Minute"] = ev.minute,
                        ["Weeks"] = DaysEnumToArray(ev.activeDays)
                    });
                }

                if (list.Count > 0) Interface.Oxide.CallHook("AddEventManagerSchedule", list);
            }
            finally
            {
                Pool.FreeUnmanaged(ref list);
            }
        }

        private bool[] DaysEnumToArray(HashSet<DayOfWeek> days)
        {
            bool[] array = new bool[7];

            if (days.Count == 0)
                for (int i = 0; i < array.Length; i++)
                    array[i] = true;
            else
                foreach (var day in days)
                    array[(int)day] = true;

            return array;
        }

        #endregion

        #region Commands

        [ConsoleCommand("em_spawn")]
        private void cmdSpawn(ConsoleSystem.Arg arg)
        {
            if (!arg.HasArgs()) return;

            var player = arg.Player();
            if (player != null && !permission.UserHasPermission(player.UserIDString, PERM_USE)) return;

            var prefabName = string.Join(" ", arg.Args);
            if (string.IsNullOrEmpty(prefabName)) return;

            var entity = GameManager.server.CreateEntity(prefabName);
            if (entity == null) return;

            if (entity.TryGetComponent<CH47HelicopterAIController>(out var ch47)) ch47.TriggeredEventSpawn();
            else if (entity.TryGetComponent<CargoShip>(out var cargo)) cargo.TriggeredEventSpawn();
            else if (entity.TryGetComponent<SupplyDrop>(out var supply))
            {
                var size = TerrainMeta.Size / 3f;
                entity.transform.position = new Vector3(UnityEngine.Random.Range(-size.x, size.x), 400f,
                    UnityEngine.Random.Range(-size.y, size.y));
            }

            entity.Spawn();
        }

        [ConsoleCommand("eventmanager.reset.default")]
        private void CmdConsoleResetToDefault(ConsoleSystem.Arg arg)
        {
            if (!arg.IsServerside) return;

            data.ResetToDefault();

            SendReply(arg, "Load default data for Event Manager");
        }

        #endregion

        #region Interface

        private const string
            Command = "UI_EventsManager",
            LayerBackground = "EventsManager.Background",
            Layer = "EventsManager.Main",
            LayerBackgroundModal = "EventsManager.Background.Modal",
            LayerModal = "EventsManager.Main.Modal";

        private Dictionary<ulong, InterfaceAdmin> _interface = new Dictionary<ulong, InterfaceAdmin>();

        private class InterfaceAdmin
        {
            public string inputCreator;
            public int editCreatorIndex = -1;

            public Event editEvent;
            public EventTime editTime;
            public int editTimeIndex = -1;
            public int editIndex = -1;

            public int page;
        }

        #region Commands

        private void CmdNewEventManager(IPlayer covPlayer)
        {
            var player = covPlayer?.Object as BasePlayer;
            if (player == null) return;

            if (!permission.UserHasPermission(player.UserIDString, PERM_USE))
            {
                PrintError($"Player {player.UserIDString} don't have permission {PERM_USE} to use /em.");
                return;
            }

            _interface[player.userID] = new InterfaceAdmin();
            DrawMainUI(player);
        }

        [ConsoleCommand(Command)]
        private void CmdNewConsoleEventManager(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs() || !permission.UserHasPermission(player.UserIDString, PERM_USE))
            {
                return;
            }

            if (!_interface.TryGetValue(player.userID, out var @interface)) return;

            switch (arg.Args[0])
            {
                case "close":
                    {
                        CuiHelper.DestroyUi(player, LayerBackground);

                        _interface.Remove(player.userID);
                        break;
                    }

                case "page":
                    {
                        if (!bool.TryParse(arg.Args[1], out var @default)) return;

                        DrawMainUI(player, @default, false);

                        break;
                    }

                case "next":
                    {
                        var pages = Mathf.CeilToInt(data.creators.Count / (float)6);

                        if ((@interface.page + 1) < pages)
                            @interface.page++;

                        DrawMainUI(player, true, false);
                        break;
                    }

                case "prev":
                    {
                        @interface.page = Mathf.Max(0, @interface.page - 1);
                        DrawMainUI(player, true, false);
                        break;
                    }

                case "event":
                    {
                        switch (arg.Args[1])
                        {
                            case "edit":
                                {
                                    if (!int.TryParse(arg.Args[2], out var eventId)) return;

                                    var @event = data.events.Find(e => e.eventId == eventId);
                                    if (@event == null) return;

                                    @interface.editEvent = @event;
                                    @interface.editTime = new();
                                    @interface.editTimeIndex = -1;
                                    @interface.editIndex = -1;
                                    DrawModal(player, "edit");
                                    break;
                                }

                            case "new":
                                {
                                    @interface.editEvent = new();
                                    DrawModal(player, "new_event");
                                    break;
                                }

                            case "save":
                                {
                                    data.events.Add(@interface.editEvent);
                                    data.SaveEvents();
                                    CheckIsEventsLoaded();
                                    UpdateEventList();
                                    @interface.editEvent = new();
                                    DrawMainUI(player);
                                    break;
                                }

                            case "remove":
                                {
                                    if (!int.TryParse(arg.Args[2], out var eventId)) return;

                                    var @event = data.events.Find(e => e.eventId == eventId);
                                    if (@event == null) return;

                                    for (int i = 0; i < data.eventTimes.Count; i++)
                                    {
                                        var time = data.eventTimes.ElementAt(i);
                                        if (time == null) continue;
                                        if (time.eventsId.Contains(eventId))
                                        {
                                            time.eventsId.Remove(eventId);
                                            if (time.eventsId.Count == 0)
                                            {
                                                data.eventTimes.Remove(time);
                                                i--;
                                            }
                                        }

                                    }

                                    data.events.Remove(@event);
                                    data.SaveEvents();
                                    data.SaveTimes();
                                    UpdateEventList();
                                    DrawMainUI(player, true, false);
                                    break;
                                }

                            case "add":
                                {

                                    var input = String.Join(" ", arg.Args.Skip(3));
                                    if (string.IsNullOrEmpty(input))
                                    {
                                        DrawModal(player, "new_event");
                                        return;
                                    }

                                    switch (arg.Args[2])
                                    {
                                        case "name":
                                            {
                                                @interface.editEvent.displayName = input;
                                                break;
                                            }

                                        case "command":
                                            {
                                                @interface.editEvent.command = input;
                                                break;
                                            }

                                        case "plugin":
                                            {
                                                @interface.editEvent.pluginName = input;
                                                break;
                                            }

                                        case "creator":
                                            {
                                                @interface.editEvent.creator = input;
                                                DrawModal(player, "new_event");
                                                break;
                                            }
                                    }
                                    break;
                                }

                            case "time":
                                {
                                    switch (arg.Args[2])
                                    {

                                        case "start":
                                            {
                                                if (@interface.editTime == null)
                                                    return;

                                                var eventEntry = data.events.Find(e => e.eventId == @interface.editTime.eventsId[0]);
                                                if (eventEntry == null)
                                                    return;

                                                instance.Server.Command(eventEntry.command);

                                                CallEventManagerStartedEvent(eventEntry.displayName);

                                                if (!String.IsNullOrEmpty(_config.DiscordWebHook)) SendLogToDiscord(eventEntry.displayName);
                                                break;
                                            }
                                        case "edit":
                                            {
                                                if (!int.TryParse(arg.Args[3], out var index) || !int.TryParse(arg.Args[4], out var index2)) return;

                                                @interface.editTime = (EventTime)data.eventTimes[index].Clone();
                                                @interface.editTimeIndex = index;
                                                if (@interface.editEvent != null)
                                                {
                                                    @interface.editTime.eventsId = new List<int> { @interface.editEvent.eventId };
                                                    @interface.editIndex = index2;
                                                    CuiElementContainer container = new();
                                                    DrawModalTimes(player, container);
                                                    CuiHelper.AddUi(player, container);
                                                }

                                                DrawModalTime(player);
                                                break;
                                            }

                                        case "new":
                                            {
                                                @interface.editTime = new();
                                                @interface.editTimeIndex = -1;
                                                @interface.editIndex = -1;
                                                if (@interface.editEvent != null)
                                                    @interface.editTime.eventsId = new List<int> { @interface.editEvent.eventId };

                                                DrawModalTime(player);
                                                break;
                                            }

                                        case "prev":
                                            {
                                                if ((int)@interface.editTime.type - 1 < 0)
                                                    @interface.editTime.type = EventType.Random;
                                                else
                                                    @interface.editTime.type = (EventType)((int)@interface.editTime.type - 1);

                                                DrawModalTime(player);
                                                break;
                                            }

                                        case "next":
                                            {
                                                if ((int)@interface.editTime.type + 1 > 2)
                                                    @interface.editTime.type = EventType.Static;
                                                else
                                                    @interface.editTime.type = (EventType)((int)@interface.editTime.type + 1);

                                                DrawModalTime(player);
                                                break;
                                            }

                                        case "hour":
                                            {
                                                if (!int.TryParse(arg.Args[3], out var num) || num < 0 || num > 24)
                                                {
                                                    DrawModalTime(player);
                                                    return;
                                                }

                                                @interface.editTime.hour = num;
                                                break;
                                            }

                                        case "minute":
                                            {
                                                if (!int.TryParse(arg.Args[3], out var num) || num < 0 || num > 60)
                                                {
                                                    DrawModalTime(player);
                                                    return;
                                                }

                                                @interface.editTime.minute = num;
                                                break;
                                            }

                                        case "offsetmin":
                                            {
                                                if (!int.TryParse(arg.Args[3], out var num) || num < 0)
                                                {
                                                    DrawModalTime(player);
                                                    return;
                                                }

                                                if (num > @interface.editTime.offsetMax)
                                                {
                                                    @interface.editTime.offsetMin = @interface.editTime.offsetMax;
                                                    DrawModalTime(player);
                                                    return;
                                                }

                                                @interface.editTime.offsetMin = num;
                                                break;
                                            }

                                        case "offsetmax":
                                            {
                                                if (!int.TryParse(arg.Args[3], out var num) || num < 0)
                                                {
                                                    DrawModalTime(player);
                                                    return;
                                                }

                                                @interface.editTime.offsetMax = num;
                                                break;
                                            }

                                        case "interval":
                                            {
                                                if (!int.TryParse(arg.Args[3], out var num) || num < 0)
                                                {
                                                    DrawModalTime(player);
                                                    return;
                                                }

                                                @interface.editTime.interval = num;
                                                break;
                                            }

                                        case "minplayers":
                                            {
                                                if (!int.TryParse(arg.Args[3], out var num) || num < 0)
                                                {
                                                    DrawModalTime(player);
                                                    return;
                                                }

                                                @interface.editTime.minPlayers = num;
                                                break;
                                            }

                                        case "maxplayers":
                                            {
                                                if (!int.TryParse(arg.Args[3], out var num) || num < 0)
                                                {
                                                    DrawModalTime(player);
                                                    return;
                                                }

                                                @interface.editTime.maxPlayers = num;
                                                break;
                                            }

                                        case "day":
                                            {
                                                if (!Enum.TryParse(arg.Args[3], out DayOfWeek day)) return;

                                                if (@interface.editTime.activeDays.Contains(day))
                                                    @interface.editTime.activeDays.Remove(day);
                                                else
                                                    @interface.editTime.activeDays.Add(day);

                                                DrawModalTime(player);

                                                break;
                                            }

                                        case "save":
                                            {
                                                if (@interface.editTime.type == EventType.Random)
                                                    @interface.editTime.interval = 0;
                                                else if (@interface.editTime.type == EventType.Every)
                                                {
                                                    @interface.editTime.offsetMin = 0;
                                                    @interface.editTime.offsetMax = 0;
                                                }

                                                @interface.editTime.UpdateInfo();
                                                @interface.editTime.UpdateOffset();
                                                @interface.editTime.isRandom = false;

                                                if (@interface.editTimeIndex == -1)
                                                    data.eventTimes.Add((EventTime)@interface.editTime.Clone());
                                                else
                                                    data.eventTimes[@interface.editTimeIndex] = (EventTime)@interface.editTime.Clone();



                                                data.SaveTimes();
                                                @interface.editTime = new();
                                                DrawModal(player, "edit", false);
                                                break;
                                            }

                                        case "delete":
                                            {
                                                if (@interface.editTimeIndex != -1)
                                                    data.eventTimes.RemoveAt(@interface.editTimeIndex);


                                                data.SaveTimes();

                                                @interface.editTime = new();
                                                DrawModal(player, "edit", false);
                                                break;
                                            }
                                    }
                                    break;
                                }
                        }
                        break;
                    }

                case "creator":
                    {
                        switch (arg.Args[1])
                        {
                            case "add":
                                {
                                    @interface.editCreatorIndex = -1;
                                    DrawModal(player, "new_creator");
                                    break;
                                }

                            case "edit":
                                {
                                    var name = String.Join(" ", arg.Args.Skip(2));
                                    @interface.editCreatorIndex = data.creators.FindIndex(n => n == name);

                                    if (@interface.editCreatorIndex == -1)
                                        return;

                                    @interface.inputCreator = name;
                                    DrawModal(player, "edit_creator");
                                    break;
                                }

                            case "name":
                                {
                                    var name = String.Join(" ", arg.Args.Skip(2));
                                    @interface.inputCreator = name;

                                    CuiElementContainer container = new();
                                    DrawModalCreator(player, container, name);
                                    CuiHelper.AddUi(player, container);
                                    break;
                                }

                            case "save":
                                {
                                    if(@interface.editCreatorIndex != -1)
                                    {
                                        string oldCreator = data.creators[@interface.editCreatorIndex];

                                        foreach (var eventEntry in data.events)
                                            if (eventEntry.creator == oldCreator)
                                                eventEntry.creator = @interface.inputCreator;

                                        data.creators[@interface.editCreatorIndex] = @interface.inputCreator;

                                        data.SaveEvents();
                                        data.SaveCreators();
                                        DrawMainUI(player);

                                        break;
                                    };

                                    var name = String.Join(" ", arg.Args.Skip(2));
                                    data.creators.Add(name);
                                    data.SaveCreators();
                                    DrawMainUI(player);
                                    break;
                                }
                        }
                        break;
                    }

                case "random":
                    {
                        switch (arg.Args[1])
                        {
                            case "time":
                                {
                                    switch (arg.Args[2])
                                    {
                                        case "new":
                                            {
                                                @interface.editTime = new();
                                                @interface.editTimeIndex = -1;
                                                @interface.editIndex = -1;
                                                @interface.editTime.eventsId = new();

                                                CuiElementContainer container = new();
                                                DrawRandomTime(player, container);
                                                CuiHelper.AddUi(player, container);
                                                break;
                                            }

                                        case "edit":
                                            {
                                                if (!int.TryParse(arg.Args[3], out var index) || !int.TryParse(arg.Args[4], out var index2)) return;

                                                @interface.editTime = (EventTime)data.eventTimes[index].Clone();
                                                @interface.editTime.eventsId = new List<int>(data.eventTimes[index].eventsId);
                                                CuiElementContainer container = new();
                                                @interface.editTimeIndex = index;

                                                @interface.editIndex = index2;

                                                DrawRandomTimes(player, container);
                                                DrawRandomTime(player, container);
                                                CuiHelper.AddUi(player, container);
                                                break;
                                            }

                                        case "prev":
                                            {
                                                if ((int)@interface.editTime.type - 1 < 0)
                                                    @interface.editTime.type = EventType.Random;
                                                else
                                                    @interface.editTime.type = (EventType)((int)@interface.editTime.type - 1);

                                                CuiElementContainer container = new();
                                                DrawRandomTime(player, container);
                                                CuiHelper.AddUi(player, container);
                                                break;
                                            }

                                        case "next":
                                            {
                                                if ((int)@interface.editTime.type + 1 > 2)
                                                    @interface.editTime.type = EventType.Static;
                                                else
                                                    @interface.editTime.type = (EventType)((int)@interface.editTime.type + 1);

                                                CuiElementContainer container = new();
                                                DrawRandomTime(player, container);
                                                CuiHelper.AddUi(player, container);
                                                break;
                                            }

                                        case "hour":
                                            {
                                                if (!int.TryParse(arg.Args[3], out var num) || num < 0 || num > 24)
                                                {
                                                    CuiElementContainer container = new();
                                                    DrawRandomTime(player, container);
                                                    CuiHelper.AddUi(player, container);
                                                    return;
                                                }

                                                @interface.editTime.hour = num;
                                                break;
                                            }

                                        case "minute":
                                            {
                                                if (!int.TryParse(arg.Args[3], out var num) || num < 0 || num > 60)
                                                {
                                                    CuiElementContainer container = new();
                                                    DrawRandomTime(player, container);
                                                    CuiHelper.AddUi(player, container);
                                                    return;
                                                }

                                                @interface.editTime.minute = num;
                                                break;
                                            }

                                        case "offsetmin":
                                            {
                                                if (!int.TryParse(arg.Args[3], out var num) || num < 0)
                                                {
                                                    CuiElementContainer container = new();
                                                    DrawRandomTime(player, container);
                                                    CuiHelper.AddUi(player, container);
                                                    return;
                                                }

                                                if (num > @interface.editTime.offsetMax)
                                                {
                                                    @interface.editTime.offsetMin = @interface.editTime.offsetMax;
                                                    CuiElementContainer container = new();
                                                    DrawRandomTime(player, container);
                                                    CuiHelper.AddUi(player, container);
                                                    return;
                                                }

                                                @interface.editTime.offsetMin = num;
                                                break;
                                            }

                                        case "offsetmax":
                                            {
                                                if (!int.TryParse(arg.Args[3], out var num) || num < 0)
                                                {
                                                    CuiElementContainer container = new();
                                                    DrawRandomTime(player, container);
                                                    CuiHelper.AddUi(player, container);
                                                    return;
                                                }

                                                @interface.editTime.offsetMax = num;
                                                break;
                                            }

                                        case "interval":
                                            {
                                                if (!int.TryParse(arg.Args[3], out var num) || num < 0)
                                                {
                                                    CuiElementContainer container = new();
                                                    DrawRandomTime(player, container);
                                                    CuiHelper.AddUi(player, container);
                                                    return;
                                                }

                                                @interface.editTime.interval = num;
                                                break;
                                            }

                                        case "minplayers":
                                            {
                                                if (!int.TryParse(arg.Args[3], out var num) || num < 0)
                                                {
                                                    CuiElementContainer container = new();
                                                    DrawRandomTime(player, container);
                                                    CuiHelper.AddUi(player, container);
                                                    return;
                                                }

                                                @interface.editTime.minPlayers = num;
                                                break;
                                            }

                                        case "maxplayers":
                                            {
                                                if (!int.TryParse(arg.Args[3], out var num) || num < 0)
                                                {
                                                    CuiElementContainer container = new();
                                                    DrawRandomTime(player, container);
                                                    CuiHelper.AddUi(player, container);
                                                    return;
                                                }

                                                @interface.editTime.maxPlayers = num;
                                                break;
                                            }

                                        case "day":
                                            {
                                                if (!Enum.TryParse(arg.Args[3], out DayOfWeek day)) return;

                                                if (@interface.editTime.activeDays.Contains(day))
                                                    @interface.editTime.activeDays.Remove(day);
                                                else
                                                    @interface.editTime.activeDays.Add(day);

                                                CuiElementContainer container = new();
                                                DrawRandomTime(player, container);
                                                CuiHelper.AddUi(player, container);

                                                break;
                                            }

                                        case "save":
                                            {


                                                if (@interface.editTime.type == EventType.Random)
                                                    @interface.editTime.interval = 0;
                                                else if (@interface.editTime.type == EventType.Every)
                                                {
                                                    @interface.editTime.offsetMin = 0;
                                                    @interface.editTime.offsetMax = 0;
                                                }

                                                @interface.editTime.UpdateInfo();
                                                @interface.editTime.UpdateOffset();
                                                @interface.editTime.isRandom = true;

                                                if (@interface.editTimeIndex == -1)
                                                    data.eventTimes.Add((EventTime)@interface.editTime.Clone());
                                                else
                                                    data.eventTimes[@interface.editTimeIndex] = (EventTime)@interface.editTime.Clone();



                                                data.SaveTimes();
                                                @interface.editTime = new();
                                                DrawMainUI(player, false, false);

                                                break;
                                            }

                                        case "delete":
                                            {
                                                if (@interface.editTimeIndex != -1)
                                                    data.eventTimes.RemoveAt(@interface.editTimeIndex);

                                                @interface.editTime = new();

                                                data.SaveTimes();

                                                DrawMainUI(player, false, false);
                                                break;
                                            }
                                    }
                                    break;
                                }

                            case "events":
                                {
                                    switch (arg.Args[2])
                                    {
                                        case "show":
                                            {
                                                DrawSelectEvent(player);
                                                break;
                                            }

                                        case "next":
                                            {
                                                var pages = Mathf.CeilToInt(data.creators.Count / (float)6);

                                                if ((@interface.page + 1) < pages)
                                                    @interface.page++;

                                                DrawSelectEvent(player);
                                                break;
                                            }

                                        case "prev":
                                            {
                                                @interface.page = Mathf.Max(0, @interface.page - 1);
                                                DrawSelectEvent(player);
                                                break;
                                            }

                                        case "select":
                                            {
                                                if (!int.TryParse(arg.Args[3], out var eventId)) return;

                                                var @event = data.events.Find(e => e.eventId == eventId);
                                                if (@event == null) return;

                                                if (!@interface.editTime.eventsId.Contains(eventId))
                                                    @interface.editTime.eventsId.Add(eventId);


                                                CuiElementContainer container = new();
                                                DrawMainUI(player, false, false);
                                                DrawRandomTime(player, container);
                                                CuiHelper.AddUi(player, container);

                                                break;
                                            }

                                        case "remove":
                                            {
                                                if (!int.TryParse(arg.Args[3], out var eventId)) return;

                                                @interface.editTime.eventsId.Remove(eventId);


                                                CuiElementContainer container = new();
                                                DrawRandomTime(player, container);
                                                CuiHelper.AddUi(player, container);


                                                break;
                                            }
                                    }
                                    break;
                                }
                        }
                        break;
                    }
            }

        }
        #endregion

        private void DrawBackground(BasePlayer player, CuiElementContainer container, string layer, string parent)
        {
            container.Add(new CuiElement
            {
                Name = layer,
                DestroyUi = layer,
                Parent = parent,
                Components = {
                    new CuiImageComponent
                    {
                        Color = "0 0 0 0.5",
                        Material = "assets/content/ui/uibackgroundblur.mat",
                        ImageType = Image.Type.Tiled
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1",
                    },
                    new CuiNeedsCursorComponent(),
                    new CuiNeedsKeyboardComponent()
                }
            });
        }

        private void DrawMainPanel(BasePlayer player, CuiElementContainer container, string anchorMin, string anchorMax, string offsetMin, string offsetMax, string layer, string parent)
        {
            container.Add(new CuiPanel
            {
                Image =
                    {
                        Color = HexToCuiColor("#191919", 90),
                        Sprite = "assets/content/ui/UI.Background.TileTex.psd",
                        Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"
                    },
                RectTransform =
                    {
                        AnchorMin = anchorMin,
                        AnchorMax = anchorMax,
                        OffsetMin = offsetMin,
                        OffsetMax = offsetMax
                    }
            }, parent, layer, layer);
        }

        private void DrawMainUI(BasePlayer player, bool @default = true, bool first = true)
        {
            if (!_interface.TryGetValue(player.userID, out var @interface)) return;

            CuiElementContainer container = new();

            if (first)
                DrawBackground(player, container, LayerBackground, "Overlay");

            DrawMainPanel(player, container, "0.5 0.5", "0.5 0.5", "-600 -298", "600 298", Layer, LayerBackground);

            #region Header

            var headerLayer = Layer + ".Header";

            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image =
                    {
                        Color = HexToCuiColor("#696969", 100),
                        Sprite = "assets/content/ui/UI.Background.Transparent.LinearLTR.tga"
                    },
                RectTransform = { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 -50", OffsetMax = "0 0" }
            }, Layer, headerLayer, headerLayer);

            container.Add(new CuiElement
            {
                Parent = headerLayer,
                Components =
                    {
                        new CuiTextComponent
                        {
                            Text = "EVENT MANAGER", Font = "robotocondensed-bold.ttf", FontSize = 18,
                            Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#E2DBD3", 100)
                        },
                        new CuiRectTransformComponent
                            {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "25 0", OffsetMax = "0 0"}
                    }
            });

            #region Btn.Close

            MeventCuiHelper.CreateButton(container, headerLayer,
                command: Command + " close",
                close: Layer,
                color: HexToCuiColor("#E44028", 100),
                material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                sprite: "assets/content/ui/UI.Background.TileTex.psd",
                anchorMin: "1 1", anchorMax: "1 1", offsetMin: "-48 -48", offsetMax: "0 0",
                iconPath: "assets/icons/close.png",
                iconRect: new CuiRectTransformComponent()
                {
                    AnchorMin = "0 0",
                    AnchorMax = "1 1",
                    OffsetMin = "10 10",
                    OffsetMax = "-10 -10"
                });

            #endregion

            #region Btn.Default

            MeventCuiHelper.CreateButton(container, headerLayer, "DEFAULT", Command + " page true",
                Layer, color: HexToCuiColor("#696969", @default ? 100 : 50),
                material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                anchorMin: "0 0.5", anchorMax: "0 0.5", offsetMin: "170 -17", offsetMax: "260 17",
                iconPath: imageDefault,
                iconColor: HexToCuiColor("#FFFFFF", @default ? 100 : 50),
                isBold: true,
                fontSize: 14,
                textAnchor: TextAnchor.MiddleLeft,
                textColor: HexToCuiColor("#FFFFFF", 100));

            #endregion

            #region Btn.Random

            MeventCuiHelper.CreateButton(container, headerLayer, "RANDOM", Command + " page false",
                Layer, color: HexToCuiColor("#696969", !@default ? 100 : 50),
                material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                anchorMin: "0 0.5", anchorMax: "0 0.5", offsetMin: "260 -17", offsetMax: "350 17",
                iconPath: imageRandom,
                iconColor: HexToCuiColor("#FFFFFF", !@default ? 100 : 50),
                isBold: true,
                fontSize: 14,
                textAnchor: TextAnchor.MiddleLeft,
                textColor: HexToCuiColor("#FFFFFF", 100));

            #endregion

            #region Server Time
            container.Add(new CuiElement
            {
                Name = headerLayer + "Time",
                Parent = headerLayer,
                Components =
                {
                    new CuiTextComponent()
                    {
                        Text = "SERVER TIME: %TIME_LEFT%",
                        Align = TextAnchor.MiddleRight,
                        Font = "robotocondensed-bold.ttf",
                        FontSize = 14,
                        Color = HexToCuiColor("#E2DBD3")
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "930 -50",
                        OffsetMax = "1134 0"
                    },
                    new CuiCountdownComponent
                    {
                        EndTime = (float) new TimeSpan(24, 0, 0).TotalSeconds,
                        StartTime = (float) DateTime.Now.TimeOfDay.TotalSeconds,
                        Step = 1,
                        TimerFormat = TimerFormat.HoursMinutesSeconds,
                        DestroyIfDone = true,
                        Command = "UI_EventManager_UpdateTimer"
                    }
                }
            });
            #endregion

            #endregion

            switch (@default)
            {
                case true:
                    {

                        #region Footer

                        MeventCuiHelper.CreateButton(container, Layer,
                            command: Command + " event new",
                            color: HexToCuiColor("#D74933", 100),
                            material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                            anchorMin: "0 0", anchorMax: "0 0", offsetMin: "10 7", offsetMax: "150 42",
                            msg: "ADD EVENT",
                            isBold: true,
                            fontSize: 14,
                            textAnchor: TextAnchor.MiddleCenter, textColor: HexToCuiColor("#FFFFFF", 100));


                        MeventCuiHelper.CreateButton(container, Layer,
                            command: Command + " creator add",
                            color: HexToCuiColor("#005FB7", 100),
                            material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                            anchorMin: "0 0", anchorMax: "0 0", offsetMin: "160 7", offsetMax: "320 42",
                            msg: "ADD CREATOR",
                            isBold: true,
                            fontSize: 14,
                            textAnchor: TextAnchor.MiddleCenter, textColor: HexToCuiColor("#FFFFFF", 100));

                        MeventCuiHelper.CreateButton(container, Layer, command: Command + " next",
                            color: HexToCuiColor("#D74933", 100),
                            material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                            anchorMin: "1 0", anchorMax: "1 0", offsetMin: "-44 7", offsetMax: "-9 42",
                            msg: ">",
                            isBold: true,
                            fontSize: 26,
                            textAnchor: TextAnchor.MiddleCenter,
                            textColor: HexToCuiColor("#FFFFFF", 100));

                        MeventCuiHelper.CreateButton(container, Layer, command: Command + " prev",
                            color: HexToCuiColor("#696969", 30),
                            material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                            anchorMin: "1 0", anchorMax: "1 0", offsetMin: "-87 7", offsetMax: "-52 42",
                            msg: "<",
                            isBold: true,
                            fontSize: 26,
                            textAnchor: TextAnchor.MiddleCenter,
                            textColor: HexToCuiColor("#FFFFFF", 100));

                        #endregion

                        #region Content

                        int
                            columnsOnPage = 6,
                            columnWidth = 190,
                            columnMarginX = 5,
                            eventHeight = 38,
                            eventMarginY = 4;

                        var contentLayer = Layer + ".Content";

                        container.Add(new CuiPanel
                        {
                            Image = { Color = HexToCuiColor("#000000", 0) },
                            RectTransform =
                        {
                            AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-590 -233", OffsetMax = "590 225"
                        }
                        }, Layer, contentLayer, contentLayer);

                        var offsetX = -(columnsOnPage * columnWidth + (columnsOnPage - 1) * columnMarginX) / 2f;

                        var authors = data.creators.Skip(@interface.page * columnsOnPage).Take(columnsOnPage).ToArray();
                        try
                        {
                            for (var i = 0; i < authors.Length; i++)
                            {
                                var targetAuthor = authors[i];
                                var events = data.events.FindAll(e => e.creator == targetAuthor);

                                var offsetLeft = offsetX + columnWidth * i + columnMarginX * i;

                                var eventPanel = container.Add(new CuiPanel
                                {
                                    Image =
                                {
                                    Color = HexToCuiColor("#696969", 15),
                                    Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"
                                },
                                    RectTransform =
                                {
                                    AnchorMin = "0.5 0", AnchorMax = "0.5 1",
                                    OffsetMin = $"{offsetLeft} 0", OffsetMax = $"{offsetLeft + columnWidth} 0"
                                }
                                }, contentLayer);

                                #region Header

                                var headerPanel = container.Add(new CuiPanel
                                {
                                    Image =
                                {
                                    Color = HexToCuiColor("#696969", 40),
                                    Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"
                                },
                                    RectTransform =
                                    {AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 -40", OffsetMax = "0 0"}
                                }, eventPanel);

                                container.Add(new CuiElement
                                {
                                    Parent = headerPanel,
                                    Components =
                                {
                                    new CuiTextComponent
                                    {
                                        Text = targetAuthor, Font = "robotocondensed-bold.ttf", FontSize = 18,
                                        Align = TextAnchor.MiddleCenter, Color = "1 1 1 1"
                                    },
                                    new CuiRectTransformComponent
                                        {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0"}
                                }
                                });


                                MeventCuiHelper.CreateButton(container, headerPanel,
                                      command:
                                      Command + $" creator edit {targetAuthor}",
                                      color: "0 0 0 0",
                                      anchorMin: "0 0", anchorMax: "1 1", offsetMin: "0 0", offsetMax: "0 0");


                                #endregion

                                #region Scroll View

                                var scrollView = eventPanel + ".ScrollView";

                                var eventsHeight = events.Count * eventHeight + (events.Count - 1) * eventMarginY;

                                eventsHeight = Mathf.Max(eventsHeight, 408);

                                container.Add(new CuiElement
                                {
                                    Parent = eventPanel,
                                    Name = scrollView,
                                    Components =
                                {
                                    new CuiScrollViewComponent
                                    {
                                        MovementType = ScrollRect.MovementType.Clamped,
                                        Vertical = true,
                                        Inertia = true,
                                        Horizontal = false,
                                        Elasticity = 0.25f,
                                        DecelerationRate = 0.3f,
                                        ScrollSensitivity = 24f,
                                        ContentTransform = new CuiRectTransformComponent
                                        {
                                            AnchorMin = "0 1", AnchorMax = "1 1",
                                            OffsetMin = $"0 -{eventsHeight}", OffsetMax = "0 0"
                                        },
                                        VerticalScrollbar = new CuiScrollbar
                                        {
                                            AutoHide = true,
                                            Size = 3,
                                            HandleColor = HexToCuiColor("#D74933"),
                                            HighlightColor = HexToCuiColor("#D74933"),
                                            PressedColor = HexToCuiColor("#D74933"),
                                            HandleSprite = "assets/content/ui/UI.Background.TileTex.psd",
                                            TrackColor = HexToCuiColor("#38393F", 40),
                                            TrackSprite = "assets/content/ui/UI.Background.TileTex.psd"
                                        }
                                    },
                                    new CuiRectTransformComponent
                                        {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 5", OffsetMax = "0 -45"}
                                }
                                });

                                #endregion

                                #region Events

                                for (var x = 0; x < events.Count; x++)
                                {
                                    var targetEvent = events[x];

                                    var offsetUp = x * eventHeight + x * eventMarginY;

                                    var eventLayer = Layer + $".Author.{targetAuthor}.Event.{targetEvent.displayName}";

                                    container.Add(new CuiPanel
                                    {
                                        Image =
                                    {
                                        Color = HexToCuiColor("#696969", 60),
                                        Sprite = "assets/content/ui/UI.Background.Transparent.LinearLTR.tga"
                                    },
                                        RectTransform =
                                    {
                                        AnchorMin = "0.5 1", AnchorMax = "0.5 1",
                                        OffsetMin = $"-90 -{offsetUp + eventHeight}", OffsetMax = $"88 -{offsetUp}"
                                    }
                                    }, scrollView, eventLayer, eventLayer);

                                    container.Add(new CuiPanel
                                    {
                                        Image = { Color = targetEvent.isLoaded ? HexToCuiColor("#2BDF31", 100) : HexToCuiColor("#DF2B2E", 100) },
                                        RectTransform =
                                        {AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = "0 0", OffsetMax = "2 0"}
                                    }, eventLayer);

                                    container.Add(new CuiElement
                                    {
                                        Parent = eventLayer,
                                        Components =
                                    {
                                        new CuiTextComponent
                                        {
                                            Text = targetEvent.displayName ?? string.Empty,
                                            Font = "robotocondensed-bold.ttf", FontSize = 14,
                                            Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 100),
                                            VerticalOverflow = VerticalWrapMode.Overflow
                                        },
                                        new CuiRectTransformComponent
                                        {
                                            AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "10 0", OffsetMax = "-20 0"
                                        }
                                    }
                                    });

                                    MeventCuiHelper.CreateButton(container, eventLayer,
                                       command:
                                       Command + $" event edit {targetEvent.eventId}",
                                       color: "0 0 0 0",
                                       anchorMin: "0 0", anchorMax: "1 1", offsetMin: "0 0", offsetMax: "0 0");


                                    MeventCuiHelper.CreateButton(container, eventLayer,
                                        command:
                                        Command + $" event remove {targetEvent.eventId}",
                                        color: "0 0 0 0",
                                        anchorMin: "1 0.5", anchorMax: "1 0.5", offsetMin: "-19 -7", offsetMax: "-5 7",
                                        iconPath: "assets/icons/close.png",
                                        iconColor: HexToCuiColor("#FF5757", 100)); var f = "26052";
                                }

                                #endregion
                            }
                        }
                        finally
                        {
                            Array.Clear(authors, 0, authors.Length);
                        }

                        #endregion

                        break;
                    }

                case false:
                    {
                        DrawRandomTimes(player, container);

                        #region Footer

                        container.Add(new CuiElement
                        {
                            Name = Layer + ".Footer",
                            Parent = Layer,
                            Components = {
                                new CuiImageComponent { Color = HexToCuiColor("#696969", 15),
                                    Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "316 -580",
                                    OffsetMax = "1192 -512"
                                }
                            }
                        });
                        container.Add(new CuiElement
                        {
                            Parent = Layer + ".Footer",
                            Components = {
                                new CuiTextComponent { Text = "Manage random events. Specify a random schedule of standard and custom events.", Font = "robotocondensed-regular.ttf", FontSize = 16, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#E2DBD3", 100) },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "62 -68",
                                    OffsetMax = "876 0"
                                }
                            }
                        });

                        container.Add(new CuiElement
                        {
                            Parent = Layer + ".Footer",
                            Components = {
                                new CuiImageComponent { Sprite  = "assets/icons/info.png" },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "18 -46",
                                    OffsetMax = "43 -21"
                                }
                            }
                        });
                        #endregion

                        break;
                    }
            }

            CuiHelper.AddUi(player, container);
        }

        #region Random
        private void DrawRandomTimes(BasePlayer player, CuiElementContainer container)
        {
            if (!_interface.TryGetValue(player.userID, out var @interface)) return;
            var times = data.eventTimes.FindAll(t => t.isRandom);

            container.Add(new CuiElement
            {
                Name = Layer + ".Times",
                Parent = Layer,
                Components = {
                                new CuiImageComponent { Color = HexToCuiColor("#696969", 15),
                                    Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "10 -580",
                                    OffsetMax = "309 -59"
                                }
                            }
            });

            var timeHeight = 49f;
            var timeMargin = 6f;

            var scrollView = "Time.Panel";

            var eventsHeight = (times.Count + 1) * timeHeight + (times.Count) * timeMargin;

            eventsHeight = Mathf.Max(eventsHeight, 505);

            container.Add(new CuiElement
            {
                Parent = Layer + ".Times",
                Name = scrollView,
                DestroyUi = scrollView,
                Components =
                    {
                        new CuiScrollViewComponent
                        {
                            MovementType = ScrollRect.MovementType.Clamped,
                            Vertical = true,
                            Inertia = true,
                            Horizontal = false,
                            Elasticity = 0.25f,
                            DecelerationRate = 0.3f,
                            ScrollSensitivity = 24f,
                            ContentTransform = new CuiRectTransformComponent
                            {
                                AnchorMin = "0 1", AnchorMax = "1 1",
                                OffsetMin = $"0 -{eventsHeight}", OffsetMax = "0 0"
                            },
                            VerticalScrollbar = new CuiScrollbar
                            {
                                AutoHide = true,
                                Size = 3,
                                HandleColor = HexToCuiColor("#D74933"),
                                HighlightColor = HexToCuiColor("#D74933"),
                                PressedColor = HexToCuiColor("#D74933"),
                                HandleSprite = "assets/content/ui/UI.Background.TileTex.psd",
                                TrackColor = HexToCuiColor("#38393F", 40),
                                TrackSprite = "assets/content/ui/UI.Background.TileTex.psd"
                            }
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 1",
                            OffsetMin = "0 10",
                            OffsetMax = "-10 -10"
                        }
                    }
            });

            container.Add(new CuiElement
            {
                Parent = scrollView,
                Components = {
                                new CuiImageComponent { Color = HexToCuiColor("#404040", 0) },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 0",
                                    AnchorMax = "1 1",

                                }
                            }
            });

            #region Event time
            var index = 0;
            float offset = 0;

            offset = -4 - index * timeHeight - index * timeMargin;
            container.Add(new CuiElement
            {
                Name = "ButtonAd",
                Parent = scrollView,
                Components = {
                    new CuiImageComponent { Color = HexToCuiColor("#696969", 100), Sprite = "assets/content/ui/UI.Background.Transparent.LinearLTR.tga" },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = $"12 {offset - timeHeight}",
                        OffsetMax = $"266 {offset}"
                    }
                }
            });

            #region Button (Add)

            container.Add(new CuiElement
            {
                Name = "icon",
                Parent = "ButtonAd",
                Components = {
                        new CuiImageComponent { Color = HexToCuiColor("#459D30", 100) },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "13 -38",
              OffsetMax = "38 -13"
                        }
                    }
            });

            container.Add(new CuiElement
            {
                Parent = "icon",
                Components = {
                    new CuiImageComponent { Sprite = "assets/icons/health.png" },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0.5",
              AnchorMax = "0.5 0.5",
              OffsetMin = "-7 -7",
              OffsetMax = "7 7"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = "ButtonAd",
                Components =
                {
                    new CuiTextComponent { Text = "Add time", Font = "robotocondensed-bold.ttf", FontSize = 16, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 100) },
                    new CuiRectTransformComponent
                    {
                       AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "49 -35",
              OffsetMax = "198 -14"
                    }
                }
            });

            container.Add(new CuiButton
            {
                RectTransform =
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 1",
                        },
                Button =
                        {
                            Color = "0 0 0 0",
                            Command = $"{Command} random time new"
                        },
            }, "ButtonAd");

            #endregion Button (Add)

            index++;

            foreach (var time in times)
            {
                offset = -4 - index * timeHeight - index * timeMargin;
                var timeLayer = CuiHelper.GetGuid();

                container.Add(new CuiElement
                {
                    Name = timeLayer,
                    Parent = scrollView,
                    Components = {
                        new CuiImageComponent { Color = HexToCuiColor(index == @interface.editIndex ? "D74933"  : "696969", 100), Sprite = "assets/content/ui/UI.Background.Transparent.LinearLTR.tga", },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "0 1",
                            OffsetMin = $"12 {offset - timeHeight}",
                            OffsetMax = $"266 {offset}"
                        }
                    }
                });

                container.Add(new CuiElement
                {
                    Parent = timeLayer,
                    Components = {
                        new CuiTextComponent { Text = time.displayInfo, Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 100) },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 1",
                            OffsetMin = "10 0"
                        }
                    }
                });

                container.Add(new CuiButton
                {
                    RectTransform =
                            {
                                AnchorMin = "0 0",
                                AnchorMax = "1 1",
                            },
                    Button =
                            {
                                Color = "0 0 0 0",
                                Command = $"{Command} random time edit {data.eventTimes.IndexOf(time)} {index}"
                            },
                }, timeLayer);

                index++;
            }

            #endregion Event time
        }

        private void DrawRandomTime(BasePlayer player, CuiElementContainer container)
        {
            if (!_interface.TryGetValue(player.userID, out var @interface)) return;

            #region Panel
            var panelName = Layer + ".Time";

            container.Add(new CuiElement
            {
                Name = panelName,
                DestroyUi = panelName,
                Parent = Layer,
                Components = {
                    new CuiImageComponent {Color = HexToCuiColor("#696969", 15),
                                    Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "316 -504",
              OffsetMax = "1192 -59"
                    }
                }
            });
            #endregion

            #region Header

            container.Add(new CuiElement
            {
                Parent = panelName,
                Components = {
                                new CuiTextComponent { Text = "TIME SETTINGS", Font = "robotocondensed-bold.ttf", FontSize = 32, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#D74933", 100) },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "21 -56",
                                    OffsetMax = "337 -18"
                                }
                            }
            });


            container.Add(new CuiElement
            {
                Parent = panelName,
                Components = {
                                new CuiTextComponent { Text = "EVENTS", Font = "robotocondensed-bold.ttf", FontSize = 32, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#D74933", 100) },
                                new CuiRectTransformComponent
                                {
                                     AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "593 -56",
              OffsetMax = "807 -18"
                                }
                            }
            });
            #endregion

            DrawType(player, container, @interface.editTime.type, panelName);

            #region MinPlayers
            container.Add(new CuiElement
            {
                Parent = panelName,
                Components = {
                    new CuiTextComponent { Text = "MIN | MAX PLAYERS", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.UpperLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "374 -103",
                        OffsetMax = "503 -76"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Name = panelName + ".MinPlayers",
                Parent = panelName,
                Components = {
                    new CuiImageComponent { Color = HexToCuiColor("#404040", 100), Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "374 -148",
                        OffsetMax = "429 -103"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = panelName + ".MinPlayers",
                Components = {
                    new CuiNeedsKeyboardComponent(),
                    new CuiInputFieldComponent { Color = HexToCuiColor("#FFFFFF", 100), FontSize = 18, Align = TextAnchor.MiddleCenter, IsPassword = false, Command = $"{Command} random time minplayers", Text =  @interface.editTime.minPlayers.ToString()  },
                    new CuiRectTransformComponent {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Name = panelName + ".MaxPlayers",
                Parent = panelName,
                Components = {
                    new CuiImageComponent { Color = HexToCuiColor("#404040", 100), Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "433 -148",
                        OffsetMax = "488 -103"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = panelName + ".MaxPlayers",
                Components = {
                    new CuiNeedsKeyboardComponent(),
                    new CuiInputFieldComponent { Color = HexToCuiColor("#FFFFFF", 100), FontSize = 18, Align = TextAnchor.MiddleCenter, IsPassword = false, Command = $"{Command} random time maxplayers", Text =  @interface.editTime.maxPlayers.ToString()  },
                    new CuiRectTransformComponent {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1"
                    }
                }
            });
            #endregion

            switch (@interface.editTime.type)
            {
                case EventType.Static:
                    {

                        container.Add(new CuiElement
                        {
                            Parent = panelName,
                            Components = {
                                new CuiTextComponent { Text = "TIME", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "18 -199",
                                    OffsetMax = "153 -162"
                                }
                            }
                        });

                        #region Hour
                        container.Add(new CuiElement
                        {
                            Name = panelName + ".MinMinutes",
                            Parent = panelName,
                            Components = {
                                new CuiImageComponent { Color = HexToCuiColor("#404040", 100), Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "18 -244",
                                    OffsetMax = "63 -199"
                                }
                            }
                        });

                        container.Add(new CuiElement
                        {
                            Parent = panelName + ".MinMinutes",
                            Components = {
                                new CuiNeedsKeyboardComponent(),
                                new CuiInputFieldComponent { Color = HexToCuiColor("#FFFFFF", 100), FontSize = 18, Align = TextAnchor.MiddleCenter, IsPassword = false, Command = $"{Command} random time hour", Text =  @interface.editTime.hour.ToString()},
                                new CuiRectTransformComponent {
                                    AnchorMin = "0 0",
                                    AnchorMax = "1 1"
                                }
                            }
                        });


                        container.Add(new CuiElement
                        {
                            Parent = panelName,
                            Components = {
                                new CuiTextComponent { Text = "HOUR", Font = "robotocondensed-regular.ttf", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#8F8F8F", 100) },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "18 -258",
              OffsetMax = "63 -244"
                                }
                            }
                        });
                        #endregion

                        #region Minute
                        container.Add(new CuiElement
                        {
                            Name = panelName + ".MaxMinutes",
                            Parent = panelName,
                            Components = {
                                new CuiImageComponent { Color = HexToCuiColor("#404040", 100), Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "71 -244",
                                    OffsetMax = "116 -199"
                                }
                            }
                        });

                        container.Add(new CuiElement
                        {
                            Parent = panelName + ".MaxMinutes",
                            Components = {
                                new CuiNeedsKeyboardComponent(),
                                new CuiInputFieldComponent { Color = HexToCuiColor("#FFFFFF", 100), FontSize = 18, Align = TextAnchor.MiddleCenter, IsPassword = false, Command = $"{Command} random time minute", Text =  @interface.editTime.minute.ToString()},
                                new CuiRectTransformComponent {
                                    AnchorMin = "0 0",
                                    AnchorMax = "1 1"
                                }
                            }
                        });


                        container.Add(new CuiElement
                        {
                            Parent = panelName,
                            Components = {
                                new CuiTextComponent { Text = "MINUTE", Font = "robotocondensed-regular.ttf", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#8F8F8F", 100) },
                                new CuiRectTransformComponent
                                {
                                     AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "71 -258",
                                    OffsetMax = "116 -244"
                                }
                            }
                        });
                        #endregion

                        container.Add(new CuiElement
                        {
                            Parent = panelName,
                            Components = {
                                new CuiTextComponent { Text = "OFFSET TIME (MINUTES)", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "159 -199",
              OffsetMax = "333 -162"
                                }
                            }
                        });

                        #region Min
                        container.Add(new CuiElement
                        {
                            Name = panelName + ".MinMinutes",
                            Parent = panelName,
                            Components = {
                                new CuiImageComponent { Color = HexToCuiColor("#404040", 100), Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                                new CuiRectTransformComponent
                                {
                                     AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "156 -244",
              OffsetMax = "221 -199"
                                }
                            }
                        });

                        container.Add(new CuiElement
                        {
                            Parent = panelName + ".MinMinutes",
                            Components = {
                                new CuiNeedsKeyboardComponent(),
                                new CuiInputFieldComponent { Color = HexToCuiColor("#FFFFFF", 100), FontSize = 16, Align = TextAnchor.MiddleCenter, IsPassword = false, Command = $"{Command} random time offsetmin", Text =  @interface.editTime.offsetMin > 0 ?  @interface.editTime.offsetMin.ToString() : "MM"},
                                new CuiRectTransformComponent {
                                    AnchorMin = "0 0",
                                    AnchorMax = "1 1"
                                }
                            }
                        });


                        container.Add(new CuiElement
                        {
                            Parent = panelName,
                            Components = {
                                new CuiTextComponent { Text = "MIN", Font = "robotocondensed-regular.ttf", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#8F8F8F", 100) },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "156 -258",
              OffsetMax = "221 -244"
                                }
                            }
                        });
                        #endregion

                        #region Max
                        container.Add(new CuiElement
                        {
                            Name = panelName + ".MaxMinutes",
                            Parent = panelName,
                            Components = {
                                new CuiImageComponent { Color = HexToCuiColor("#404040", 100), Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                                new CuiRectTransformComponent
                                {
                                   AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "230 -244",
              OffsetMax = "295 -199"
                                }
                            }
                        });

                        container.Add(new CuiElement
                        {
                            Parent = panelName + ".MaxMinutes",
                            Components = {
                                new CuiNeedsKeyboardComponent(),
                                new CuiInputFieldComponent { Color = HexToCuiColor("#FFFFFF", 100), FontSize = 16, Align = TextAnchor.MiddleCenter, IsPassword = false, Command = $"{Command} random time offsetmax", Text =  @interface.editTime.offsetMax > 0 ?  @interface.editTime.offsetMax.ToString() : "MM"},
                                new CuiRectTransformComponent {
                                    AnchorMin = "0 0",
                                    AnchorMax = "1 1"
                                }
                            }
                        });


                        container.Add(new CuiElement
                        {
                            Parent = panelName,
                            Components = {
                                new CuiTextComponent { Text = "MAX", Font = "robotocondensed-regular.ttf", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#8F8F8F", 100) },
                                new CuiRectTransformComponent
                                {
                                     AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "230 -258",
              OffsetMax = "295 -244"
                                }
                            }
                        });
                        #endregion

                        #region Weeks

                        container.Add(new CuiElement
                        {
                            Parent = panelName,
                            Components = {
                                new CuiTextComponent { Text = "SELECT DAYS", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.UpperLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "18 -306",
              OffsetMax = "153 -269"
                                }
                            }
                        });

                        float width = 113f;
                        float height = 18f;
                        float marginX = 3f;
                        float marginY = 5f;
                        var itemsOffsetX = 18f;
                        var itemsOffsetY = -306f;

                        int index = 0;

                        foreach (var day in Enum.GetValues(typeof(DayOfWeek)))
                        {
                            var layer = CuiHelper.GetGuid();

                            container.Add(new CuiElement
                            {
                                Name = layer,
                                Parent = panelName,
                                Components = {
                                    new CuiImageComponent { Color = HexToCuiColor("#000000", 0) },
                                    new CuiRectTransformComponent
                                    {
                                        AnchorMin = "0 1",
                                        AnchorMax = "0 1",
                                        OffsetMin = $"{itemsOffsetX} {itemsOffsetY - height}",
                                        OffsetMax = $"{itemsOffsetX + width} {itemsOffsetY}"
                                    }
                                }
                            });

                            MeventCuiHelper.CreateButton(container, layer,
                                       command:
                                       Command + $" random time day {day}",
                                       color: HexToCuiColor(@interface.editTime.activeDays.Contains((DayOfWeek)day) ? "#E44028" : "616161", 100),
                                       anchorMin: "0 1", anchorMax: "0 1", offsetMin: "0 -18", offsetMax: "18 0");

                            container.Add(new CuiElement
                            {
                                Parent = layer,
                                Components = {
                                    new CuiTextComponent { Text = day.ToString(), Font = "robotocondensed-regular.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                                    new CuiRectTransformComponent
                                    {
                                        AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "23 -18",
              OffsetMax = "113 0"
                                    }
                                }
                            });

                            if ((index + 1) % 4 == 0)
                            {
                                itemsOffsetX = 18f;
                                itemsOffsetY = itemsOffsetY - height - marginY;
                            }
                            else
                            {
                                itemsOffsetX = itemsOffsetX + width + marginX;
                            }

                            index++;
                        }

                        #endregion
                        break;
                    }

                case EventType.Every:
                    {
                        container.Add(new CuiElement
                        {
                            Parent = panelName,
                            Components = {
                                new CuiTextComponent { Text = "EVERY TIME (MINUTES)", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "18 -199",
              OffsetMax = "153 -162"
                                }
                            }
                        });

                        #region Interval
                        container.Add(new CuiElement
                        {
                            Name = panelName + ".MinMinutes",
                            Parent = panelName,
                            Components = {
                                new CuiImageComponent { Color = HexToCuiColor("#404040", 100), Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "18 -244",
              OffsetMax = "138 -199"
                                }
                            }
                        });

                        container.Add(new CuiElement
                        {
                            Parent = panelName + ".MinMinutes",
                            Components = {
                                new CuiNeedsKeyboardComponent(),
                                new CuiInputFieldComponent { Color = HexToCuiColor("#FFFFFF", 100), FontSize = 18, Align = TextAnchor.MiddleCenter, IsPassword = false, Command = $"{Command} random time interval", Text =  @interface.editTime.interval > 0 ?  @interface.editTime.interval.ToString() : "MM"},
                                new CuiRectTransformComponent {
                                    AnchorMin = "0 0",
                                    AnchorMax = "1 1"
                                }
                            }
                        });
                        #endregion

                        #region Weeks

                        container.Add(new CuiElement
                        {
                            Parent = panelName,
                            Components = {
                                new CuiTextComponent { Text = "SELECT DAYS", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.UpperLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "18 -306",
              OffsetMax = "153 -269"
                                }
                            }
                        });

                        float width = 113f;
                        float height = 18f;
                        float marginX = 3f;
                        float marginY = 5f;
                        var itemsOffsetX = 18f;
                        var itemsOffsetY = -306f;

                        int index = 0;

                        foreach (var day in Enum.GetValues(typeof(DayOfWeek)))
                        {
                            var layer = CuiHelper.GetGuid();

                            container.Add(new CuiElement
                            {
                                Name = layer,
                                Parent = panelName,
                                Components = {
                                    new CuiImageComponent { Color = HexToCuiColor("#000000", 0) },
                                    new CuiRectTransformComponent
                                    {
                                        AnchorMin = "0 1",
                                        AnchorMax = "0 1",
                                        OffsetMin = $"{itemsOffsetX} {itemsOffsetY - height}",
                                        OffsetMax = $"{itemsOffsetX + width} {itemsOffsetY}"
                                    }
                                }
                            });

                            MeventCuiHelper.CreateButton(container, layer,
                                       command:
                                       Command + $" random time day {day}",
                                       color: HexToCuiColor(@interface.editTime.activeDays.Contains((DayOfWeek)day) ? "#E44028" : "616161", 100),
                                       anchorMin: "0 1", anchorMax: "0 1", offsetMin: "0 -18", offsetMax: "18 0");

                            container.Add(new CuiElement
                            {
                                Parent = layer,
                                Components = {
                                    new CuiTextComponent { Text = day.ToString(), Font = "robotocondensed-regular.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                                    new CuiRectTransformComponent
                                    {
                                        AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "23 -18",
              OffsetMax = "113 0"
                                    }
                                }
                            });

                            if ((index + 1) % 4 == 0)
                            {
                                itemsOffsetX = 18f;
                                itemsOffsetY = itemsOffsetY - height - marginY;
                            }
                            else
                            {
                                itemsOffsetX = itemsOffsetX + width + marginX;
                            }

                            index++;
                        }

                        #endregion
                        break;
                    }

                case EventType.Random:
                    {
                        container.Add(new CuiElement
                        {
                            Parent = panelName,
                            Components = {
                                new CuiTextComponent { Text = "RANDOM TIME (MINUTES)", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "18 -199",
              OffsetMax = "221 -162"
                                }
                            }
                        });

                        #region Min
                        container.Add(new CuiElement
                        {
                            Name = panelName + ".MinMinutes",
                            Parent = panelName,
                            Components = {
                                new CuiImageComponent { Color = HexToCuiColor("#404040", 100), Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "18 -244",
              OffsetMax = "138 -199"
                                }
                            }
                        });

                        container.Add(new CuiElement
                        {
                            Parent = panelName + ".MinMinutes",
                            Components = {
                                new CuiNeedsKeyboardComponent(),
                                new CuiInputFieldComponent { Color = HexToCuiColor("#FFFFFF", 100), FontSize = 18, Align = TextAnchor.MiddleCenter, IsPassword = false, Command = $"{Command} random time offsetmin", Text =  @interface.editTime.offsetMin > 0 ?  @interface.editTime.offsetMin.ToString() : "MM"},
                                new CuiRectTransformComponent {
                                    AnchorMin = "0 0",
                                    AnchorMax = "1 1"
                                }
                            }
                        });


                        container.Add(new CuiElement
                        {
                            Parent = panelName,
                            Components = {
                                new CuiTextComponent { Text = "MIN", Font = "robotocondensed-regular.ttf", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#8F8F8F", 100) },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "18 -258",
              OffsetMax = "138 -244"
                                }
                            }
                        });
                        #endregion

                        #region Max
                        container.Add(new CuiElement
                        {
                            Name = panelName + ".MaxMinutes",
                            Parent = panelName,
                            Components = {
                                new CuiImageComponent { Color = HexToCuiColor("#404040", 100), Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "147 -244",
              OffsetMax = "267 -199"
                                }
                            }
                        });

                        container.Add(new CuiElement
                        {
                            Parent = panelName + ".MaxMinutes",
                            Components = {
                                new CuiNeedsKeyboardComponent(),
                                new CuiInputFieldComponent { Color = HexToCuiColor("#FFFFFF", 100), FontSize = 18, Align = TextAnchor.MiddleCenter, IsPassword = false, Command = $"{Command} random time offsetmax", Text =  @interface.editTime.offsetMax > 0 ?  @interface.editTime.offsetMax.ToString() : "MM"},
                                new CuiRectTransformComponent {
                                    AnchorMin = "0 0",
                                    AnchorMax = "1 1"
                                }
                            }
                        });


                        container.Add(new CuiElement
                        {
                            Parent = panelName,
                            Components = {
                                new CuiTextComponent { Text = "MAX", Font = "robotocondensed-regular.ttf", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#8F8F8F", 100) },
                                new CuiRectTransformComponent
                                {
                                   AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "147 -258",
              OffsetMax = "267 -244"
                                }
                            }
                        });
                        #endregion

                        #region Weeks

                        container.Add(new CuiElement
                        {
                            Parent = panelName,
                            Components = {
                                new CuiTextComponent { Text = "SELECT DAYS", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.UpperLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "18 -306",
              OffsetMax = "153 -269"
                                }
                            }
                        });

                        float width = 113f;
                        float height = 18f;
                        float marginX = 3f;
                        float marginY = 5f;
                        var itemsOffsetX = 18f;
                        var itemsOffsetY = -306f;

                        int index = 0;

                        foreach (var day in Enum.GetValues(typeof(DayOfWeek)))
                        {
                            var layer = CuiHelper.GetGuid();

                            container.Add(new CuiElement
                            {
                                Name = layer,
                                Parent = panelName,
                                Components = {
                                    new CuiImageComponent { Color = HexToCuiColor("#000000", 0) },
                                    new CuiRectTransformComponent
                                    {
                                        AnchorMin = "0 1",
                                        AnchorMax = "0 1",
                                        OffsetMin = $"{itemsOffsetX} {itemsOffsetY - height}",
                                        OffsetMax = $"{itemsOffsetX + width} {itemsOffsetY}"
                                    }
                                }
                            });

                            MeventCuiHelper.CreateButton(container, layer,
                                       command:
                                       Command + $" random time day {day}",
                                       color: HexToCuiColor(@interface.editTime.activeDays.Contains((DayOfWeek)day) ? "#E44028" : "616161", 100),
                                       anchorMin: "0 1", anchorMax: "0 1", offsetMin: "0 -18", offsetMax: "18 0");

                            container.Add(new CuiElement
                            {
                                Parent = layer,
                                Components = {
                                    new CuiTextComponent { Text = day.ToString(), Font = "robotocondensed-regular.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                                    new CuiRectTransformComponent
                                    {
                                        AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "23 -18",
              OffsetMax = "113 0"
                                    }
                                }
                            });

                            if ((index + 1) % 4 == 0)
                            {
                                itemsOffsetX = 18f;
                                itemsOffsetY = itemsOffsetY - height - marginY;
                            }
                            else
                            {
                                itemsOffsetX = itemsOffsetX + width + marginX;
                            }

                            index++;
                        }

                        #endregion
                        break;
                    }

            }

            #region Events

            var events = @interface.editTime.eventsId;
            container.Add(new CuiElement
            {
                Name = panelName + ".Events",
                Parent = panelName,
                Components = {
                    new CuiImageComponent { Color = HexToCuiColor("#696969", 20) },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "594 -353",
                        OffsetMax = "858 -76"
                    }
                }
            });

            var eventHeight = 45f;
            var eventMargin = 6f;

            var scrollView = "Events.Panel";

            var eventsHeight = (events.Count) * eventHeight + (events.Count - 1) * eventMargin;

            eventsHeight = Mathf.Max(eventsHeight, 200);

            container.Add(new CuiElement
            {
                Parent = panelName + ".Events",
                Name = scrollView,
                DestroyUi = scrollView,
                Components =
                    {
                        new CuiScrollViewComponent
                        {
                            MovementType = ScrollRect.MovementType.Clamped,
                            Vertical = true,
                            Inertia = true,
                            Horizontal = false,
                            Elasticity = 0.25f,
                            DecelerationRate = 0.3f,
                            ScrollSensitivity = 24f,
                            ContentTransform = new CuiRectTransformComponent
                            {
                                AnchorMin = "0 1", AnchorMax = "1 1",
                                OffsetMin = $"0 -{eventsHeight}", OffsetMax = "0 0"
                            },
                            VerticalScrollbar = new CuiScrollbar
                            {
                                AutoHide = true,
                                Size = 3,
                                HandleColor = HexToCuiColor("#D74933"),
                                HighlightColor = HexToCuiColor("#D74933"),
                                PressedColor = HexToCuiColor("#D74933"),
                                HandleSprite = "assets/content/ui/UI.Background.TileTex.psd",
                                TrackColor = HexToCuiColor("#38393F", 40),
                                TrackSprite = "assets/content/ui/UI.Background.TileTex.psd"
                            }
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 1",
                            OffsetMin = "0 50",
                            OffsetMax = "-5 -10"
                        }
                    }
            });

            container.Add(new CuiElement
            {
                Parent = scrollView,
                Components = {
                                new CuiImageComponent { Color = HexToCuiColor("#404040", 0) },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 0",
                                    AnchorMax = "1 1",

                                }
                            }
            });

            var i = 0;
            float offset = 0;

            foreach (var eventId in events)
            {
                var @event = data.events.Find(e => e.eventId == eventId);
                if (@event == null)
                    continue;

                offset = -4 - i * eventHeight - i * eventMargin;
                var timeLayer = CuiHelper.GetGuid();

                container.Add(new CuiElement
                {
                    Name = timeLayer,
                    Parent = scrollView,
                    Components = {
                        new CuiImageComponent { Color = HexToCuiColor("696969", 100), Sprite = "assets/content/ui/UI.Background.Transparent.LinearLTR.tga", },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "0 1",
                            OffsetMin = $"7 {offset - eventHeight}",
                            OffsetMax = $"249 {offset}"
                        }
                    }
                });

                container.Add(new CuiElement
                {
                    Parent = timeLayer,
                    Components = {
                        new CuiTextComponent { Text = @event.displayName, Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 100) },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 1",
                            OffsetMin = "10 0"
                        }
                    }
                });

                MeventCuiHelper.CreateButton(container, timeLayer,
                                        command:
                                        Command + $" random events remove {eventId}",
                                        color: "0 0 0 0",
                                        anchorMin: "1 0.5", anchorMax: "1 0.5", offsetMin: "-19 -7", offsetMax: "-5 7",
                                        iconPath: "assets/icons/close.png",
                                        iconColor: HexToCuiColor("#FF5757", 100));

                i++;
            }

            #region events
            #endregion

            MeventCuiHelper.CreateButton(container, panelName + ".Events",
                                       command:
                                       Command + $" random events show",
                                       color: HexToCuiColor("5A5A5A", 100),
                                       msg: "ADD EVENT",
                                       textAnchor: TextAnchor.MiddleCenter,
                                       textColor: HexToCuiColor("E2DBD3", 80),
                                       fontSize: 18,
                                       anchorMin: "0 1", anchorMax: "0 1", offsetMin: "0 -277", offsetMax: "264 -238");

            #endregion

            #region Save
            MeventCuiHelper.CreateButton(container, panelName, "SAVE", Command + " random time save",
                color: HexToCuiColor("#459D30", 100),
                material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                anchorMin: "0 1", anchorMax: "0 1", offsetMin: "618 -423", offsetMax: "723 -383",
                isBold: true,
                fontSize: 12,
                textAnchor: TextAnchor.MiddleCenter,
                textColor: HexToCuiColor("#FFFFFF", 100));
            #endregion

            #region Delete
            MeventCuiHelper.CreateButton(container, panelName, "DELETE", Command + " random time delete",
                color: HexToCuiColor("#B63537", 100),
                material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                anchorMin: "0 1", anchorMax: "0 1", offsetMin: "737 -423", offsetMax: "857 -383",
                isBold: true,
                fontSize: 12,
                textAnchor: TextAnchor.MiddleCenter,
                textColor: HexToCuiColor("#FFFFFF", 100));
            #endregion
        }

        private void DrawType(BasePlayer player, CuiElementContainer container, EventType curType, string parent)
        {
            var layerName = parent + ".Type";

            #region Header
            container.Add(new CuiElement
            {
                Parent = parent,
                Components = {
                    new CuiTextComponent { Text = "REPEAT TYPE", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "18 -103",
              OffsetMax = "159 -76"
                    }
                }
            });
            #endregion


            #region Panel
            container.Add(new CuiElement
            {
                Name = layerName,
                Parent = parent,
                Components = {
                    new CuiImageComponent { Color = HexToCuiColor("#404040", 100), Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "18 -148",
              OffsetMax = "324 -103"
                    }
                }
            });
            #endregion

            #region Type
            container.Add(new CuiElement
            {
                Parent = layerName,
                Components = {
                    new CuiTextComponent { Text = curType.ToString(), Font = "robotocondensed-regular.ttf", FontSize = 18, Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#E2DBD3", 90) },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "45 -45",
              OffsetMax = "261 0"
                    }
                }
            });
            #endregion

            #region Btn.Prev
            MeventCuiHelper.CreateButton(container, layerName,
                                       command:
                                       Command + $" random time prev",
                                       color: HexToCuiColor("4D4D4D", 100),
                                       msg: "<",
                                       textAnchor: TextAnchor.MiddleCenter,
                                       textColor: HexToCuiColor("E2DBD3", 80),
                                       fontSize: 24,
                                       anchorMin: "0 1", anchorMax: "0 1", offsetMin: "0 -45", offsetMax: "45 0");
            #endregion

            #region Btn.Next
            MeventCuiHelper.CreateButton(container, layerName,
                                       command:
                                       Command + $" random time next",
                                       color: HexToCuiColor("4D4D4D", 100),
                                       msg: ">",
                                       textAnchor: TextAnchor.MiddleCenter,
                                       textColor: HexToCuiColor("E2DBD3", 80),
                                       fontSize: 24,
                                       anchorMin: "0 1", anchorMax: "0 1", offsetMin: "261 -45", offsetMax: "306 0");
            #endregion
        }

        private void DrawSelectEvent(BasePlayer player)
        {
            if (!_interface.TryGetValue(player.userID, out var @interface)) return;

            CuiElementContainer container = new();

            DrawMainPanel(player, container, "0.5 0.5", "0.5 0.5", "-600 -298", "600 298", Layer, LayerBackground);

            #region Header

            var headerLayer = Layer + ".Header";

            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image =
                    {
                        Color = HexToCuiColor("#696969", 100),
                        Sprite = "assets/content/ui/UI.Background.Transparent.LinearLTR.tga"
                    },
                RectTransform = { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 -50", OffsetMax = "0 0" }
            }, Layer, headerLayer, headerLayer);

            container.Add(new CuiElement
            {
                Parent = headerLayer,
                Components =
                    {
                        new CuiTextComponent
                        {
                            Text = "SELECT EVENT", Font = "robotocondensed-bold.ttf", FontSize = 18,
                            Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#E2DBD3", 100)
                        },
                        new CuiRectTransformComponent
                            {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "25 0", OffsetMax = "0 0"}
                    }
            });

            #region Btn.Close

            MeventCuiHelper.CreateButton(container, headerLayer,
                command: Command + " close",
                close: Layer,
                color: HexToCuiColor("#E44028", 100),
                material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                sprite: "assets/content/ui/UI.Background.TileTex.psd",
                anchorMin: "1 1", anchorMax: "1 1", offsetMin: "-48 -48", offsetMax: "0 0",
                iconPath: "assets/icons/close.png",
                iconRect: new CuiRectTransformComponent()
                {
                    AnchorMin = "0 0",
                    AnchorMax = "1 1",
                    OffsetMin = "10 10",
                    OffsetMax = "-10 -10"
                });

            #endregion

            #region Server Time
            container.Add(new CuiElement
            {
                Name = headerLayer + "Time",
                Parent = headerLayer,
                Components =
                {
                    new CuiTextComponent()
                    {
                        Text = "SERVER TIME: %TIME_LEFT%",
                        Align = TextAnchor.MiddleRight,
                        Font = "robotocondensed-bold.ttf",
                        FontSize = 14,
                        Color = HexToCuiColor("#E2DBD3")
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "930 -50",
                        OffsetMax = "1134 0"
                    },
                    new CuiCountdownComponent
                    {
                        EndTime = (float) new TimeSpan(24, 0, 0).TotalSeconds,
                        StartTime = (float) DateTime.Now.TimeOfDay.TotalSeconds,
                        Step = 1,
                        TimerFormat = TimerFormat.HoursMinutesSeconds,
                        DestroyIfDone = true,
                        Command = "UI_EventManager_UpdateTimer"
                    }
                }
            });
            #endregion

            #endregion

            #region Content

            int
                columnsOnPage = 6,
                columnWidth = 190,
                columnMarginX = 5,
                eventHeight = 38,
                eventMarginY = 4;

            var contentLayer = Layer + ".Content";

            container.Add(new CuiPanel
            {
                Image = { Color = HexToCuiColor("#000000", 0) },
                RectTransform =
                        {
                            AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-590 -233", OffsetMax = "590 225"
                        }
            }, Layer, contentLayer, contentLayer);

            var offsetX = -(columnsOnPage * columnWidth + (columnsOnPage - 1) * columnMarginX) / 2f;

            var authors = data.creators.Skip(@interface.page * columnsOnPage).Take(columnsOnPage).ToArray();
            try
            {
                for (var i = 0; i < authors.Length; i++)
                {
                    var targetAuthor = authors[i];
                    var events = data.events.FindAll(e => e.creator == targetAuthor);

                    var offsetLeft = offsetX + columnWidth * i + columnMarginX * i;

                    var eventPanel = container.Add(new CuiPanel
                    {
                        Image =
                                {
                                    Color = HexToCuiColor("#696969", 15),
                                    Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"
                                },
                        RectTransform =
                                {
                                    AnchorMin = "0.5 0", AnchorMax = "0.5 1",
                                    OffsetMin = $"{offsetLeft} 0", OffsetMax = $"{offsetLeft + columnWidth} 0"
                                }
                    }, contentLayer);

                    #region Header

                    var headerPanel = container.Add(new CuiPanel
                    {
                        Image =
                                {
                                    Color = HexToCuiColor("#696969", 40),
                                    Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"
                                },
                        RectTransform =
                                    {AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 -40", OffsetMax = "0 0"}
                    }, eventPanel);

                    container.Add(new CuiElement
                    {
                        Parent = headerPanel,
                        Components =
                                {
                                    new CuiTextComponent
                                    {
                                        Text = targetAuthor, Font = "robotocondensed-bold.ttf", FontSize = 18,
                                        Align = TextAnchor.MiddleCenter, Color = "1 1 1 1"
                                    },
                                    new CuiRectTransformComponent
                                        {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0"}
                                }
                    });

                    #endregion

                    #region Scroll View

                    var scrollView = eventPanel + ".ScrollView";

                    var eventsHeight = events.Count * eventHeight + (events.Count - 1) * eventMarginY;

                    eventsHeight = Mathf.Max(eventsHeight, 408);

                    container.Add(new CuiElement
                    {
                        Parent = eventPanel,
                        Name = scrollView,
                        Components =
                                {
                                    new CuiScrollViewComponent
                                    {
                                        MovementType = ScrollRect.MovementType.Clamped,
                                        Vertical = true,
                                        Inertia = true,
                                        Horizontal = false,
                                        Elasticity = 0.25f,
                                        DecelerationRate = 0.3f,
                                        ScrollSensitivity = 24f,
                                        ContentTransform = new CuiRectTransformComponent
                                        {
                                            AnchorMin = "0 1", AnchorMax = "1 1",
                                            OffsetMin = $"0 -{eventsHeight}", OffsetMax = "0 0"
                                        },
                                        VerticalScrollbar = new CuiScrollbar
                                        {
                                            AutoHide = true,
                                            Size = 3,
                                            HandleColor = HexToCuiColor("#D74933"),
                                            HighlightColor = HexToCuiColor("#D74933"),
                                            PressedColor = HexToCuiColor("#D74933"),
                                            HandleSprite = "assets/content/ui/UI.Background.TileTex.psd",
                                            TrackColor = HexToCuiColor("#38393F", 40),
                                            TrackSprite = "assets/content/ui/UI.Background.TileTex.psd"
                                        }
                                    },
                                    new CuiRectTransformComponent
                                        {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 5", OffsetMax = "0 -45"}
                                }
                    });

                    #endregion

                    #region Events

                    for (var x = 0; x < events.Count; x++)
                    {
                        var targetEvent = events[x];

                        var offsetUp = x * eventHeight + x * eventMarginY;

                        var eventLayer = Layer + $".Author.{targetAuthor}.Event.{targetEvent.displayName}";

                        container.Add(new CuiPanel
                        {
                            Image =
                                    {
                                        Color = HexToCuiColor("#696969", 60),
                                        Sprite = "assets/content/ui/UI.Background.Transparent.LinearLTR.tga"
                                    },
                            RectTransform =
                                    {
                                        AnchorMin = "0.5 1", AnchorMax = "0.5 1",
                                        OffsetMin = $"-90 -{offsetUp + eventHeight}", OffsetMax = $"88 -{offsetUp}"
                                    }
                        }, scrollView, eventLayer, eventLayer);

                        container.Add(new CuiPanel
                        {
                            Image = { Color = targetEvent.isLoaded ? HexToCuiColor("#2BDF31", 100) : HexToCuiColor("#DF2B2E", 100) },
                            RectTransform =
                                        {AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = "0 0", OffsetMax = "2 0"}
                        }, eventLayer);

                        container.Add(new CuiElement
                        {
                            Parent = eventLayer,
                            Components =
                                    {
                                        new CuiTextComponent
                                        {
                                            Text = targetEvent.displayName ?? string.Empty,
                                            Font = "robotocondensed-bold.ttf", FontSize = 14,
                                            Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 100),
                                            VerticalOverflow = VerticalWrapMode.Overflow
                                        },
                                        new CuiRectTransformComponent
                                        {
                                            AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "10 0", OffsetMax = "-20 0"
                                        }
                                    }
                        });

                        MeventCuiHelper.CreateButton(container, eventLayer,
                           command:
                           Command + $" random events select {targetEvent.eventId}",
                           color: "0 0 0 0",
                           anchorMin: "0 0", anchorMax: "1 1", offsetMin: "0 0", offsetMax: "0 0");
                    }

                    #endregion
                }
            }
            finally
            {
                Array.Clear(authors, 0, authors.Length);
            }

            #endregion

            MeventCuiHelper.CreateButton(container, Layer, command: Command + " random events next",
                           color: HexToCuiColor("#D74933", 100),
                           material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                           anchorMin: "1 0", anchorMax: "1 0", offsetMin: "-44 7", offsetMax: "-9 42",
                           msg: ">",
                           isBold: true,
                           fontSize: 26,
                           textAnchor: TextAnchor.MiddleCenter,
                           textColor: HexToCuiColor("#FFFFFF", 100));

            MeventCuiHelper.CreateButton(container, Layer, command: Command + " random events prev",
                color: HexToCuiColor("#696969", 30),
                material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                anchorMin: "1 0", anchorMax: "1 0", offsetMin: "-87 7", offsetMax: "-52 42",
                msg: "<",
                isBold: true,
                fontSize: 26,
                textAnchor: TextAnchor.MiddleCenter,
                textColor: HexToCuiColor("#FFFFFF", 100));

            CuiHelper.AddUi(player, container);
        }

        #endregion

        #region Modal
        private void DrawModal(BasePlayer player, string type, bool first = true)
        {
            if (!_interface.TryGetValue(player.userID, out var @interface)) return;

            CuiElementContainer container = new();

            switch (type)
            {
                case "edit":
                    {
                        if (first)
                            DrawBackground(player, container, LayerBackgroundModal, LayerBackground);

                        DrawMainPanel(player, container, "0.5 0.5", "0.5 0.5", "-273 -157", "274 157", LayerModal, LayerBackgroundModal);

                        #region Header

                        container.Add(new CuiElement
                        {
                            Name = LayerModal + "Header.Panel",
                            Parent = LayerModal,
                            Components = {
                                new CuiImageComponent { Color = HexToCuiColor("#696969", 100), Sprite = "assets/content/ui/UI.Background.Transparent.LinearLTR.tga", },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "0 -40",
                                    OffsetMax = "547 0"
                                }
                            }
                        });

                        container.Add(new CuiElement
                        {
                            Parent = LayerModal + "Header.Panel",
                            Components = {
                            new CuiTextComponent { Text = $"EDIT EVENT: {@interface.editEvent.displayName}", Font = "robotocondensed-bold.ttf", FontSize = 18, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#D9D9D9", 100) },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0 0",
                                AnchorMax = "1 1",
                                OffsetMin = "5 0"
                            }
                        }
                        });

                        MeventCuiHelper.CreateButton(container, LayerModal + "Header.Panel",
                            command: "",
                            close: LayerBackgroundModal,
                            color: HexToCuiColor("#E44028", 100),
                            material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                            sprite: "assets/content/ui/UI.Background.TileTex.psd",
                            anchorMin: "1 1", anchorMax: "1 1", offsetMin: "-40 -40", offsetMax: "0 0",
                            iconPath: "assets/icons/close.png",
                            iconRect: new CuiRectTransformComponent()
                            {
                                AnchorMin = "0 1",
                                AnchorMax = "0 1",
                                OffsetMin = "9 -31",
                                OffsetMax = "31 -9"
                            });

                        #endregion Header

                        DrawModalTimes(player, container);
                        break;
                    }

                case "new_event":
                    {
                        if (first)
                            DrawBackground(player, container, LayerBackgroundModal, LayerBackground);

                        DrawMainPanel(player, container, "0.5 0.5", "0.5 0.5", "-275 -137", "275 193", LayerModal, LayerBackgroundModal);

                        #region Header

                        container.Add(new CuiElement
                        {
                            Name = LayerModal + "Header.Panel",
                            Parent = LayerModal,
                            Components = {
                                new CuiImageComponent { Color = HexToCuiColor("#696969", 100), Sprite = "assets/content/ui/UI.Background.Transparent.LinearLTR.tga", },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "0 -40",
                                    OffsetMax = "547 0"
                                }
                            }
                        });

                        container.Add(new CuiElement
                        {
                            Parent = LayerModal + "Header.Panel",
                            Components = {
                            new CuiTextComponent { Text = $"NEW EVENT", Font = "robotocondensed-bold.ttf", FontSize = 18, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#D9D9D9", 100) },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0 0",
                                AnchorMax = "1 1",
                                OffsetMin = "5 0"
                            }
                        }
                        });

                        MeventCuiHelper.CreateButton(container, LayerModal + "Header.Panel",
                            command: "",
                            close: LayerBackgroundModal,
                            color: HexToCuiColor("#E44028", 100),
                            material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                            sprite: "assets/content/ui/UI.Background.TileTex.psd",
                            anchorMin: "1 1", anchorMax: "1 1", offsetMin: "-40 -40", offsetMax: "0 0",
                            iconPath: "assets/icons/close.png",
                            iconRect: new CuiRectTransformComponent()
                            {
                                AnchorMin = "0 1",
                                AnchorMax = "0 1",
                                OffsetMin = "9 -31",
                                OffsetMax = "31 -9"
                            });

                        #endregion Header

                        DrawModalEvent(player, container);

                        break;
                    }

                case "new_creator":
                    {
                        if (first)
                            DrawBackground(player, container, LayerBackgroundModal, LayerBackground);

                        DrawMainPanel(player, container, "0.5 0.5", "0.5 0.5", "-170 -24", "170 176", LayerModal, LayerBackgroundModal);

                        #region Header

                        container.Add(new CuiElement
                        {
                            Name = LayerModal + "Header.Panel",
                            Parent = LayerModal,
                            Components = {
                                new CuiImageComponent { Color = HexToCuiColor("#696969", 100), Sprite = "assets/content/ui/UI.Background.Transparent.LinearLTR.tga", },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "0 -40",
                                    OffsetMax = "340 0"
                                }
                            }
                        });

                        container.Add(new CuiElement
                        {
                            Parent = LayerModal + "Header.Panel",
                            Components = {
                            new CuiTextComponent { Text = $"NEW CREATOR", Font = "robotocondensed-bold.ttf", FontSize = 18, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#D9D9D9", 100) },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0 0",
                                AnchorMax = "1 1",
                                OffsetMin = "5 0"
                            }
                        }
                        });

                        MeventCuiHelper.CreateButton(container, LayerModal + "Header.Panel",
                            command: "",
                            close: LayerBackgroundModal,
                            color: HexToCuiColor("#E44028", 100),
                            material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                            sprite: "assets/content/ui/UI.Background.TileTex.psd",
                            anchorMin: "1 1", anchorMax: "1 1", offsetMin: "-40 -40", offsetMax: "0 0",
                            iconPath: "assets/icons/close.png",
                            iconRect: new CuiRectTransformComponent()
                            {
                                AnchorMin = "0 1",
                                AnchorMax = "0 1",
                                OffsetMin = "9 -31",
                                OffsetMax = "31 -9"
                            });

                        #endregion Header

                        DrawModalCreator(player, container, "Enter creator name...");
                        break;
                    }

                case "edit_creator":
                    {

                        if (first)
                            DrawBackground(player, container, LayerBackgroundModal, LayerBackground);

                        DrawMainPanel(player, container, "0.5 0.5", "0.5 0.5", "-170 -24", "170 176", LayerModal, LayerBackgroundModal);

                        #region Header

                        container.Add(new CuiElement
                        {
                            Name = LayerModal + "Header.Panel",
                            Parent = LayerModal,
                            Components = {
                                new CuiImageComponent { Color = HexToCuiColor("#696969", 100), Sprite = "assets/content/ui/UI.Background.Transparent.LinearLTR.tga", },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "0 -40",
                                    OffsetMax = "340 0"
                                }
                            }
                        });

                        container.Add(new CuiElement
                        {
                            Parent = LayerModal + "Header.Panel",
                            Components = {
                            new CuiTextComponent { Text = $"EDIT CREATOR", Font = "robotocondensed-bold.ttf", FontSize = 18, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#D9D9D9", 100) },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0 0",
                                AnchorMax = "1 1",
                                OffsetMin = "5 0"
                            }
                        }
                        });

                        MeventCuiHelper.CreateButton(container, LayerModal + "Header.Panel",
                            command: "",
                            close: LayerBackgroundModal,
                            color: HexToCuiColor("#E44028", 100),
                            material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                            sprite: "assets/content/ui/UI.Background.TileTex.psd",
                            anchorMin: "1 1", anchorMax: "1 1", offsetMin: "-40 -40", offsetMax: "0 0",
                            iconPath: "assets/icons/close.png",
                            iconRect: new CuiRectTransformComponent()
                            {
                                AnchorMin = "0 1",
                                AnchorMax = "0 1",
                                OffsetMin = "9 -31",
                                OffsetMax = "31 -9"
                            });

                        #endregion Header

                        DrawModalCreator(player, container, @interface.inputCreator, @interface.editCreatorIndex);
                        break;
                    }
            }

            CuiHelper.AddUi(player, container);
        }

        private void DrawModalTimes(BasePlayer player, CuiElementContainer container)
        {
            if (!_interface.TryGetValue(player.userID, out var @interface)) return;

            #region Times

            #region Scroll View
            var times = data.eventTimes.FindAll(t => !t.isRandom && t.eventsId.Contains(@interface.editEvent.eventId));


            var timeHeight = 38f;
            var timeMargin = 4f;

            var scrollView = "Time.Panel";

            var eventsHeight = (times.Count + 1) * timeHeight + (times.Count) * timeMargin;

            eventsHeight = Mathf.Max(eventsHeight, 266);

            container.Add(new CuiElement
            {
                Parent = LayerModal,
                Name = scrollView,
                DestroyUi = scrollView,
                Components =
                                {
                                    new CuiScrollViewComponent
                                    {
                                        MovementType = ScrollRect.MovementType.Clamped,
                                        Vertical = true,
                                        Inertia = true,
                                        Horizontal = false,
                                        Elasticity = 0.25f,
                                        DecelerationRate = 0.3f,
                                        ScrollSensitivity = 24f,
                                        ContentTransform = new CuiRectTransformComponent
                                        {
                                            AnchorMin = "0 1", AnchorMax = "1 1",
                                            OffsetMin = $"0 -{eventsHeight}", OffsetMax = "0 0"
                                        },
                                        VerticalScrollbar = new CuiScrollbar
                                        {
                                            AutoHide = true,
                                            Size = 3,
                                            HandleColor = HexToCuiColor("#D74933"),
                                            HighlightColor = HexToCuiColor("#D74933"),
                                            PressedColor = HexToCuiColor("#D74933"),
                                            HandleSprite = "assets/content/ui/UI.Background.TileTex.psd",
                                            TrackColor = HexToCuiColor("#38393F", 40),
                                            TrackSprite = "assets/content/ui/UI.Background.TileTex.psd"
                                        }
                                    },
                                    new CuiRectTransformComponent
                                    {
                                        AnchorMin = "0 1",
                                        AnchorMax = "0 1",
                                        OffsetMin = "4 -310",
                                        OffsetMax = "214 -44"
                                    }
                                }
            });

            container.Add(new CuiElement
            {
                Parent = scrollView,
                Components = {
                                new CuiImageComponent { Color = HexToCuiColor("#404040", 50) },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 0",
                                    AnchorMax = "1 1"
                                }
                            }
            });
            #endregion

            #region Event time
            var index = 0;
            float offset = 0;


            offset = -4 - index * timeHeight - index * timeMargin;
            container.Add(new CuiElement
            {
                Name = "ButtonAd",
                Parent = scrollView,
                Components = {
                    new CuiImageComponent { Color = HexToCuiColor("#696969", 100), Sprite = "assets/content/ui/UI.Background.Transparent.LinearLTR.tga" },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = $"4 {offset - timeHeight}",
                        OffsetMax = $"199 {offset}"
                    }
                }
            });

            #region Button (Add)

            container.Add(new CuiElement
            {
                Name = "icon",
                Parent = "ButtonAd",
                Components = {
                        new CuiImageComponent { Color = HexToCuiColor("#459D30", 100) },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "0 1",
                            OffsetMin = "10 -29",
                            OffsetMax = "29 -10"
                        }
                    }
            });

            container.Add(new CuiElement
            {
                Parent = "icon",
                Components = {
                    new CuiImageComponent { Sprite = "assets/icons/health.png" },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-5 -5",
                        OffsetMax = "6 6"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = "ButtonAd",
                Components =
                {
                    new CuiTextComponent { Text = "Add time", Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 100) },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "38 -27",
                        OffsetMax = "89 -11"
                    }
                }
            });

            container.Add(new CuiButton
            {
                RectTransform =
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 1",
                        },
                Button =
                        {
                            Color = "0 0 0 0",
                            Command = $"{Command} event time new"
                        },
            }, "ButtonAd");

            #endregion Button (Add)

            index++;

            foreach (var time in times)
            {
                offset = -4 - index * timeHeight - index * timeMargin;
                var timeLayer = CuiHelper.GetGuid();

                container.Add(new CuiElement
                {
                    Name = timeLayer,
                    Parent = scrollView,
                    Components = {
                        new CuiImageComponent { Color = HexToCuiColor(index == @interface.editIndex ? "D74933"  : "696969", 100), Sprite = "assets/content/ui/UI.Background.Transparent.LinearLTR.tga", },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "0 1",
                            OffsetMin = $"4 {offset - timeHeight}",
                            OffsetMax = $"199 {offset}"
                        }
                    }
                });

                container.Add(new CuiElement
                {
                    Parent = timeLayer,
                    Components = {
                        new CuiTextComponent { Text = time.displayInfo, Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 100) },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 1",
                            OffsetMin = "10 0"
                        }
                    }
                });

                container.Add(new CuiButton
                {
                    RectTransform =
                            {
                                AnchorMin = "0 0",
                                AnchorMax = "1 1",
                            },
                    Button =
                            {
                                Color = "0 0 0 0",
                                Command = $"{Command} event time edit {data.eventTimes.IndexOf(time)} {index}"
                            },
                }, timeLayer);

                index++;
            }

            #endregion Event time

            #endregion

        }

        private void DrawModalTime(BasePlayer player)
        {
            if (!_interface.TryGetValue(player.userID, out var @interface)) return;

            CuiElementContainer container = new();

            #region Panel
            var panelName = LayerModal + ".Time";

            container.Add(new CuiElement
            {
                Name = panelName,
                DestroyUi = panelName,
                Parent = LayerModal,
                Components = {
                    new CuiImageComponent { Color = HexToCuiColor("#404040", 50) },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "218 -310",
                        OffsetMax = "543 -44"
                    }
                }
            });
            #endregion

            #region Header

            container.Add(new CuiElement
            {
                Parent = panelName,
                Components = {
                    new CuiTextComponent { Text = "TIME SETTINGS", Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 100) },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "7 -22",
                        OffsetMax = "85 -8"
                    }
                }
            });
            #endregion

            DrawModalType(player, container, @interface.editTime.type, panelName);

            #region MinPlayers
            container.Add(new CuiElement
            {
                Parent = panelName,
                Components = {
                    new CuiTextComponent { Text = "MIN | MAX PLAYERS", Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.UpperLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "223 -45",
                        OffsetMax = "321 -32"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Name = panelName + ".MinPlayers",
                Parent = panelName,
                Components = {
                    new CuiImageComponent { Color = HexToCuiColor("#404040", 100), Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "223 -77",
                        OffsetMax = "261 -47"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = panelName + ".MinPlayers",
                Components = {
                    new CuiNeedsKeyboardComponent(),
                    new CuiInputFieldComponent { Color = HexToCuiColor("#FFFFFF", 100), FontSize = 16, Align = TextAnchor.MiddleCenter, IsPassword = false, Command = $"{Command} event time minplayers", Text =  @interface.editTime.minPlayers.ToString()  },
                    new CuiRectTransformComponent {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1"
                    }
                }
            });


            container.Add(new CuiElement
            {
                Name = panelName + ".MaxPlayers",
                Parent = panelName,
                Components = {
                    new CuiImageComponent { Color = HexToCuiColor("#404040", 100), Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "265 -77",
                        OffsetMax = "303 -47"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = panelName + ".MaxPlayers",
                Components = {
                    new CuiNeedsKeyboardComponent(),
                    new CuiInputFieldComponent { Color = HexToCuiColor("#FFFFFF", 100), FontSize = 16, Align = TextAnchor.MiddleCenter, IsPassword = false, Command = $"{Command} event time maxplayers", Text =  @interface.editTime.maxPlayers.ToString()  },
                    new CuiRectTransformComponent {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1"
                    }
                }
            });
            #endregion

            switch (@interface.editTime.type)
            {
                case EventType.Static:
                    {
                        container.Add(new CuiElement
                        {
                            Parent = panelName,
                            Components = {
                                new CuiTextComponent { Text = "TIME", Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.UpperLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "7 -100",
                                    OffsetMax = "130 -87"
                                }
                            }
                        });

                        #region Hour
                        container.Add(new CuiElement
                        {
                            Name = panelName + ".MinMinutes",
                            Parent = panelName,
                            Components = {
                                new CuiImageComponent { Color = HexToCuiColor("#404040", 100), Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "7 -132",
                                    OffsetMax = "75 -102"
                                }
                            }
                        });

                        container.Add(new CuiElement
                        {
                            Parent = panelName + ".MinMinutes",
                            Components = {
                                new CuiNeedsKeyboardComponent(),
                                new CuiInputFieldComponent { Color = HexToCuiColor("#FFFFFF", 100), FontSize = 16, Align = TextAnchor.MiddleCenter, IsPassword = false, Command = $"{Command} event time hour", Text =  @interface.editTime.hour.ToString()},
                                new CuiRectTransformComponent {
                                    AnchorMin = "0 0",
                                    AnchorMax = "1 1"
                                }
                            }
                        });


                        container.Add(new CuiElement
                        {
                            Parent = panelName,
                            Components = {
                                new CuiTextComponent { Text = "HOUR", Font = "robotocondensed-regular.ttf", FontSize = 8, Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#8F8F8F", 100) },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "30 -145",
                                    OffsetMax = "54 -131"
                                }
                            }
                        });
                        #endregion

                        #region Minute
                        container.Add(new CuiElement
                        {
                            Name = panelName + ".MaxMinutes",
                            Parent = panelName,
                            Components = {
                                new CuiImageComponent { Color = HexToCuiColor("#404040", 100), Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "80 -132",
                                    OffsetMax = "148 -102"
                                }
                            }
                        });

                        container.Add(new CuiElement
                        {
                            Parent = panelName + ".MaxMinutes",
                            Components = {
                                new CuiNeedsKeyboardComponent(),
                                new CuiInputFieldComponent { Color = HexToCuiColor("#FFFFFF", 100), FontSize = 16, Align = TextAnchor.MiddleCenter, IsPassword = false, Command = $"{Command} event time minute", Text =  @interface.editTime.minute.ToString()},
                                new CuiRectTransformComponent {
                                    AnchorMin = "0 0",
                                    AnchorMax = "1 1"
                                }
                            }
                        });


                        container.Add(new CuiElement
                        {
                            Parent = panelName,
                            Components = {
                                new CuiTextComponent { Text = "MINUTE", Font = "robotocondensed-regular.ttf", FontSize = 8, Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#8F8F8F", 100) },
                                new CuiRectTransformComponent
                                {
                                     AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "102 -145",
                                    OffsetMax = "128 -131"
                                }
                            }
                        });
                        #endregion

                        container.Add(new CuiElement
                        {
                            Parent = panelName,
                            Components = {
                                new CuiTextComponent { Text = "OFFSET TIME (MINUTES)", Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.UpperLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "163 -100",
                                    OffsetMax = "258 -87"
                                }
                            }
                        });

                        #region Min
                        container.Add(new CuiElement
                        {
                            Name = panelName + ".MinMinutes",
                            Parent = panelName,
                            Components = {
                                new CuiImageComponent { Color = HexToCuiColor("#404040", 100), Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "163 -132",
                                    OffsetMax = "231 -102"
                                }
                            }
                        });

                        container.Add(new CuiElement
                        {
                            Parent = panelName + ".MinMinutes",
                            Components = {
                                new CuiNeedsKeyboardComponent(),
                                new CuiInputFieldComponent { Color = HexToCuiColor("#FFFFFF", 100), FontSize = 16, Align = TextAnchor.MiddleCenter, IsPassword = false, Command = $"{Command} event time offsetmin", Text =  @interface.editTime.offsetMin > 0 ?  @interface.editTime.offsetMin.ToString() : "MM"},
                                new CuiRectTransformComponent {
                                    AnchorMin = "0 0",
                                    AnchorMax = "1 1"
                                }
                            }
                        });


                        container.Add(new CuiElement
                        {
                            Parent = panelName,
                            Components = {
                                new CuiTextComponent { Text = "MIN", Font = "robotocondensed-regular.ttf", FontSize = 8, Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#8F8F8F", 100) },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "191 -145",
                                    OffsetMax = "205 -131"
                                }
                            }
                        });
                        #endregion

                        #region Max
                        container.Add(new CuiElement
                        {
                            Name = panelName + ".MaxMinutes",
                            Parent = panelName,
                            Components = {
                                new CuiImageComponent { Color = HexToCuiColor("#404040", 100), Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                                new CuiRectTransformComponent
                                {
                                   AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "236 -132",
                                    OffsetMax = "304 -102"
                                }
                            }
                        });

                        container.Add(new CuiElement
                        {
                            Parent = panelName + ".MaxMinutes",
                            Components = {
                                new CuiNeedsKeyboardComponent(),
                                new CuiInputFieldComponent { Color = HexToCuiColor("#FFFFFF", 100), FontSize = 16, Align = TextAnchor.MiddleCenter, IsPassword = false, Command = $"{Command} event time offsetmax", Text =  @interface.editTime.offsetMax > 0 ?  @interface.editTime.offsetMax.ToString() : "MM"},
                                new CuiRectTransformComponent {
                                    AnchorMin = "0 0",
                                    AnchorMax = "1 1"
                                }
                            }
                        });


                        container.Add(new CuiElement
                        {
                            Parent = panelName,
                            Components = {
                                new CuiTextComponent { Text = "MAX", Font = "robotocondensed-regular.ttf", FontSize = 8, Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#8F8F8F", 100) },
                                new CuiRectTransformComponent
                                {
                                     AnchorMin = "0 1",
                                      AnchorMax = "0 1",
                                      OffsetMin = "263 -145",
                                      OffsetMax = "279 -131"
                                }
                            }
                        });
                        #endregion

                        #region Weeks

                        container.Add(new CuiElement
                        {
                            Parent = panelName,
                            Components = {
                                new CuiTextComponent { Text = "SELECT DAYS", Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.UpperLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "7 -182",
                                    OffsetMax = "67 -169"
                                }
                            }
                        });

                        float width = 75f;
                        float height = 12f;
                        float marginX = 3f;
                        float marginY = 5f;
                        var itemsOffsetX = 7f;
                        var itemsOffsetY = -184f;

                        int index = 0;

                        foreach (var day in Enum.GetValues(typeof(DayOfWeek)))
                        {
                            var layer = CuiHelper.GetGuid();

                            container.Add(new CuiElement
                            {
                                Name = layer,
                                Parent = panelName,
                                Components = {
                                    new CuiImageComponent { Color = HexToCuiColor("#000000", 0) },
                                    new CuiRectTransformComponent
                                    {
                                        AnchorMin = "0 1",
                                        AnchorMax = "0 1",
                                        OffsetMin = $"{itemsOffsetX} {itemsOffsetY - height}",
                                        OffsetMax = $"{itemsOffsetX + width} {itemsOffsetY}"
                                    }
                                }
                            });

                            MeventCuiHelper.CreateButton(container, layer,
                                       command:
                                       Command + $" event time day {day}",
                                       color: HexToCuiColor(@interface.editTime.activeDays.Contains((DayOfWeek)day) ? "#E44028" : "404040", 100),
                                       anchorMin: "0 1", anchorMax: "0 1", offsetMin: "0 -12", offsetMax: "12 0");

                            container.Add(new CuiElement
                            {
                                Parent = layer,
                                Components = {
                                    new CuiTextComponent { Text = day.ToString(), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                                    new CuiRectTransformComponent
                                    {
                                        AnchorMin = "0 1",
                                        AnchorMax = "0 1",
                                        OffsetMin = "15 -12",
                                        OffsetMax = "75 0"
                                    }
                                }
                            });

                            if ((index + 1) % 4 == 0)
                            {
                                itemsOffsetX = 7f;
                                itemsOffsetY = itemsOffsetY - height - marginY;
                            }
                            else
                            {
                                itemsOffsetX = itemsOffsetX + width + marginX;
                            }

                            index++;
                        }

                        #endregion
                        break;
                    }

                case EventType.Every:
                    {
                        container.Add(new CuiElement
                        {
                            Parent = panelName,
                            Components = {
                                new CuiTextComponent { Text = "EVERY TIME (MINUTES)", Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.UpperLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "7 -100",
                                    OffsetMax = "102 -87"
                                }
                            }
                        });

                        #region Interval
                        container.Add(new CuiElement
                        {
                            Name = panelName + ".MinMinutes",
                            Parent = panelName,
                            Components = {
                                new CuiImageComponent { Color = HexToCuiColor("#404040", 100), Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "7 -132",
                                    OffsetMax = "75 -102"
                                }
                            }
                        });

                        container.Add(new CuiElement
                        {
                            Parent = panelName + ".MinMinutes",
                            Components = {
                                new CuiNeedsKeyboardComponent(),
                                new CuiInputFieldComponent { Color = HexToCuiColor("#FFFFFF", 100), FontSize = 16, Align = TextAnchor.MiddleCenter, IsPassword = false, Command = $"{Command} event time interval", Text =  @interface.editTime.interval > 0 ?  @interface.editTime.interval.ToString() : "MM"},
                                new CuiRectTransformComponent {
                                    AnchorMin = "0 0",
                                    AnchorMax = "1 1"
                                }
                            }
                        });
                        #endregion

                        #region Weeks

                        container.Add(new CuiElement
                        {
                            Parent = panelName,
                            Components = {
                                new CuiTextComponent { Text = "SELECT DAYS", Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.UpperLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "7 -182",
                                    OffsetMax = "67 -169"
                                }
                            }
                        });

                        float width = 75f;
                        float height = 12f;
                        float marginX = 3f;
                        float marginY = 5f;
                        var itemsOffsetX = 7f;
                        var itemsOffsetY = -184f;

                        int index = 0;

                        foreach (var day in Enum.GetValues(typeof(DayOfWeek)))
                        {
                            var layer = CuiHelper.GetGuid();

                            container.Add(new CuiElement
                            {
                                Name = layer,
                                Parent = panelName,
                                Components = {
                                    new CuiImageComponent { Color = HexToCuiColor("#000000", 0) },
                                    new CuiRectTransformComponent
                                    {
                                        AnchorMin = "0 1",
                                        AnchorMax = "0 1",
                                        OffsetMin = $"{itemsOffsetX} {itemsOffsetY - height}",
                                        OffsetMax = $"{itemsOffsetX + width} {itemsOffsetY}"
                                    }
                                }
                            });

                            MeventCuiHelper.CreateButton(container, layer,
                                       command:
                                       Command + $" event time day {day}",
                                       color: HexToCuiColor(@interface.editTime.activeDays.Contains((DayOfWeek)day) ? "#E44028" : "404040", 100),
                                       anchorMin: "0 1", anchorMax: "0 1", offsetMin: "0 -12", offsetMax: "12 0");

                            container.Add(new CuiElement
                            {
                                Parent = layer,
                                Components = {
                                    new CuiTextComponent { Text = day.ToString(), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                                    new CuiRectTransformComponent
                                    {
                                        AnchorMin = "0 1",
                                        AnchorMax = "0 1",
                                        OffsetMin = "15 -12",
                                        OffsetMax = "75 0"
                                    }
                                }
                            });

                            if ((index + 1) % 4 == 0)
                            {
                                itemsOffsetX = 7f;
                                itemsOffsetY = itemsOffsetY - height - marginY;
                            }
                            else
                            {
                                itemsOffsetX = itemsOffsetX + width + marginX;
                            }

                            index++;
                        }

                        #endregion
                        break;
                    }

                case EventType.Random:
                    {
                        container.Add(new CuiElement
                        {
                            Parent = panelName,
                            Components = {
                                new CuiTextComponent { Text = "RANDOM TIME (MINUTES)", Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.UpperLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "7 -100",
                                    OffsetMax = "130 -87"
                                }
                            }
                        });

                        #region Min
                        container.Add(new CuiElement
                        {
                            Name = panelName + ".MinMinutes",
                            Parent = panelName,
                            Components = {
                                new CuiImageComponent { Color = HexToCuiColor("#404040", 100), Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "7 -132",
                                    OffsetMax = "75 -102"
                                }
                            }
                        });

                        container.Add(new CuiElement
                        {
                            Parent = panelName + ".MinMinutes",
                            Components = {
                                new CuiNeedsKeyboardComponent(),
                                new CuiInputFieldComponent { Color = HexToCuiColor("#FFFFFF", 100), FontSize = 16, Align = TextAnchor.MiddleCenter, IsPassword = false, Command = $"{Command} event time offsetmin", Text =  @interface.editTime.offsetMin > 0 ?  @interface.editTime.offsetMin.ToString() : "MM"},
                                new CuiRectTransformComponent {
                                    AnchorMin = "0 0",
                                    AnchorMax = "1 1"
                                }
                            }
                        });


                        container.Add(new CuiElement
                        {
                            Parent = panelName,
                            Components = {
                                new CuiTextComponent { Text = "MIN", Font = "robotocondensed-regular.ttf", FontSize = 8, Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#8F8F8F", 100) },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "35 -145",
                                    OffsetMax = "49 -131"
                                }
                            }
                        });
                        #endregion

                        #region Max
                        container.Add(new CuiElement
                        {
                            Name = panelName + ".MaxMinutes",
                            Parent = panelName,
                            Components = {
                                new CuiImageComponent { Color = HexToCuiColor("#404040", 100), Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "80 -132",
                                    OffsetMax = "148 -102"
                                }
                            }
                        });

                        container.Add(new CuiElement
                        {
                            Parent = panelName + ".MaxMinutes",
                            Components = {
                                new CuiNeedsKeyboardComponent(),
                                new CuiInputFieldComponent { Color = HexToCuiColor("#FFFFFF", 100), FontSize = 16, Align = TextAnchor.MiddleCenter, IsPassword = false, Command = $"{Command} event time offsetmax", Text =  @interface.editTime.offsetMax > 0 ?  @interface.editTime.offsetMax.ToString() : "MM"},
                                new CuiRectTransformComponent {
                                    AnchorMin = "0 0",
                                    AnchorMax = "1 1"
                                }
                            }
                        });


                        container.Add(new CuiElement
                        {
                            Parent = panelName,
                            Components = {
                                new CuiTextComponent { Text = "MAX", Font = "robotocondensed-regular.ttf", FontSize = 8, Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#8F8F8F", 100) },
                                new CuiRectTransformComponent
                                {
                                     AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "107 -145",
                                    OffsetMax = "123 -131"
                                }
                            }
                        });
                        #endregion

                        #region Weeks

                        container.Add(new CuiElement
                        {
                            Parent = panelName,
                            Components = {
                                new CuiTextComponent { Text = "SELECT DAYS", Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.UpperLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 1",
                                    AnchorMax = "0 1",
                                    OffsetMin = "7 -182",
                                    OffsetMax = "67 -169"
                                }
                            }
                        });

                        float width = 75f;
                        float height = 12f;
                        float marginX = 3f;
                        float marginY = 5f;
                        var itemsOffsetX = 7f;
                        var itemsOffsetY = -184f;

                        int index = 0;

                        foreach (var day in Enum.GetValues(typeof(DayOfWeek)))
                        {
                            var layer = CuiHelper.GetGuid();

                            container.Add(new CuiElement
                            {
                                Name = layer,
                                Parent = panelName,
                                Components = {
                                    new CuiImageComponent { Color = HexToCuiColor("#000000", 0) },
                                    new CuiRectTransformComponent
                                    {
                                        AnchorMin = "0 1",
                                        AnchorMax = "0 1",
                                        OffsetMin = $"{itemsOffsetX} {itemsOffsetY - height}",
                                        OffsetMax = $"{itemsOffsetX + width} {itemsOffsetY}"
                                    }
                                }
                            });

                            MeventCuiHelper.CreateButton(container, layer,
                                       command:
                                       Command + $" event time day {day}",
                                       color: HexToCuiColor(@interface.editTime.activeDays.Contains((DayOfWeek)day) ? "#E44028" : "404040", 100),
                                       anchorMin: "0 1", anchorMax: "0 1", offsetMin: "0 -12", offsetMax: "12 0");

                            container.Add(new CuiElement
                            {
                                Parent = layer,
                                Components = {
                                    new CuiTextComponent { Text = day.ToString(), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                                    new CuiRectTransformComponent
                                    {
                                        AnchorMin = "0 1",
                                        AnchorMax = "0 1",
                                        OffsetMin = "15 -12",
                                        OffsetMax = "75 0"
                                    }
                                }
                            });

                            if ((index + 1) % 4 == 0)
                            {
                                itemsOffsetX = 7f;
                                itemsOffsetY = itemsOffsetY - height - marginY;
                            }
                            else
                            {
                                itemsOffsetX = itemsOffsetX + width + marginX;
                            }

                            index++;
                        }

                        #endregion
                        break;
                    }
            }


            #region Start
            MeventCuiHelper.CreateButton(container, panelName, "START", Command + " event time start",
                color: HexToCuiColor("#005FB7", 100),
                material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                anchorMin: "0 1", anchorMax: "0 1", offsetMin: "139 -259", offsetMax: "199 -239",
                isBold: true,
                fontSize: 12,
                textAnchor: TextAnchor.MiddleCenter,
                textColor: HexToCuiColor("#FFFFFF", 100));
            #endregion

            #region Save
            MeventCuiHelper.CreateButton(container, panelName, "SAVE", Command + " event time save",
                color: HexToCuiColor("#459D30", 100),
                material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                anchorMin: "0 1", anchorMax: "0 1", offsetMin: "204 -259", offsetMax: "257 -239",
                isBold: true,
                fontSize: 12,
                textAnchor: TextAnchor.MiddleCenter,
                textColor: HexToCuiColor("#FFFFFF", 100));
            #endregion

            #region Delete
            MeventCuiHelper.CreateButton(container, panelName, "DELETE", Command + " event time delete",
                color: HexToCuiColor("#B63537", 100),
                material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                anchorMin: "0 1", anchorMax: "0 1", offsetMin: "261 -259", offsetMax: "322 -239",
                isBold: true,
                fontSize: 12,
                textAnchor: TextAnchor.MiddleCenter,
                textColor: HexToCuiColor("#FFFFFF", 100));
            #endregion

            CuiHelper.AddUi(player, container);
        }

        private void DrawModalType(BasePlayer player, CuiElementContainer container, EventType curType, string parent)
        {
            var layerName = parent + ".Type";

            #region Header
            container.Add(new CuiElement
            {
                Parent = parent,
                Components = {
                    new CuiTextComponent { Text = "REPEAT TYPE", Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.UpperLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "7 -45",
                        OffsetMax = "107 -32"
                    }
                }
            });
            #endregion


            #region Panel
            container.Add(new CuiElement
            {
                Name = layerName,
                Parent = parent,
                Components = {
                    new CuiImageComponent { Color = HexToCuiColor("#404040", 100), Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "7 -77",
                        OffsetMax = "209 -47"
                    }
                }
            });
            #endregion

            #region Type
            container.Add(new CuiElement
            {
                Parent = layerName,
                Components = {
                    new CuiTextComponent { Text = curType.ToString(), Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#E2DBD3", 90) },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "30 -30",
                        OffsetMax = "172 0"
                    }
                }
            });
            #endregion

            #region Btn.Prev
            MeventCuiHelper.CreateButton(container, layerName,
                                       command:
                                       Command + $" event time prev",
                                       color: HexToCuiColor("4D4D4D", 100),
                                       msg: "<",
                                       textAnchor: TextAnchor.MiddleCenter,
                                       textColor: HexToCuiColor("E2DBD3", 80),
                                       anchorMin: "0 1", anchorMax: "0 1", offsetMin: "0 -30", offsetMax: "30 0");
            #endregion

            #region Btn.Next
            MeventCuiHelper.CreateButton(container, layerName,
                                       command:
                                       Command + $" event time next",
                                       color: HexToCuiColor("4D4D4D", 100),
                                       msg: ">",
                                       textAnchor: TextAnchor.MiddleCenter,
                                       textColor: HexToCuiColor("E2DBD3", 80),
                                       anchorMin: "0 1", anchorMax: "0 1", offsetMin: "172 -30", offsetMax: "202 0");
            #endregion
        }

        private void DrawModalCreator(BasePlayer player, CuiElementContainer container, string creatorName, int index = -1)
        {
            var layerName = LayerModal + ".NewCreator";

            container.Add(new CuiElement
            {
                Name = layerName,
                DestroyUi = layerName,
                Parent = LayerModal,
                Components = {
                    new CuiImageComponent { Color = HexToCuiColor("#404040", 50)  },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "0 -200",
                        OffsetMax = "340 -40"
                    }
                }
            });



            container.Add(new CuiElement
            {
                Parent = layerName,
                Components = {
                    new CuiTextComponent { Text = "CREATOR NAME", Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "40 -31",
                        OffsetMax = "177 -14"
                    }
                }
            });


            container.Add(new CuiElement
            {
                Name = layerName + ".InputName",
                Parent = layerName,
                Components = {
                    new CuiImageComponent { Color = HexToCuiColor("#404040", 100), Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "40 -78",
                        OffsetMax = "300 -36"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = layerName + ".InputName",
                Components = {
                                new CuiNeedsKeyboardComponent(),
                                new CuiInputFieldComponent { Color = HexToCuiColor("#E2DBD3", 50), FontSize = 16, Align = TextAnchor.MiddleLeft, IsPassword = false, Command = $"{Command} creator name", Text =  creatorName},
                                new CuiRectTransformComponent {
                                    AnchorMin = "0 0",
                                    AnchorMax = "1 1",
                                    OffsetMin = "5 0"
                                }
                            }
            });


            #region Save
            MeventCuiHelper.CreateButton(container, layerName, "CREATE", Command + $" creator save {creatorName}",
                color: HexToCuiColor("#459D30", 100),
                material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                anchorMin: "0 1", anchorMax: "0 1", offsetMin: "180 -132", offsetMax: "300 -92",
                isBold: true,
                fontSize: 16,
                textAnchor: TextAnchor.MiddleCenter,
                textColor: HexToCuiColor("#FFFFFF", 100));
            #endregion

            #region Delete
            MeventCuiHelper.CreateButton(container, layerName, "CANCEL",
                close: LayerBackgroundModal,
                color: HexToCuiColor("#B63537", 100),
                material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                anchorMin: "0 1", anchorMax: "0 1", offsetMin: "41 -132", offsetMax: "161 -92",
                isBold: true,
                fontSize: 16,
                textAnchor: TextAnchor.MiddleCenter,
                textColor: HexToCuiColor("#FFFFFF", 100));
            #endregion
        }

        private void DrawModalEvent(BasePlayer player, CuiElementContainer container)
        {
            if (!_interface.TryGetValue(player.userID, out var @interface)) return;

            container.Add(new CuiElement
            {
                Parent = LayerModal,
                Components = {
                    new CuiImageComponent { Color = HexToCuiColor("#404040", 50)  },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-275 -165",
                        OffsetMax = "275 125"
                    }
                }
            });

            #region name

            container.Add(new CuiElement
            {
                Parent = LayerModal,
                Components = {
                    new CuiTextComponent { Text = "EVENT NAME", Font = "robotocondensed-regular.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "12 -71",
                        OffsetMax = "272 -51"
                    }
                }
            });


            container.Add(new CuiElement
            {
                Name = LayerModal + ".InputName",
                Parent = LayerModal,
                Components = {
                    new CuiImageComponent { Color = HexToCuiColor("#404040", 100), Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"  },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "12 -118",
                        OffsetMax = "272 -76"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = LayerModal + ".InputName",
                Components = {
                                new CuiNeedsKeyboardComponent(),
                                new CuiInputFieldComponent { Color = HexToCuiColor("#E2DBD3", 50), Font = "robotocondensed-regular.ttf", FontSize = 16, Align = TextAnchor.MiddleLeft, IsPassword = false, Command = $"{Command} event add name", Text =  string.IsNullOrEmpty(@interface.editEvent.displayName) ? "Enter event name..." : @interface.editEvent.displayName},
                                new CuiRectTransformComponent {
                                    AnchorMin = "0 0",
                                    AnchorMax = "1 1",
                                    OffsetMin = "5 0"
                                }
                            }
            });
            #endregion

            #region command

            container.Add(new CuiElement
            {
                Parent = LayerModal,
                Components = {
                    new CuiTextComponent { Text = "COMMAND", Font = "robotocondensed-regular.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "12 -146",
              OffsetMax = "272 -126"
                    }
                }
            });


            container.Add(new CuiElement
            {
                Name = LayerModal + ".InputCommand",
                Parent = LayerModal,
                Components = {
                    new CuiImageComponent { Color = HexToCuiColor("#404040", 100), Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"  },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "12 -188",
              OffsetMax = "272 -146"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = LayerModal + ".InputCommand",
                Components = {
                                new CuiNeedsKeyboardComponent(),
                                new CuiInputFieldComponent { Color = HexToCuiColor("#E2DBD3", 50), Font = "robotocondensed-regular.ttf", FontSize = 16, Align = TextAnchor.MiddleLeft, IsPassword = false, Command = $"{Command} event add command", Text =  string.IsNullOrEmpty(@interface.editEvent.command) ? "Enter event command..." : @interface.editEvent.command},
                                new CuiRectTransformComponent {
                                    AnchorMin = "0 0",
                                    AnchorMax = "1 1",
                                    OffsetMin = "5 0"
                                }
                            }
            });
            #endregion

            #region plugin

            container.Add(new CuiElement
            {
                Parent = LayerModal,
                Components = {
                    new CuiTextComponent { Text = "PLUGIN NAME", Font = "robotocondensed-regular.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "12 -216",
              OffsetMax = "225 -196"
                    }
                }
            });


            container.Add(new CuiElement
            {
                Name = LayerModal + ".InputPlg",
                Parent = LayerModal,
                Components = {
                    new CuiImageComponent { Color = HexToCuiColor("#404040", 100), Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"  },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "12 -258",
              OffsetMax = "272 -216"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = LayerModal + ".InputPlg",
                Components = {
                                new CuiNeedsKeyboardComponent(),
                                new CuiInputFieldComponent { Color = HexToCuiColor("#E2DBD3", 50),Font = "robotocondensed-regular.ttf", FontSize = 16, Align = TextAnchor.MiddleLeft, IsPassword = false, Command = $"{Command} event add plugin", Text =  string.IsNullOrEmpty(@interface.editEvent.pluginName) ? "Enter plugin name..." : @interface.editEvent.pluginName},
                                new CuiRectTransformComponent {
                                    AnchorMin = "0 0",
                                    AnchorMax = "1 1",
                                    OffsetMin = "5 0"
                                }
                            }
            });
            #endregion

            #region creator

            container.Add(new CuiElement
            {
                Parent = LayerModal,
                Components = {
                    new CuiTextComponent { Text = "EVENT CREATOR", Font = "robotocondensed-regular.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#E2DBD3", 90) },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "279 -69",
              OffsetMax = "394 -49"
                    }
                }
            });


            container.Add(new CuiElement
            {
                Name = LayerModal + ".Creator",
                Parent = LayerModal,
                Components = {
                    new CuiImageComponent { Color = HexToCuiColor("#404040", 100), Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"  },
                    new CuiRectTransformComponent
                    {
                         AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "279 -118",
              OffsetMax = "539 -76"
                    }
                }
            });


            container.Add(new CuiElement
            {
                Parent = LayerModal + ".Creator",
                Components = {
                    new CuiTextComponent { Text = string.IsNullOrEmpty(@interface.editEvent.creator) ? "Choose event creator..." : @interface.editEvent.creator, Font = "robotocondensed-regular.ttf",FontSize = 16, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#E2DBD3", 50) },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "15 -31",
              OffsetMax = "260 -11"
                    }
                }
            });

            #region list
            var scrollView = LayerModal + ".ScrollView";
            var height = 30f;
            var margin = 4f;
            var index = 0;
            var offsetTop = 5f;

            var creatorsHeight = data.creators.Count * height + (data.creators.Count - 1) * margin + offsetTop * 2;

            creatorsHeight = Mathf.Max(creatorsHeight, 206);

            container.Add(new CuiElement
            {
                Parent = LayerModal,
                Name = scrollView,
                Components =
                                {
                                    new CuiScrollViewComponent
                                    {
                                        MovementType = ScrollRect.MovementType.Clamped,
                                        Vertical = true,
                                        Inertia = true,
                                        Horizontal = false,
                                        Elasticity = 0.25f,
                                        DecelerationRate = 0.3f,
                                        ScrollSensitivity = 24f,
                                        ContentTransform = new CuiRectTransformComponent
                                        {
                                            AnchorMin = "0 1", AnchorMax = "1 1",
                                            OffsetMin = $"0 -{creatorsHeight}", OffsetMax = "0 0"
                                        },
                                        VerticalScrollbar = new CuiScrollbar
                                        {
                                            AutoHide = true,
                                            Size = 3,
                                            HandleColor = HexToCuiColor("#D74933"),
                                            HighlightColor = HexToCuiColor("#D74933"),
                                            PressedColor = HexToCuiColor("#D74933"),
                                            HandleSprite = "assets/content/ui/UI.Background.TileTex.psd",
                                            TrackColor = HexToCuiColor("#38393F", 40),
                                            TrackSprite = "assets/content/ui/UI.Background.TileTex.psd"
                                        }
                                    },
                                    new CuiRectTransformComponent
                                        {
                                        AnchorMin = "0 1",
              AnchorMax = "0 1",
              OffsetMin = "279 -324",
              OffsetMax = "539 -118"
                                    }
                                }
            });

            foreach (var creator in data.creators)
            {
                var offset = -offsetTop - index * height - index * margin;

                MeventCuiHelper.CreateButton(container, scrollView,
                                       command:
                                       Command + $" event add creator {creator}",
                                       color: HexToCuiColor(@interface.editEvent.creator == creator ? "#D74933" : "#4E4E4E", 100),
                                       msg: creator,
                                       anchorMin: "0 1",
                                       anchorMax: "0 1",
                                       offsetMin: $"5 {offset - height}",
                                       offsetMax: $"245 {offset}",
                                       textColor: HexToCuiColor("#E2DBD3", 90),
                                       textAnchor: TextAnchor.MiddleCenter,
                                       fontSize: 14,
                                       textRect: new CuiRectTransformComponent()
                                       {
                                           AnchorMin = "0 1",
                                           AnchorMax = "0 1",
                                           OffsetMin = "11 -69",
                                           OffsetMax = "245 -39"
                                       }
                                       );

                index++;
            }
            #endregion

            #endregion

            #region Save
            MeventCuiHelper.CreateButton(container, LayerModal, "CREATE", Command + $" event save",
                color: HexToCuiColor("#459D30", 100),
                material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                anchorMin: "0 1", anchorMax: "0 1", offsetMin: "13 -316", offsetMax: "113 -276",
                isBold: true,
                fontSize: 16,
                textAnchor: TextAnchor.MiddleCenter,
                textColor: HexToCuiColor("#FFFFFF", 100));
            #endregion
        }
        #endregion

        #region Functions
        private static string HexToCuiColor(string hex, float alpha = 100)
        {
            if (string.IsNullOrEmpty(hex)) hex = "#FFFFFF";

            var str = hex.Trim('#');
            if (str.Length != 6) throw new Exception(hex);
            var r = byte.Parse(str.AsSpan(0, 2), NumberStyles.HexNumber);
            var g = byte.Parse(str.AsSpan(2, 2), NumberStyles.HexNumber);
            var b = byte.Parse(str.AsSpan(4, 2), NumberStyles.HexNumber);

            return $"{(double)r / 255} {(double)g / 255} {(double)b / 255} {alpha / 100f}";
        }
        #endregion

        #region CuiHelper
        private static class MeventCuiHelper
        {
            public static string CreateButton(CuiElementContainer container, string parent, string msg = null, string command = null,
                string close = null,
                string name = null,
                string destroyUi = null,
                string color = "1 1 1 1",
                string sprite = null,
                string material = null,
                string iconPath = null,
                string iconColor = "1 1 1 1",
                CuiRectTransformComponent iconRect = null,
                string anchorMin = "0 0", string anchorMax = "1 1", string offsetMin = "0 0", string offsetMax = "0 0",
                string textColor = "1 1 1 1",
                int fontSize = 14,
                CuiRectTransformComponent textRect = null,
                TextAnchor textAnchor = TextAnchor.MiddleCenter,
                bool isBold = false)
            {
                if (string.IsNullOrEmpty(name))
                    name = CuiHelper.GetGuid();

                var btnComponent = new CuiButtonComponent
                {
                    Command = command ?? string.Empty,
                    Color = color ?? HexToCuiColor("#FFFFFF", 100)
                };

                if (!string.IsNullOrEmpty(close))
                    btnComponent.Close = close;

                if (!string.IsNullOrEmpty(sprite))
                    btnComponent.Sprite = sprite;

                if (!string.IsNullOrEmpty(material))
                    btnComponent.Material = material;

                var btn = new CuiElement()
                {
                    Name = name,
                    Parent = parent,
                    Components =
                    {
                        btnComponent,
                        new CuiRectTransformComponent()
                        {
                            AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax
                        }
                    }
                };

                if (!string.IsNullOrEmpty(destroyUi))
                    btn.DestroyUi = destroyUi;

                container.Add(btn);

                if (!string.IsNullOrEmpty(iconPath) && !string.IsNullOrEmpty(msg))
                {
                    ICuiComponent image = iconPath.StartsWith("http") ?
                        new CuiRawImageComponent() { Color = iconColor, Png = instance.GetImage(iconPath) } :
                        new CuiImageComponent { Color = iconColor, Sprite = iconPath };

                    container.Add(new CuiElement
                    {
                        Parent = name,
                        Components =
                        {
                            image,
                            iconRect ?? new CuiRectTransformComponent {AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-35 -7", OffsetMax = "-21 7"}
                        }
                    });

                    container.Add(new CuiElement
                    {
                        Parent = name,
                        Components =
                        {
                            new CuiTextComponent
                            {
                                Text = msg,
                                Font = "robotocondensed-" + (!isBold ? "regular" : "bold") + ".ttf",
                                FontSize = fontSize,
                                Align = textAnchor,
                                Color = textColor ?? "1 1 1 1"
                            },
                            textRect ?? new CuiRectTransformComponent {AnchorMin = "0.5 0", AnchorMax = "0.5 1", OffsetMin =  "-14 0", OffsetMax = "45 0"}
                        }
                    });
                }
                else if (!string.IsNullOrEmpty(msg))
                {
                    container.Add(new CuiElement
                    {
                        Parent = name,
                        Components =
                        {
                            new CuiTextComponent
                            {
                                Text = msg,
                                Font = "robotocondensed-" + (!isBold ? "regular" : "bold") + ".ttf",
                                FontSize = fontSize,
                                Align = textAnchor,
                                Color = textColor ?? "1 1 1 1"
                            },
                            new CuiRectTransformComponent()
                        }
                    });
                }
                else if (!string.IsNullOrEmpty(iconPath))
                {
                    ICuiComponent image = iconPath.StartsWith("http") ?
                        new CuiRawImageComponent() { Color = iconColor, Url = iconPath } :
                        new CuiImageComponent { Color = iconColor, Sprite = iconPath };

                    container.Add(new CuiElement
                    {
                        Parent = name,
                        Components =
                        {
                            image,
                            iconRect ?? new CuiRectTransformComponent()
                        }
                    });
                }

                return name;
            }
        }
        #endregion
        #endregion
    }
}
