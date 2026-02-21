/*
*  <----- End-User License Agreement ----->
*  Copyright © 2024-26 Iftebinjan
*  Devoloper: Iftebinjan (Contact: https://discord.gg/HFaGs8YwsH)
*  
*  You may not copy, modify, merge, publish, distribute, sublicense, or sell copies of This Software without the Developer’s consent
*  
*  THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO,
*  THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS
*  BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE
*  GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT
*  LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*  
*  <----- End-User License Agreement ----->
*/
using ConVar;
using Facepunch;
using Facepunch.Math;
using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Oxide.Game.Rust.Libraries;
using ProtoBuf;
using Rust;
using Rust.Ai.Gen2;
using Steamworks.ServerList;
using System;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;
//
namespace Oxide.Plugins
{
    [Info("SimplePVE", "Ifte", "1.2.14")]
    [Description("An easy simple PVE plugin for your server to modify or change PVE rules.")]
    public class SimplePVE : RustPlugin
    {
        /*------------------------------------
         *
         *     SimplePVE by Ifte
         *     Support: https://discord.gg/cCWadjcapr
         *     Fiverr: https://www.fiverr.com/s/e2pkYY
         *     Eror404 not found
         *
         ------------------------------------*/

        /*------------------------------------
        * 
        *   UPDATES
        * --------------
        * 1.2.0 - Latest with NewUI, Added new schedule system with weekdays support. Added config for Commands to execute when pvp purge starts and ends(Only from schedules)
        * 1.2.1 - Fixed ZombieHorde plugin, Fixed Tugboat vehicle damage, Added support for Backpack(Backpack no longer be pickup or open by other players)
        * 1.2.2 - Added new configuration, all rules configurations moved to Oxide/Data/SimplePVE/, Config values changed, Fixed DropBox & Mail not opening(More entitys option in the config)
        * 1.2.3 - Updated for latest force wipe, Added New Status UI for PVE status Zone Status Schedule Stats, New Configuration added, Fixed a bug where backpack cant be pickup after death by entity, Fixed few Zone Bugs, More bug added.
        * 1.2.4 - Updated for New Forced Update, Added support for FClan, Added support for codelock raid, Fixed Shark not damageing players
        * 1.2.5 - Fixed Status UI, Fixed APIs Not Calling, Working APIs(IsServerPVE, GetPVPStartTimeRemaining, GetPVPEndTimeRemaining, AddToExcludeZone, RemoveFromExcludeZone, IsStatusEnabled_API), 
        * 1.2.6 - Update for recent force wipe, Fixed for new wolf ai and wolf damage
        * 1.2.7 - Updated config file(Config will auto update from the next update), Changes to StatusUI(Now it requires SimpleStatus plugin as dependency), Code cleanup for maxium performance, Fixed Torpedo damaging Tugboat, Added Support for Toastify & Notify
        * 1.2.8 - Updated config, Added TIP configuration, Added new rule (Allow wallpaper damage, Allow beeswarm damage), Added new command /spdebug (use it wisely and disable this when debug is done), Added support for owned vehicles damage
        * 1.2.9 - Quick fix for the Flame Turret which got broken in last update, Added a new rule (Traps can damage Player) default is false, Fix Fridge lootable by other players, Added new rule (Player SamSite Hurt Players, Npc SamSite Hurt Players), Fix for Chicken coop chiken damage (Only auth to tc player can damage to Farmable Animals), Fix chiken was damageable
        * 1.2.10 - Fixed for the Rust Force wipe
        * 1.2.11 - Fixed "Anyone can open Player called SupplyDrops"
        * 1.2.11 - Fixed Spikes trap wasn't effected by "Traps can damage Player" rule, Fixed player bleeding not working in PVE mode, Fixed Tugboat auth damage, Added New Config Option: Auto Claim Vehicle on Engine Start, Added New Rules -Player can damage own/team vehciles -Player can damage other owned vehicles (Requires Auto Claim Vehicle to be true), New Command /resetsprules (Player & Console command to reset Rules to default)
        * 1.2.12 - Updated for Novemeber Force wipe
        * 1.2.13 - Updated for latest force wipe, Added new rule "Player Can Damage Player in Deep Sea", Added new rule "Player Can Damage Other Player Boats in Deep Sea", Added Deep Sea Bounds to config, Added New config "Auto Claim Vehicle Show Message", Fixed a bug for Player Own Damage
        * 1.2.14 - Fixed a message when shooting the Deep Sea Rhib & PTBoat.deepsea
        * 
        *   BUGS
        * ---------
        * Updates coming in
        * 
        --------------------------------------*/

        #region Varriables

        [PluginReference]
        Plugin ZoneManager, ImageLibrary, Clans, Friends, RaidableBases, Convoy, HeliSignals, AbandonedBases, FClan, SimpleStatus, Toastify, Notify;

        private static SimplePVE ins;
        private Configuration config;
        private ScheduleSave savedSch;
        private bool update = true;
        private bool PVPRun = false;
        private bool changedConfig = false;
        private bool DiscordMessageSent = false;
        private Dictionary<ulong, PVPDelay> pvp_delays = new Dictionary<ulong, PVPDelay>();
        enum PVERules
        {
            GRules,
            PRules,
            NRules,
            ERules,
            LRules,
            ZRules
        }

        private class PVPDelay
        {
            public string ZoneID;
            public Timer DelayTimer;
        }
        //permissions
        private const string PvEAdminPerms = "simplepve.admin";
        private const string PvEAdminLootPerms = "simplepve.adminloot";
        private const string PvEAdminDamagePerms = "simplepve.admindamage";

        private Bounds DeepSeaBounds;

        #endregion

        #region Configs 

        private class Configuration
        {
            [JsonProperty(PropertyName = "PVE Enabled")]
            public bool pveEnabledConfig { get; set; }

            [JsonProperty(PropertyName = "Auto Claim Vehicle(Mini, ScrapHeli, Boats, Moudlars etcs) On Engine Start")]
            public bool vehicleAutoClaim { get; set; }

            [JsonProperty(PropertyName = "Auto Claim Vehicle Show Message")]
            public bool vehicleAutoClaimShowMessage { get; set; }

            [JsonProperty(PropertyName = "Notifications")]
            public DisplayNotify displayNotify { get; set; }

            [JsonProperty(PropertyName = "Loot Protection Excluded Entitys", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> excludedEntitys { get; set; }

            [JsonProperty(PropertyName = "Schedules Setting")]
            public Sche sche { get; set; }

            [JsonProperty(PropertyName = "PVP Delay Setting")]
            public PVPDelayNotify delaySetting { get; set; }


            [JsonProperty(PropertyName = "Discord Setting")]
            public DiscordSetting discordSetting { get; set; }

            
            [JsonProperty(PropertyName = "Commands when PVP Purge Starts")]
            public List<string> commandsExefrompvpstart { get; set; }

            [JsonProperty(PropertyName = "Commands when PVP Purge Ends")]
            public List<string> commandsExefrompvpends { get; set; }

            [JsonProperty(PropertyName = "Exclude Zone IDs From Rules")]
            public List<string> excludedZoneIDs { get; set; }


            [JsonProperty(PropertyName = "Status UI")]
            public StatusUI statusUI { get; set; }

            [JsonProperty(PropertyName = "Config Version (Don't Edit This)")]
            public VersionNumber Version { get; set; }

        }

        private class PVPDelayNotify
        {
            [JsonProperty("PVP delay time in seconds(If enabled from ZoneRules)")]
            public int pvpdelaytime { get; set; }

            [JsonProperty("Show PVP Delay Message")]
            public bool pvpDelayMsgEnabled { get; set; }

            [JsonProperty("PVP Delay Message")]
            public string pvpDelayMessage { get; set; }
        }

        private class DisplayNotify
        {
            [JsonProperty("Prefix")]
            public string prefix { get; set; }

            [JsonProperty("Chat Avatar")]
            public ulong chatAvatar { get; set; }

            [JsonProperty("Show PVE Icon Overlay")]
            public bool showPvEOverlay { get; set; }

            [JsonProperty("Show PVE Warning Messages")]
            public bool showDamageM { get; set; }

            [JsonProperty("Show PVE Warning Messages TIP")]
            public bool showDamageMTip { get; set; }

            [JsonProperty("PVE Warning Type(Chat/UI/Both(Chat & UI)/Toastify/Notify)")]
            public string showDamageMType { get; set; }

            [JsonProperty("PVE Warning Chat Message")]
            public string showDamageMTypeMessage { get; set; }

            [JsonProperty("PVE Warning Tip Message")]
            public string showDamageMTypeMessageTip { get; set; }

            [JsonProperty("PVE Warning Custom UI Message(Toastify/Notify)")]
            public string showMsgCustomUI { get; set; }

            [JsonProperty("PVE Warning UI Settings")]
            public WarningUI warningUI { get; set; }

            public class WarningUI
            {
                [JsonProperty("Anchor Min")]
                public string WarningAMin { get; set; }

                [JsonProperty("Anchor Max")]
                public string WarningAMax { get; set; }

                [JsonProperty("Warning Image URL")]
                public string WarningImgUrl { get; set; }
            }

            [JsonProperty("Show Loot Protection Messages")]
            public bool LPMessagesOn { get; set; }

            [JsonProperty("Show Loot Protection Messages TIP")]
            public bool LPMessagesOnTip { get; set; }

            [JsonProperty("Loot Protection Type(Chat/UI/Both(Chat & UI)/Toastify/Notify)")]
            public string LPType { get; set; }

            [JsonProperty("Loot Protection Chat Message")]
            public string LPChatMessage { get; set; }

            [JsonProperty("Loot Protection Tip Message")]
            public string LPChatMessageTIP { get; set; }

            [JsonProperty("Loot Protection Custom UI Message(Toastify/Notify)")]
            public string LPChatMessageUI { get; set; }

            [JsonProperty("Loot Protection UI Settings")]
            public LPUISetting lPUISetting { get; set; }

            public class LPUISetting
            {
                [JsonProperty("Anchor Min")]
                public string WarningAMin { get; set; }

                [JsonProperty("Anchor Max")]
                public string WarningAMax { get; set; }

                [JsonProperty("Warning Loot UI Image URL")]
                public string WarningImgUrl { get; set; }
            }


            [JsonProperty("PVE/PVP Icon UI Settings")]
            public PEPUISetting pUISetting { get; set; }

            public class PEPUISetting
            {
                [JsonProperty("Anchor Min")]
                public string pepAMin { get; set; }

                [JsonProperty("Anchor Max")]
                public string pepAMax { get; set; }

                [JsonProperty("Offset Min")]
                public string pepOMin { get; set; }

                [JsonProperty("Offset Max")]
                public string pepOMax { get; set; }

            }

        }

        private class Sche
        {
            [JsonProperty("UTC Time Difference")]
            public int utcTimeDif { get; set; }

            [JsonProperty("Enable Weekly Schedule Only")]
            public bool enableweeklysch { get; set; }

            [JsonProperty("PVP On Notify Message")]
            public string pvpOnMessage { get; set; }

            [JsonProperty("PVE On Notify Message")]
            public string pveOnMessage { get; set; }

            [JsonProperty("PVP Purge Start Before Message")]
            public string pvppurgeStartMsg { get; set; }

            [JsonProperty("PVP Purge Ending Before Message")]
            public string pvppurgeEndingMsg { get; set; }

        }

        private class DiscordSetting
        {
            [JsonProperty("Enable Discord Notification")]
            public bool enableDiscordNotify { get; set; }
            [JsonProperty("Discord Webhook URL")]
            public string discordWebhookURL { get; set; }
            [JsonProperty("Enable Message Before PVP Time Start")]
            public bool messageBeforeStart { get; set; }
            [JsonProperty("Before PVP Time Start Minutes")]
            public int messageBeforeStartMinutes { get; set; }
            [JsonProperty("PVP Message Embed Color(Hexa)")]
            public string pvpmessageEmbed { get; set; }
            [JsonProperty("Before PVP Time Start Message Content")]
            public string[] pvpTimeMessage { get; set; }

            [JsonProperty("Enable Message Before PVE Time Start")]
            public bool messageBeforeStartPVE { get; set; }
            [JsonProperty("Before PVE Time Start Minutes")]
            public int messageBeforeStartMinutesPVE { get; set; }
            [JsonProperty("PVE Message Embed Color")]
            public string pvemessageEmbed { get; set; }
            [JsonProperty("Before PVE Time Start Message Content")]
            public string[] pveTimeMessage { get; set; }

        }
        private class StatusUI
        {
            [JsonProperty(PropertyName = "Enable Status")]
            public bool status { get; set; }

            [JsonProperty(PropertyName = "Interval Time In Seconds")]
            public int interval { get; set; }

            [JsonProperty(PropertyName = "PVE/PVP Status")]
            public PVPorPVEStatus peStatus { get; set; }

            /*[JsonProperty(PropertyName = "Zone Status")]
            public ZoneStatus zoneStatus { get; set; }*/

            [JsonProperty(PropertyName = "Purge Schedule Status")]
            public PurgeStatus purgeStatus { get; set; }
        }

        private class PVPorPVEStatus
        {
            [JsonProperty(PropertyName = "Status Enable")]
            public bool status { get; set; }

            [JsonProperty(PropertyName = "PVE Status")]
            public Setting PVEStatus { get; set; }

            [JsonProperty(PropertyName = "PVP Status")]
            public Setting PVPStatus { get; set; }
        }

        private class ZoneStatus
        {
            [JsonProperty(PropertyName = "Status Enable")]
            public bool status { get; set; }

            [JsonProperty(PropertyName = "In PVP Area Status")]
            public Setting pvparea { get; set; }
        }

        private class PurgeStatus
        {
            [JsonProperty(PropertyName = "Status Enable")]
            public bool status { get; set; }

            [JsonProperty(PropertyName = "Purge PVP Status")]
            public Setting purgepvpStatus { get; set; }

            [JsonProperty(PropertyName = "Purge PVE Status")]
            public Setting purgepveStatus { get; set; }
        }

        private class Setting
        {
            [JsonProperty(PropertyName = "Enable Status")]
            public bool status { get; set; }

            [JsonProperty(PropertyName = "Text List")]
            public string[] strings { get; set; }

            [JsonProperty(PropertyName = "Text Color")]
            public string textColor { get; set; }

            [JsonProperty(PropertyName = "Icon Image Url")]
            public string icon { get; set; }

            [JsonProperty(PropertyName = "Icon Image Color")]
            public string iconColor { get; set; }

            [JsonProperty(PropertyName = "Status Color")]
            public string statusColor { get; set; }
        }

        private Configuration CreateConfig()
        {
            return new Configuration
            {
                pveEnabledConfig = true,
                vehicleAutoClaim = true,
                vehicleAutoClaimShowMessage = false,
                displayNotify = new DisplayNotify
                {
                    showDamageM = true,
                    prefix = "<color=#00ffff>[SimplePVE]</color>: ",
                    chatAvatar = 0,
                    showDamageMType = "Both(Chat & UI)",
                    showDamageMTip = true,
                    showDamageMTypeMessage = "PVE enabled on this server, blocking damage.",
                    showDamageMTypeMessageTip = "PVE enabled on this server, blocking damage.",
                    showMsgCustomUI = "PVE enabled on this server, blocking damage.",
                    warningUI = new DisplayNotify.WarningUI
                    {
                        WarningAMin = "0.786 0.722",
                        WarningAMax = "0.99 0.815",
                        WarningImgUrl = "https://i.postimg.cc/0jZNDr9x/Add-a-subheading-2.png"
                    },
                    showPvEOverlay = true,
                    LPMessagesOn = true,
                    LPType = "Both(Chat & UI)",
                    LPMessagesOnTip = true,
                    LPChatMessage = "This entity is protected!",
                    LPChatMessageTIP = "This entity is protected!",
                    LPChatMessageUI = "This entity is protected!",
                    lPUISetting = new DisplayNotify.LPUISetting
                    {
                        WarningAMin = "0.786 0.722",
                        WarningAMax = "0.99 0.815",
                        WarningImgUrl = "https://i.postimg.cc/SxSsS67s/Add-a-subheading-1.png"
                    },
                    pUISetting = new DisplayNotify.PEPUISetting
                    {
                        pepAMin = "0.5 0",
                        pepAMax = "0.5 0",
                        pepOMin = "190 30",
                        pepOMax = "250 60"
                    }
                },
                statusUI = new StatusUI()
                {
                    status = true,
                    interval = 10,
                    peStatus = new PVPorPVEStatus()
                    {
                        status = true,
                        PVEStatus = new Setting()
                        {
                            status = true,
                            strings = new string[]
                            {
                                    "PVE ENABLED",
                                    "RAID IS DISABLED",
                                    "PVP IS DISABLED",
                            },
                            textColor = "#FFFFFF",
                            icon = "https://i.postimg.cc/4ymwFnSC/quality.png",
                            iconColor = "#A4FE0A",
                            statusColor = "#718D48"
                        },
                        PVPStatus = new Setting()
                        {
                            status = true,
                            strings = new string[]
                            {
                                    "PVP ENABLED",
                                    "RAID IS ENABLED",
                                    "PVE IS DISABLED",
                                    "PVP IS ENABLED",
                            },
                            textColor = "#FFFFFF",
                            icon = "https://i.postimg.cc/50YVChxW/human-skull-with-crossed-bones-silhouette.png",
                            iconColor = "#FFFFFF",
                            statusColor = "#B22222"
                        }
                    },
                    /*zoneStatus = new ZoneStatus()
                    {
                        status = true,
                        pvparea = new Setting()
                        {
                            status = true,
                            strings = new string[]
                            {
                                    "YOU'RE IN THE PVP ZONE"
                            },
                            textColor = "#FFFFFF",
                            icon = "https://i.postimg.cc/qRGxrMBR/fighting-game.png",
                            iconColor = "#FFFFFF",
                            statusColor = "#A30000"
                        }
                    },*/
                    purgeStatus = new PurgeStatus()
                    {
                        status = true,
                        purgepvpStatus = new Setting()
                        {
                            status = true,
                            strings = new string[]
                            {
                                    "PVP ENABLEING IN {TIMER}",
                                    "RAID ENABLEING IN {TIMER}",
                                    "PVP PURGING IN {TIMER}"
                            },
                            textColor = "#FFFFFF",
                            icon = "https://i.postimg.cc/0j2n3rzp/time.png",
                            iconColor = "#FFFFFF",
                            statusColor = "#A30000"
                        },
                        purgepveStatus = new Setting()
                        {
                            status = true,
                            strings = new string[]
                            {
                                    "PVE ENABLING IN {TIMER}",
                                    "RAID DISABLEING IN {TIMER}",
                                    "PVE PURGING IN {TIMER}"
                            },
                            textColor = "#FFFFFF",
                            icon = "https://i.postimg.cc/rydRF7cc/time.png",
                            iconColor = "#FFFFFF",
                            statusColor = "#718D48"
                        }
                    }
                },
                excludedEntitys = new List<string>()
                    {
                        "mailbox.deployed",
                        "dropbox.deployed"
                    },
                sche = new Sche
                {
                    utcTimeDif = 360,
                    enableweeklysch = true,
                    pvpOnMessage = "<#990000>PVP ENABLED: You can now raid other bases and engage in combat!</color>",
                    pveOnMessage = "<#008000>PVE ENABLED: Raiding and fighting are now prohibited. Enjoy peace and safety!</color>",
                    pvppurgeStartMsg = "PVP TIME APPROACHING: Only {TIMELEFT} minutes remaining until PVP begins!",
                    pvppurgeEndingMsg = "PVP TIME ENDING: Only {TIMELEFT} minutes remaining until PVP ends!",

                },
                delaySetting = new PVPDelayNotify
                {
                    pvpdelaytime = 10,
                    pvpDelayMsgEnabled = true,
                    pvpDelayMessage = "You will be removed from PVP state in <color=#DC143C>{delay}</color> seconds.",
                },
                discordSetting = new DiscordSetting
                {
                    enableDiscordNotify = false,
                    discordWebhookURL = string.Empty,
                    messageBeforeStart = false,
                    messageBeforeStartMinutes = 30,
                    pvpmessageEmbed = "#990000",
                    pvpTimeMessage = new string[]
                    {
                            "🔔 **Attention Rust Survivors!**",
                            "",
                            "PVP purge approaching!",
                            "In {Minutes} Minutes🕒",
                            "",
                            "Prepare yourselves for intense battles! ⚔️",
                    },
                    messageBeforeStartPVE = false,
                    messageBeforeStartMinutesPVE = 30,
                    pvemessageEmbed = "#32CD32",
                    pveTimeMessage = new string[]
                    {
                            "🔔 **Attention Rust Survivors!**",
                            "",
                            "PVP purge ending soon!",
                            "In {Minutes} Minutes🕒",
                            "",
                            "Make your final moves wisely!",
                            "Collect your victories and gear up for the next day!"
                    }
                },
                commandsExefrompvpstart = new List<string> { },
                commandsExefrompvpends = new List<string> { },
                excludedZoneIDs = new List<string>
                    {
                        "999",
                        "6969"
                    },
                Version = Version,

            };
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            config = Config.ReadObject<Configuration>();

            if (config.Version < Version)
                UpdateConfigValues();

            Config.WriteObject(config, true);
        }

        protected override void LoadDefaultConfig()
        {
            config = CreateConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        private void UpdateConfigValues()
        {
            if (config.Version < new VersionNumber(1, 2, 8))
            {
                PrintWarning("Config version outdated. Backing up old config for safe...");
                BackupOldConfig();

                Configuration baseConfig = CreateConfig();
                config.displayNotify.showDamageMTip = baseConfig.displayNotify.showDamageMTip;
                config.displayNotify.showDamageMTypeMessageTip = baseConfig.displayNotify.showDamageMTypeMessageTip;

                config.displayNotify.LPMessagesOnTip = baseConfig.displayNotify.LPMessagesOnTip;
                config.displayNotify.LPChatMessageTIP = baseConfig.displayNotify.LPChatMessageTIP;
                PrintWarning("Config updated to version 1.2.8");
            }
            if (config.Version < new VersionNumber(1, 2, 11))
            {
                PrintWarning("Config version outdated. Backing up old config for safe...");
                BackupOldConfig();

                Configuration baseConfig = CreateConfig();
                config.vehicleAutoClaim = baseConfig.vehicleAutoClaim;

                PrintWarning("Config updated to version 1.2.11");
            }
            if (config.Version < new VersionNumber(1, 2, 13))
            {
                PrintWarning("Config version outdated. Backing up old config for safe...");
                BackupOldConfig();

                Configuration baseConfig = CreateConfig();
                config.vehicleAutoClaimShowMessage = baseConfig.vehicleAutoClaimShowMessage;

                PrintWarning("Config updated to version 1.2.13");
            }
            config.Version = Version;
        }

        private void BackupOldConfig()
        {
            string backupFileName = $"{Name}_config_backup_ver{config.Version.ToString()}_{Time:yyyy-MM-dd_HH-mm-ss}.json";
            string backupFilePath = Path.Combine(Interface.Oxide.ConfigDirectory, backupFileName);
            Config.WriteObject(config, false, backupFilePath);
        }

        #endregion

        #region Localization

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["TEST"] = "Attention! A test message",

            }, this, "en");
        }

        #endregion

        #region Data files

        private RulesData rulesData;

        private void LoadRulesData()
        {
            rulesData = Interface.Oxide.DataFileSystem.ReadObject<RulesData>($"{Name}/RulesData");

            if (rulesData == null || rulesData.IsRulesDataEmpty(rulesData))
            {
                rulesData = RulesData.CreateDefaultRules();
                SaveRulesData();
            }
        }

        private void SaveRulesData()
        {
            Interface.Oxide.DataFileSystem.WriteObject($"{Name}/RulesData", rulesData);
        }

        private class RulesData
        {

            [JsonProperty(PropertyName = "General Rules")]
            public GRules gRules { get; set; }

            public class GRules
            {
                [JsonProperty("Allow Friendly Fire")]
                public bool aFriendlyFire { get; set; }

                [JsonProperty("Allow Suicide")]
                public bool allowSuicide { get; set; }

                [JsonProperty("Kill Sleepers")]
                public bool killSleepers { get; set; }

                [JsonProperty("Player Radiation Damage")]
                public bool pRadiation { get; set; }

                [JsonProperty("Allow Codelock Raid")]
                public bool aCodelockRaid { get; set; }

                [JsonProperty("Allow Wallpaper Damage")]
                public bool aWallpaperDamage { get; set; }

                [JsonProperty("Allow BeeSwarm Damage")]
                public bool aBeeSwarmDamage { get; set; }
            }

            [JsonProperty(PropertyName = "Player Rules")]
            public PlayerRules playerRules { get; set; }

            public class PlayerRules
            {
                [JsonProperty("Player Can Hurt Players")]
                public bool pCanHurtp { get; set; }

                [JsonProperty("Player Can Hurt Animals")]
                public bool pCanHurta { get; set; }

                [JsonProperty("Player Can Hurt NPC")]
                public bool pCanHurtn { get; set; }

                [JsonProperty("Player Can Damage Vehicles(Mini, Vehicles etcs)")]
                public bool pCanDamEntitys { get; set; }

                [JsonProperty("Player Can Damage Own/Team Vehicles(Mini, Vehicles etcs)")]
                public bool PlayerCanDamagaOwnTeamVehicles { get; set; }

                [JsonProperty("Player Can Damage Others Vehicles(Mini, Vehicles etcs)")]
                public bool PlayerCanDamagaOtherVehicles { get; set; }

                [JsonProperty("Player Can Damage Other Player Buildings")]
                public bool pCanDamOtherpBuildings { get; set; }

                [JsonProperty("Player Can Damage Own Buildings")]
                public bool pCanDamageOwnBuildings { get; set; }

                [JsonProperty("Player Can Damage Own Teammates Building")]
                public bool pCanDamOwnTeamamtesBuilding { get; set; }

                [JsonProperty("Player Can Damage Other Player Twigs")]
                public bool PallowTwigDamage { get; set; }

                [JsonProperty("Player Can Damage Own Player Boats")]
                public bool PlayerCanDamageOwnBoats { get; set; }

                [JsonProperty("Player Can Damage Other Player Boats")]
                public bool PlayerCanDamageOtherBoats { get; set; }

                [JsonProperty("Player Can Damage Player in Deep Sea")]
                public bool PlayerCanDamagePlayerInDeepSea { get; set; }

                [JsonProperty("Player Can Damage Other Player Boats in Deep Sea")]
                public bool PlayerCanDamageOtherBoatsInDeepSea { get; set; }

            }

            [JsonProperty(PropertyName = "Npc Rules")]
            public NpcRules npcRules { get; set; }
            public class NpcRules
            {
                [JsonProperty("Npc Can Hurt Players")]
                public bool NpcHurtP { get; set; }

                [JsonProperty("Animal Can Hurt Players")]
                public bool AnimalCanHurtP { get; set; }

                [JsonProperty("Patrol Heli Can Hurt Player")]
                public bool PatrolHurtP { get; set; }

                [JsonProperty("Patrol Heli Can Hurt Buildings")]
                public bool PatrolHurtB { get; set; }

                [JsonProperty("Player SamSite Ignore Players")]
                public bool PSamigP { get; set; }

                [JsonProperty("Player SamSite Hurt Players")]
                public bool PSamigPHurt { get; set; }

                [JsonProperty("Npc SamSite Ignore Players")]
                public bool NSamigP { get; set; }

                [JsonProperty("Npc SamSite Hurt Players")]
                public bool NSamigPHurts { get; set; }

                [JsonProperty("Bradley Can Hurt Players")]
                public bool bradleyHurtP { get; set; }

            }

            [JsonProperty(PropertyName = "Entity Rules")]
            public EntityRules entityRules { get; set; }

            public class EntityRules
            {

                [JsonProperty("Walls Entitys Can Hurt Players(Example: High External Wall)")]
                public bool WallHurtP { get; set; }

                [JsonProperty("Barricade Entitys Can Hurt Players(Example: Wooden Barricade)")]
                public bool BarrHurtP { get; set; }

                [JsonProperty("MLRS Rocket Can Damage Players or Buildings")]
                public bool MlrsHurtPB { get; set; }

                [JsonProperty("Vehicle Entitys Can Hurt Players(Example: Cars, Mini, ScrapHeli)")]
                public bool VehicleHurtP { get; set; }

                [JsonProperty("Disable Traps (BearTrap, LandMine, GunTrap, FlameTurret, AutoTurret)")]
                public bool trapD { get; set; }

                [JsonProperty("Traps can damage Player")]
                public bool trapToPlayer { get; set; }

                [JsonProperty("Enable Vehicle Collsion Damage")]
                public bool enableVehicleCollsionD { get; set; }

            }

            [JsonProperty(PropertyName = "Loot Rules")]
            public LootRules lootRules { get; set; }

            public class LootRules
            {
                [JsonProperty("Use Loot Protection")]
                public bool useLP { get; set; }

                [JsonProperty("Admin Can Loot All")]
                public bool adminCanLootAll { get; set; }

                [JsonProperty("Teams Can Access Loot")]
                public bool teamsAccessLoot { get; set; }

                [JsonProperty("Player can loot other player in PVP Zones")]
                public bool pclpvpzones { get; set; }

                [JsonProperty("Anyone can open Player called SupplyDrops")]
                public bool anyoneOpenSupplyDrop { get; set; }

            }

            [JsonProperty(PropertyName = "Zone Rules")]
            public ZoneRules zoneRules { get; set; }

            public class ZoneRules
            {
                [JsonProperty("Use ZoneManager")]
                public bool zoneManager { get; set; }

                [JsonProperty("Disable PVE Rules In Zones")]
                public bool disableRulesZone { get; set; }

                [JsonProperty("Enable PVP Delay from ZoneManager")]
                public bool enablePVPDelay { get; set; }
            }

            public static RulesData CreateDefaultRules()
            {
                return new RulesData
                {
                    zoneRules = new ZoneRules
                    {
                        zoneManager = true,
                        disableRulesZone = true,
                        enablePVPDelay = true,
                    },
                    gRules = new GRules
                    {
                        aFriendlyFire = true,
                        allowSuicide = true,
                        killSleepers = false,
                        pRadiation = true,
                        aCodelockRaid = false,
                        aWallpaperDamage = false
                    },
                    playerRules = new PlayerRules
                    {
                        pCanHurtp = false,
                        pCanHurta = true,
                        pCanHurtn = true,
                        pCanDamEntitys = true,
                        PlayerCanDamagaOwnTeamVehicles = true,
                        PlayerCanDamagaOtherVehicles = false,
                        pCanDamOtherpBuildings = false,
                        pCanDamageOwnBuildings = true,
                        pCanDamOwnTeamamtesBuilding = true,
                        PallowTwigDamage = true,
                        PlayerCanDamageOwnBoats = true,
                        PlayerCanDamageOtherBoats = false,
                        PlayerCanDamagePlayerInDeepSea = false,
                        PlayerCanDamageOtherBoatsInDeepSea = false
                    },
                    npcRules = new NpcRules
                    {
                        NpcHurtP = true,
                        AnimalCanHurtP = true,
                        PatrolHurtP = true,
                        PatrolHurtB = true,
                        PSamigP = true,
                        PSamigPHurt = true,
                        NSamigP = false,
                        NSamigPHurts = true,
                        bradleyHurtP = true

                    },
                    entityRules = new EntityRules
                    {
                        WallHurtP = true,
                        BarrHurtP = true,
                        MlrsHurtPB = false,
                        VehicleHurtP = false,
                        trapD = true,
                        trapToPlayer = false,
                        enableVehicleCollsionD = true,
                    },
                    lootRules = new LootRules
                    {
                        useLP = true,
                        adminCanLootAll = true,
                        teamsAccessLoot = true,
                        pclpvpzones = false,
                        anyoneOpenSupplyDrop = false
                    },
                };
            }

            public bool IsRulesDataEmpty(RulesData data)
            {
                return data.gRules == null &&
                       data.playerRules == null &&
                       data.npcRules == null &&
                       data.entityRules == null &&
                       data.lootRules == null &&
                       data.zoneRules == null;
            }

        }


        private void ResetRulesDataToDefault()
        {
            rulesData = RulesData.CreateDefaultRules();
            SaveRulesData();
        }

        #endregion

        #region Commands Pack

        [Command("simplepve")]
        private void CommandEnable(IPlayer p, string cmd, string[] args)
        {
            if (!p.HasPermission(PvEAdminPerms))
            {
                p.Message("You don't have permission to use this command.");
                return;
            }

            if (args.Length == 0)
            {
                p.Message("Usage: simplepve <on|off>");
                return;
            }

            bool enablePVE = false;

            switch (args[0].ToLower())
            {
                case "on":
                    enablePVE = true;
                    break;
                case "off":
                    enablePVE = false;
                    break;
                default:
                    p.Message("Invalid argument. Usage: simplepve <on|off>");
                    return;
            }

            if (config.pveEnabledConfig == enablePVE)
            {
                p.Message($"SimplePVE is already {(enablePVE ? "enabled" : "disabled")}.");
                return;
            }

            config.pveEnabledConfig = enablePVE;
            SaveConfig();

            string message = config.pveEnabledConfig ? "SimplePVE enabled" : "SimplePVE disabled";
            p.Message(message);

            if (config.pveEnabledConfig)
            {
                Subscribe();
            }
            else
            {
                NotSubscribe();
            }

            foreach (var player in BasePlayer.activePlayerList)
            {
                RemovePVPPanel(player);
                RemovePVEPanel(player);

                if (config.pveEnabledConfig)
                {
                    ShowPVEUI(player);
                }
                else
                {
                    ShowPVPUi(player);
                }
            }
        }

        [Command("resetsprules")]
        private void CommandResetRules(IPlayer player, string cmd, string[] args)
        {
            if (!player.HasPermission(PvEAdminPerms))
            {
                player.Message("You don't have permission to use this command.");
                return;
            }
            ResetRulesDataToDefault();
            player.Message("SimplePVE rules have been reset to default.");
        }

        [Command("rsp")]
        private void ReloadPlugin(IPlayer player, string cmd, string[] args)
        {
            if (!player.HasPermission(PvEAdminPerms))
            {
                player.Message("You don't have permission to use this command.");
                return;
            }
            NextTick(() =>
            {
                Interface.Oxide.ReloadPlugin(Name);
            });

            player.Message("SimplePVE reloaded.");
        }

        [ChatCommand("sprules")]
        private void SimplePVEGui(BasePlayer player, string cmd, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PvEAdminPerms))
            {
                SendChatMessage(player, "You don't have permission to use this command.");
                return;
            }
            SimplePVEMainGui(player);
            PVERulesUI(player, PVERules.GRules);
        }

        [ConsoleCommand("spvecui")]
        private void SPVECUICMD(ConsoleSystem.Arg args)
        {
            var player = args?.Player();

            string command = args.Args[0];

            switch (command)
            {
                case "pverules":
                    PVERulesUI(player, PVERules.GRules);
                    break;
                case "pveschedules":
                    var time = DateTime.Now;
                    PVESchedules(player);
                    break;
                case "grules":
                    PVERulesUI(player, PVERules.GRules);
                    break;
                case "prules":
                    PVERulesUI(player, PVERules.PRules);
                    break;
                case "nrules":
                    PVERulesUI(player, PVERules.NRules);
                    break;
                case "erules":
                    PVERulesUI(player, PVERules.ERules);
                    break;
                case "lrules":
                    PVERulesUI(player, PVERules.LRules);
                    break;
                case "zrules":
                    PVERulesUI(player, PVERules.ZRules);
                    break;
                case "closeall":
                    CuiHelper.DestroyUi(player, "SimplePVECUI");
                    if (changedConfig)
                    {
                        SendChatMessage(player, "Rules changed\nPlease allow a moment reloading SimplePVE\nUse debug if rules not working <color=orange>/spdebug</color>");
                        changedConfig = false;
                        NextTick(() =>
                        {
                            Interface.Oxide.ReloadPlugin(Name);
                        });
                    }
                    
                    break;

            }
        }

        [ConsoleCommand("grulesSetting")]
        private void GrulesCcmd(ConsoleSystem.Arg args)
        {
            var player = args?.Player();
            string propertyName = args.Args[0];
            SetProperty(propertyName);
            PVERulesUI(player, PVERules.GRules);

        }
        [ConsoleCommand("prulesSetting")]
        private void PrulesCcmd(ConsoleSystem.Arg args)
        {
            var player = args?.Player();
            string propertyName = args.Args[0];
            SetProperty(propertyName);
            PVERulesUI(player, PVERules.PRules);
        }
        [ConsoleCommand("nrulesSetting")]
        private void NrulesCcmd(ConsoleSystem.Arg args)
        {
            var player = args?.Player();
            string propertyName = args.Args[0];
            SetProperty(propertyName);
            PVERulesUI(player, PVERules.NRules);
        }
        [ConsoleCommand("erulesSetting")]
        private void ErulesCcmd(ConsoleSystem.Arg args)
        {
            var player = args?.Player();
            string propertyName = args.Args[0];
            SetProperty(propertyName);
            PVERulesUI(player, PVERules.ERules);
        }
        [ConsoleCommand("lrulesSetting")]
        private void LrulesCcmd(ConsoleSystem.Arg args)
        {
            var player = args?.Player();
            string propertyName = args.Args[0];
            SetProperty(propertyName);
            PVERulesUI(player, PVERules.LRules);
        }
        [ConsoleCommand("zrulesSetting")]
        private void ZrulesCcmd(ConsoleSystem.Arg args)
        {
            var player = args?.Player();
            string propertyName = args.Args[0];
            SetProperty(propertyName);
            PVERulesUI(player, PVERules.ZRules);
        }
        private void SetProperty(string propertyName)
        {
            if (config == null) return;
            PropertyInfo gRulesProperty = typeof(RulesData.GRules).GetProperty(propertyName);
            PropertyInfo pRulesProperty = typeof(RulesData.PlayerRules).GetProperty(propertyName);
            PropertyInfo nRulesProperty = typeof(RulesData.NpcRules).GetProperty(propertyName);
            PropertyInfo eRulesProperty = typeof(RulesData.EntityRules).GetProperty(propertyName);
            PropertyInfo lRulesProperty = typeof(RulesData.LootRules).GetProperty(propertyName);
            PropertyInfo zRulesProperty = typeof(RulesData.ZoneRules).GetProperty(propertyName);

            changedConfig = true;

            if (gRulesProperty != null)
            {
                bool currentValue = (bool)gRulesProperty.GetValue(rulesData.gRules);
                bool newValue = !currentValue;
                gRulesProperty.SetValue(rulesData.gRules, newValue);
            }
            else if (pRulesProperty != null)
            {
                bool currentValue = (bool)pRulesProperty.GetValue(rulesData.playerRules);
                bool newValue = !currentValue;
                pRulesProperty.SetValue(rulesData.playerRules, newValue);
            }
            else if (nRulesProperty != null)
            {
                bool currentValue = (bool)nRulesProperty.GetValue(rulesData.npcRules);
                bool newValue = !currentValue;
                nRulesProperty.SetValue(rulesData.npcRules, newValue);
            }
            else if (eRulesProperty != null)
            {
                bool currentValue = (bool)eRulesProperty.GetValue(rulesData.entityRules);
                bool newValue = !currentValue;
                eRulesProperty.SetValue(rulesData.entityRules, newValue);
            }
            else if (lRulesProperty != null)
            {
                bool currentValue = (bool)lRulesProperty.GetValue(rulesData.lootRules);
                bool newValue = !currentValue;
                lRulesProperty.SetValue(rulesData.lootRules, newValue);
            }
            else if (zRulesProperty != null)
            {
                bool currentValue = (bool)zRulesProperty.GetValue(rulesData.zoneRules);
                bool newValue = !currentValue;
                zRulesProperty.SetValue(rulesData.zoneRules, newValue);
            }
        }

        [Command("spdebug")]
        private void CMDDebugToggle(IPlayer ip, string cmd, string[] args)
        {
            if (!ip.HasPermission(PvEAdminPerms))
            {
                ip.Message("You don't have permission to use this command.");
                return;
            }
            Debug = !Debug;
            ip.Message($"Debug mode is now {(Debug ? "enabled" : "disabled")}.\nMake sure to disable this after use.");
        }

        #endregion

        #region Plugin Loads

        private void Init()
        {
            Puts("Cleaning up...");
            Puts("Initializing rules.....");
            //permission
            permission.RegisterPermission(PvEAdminPerms, this);
            permission.RegisterPermission(PvEAdminLootPerms, this);
            permission.RegisterPermission(PvEAdminDamagePerms, this);
            //Commands
            AddCovalenceCommand("simplepve", "CommandEnable");
            AddCovalenceCommand("resetsprules", "CommandResetRules");
            AddCovalenceCommand("rsp", "ReloadPlugin");
            AddCovalenceCommand("spdebug", "CMDDebugToggle");

            NotSubscribe();
        }

        private void OnServerInitialized()
        {
            ins = this;

            if (config.pveEnabledConfig)
            {
                Subscribe();
            }
            else NotSubscribe();

            foreach (var player in BasePlayer.activePlayerList)
            {
                OnPlayerConnected(player);
            }

            InitializeStatus();

            //Images load from ImageLibrary
            LoadImages();
            CheckStatusPlugin();

            DeepSeaBounds = DeepSeaManager.DeepSeaBounds;

            timer.In(1f, OnServerInitializedLate);
        }
        private void OnServerInitializedLate()
        {
            //Start Schedules
            ServerMgr.Instance.StartCoroutine(Schedules());
        }
        private void Loaded()
        {
            LoadSchTime();
            LoadRulesData();
        }

        private void OnServerSave()
        {
            SaveRulesData();
            SaveSchTime();
        }

        private void Unload()
        {
            Facepunch.Pool.FreeUnmanaged(ref builder);
            SaveRulesData();
            SaveSchTime();
            excludedBaseEntity.Clear();
            NotSubscribe();
            foreach (var player in BasePlayer.activePlayerList)
            {
                RemovePVEPanel(player);
                RemovePVPPanel(player);
                RemoveUIWarning(player);
                RemoveWarningLoot(player);
                CuiHelper.DestroyUi(player, "SimplePVECUI");
                if (config.statusUI.status && SimpleStatus) 
                {
                    SetStatus(player.UserIDString, StatusKey, 0);
                }
            }
            update = false;
            changedConfig = false;
            DiscordMessageSent = false;
        }

        private void Subscribe()
        {
            Subscribe(nameof(OnEntitySpawned));
            Subscribe(nameof(OnEntityTakeDamage));
            if (rulesData.lootRules.useLP)
            {
                Subscribe(nameof(CanLootEntity));
                Subscribe(nameof(CanLootPlayer));
                Subscribe(nameof(CanAdministerVending));
                Subscribe(nameof(OnOvenToggle));
                Subscribe(nameof(CanBuild));
                Subscribe(nameof(OnRackedWeaponMount));
                Subscribe(nameof(OnRackedWeaponSwap));
                Subscribe(nameof(OnRackedWeaponTake));
                Subscribe(nameof(OnRackedWeaponUnload));
                Subscribe(nameof(OnRackedWeaponLoad));
                //new
                Subscribe(nameof(OnItemDropped));
                Subscribe(nameof(OnLootEntity));
                //supply drop
                if (!rulesData.lootRules.anyoneOpenSupplyDrop)
                {
                    Subscribe(nameof(OnExplosiveThrown));
                    Subscribe(nameof(OnExplosiveDropped));
                    Subscribe(nameof(OnCargoPlaneSignaled));
                    Subscribe(nameof(OnSupplyDropDropped));
                }               
            }
            if (rulesData.entityRules.trapD)
            {
                Subscribe(nameof(OnTrapTrigger));
                Subscribe(nameof(CanBeTargeted));
            }
            if (rulesData.npcRules.NSamigP || rulesData.npcRules.PSamigP)
            {
                Subscribe(nameof(OnSamSiteTarget));
            }
            Subscribe(nameof(OnWallpaperRemove));
        }

        private void NotSubscribe()
        {
            Unsubscribe(nameof(OnEntityTakeDamage));
            Unsubscribe(nameof(CanLootEntity));
            Unsubscribe(nameof(CanLootPlayer));
            Unsubscribe(nameof(CanAdministerVending));
            Unsubscribe(nameof(OnOvenToggle));
            Unsubscribe(nameof(OnTrapTrigger));
            Unsubscribe(nameof(CanBeTargeted));
            Unsubscribe(nameof(OnSamSiteTarget));
            Unsubscribe(nameof(OnEntitySpawned));
            Unsubscribe(nameof(CanBuild));
            //Racked
            Unsubscribe(nameof(OnRackedWeaponMount));
            Unsubscribe(nameof(OnRackedWeaponSwap));
            Unsubscribe(nameof(OnRackedWeaponTake));
            Unsubscribe(nameof(OnRackedWeaponUnload));
            Unsubscribe(nameof(OnRackedWeaponLoad));
            //new
            Unsubscribe(nameof(OnItemDropped));
            Unsubscribe(nameof(OnLootEntity));
            //wallpaper
            Unsubscribe(nameof(OnWallpaperRemove));
            //supply drop
            Unsubscribe(nameof(OnExplosiveThrown));
            Unsubscribe(nameof(OnExplosiveDropped));
            Unsubscribe(nameof(OnCargoPlaneSignaled));
            Unsubscribe(nameof(OnSupplyDropDropped));
        }

        private void CheckStatusPlugin()
        {
            if (config.statusUI.status && !SimpleStatus)
            {
                PrintWarning("Please install SimpleStatus plugin if you are want to use status bar. (https://codefling.com/plugins/simple-status)");
            }
        }

        #endregion

        #region PVE Rules

        private bool Debug = false;

        StringBuilder builder = Facepunch.Pool.Get<StringBuilder>();

        private void DebugMessage(string message, BaseCombatEntity entity = null, HitInfo hitInfo = null, string rule = null)
        {
            if (!Debug) return;
            if (entity == null || hitInfo == null) return;

            builder.Clear();

            try
            {
                builder.AppendLine($"[SimplePVE Debug] {Time:yyyy-MM-dd_HH-mm-ss}");

                builder.AppendLine($" - Message: {message}");

                if (hitInfo?.Initiator != null)
                {
                    builder.AppendLine($" - Attacker: {hitInfo.Initiator.ShortPrefabName}");
                }

                if (entity != null)
                {
                    builder.AppendLine($" - Entity: {entity.ShortPrefabName}");
                }

                if (!string.IsNullOrEmpty(rule))
                {
                    builder.AppendLine($" - Rule: {rule}");
                }

                Puts(builder.ToString());
            }
            catch (Exception ex)
            {
                Puts($"[SimplePVE Debug Error]: {ex.Message}");
            }
            finally
            {
                builder.Clear();
            }
        }

        private HashSet<ulong> excludedBaseEntity = new HashSet<ulong>();

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo hit)
        {
            if (!config.pveEnabledConfig || entity == null || entity.IsDestroyed || entity.IsDead() || hit == null
                || (hit.InitiatorPlayer != null && permission.UserHasPermission(hit.InitiatorPlayer.UserIDString, PvEAdminDamagePerms)))
            {
                return null;
            }

            if (Interface.CallHook("CanEntityTakeDamage", new object[] { entity, hit }) is bool val)
            {
                if (val)
                {
                    DebugMessage("External called", entity, hit, "No rules applied Value: " + val);
                    return null;
                }
                noDamage(hit);
                DebugMessage("External called but bool is null ", entity, hit, "No rules applied & blocking damage");
                return true;
            }

            if (Interface.CallHook("IsHeliSignalObject", entity.skinID) != null) return null;

            if (entity is PatrolHelicopter || entity.ShortPrefabName.Contains("patrolheli") || entity is BradleyAPC || entity.ShortPrefabName.Contains("bradley") || entity is CH47Helicopter || entity.ShortPrefabName.Contains("ch47"))
            {
                DebugMessage("Entity is Helicopter or Bradley or Chinook or CH47", entity, hit, "No rules - Do damage");
                return null;
            }

            if (!CheckIncludes(entity, hit))
            {
                noDamage(hit);
                if (hit.InitiatorPlayer?.userID.IsSteamId() == true)
                {
                    ShowWarningAlert(hit.InitiatorPlayer);
                }
                DebugMessage($"Attacker: {hit?.Initiator} checkmate & Damaged: {entity.ShortPrefabName}");
                return true;
            }
            //DebugMessage("Internal called", entity, hit, "No rules applied");
            return null;
        }

        private bool CheckIncludes(BaseCombatEntity entity, HitInfo hit)
        {
            var damageType = hit.damageTypes.GetMajorityDamageType();

            if (damageType == DamageType.Suicide)
            {
                if (entity is BasePlayer victim1 && victim1.userID.IsSteamId() && rulesData.gRules.allowSuicide)
                {
                    DebugMessage("Suicide damage", entity, hit, nameof(rulesData.gRules.allowSuicide) + " Damage: " + rulesData.gRules.allowSuicide);
                    return true;
                }
                else
                {
                    if (entity is BasePlayer player && player.userID.IsSteamId())
                        entity.SendMessage("Suicide is disabled on this server!");
                    return false;
                }
            }

            if (damageType == DamageType.Radiation || damageType == DamageType.RadiationExposure)
            {
                if (entity is BasePlayer && rulesData.gRules.pRadiation)
                {
                    DebugMessage("Radiation damage", entity, hit, nameof(rulesData.gRules.pRadiation) + " Damage: " + rulesData.gRules.pRadiation);
                    return true;
                }
            }

            if (damageType == DamageType.Fall ||
                damageType == DamageType.Drowned ||
                damageType == DamageType.Decay ||
                damageType == DamageType.Cold ||
                damageType == DamageType.Hunger ||
                damageType == DamageType.Poison ||
                damageType == DamageType.Thirst ||
                damageType == DamageType.Bleeding) //new rules
            {
                return true;
            }

            if (rulesData.zoneRules.zoneManager && ZoneManager != null)
            {
                var weapon = hit.Initiator ?? hit.Weapon ?? hit.WeaponPrefab;
                Initiator(hit, weapon);
                List<string> entityLoc = GetAllZones(entity);
                List<string> iniLoc = GetAllZones(weapon);

                if (CheckExclusion(entityLoc, iniLoc))
                {
                    DebugMessage("ZoneManager zones", entity, hit, nameof(rulesData.zoneRules.disableRulesZone) + " Damage: " + rulesData.zoneRules.disableRulesZone);
                    return rulesData.zoneRules.disableRulesZone;
                }
            }

            if (entity is DecayEntity)
            {
                if (hit.Initiator is BasePlayer)
                {
                    BasePlayer player = hit.Initiator as BasePlayer;
                    if (player.userID.IsSteamId() && entity.ShortPrefabName.Contains("cupboard"))
                    {
                        if (entity.OwnerID == player.userID)
                        {
                            DebugMessage("Cupboard damage", entity, hit, nameof(rulesData.playerRules.pCanDamageOwnBuildings) + " Damage: " + rulesData.playerRules.pCanDamageOwnBuildings);
                            return rulesData.playerRules.pCanDamageOwnBuildings;
                        }
                        else if (IsAlliedWithPlayer(entity.OwnerID, player.userID))
                        {
                            DebugMessage("Cupboard damage team", entity, hit, nameof(rulesData.playerRules.pCanDamOwnTeamamtesBuilding) + " Damage: " + rulesData.playerRules.pCanDamOwnTeamamtesBuilding);
                            return rulesData.playerRules.pCanDamOwnTeamamtesBuilding;
                        }
                        else if (entity.OwnerID == 0)
                        {
                            DebugMessage("Cupboard damage server", entity, hit, "No rules damage on");
                            return true;
                        }
                        else
                        {
                            DebugMessage("Cupboard damage other players", entity, hit, nameof(rulesData.playerRules.pCanDamOtherpBuildings) + " Damage: " + rulesData.playerRules.pCanDamOtherpBuildings);
                            return rulesData.playerRules.pCanDamOtherpBuildings;
                        }
                    }
                }
            }

            if (entity is BasePlayer && hit.Initiator is BasePlayer)
            {
                BasePlayer attacker = hit.Initiator as BasePlayer;
                BasePlayer victim = entity as BasePlayer;

                //check friendly fire
                if (attacker.userID.IsSteamId() && victim.userID.IsSteamId() && IsAlliedWithPlayer(attacker.userID, victim.userID))
                {
                    if (attacker.userID == victim.userID)
                    {
                        DebugMessage("Player v player self damage", entity, hit, "No rules damage on & self damage allowed");
                        return true;
                    }
                    DebugMessage("Player v player friendly", entity, hit, nameof(rulesData.gRules.aFriendlyFire) + " Damage: " + rulesData.gRules.aFriendlyFire);
                    return rulesData.gRules.aFriendlyFire;

                } //check sleepers
                else if (attacker.userID.IsSteamId() && victim.userID.IsSteamId() && !IsAlliedWithPlayer(attacker.userID, victim.userID))
                {
                    if (victim.IsSleeping())
                    {
                        DebugMessage("Player v player sleeper", entity, hit, nameof(rulesData.gRules.killSleepers) + " Damage: " + rulesData.gRules.killSleepers);
                        return rulesData.gRules.killSleepers;

                    }
                    else if (rulesData.zoneRules.enablePVPDelay && CheckPVPDelay(victim, attacker)) //pvp delay from zones
                    {
                        DebugMessage("Player v player zone pvp delay", entity, hit, nameof(rulesData.zoneRules.enablePVPDelay) + " Damage: " + rulesData.zoneRules.enablePVPDelay);
                        return true;
                    }
                    else if (IsInDeepSea(attacker.transform.position) && IsInDeepSea(victim.transform.position))
                    {
                        DebugMessage("Player v player deep sea", entity, hit, nameof(rulesData.playerRules.PlayerCanDamagePlayerInDeepSea) + " Damage: " + rulesData.playerRules.PlayerCanDamagePlayerInDeepSea);
                        return rulesData.playerRules.PlayerCanDamagePlayerInDeepSea;
                    }
                    //player can hurt players
                    else if (rulesData.playerRules.pCanHurtp == true && victim.IsConnected)
                    {                      
                        DebugMessage("Player v player enemy", entity, hit, nameof(rulesData.playerRules.pCanHurtp) + " Damage: " + rulesData.playerRules.pCanHurtp);
                        return true;
                    }
                    else
                    {
                        DebugMessage("Player v player no one", entity, hit, "No rules damage off");
                        return false;
                    }
                }
            }

            if (hit.Initiator is BasePlayer attacker1)
            {
                if (attacker1.userID.IsSteamId())
                {
                    if (IsHostileEntity(entity))
                    {
                        DebugMessage("Player v Hostile NPCs", entity, hit, nameof(rulesData.playerRules.pCanHurtn) + " Damage: " + rulesData.playerRules.pCanHurtn);
                        return rulesData.playerRules.pCanHurtn;
                    }
                    else if (entity is FarmableAnimal)
                    {
                        BuildingPrivlidge privs = entity.GetBuildingPrivilege();
                        if (privs != null && privs.IsAuthed(attacker1))
                        {
                            DebugMessage("Player v FarmableAnimal own", entity, hit, "No rule damage on & player is authed");
                            return true;
                        }
                        else
                        {
                            DebugMessage("Player v FarmableAnimal enemy", entity, hit, "No rule no damage to Framables cutie");
                            return false;
                        }
                    }
                    else if (IsFriendlyEntity(entity))
                    {
                        DebugMessage("Player v Animal NPCs", entity, hit, nameof(rulesData.playerRules.pCanHurta) + " Damage: " + rulesData.playerRules.pCanHurta);
                        return rulesData.playerRules.pCanHurta;
                    }
                    else if (IsVehicleEntity(entity))
                    {
                        bool VehcileAutoClaim = config.vehicleAutoClaim;

                        if (VehcileAutoClaim)
                        {
                            BaseCombatEntity vehicleEntity = GetAppropriateVehicle(entity as BaseEntity);
                            //Puts(vehicleEntity.OwnerID.ToString());
                            if (vehicleEntity.OwnerID == 0 || vehicleEntity.OwnerID < 76561197960265728L)
                            {                              
                                DebugMessage("Player v vehicles no owner & Vehicle AutoClaim Allowed", entity, hit, "No rules damage on");
                                return true;
                            }
                            else if (vehicleEntity.OwnerID == attacker1.userID || IsAlliedWithPlayer(vehicleEntity.OwnerID, attacker1.userID))
                            {
                                DebugMessage("Player v vehicles own/team & Vehicle AutoClaim Allowed", entity, hit, nameof(rulesData.playerRules.PlayerCanDamagaOwnTeamVehicles) + " Damage: " + rulesData.playerRules.PlayerCanDamagaOwnTeamVehicles);
                                return rulesData.playerRules.PlayerCanDamagaOwnTeamVehicles;
                            }
                            else
                            {
                                DebugMessage("Player v vehicles enemy & Vehicle AutoClaim Allowed", entity, hit, nameof(rulesData.playerRules.PlayerCanDamagaOtherVehicles) + " Damage: " + rulesData.playerRules.PlayerCanDamagaOtherVehicles);
                                return rulesData.playerRules.PlayerCanDamagaOtherVehicles;
                            }
                        }
                        else
                        {
                            DebugMessage("Player v vehicles but AutoClaim Vehicle is off so 1 Rule Applies", entity, hit, nameof(rulesData.playerRules.pCanDamEntitys) + " Damage: " + rulesData.playerRules.pCanDamEntitys);
                            return rulesData.playerRules.pCanDamEntitys;
                        }
                        //Depricated
                    }
                    else if (entity is Tugboat tugboat1)
                    {
                        if (IsTugboatAuthed(tugboat1, attacker1))
                        {
                            DebugMessage("Player v tugboat has auth", entity, hit, nameof(rulesData.playerRules.pCanDamageOwnBuildings) + " Damage: " + rulesData.playerRules.pCanDamageOwnBuildings);
                            return rulesData.playerRules.pCanDamageOwnBuildings;
                        }
                        else
                        {
                            DebugMessage("Player v tugboat enemy", entity, hit, nameof(rulesData.playerRules.pCanDamOtherpBuildings) + " Damage: " + rulesData.playerRules.pCanDamOtherpBuildings);
                            return rulesData.playerRules.pCanDamOtherpBuildings;
                        }
                    }

                    //twig
                    if (entity is BuildingBlock && attacker1.userID.IsSteamId())
                    {
                        var twig = entity as BuildingBlock;
                        if (twig.grade == BuildingGrade.Enum.Twigs)
                        {
                            if (entity.OwnerID == 0) return true;
                            if (entity.OwnerID == attacker1.userID || IsAlliedWithPlayer(entity.OwnerID, attacker1.userID) || BuildingAuth(twig, attacker1))
                            {
                                DebugMessage("Player v twig own/team/authed", entity, hit, "allowed by default");
                                return true;
                            }
                            else
                            {
                                DebugMessage("Player v twig enemy", entity, hit, nameof(rulesData.playerRules.PallowTwigDamage) + " Damage: " + rulesData.playerRules.PallowTwigDamage);
                                return rulesData.playerRules.PallowTwigDamage;
                            }
                        }
                    }
                    //check wooden shelter
                    if (entity.ShortPrefabName.Contains("legacy.shelter.wood"))
                    {
                        if (entity.OwnerID == 0) return true;
                        if (entity.OwnerID == attacker1.userID)
                        {
                            DebugMessage("Player v woodenshelter own", entity, hit, "No rules damage on");
                            return true;
                        }
                        else
                        {
                            DebugMessage("Player v woodenshelter team/enemy", entity, hit, nameof(rulesData.playerRules.pCanDamOtherpBuildings) + " Damage: " + rulesData.playerRules.pCanDamOtherpBuildings);
                            return rulesData.playerRules.pCanDamOtherpBuildings;
                        }
                    }
                    // Check if the entity is a BuildingPrivilege
                    if (entity is BuildingPrivlidge)
                    {
                        object owned = PlayerOwnsTC(attacker1.userID, entity as BuildingPrivlidge);

                        if (owned is bool && !(bool)owned)
                        {
                            // If the player doesn't own the building, check if they can damage other player builds
                            DebugMessage("Player v building enemy", entity, hit, nameof(rulesData.playerRules.pCanDamOtherpBuildings) + " Damage: " + rulesData.playerRules.pCanDamOtherpBuildings);
                            return rulesData.playerRules.pCanDamOtherpBuildings;
                        }
                        else
                        {
                            if (entity.OwnerID == attacker1.userID || IsAuthtoTC(entity, attacker1))
                            {
                                DebugMessage("Player v building own/authed", entity, hit, nameof(rulesData.playerRules.pCanDamageOwnBuildings) + " Damage: " + rulesData.playerRules.pCanDamageOwnBuildings);
                                return rulesData.playerRules.pCanDamageOwnBuildings;
                            }

                            if (IsAlliedWithPlayer(entity.OwnerID, attacker1.userID))
                            {
                                DebugMessage("Player v building team", entity, hit, nameof(rulesData.playerRules.pCanDamOwnTeamamtesBuilding) + " Damage: " + rulesData.playerRules.pCanDamOwnTeamamtesBuilding);
                                return rulesData.playerRules.pCanDamOwnTeamamtesBuilding;
                            }

                            if (entity.OwnerID == 0)
                            {
                                DebugMessage("Player v building server", entity, hit, "No rules damage on");
                                return true;
                            }
                        }
                    }
                    // Check if the entity is a BaseEntity
                    else if (entity is BaseEntity && attacker1.userID.IsSteamId())
                    {
                        object ownsEntity = OwnsItem(attacker1.userID, entity);

                        if (ownsEntity is bool && !(bool)ownsEntity)
                        {
                            DebugMessage("Player v entity enemy", entity, hit, nameof(rulesData.playerRules.pCanDamOtherpBuildings) + " Damage: " + rulesData.playerRules.pCanDamOtherpBuildings);
                            return rulesData.playerRules.pCanDamOtherpBuildings;
                        }
                        else
                        {
                            if (entity.OwnerID == attacker1.userID || IsAuthtoTC(entity, attacker1))
                            {
                                DebugMessage("Player v entity own/authed", entity, hit, nameof(rulesData.playerRules.pCanDamageOwnBuildings) + " Damage: " + rulesData.playerRules.pCanDamageOwnBuildings);
                                return rulesData.playerRules.pCanDamageOwnBuildings;
                            }
                            
                            if (IsAlliedWithPlayer(entity.OwnerID, attacker1.userID))
                            {
                                DebugMessage("Player v entity team", entity, hit, nameof(rulesData.playerRules.pCanDamOwnTeamamtesBuilding) + " Damage: " + rulesData.playerRules.pCanDamOwnTeamamtesBuilding);
                                return rulesData.playerRules.pCanDamOwnTeamamtesBuilding;
                            }

                            if (entity is PlayerBoat playerBoat)
                            {
                                if (playerBoat.IsPlayerAuthed(attacker1, false))
                                {
                                    DebugMessage("Player v entity PlayerBoat own/authed", entity, hit, nameof(rulesData.playerRules.PlayerCanDamageOwnBoats) + " Damage: " + rulesData.playerRules.PlayerCanDamageOwnBoats);
                                    return rulesData.playerRules.PlayerCanDamageOwnBoats;
                                }
                                else
                                {
                                    if (IsInDeepSea(playerBoat.transform.position) && IsInDeepSea(attacker1.transform.position))
                                    {
                                        DebugMessage("Player v entity PlayerBoat enemy in DeepSea", entity, hit, nameof(rulesData.playerRules.PlayerCanDamageOtherBoatsInDeepSea) + " Damage: " + rulesData.playerRules.PlayerCanDamageOtherBoatsInDeepSea);
                                        return rulesData.playerRules.PlayerCanDamageOtherBoatsInDeepSea;
                                    }
                                    DebugMessage("Player v entity PlayerBoat enemy", entity, hit, nameof(rulesData.playerRules.PlayerCanDamageOtherBoats) + " Damage: " + rulesData.playerRules.PlayerCanDamageOtherBoats);
                                    return rulesData.playerRules.PlayerCanDamageOtherBoats;
                                }
                            }

                            if (entity.OwnerID == 0)
                            {
                                DebugMessage("Player v entity server", entity, hit, "No rules damage on");
                                return true;
                            }
                        }
                    }
                }
            }

            if (hit.Initiator is BaseAnimalNPC || hit.Initiator is BaseNPC2 || hit.Initiator is SnakeHazard)
            {
                if (entity is BasePlayer)
                {
                    DebugMessage("Animal v player", entity, hit, nameof(rulesData.npcRules.AnimalCanHurtP) + " Damage: " + rulesData.npcRules.AnimalCanHurtP);
                    return rulesData.npcRules.AnimalCanHurtP;
                }
                else
                {
                    DebugMessage("Animal v others", entity, hit, "No rules damage on");
                    return true;
                }
            }

            if (hit.Initiator is ScientistNPC || hit.Initiator is NPCPlayer || hit.Initiator is HumanNPC ||
                hit.Initiator is TunnelDweller || hit.Initiator is UnderwaterDweller || hit.Initiator is ScarecrowNPC || hit.Initiator is FrankensteinPet || hit.Initiator is SimpleShark ||
                (hit.Initiator != null && hit.Initiator.ShortPrefabName.Contains("zombie")) || (hit.Initiator != null && hit.Initiator.ShortPrefabName.Contains("CustomScientistNPC")))
            {
                if (entity is BasePlayer victim2)
                {
                    if (victim2.userID.IsSteamId() && !victim2.IsNpc)
                    {
                        DebugMessage("NPC player vs player", entity, hit, nameof(rulesData.npcRules.NpcHurtP) + " Damage: " + rulesData.npcRules.NpcHurtP);
                        return rulesData.npcRules.NpcHurtP;
                    }
                    else
                    {
                        DebugMessage("NPC player vs npc", entity, hit, "No rules damage on");
                        return true;
                    }
                }
                else if (entity != null && (entity is BuildingBlock || entity is BuildingPrivlidge || entity is BaseEntity))
                {
                    DebugMessage("NPC player vs base entitys", entity, hit, "No rules damage on");
                    return true;
                }
                else
                {
                    DebugMessage("NPC player vs others", entity, hit, "No rules damage on");
                    return true;
                }
            }

            if (hit.Initiator is PatrolHelicopter || (hit.WeaponPrefab != null && (hit.WeaponPrefab.ShortPrefabName.Equals("rocket_heli") || hit.WeaponPrefab.ShortPrefabName.Equals("rocket_heli_napalm")))
                || (hit.Initiator is PatrolHelicopter && hit.damageTypes.Has(DamageType.Explosion))
                || (hit.Initiator != null && (hit.Initiator.ShortPrefabName.Equals("oilfireballsmall") || hit.Initiator.ShortPrefabName.Equals("napalm"))))
            {
                if (entity is BasePlayer)
                {
                    DebugMessage("Patrol Heli bombs/fire/naplm vs player/npc", entity, hit, nameof(rulesData.npcRules.PatrolHurtP) + " Damage: " + rulesData.npcRules.PatrolHurtP);
                    return rulesData.npcRules.PatrolHurtP;
                }
                else if (entity is BuildingBlock || entity is BaseEntity)
                {
                    DebugMessage("Patrol Heli bombs/fire/naplm vs bases", entity, hit, nameof(rulesData.npcRules.PatrolHurtB) + " Damage: " + rulesData.npcRules.PatrolHurtB);
                    return rulesData.npcRules.PatrolHurtB;
                }
                else
                {
                    DebugMessage("Patrol Heli bombs/fire/naplm vs others", entity, hit, "No rules damage on");
                    return true;
                }
            }

            if (hit.Initiator is BaseEntity wall)
            {
                if (wall.PrefabName.Contains("wall.external") || wall.ShortPrefabName.Contains("gates.external.high"))
                {
                    DebugMessage("Walls external vs others", entity, hit, nameof(rulesData.entityRules.WallHurtP) + " Damage: " + rulesData.entityRules.WallHurtP); //add new rules
                    return rulesData.entityRules.WallHurtP;
                }
                else if (wall.ShortPrefabName.Contains("barricade"))
                {
                    DebugMessage("Barricades vs others", entity, hit, nameof(rulesData.entityRules.BarrHurtP) + " Damage: " + rulesData.entityRules.BarrHurtP);
                    return rulesData.entityRules.BarrHurtP;
                }
            }

            if (hit.Initiator is BaseModularVehicle vehicle && vehicle.name.Contains("modularcar"))
            {
                if (entity is BasePlayer vP)
                {
                    if (vP.userID.IsSteamId())
                    {
                        if (!vP.IsSleeping())
                        {
                            DebugMessage("Vehicles vs players", entity, hit, nameof(rulesData.entityRules.VehicleHurtP) + " Damage: " + rulesData.entityRules.VehicleHurtP);
                            return rulesData.entityRules.VehicleHurtP;
                        }
                        else
                        {
                            DebugMessage("Vehicles vs players sleeping", entity, hit, nameof(rulesData.gRules.killSleepers) + " Damage: " + rulesData.gRules.killSleepers);
                            return rulesData.gRules.killSleepers;
                        }
                    }
                }
                else if (entity != null && (entity is BaseAnimalNPC || IsFriendlyEntity(entity) || IsHostileEntity(entity)))
                {
                    DebugMessage("Vehicles vs others/npc/animals", entity, hit, "No rules damage on");
                    return true;
                }
                else
                {
                    DebugMessage("Vehicles vs vehicle collision/vehicle", entity, hit, nameof(rulesData.entityRules.enableVehicleCollsionD) + " Damage: " + rulesData.entityRules.enableVehicleCollsionD);
                    return rulesData.entityRules.enableVehicleCollsionD;
                }
            }

            if (hit.Initiator is Minicopter || hit.Initiator is ScrapTransportHelicopter || hit.Initiator is AttackHelicopter)
            {
                if (entity is BasePlayer || entity is BuildingBlock)
                {
                    DebugMessage("Mini/Scrap/Attack vs player/bases", entity, hit, nameof(rulesData.entityRules.VehicleHurtP) + " Damage: " + rulesData.entityRules.VehicleHurtP); //add new rules
                    return rulesData.entityRules.VehicleHurtP;
                }
            }

            if (hit.Initiator is SamSite samSite)
            {
                if (entity is Minicopter || entity is ScrapTransportHelicopter || entity is Parachute || entity is BasePlayer
                    || entity is HotAirBalloon || entity is HotAirBalloonArmor || entity is AttackHelicopter)
                {
                    if (samSite.InSafeZone() || samSite.isServer)
                    {
                        DebugMessage("Safe zone Samsites vs mini/scrap/para/hab/haba/attack", entity, hit, nameof(rulesData.npcRules.NSamigPHurts) + " Damage: " + rulesData.npcRules.NSamigPHurts);
                        return rulesData.npcRules.NSamigPHurts;
                    }
                    else
                    {
                        DebugMessage("Player Samsites vs mini/scrap/para/hab/haba/attack", entity, hit, nameof(rulesData.npcRules.PSamigPHurt) + " Damage: " + rulesData.npcRules.PSamigPHurt);
                        return rulesData.npcRules.PSamigPHurt;
                    }
                }
            }
            var findIni = hit.Initiator ?? hit.Weapon ?? hit.WeaponPrefab;
            //trap damages
            if (entity is BasePlayer victim3 && hit.Initiator != null && (hit.Initiator is BaseTrap || hit.Initiator is GunTrap || hit.Initiator is FlameTurret || hit.Initiator is AutoTurret
                || hit.Initiator is TeslaCoil || hit.Initiator is ReactiveTarget || hit.Initiator.ShortPrefabName.Contains("spikes.trap")))
            {
                if (hit.Initiator.OwnerID == 0)
                {
                    DebugMessage("Traps vs Any", entity, hit, "No Rules cause Ini is 0");
                    return true;
                }
                else if (victim3.userID.IsSteamId())
                {
                    DebugMessage("Traps damage vs players", entity, hit, nameof(rulesData.entityRules.trapToPlayer) + " Damage: " + rulesData.entityRules.trapToPlayer);
                    return rulesData.entityRules.trapToPlayer;
                }
                else if (victim3.IsNpc || IsHostileEntity(victim3) || IsFriendlyEntity(entity))
                {
                    DebugMessage("Traps vs npcs", entity, hit, "No Rules");
                    return true;
                }
                else
                {
                    DebugMessage("Traps vs others", entity, hit, "No Rules");
                    return true;
                }
            }

            TryInitiator(hit, findIni);

            //default collision damage
            if (entity is BaseVehicle)
            {
                BaseVehicle vehicleGet = entity as BaseVehicle;
                if ((vehicleGet.GetDriver() != null && findIni == vehicleGet) || vehicleGet.GetDriver() == null)
                {
                    DebugMessage("Vehicle collision", entity, hit, nameof(rulesData.entityRules.enableVehicleCollsionD) + " Damage: " + rulesData.entityRules.enableVehicleCollsionD);
                    return rulesData.entityRules.enableVehicleCollsionD;
                }
            }

            //Bradly Damages           
            if (findIni is BradleyAPC || findIni?.ShortPrefabName == "maincannonshell")
            {
                if (entity is BasePlayer || entity is BaseEntity)
                {
                    DebugMessage("Bradley vs players/entity", entity, hit, nameof(rulesData.npcRules.bradleyHurtP) + " Damage: " + rulesData.npcRules.bradleyHurtP);
                    return rulesData.npcRules.bradleyHurtP;
                }
            }

            if ((findIni != null && findIni.ShortPrefabName.Contains("oilfireball2"))
                || (findIni != null && findIni.ShortPrefabName.Contains("oilfireballsmall"))
                || (findIni != null && findIni.ShortPrefabName.Contains("fireball")))
            {
                if (entity is BasePlayer || entity is BaseEntity)
                {
                    BasePlayer hitter = GetOwnerPlayer(findIni);
                    if (hitter != null)
                    {
                        DebugMessage("oil fires", entity, hit, "No rules Damage off");
                        return false;
                    }
                    else
                    {
                        DebugMessage("oil fires", entity, hit, "No rules Damage false");
                        return false;
                    }
                }
            }

            //singles

            //train
            if (entity.ShortPrefabName.Contains("trainbarricade"))
            {
                DebugMessage("trainbarricade", entity, hit, "No rules Damage on");
                return true;
            }

            if (findIni != null && findIni?.ShortPrefabName == "campfire")
            {
                DebugMessage("campfire", entity, hit, "No rules Damage on");
                return true;
            }

            if (findIni != null && findIni.ShortPrefabName.Contains("cactus"))
            {
                DebugMessage("cactus", entity, hit, "No rules Damage on");
                return true;
            }

            if (findIni != null && findIni.ShortPrefabName.Contains("beeswarm"))
            {
                DebugMessage("beeswarm", entity, hit, nameof(rulesData.gRules.aBeeSwarmDamage) + " Damage: " + rulesData.gRules.aBeeSwarmDamage);
                return rulesData.gRules.aBeeSwarmDamage;
            }

            if (findIni != null && findIni.ShortPrefabName.Contains("drone.deployed"))
            {
                DebugMessage("drone.deployed", entity, hit, "No rules Damage on");
                return true;
            }

            return false;
        }

        private bool CheckPVPDelay(BasePlayer victim, BasePlayer attacker)
        {
            PVPDelay victimDelay;
            if (pvp_delays.TryGetValue(victim.userID, out victimDelay))
            {
                if (!string.IsNullOrEmpty(victimDelay.ZoneID) && IsPlayerInZone(victimDelay, attacker)) //ZonePlayer attacked delayed player
                {
                    return true;
                }
                PVPDelay attackerDelay;
                if (pvp_delays.TryGetValue(attacker.userID, out attackerDelay) && victimDelay.ZoneID == attackerDelay.ZoneID) //Delayed player attacked delayed player
                {
                    return true;
                }
            }
            else
            {
                PVPDelay attackerDelay;
                if (pvp_delays.TryGetValue(attacker.userID, out attackerDelay))
                {
                    if (!string.IsNullOrEmpty(attackerDelay.ZoneID) && IsPlayerInZone(attackerDelay, victim)) //DelayedPlayer attack ZonePlayer
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool IsPlayerInZone(PVPDelay delay, BasePlayer player)
        {
            return (bool)ZoneManager.Call("IsPlayerInZone", delay.ZoneID, player);
        }

        /*private BasePlayer GetOwnerPlayer(BaseEntity entity)
        {
            if (entity != null)
            {
                BasePlayer ownerPlayer = entity.OwnerID != 0 ? BasePlayer.FindByID(entity.OwnerID) : null;
                return ownerPlayer;
            }
            return null;
        }*/

        private BasePlayer GetOwnerPlayer(BaseEntity entity)
        {
            if (entity == null) return null;

            // Retrieve the owner based on the OwnerID or creatorEntity if available
            BasePlayer ownerPlayer = entity.OwnerID != 0 ? BasePlayer.FindByID(entity.OwnerID) : entity.creatorEntity as BasePlayer;

            return ownerPlayer;
        }


        //entitys Methods
        private bool IsHostileEntity(BaseCombatEntity entity)
        {
            return entity.ShortPrefabName.Contains("scientistnpc") || entity.ShortPrefabName.Contains("scientist") || entity.ShortPrefabName.Contains("frankensteinpet") ||
                   entity.ShortPrefabName.Contains("zombie") || entity.ShortPrefabName.Contains("tunneldweller") ||
                   entity.ShortPrefabName.Contains("underwaterdweller") || entity.ShortPrefabName.Contains("scarecrow") ||
                   entity.ShortPrefabName.Contains("xmasdwelling") || entity.ShortPrefabName.Contains("gingerbread") || entity.ShortPrefabName.Contains("CustomScientistNPC") ||
                   entity.ShortPrefabName.Contains("zombie") || entity.ShortPrefabName.Contains("shark") || entity.ShortPrefabName.Contains("ch47scientists");
        }

        private bool IsFriendlyEntity(BaseCombatEntity entity)
        {
            return entity.ShortPrefabName.Contains("bear") || entity.ShortPrefabName.Contains("boar") ||
                   entity.ShortPrefabName.Equals("chicken") || entity.ShortPrefabName.Contains("stag") ||
                   entity.ShortPrefabName.Contains("wolf") || entity.ShortPrefabName.Contains("deer") || entity.ShortPrefabName.Contains("wolf2") || entity is BaseAnimalNPC || entity is BaseNPC || entity is BaseNPC2 || entity is SnakeHazard || entity is Tiger || entity is Panther || entity is Crocodile;
        }

        private bool IsVehicleEntity(BaseCombatEntity entity)
        {
            return entity.name.Contains("modularcar") || entity.name.Contains("mini") ||
                   entity.name.Contains("scraptransport") || entity.name.Contains("attackheli") ||
                   entity.name.Contains("row") || entity.name.Contains("rhib") ||
                   entity.name.Contains("helicopter") ||
                   entity.name.Contains("bike") ||
                   entity.name.Contains("ptboat.deepsea");
        }

        private bool CheckTugboat(Tugboat boat, BasePlayer player)
        {
            return !boat.children.IsNullOrEmpty() && boat.children.Exists(child => child is VehiclePrivilege vehiclePrivilege && vehiclePrivilege.AnyAuthed() && vehiclePrivilege.IsAuthed(player));
        }

        //methods
        private void noDamage(HitInfo hit)
        {
            if (hit.Weapon is BlowPipeWeapon)
            {
                hit.HitEntity = null;
            }
            hit.damageTypes?.Clear();
            hit.DidHit = false;
            hit.DoHitEffects = false;
            hit.damageTypes.ScaleAll(0);
        }

        private bool IsTugboatAuthed(Tugboat tugboat, BasePlayer attacker)
        {
            return !tugboat.children.IsNullOrEmpty() && tugboat.children.Exists(child => child is VehiclePrivilege vehiclePrivilege && vehiclePrivilege.AnyAuthed() && vehiclePrivilege.IsAuthed(attacker));
        }

        private void TryInitiator(HitInfo hitInfo, BaseEntity weapon)
        {
            if (weapon == null)
            {
                return;
            }

            if (!(hitInfo.Initiator is BasePlayer) && weapon.creatorEntity is BasePlayer)
            {
                hitInfo.Initiator = weapon.creatorEntity;
            }

            if (hitInfo.Initiator == null)
            {
                hitInfo.Initiator = weapon.GetParentEntity();
            }

            if (hitInfo.Initiator == null)
            {
                hitInfo.Initiator = weapon;
            }
        }

        private bool IsAlliedWithPlayer(ulong playerId, ulong targetId)
        {
            return playerId == targetId ||
                   (RelationshipManager.ServerInstance.playerToTeam.TryGetValue(playerId, out var team) && team.members.Contains(targetId)) ||
                   (Clans != null && Convert.ToBoolean(Clans.Call("IsClanMember", playerId.ToString(), targetId.ToString()))) ||
                   (FClan != null && Convert.ToBoolean(FClan.Call("IsClanMember", playerId.ToString(), targetId.ToString()))) ||
                   (Friends != null && Convert.ToBoolean(Friends.Call("AreFriends", playerId.ToString(), targetId.ToString())));
        }


        private object PlayerOwnsTC(ulong p, BuildingPrivlidge privilege)
        {
            if (p == null || privilege == null) return null;

            BuildingManager.Building building = privilege.GetBuilding();
            if (building != null)
            {
                BuildingPrivlidge pr = building.GetDominatingBuildingPrivilege();

                foreach (ulong authroized in pr.authorizedPlayers) //changed 
                {
                    if (privilege.OwnerID == p || p == authroized || IsAlliedWithPlayer(authroized, privilege.OwnerID))
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }

                }
                return false;
            }
            return null;

        }
        private bool BuildingAuth(BuildingBlock block, BasePlayer player)
        {
            var building = block.GetBuilding();
            if (building == null) return false;
            var priv = building.GetDominatingBuildingPrivilege();
            return priv != null && priv.IsAuthed(player);
        }

        private object OwnsItem(ulong p, BaseEntity e)
        {
            if (p == null || e == null)
            {
                return null;
            }
            if (e.OwnerID == 0) return true;

            BuildingPrivlidge priv = e.GetBuildingPrivilege();
            bool hasPriv = false;
            if (priv != null)
            {
                foreach (ulong authrized in priv.authorizedPlayers) //priv.authorizedPlayers.Select(x => x.userid).ToArray()
                {
                    if (p == authrized)
                    {
                        //player has privs on the entity
                        hasPriv = true;
                        break;
                    }
                    else
                    {
                        if (IsAlliedWithPlayer(authrized, e.OwnerID))
                        {
                            hasPriv = true;
                            //player is teams with the owner
                            break;
                        }
                    }
                }
            }
            else
            {
                if (e.OwnerID == p || IsAlliedWithPlayer(e.OwnerID, p))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            bool isFriend = IsAlliedWithPlayer(p, e.OwnerID);
            if (hasPriv)
            {
                if (rulesData.playerRules.pCanDamageOwnBuildings && e.OwnerID == p)
                {
                    return true; // Player owns entity and has privs
                }
                else if (isFriend && rulesData.playerRules.pCanDamOwnTeamamtesBuilding)
                {
                    return true; // Player is allied with the entity owner & has Building priv
                }
            }

            return false; // Default to false if none of the above conditions are met

        }

        private void Initiator(HitInfo hitInfo, BaseEntity weapon)
        {
            if (weapon == null)
            {
                return;
            }

            if (!(hitInfo.Initiator is BasePlayer) && weapon.creatorEntity is BasePlayer)
            {
                hitInfo.Initiator = weapon.creatorEntity;
            }

            if (hitInfo.Initiator == null)
            {
                hitInfo.Initiator = weapon.GetParentEntity();
            }

            if (hitInfo.Initiator == null)
            {
                hitInfo.Initiator = weapon;
            }
        }

        private bool CheckExclusion(List<string> locations1, List<string> locations2)
        {
            if (locations1 == null || locations2 == null) return false;
            List<string> locations = GetSharedLocations(locations1, locations2);

            if (!locations.IsNullOrEmpty())
            {
                foreach (string loc in locations)
                {
                    return true;
                }
            }
            return false;
        }

        private List<string> GetSharedLocations(List<string> e0Locations, List<string> e1Locations)
        {
            return e0Locations.Intersect(e1Locations).ToList();
        }

        private bool CheckConvoy(BaseEntity entity)
        {
            if (Convoy != null)
            {
                object result = Convoy.CallHook("IsConvoyVehicle", entity);
                if (result is bool)
                {
                    return (bool)result;
                }
            }
            return false;
        }

        #endregion

        #region Looting Rules               

        private void CanLootEntity(BasePlayer player, Fridge fridge)
        {
            if (!rulesData.lootRules.useLP) return;
            if (player == null || fridge == null) return;
            if (fridge.OwnerID == 0) return;
            if (config.excludedEntitys.Count > 0 && config.excludedEntitys.Contains(fridge.ShortPrefabName)) return;

            if (rulesData.lootRules.adminCanLootAll && permission.UserHasPermission(player.UserIDString, PvEAdminLootPerms)) return;

            bool isOwner = fridge.OwnerID == player.userID;
            bool isAlly = IsAlliedWithPlayer(fridge.OwnerID, player.userID);
            bool isAuthed = IsAuthtoTC(fridge, player);

            if (!isOwner && !isAlly && !isAuthed)
            {
                NextFrame(() =>
                {
                    player.inventory.loot.Clear();
                    player.inventory.loot.SendImmediate();
                });
                ShowWarningLoot(player);
            }
        }

        private void OnItemDropped(Item item, BaseEntity entity)
        {
            if (!rulesData.lootRules.useLP) return;

            if (item.info.itemid == -907422733 || item.info.itemid == 2068884361)
            {
                if (entity.OwnerID == 0)
                {
                    entity.OwnerID = item.GetOwnerPlayer()?.userID ?? 0;
                }
            }
        }

        private object OnItemPickup(Item item, BasePlayer player)
        {
            if (player == null || item == null) return null;

            if (!rulesData.lootRules.useLP) return null;
            if (rulesData.lootRules.adminCanLootAll && permission.UserHasPermission(player.UserIDString, PvEAdminLootPerms)) return null;

            BaseEntity entity = item?.GetWorldEntity();

            if (entity != null && entity.OwnerID != 0)
            {
                if ((item.info.itemid == -907422733 || item.info.itemid == 2068884361) && !player.IsNpc && player.userID.IsSteamId())
                {
                    if (entity.OwnerID == player.userID || IsAlliedWithPlayer(entity.OwnerID, player.userID) || PlayersIsInZone(player))
                    {
                        return null;
                    }

                    ShowWarningLoot(player);
                    return false;
                }
            }

            return null;
        }

        private object OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (!rulesData.lootRules.useLP) return null;
            if (player == null || entity == null) return null;
            if (entity.OwnerID == 0) return null;
            if (config.excludedEntitys.Count > 0 && config.excludedEntitys.Contains(entity.ShortPrefabName)) return null;

            if (rulesData.lootRules.adminCanLootAll && permission.UserHasPermission(player.UserIDString, PvEAdminLootPerms)) return null;

            Item item = entity?.GetItem();
            if (player != null && item != null && entity.OwnerID != 0 && (item.info.itemid == -907422733 || item.info.itemid == 2068884361) && !player.IsNpc && player.userID.IsSteamId())
            {
                if (entity.OwnerID != player.userID && !IsAlliedWithPlayer(entity.OwnerID, player.userID))
                {
                    NextFrame(() =>
                    {
                        player.inventory.loot.Clear();
                        player.inventory.loot.SendImmediate();
                    });

                    ShowWarningLoot(player);
                }
            }
            
            return null;
        }


        private object CanLootEntity(BasePlayer player, LootableCorpse corpse)
        {
            if (!rulesData.lootRules.useLP) return null;
            if (player == null || corpse == null) return null;
            if (rulesData.lootRules.adminCanLootAll && permission.UserHasPermission(player.UserIDString, PvEAdminLootPerms)) return null;
            if (corpse.playerSteamID < 76561197960265728L || corpse.playerSteamID == player.userID || (rulesData.lootRules.teamsAccessLoot && IsAlliedWithPlayer(corpse.playerSteamID, player.userID))) return null;
            if (rulesData.lootRules.pclpvpzones)
            {
                if (ZoneManagerCheck(player, corpse))
                {
                    return null;
                }
            }
            ShowWarningLoot(player);
            return false;
        }
        private object CanLootEntity(BasePlayer player, DroppedItemContainer container)
        {
            if (!rulesData.lootRules.useLP) return null;
            if (player == null || container == null) return null;
            if (container.playerSteamID == 0) return null;
            if (rulesData.lootRules.adminCanLootAll && permission.UserHasPermission(player.UserIDString, PvEAdminLootPerms)) return null;

            if (container.name.Contains("item_drop_backpack"))
            {
                if (ZoneManager != null && rulesData.lootRules.pclpvpzones && ZoneManagerCheck(player, container))
                {
                    return null;
                }
                if (container.playerSteamID < 76561197960265728L || container.playerSteamID == player.userID || (rulesData.lootRules.teamsAccessLoot && IsAlliedWithPlayer(container.OwnerID, player.userID)))
                    return null;
                ShowWarningLoot(player);
                return false;
            }

            return null;
        }
        private object CanLootEntity(BasePlayer player, StorageContainer container)
        {
            if (!rulesData.lootRules.useLP) return null;
            if (player == null || container == null) return null;
            BaseEntity entity = container as BaseEntity;
            BaseEntity childentity = entity;
            entity = CheckParent(entity);

            if (config.excludedEntitys.Count > 0 && config.excludedEntitys.Contains(entity.ShortPrefabName)) return null;

            if (IsVendingOpen(player, entity) || IsDropBoxOpen(player, entity)) return null;

            if (rulesData.lootRules.adminCanLootAll && permission.UserHasPermission(player.UserIDString, PvEAdminLootPerms)) return null;

            if (entity.OwnerID == player.userID 
                || (rulesData.lootRules.teamsAccessLoot && IsAlliedWithPlayer(entity.OwnerID, player.userID)) 
                || IsAuthtoTC(entity, player) 
                || (entity is SupplyDrop drop && drop.OwnerID != 0 && rulesData.lootRules.anyoneOpenSupplyDrop))
            {
                return null;
            }

            if (entity.OwnerID != player.userID && !IsAlliedWithPlayer(entity.OwnerID, player.userID) && entity.OwnerID != 0)
            {
                ShowWarningLoot(player);
                return false;
            }

            return null;
        }

        private object CanLootPlayer(BasePlayer target, BasePlayer player)
        {
            if (!rulesData.lootRules.useLP) return null;
            if (player == null || target == null) return null;
            if (target.userID == 0) return null;
            if (rulesData.lootRules.adminCanLootAll && permission.UserHasPermission(player.UserIDString, PvEAdminLootPerms)) return null;

            if (target.userID == player.userID || (rulesData.lootRules.teamsAccessLoot && IsAlliedWithPlayer(target.userID, player.userID))) return null;
            if (rulesData.lootRules.pclpvpzones)
            {
                if (ZoneManagerCheck(player, target))
                {
                    return null;
                }
            }
            ShowWarningLoot(player);
            return false;
        }

        private object CanPickupEntity(BasePlayer player, BaseCombatEntity ent)
        {
            if (!rulesData.lootRules.useLP) return null;
            if (player == null || ent == null) return null;
            BaseEntity entity = ent as BaseEntity;
            if (entity.OwnerID == 0) return null;
            if (rulesData.lootRules.adminCanLootAll && permission.UserHasPermission(player.UserIDString, PvEAdminLootPerms)) return null;

            if (entity.OwnerID == player.userID || (rulesData.lootRules.teamsAccessLoot && IsAlliedWithPlayer(entity.OwnerID, player.userID)) || IsAuthtoTC(entity, player)) return null;

            if (ent.OwnerID != 0 && entity.OwnerID != player.userID && !IsAlliedWithPlayer(entity.OwnerID, player.userID))
            {
                ShowWarningLoot(player);
                return false;
            }

            return null;
        }

        private object CanAdministerVending(BasePlayer player, VendingMachine vending)
        {
            if (!rulesData.lootRules.useLP) return null;
            if (player == null || vending == null) return null;
            if (vending.OwnerID == 0) return null;
            if (rulesData.lootRules.adminCanLootAll && permission.UserHasPermission(player.UserIDString, PvEAdminLootPerms)) return null;

            if (vending.OwnerID == player.userID || (rulesData.lootRules.teamsAccessLoot && IsAlliedWithPlayer(vending.OwnerID, player.userID))) return null;

            ShowWarningLoot(player);
            return false;
        }

        private object OnOvenToggle(BaseOven oven, BasePlayer player)
        {
            if (!rulesData.lootRules.useLP) return null;
            if (player == null || oven == null) return null;
            if (oven.OwnerID == 0) return null;
            if (rulesData.lootRules.adminCanLootAll && permission.UserHasPermission(player.UserIDString, PvEAdminLootPerms)) return null;

            if (oven.OwnerID == player.userID || (rulesData.lootRules.teamsAccessLoot && IsAlliedWithPlayer(oven.OwnerID, player.userID))) return null;

            ShowWarningLoot(player);
            return false;

        }

        private BaseEntity CheckParent(BaseEntity entity)
        {
            if (entity.HasParent())
            {
                BaseEntity parententity = entity.GetParentEntity();
                if (parententity is MiningQuarry)
                {
                    entity.OwnerID = parententity.OwnerID;
                    entity = parententity;
                }
            }
            return entity;
        }

        private bool IsAuthtoTC(BaseEntity entity, BasePlayer player)
        {
            //BaseEntity entity = ent as BaseEntity;
            BuildingPrivlidge bPrev = player.GetBuildingPrivilege(new OBB(entity.transform.position, entity.transform.rotation, entity.bounds));
            BuildingPrivlidge noPrev = entity.GetBuildingPrivilege();
            if (bPrev == null)
            {
                return false;
            }
            else
            {
                if (bPrev.IsAuthed(player)) return true;
            }
            return false;
        }

        private bool IsVendingOpen(BasePlayer player, BaseEntity entity)
        {
            if (entity is VendingMachine)
            {
                VendingMachine shopFront = entity as VendingMachine;
                if (shopFront.PlayerInfront(player)) return true;
                return false;
            }
            return false;
        }
        private bool IsDropBoxOpen(BasePlayer player, BaseEntity entity)
        {
            if (entity is DropBox)
            {
                DropBox dropboxFront = entity as DropBox;
                if (dropboxFront.PlayerInfront(player)) return true;
                return false;
            }
            return false;
        }

        private bool ZoneManagerCheck(BasePlayer player, BaseEntity entity)
        {
            List<string> playerZones = GetAllZones(player);
            List<string> entityZones = GetAllZones(entity);

            if (CheckExclusion(playerZones, entityZones))
            {
                return true;
            }
            return false;
        }

        private bool CheckRackBox(BasePlayer player, BaseEntity entity)
        {
            if (entity == null || player == null) return true;
            if (entity.OwnerID == 0) return true;
            if (rulesData.lootRules.adminCanLootAll && permission.UserHasPermission(player.UserIDString, PvEAdminLootPerms)) return true;
            if (entity.OwnerID == player.userID || (rulesData.lootRules.teamsAccessLoot && IsAlliedWithPlayer(entity.OwnerID, player.userID)) || IsAuthtoTC(entity, player)) return true;

            return false;
        }

        private object OnRackedWeaponMount(Item item, BasePlayer player, WeaponRack rack)
        {
            if (!rulesData.lootRules.useLP) return null;
            if (rack == null || player == null || item == null) return null;

            BaseEntity entity = rack as BaseEntity;
            if (CheckRackBox(player, entity))
            {
                return null;
            }

            ShowWarningLoot(player);
            return false;
        }

        private object OnRackedWeaponSwap(Item item, WeaponRackSlot weaponTaking, BasePlayer player, WeaponRack rack)
        {
            if (!rulesData.lootRules.useLP) return null;
            if (rack == null || player == null || item == null) return null;

            BaseEntity entity = rack as BaseEntity;
            if (CheckRackBox(player, entity))
            {
                return null;
            }

            ShowWarningLoot(player);
            return false;
        }

        private object OnRackedWeaponTake(Item item, BasePlayer player, WeaponRack rack)
        {
            if (!rulesData.lootRules.useLP) return null;
            if (rack == null || player == null || item == null) return null;

            BaseEntity entity = rack as BaseEntity;
            if (CheckRackBox(player, entity))
            {
                return null;
            }

            ShowWarningLoot(player);
            return false;
        }

        private object OnRackedWeaponUnload(Item item, BasePlayer player, WeaponRack rack)
        {
            if (!rulesData.lootRules.useLP) return null;
            if (rack == null || player == null || item == null) return null;

            BaseEntity entity = rack as BaseEntity;
            if (CheckRackBox(player, entity))
            {
                return null;
            }

            ShowWarningLoot(player);
            return false;
        }

        private object OnRackedWeaponLoad(Item item, ItemDefinition ammoItem, BasePlayer player, WeaponRack rack)
        {
            if (!rulesData.lootRules.useLP) return null;
            if (rack == null || player == null || item == null) return null;

            BaseEntity entity = rack as BaseEntity;
            if (CheckRackBox(player, entity))
            {
                return null;
            }

            ShowWarningLoot(player);
            return false;
        }

        private void OnExplosiveThrown(BasePlayer player, SupplySignal drop, ThrownWeapon weapon) => OnExplosiveDropped(player, drop, weapon);

        private void OnExplosiveDropped(BasePlayer player, SupplySignal drop, ThrownWeapon weapon)
        {
            NextTick(() =>
            {
                if (drop == null)
                    return;
                if (!(drop is SupplySignal))
                    return;
                if (!rulesData.lootRules.useLP)
                    return;
                if (rulesData.lootRules.anyoneOpenSupplyDrop)
                    return;

                drop.OwnerID = player.userID;
                drop.skinID = SupplyDropID;
            });
        }

        private void OnCargoPlaneSignaled(CargoPlane plane, SupplySignal drop)
        {
            if (drop.skinID != SupplyDropID) return;

            plane.OwnerID = drop.OwnerID;
            plane.skinID = SupplyDropID;
        }

        private void OnSupplyDropDropped(SupplyDrop drop, CargoPlane plane)
        {
            if (plane.skinID != SupplyDropID)
                return;

            drop.OwnerID = plane.OwnerID;
            drop.skinID = SupplyDropID;
        }

        private const ulong SupplyDropID = 76561199137756632;

        /// <summary>
        ///  Credit to rightful owner of the code: Kaucsenta
        ///  Code edited and adapted to fit this plugin.
        ///  https://umod.org/plugins/anti-ladder-and-twig
        /// </summary>

        private object CanBuild(Planner planner, Construction prefab, Construction.Target target)
        {
            if (!rulesData.lootRules.useLP) return null;

            BasePlayer player = planner.GetOwnerPlayer();
            if (player == null) return null;
            if (permission.UserHasPermission(player.UserIDString, PvEAdminLootPerms)) return null;

            bool raidbaseFlag = true;
            bool abandonedBaseFlag = false;

            if (IsSpecialPrefab(prefab) && target.entity != null)
            {
                if (IsValidEntity(target.entity))
                {
                    if (target.entity.GetBuildingPrivilege() != null)
                    {
                        foreach (var tmpplayer in target.entity.GetBuildingPrivilege().authorizedPlayers)
                        {
                            if (covalence.Players.FindPlayerById(tmpplayer.ToString()) != null)
                            {
                                // If there is any valid player authorized, then it is not a raidable base
                                raidbaseFlag = false;
                            }

                            if (tmpplayer == target.player.userID || IsAlliedWithPlayer(tmpplayer, target.player.userID))
                            {
                                return null; // Player is authorized, allow building
                            }
                        }

                        if (System.Convert.ToBoolean(AbandonedBases?.Call("EventTerritory", target.position)))
                        {
                            // If the entity is abandoned, then ladder can be placed like raidable bases
                            abandonedBaseFlag = true;
                        }

                        if (raidbaseFlag || abandonedBaseFlag)
                        {
                            return null; // It is a raidable base or an abandoned base, allow building
                        }
                        else
                        {
                            SendChatMessage(player, config.displayNotify.LPChatMessage);
                            return false; // Do not allow building
                        }
                    }
                }
            }

            return null; // Default case, return null
        }

        private bool IsSpecialPrefab(Construction prefab)
        {
            return prefab.fullName.Contains("floor.prefab") || prefab.fullName.Contains("ladder.wooden.wall") || prefab.fullName.Contains("floor.frame") || prefab.fullName.Contains("floor.triangle.frame")
                || prefab.fullName.Contains("floor.triangle") || prefab.fullName.Contains("shopfront") || prefab.fullName.Contains("shutter") || prefab.fullName.Contains("neon") || prefab.fullName.Contains("sign");
        }

        private bool IsValidEntity(BaseEntity entity)
        {
            return entity is SimpleBuildingBlock || entity is BuildingBlock || entity is ShopFront || entity is StabilityEntity || entity is NeonSign || entity is Signage;
        }



        #endregion

        #region UIs

        #region Toastify

        private void SendToastifyMessage(BasePlayer player, string message)
        {
            if (!Toastify) return;
            if (NotifyACool.Contains(player.userID)) return;

            Toastify.CallHook("SendToast", player, "error", "<color=yellow>WARNING!</color>", message, 10f);
            NotifyACool.Add(player.userID);
            timer.In(10, () => { NotifyACool.Remove(player.userID); });
        }

        private void SendNotifyMessage(BasePlayer player, string message)
        {
            if (!Notify) return;
            if (NotifyACool.Contains(player.userID)) return;

            Notify.CallHook("SendNotify", player, 0, message);
            NotifyACool.Add(player.userID);
            timer.In(10, () => { NotifyACool.Remove(player.userID); });
        }

        #endregion

        #region PVE or PvP info & Loot Ui

        private HashSet<ulong> NotifyACool = new HashSet<ulong>();
        private HashSet<ulong> NotifyMCool = new HashSet<ulong>();
        private HashSet<ulong> WarningLoot = new HashSet<ulong>();
        private HashSet<ulong> WarningMLoot = new HashSet<ulong>();
        private const string newUI = "WarningUI";
        private const string wLootC = "WarningLoot";

        // Helper method to check cooldowns
        private bool IsCooldownActive(HashSet<ulong> cooldownList, ulong userId, float cooldownTime)
        {
            if (cooldownList.Contains(userId)) return true;
            cooldownList.Add(userId);
            timer.In(cooldownTime, () => cooldownList.Remove(userId));
            return false;
        }

        // Show general warning alert
        private void ShowWarningAlert(BasePlayer player)
        {
            if (player == null) return;

            // Display toast if needed
            if (config.displayNotify.showDamageMTip)
            {
                player.SendConsoleCommand("gametip.showtoast", "5", config.displayNotify.showDamageMTypeMessageTip);
            }

            // Show UI and chat warnings based on configuration
            if (!config.displayNotify.showDamageM) return;

            if (config.displayNotify.showDamageMType == "Both(Chat & UI)")
            {
                ShowUIWarning(player);
                ShowWarningMessage(player);
            }
            else if (config.displayNotify.showDamageMType == "UI")
            {
                ShowUIWarning(player);
            }
            else if (config.displayNotify.showDamageMType == "Chat")
            {
                ShowWarningMessage(player);
            }
            else if (config.displayNotify.showDamageMType == "Toastify")
            {
                SendToastifyMessage(player, config.displayNotify.showDamageMTypeMessage);
            }
            else if (config.displayNotify.showDamageMType == "Notify")
            {
                SendNotifyMessage(player, config.displayNotify.showDamageMTypeMessage);
            }
        }

        // Display warning message in chat with cooldown
        private void ShowWarningMessage(BasePlayer player)
        {
            if (player == null || IsCooldownActive(NotifyMCool, player.userID, 10f)) return;

            SendChatMessage(player, config.displayNotify.showDamageMTypeMessage);
        }

        // Show the UI warning with cooldown
        private void ShowUIWarning(BasePlayer player)
        {
            if (NotifyACool.Contains(player.userID)) return;

            CuiElementContainer container = new CuiElementContainer
            {
                {
                    new CuiElement
                    {
                        Name = newUI,
                        Parent = "Overlay",
                        Components = {
                            new CuiRawImageComponent { Png = GetImage(config.displayNotify.warningUI.WarningImgUrl) },
                            new CuiRectTransformComponent {
                                AnchorMin = config.displayNotify.warningUI.WarningAMin,
                                AnchorMax = config.displayNotify.warningUI.WarningAMax
                            }
                        }
                    }
                }
            };

            CuiHelper.DestroyUi(player, newUI);
            NotifyACool.Add(player.userID);
            CuiHelper.AddUi(player, container);

            timer.In(10, () => RemoveUIWarning(player));
        }

        // Remove UI after the cooldown
        private void RemoveUIWarning(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, newUI);
            NotifyACool.Remove(player.userID);
        }

        // Show Looting Warning UI with cooldown
        private void ShowWarningLoot(BasePlayer player)
        {
            if (player == null || IsCooldownActive(WarningLoot, player.userID, 5f)) return;

            if (config.displayNotify.LPMessagesOnTip)
            {
                player.SendConsoleCommand("gametip.showtoast", "5", config.displayNotify.LPChatMessageTIP);
            }

            if (!config.displayNotify.LPMessagesOn) return;

            if (config.displayNotify.LPType == "Both(Chat & UI)")
            {
                UIWarningLoot(player);
                ShowLootWarningMessage(player);
            }
            else if (config.displayNotify.LPType == "UI")
            {
                UIWarningLoot(player);
            }
            else if (config.displayNotify.LPType == "Chat")
            {
                ShowLootWarningMessage(player);
            }
            else if (config.displayNotify.LPType == "Toastify")
            {
                SendToastifyMessage(player, config.displayNotify.LPChatMessageUI);
            }
            else if (config.displayNotify.LPType == "Notify")
            {
                SendNotifyMessage(player, config.displayNotify.LPChatMessageUI);
            }
        }

        // Display loot warning message in chat with cooldown
        private void ShowLootWarningMessage(BasePlayer player)
        {
            if (player == null || IsCooldownActive(WarningMLoot, player.userID, 10f)) return;

            SendChatMessage(player, config.displayNotify.LPChatMessage);
        }

        // Show Loot Warning UIw
        private void UIWarningLoot(BasePlayer player)
        {
            if (WarningLoot.Contains(player.userID)) return;

            CuiElementContainer container = new CuiElementContainer
            {
                {
                    new CuiElement
                    {
                        Name = wLootC,
                        Parent = "Overlay",
                        Components = {
                            new CuiRawImageComponent { Png = GetImage(config.displayNotify.lPUISetting.WarningImgUrl) },
                            new CuiRectTransformComponent {
                                AnchorMin = config.displayNotify.lPUISetting.WarningAMin,
                                AnchorMax = config.displayNotify.lPUISetting.WarningAMax
                            }
                        }
                    }
                }
            };

            CuiHelper.DestroyUi(player, wLootC);
            WarningLoot.Add(player.userID);
            CuiHelper.AddUi(player, container);

            timer.In(5, () => RemoveWarningLoot(player));
        }

        // Remove Loot Warning UI after the cooldown
        private void RemoveWarningLoot(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, wLootC);
            WarningLoot.Remove(player.userID);
        }

        // Show PVE UI
        private void ShowPVEUI(BasePlayer player)
        {
            if (!config.displayNotify.showPvEOverlay) return;

            CuiElementContainer container = new CuiElementContainer
            {
                {
                    new CuiPanel
                    {
                        RectTransform = {
                            AnchorMin = config.displayNotify.pUISetting.pepAMin,
                            AnchorMax = config.displayNotify.pUISetting.pepAMax,
                            OffsetMin = config.displayNotify.pUISetting.pepOMin,
                            OffsetMax = config.displayNotify.pUISetting.pepOMax
                        },
                        Image = { Color = "0.3 0.8 0.1 0.8", FadeIn = 1f }
                    },
                    "Hud", "PVEUI"
                }
            };

            container.Add(new CuiLabel
            {
                RectTransform = {
                    AnchorMin = config.displayNotify.pUISetting.pepAMin,
                    AnchorMax = config.displayNotify.pUISetting.pepAMax,
                    OffsetMin = config.displayNotify.pUISetting.pepOMin,
                    OffsetMax = config.displayNotify.pUISetting.pepOMax
                },
                Text = { Text = "PVE", Color = "1 1 1 1", Align = TextAnchor.MiddleCenter, FontSize = 13, Font = "robotocondensed-bold.ttf" },
                FadeOut = 1f
            }, "Hud", "textpve");

            RemovePVEPanel(player);
            CuiHelper.AddUi(player, container);
        }

        // Remove PVE UI
        private void RemovePVEPanel(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "textpve");
            CuiHelper.DestroyUi(player, "PVEUI");
        }

        // Show PVP UI
        private void ShowPVPUi(BasePlayer player)
        {
            if (!config.displayNotify.showPvEOverlay) return;

            CuiElementContainer container = new CuiElementContainer
            {
                {
                    new CuiPanel
                    {
                        RectTransform = {
                            AnchorMin = config.displayNotify.pUISetting.pepAMin,
                            AnchorMax = config.displayNotify.pUISetting.pepAMax,
                            OffsetMin = config.displayNotify.pUISetting.pepOMin,
                            OffsetMax = config.displayNotify.pUISetting.pepOMax
                        },
                        Image = { Color = "0.8 0.2 0.2 0.8", FadeIn = 1f }
                    },
                    "Hud", "PVPUI"
                }
            };

            container.Add(new CuiLabel
            {
                RectTransform = {
                    AnchorMin = config.displayNotify.pUISetting.pepAMin,
                    AnchorMax = config.displayNotify.pUISetting.pepAMax,
                    OffsetMin = config.displayNotify.pUISetting.pepOMin,
                    OffsetMax = config.displayNotify.pUISetting.pepOMax
                },
                Text = { Text = "PVP", Color = "1 1 1 1", Align = TextAnchor.MiddleCenter, FontSize = 13, Font = "robotocondensed-bold.ttf" },
                FadeOut = 1f
            }, "Hud", "textpvp");

            RemovePVPPanel(player);
            CuiHelper.AddUi(player, container);
        }

        // Remove PVP UI
        private void RemovePVPPanel(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "textpvp");
            CuiHelper.DestroyUi(player, "PVPUI");

        }

        #endregion

        #region Schedules UI
        private void SimplePVEMainGui(BasePlayer player)
        {
            var container = new CuiElementContainer();

            container.Add(new CuiElement
            {
                Name = "SimplePVECUI",
                Parent = "Overlay",
                Components =
                {
                    new CuiRawImageComponent { Png = GetImage("https://i.postimg.cc/SxC1PDyF/Untitled-design.png") },
                    new CuiRectTransformComponent { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-515.949 -619.479", OffsetMax = "515.941 -68.521" }
                }
            });

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-515.939 -107.605", OffsetMax = "515.961 0" }
            }, "SimplePVECUI", "TopBarPanel");

            container.Add(new CuiPanel
            {
                Image = { Color = "0.1372549 0.1529412 0.1372549 1" }, //0.1411765 0.1372549 0.1372549 1
                RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-515.963 -54.839", OffsetMax = "515.938 0.001" }
            }, "TopBarPanel", "TitlePanel");

            container.Add(new CuiElement
            {
                Name = "Title",
                Parent = "TitlePanel",
                Components = {
                    new CuiTextComponent { Text = "SimplePVE", Font = "robotocondensed-bold.ttf", FontSize = 20, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-257.589 -27.42", OffsetMax = "257.589 27.42" }
                }
            });

            container.Add(new CuiButton
            {
                Button = { Color = "1 0 0 0.6666667", Command = "spvecui closeall" },
                Text = { Text = "X", Font = "robotocondensed-regular.ttf", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = "-38.9 -42.42", OffsetMax = "-3.9 -12.42" }
            }, "TitlePanel", "CloseBtn");

            container.Add(new CuiButton
            {
                Button = { Color = "0.1294118 0.5333334 0.2196078 1", Command = "spvecui pverules" },
                Text = { Text = "PVE Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "103.851 7.5", OffsetMax = "336.149 42.5" }
            }, "TopBarPanel", "PVERules");

            container.Add(new CuiButton
            {
                Button = { Color = "0.1294118 0.5333334 0.2196078 1", Command = "spvecui pveschedules" },
                Text = { Text = "Schedules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "399.791 7.5", OffsetMax = "632.089 42.5" }
            }, "TopBarPanel", "Schedules");

            CuiHelper.DestroyUi(player, "SimplePVECUI");
            CuiHelper.AddUi(player, container);
        }

        private void PVERulesUI(BasePlayer player, PVERules rules)
        {
            var container = new CuiElementContainer();

            if (rules == PVERules.GRules)
            {
                container.Add(new CuiPanel
                {
                    CursorEnabled = true,
                    Image = { Color = "1 1 1 0" },
                    RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "-515.939 0.005", OffsetMax = "515.961 443.355" }
                }, "SimplePVECUI", "MainShit");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1372549 0.1529412 0.1372549 1", Command = "spvecui grules" },//0.1019608 0.09803922 0.1019608 1
                    Text = { Text = "General Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "1 -35", OffsetMax = "172.984 0" }
                }, "MainShit", "GeneralRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui prules" },//0.2941177 0.2941177 0.2941177 1
                    Text = { Text = "Player Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "172.988 -35", OffsetMax = "344.972 0" }
                }, "MainShit", "PlayerRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui nrules" },
                    Text = { Text = "NPC Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "344.968 -35", OffsetMax = "516.952 0" }
                }, "MainShit", "NPCRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui erules" },
                    Text = { Text = "Entity Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "516.948 -35", OffsetMax = "688.932 0" }
                }, "MainShit", "EntityRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui lrules" },
                    Text = { Text = "Loot Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "688.928 -35", OffsetMax = "860.912 0" }
                }, "MainShit", "LootRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui zrules" },
                    Text = { Text = "ZoneManager", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "860.908 -35", OffsetMax = "1032.892 0" }
                }, "MainShit", "ZoneManager");

                int yOffset = 0;
                int gapOffset = 0;
                int panelsPerColumn = 14; // Number of panels per column
                int panelsCount = 0;
                int columnCount = 0;
                int columnGap = 520; // Gap between columns
                foreach (var property in typeof(RulesData.GRules).GetProperties())
                {
                    string propertyName = property.Name;
                    var nProperty = property.GetCustomAttribute<JsonPropertyAttribute>();
                    string name = nProperty.PropertyName;//
                    object propertyValue = property.GetValue(rulesData.gRules);
                    container.Add(new CuiPanel
                    {
                        CursorEnabled = true,
                        Image = { Color = ColorOdd(panelsCount) },//0.1019608 0.09803922 0.1019608 1
                        RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{-0.005 + gapOffset} -{74.5 + yOffset}", OffsetMax = $"{511.505 + gapOffset} -{49.5 + yOffset}" }
                    }, "MainShit", $"Template_{propertyName}");
                    container.Add(new CuiElement
                    {
                        Name = $"Label_{propertyName}",
                        Parent = $"Template_{propertyName}",
                        Components = {
                        new CuiTextComponent { Text = $"{name}", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
                        new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "24.097 -12.5", OffsetMax = "512.211 12.5" }
                    }
                    });
                    container.Add(new CuiButton
                    {
                        Button = { Color = "0 0 0 0", Command = $"grulesSetting {propertyName}" },
                        Text = { Text = $"{propertyValue}", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = (bool)propertyValue ? "0 0.5019608 0 1" : "0.5568628 0.1960784 0.1960784 1" },
                        RectTransform = { AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-127.788 -12.5", OffsetMax = "0 12.5" }
                    }, $"Template_{propertyName}", $"State_{propertyName}");
                    yOffset += 28;
                    panelsCount++;
                    // Check if it's time to start a new column
                    if (panelsCount >= panelsPerColumn)
                    {
                        yOffset = 0;
                        panelsCount = 0;
                        columnCount++;
                        // Move to the next column with the specified gap
                        gapOffset += columnCount * columnGap;
                    }
                }

            }
            else if (rules == PVERules.PRules)
            {
                container.Add(new CuiPanel
                {
                    CursorEnabled = true,
                    Image = { Color = "1 1 1 0" },
                    RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "-515.939 0.005", OffsetMax = "515.961 443.355" }
                }, "SimplePVECUI", "MainShit");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui grules" },
                    Text = { Text = "General Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "1 -35", OffsetMax = "172.984 0" }
                }, "MainShit", "GeneralRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1372549 0.1529412 0.1372549 1", Command = "spvecui prules" },
                    Text = { Text = "Player Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "172.988 -35", OffsetMax = "344.972 0" }
                }, "MainShit", "PlayerRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui nrules" },
                    Text = { Text = "NPC Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "344.968 -35", OffsetMax = "516.952 0" }
                }, "MainShit", "NPCRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui erules" },
                    Text = { Text = "Entity Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "516.948 -35", OffsetMax = "688.932 0" }
                }, "MainShit", "EntityRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui lrules" },
                    Text = { Text = "Loot Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "688.928 -35", OffsetMax = "860.912 0" }
                }, "MainShit", "LootRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui zrules" },
                    Text = { Text = "ZoneManager", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "860.908 -35", OffsetMax = "1032.892 0" }
                }, "MainShit", "ZoneManager");

                int yOffset = 0;
                int gapOffset = 0;
                int panelsPerColumn = 14; // Number of panels per column
                int panelsCount = 0;
                int columnCount = 0;
                int columnGap = 520; // Gap between columns
                foreach (var property in typeof(RulesData.PlayerRules).GetProperties())
                {
                    string propertyName = property.Name;
                    var nProperty = property.GetCustomAttribute<JsonPropertyAttribute>();
                    string name = nProperty.PropertyName;
                    object propertyValue = property.GetValue(rulesData.playerRules);
                    container.Add(new CuiPanel
                    {
                        Image = { Color = ColorOdd(panelsCount) },
                        RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{-0.005 + gapOffset} -{74.5 + yOffset}", OffsetMax = $"{511.505 + gapOffset} -{49.5 + yOffset}" }
                    }, "MainShit", $"Template_{propertyName}");
                    container.Add(new CuiElement
                    {
                        Name = $"Label_{propertyName}",
                        Parent = $"Template_{propertyName}",
                        Components = {
                        new CuiTextComponent { Text = $"{name}", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
                        new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "24.097 -12.5", OffsetMax = "512.211 12.5" }
                    }
                    });
                    container.Add(new CuiButton
                    {
                        Button = { Color = "0 0 0 0", Command = $"prulesSetting {propertyName}" },
                        Text = { Text = $"{propertyValue}", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = (bool)propertyValue ? "0 0.5019608 0 1" : "0.5568628 0.1960784 0.1960784 1" },
                        RectTransform = { AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-127.788 -12.5", OffsetMax = "0 12.5" }
                    }, $"Template_{propertyName}", $"State_{propertyName}");
                    yOffset += 28;
                    panelsCount++;
                    // Check if it's time to start a new column
                    if (panelsCount >= panelsPerColumn)
                    {
                        yOffset = 0;
                        panelsCount = 0;
                        columnCount++;
                        // Move to the next column with the specified gap
                        gapOffset += columnCount * columnGap;
                    }
                }

            }
            else if (rules == PVERules.NRules)
            {
                container.Add(new CuiPanel
                {
                    CursorEnabled = true,
                    Image = { Color = "1 1 1 0" },
                    RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "-515.939 0.005", OffsetMax = "515.961 443.355" }
                }, "SimplePVECUI", "MainShit");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui grules" },
                    Text = { Text = "General Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "1 -35", OffsetMax = "172.984 0" }
                }, "MainShit", "GeneralRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui prules" },
                    Text = { Text = "Player Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "172.988 -35", OffsetMax = "344.972 0" }
                }, "MainShit", "PlayerRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1372549 0.1529412 0.1372549 1", Command = "spvecui nrules" },
                    Text = { Text = "NPC Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "344.968 -35", OffsetMax = "516.952 0" }
                }, "MainShit", "NPCRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui erules" },
                    Text = { Text = "Entity Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "516.948 -35", OffsetMax = "688.932 0" }
                }, "MainShit", "EntityRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui lrules" },
                    Text = { Text = "Loot Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "688.928 -35", OffsetMax = "860.912 0" }
                }, "MainShit", "LootRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui zrules" },
                    Text = { Text = "ZoneManager", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "860.908 -35", OffsetMax = "1032.892 0" }
                }, "MainShit", "ZoneManager");

                int yOffset = 0;
                int gapOffset = 0;
                int panelsPerColumn = 14; // Number of panels per column
                int panelsCount = 0;
                int columnCount = 0;
                int columnGap = 520; // Gap between columns
                foreach (var property in typeof(RulesData.NpcRules).GetProperties())
                {
                    string propertyName = property.Name;
                    var nProperty = property.GetCustomAttribute<JsonPropertyAttribute>();
                    string name = nProperty.PropertyName;//
                    object propertyValue = property.GetValue(rulesData.npcRules);
                    container.Add(new CuiPanel
                    {
                        CursorEnabled = true,
                        Image = { Color = ColorOdd(panelsCount) },
                        RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{-0.005 + gapOffset} -{74.5 + yOffset}", OffsetMax = $"{511.505 + gapOffset} -{49.5 + yOffset}" }
                    }, "MainShit", $"Template_{propertyName}");
                    container.Add(new CuiElement
                    {
                        Name = $"Label_{propertyName}",
                        Parent = $"Template_{propertyName}",
                        Components = {
        new CuiTextComponent { Text = $"{name}", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
        new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "24.097 -12.5", OffsetMax = "512.211 12.5" }
    }
                    });
                    container.Add(new CuiButton
                    {
                        Button = { Color = "0 0 0 0", Command = $"nrulesSetting {propertyName}" },
                        Text = { Text = $"{propertyValue}", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = (bool)propertyValue ? "0 0.5019608 0 1" : "0.5568628 0.1960784 0.1960784 1" },
                        RectTransform = { AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-127.788 -12.5", OffsetMax = "0 12.5" }
                    }, $"Template_{propertyName}", $"State_{propertyName}");
                    yOffset += 28;
                    panelsCount++;
                    // Check if it's time to start a new column
                    if (panelsCount >= panelsPerColumn)
                    {
                        yOffset = 0;
                        panelsCount = 0;
                        columnCount++;
                        // Move to the next column with the specified gap
                        gapOffset += columnCount * columnGap;
                    }
                }

            }
            else if (rules == PVERules.ERules)
            {
                container.Add(new CuiPanel
                {
                    CursorEnabled = true,
                    Image = { Color = "1 1 1 0" },
                    RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "-515.939 0.005", OffsetMax = "515.961 443.355" }
                }, "SimplePVECUI", "MainShit");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui grules" },
                    Text = { Text = "General Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "1 -35", OffsetMax = "172.984 0" }
                }, "MainShit", "GeneralRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui prules" },
                    Text = { Text = "Player Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "172.988 -35", OffsetMax = "344.972 0" }
                }, "MainShit", "PlayerRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui nrules" },
                    Text = { Text = "NPC Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "344.968 -35", OffsetMax = "516.952 0" }
                }, "MainShit", "NPCRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1372549 0.1529412 0.1372549 1", Command = "spvecui erules" },
                    Text = { Text = "Entity Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "516.948 -35", OffsetMax = "688.932 0" }
                }, "MainShit", "EntityRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui lrules" },
                    Text = { Text = "Loot Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "688.928 -35", OffsetMax = "860.912 0" }
                }, "MainShit", "LootRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui zrules" },
                    Text = { Text = "ZoneManager", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "860.908 -35", OffsetMax = "1032.892 0" }
                }, "MainShit", "ZoneManager");

                int yOffset = 0;
                int gapOffset = 0;
                int panelsPerColumn = 14; // Number of panels per column
                int panelsCount = 0;
                int columnCount = 0;
                int columnGap = 520; // Gap between columns
                foreach (var property in typeof(RulesData.EntityRules).GetProperties())
                {
                    string propertyName = property.Name;
                    var nProperty = property.GetCustomAttribute<JsonPropertyAttribute>();
                    string name = nProperty.PropertyName;//
                    object propertyValue = property.GetValue(rulesData.entityRules);
                    container.Add(new CuiPanel
                    {
                        CursorEnabled = true,
                        Image = { Color = ColorOdd(panelsCount) },
                        RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{-0.005 + gapOffset} -{74.5 + yOffset}", OffsetMax = $"{511.505 + gapOffset} -{49.5 + yOffset}" }
                    }, "MainShit", $"Template_{propertyName}");
                    container.Add(new CuiElement
                    {
                        Name = $"Label_{propertyName}",
                        Parent = $"Template_{propertyName}",
                        Components = {
        new CuiTextComponent { Text = $"{name}", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
        new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "24.097 -12.5", OffsetMax = "512.211 12.5" }
    }
                    });
                    container.Add(new CuiButton
                    {
                        Button = { Color = "0 0 0 0", Command = $"erulesSetting {propertyName}" },
                        Text = { Text = $"{propertyValue}", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = (bool)propertyValue ? "0 0.5019608 0 1" : "0.5568628 0.1960784 0.1960784 1" },
                        RectTransform = { AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-127.788 -12.5", OffsetMax = "0 12.5" }
                    }, $"Template_{propertyName}", $"State_{propertyName}");
                    yOffset += 28;
                    panelsCount++;
                    // Check if it's time to start a new column
                    if (panelsCount >= panelsPerColumn)
                    {
                        yOffset = 0;
                        panelsCount = 0;
                        columnCount++;
                        // Move to the next column with the specified gap
                        gapOffset += columnCount * columnGap;
                    }
                }

            }
            else if (rules == PVERules.LRules)
            {
                container.Add(new CuiPanel
                {
                    CursorEnabled = true,
                    Image = { Color = "1 1 1 0" },
                    RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "-515.939 0.005", OffsetMax = "515.961 443.355" }
                }, "SimplePVECUI", "MainShit");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui grules" },
                    Text = { Text = "General Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "1 -35", OffsetMax = "172.984 0" }
                }, "MainShit", "GeneralRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui prules" },
                    Text = { Text = "Player Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "172.988 -35", OffsetMax = "344.972 0" }
                }, "MainShit", "PlayerRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui nrules" },
                    Text = { Text = "NPC Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "344.968 -35", OffsetMax = "516.952 0" }
                }, "MainShit", "NPCRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui erules" },
                    Text = { Text = "Entity Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "516.948 -35", OffsetMax = "688.932 0" }
                }, "MainShit", "EntityRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1372549 0.1529412 0.1372549 1", Command = "spvecui lrules" },
                    Text = { Text = "Loot Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "688.928 -35", OffsetMax = "860.912 0" }
                }, "MainShit", "LootRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui zrules" },
                    Text = { Text = "ZoneManager", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "860.908 -35", OffsetMax = "1032.892 0" }
                }, "MainShit", "ZoneManager");

                int yOffset = 0;
                int gapOffset = 0;
                int panelsPerColumn = 14; // Number of panels per column
                int panelsCount = 0;
                int columnCount = 0;
                int columnGap = 520; // Gap between columns
                foreach (var property in typeof(RulesData.LootRules).GetProperties())
                {
                    string propertyName = property.Name;
                    var nProperty = property.GetCustomAttribute<JsonPropertyAttribute>();
                    string name = nProperty.PropertyName;//
                    object propertyValue = property.GetValue(rulesData.lootRules);
                    container.Add(new CuiPanel
                    {
                        CursorEnabled = true,
                        Image = { Color = ColorOdd(panelsCount) },
                        RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{-0.005 + gapOffset} -{74.5 + yOffset}", OffsetMax = $"{511.505 + gapOffset} -{49.5 + yOffset}" }
                    }, "MainShit", $"Template_{propertyName}");
                    container.Add(new CuiElement
                    {
                        Name = $"Label_{propertyName}",
                        Parent = $"Template_{propertyName}",
                        Components = {
        new CuiTextComponent { Text = $"{name}", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
        new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "24.097 -12.5", OffsetMax = "512.211 12.5" }
    }
                    });
                    container.Add(new CuiButton
                    {
                        Button = { Color = "0 0 0 0", Command = $"lrulesSetting {propertyName}" },
                        Text = { Text = $"{propertyValue}", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = (bool)propertyValue ? "0 0.5019608 0 1" : "0.5568628 0.1960784 0.1960784 1" },
                        RectTransform = { AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-127.788 -12.5", OffsetMax = "0 12.5" }
                    }, $"Template_{propertyName}", $"State_{propertyName}");
                    yOffset += 28;
                    panelsCount++;
                    // Check if it's time to start a new column
                    if (panelsCount >= panelsPerColumn)
                    {
                        yOffset = 0;
                        panelsCount = 0;
                        columnCount++;
                        // Move to the next column with the specified gap
                        gapOffset += columnCount * columnGap;
                    }
                }

            }
            else if (rules == PVERules.ZRules)
            {
                container.Add(new CuiPanel
                {
                    CursorEnabled = true,
                    Image = { Color = "1 1 1 0" },
                    RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "-515.939 0.005", OffsetMax = "515.961 443.355" }
                }, "SimplePVECUI", "MainShit");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui grules" },
                    Text = { Text = "General Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "1 -35", OffsetMax = "172.984 0" }
                }, "MainShit", "GeneralRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui prules" },
                    Text = { Text = "Player Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "172.988 -35", OffsetMax = "344.972 0" }
                }, "MainShit", "PlayerRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui nrules" },
                    Text = { Text = "NPC Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "344.968 -35", OffsetMax = "516.952 0" }
                }, "MainShit", "NPCRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui erules" },
                    Text = { Text = "Entity Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "516.948 -35", OffsetMax = "688.932 0" }
                }, "MainShit", "EntityRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1215686 0.1372549 0.1215686 1", Command = "spvecui lrules" },
                    Text = { Text = "Loot Rules", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "688.928 -35", OffsetMax = "860.912 0" }
                }, "MainShit", "LootRules");

                container.Add(new CuiButton
                {
                    Button = { Color = "0.1372549 0.1529412 0.1372549 1", Command = "spvecui zrules" },
                    Text = { Text = "ZoneManager", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "860.908 -35", OffsetMax = "1032.892 0" }
                }, "MainShit", "ZoneManager");

                int yOffset = 0;
                int gapOffset = 0;
                int panelsPerColumn = 14; // Number of panels per column
                int panelsCount = 0;
                int columnCount = 0;
                int columnGap = 520; // Gap between columns
                foreach (var property in typeof(RulesData.ZoneRules).GetProperties())
                {
                    string propertyName = property.Name;
                    var nProperty = property.GetCustomAttribute<JsonPropertyAttribute>();
                    string name = nProperty.PropertyName;//
                    object propertyValue = property.GetValue(rulesData.zoneRules);
                    container.Add(new CuiPanel
                    {
                        CursorEnabled = true,
                        Image = { Color = ColorOdd(panelsCount) },
                        RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{-0.005 + gapOffset} -{74.5 + yOffset}", OffsetMax = $"{511.505 + gapOffset} -{49.5 + yOffset}" }
                    }, "MainShit", $"Template_{propertyName}");
                    container.Add(new CuiElement
                    {
                        Name = $"Label_{propertyName}",
                        Parent = $"Template_{propertyName}",
                        Components = {
        new CuiTextComponent { Text = $"{name}", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
        new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "24.097 -12.5", OffsetMax = "512.211 12.5" }
    }
                    });
                    container.Add(new CuiButton
                    {
                        Button = { Color = "0 0 0 0", Command = $"zrulesSetting {propertyName}" },
                        Text = { Text = $"{propertyValue}", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = (bool)propertyValue ? "0 0.5019608 0 1" : "0.5568628 0.1960784 0.1960784 1" },
                        RectTransform = { AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-127.788 -12.5", OffsetMax = "0 12.5" }
                    }, $"Template_{propertyName}", $"State_{propertyName}");
                    yOffset += 28;
                    panelsCount++;
                    // Check if it's time to start a new column
                    if (panelsCount >= panelsPerColumn)
                    {
                        yOffset = 0;
                        panelsCount = 0;
                        columnCount++;
                        // Move to the next column with the specified gap
                        gapOffset += columnCount * columnGap;
                    }
                }

            }

            CuiHelper.DestroyUi(player, "MainShit");
            CuiHelper.AddUi(player, container);
        }

        private string ColorOdd(int number)
        {
            if (number % 2 == 0)
            {
                return "0.1215686 0.1372549 0.1215686 1";
            }
            else
            {
                return "0.1372549 0.1529412 0.1372549 1";
            }
        }

        private void PVESchedules(BasePlayer player)
        {
            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "1 1 1 0" },
                RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "-515.939 0.005", OffsetMax = "515.961 443.355" }
            }, "SimplePVECUI", "MainShit");
            //time 
            var timenow = DateTime.Now;
            container.Add(new CuiButton
            {
                Button = { Color = "0 0.5019608 0 1", Command = $"sch selectdate {timenow.Month} {timenow.Year}" },  //create a schedules
                Text = { Text = "Create Schedule", Font = "robotocondensed-bold.ttf", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-502.618 175.67", OffsetMax = "-364.549 210.67" }
            }, "MainShit", "CreateSchBTN");

            //create enable days sch btn
            container.Add(new CuiPanel
            {
                Image = { Color = "1 1 1 0" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-359.034 175.67", OffsetMax = "-220.965 210.67" }
            }, "MainShit", "EnableWeeklySCH");

            container.Add(new CuiButton
            {
                Button = { Color = config.sche.enableweeklysch ? "0 0.5019608 0 1" : "0.4588236 0.2 0.1764706 1", Command = "sch changetodays" },
                Text = { Text = "Enable Weekly Schedule", Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-69.034 -17.5", OffsetMax = "69.036 17.5" }
            }, "EnableWeeklySCH", "Button_4654");

            //create a list of schedules here from data
            container.Add(new CuiElement
            {
                Name = "SchListTitle",
                Parent = "MainShit",
                Components = {
                    new CuiTextComponent { Text = "Schedules List", Font = "robotocondensed-bold.ttf", FontSize = 20, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-85.427 174.487", OffsetMax = "85.427 206.433" }
                }
            });

            if (savedSch.SchedulesTime.Count < 1)
            {
                container.Add(new CuiPanel
                {
                    CursorEnabled = true,
                    Image = { Color = "0.1411765 0.1372549 0.1372549 1" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-201.04 -5.848", OffsetMax = "201.04 50" }
                }, "MainShit", "NotingFound");

                container.Add(new CuiElement
                {
                    Name = "Label_3872",
                    Parent = "NotingFound",
                    Components = {
                    new CuiTextComponent { Text = "No Schedules Found!", Font = "robotocondensed-bold.ttf", FontSize = 20, Align = TextAnchor.MiddleCenter, Color = "0.4588236 0.2 0.1764706 1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-201.04 -27.925", OffsetMax = "201.04 27.924" }
                    }
                });
                CuiHelper.DestroyUi(player, "MainShit");
                CuiHelper.AddUi(player, container);
                return;
            }
            // Define variables to track the positioning
            int initialXOffset = -490; // Initial X offset for the first column
            int initialYOffset = 115; // Initial Y offset for the first column
            int xOffset = initialXOffset; // Current X offset
            int yOffset = initialYOffset; // Current Y offset
            int entrySpacing = 40; // Vertical spacing between entries
            int entriesPerColumn = 9; // Number of entries in a column
            int currentEntryCount = 0; // Initialize entry count

            foreach (string kvp in savedSch.SchedulesTime.Keys)
            {
                PVPTime value = savedSch.SchedulesTime[kvp];
                container.Add(new CuiPanel
                {
                    CursorEnabled = true,
                    Image = { Color = "0.1411765 0.1372549 0.1372549 1" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{xOffset} {yOffset}", OffsetMax = $"{xOffset + 261} {yOffset + 36}" }
                }, "MainShit", $"Leftside_{kvp}");

                container.Add(new CuiElement
                {
                    Name = "SchsName",
                    Parent = $"Leftside_{kvp}",
                    Components = {
                    new CuiTextComponent { Text = $"{value.StartDate}", Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-118.744 -18.051", OffsetMax = "-15.293 18.052" }
                }
                });
                container.Add(new CuiButton
                {
                    Button = { Color = "0 0.5019608 0 1", Command = $"sch edit {kvp}" },// edit command
                    Text = { Text = "Edit", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-15.293 -13", OffsetMax = "53.968 13" }
                }, $"Leftside_{kvp}", "EditBTN");
                container.Add(new CuiButton
                {
                    Button = { Color = "0.4588236 0.2 0.1764706 1", Command = $"sch removesch {kvp}" },
                    Text = { Text = "Delete", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "56.969 -13", OffsetMax = "126.23 13" }
                }, $"Leftside_{kvp}", "DelBTN");

                // Update Y offset for the next entry
                yOffset -= entrySpacing;
                currentEntryCount++;

                // Check if we need to start a new column
                if (currentEntryCount >= entriesPerColumn)
                {
                    xOffset += 290; // Move to the right for the next column
                    yOffset = initialYOffset; // Reset Y offset
                    currentEntryCount = 0; // Reset entry count for the new column
                }
            }

            CuiHelper.DestroyUi(player, "MainShit");
            CuiHelper.AddUi(player, container);
        }

        private void SelectDate(BasePlayer player, int month, int year, bool creating = true, string oldDate = "")
        {
            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "1 1 1 0" },
                RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "-515.939 0.005", OffsetMax = "515.961 443.355" }
            }, "SimplePVECUI", "MainShit");

            container.Add(new CuiPanel
            {
                Image = { Color = "0.2980392 0.2862745 0.2784314 0" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-237.463 -206.433", OffsetMax = "228.408 206.433" }
            }, "MainShit", "SelectDatePanel");
            container.Add(new CuiPanel
            {
                Image = { Color = "0.2039216 0.2078432 0.2117647 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-182.641 161.719", OffsetMax = "191.689 197.821" }
            }, "SelectDatePanel", "Back");
            string monthName = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month);
            container.Add(new CuiElement
            {
                Name = $"{monthName}_",
                Parent = "SelectDatePanel",
                Components = {
                    new CuiTextComponent { Text = $"{monthName}", Font = "robotocondensed-bold.ttf", FontSize = 20, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-89.056 -45.383", OffsetMax = "4.527 -7.617" }
                }
            });
            container.Add(new CuiElement
            {
                Name = $"{year}_",
                Parent = "SelectDatePanel",
                Components = {
                    new CuiTextComponent { Text = $"{year}", Font = "robotocondensed-bold.ttf", FontSize = 20, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "4.527 -45.383", OffsetMax = "98.11 -7.617" }
                }
            });
            if (month > DateTime.Now.Month || year > DateTime.Now.Year)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = "1 1 1 0", Command = creating ? $"sch prevsch {month} {year}" : $"sch prevsch00 {month} {year} {creating} {oldDate}" },
                    Text = { Text = "<<", Font = "robotocondensed-bold.ttf", FontSize = 20, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-182.644 -45.546", OffsetMax = "-89.061 -7.78" }
                }, "SelectDatePanel", "Left");
            }
            container.Add(new CuiButton
            {
                Button = { Color = "1 1 1 0", Command = creating ? $"sch nextsch {month} {year}" : $"sch nextsch00 {month} {year} {creating} {oldDate}" },
                Text = { Text = ">>", Font = "robotocondensed-bold.ttf", FontSize = 20, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "98.106 -45.383", OffsetMax = "191.689 -7.617" }
            }, "SelectDatePanel", "Right");
            var amountOfDays = DateTime.DaysInMonth(year, month);
            int x = 0, y = 0;
            for (int i = 1; i <= amountOfDays; i++)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = "0.1411765 0.1372549 0.1372549 1", Command = creating ? $"sch openday {i} {month} {year}" : $"sch changeenddate {i} {month} {year} {oldDate}" },
                    Text = { Text = $"{i}", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = $"{-210.472 + (x * 65)} {-108.985 - (y * 60)}", OffsetMax = $"{-160.472 + (x * 65)} {-58.985 - (y * 60)}" }
                }, "SelectDatePanel", $"{i}_");
                x++;
                if (x >= 7)
                {
                    y++;
                    x = 0;
                }
            }

            CuiHelper.DestroyUi(player, "MainShit");
            CuiHelper.AddUi(player, container);

        }

        private void OpenSelectDate(BasePlayer player, int day, int month, int year, string oldDate)
        {
            savedSch.SchedulesTime[$"{oldDate}"].EndDate = $"{day}/{month}/{year}";
            DateTime date = DateTime.ParseExact(oldDate, "d/M/yyyy", null);
            // Extract day, month, and year components
            int d = date.Day;
            int m = date.Month;
            int y = date.Year;
            CreateSch(player, d.ToString(), m.ToString(), y.ToString());
        }

        private void CreateSch(BasePlayer player, string day, string month, string year, bool create = true)
        {
            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "1 1 1 0" },
                RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "-515.939 0.005", OffsetMax = "515.961 443.355" }
            }, "SimplePVECUI", "MainShit");

            container.Add(new CuiPanel
            {
                Image = { Color = "0.2980392 0.2862745 0.2784314 1", Material = "assets/content/ui/uibackgroundblur.mat" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-172.592 -106.972", OffsetMax = "172.592 154.972" }
            }, "MainShit", "CreateCUI");

            container.Add(new CuiPanel
            {
                Image = { Color = "0.2039216 0.2078431 0.2117647 1" },
                RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-172.589 -35.661", OffsetMax = "172.591 0.385" }
            }, "CreateCUI", "TopBar");

            container.Add(new CuiElement
            {
                Name = "Title",
                Parent = "TopBar",
                Components = {
                    new CuiTextComponent { Text = "Create A New Schedule", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-159.912 -13.851", OffsetMax = "19.712 13.85" }
                }
            });

            container.Add(new CuiPanel
            {
                Image = { Color = "0.1411765 0.1372549 0.1372549 1" },
                RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-166.45 -115.151", OffsetMax = "160.849 -79.049" }
            }, "CreateCUI", "StartTime");

            container.Add(new CuiElement
            {
                Name = "Label_8226",
                Parent = "StartTime",
                Components = {
                    new CuiTextComponent { Text = "Start Time :", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "0.355 -18.051", OffsetMax = "106.265 18.052" }
                }
            });
            /// start time
            /*container.Add(new CuiElement
            {
                Name = "Day",
                Parent = "StartTime",
                Components = {
                    new CuiTextComponent { Text = $"{savedSch.SchedulesTime[$"{day}/{month}/{year}"].StartMinute}M", Color = "0.2039216 0.2078432 0.2117647 1", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "47.633 -13", OffsetMax = "131.567 13" }
                }
            });*/
            container.Add(new CuiElement
            {
                Name = "Day",
                Parent = "StartTime",
                Components = {
                    new CuiInputFieldComponent { Text = $"{savedSch.SchedulesTime[$"{day}/{month}/{year}"].StartMinute}M", Command = $"sch changesm {day} {month} {year}", Color = "0.2039216 0.2078432 0.2117647 1", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, CharsLimit = 0, IsPassword = false },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "47.633 -13", OffsetMax = "131.567 13" }
                }
            });

            /*container.Add(new CuiElement
            {
                Name = "Year",
                Parent = "StartTime",
                Components = {
                    new CuiTextComponent { Text = $"{savedSch.SchedulesTime[$"{day}/{month}/{year}"].StartHour}H", Color = "0.2039216 0.2078432 0.2117647 1", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-57.735 -13", OffsetMax = "22.809 13" }
                }
            });
            */
            container.Add(new CuiElement
            {
                Name = "Year",
                Parent = "StartTime",
                Components = {
                    new CuiInputFieldComponent { Text = $"{savedSch.SchedulesTime[$"{day}/{month}/{year}"].StartHour}H", Command = $"sch changesh {day} {month} {year}", Color = "0.2039216 0.2078432 0.2117647 1", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, CharsLimit = 0, IsPassword = false },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-57.735 -13", OffsetMax = "22.809 13" }
                }
            });

            container.Add(new CuiPanel
            {
                Image = { Color = "0.1411765 0.1372549 0.1372549 1" },
                RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-166.45 -75.249", OffsetMax = "60.754 -39.147" }
            }, "CreateCUI", "StartDate");

            container.Add(new CuiElement
            {
                Name = "Label_8226",
                Parent = "StartDate",
                Components = {
                    new CuiTextComponent { Text = "Start Date :", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "0.355 -18.051", OffsetMax = "106.265 18.052" }
                }
            });

            container.Add(new CuiElement
            {
                Name = "Label_2552",
                Parent = "StartDate",
                Components = {
                    new CuiTextComponent { Text = $"{savedSch.SchedulesTime[$"{day}/{month}/{year}"].StartDate}", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "0.001 -18.051", OffsetMax = "113.601 18.052" }
                }
            });

            container.Add(new CuiPanel
            {
                Image = { Color = "0.1411765 0.1372549 0.1372549 1" },
                RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-166.45 -154.751", OffsetMax = "160.85 -118.649" }
            }, "CreateCUI", "EndDate");

            container.Add(new CuiElement
            {
                Name = "Label_8226",
                Parent = "EndDate",
                Components = {
                    new CuiTextComponent { Text = "End Date :", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "0.355 -18.051", OffsetMax = "106.265 18.052" }
                }
            });

            container.Add(new CuiButton
            {
                Button = { Color = "0.2039216 0.2078432 0.2117647 1", Command = $"sch selectdatefalse {month} {year} {day}" }, //select date command
                Text = { Text = "Select", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.5019608 0.5019608 0.5019608 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-57.385 -13", OffsetMax = "23.159 13" }
            }, "EndDate", "Button_369");

            container.Add(new CuiElement
            {
                Name = "EndDate",
                Parent = "EndDate",
                Components = {
                    new CuiTextComponent { Text = $"{savedSch.SchedulesTime[$"{day}/{month}/{year}"].EndDate}", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "48.241 -18.051", OffsetMax = "157.453 18.051" }
                }
            });

            container.Add(new CuiPanel
            {
                Image = { Color = "0.1411765 0.1372549 0.1372549 1" },
                RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-166.45 -194.051", OffsetMax = "160.849 -157.949" }
            }, "CreateCUI", "EndTime");

            container.Add(new CuiElement
            {
                Name = "Label_8226",
                Parent = "EndTime",
                Components = {
                    new CuiTextComponent { Text = "End Time :", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "0.355 -18.051", OffsetMax = "106.265 18.052" }
                }
            });
            //time
            /*container.Add(new CuiElement
            {
                Name = "Day",
                Parent = "EndTime",
                Components = {
                    new CuiTextComponent { Text = $"{savedSch.SchedulesTime[$"{day}/{month}/{year}"].EndMinute}M",Color = "0.2039216 0.2078432 0.2117647 1", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter},
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "47.633 -13", OffsetMax = "131.567 13" }
                }
            });*/
            container.Add(new CuiElement
            {
                Name = "Day",
                Parent = "EndTime",
                Components = {
                    new CuiInputFieldComponent { Text = $"{savedSch.SchedulesTime[$"{day}/{month}/{year}"].EndMinute}M", Command = $"sch changeem {day} {month} {year}", Color = "0.2039216 0.2078432 0.2117647 1", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, CharsLimit = 0, IsPassword = false },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "47.633 -13", OffsetMax = "131.567 13" }
                }
            });
            /*container.Add(new CuiElement
            {
                Name = "Year",
                Parent = "EndTime",
                Components = {
                    new CuiTextComponent { Text = $"{savedSch.SchedulesTime[$"{day}/{month}/{year}"].EndHour}H", Color = "0.2039216 0.2078432 0.2117647 1", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-57.735 -13", OffsetMax = "22.809 13" }
                }
            });*/
            container.Add(new CuiElement
            {
                Name = "Year",
                Parent = "EndTime",
                Components = {
                    new CuiInputFieldComponent { Text = $"{savedSch.SchedulesTime[$"{day}/{month}/{year}"].EndHour}H", Command = $"sch changeeh {day} {month} {year}", Color = "0.2039216 0.2078432 0.2117647 1", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, CharsLimit = 0, IsPassword = false },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-57.735 -13", OffsetMax = "22.809 13" }
                }
            });

            container.Add(new CuiButton
            {
                Button = { Color = "0.5528213 0.9528302 0.5568399 1", Command = "spvecui pveschedules" },
                Text = { Text = "Save", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "86.327 -126", OffsetMax = "166.872 -100" }
            }, "CreateCUI", "Create");

            CuiHelper.DestroyUi(player, "MainShit");
            CuiHelper.AddUi(player, container);
        }

        private enum Mode
        {
            PVE,
            PVP
        }

        private class ScheduleSave
        {
            public Dictionary<string, PVPTime> SchedulesTime = new Dictionary<string, PVPTime>();
        }

        private class PVPTime
        {
            public string StartDate;
            public int StartHour;
            public int StartMinute;
            public string EndDate;
            public int EndHour;
            public int EndMinute;
            public bool IsWeekDay;
        }

        private void LoadSchTime()
        {
            savedSch = Interface.Oxide.DataFileSystem.ReadObject<ScheduleSave>($"{Name}/SchedulesTime");
        }

        private void SaveSchTime()
        {
            Interface.Oxide.DataFileSystem.WriteObject($"{Name}/SchedulesTime", savedSch);
        }

        [ConsoleCommand("sch")]
        private void SchTimeCMD(ConsoleSystem.Arg args)
        {
            var player = args?.Player();

            string command = args.GetString(0);
            int time;

            switch (command)
            {
                case "openday":
                    if (savedSch.SchedulesTime.ContainsKey($"{args.GetString(1)}/{args.GetString(2)}/{args.GetString(3)}"))
                    {
                        CreateSch(player, args.GetString(1), args.GetString(2), args.GetString(3));
                        return;
                    }
                    savedSch.SchedulesTime.Add($"{args.GetString(1)}/{args.GetString(2)}/{args.GetString(3)}", new PVPTime { StartHour = 12, StartMinute = 30, EndHour = 23, EndMinute = 30, StartDate = $"{args.GetString(1)}/{args.GetString(2)}/{args.GetString(3)}", EndDate = $"{args.GetString(1)}/{args.GetString(2)}/{args.GetString(3)}", IsWeekDay = false });
                    CreateSch(player, args.GetString(1), args.GetString(2), args.GetString(3));
                    break;
                case "openweekday":
                    if (savedSch.SchedulesTime.ContainsKey($"{args.GetString(1)}"))
                    {
                        CreateSchForWeekDay(player, args.GetString(1));
                        return;
                    }
                    savedSch.SchedulesTime.Add($"{args.GetString(1)}", new PVPTime { StartHour = 12, StartMinute = 30, EndHour = 23, EndMinute = 30, StartDate = $"{args.GetString(1)}", EndDate = $"{args.GetString(1)}", IsWeekDay = true });
                    CreateSchForWeekDay(player, args.GetString(1));
                    break;
                case "selectdate":
                    if (!config.sche.enableweeklysch)
                    {
                        SelectDate(player, args.GetInt(1), args.GetInt(2));
                    }
                    else
                    {
                        OpenDaysMenu(player);
                    }
                    break;
                case "selectdatefalse":
                    SelectDate(player, args.GetInt(1), args.GetInt(2), false, $"{args.GetInt(3)}/{args.GetInt(1)}/{args.GetInt(2)}");
                    break;
                case "changeenddate":
                    OpenSelectDate(player, args.GetInt(1), args.GetInt(2), args.GetInt(3), args.GetString(4));
                    break;
                case "changesm":
                    if (!savedSch.SchedulesTime.ContainsKey($"{args.GetString(1)}/{args.GetString(2)}/{args.GetString(3)}")) return;
                    time = args.GetInt(4);
                    if (time > 59) time = 59;
                    if (time < 0) time = 0;
                    savedSch.SchedulesTime[$"{args.GetString(1)}/{args.GetString(2)}/{args.GetString(3)}"].StartMinute = time;
                    CreateSch(player, args.GetString(1), args.GetString(2), args.GetString(3));
                    break;
                case "changesh":
                    if (!savedSch.SchedulesTime.ContainsKey($"{args.GetString(1)}/{args.GetString(2)}/{args.GetString(3)}")) return;
                    time = args.GetInt(4);
                    if (time > 24) time = 24;
                    if (time < 0) time = 0;
                    savedSch.SchedulesTime[$"{args.GetString(1)}/{args.GetString(2)}/{args.GetString(3)}"].StartHour = time;
                    CreateSch(player, args.GetString(1), args.GetString(2), args.GetString(3));
                    break;
                case "changeem":
                    if (!savedSch.SchedulesTime.ContainsKey($"{args.GetString(1)}/{args.GetString(2)}/{args.GetString(3)}")) return;
                    time = args.GetInt(4);
                    if (time > 59) time = 59;
                    if (time < 0) time = 0;
                    savedSch.SchedulesTime[$"{args.GetString(1)}/{args.GetString(2)}/{args.GetString(3)}"].EndMinute = time;
                    CreateSch(player, args.GetString(1), args.GetString(2), args.GetString(3));
                    break;
                case "changeeh":
                    if (!savedSch.SchedulesTime.ContainsKey($"{args.GetString(1)}/{args.GetString(2)}/{args.GetString(3)}")) return;
                    time = args.GetInt(4);
                    if (time > 24) time = 24;
                    if (time < 0) time = 0;
                    savedSch.SchedulesTime[$"{args.GetString(1)}/{args.GetString(2)}/{args.GetString(3)}"].EndHour = time;
                    CreateSch(player, args.GetString(1), args.GetString(2), args.GetString(3));
                    break;
                case "removesch":
                    if (savedSch.SchedulesTime.ContainsKey(args.GetString(1)))
                    {
                        savedSch.SchedulesTime.Remove(args.GetString(1));
                    }
                    PVESchedules(player);
                    break;
                case "edit":
                    string dateTime = args.GetString(1);
                    if (dateTime.Contains("day"))
                    {
                        CreateSchForWeekDay(player, dateTime);
                        return;
                    }
                    DateTime date = DateTime.ParseExact(dateTime, "d/M/yyyy", null);
                    // Extract day, month, and year components
                    int d = date.Day;
                    int m = date.Month;
                    int y = date.Year;
                    CreateSch(player, d.ToString(), m.ToString(), y.ToString());
                    break;
                case "nextsch":
                    int month = args.GetInt(1), year = args.GetInt(2);
                    if (month >= 12)
                    {
                        SelectDate(player, 1, (year + 1));
                    }
                    else { SelectDate(player, (month + 1), year); }
                    break;
                case "prevsch":
                    int premonth = args.GetInt(1), preyear = args.GetInt(2);
                    if (premonth <= 1)
                    {
                        SelectDate(player, 12, (preyear - 1));
                    }
                    else { SelectDate(player, (premonth - 1), preyear); }
                    break;
                case "nextsch00":
                    int month00 = args.GetInt(1), year00 = args.GetInt(2);
                    if (month00 >= 12)
                    {
                        SelectDate(player, 1, (year00 + 1), args.GetBool(3), args.GetString(4));
                    }
                    else { SelectDate(player, (month00 + 1), year00, args.GetBool(3), args.GetString(4)); }
                    break;
                case "prevsch00":
                    int premonth00 = args.GetInt(1), preyear00 = args.GetInt(2);
                    if (premonth00 <= 1)
                    {
                        SelectDate(player, 12, (preyear00 - 1), args.GetBool(3), args.GetString(4));
                    }
                    else { SelectDate(player, (premonth00 - 1), preyear00, args.GetBool(3), args.GetString(4)); }
                    break;
                case "changetodays": //new schedules day
                    config.sche.enableweeklysch = !config.sche.enableweeklysch;
                    SaveConfig();
                    PVESchedules(player);
                    break;
                case "changesmday":
                    if (!savedSch.SchedulesTime.ContainsKey($"{args.GetString(1)}")) return;
                    time = args.GetInt(2);
                    if (time > 59) time = 59;
                    if (time < 0) time = 0;
                    savedSch.SchedulesTime[$"{args.GetString(1)}"].StartMinute = time;
                    CreateSchForWeekDay(player, args.GetString(1));
                    break;
                case "changeshday":
                    if (!savedSch.SchedulesTime.ContainsKey($"{args.GetString(1)}")) return;
                    time = args.GetInt(2);
                    if (time > 24) time = 24;
                    if (time < 0) time = 0;
                    savedSch.SchedulesTime[$"{args.GetString(1)}"].StartHour = time;
                    CreateSchForWeekDay(player, args.GetString(1));
                    break;
                case "changeemday":
                    if (!savedSch.SchedulesTime.ContainsKey($"{args.GetString(1)}")) return;
                    time = args.GetInt(2);
                    if (time > 59) time = 59;
                    if (time < 0) time = 0;
                    savedSch.SchedulesTime[$"{args.GetString(1)}"].EndMinute = time;
                    CreateSchForWeekDay(player, args.GetString(1));
                    break;
                case "changeehday":
                    if (!savedSch.SchedulesTime.ContainsKey($"{args.GetString(1)}")) return;
                    time = args.GetInt(2);
                    if (time > 24) time = 24;
                    if (time < 0) time = 0;
                    savedSch.SchedulesTime[$"{args.GetString(1)}"].EndHour = time;
                    CreateSchForWeekDay(player, args.GetString(1));
                    break;

            }

        }

        private void ShowPVPPurgeHud(BasePlayer player)
        {
            var container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = "0.7764707 0.2352941 0.1607843 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "452.616 -231.151", OffsetMax = "625.358 -205.603" }
            }, "Overlay", "PVPHud");

            container.Add(new CuiElement
            {
                Name = "Label_3928",
                Parent = "PVPHud",
                Components = {
                    new CuiTextComponent { Text = "PVP Purge In", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-83.942 -12.774", OffsetMax = "18.332 12.774" }
                }
            });

            container.Add(new CuiPanel
            {
                Image = { Color = "0.5490196 0.1843137 0.08627451 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "34.458 -12.774", OffsetMax = "86.371 12.774" }
            }, "PVPHud", "Panel_9108");

            container.Add(new CuiElement
            {
                Name = "Label_4112",
                Parent = "Panel_9108",
                Components = {
                    new CuiTextComponent { Text = "", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-25.957 -12.774", OffsetMax = "25.956 12.774" }
                }
            });

            CuiHelper.DestroyUi(player, "PVPHud");
            CuiHelper.AddUi(player, container);
        }

        private void OpenDaysMenu(BasePlayer player)
        {
            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "0.2196079 0.2196079 0.2196079 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-114.625 -200.549", OffsetMax = "114.625 154.971" }
            }, "MainShit", "DaysList");
            container.Add(new CuiPanel
            {
                Image = { Color = "0.2078431 0.2078431 0.2078431 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-114.626 136.061", OffsetMax = "114.624 177.759" }
            }, "DaysList", "TitleDay");
            container.Add(new CuiElement
            {
                Name = "Label_60",
                Parent = "TitleDay",
                Components = {
                    new CuiTextComponent { Text = "Select Day", Font = "robotocondensed-bold.ttf", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-103.341 -17.819", OffsetMax = "103.341 17.819" }
                }
            });

            container.Add(new CuiButton
            {
                Button = { Color = "0.1333333 0.1333333 0.1333333 1", Command = "sch openweekday Sunday" },
                Text = { Text = "Sunday", Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-52.6 81.702", OffsetMax = "47.4 120.77" }
            }, "DaysList", "Sunday");

            container.Add(new CuiButton
            {
                Button = { Color = "0.1333333 0.1333333 0.1333333 1", Command = "sch openweekday Monday" },
                Text = { Text = "Monday", Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-52.6 42.634", OffsetMax = "47.4 81.702" }
            }, "DaysList", "Monday");

            container.Add(new CuiButton
            {
                Button = { Color = "0.1333333 0.1333333 0.1333333 1", Command = "sch openweekday Tuesday" },
                Text = { Text = "Tuesday", Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-52.6 3.566", OffsetMax = "47.4 42.634" }
            }, "DaysList", "Tuesday");

            container.Add(new CuiButton
            {
                Button = { Color = "0.1333333 0.1333333 0.1333333 1", Command = "sch openweekday Wednesday" },
                Text = { Text = "Wednesday", Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-52.6 -35.502", OffsetMax = "47.4 3.566" }
            }, "DaysList", "Wednesday");

            container.Add(new CuiButton
            {
                Button = { Color = "0.1333333 0.1333333 0.1333333 1", Command = "sch openweekday Thursday" },
                Text = { Text = "Thursday", Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-52.6 -70.798", OffsetMax = "47.4 -31.73" }
            }, "DaysList", "Thurday");

            container.Add(new CuiButton
            {
                Button = { Color = "0.1333333 0.1333333 0.1333333 1", Command = "sch openweekday Friday" },
                Text = { Text = "Friday", Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-52.6 -109.866", OffsetMax = "47.4 -70.798" }
            }, "DaysList", "Friday");

            container.Add(new CuiButton
            {
                Button = { Color = "0.1333333 0.1333333 0.1333333 1", Command = "sch openweekday Saturday" },
                Text = { Text = "Saturday", Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-52.6 -148.934", OffsetMax = "47.4 -109.866" }
            }, "DaysList", "Saturday");

            CuiHelper.DestroyUi(player, "DaysList");
            CuiHelper.AddUi(player, container);
        }

        private void CreateSchForWeekDay(BasePlayer player, string day)
        {
            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "1 1 1 0" },
                RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "-515.939 0.005", OffsetMax = "515.961 443.355" }
            }, "SimplePVECUI", "MainShit");

            container.Add(new CuiPanel
            {
                Image = { Color = "0.2980392 0.2862745 0.2784314 1", Material = "assets/content/ui/uibackgroundblur.mat" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-172.592 -106.972", OffsetMax = "172.592 154.972" }
            }, "MainShit", "CreateCUI");

            container.Add(new CuiPanel
            {
                Image = { Color = "0.2039216 0.2078431 0.2117647 1" },
                RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-172.589 -35.661", OffsetMax = "172.591 0.385" }
            }, "CreateCUI", "TopBar");

            container.Add(new CuiElement
            {
                Name = "Title",
                Parent = "TopBar",
                Components = {
                    new CuiTextComponent { Text = "Create A New Schedule", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-159.912 -13.851", OffsetMax = "19.712 13.85" }
                }
            });

            container.Add(new CuiPanel
            {
                Image = { Color = "0.1411765 0.1372549 0.1372549 1" },
                RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-166.45 -115.151", OffsetMax = "160.849 -79.049" }
            }, "CreateCUI", "StartTime");

            container.Add(new CuiElement
            {
                Name = "Label_8226",
                Parent = "StartTime",
                Components = {
                    new CuiTextComponent { Text = "Start Time :", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "0.355 -18.051", OffsetMax = "106.265 18.052" }
                }
            });
            /// start time
            /*container.Add(new CuiElement
            {
                Name = "Day",
                Parent = "StartTime",
                Components = {
                    new CuiTextComponent { Text = $"{savedSch.SchedulesTime[$"{day}"].StartMinute}M", Color = "0.2039216 0.2078432 0.2117647 1", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "47.633 -13", OffsetMax = "131.567 13" }
                }
            });*/
            container.Add(new CuiElement
            {
                Name = "Day",
                Parent = "StartTime",
                Components = {
                    new CuiInputFieldComponent { Text = $"{savedSch.SchedulesTime[$"{day}"].StartMinute}M", Command = $"sch changesmday {day}", Color = "0.2039216 0.2078432 0.2117647 1", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, CharsLimit = 0, IsPassword = false },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "47.633 -13", OffsetMax = "131.567 13" }
                }
            });
            /*container.Add(new CuiElement
            {
                Name = "Year",
                Parent = "StartTime",
                Components = {
                    new CuiTextComponent { Text = $"{savedSch.SchedulesTime[$"{day}"].StartHour}H",Color = "0.2039216 0.2078432 0.2117647 1", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-57.735 -13", OffsetMax = "22.809 13" }
                }
            });*/
            container.Add(new CuiElement
            {
                Name = "Year",
                Parent = "StartTime",
                Components = {
                    new CuiInputFieldComponent { Text = $"{savedSch.SchedulesTime[$"{day}"].StartHour}H", Command = $"sch changeshday {day}", Color = "0.2039216 0.2078432 0.2117647 1", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, CharsLimit = 0, IsPassword = false },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-57.735 -13", OffsetMax = "22.809 13" }
                }
            });

            container.Add(new CuiPanel
            {
                Image = { Color = "0.1411765 0.1372549 0.1372549 1" },
                RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-166.45 -75.249", OffsetMax = "60.754 -39.147" }
            }, "CreateCUI", "StartDate");

            container.Add(new CuiElement
            {
                Name = "Label_8226",
                Parent = "StartDate",
                Components = {
                    new CuiTextComponent { Text = "Start Date :", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "0.355 -18.051", OffsetMax = "106.265 18.052" }
                }
            });

            container.Add(new CuiElement
            {
                Name = "Label_2552",
                Parent = "StartDate",
                Components = {
                    new CuiTextComponent { Text = $"{savedSch.SchedulesTime[$"{day}"].StartDate}", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "0.001 -18.051", OffsetMax = "113.601 18.052" }
                }
            });

            container.Add(new CuiPanel
            {
                Image = { Color = "0.1411765 0.1372549 0.1372549 1" },
                RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-166.45 -154.751", OffsetMax = "160.85 -118.649" }
            }, "CreateCUI", "EndDate");

            container.Add(new CuiElement
            {
                Name = "Label_8226",
                Parent = "EndDate",
                Components = {
                    new CuiTextComponent { Text = "End Date :", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "0.355 -18.051", OffsetMax = "106.265 18.052" }
                }
            });

            /*container.Add(new CuiButton
            {
                Button = { Color = "0.2039216 0.2078432 0.2117647 1", Command = $"sch selectdatefalse {month} {year} {day}" }, //select date command
                Text = { Text = "Select", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.5019608 0.5019608 0.5019608 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-57.385 -13", OffsetMax = "23.159 13" }
            }, "EndDate", "Button_369");*/

            container.Add(new CuiElement
            {
                Name = "EndDate",
                Parent = "EndDate",
                Components = {
                    new CuiTextComponent { Text = $"{savedSch.SchedulesTime[$"{day}"].EndDate}", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "48.241 -18.051", OffsetMax = "157.453 18.051" }
                }
            });

            container.Add(new CuiPanel
            {
                Image = { Color = "0.1411765 0.1372549 0.1372549 1" },
                RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-166.45 -194.051", OffsetMax = "160.849 -157.949" }
            }, "CreateCUI", "EndTime");

            container.Add(new CuiElement
            {
                Name = "Label_8226",
                Parent = "EndTime",
                Components = {
                    new CuiTextComponent { Text = "End Time :", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "0.355 -18.051", OffsetMax = "106.265 18.052" }
                }
            });
            //time
            /*container.Add(new CuiElement
            {
                Name = "Day",
                Parent = "EndTime",
                Components = {
                    new CuiTextComponent { Text = $"{savedSch.SchedulesTime[$"{day}"].EndMinute}M",Color = "0.2039216 0.2078432 0.2117647 1", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter},
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "47.633 -13", OffsetMax = "131.567 13" }
                }
            });*/
            container.Add(new CuiElement
            {
                Name = "Day",
                Parent = "EndTime",
                Components = {
                    new CuiInputFieldComponent { Text = $"{savedSch.SchedulesTime[$"{day}"].EndMinute}M", Command = $"sch changeemday {day}", Color = "0.2039216 0.2078432 0.2117647 1", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, CharsLimit = 0, IsPassword = false },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "47.633 -13", OffsetMax = "131.567 13" }
                }
            });
            /*container.Add(new CuiElement
            {
                Name = "Year",
                Parent = "EndTime",
                Components = {
                    new CuiTextComponent { Text = $"{savedSch.SchedulesTime[$"{day}"].EndHour}H", Color = "0.2039216 0.2078432 0.2117647 1", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-57.735 -13", OffsetMax = "22.809 13" }
                }
            });*/
            container.Add(new CuiElement
            {
                Name = "Year",
                Parent = "EndTime",
                Components = {
                    new CuiInputFieldComponent { Text = $"{savedSch.SchedulesTime[$"{day}"].EndHour}H", Command = $"sch changeehday {day}", Color = "0.2039216 0.2078432 0.2117647 1", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, CharsLimit = 0, IsPassword = false },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-57.735 -13", OffsetMax = "22.809 13" }
                }
            });

            container.Add(new CuiButton
            {
                Button = { Color = "0.5528213 0.9528302 0.5568399 1", Command = "spvecui pveschedules" },
                Text = { Text = "Save", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "86.327 -126", OffsetMax = "166.872 -100" }
            }, "CreateCUI", "Create");

            CuiHelper.DestroyUi(player, "MainShit");
            CuiHelper.AddUi(player, container);
        }

        #endregion

        #endregion

        #region Helpers

        private bool IsInDeepSea(Vector3 position)
        {
            return DeepSeaBounds.Contains(position);
        }

        public static string Color(string hexColor, float alpha)
        {
            if (hexColor.StartsWith("#"))
                hexColor = hexColor.TrimStart('#');

            int red = int.Parse(hexColor.Substring(0, 2), NumberStyles.AllowHexSpecifier);
            int green = int.Parse(hexColor.Substring(2, 2), NumberStyles.AllowHexSpecifier);
            int blue = int.Parse(hexColor.Substring(4, 2), NumberStyles.AllowHexSpecifier);

            return $"{(double)red / 255} {(double)green / 255} {(double)blue / 255} {alpha}";
        }

        private void SendChatMessage(BasePlayer player, string message)
        {
            Player.Message(player, config.displayNotify.prefix + message, config.displayNotify.chatAvatar);
        }

        private string FormatTime(double time)
        {
            TimeSpan dateDifference = TimeSpan.FromSeconds(time);

            if (dateDifference.Days > 0)
                return $"{dateDifference.Days}D:{dateDifference.Hours:00}H";

            if (dateDifference.Hours > 0)
                return $"{dateDifference.Hours}H:{dateDifference.Minutes:00}M";

            if (dateDifference.Minutes > 0)
                return $"{dateDifference.Minutes}M:{dateDifference.Seconds:00}S";

            return $"{dateDifference.Seconds}S";
        }

        #endregion

        #region Schedules Timer

        private DateTime Time => DateTime.UtcNow.AddMinutes(config.sche.utcTimeDif);

        private IEnumerator Schedules()
        {
            int count = 0;

            while (update)
            {
                if (config.discordSetting.enableDiscordNotify) SendMessage();

                if (config.statusUI.status && SimpleStatus)
                {
                    if (count >= config.statusUI.interval)
                    {                       
                        InitializeStatusPlayers();
                        AddSimpleStatus();
                        count = 0;
                    }
                    count += 5;
                } 

                if (IsItTime())
                {
                    if (PVPRun)
                    {
                        yield return new WaitForSeconds(5f);
                        continue;
                    }

                    PVPRun = true;
                    PrintWarning("Raid time has begun!");
                    EnablePVP();
                    DiscordMessageSent = false;
                }
                else
                {
                    if (!PVPRun)
                    {
                        yield return new WaitForSeconds(5f);
                        continue;
                    }
                    PVPRun = false;
                    EnablePVE();
                    DiscordMessageSent = false;
                }

                // Sleep for a while before checking again
                yield return new WaitForSeconds(5f);
            }

        }

        private bool IsItTime()
        {
            DateTime currentTime = Time;

            foreach (var kvp in savedSch.SchedulesTime)
            {
                PVPTime schedule = kvp.Value;

                if (config.sche.enableweeklysch)
                {
                    if (schedule.IsWeekDay)
                    {
                        if (currentTime.DayOfWeek == ParseDayOfWeek(schedule.StartDate))
                        {
                            DateTime startTimeDay = currentTime.Date.AddHours(schedule.StartHour).AddMinutes(schedule.StartMinute);
                            DateTime endTimeDay = currentTime.Date.AddHours(schedule.EndHour).AddMinutes(schedule.EndMinute);

                            //Puts($"{currentTime} >= {startTimeDay} + {currentTime} <= {endTimeDay}");
                            if (currentTime >= startTimeDay && currentTime <= endTimeDay)
                            {
                                //Puts("Its true");
                                return true;
                            }
                        }
                    }
                }
                else
                {
                    if (!schedule.IsWeekDay)
                    {
                        // Parse the start and end dates and times
                        DateTime startDate = DateTime.ParseExact(schedule.StartDate, "d/M/yyyy", null);
                        DateTime endDate = DateTime.ParseExact(schedule.EndDate, "d/M/yyyy", null);
                        DateTime startTime = startDate.AddHours(schedule.StartHour).AddMinutes(schedule.StartMinute);
                        DateTime endTime = endDate.AddHours(schedule.EndHour).AddMinutes(schedule.EndMinute);

                        // Check if the current time is within the schedule
                        if (currentTime >= startTime && currentTime <= endTime)
                        {
                            return true;
                        }
                    }
                }

            }
            return false;
        }

        private DayOfWeek ParseDayOfWeek(string dayOfWeek)
        {
            switch (dayOfWeek.ToLower())
            {
                case "sunday":
                    return DayOfWeek.Sunday;
                case "monday":
                    return DayOfWeek.Monday;
                case "tuesday":
                    return DayOfWeek.Tuesday;
                case "wednesday":
                    return DayOfWeek.Wednesday;
                case "thursday":
                    return DayOfWeek.Thursday;
                case "friday":
                    return DayOfWeek.Friday;
                case "saturday":
                    return DayOfWeek.Saturday;
                default:
                    throw new ArgumentException("Invalid day of the week: " + dayOfWeek);
            }
        }

        private TimeSpan CalculateTimeDifference()
        {
            DateTime currentTime = Time;

            foreach (var kvp in savedSch.SchedulesTime)
            {
                PVPTime schedule = kvp.Value;

                if (config.sche.enableweeklysch)
                {
                    if (schedule.IsWeekDay)
                    {
                        DayOfWeek scheduleDay = ParseDayOfWeek(schedule.StartDate);
                        DateTime startTimeDay = currentTime.Date.AddHours(schedule.StartHour).AddMinutes(schedule.StartMinute);

                        if (currentTime.DayOfWeek == scheduleDay)
                        {
                            TimeSpan timeDifferenceDay = startTimeDay - currentTime;
                            return timeDifferenceDay;
                        }
                        else
                        {
                            int daysUntilNextOccurrence = ((int)scheduleDay - (int)currentTime.DayOfWeek + 7) % 7;
                            if (daysUntilNextOccurrence == 0) daysUntilNextOccurrence = 7;

                            DateTime nextOccurrence = currentTime.AddDays(daysUntilNextOccurrence).Date.AddHours(schedule.StartHour).AddMinutes(schedule.StartMinute);
                            TimeSpan timeDifferenceNextOccurrence = nextOccurrence - currentTime;

                            return timeDifferenceNextOccurrence;
                        }
                    }

                }
                else
                {
                    if (!schedule.IsWeekDay)
                    {
                        DateTime startDate = DateTime.ParseExact(schedule.StartDate, "d/M/yyyy", null);
                        DateTime startTime = startDate.AddHours(schedule.StartHour).AddMinutes(schedule.StartMinute);

                        // Calculate time difference
                        TimeSpan timeDifference = startTime - currentTime;

                        return timeDifference;
                    }
                }
            }

            // Return a default TimeSpan if no valid schedule is found
            return TimeSpan.Zero;
        }

        private TimeSpan CalculateTimeDifferenceToEnd()
        {
            DateTime currentTime = Time;

            foreach (var kvp in savedSch.SchedulesTime)
            {
                PVPTime schedule = kvp.Value;

                if (config.sche.enableweeklysch)
                {
                    if (schedule.IsWeekDay)
                    {
                        if (currentTime.DayOfWeek == ParseDayOfWeek(schedule.EndDate))
                        {
                            DateTime endTimeDay = currentTime.Date.AddHours(schedule.EndHour).AddMinutes(schedule.EndMinute);

                            TimeSpan timeDifferenceDay = endTimeDay - currentTime;

                            if (timeDifferenceDay.TotalMinutes > 0)
                            {
                                return timeDifferenceDay;
                            }
                        }
                    }
                }
                else
                {
                    if (!schedule.IsWeekDay)
                    {
                        DateTime endDate = DateTime.ParseExact(schedule.EndDate, "d/M/yyyy", null);
                        DateTime endTime = endDate.AddHours(schedule.EndHour).AddMinutes(schedule.EndMinute);

                        // Calculate time difference to the end time
                        TimeSpan timeDifferenceToEnd = endTime - currentTime;

                        // Check if the scheduled end time is in the future
                        if (timeDifferenceToEnd.TotalMinutes > 0)
                        {
                            return timeDifferenceToEnd;
                        }
                    }
                }
            }

            // Return a default TimeSpan if no valid schedule is found
            return TimeSpan.Zero;
        }

        private void EnablePVP()
        {
            config.pveEnabledConfig = false;
            SaveConfig();
            NotSubscribe();
            foreach (var player in BasePlayer.activePlayerList)
            {
                try
                {
                    RemovePVEPanel(player);
                    RemovePVPPanel(player);
                    ShowPVPUi(player);
                    SendChatMessage(player, config.sche.pvpOnMessage);
                    player.SendConsoleCommand("gametip.showtoast", "5", config.sche.pvpOnMessage);
                }catch (Exception e)
                {
                    PrintError(e.Message);
                }
            }
            //execute commands from list
            foreach (var command in config.commandsExefrompvpstart)
            {
                Server.Command(command);
            }
            Interface.CallHook("OnSPVEPurgeStarted");
        }
        private void EnablePVE()
        {
            config.pveEnabledConfig = true;
            SaveConfig();
            Subscribe();
            foreach (var player in BasePlayer.activePlayerList)
            {
                try
                {
                    RemovePVPPanel(player);
                    RemovePVEPanel(player);
                    ShowPVEUI(player);
                    SendChatMessage(player, config.sche.pveOnMessage);
                    player.SendConsoleCommand("gametip.showtoast", "5", config.sche.pveOnMessage);
                }
                catch (Exception e)
                {
                    PrintError(e.Message);
                }
            }
            //execute commands from list
            foreach (var command in config.commandsExefrompvpends)
            {
                Server.Command(command);
            }
            Interface.CallHook("OnSPVEPurgeEnded");
        }

        private void SendPVPStartMessage()
        {
            if (config.discordSetting.enableDiscordNotify && config.discordSetting.discordWebhookURL != null && config.discordSetting.messageBeforeStart)
            {
                string message = string.Join("\n", config.discordSetting.pvpTimeMessage);
                SendDiscordMessage(config.discordSetting.discordWebhookURL, "", new List<string> { message.Replace("{Minutes}", config.discordSetting.messageBeforeStartMinutes.ToString()) }, config.discordSetting.pvpmessageEmbed, false);
            }
        }
        private void SendPVPEndMessage()
        {
            if (config.discordSetting.enableDiscordNotify && config.discordSetting.discordWebhookURL != null && config.discordSetting.messageBeforeStartPVE)
            {
                string message = string.Join("\n", config.discordSetting.pveTimeMessage);
                SendDiscordMessage(config.discordSetting.discordWebhookURL, "", new List<string> { message.Replace("{Minutes}", config.discordSetting.messageBeforeStartMinutesPVE.ToString()) }, config.discordSetting.pvemessageEmbed, false);
            }
        }

        private void SendMessage()
        {
            if (DiscordMessageSent)
                return;

            if (config.discordSetting.enableDiscordNotify && config.discordSetting.discordWebhookURL != null)
            {
                TimeSpan timeDifference = CalculateTimeDifference();
                TimeSpan timeDifferenceEnd = CalculateTimeDifferenceToEnd();

                if (Math.Abs(timeDifference.TotalMinutes - config.discordSetting.messageBeforeStartMinutes) < 0.1)
                {
                    Puts($"PVP time is approaching! {Math.Round(timeDifference.TotalMinutes)} minutes left.");
                    Broadcast(config.sche.pvppurgeStartMsg.Replace("{TIMELEFT}", Math.Round(timeDifference.TotalMinutes).ToString()));
                    SendPVPStartMessage();
                    DiscordMessageSent = true;
                }
                else if (Math.Abs(timeDifferenceEnd.TotalMinutes - config.discordSetting.messageBeforeStartMinutesPVE) < 0.1)
                {
                    Puts($"PVP time is ending! {Math.Round(timeDifferenceEnd.TotalMinutes)} minutes left.");
                    Broadcast(config.sche.pvppurgeEndingMsg.Replace("{TIMELEFT}", Math.Round(timeDifferenceEnd.TotalMinutes).ToString()));
                    SendPVPEndMessage();
                    DiscordMessageSent = true;
                }
            }

        }

        private void Broadcast(string Message)
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player != null)
                    SendChatMessage(player, Message);
            }
        }

        #endregion

        #region Zone Manager 

        private List<string> GetAllZones(BaseEntity entity)
        {
            if (!rulesData.zoneRules.zoneManager || entity == null) return null;

            List<string> locations = new List<string>();

            List<string> zmloc = new List<string>();
            string zname;

            if (entity is BasePlayer player)
            {
                string[] zmlocplr = (string[])ZoneManager.Call("GetPlayerZoneIDs", new object[] { player });
                foreach (string s in zmlocplr)
                {
                    zmloc.Add(s);
                }
            }
            else if (entity.IsValid())
            {
                string[] zmlocent = (string[])ZoneManager.Call("GetEntityZoneIDs", new object[] { entity });
                foreach (string s in zmlocent)
                {
                    zmloc.Add(s);
                }
            }

            if (zmloc != null && zmloc.Count > 0)
            {
                foreach (string s in zmloc)
                {
                    locations.Add(s);
                    zname = (string)ZoneManager.Call("GetZoneName", s);
                    if (zname != null) locations.Add(zname);
                }
            }

            foreach (var zones in config.excludedZoneIDs)
            {
                if (ZoneManager.Call("CheckZoneID", zones) != null)
                {
                    zname = (string)ZoneManager.Call("GetZoneName", zones);

                    if (zname != null)
                    {
                        locations.RemoveAll(x => x == zones || x == zname);
                    }
                }
            }

            return locations;

        }

        private bool PlayerInPVPZone(BasePlayer player)
        {
            string[] playerZoneINRightNow = (string[])ZoneManager.Call("GetPlayerZoneIDs", new object[] { player });

            if (playerZoneINRightNow == null || playerZoneINRightNow.Length == 0)
            {
                return false;
            }

            foreach (string zone in playerZoneINRightNow)
            {
                if (!config.excludedZoneIDs.Contains(zone))
                {
                    return true;
                }
            }

            return false;
        }

        private bool PlayersIsInZone(BasePlayer player)
        {
            string[] playerZoneINRightNow = (string[])ZoneManager.Call("GetPlayerZoneIDs", new object[] { player });

            if (playerZoneINRightNow != null && playerZoneINRightNow.Length > 0)
            {
                return true;
            }

            return false;
        }

        private void OnEnterZone(string zoneID, BasePlayer player)
        {
            RemovePVPDelay(player.userID);

            if (PlayersIsInZone(player) && rulesData.zoneRules.disableRulesZone && rulesData.zoneRules.zoneManager && config.pveEnabledConfig && !config.excludedZoneIDs.Contains(zoneID))
            {
                RemovePVEPanel(player);
                NextTick(() => ShowPVPUi(player));
            }
        }

        private void OnExitZone(string ZoneID, BasePlayer player) // Called when a player leaves a zone
        {
            if (!config.pveEnabledConfig)
            {
                RemovePVPPanel(player);
                RemovePVEPanel(player);
                NextTick(() => ShowPVPUi(player));
                return;
            }

            if (config.excludedZoneIDs.Contains(ZoneID))
            {
                return;
            }

            var delay = GetToZone(player, ZoneID);

            int delayTime = rulesData.zoneRules.enablePVPDelay ? config.delaySetting.pvpdelaytime : 0;

            if (rulesData.zoneRules.enablePVPDelay && config.delaySetting.pvpdelaytime > 0 && config.delaySetting.pvpDelayMsgEnabled)
            {
                SendChatMessage(player, config.delaySetting.pvpDelayMessage.Replace("{delay}", config.delaySetting.pvpdelaytime.ToString()));
            }

            //if (config.zoneRules.enablePVPDelay && delayTime > 0) ShowPVPUi(player);

            delay.DelayTimer = timer.Once(delayTime, () =>
            {
                inPVPBase.Remove(player.userID);

                RemovePVPDelay(player.userID);

                if (config.pveEnabledConfig)
                {
                    RemovePVPPanel(player);
                    NextTick(() => ShowPVEUI(player));
                }
                else
                {
                    RemovePVEPanel(player);
                    NextTick(() => ShowPVPUi(player));
                }
            });
        }

        //Raidable bases exclude
        private HashSet<ulong> inRaidableBasesZone = new HashSet<ulong>();
        private HashSet<ulong> inPVPBase = new HashSet<ulong>();

        private void OnPlayerEnteredRaidableBase(BasePlayer player, Vector3 raidPos, bool allowPVP, int mode)
        {
            inRaidableBasesZone.Add(player.userID);

            RemovePVEPanel(player);
            RemovePVPPanel(player);
        }

        private void OnPlayerExitedRaidableBase(BasePlayer player, Vector3 raidPos, bool allowPVP, int mode)
        {
            inRaidableBasesZone.Remove(player.userID);

            if (config.pveEnabledConfig)
            {
                ShowPVEUI(player);
            }
            else
            {
                ShowPVPUi(player);
            }
        }

        private void OnPlayerPvpDelayExpired(BasePlayer target)
        {
            if (config.pveEnabledConfig)
            {
                ShowPVEUI(target);
            }
            else
            {
                ShowPVPUi(target);
            }
        }
        /*private void OnPlayerEnteredRaidableBase(BasePlayer player, Vector3 raidPos, bool allowPVP, int mode)
        {
            inRaidableBasesZone.Add(player.userID);
            if (allowPVP)
            {
                Puts("added to pvp base");
                inPVPBase.Add(player.userID);
                RemovePVPDelay(player.userID);
            }

            RemovePVEPanel(player);
            RemovePVPPanel(player);
        }

        private void OnPlayerExitedRaidableBase(BasePlayer player, Vector3 raidPos, bool allowPVP, int mode)
        {
            inRaidableBasesZone.Remove(player.userID);

            if (allowPVP)
            {
                Puts("remove from pvp base");
                OnExitZone(raidPos.ToString(), player);                 
            }
            else
            {
                if (config.pveEnabledConfig)
                {
                    ShowPVEUI(player);
                }
                else
                {
                    ShowPVPUi(player);
                }
            }
        }*/

        private PVPDelay GetToZone(BasePlayer player, string ZoneID)
        {
            PVPDelay Delay;
            if (pvp_delays.TryGetValue(player.userID, out Delay))
            {
                Delay.DelayTimer?.Destroy();
            }
            else
            {
                Delay = new PVPDelay();
                pvp_delays.Add(player.userID, Delay);
            }
            Delay.ZoneID = ZoneID;

            return Delay;
        }

        private void RemovePVPDelay(ulong userID)
        {
            if (pvp_delays.TryGetValue(userID, out PVPDelay Delay))
            {
                Delay.DelayTimer?.Destroy();
                pvp_delays.Remove(userID);
            }
        }

        private bool IsSafeZone(string zoneID)
        {
            Dictionary<string, string> fields = (Dictionary<string, string>)ZoneManager.Call("ZoneFieldList", zoneID);

            if (fields != null && fields.ContainsKey("safezone"))
            {
                string safezoneValue = fields["safezone"];
                bool safezoneBoolValue;
                if (bool.TryParse(safezoneValue, out safezoneBoolValue))
                {
                    return safezoneBoolValue;
                }
            }

            return false;
        }

        #endregion

        #region HOOKS

        private void OnEngineStarted(BaseVehicle vehicle, BasePlayer driver)
        {
            if (!config.vehicleAutoClaim) return;

            if (vehicle == null || driver == null) return;

            if (vehicle.OwnerID != 0UL)
                return;
            if (driver.IsBuildingBlocked())
            {
                if (config.vehicleAutoClaimShowMessage)
                    SendChatMessage(driver, "You cannot claim a vehicle while building blocked.");

                return;
            }
            BaseCombatEntity baseCombatEntity = GetAppropriateVehicle(vehicle as BaseEntity);
            baseCombatEntity.OwnerID = driver.userID;
            if (config.vehicleAutoClaimShowMessage)
                SendChatMessage(driver, "You have claimed this vehicle.");
        }

        private object OnWallpaperRemove(BuildingBlock block, int side)
        {
            if (block == null || block.IsDestroyed || !config.pveEnabledConfig)
                return null;

            if (rulesData?.gRules.aWallpaperDamage == true) 
                return null;

            switch (side)
            {
                case 0:
                    {
                        if (block.wallpaperHealth <= 0f)
                        {
                            block.wallpaperHealth = block.health;
                            return true;
                        }
                        break;
                    }
                case 1:
                    {
                        if (block.wallpaperHealth2 <= 0f)
                        {
                            block.wallpaperHealth2 = block.health;
                            return true;
                        }
                        break;
                    }
            }
            return null;
        }
        private object OnCodeEntered(CodeLock codeLock, BasePlayer player, string code)
        {
            if (rulesData.gRules.aCodelockRaid)
                return null;

            if (!IsAlliedWithPlayer(codeLock.GetParentEntity().OwnerID, player.userID))
            {
                SendChatMessage(player, "You are not in a team with the player.");
                return true;
            }

            return null;
        }

        private void OnPlayerRespawned(BasePlayer player)
        {
            if (player == null || !player.userID.IsSteamId())
                return;

            RemovePVPDelay(player.userID);
        }

        //Mlrs damages disable or enable
        private void OnEntitySpawned(MLRSRocket rocket)
        {
            if (rocket.IsRealNull()) return;

            // Find all MLRS systems in the vicinity
            var systems = FindEntitiesOfType<MLRS>(rocket.transform.position, 15f);

            if (systems.Count == 0 || CheckIsEventTerritory(systems[0].TrueHitPos)) return;

            // Get the owner of the MLRS system
            var owner = systems[0].rocketOwnerRef.Get(true) as BasePlayer;

            if (owner.IsRealNull()) return;

            // Check if the MLRS rocket's owner is the owner of entities within a certain radius
            DamageEntitiesAroundOwner(rocket, owner);
        }

        private void DamageEntitiesAroundOwner(MLRSRocket rocket, BasePlayer owner)
        {
            // Find all entities within a certain radius of the MLRS rocket
            var entities = FindEntitiesInRadius<BaseEntity>(rocket.transform.position, 15f);

            foreach (var entity in entities)
            {
                if ((entity.OwnerID > 0 || entity.OwnerID == owner.userID || IsAlliedWithPlayer(entity.OwnerID, owner.userID)) && !rulesData.entityRules.MlrsHurtPB)
                {
                    // Allow the MLRS rocket to damage this entity
                    rocket.SetDamageScale(0f);
                    rocket.damageTypes.Clear();
                }
                else
                {

                }
            }
        }

        private static List<T> FindEntitiesOfType<T>(Vector3 a, float n, int m = -1) where T : BaseEntity
        {
            int hits = UnityEngine.Physics.OverlapSphereNonAlloc(a, n, Vis.colBuffer, m, QueryTriggerInteraction.Collide);
            List<T> entities = new List<T>();
            for (int i = 0; i < hits; i++)
            {
                var entity = Vis.colBuffer[i]?.ToBaseEntity();
                if (entity is T) entities.Add(entity as T);
                Vis.colBuffer[i] = null;
            }
            return entities;
        }

        private static List<T> FindEntitiesInRadius<T>(Vector3 position, float radius, int layerMask = -1) where T : BaseEntity
        {
            Collider[] colliders = UnityEngine.Physics.OverlapSphere(position, radius, layerMask, QueryTriggerInteraction.Collide);
            List<T> entities = new List<T>();

            foreach (var collider in colliders)
            {
                var entity = collider.GetComponentInParent<T>();
                if (entity != null)
                {
                    entities.Add(entity);
                }
            }

            return entities;
        }

        private bool CheckIsEventTerritory(Vector3 position)
        {
            if (AbandonedBases != null && Convert.ToBoolean(AbandonedBases?.Call("EventTerritory", position))) return true;
            if (RaidableBases != null && Convert.ToBoolean(RaidableBases?.Call("EventTerritory", position))) return true;
            return false;
        }

        //bear & landmine trap ignores if true
        private object OnTrapTrigger(BaseTrap t, GameObject g)
        {
            var p = g.GetComponent<BasePlayer>();

            if (p == null || t == null)
            {
                return null;
            }

            if (Interface.CallHook("CanEntityTrapTrigger", new object[] { t, p }) is bool val)
            {
                return val ? (object)null : true;
            }

            if (inRaidableBasesZone.Contains(p.userID)) return null;

            if (p.IsNpc || !p.userID.IsSteamId())
            {
                return null;
            }
            else if (p.userID.IsSteamId() && rulesData.entityRules.trapD)
            {
                return false;
            }
            else return null;
        }

        //Gun trap ignore players if true
        private object CanBeTargeted(BasePlayer t, GunTrap g)
        {
            if (t == null || g == null) return null;

            try
            {
                object extCanEntityBeTargeted = Interface.CallHook("CanEntityBeTargeted", new object[] { t, g });
                if (!ReferenceEquals(extCanEntityBeTargeted, null) && extCanEntityBeTargeted is bool && (bool)extCanEntityBeTargeted)
                {
                    return null;
                }
            }
            catch { }

            if (inRaidableBasesZone.Contains(t.userID)) return null;

            if (t.IsNpc || !t.userID.IsSteamId())
            {
                return null;
            }
            else if (g.OwnerID == 0)
            {
                return null;
            }
            else if (t.userID.IsSteamId() && rulesData.entityRules.trapD)
            {
                return false;
            }
            else return null;
        }

        //flame turret ignore player if true
        private object CanBeTargeted(HashSet<BaseEntity>.Enumerator enumerator, FlameTurret turret)
        {
            if (turret == null) return null;

            if (!enumerator.Current) return null;

            BaseEntity entity = enumerator.Current;
            if (entity == null) return null;

            BasePlayer player = entity.GetComponent<BasePlayer>();
            if (player == null) return null;

            return CanBeTargeted(player, turret);
        }

        private object CanBeTargeted(BasePlayer t, FlameTurret g)
        {
            if (t == null || g == null) return null;

            try
            {
                object extCanEntityBeTargeted = Interface.CallHook("CanEntityBeTargeted", new object[] { t, g });
                if (!ReferenceEquals(extCanEntityBeTargeted, null) && extCanEntityBeTargeted is bool && (bool)extCanEntityBeTargeted)
                {
                    return null;
                }
            }
            catch { }

            if (inRaidableBasesZone.Contains(t.userID)) return null;

            if (t.IsNpc || !t.userID.IsSteamId())
            {
                return null;
            }
            else if (g.OwnerID == 0)
            {
                return null;
            }
            else if (t.userID.IsSteamId() && rulesData.entityRules.trapD)
            {
                return false;
            }
            else return null;
        }

        //auto turrets ignore players if true
        private object CanBeTargeted(BasePlayer t, AutoTurret g)
        {
            if (t == null || g == null) return null;

            try
            {
                object extCanEntityBeTargeted = Interface.CallHook("CanEntityBeTargeted", new object[] { t, g });
                if (!ReferenceEquals(extCanEntityBeTargeted, null) && extCanEntityBeTargeted is bool && (bool)extCanEntityBeTargeted)
                {
                    return null;
                }
            }
            catch { }

            if (inRaidableBasesZone.Contains(t.userID)) return null;

            /*sif (ZoneManagerCheck(t, g as BaseEntity as AQpkBQN=))
            {
                return null;
            }*/
        
            if (t.IsNpc || !t.userID.IsSteamId() || t.ShortPrefabName.Contains("zombie"))
            {
                return null;
            }
            else if (g.OwnerID == 0)
            {
                return null;
            }
            else if (t.userID.IsSteamId() && rulesData.entityRules.trapD)
            {
                return false;
            }
            else return null;
        }

        /*private object OnEntityEnter(TargetTrigger trigger, BasePlayer target)
        {          
            if (trigger == null || target == null)
            {
                return null;
            }

            var entity = trigger.gameObject.ToBaseEntity();
            if (!entity.IsValid())
            {
                return null;
            }
            return TriggerdEntity(entity, target);
        }

        private object TriggerdEntity(BaseEntity entity, BasePlayer target)
        {
            Puts("TargetTrigger entered " + entity.ShortPrefabName + " Target " + target.ShortPrefabName);
            if (Interface.CallHook("CanEntityBeTargeted", new object[] { target, entity }) is bool val)
            {
                return val ? (object)null : true;
            }

            if (entity.OwnerID == 0 && target.userID.IsSteamId() && (entity is FlameTurret or AutoTurret) && !entity.HasParent())
            {
                if (entity is NPCAutoTurret && target.InSafeZone()) return true;
                return null;
            }           

            if (!target.userID.IsSteamId())
            {
                return null;
            }
            else if (entity is NPCAutoTurret && entity.OwnerID == 0)
            {
                return true;
            }
            else if (target.userID.IsSteamId() && rulesData.entityRules.trapD)
            {
                return true;
            }

            return null;
        }*/

        //NPC and Player sam site ignores if true
        /*private object OnSamSiteTarget(SamSite sam, BaseMountable mountable)
        {
            Puts("targetting sam site");
            if (sam == null) return null;
            if (mountable == null) return null;

            BasePlayer p = GetMountedPlayer(mountable);

            if (p.IsValid())
            {
                try
                {
                    object extCanEntityBeTargeted = Interface.CallHook("CanEntityBeTargeted", new object[] { p, sam });
                    if (!ReferenceEquals(extCanEntityBeTargeted, null) && extCanEntityBeTargeted is bool && (bool)extCanEntityBeTargeted)
                    {
                        return null;
                    }
                }
                catch { }

                if (inRaidableBasesZone.Contains(p.userID)) return null;
                //npc sam to player
                if (sam.OwnerID == 0 && rulesData.npcRules.NSamigP)
                {
                    return true;
                    //player sam to player
                }
                else if (sam.OwnerID > 0 && rulesData.npcRules.PSamigP)
                {
                    return true;
                }
                else if (IsAlliedWithPlayer(sam.OwnerID, p.userID))
                {
                    return true;
                }
            }
            else { return true; }

            return null;
        }*/

        private object OnSamSiteTarget(BaseEntity attacker, BaseEntity entity)
        {
            SamSite ss = attacker as SamSite;
            if (Interface.CallHook("CanEntityBeTargeted", new object[] { entity, attacker }) is bool val)
            {
                if (val)
                {
                    return null;
                }

                if (ss != null)
                {
                    ss.CancelInvoke(ss.WeaponTick);
                }
                return true;
            }
            BasePlayer p = GetMountedPlayer(entity?.GetComponent<BaseMountable>());

            if (p.IsValid())
            {
                //npc sam to player
                if (attacker.OwnerID == 0 && rulesData.npcRules.NSamigP)
                {
                    return true;
                    //player sam to player
                }
                else if (attacker.OwnerID > 0 && rulesData.npcRules.PSamigP)
                {
                    return true;
                }
            }

            return null;
        }

        /*private object OnEntityEnter(TargetTrigger trigger, BasePlayer player)
        {
            if (trigger == null || player == null)
                return null;

            var entity = trigger.gameObject.ToBaseEntity();

            if (!entity.IsValid())
                return null;

            return EntityTriggerable(entity, player);
        }

        private object EntityTriggerable(BaseEntity entity, BasePlayer player)
        {
            if (Interface.CallHook("CanEntityBeTargeted", new object[] { player, entity }) is bool val)
            {
                return val ? (object)null : true;
            }


        }*/

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null || !player.userID.IsSteamId()) return;

            if (config.displayNotify.showPvEOverlay && config.pveEnabledConfig)
            {
                ShowPVEUI(player);
            }
            else
            {
                ShowPVPUi(player);
            }

            if (config.statusUI.status && SimpleStatus)
            {
                SetStatus(player.UserIDString, StatusKey);
            }
        }

        private BasePlayer GetMountedPlayer(BaseMountable m)
        {
            if (m.GetMounted())
            {
                return m.GetMounted();
            }

            if (m as BaseVehicle)
            {
                BaseVehicle v = m as BaseVehicle;

                foreach (BaseVehicle.MountPointInfo point in v.mountPoints)
                {
                    if (point.mountable.IsValid() && point.mountable.GetMounted())
                    {
                        return point.mountable.GetMounted();
                    }
                }

            }
            return null;
        }

        private Dictionary<string, string> Images = new Dictionary<string, string>();

        private void AddImage(string url)
        {
            if (!ImageLibrary.Call<bool>("HasImage", url)) ImageLibrary.Call("AddImage", url, url);
            timer.In(0.1f, () => { Images.Add(url, ImageLibrary.Call<string>("GetImage", url)); });
        }

        private void LoadImages()
        {
            if (ImageLibrary == null)
            {
                PrintError("[ImageLibrary] not found!");
                return;
            }
            AddImage(config.displayNotify.warningUI.WarningImgUrl);
            AddImage(config.displayNotify.lPUISetting.WarningImgUrl);
            AddImage("https://i.postimg.cc/SxC1PDyF/Untitled-design.png");
            AddImage(config.statusUI.peStatus.PVEStatus.icon);
            AddImage(config.statusUI.peStatus.PVPStatus.icon);
            AddImage(config.statusUI.purgeStatus.purgepvpStatus.icon);
            AddImage(config.statusUI.purgeStatus.purgepveStatus.icon);
        }

        private string GetImage(string url)
        {
            return Images[url];
        }

        private BaseCombatEntity GetAppropriateVehicle(BaseEntity entity)
        {
            var vehicleModule = entity as BaseVehicleModule;
            if ((object)vehicleModule != null)
                return vehicleModule.Vehicle;

            var carLift = entity as ModularCarGarage;
            if ((object)carLift != null)
                return carLift.carOccupant;

            return entity as BaseCombatEntity;
        }

        #endregion

        #region StatusSystem

        private List<(Setting statusConfig, double timeRemaining, string message)> statusUI = new List<(Setting, double, string)>();
        private string StatusKey = "SimplePVEStatus";
        private void AddSimpleStatus()
        {
            statusUI.Clear();

            /* // Zone status check
            if (ins.config.statusUI.zoneStatus.status && ins.ZoneManager != null)
            {
                if (ins.PlayerInPVPZone(player) && ins.rulesData.zoneRules.disableRulesZone
                    && ins.rulesData.zoneRules.zoneManager && ins.config.pveEnabledConfig
                    && ins.config.statusUI.zoneStatus.pvparea.status)
                {
                    statusUI.Add((ins.config.statusUI.zoneStatus.pvparea, 0, ins.config.statusUI.zoneStatus.pvparea.strings.GetRandom()));
                }
            }*/

            // PVE/PVP status check
            if (ins.config.statusUI.peStatus.status)
            {
                var status = ins.config.pveEnabledConfig ? ins.config.statusUI.peStatus.PVEStatus : ins.config.statusUI.peStatus.PVPStatus;
                var msg = ins.config.pveEnabledConfig ? ins.config.statusUI.peStatus.PVEStatus.strings.GetRandom() : ins.config.statusUI.peStatus.PVPStatus.strings.GetRandom();
                if (status.status)
                {
                    statusUI.Add((status, 0, msg));
                }
            }

            // Purge status check
            if (ins.config.statusUI.purgeStatus.status)
            {
                var timepvp = ins.CalculateTimeDifference();
                var timepve = ins.CalculateTimeDifferenceToEnd();
                if (ins.config.statusUI.purgeStatus.purgepvpStatus.status && timepvp.TotalSeconds > 0)
                {
                    statusUI.Add((ins.config.statusUI.purgeStatus.purgepvpStatus, timepvp.TotalSeconds, ins.config.statusUI.purgeStatus.purgepvpStatus.strings.GetRandom()));
                }
                else if (ins.config.statusUI.purgeStatus.purgepveStatus.status && timepve.TotalSeconds > 0)
                {
                    statusUI.Add((ins.config.statusUI.purgeStatus.purgepveStatus, timepve.TotalSeconds, ins.config.statusUI.purgeStatus.purgepveStatus.strings.GetRandom()));
                }
            }

            var random = new System.Random();
            int index = random.Next(statusUI.Count);
            var selectedStatus = statusUI[index];
            var timeRemaining = selectedStatus.timeRemaining;
            Setting statusSetting = selectedStatus.statusConfig;

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player != null)
                {
                    SetStatusProperty(player.UserIDString, StatusKey, new Dictionary<string, object>
                    {
                        ["color"] = Color(statusSetting.statusColor, 1),
                        ["title"] = selectedStatus.message.Replace("{TIMER}", ins.FormatTime(timeRemaining)),
                        ["titleColor"] = Color(statusSetting.textColor, 1),
                        ["icon"] = statusSetting.icon,
                        ["iconColor"] = Color(statusSetting.iconColor, 1),
                    });
                    //DebugStatusProperties(player.UserIDString, StatusKey, statusProperties);
                }
            }

        }

        private void DebugStatusProperties(string userId, string statusId, Dictionary<string, object> properties)
        {
            string debugMessage = $"Setting status '{statusId}' for player '{userId}' with properties:\n";
            foreach (var property in properties)
            {
                debugMessage += $"{property.Key}: {property.Value}\n";
            }
            Puts(debugMessage);
        }

        private void InitializeStatus()
        {
            if (!config.statusUI.status) return;
            if (!SimpleStatus) return;

            SimpleStatus.CallHook("CreateStatus", this, StatusKey, new Dictionary<string, object>
            {
                ["color"] = Color("#B22222", 1),
                ["title"] = "Starting...",
                ["titleColor"] = "0.91373 0.77647 0.75686 1",
                ["icon"] = "assets/icons/home.png",
                ["iconColor"] = "0.91373 0.77647 0.75686 1"
            });
        }

        private bool StatusSet = false;

        private void InitializeStatusPlayers()
        {
            if (!StatusSet)
            {
                foreach (var player in BasePlayer.activePlayerList)
                {
                    if (player != null)
                    {
                        SetStatus(player.UserIDString, StatusKey, int.MaxValue);
                    }
                }
                StatusSet = true;
            }
        }

        private void SetStatusProperty(string userId, string statusId, Dictionary<string, object> properties) => SimpleStatus?.CallHook("SetStatusProperty", userId, statusId, properties);

        private void SetStatus(string userId, string statusId, int duration = int.MaxValue, bool pauseOffline = true)
        {
            SimpleStatus.CallHook("SetStatus", userId, statusId, duration, pauseOffline);
        }

        #endregion

        #region Discord

        private void SendDiscordMessage(string webhook, string title, List<string> embeds, string color, bool inline = false)
        {
            int dColor = HexToDiscordColor(color);

            Embed embed = new Embed();
            foreach (var item in embeds)
            {
                embed.AddField(title, item, inline, dColor);
            }

            webrequest.Enqueue(webhook, new DiscordMessage("@everyone", embed).ToJson(), (code, response) => { },
                this,
                RequestMethod.POST, new Dictionary<string, string>
                {
                    { "Content-Type", "application/json" }
                });
        }

        private class DiscordMessage
        {
            public DiscordMessage(string content, params Embed[] embeds)
            {
                Content = content;
                Embeds = embeds.ToList();
            }

            [JsonProperty("content")] public string Content { get; set; }
            [JsonProperty("embeds")] public List<Embed> Embeds { get; set; }

            public string ToJson()
            {
                return JsonConvert.SerializeObject(this);
            }
        }

        private class Embed
        {
            public int color
            {
                get; set;
            }
            [JsonProperty("fields")] public List<Field> Fields { get; set; } = new List<Field>();

            public Embed AddField(string name, string value, bool inline, int colors)
            {
                Fields.Add(new Field(name, Regex.Replace(value, "<.*?>", string.Empty), inline));
                color = colors;
                return this;
            }
        }

        private class Field
        {
            public Field(string name, string value, bool inline)
            {
                Name = name;
                Value = value;
                Inline = inline;
            }

            [JsonProperty("name")] public string Name { get; set; }
            [JsonProperty("value")] public string Value { get; set; }
            [JsonProperty("inline")] public bool Inline { get; set; }
        }
        public int HexToDiscordColor(string hexColor)
        {
            // Remove the '#' if present
            hexColor = hexColor.TrimStart('#');

            // Parse the hex color string to a 32-bit integer
            int colorValue = int.Parse(hexColor, System.Globalization.NumberStyles.HexNumber);

            // Discord uses a 24-bit color representation, so discard the alpha channel
            colorValue &= 0xFFFFFF;

            return colorValue;
        }

        #endregion

        #region APIs

        [HookMethod("GetPVPStartTimeRemaining")]
        public TimeSpan GetPVPStartTimeRemaining()
        {
            return CalculateTimeDifference();
        }

        [HookMethod("GetPVPEndTimeRemaining")]
        public TimeSpan GetPVPEndTimeRemaining()
        {
            return CalculateTimeDifferenceToEnd();
        }

        [HookMethod("API_SetProperty")]
        private void API_SetProperty(string PropertyName)
        {
            SetProperty(PropertyName);
        }

        [HookMethod("AddToExcludeZone")]
        public bool AddToExcludeZone(string ID)
        {
            if (string.IsNullOrEmpty(ID) || config == null || config.excludedZoneIDs == null)
                return false;

            if (!config.excludedZoneIDs.Contains(ID))
            {
                config.excludedZoneIDs.Add(ID);
                SaveConfig();
            }
            return true;
        }

        [HookMethod("RemoveFromExcludeZone")]
        public bool RemoveFromExcludeZone(string ID)
        {
            if (config.excludedZoneIDs.Remove(ID))
            {
                SaveConfig();
                return true;
            }
            return false;
        }

        [HookMethod("IsStatusEnabled_API")]
        public bool IsStatusEnabled_API()
        {
            return config.statusUI.status;
        }

        [HookMethod("IsServerPVE")]
        public bool IsServerPVE()
        {
            return config.pveEnabledConfig;
        }

        [HookMethod("AddToExcludedList")]
        public void AddToExcludedList(ulong Value) //not working
        {
            try
            {
                excludedBaseEntity.Add(Value);
            }catch (Exception ex)
            {
                PrintWarning(ex.Message);
            }
        }

        #endregion


    }
}
 