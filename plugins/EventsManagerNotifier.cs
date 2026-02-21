using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;
using System.Linq;
using System.Collections.Generic;
using Oxide.Core;
using UnityEngine;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("EventsManagerNotifier", "M&B-Studios", "1.0.4")]
    public class EventsManagerNotifier : RustPlugin
    {
        [PluginReference] Plugin GUIAnnouncements;

        private ConfigData configData;

        class EventConfig
        {
            public bool MessagesActive { get; set; }
            public string EventName { get; set; }
            public string StartCommand { get; set; }
            public string StartMessage { get; set; }
        }

        enum NotifierType
        {
            Chat,
            Announcements
        }

        class ConfigData
        {
            public List<EventConfig> Events { get; set; } = new List<EventConfig>();

            [JsonProperty("Type 0 - chat, 1 - GUIAnnouncements")]
            public NotifierType type;
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new configuration file");
            configData = new ConfigData
            {
                Events = new List<EventConfig>
                {
                    new EventConfig
                    {
                        MessagesActive = true,
                        EventName = "Airdrop",
                        StartCommand = "em_spawn assets/prefabs/misc/supply drop/supply_drop.prefab",
                        StartMessage = "Airdrop was <color=green>dropped</color>"
                    },
                    new EventConfig
                    { 
                        MessagesActive = true,
                        EventName = "Chinook 47",
                        StartCommand = "em_spawn assets/prefabs/npc/ch47/ch47scientists.entity.prefab",
                        StartMessage = "Chinook 47 has <color=green>started</color> and is making its rounds"
                    },
                    new EventConfig
                    {
                        MessagesActive = true,
                        EventName = "Cargoship",
                        StartCommand = "em_spawn assets/content/vehicles/boats/cargoship/cargoshiptest.prefab",
                        StartMessage = "CargoShip sets sail and has <color=green>started</color> the journey"
                    },
                    new EventConfig
                    {
                        MessagesActive = true,
                        EventName = "Helicopter",
                        StartCommand = "em_spawn assets/prefabs/npc/patrol helicopter/patrolhelicopter.prefab",
                        StartMessage = "Helicopter <color=green>started</color> the sightseeing flight"
                    }
                }
            };
            SaveConfig();
        }

        private void OnEventStarted(string eventname)
        {
            var @event = configData.Events.FirstOrDefault(x => x.EventName == eventname);
            if (@event != null)
                Server.Broadcast(@event.StartMessage);
        }
        
        void Init()
        {
            configData = Config.ReadObject<ConfigData>();
            if (configData == null || configData.Events == null || configData.Events.Count == 0)
            {
                LoadDefaultConfig();
            }

            if(Version == new VersionNumber(1, 0, 4))
            {
                SaveConfig();
            }

            foreach (var eventConfig in configData.Events)
            {
                cmd.AddChatCommand(eventConfig.StartCommand, this, nameof(CmdEventStart));
                cmd.AddConsoleCommand(eventConfig.StartCommand, this, nameof(cmdEventStartConsole));   
                // AddCovalenceCommand(eventConfig.StartCommand, "CmdEventStart");
            }
        }

        private void cmdEventStartConsole(ConsoleSystem.Arg arg) => CmdEventStart(arg.Player(), "", arg.Args);

        private void CmdEventStart(BasePlayer player, string command, string[] args)
        {
            var eventConfig = configData.Events.Find(e => e.StartCommand == command);
            if (eventConfig != null && eventConfig.MessagesActive)
            {
                switch(configData.type)
                {
                    case NotifierType.Chat:
                        {
                            Server.Broadcast(eventConfig.StartMessage);
                            break;
                        }

                    case NotifierType.Announcements:
                        {
                            if (GUIAnnouncements == null || !GUIAnnouncements.IsLoaded)
                            {
                                PrintError("GUIAnnouncements IS NOT INSTALLED!");
                                return;
                            }

                            GUIAnnouncements?.Call("CreateAnnouncement", eventConfig.StartMessage, "Grey", "White", null, 0.03f);
                            break;
                        }
                }

                
            }
        }

        void SaveConfig() => Config.WriteObject(configData);
    }
}
