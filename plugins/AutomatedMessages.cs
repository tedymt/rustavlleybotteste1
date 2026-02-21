using System;
using ConVar;
using Newtonsoft.Json;
using System.Globalization;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Text;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using UnityEngine.UI;
using Oxide.Core.Libraries;
using Oxide.Plugins.AutomatedMessagesMethods;

/*2.2.4
 * Fixed PlaceholderAPI placeholders not processing properly
*/

//TODO: Extend Timed Trigger to support static and dynamic time modes 
//TODO: Add formatting tips in reply editor
//TODO: Ability to copy a reply and paste in another (admin ui)
//TODO: Add player data settings to admin ui

namespace Oxide.Plugins
{
    [Info("AutomatedMessages", "beee", "2.2.4")]
    [Description("Automated chat messages based on triggers or repeating interval.")]
    class AutomatedMessages : RustPlugin
    {
        #region Fields

        private PluginConfig _config;
        private List<Timer> _timers;

        private const string PREFIX_SHORT = "am";
        private const string PREFIX_LONG = "automatedmessages";
        private const string PERM_ADMIN = $"{PREFIX_LONG}.admin";

        private Dictionary<Regex, Config_Action> _autoReplyPatterns;

        [PluginReference]
        private Plugin PlaceholderAPI;

        #endregion

        #region Load/Unload Hooks

        private void Init()
        {
            UnsubscribeHooks();
            _timers = new();
        }

        private void OnServerInitialized()
        {
            LoadPlayersData();
            ProcessConfig();
            DisableNonfunctionalTriggers();
            RegisterPermissions();
            InitTimers();
            InitAutoReply();
            SubscribeHooks();
            RegisterConfigCommands();
            SetUnsetCountries();
            RegisterCommand($"{PREFIX_SHORT}.ui.texteditor", nameof(LongInputCMD));
        }

        private void Unload()
        {
            DestroyTimers();
            SaveData();

            foreach (var player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, uimodal);
                CuiHelper.DestroyUi(player, chatpreview);
                CuiHelper.DestroyUi(player, gametippreview);
            }

            _timers = null;
        }

        #endregion

        #region Functions

        private void DisableNonfunctionalTriggers()
        {
            if (_config.PlayerDataSettings.Disabled)
            {
                TriggerDefinitions["NewPlayerJoined"].Enabled = false;
                TriggerDefinitions["NewPlayerJoined"].DisableReason = "Player data disabled";
            }
            else
                TriggerDefinitions["NewPlayerJoined"].Enabled = true;

            if (!plugins.Exists("ZoneManager"))
            {
                TriggerDefinitions["EnteredZone"].Enabled = false;
                TriggerDefinitions["EnteredZone"].DisableReason = "Requires Zone Manager";
                TriggerDefinitions["LeftZone"].Enabled = false;
                TriggerDefinitions["LeftZone"].DisableReason = "Requires Zone Manager";
            }
            else
            {
                TriggerDefinitions["EnteredZone"].Enabled = true;
                TriggerDefinitions["LeftZone"].Enabled = true;
            }

            if (!plugins.Exists("MonumentsWatcher"))
            {
                TriggerDefinitions["EnteredMonument"].Enabled = false;
                TriggerDefinitions["EnteredMonument"].DisableReason = "Requires Monuments Watcher";
                TriggerDefinitions["LeftMonument"].Enabled = false;
                TriggerDefinitions["LeftMonument"].DisableReason = "Requires Monuments Watcher";
            }
            else
            {
                TriggerDefinitions["EnteredMonument"].Enabled = true;
                TriggerDefinitions["LeftMonument"].Enabled = true;
            }
        }

        private void RegisterPermissions()
        {
            permission.RegisterPermission(PERM_ADMIN, this);
        }

        private void InitTimers()
        {
            DestroyTimers();
            _timers = new();

            int count = 0;
            foreach (var action in _config.Actions.FindAll(s => s.IsEnabled() && s.Type == "Timed"))
            {
                Lang_Action defaultLangAction = action.CachedLangActions[_config.RepliesLangSettings.DefaultLang];
                if (defaultLangAction.Replies == null || defaultLangAction.Replies.Count == 0)
                    continue;

                if (action.Interval <= 0)
                    continue;

                var repeatTimer = timer.Repeat(action.Interval * 60, 0, () => ProcessReply(action, defaultLangAction: defaultLangAction));
                _timers.Add(repeatTimer);
                count++;
            }
            Puts($"Started {count} chat timer{(count == 1 ? "" : "s")}.");
        }

        private void InitAutoReply()
        {
            _autoReplyPatterns = new();

            int count = 0;
            foreach (var message in _config.Actions.FindAll(s => s.IsEnabled() && s.Type == "AutoReply"))
            {
                Lang_Action defaultLangAction = message.CachedLangActions[_config.RepliesLangSettings.DefaultLang];
                if (defaultLangAction.Replies == null || defaultLangAction.Replies.Count == 0)
                    continue;

                if (string.IsNullOrEmpty(message.Target))
                    continue;

                foreach (var set in message.Target.Split('|'))
                {
                    string regexPattern = "";

                    foreach (var keyword in set.Split(','))
                        regexPattern += $@"(?=.*{(keyword.StartsWith("!") ? "^!" : "")}\b{keyword.TrimStart('!').Trim()}\b)";

                    if (regexPattern == "")
                        continue;

                    regexPattern = $"^{regexPattern}.*$";

                    _autoReplyPatterns.Add(new (regexPattern, RegexOptions.IgnoreCase), message);
                    count++;
                }
            }
            Puts($"Cached {count} auto reply keyword pattern{(count == 1 ? "" : "s")}.");
        }

        List<string> _registeredConfigCommands;
        private void RegisterConfigCommands()
        {
            _registeredConfigCommands = new();

            if (!_config.PlayerDataSettings.Disabled)
            {
                RegisterCommand(_config.ToggleCommand, nameof(TipsToggleChatCMD));
                _registeredConfigCommands.Add(_config.ToggleCommand);
            }
            
            int count = 0;
            foreach (var message in _config.Actions.FindAll(s => s.IsEnabled() && s.Type == "ChatCommand"))
            {
                Lang_Action defaultLangAction = message.CachedLangActions[_config.RepliesLangSettings.DefaultLang];
                if (defaultLangAction.Replies == null || defaultLangAction.Replies.Count == 0)
                    continue;

                if (string.IsNullOrEmpty(message.Target))
                    continue;

                string command = message.Target.Replace("/", "").Trim().ToLower();

                foreach (string com in command.Split(',').Select(s => s.Trim()))
                {
                    if (string.IsNullOrEmpty(com))
                        continue;

                    RegisterCommand(com, nameof(CustomChatCMD));
                    _registeredConfigCommands.Add(com);

                    count++;
                }
            }
            Puts($"Registered {count} chat command{(count == 1 ? "" : "s")}.");
        }

        private void UnregisterCachedCommands()
        {
            if (_registeredConfigCommands == null) return;

            var library = GetLibrary<Covalence>();
            foreach (var command in _registeredConfigCommands)
                library.UnregisterCommand(command, this);

            _registeredConfigCommands.Clear();
        }

        private void RegisterCommand(string command, string callback, string perm = null)
        {
            if (!string.IsNullOrEmpty(command) && !command.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(perm))
                    AddCovalenceCommand(command, callback);
                else
                    AddCovalenceCommand(command, callback, perm);
            }
        }

        private void SetUnsetCountries()
        {
            if(_config.PlayerDataSettings.Disabled) return;
            
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (!pData.Players.TryGetValue(player.userID, out var pinfo))
                    pData.Players.Add(player.userID, pinfo = new PlayerInfo { TipsActive = true, isNew = true });

                if (string.IsNullOrEmpty(pinfo.Country))
                    FetchConnectionCountry(player.Connection, pinfo);
            }
        }

        private void DestroyTimers()
        {
            foreach (var repeatTimer in _timers)
                repeatTimer?.Destroy();
        }

        private void ProcessReplies(BasePlayer player, List<string> triggersToProcess, string argument = "", bool onlySendToTeam = false)
        {
            if (!player.userID.IsSteamId()) return;

            //add player if not in data, and in that case set isNew to true to recognize for NewPlayerJoined trigger
            if(!pData.Players.TryGetValue(player.userID, out var pinfo))
                pData.Players.Add(player.userID, pinfo = new PlayerInfo() { TipsActive = true, isNew = true, justJoined = !HasActiveNewPlayerJoinedAction});
            
            //loop enabled actions with trigger in triggersToProcess
            foreach (var action in _config.Actions.FindAll(s => s.IsEnabled() && triggersToProcess.Contains(s.Type)))
            {
                Lang_Action defaultLangAction = action.CachedLangActions[_config.RepliesLangSettings.DefaultLang];
                //skip if default lang for this action does not have replies
                if (defaultLangAction.Replies == null || defaultLangAction.Replies.Count == 0) 
                    continue;

                bool canSend = false;

                if(TriggerDefinitions.TryGetValue(action.Type, out TriggerDefinition triggerDefinition))
                {
                    if(!triggerDefinition.Enabled) continue;
                    
                    if (triggerDefinition.RequiresTarget)
                    {
                        //skip action if it's trigger requires a target but target parameter is null/empty
                        if (string.IsNullOrEmpty(argument)) continue;

                        //trigger based conditions
                        switch (triggerDefinition.Key)
                        {
                            case "ChatCommand":
                                if (action.Target.Replace("/", "").ToLower().Split(',').Any(x => x.Trim() == argument))
                                    canSend = true;
                                break;
                            default:
                                if(action.Target == argument)
                                    canSend = true;
                                break;
                        }
                    }
                    else
                    {
                        //trigger based conditions
                        switch (triggerDefinition.Key)
                        {
                            case "NewPlayerJoined":
                                if (pinfo.isNew)
                                    canSend = true;
                                break;
                            case "PlayerConnected":
                                if (pinfo.justJoined)
                                    canSend = true;
                                break;
                            case "PlayerDead":
                                if (pinfo.wasDead && ((argument == "" && !action.IsGlobalBroadcast) || (argument == "FromEntityDeath" && action.IsGlobalBroadcast)))
                                {
                                    pinfo.wasDead = false;
                                    canSend = true;
                                }
                                break;
                            default:
                                canSend = true;
                                break;
                        }
                    }
                    
                    //skip if action is on cooldown
                    if (triggerDefinition.UsesGenericCooldown && action.OnCooldownAll)
                        continue;

                    //skip if trigger owner is on cooldown for this action
                    if (triggerDefinition.UsesPlayerCooldown && action.OnCooldown != null && action.OnCooldown.Contains(player.userID))
                        continue;
                }

                if (canSend)
                {
                    if (action.IsGlobalBroadcast) //sends to all eligible players
                        ProcessReply(action, defaultLangAction, player, onlySendToTeam);
                    else //sends to trigger owner
                    {
                        //check if player is eligible or has tips enabled
                        if ((action.PlayerCanDisable && !pinfo.TipsActive) || !MessageIsEligible(action, player.UserIDString))
                            continue;
                        
                        string playerLanguage = lang.GetLanguage(player.UserIDString);
                        //look for reply based on player language, if not use defaut server language
                        if (!Lang_TryGetAction(player, action.Type, action.Id, playerLanguage, out Lang_Action langAction) || langAction.Replies.Count == 0)
                            langAction = defaultLangAction;
                        
                        //double check to avoid index out of range (might be unnecessary)
                        if (langAction.ReplyIndex >= langAction.Replies.Count)
                            langAction.ReplyIndex = 0;

                        string reply = langAction.Replies[langAction.ReplyIndex];
                        IncrementReplyIndex(ref langAction);//increment reply index
                        
                        //define variables/placeholders
                        reply = DefineVariables(player, reply, triggerDefinition);
                        
                        //Log($"{action.Type} | ({player.displayName} | {player.UserIDString}) | {playerLanguage} | index({langAction.ReplyIndex}) | {reply}");
                        
                        //get icon steam id - reply custom icon > action custom icon > general settings custom icon
                        ulong iconSteamId = GetActionIcon(action, player, _config.IconSteamId);
                        timer.Once(0.5f, () => SendMessage(player, reply, action.SendInChat, iconSteamId, action.SendAsGameTip, action.IsBlueGameTip));
                    }
                    
                    //start cooldowns
                    switch(action.Type)
                    {
                        case "AutoReply":
                            RunActionCooldownAll(action, _config.CooldownSettings.AutoReplyCooldown);
                            break;
                        case "ChatCommand":
                            RunActionCooldownAll(action, _config.CooldownSettings.ChatCommandCooldown);
                            break;
                        case "EnteredZone":
                            RunActionCooldownPlayer(action, player, _config.CooldownSettings.ZoneManagerCooldown);
                            break;
                        case "LeftZone":
                            RunActionCooldownPlayer(action, player, _config.CooldownSettings.ZoneManagerCooldown);
                            break;
                        case "EnteredMonument":
                            RunActionCooldownPlayer(action, player, _config.CooldownSettings.MonumentWatcherCooldown);
                            break;
                        case "LeftMonument":
                            RunActionCooldownPlayer(action, player, _config.CooldownSettings.MonumentWatcherCooldown);
                            break;
                    }
                }
            }

            pinfo.isNew = false;
            pinfo.justJoined = false;
        }

        private void ProcessReply(Config_Action action, Lang_Action defaultLangAction, BasePlayer triggerOwner = null, bool onlySendToTeam = false)
        {
            //skip if trigger owner is admin and disables it
            if (triggerOwner && triggerOwner.IsAdmin && action.DontTriggerAdmin)
                return;

            //keep track of already incremented lang replies
            Dictionary<string, string> cachedLangsReplies = new();
            
            ulong iconSteamId = GetActionIcon(action, triggerOwner, _config.IconSteamId);

            foreach (var player in BasePlayer.activePlayerList)
            {
                if(triggerOwner && onlySendToTeam && triggerOwner.currentTeam != 0 && triggerOwner.currentTeam != player.currentTeam) continue;

                if (!pData.Players.TryGetValue(player.userID, out var pinfo))
                    pData.Players.Add(player.userID, pinfo = new PlayerInfo { TipsActive = true, isNew = true });

                if ((action.PlayerCanDisable && !pinfo.TipsActive) || !MessageIsEligible(action, player.UserIDString))
                    continue;

                string playerLanguage = lang.GetLanguage(player.UserIDString);
                
                if (!cachedLangsReplies.TryGetValue(playerLanguage, out string reply))
                {
                    bool retrievedCached = false;
                    
                    //look for reply based on player language, if not use default server lang action
                    if (!Lang_TryGetAction(player, action.Type, action.Id, playerLanguage, out Lang_Action playerlangAction) || playerlangAction.Replies.Count == 0)
                    {
                        //check if default lang reply was cached
                        if (cachedLangsReplies.TryGetValue(_config.RepliesLangSettings.DefaultLang, out reply))
                            retrievedCached = true;
                        else
                        {
                            playerLanguage = _config.RepliesLangSettings.DefaultLang;
                            playerlangAction = defaultLangAction;
                        }
                    }

                    if (!retrievedCached)
                    {
                        //double check to avoid index out of range (might be unnecessary)
                        if (playerlangAction.ReplyIndex >= playerlangAction.Replies.Count)
                            playerlangAction.ReplyIndex = 0;

                        reply = playerlangAction.Replies[playerlangAction.ReplyIndex];
                    
                        //increment reply index
                        IncrementReplyIndex(ref playerlangAction);
                        cachedLangsReplies.Add(playerLanguage, reply);
                    }
                }

                //define variables/placeholders
                reply = DefineVariables(triggerOwner ? triggerOwner : player, reply, TriggerDefinitions[action.Type]);
                
                //Log($"{action.Type} | ({player.displayName} | {player.UserIDString}) | {playerLanguage} | {reply}");

                timer.Once(0.5f, () => SendMessage(player, reply, action.SendInChat, iconSteamId, action.SendAsGameTip, action.IsBlueGameTip));
            }
        }
        
        private ulong GetActionIcon(Config_Action action, BasePlayer triggerOwner, string defaultIconSteamId)
        {
            ulong iconSteamId;

            if (triggerOwner != null && action.UseTriggerOwnerIcon)
                iconSteamId = triggerOwner.userID;
            else if (!ulong.TryParse(action.CustomIconSteamId, out iconSteamId) || iconSteamId <= 0)
                ulong.TryParse(defaultIconSteamId, out iconSteamId);

            return iconSteamId;
        }
        
        private void RunActionCooldownAll(Config_Action action, int cooldown)
        {
            if (cooldown <= 0) return;

            action.OnCooldownAll = true;
            timer.Once(cooldown, () =>
            {
                action.OnCooldownAll = false;
            });
        }

        private void RunActionCooldownPlayer(Config_Action action, BasePlayer player, int cooldown)
        {
            action.OnCooldown ??= new();
            if (cooldown <= 0 || action.OnCooldown.Contains(player.userID)) return;

            action.OnCooldown.Add(player.userID);
            timer.Once(cooldown, () =>
            {
                action.OnCooldown.Remove(player.userID);
            });
        }

        private void IncrementReplyIndex(ref Lang_Action lang_Action)
        {
            lang_Action.ReplyIndex++;
            if (lang_Action.ReplyIndex >= lang_Action.Replies.Count || lang_Action.ReplyIndex < 0)
                lang_Action.ReplyIndex = 0;
        }

        readonly List<string> AvailableVariables = new()
        {
            "{playername}", "{playerid}", "{playercountry}", "{wipetimeremaining}", "{online}",
            "{sleeping}", "{joining}"
        };
        
        private readonly Regex VariablePattern = new Regex(@"\{(\w+)(?::(\w+))?\}", RegexOptions.Compiled);

        private string DefineVariables(BasePlayer player, string message, TriggerDefinition triggerDefinition)
        {
            if (string.IsNullOrEmpty(message)) return string.Empty;

            var resolved = VariablePattern.Replace(message, match =>
            {
                var key = match.Groups[1].Value.ToLowerInvariant();
                var option = match.Groups[2].Success ? match.Groups[2].Value.ToLowerInvariant() : null;

                switch (key)
                {
                    case "playername":
                        return player.displayName;
                    case "playerid":
                        return player.UserIDString;
                    case "online":
                        return $"{BasePlayer.activePlayerList.Count}";
                    case "sleeping":
                        return $"{BasePlayer.sleepingPlayerList.Count}";
                    case "joining":
                        return $"{ServerMgr.Instance.connectionQueue.Joining + ServerMgr.Instance.connectionQueue.Queued}";
                    case "playercountry":
                        if (_config.PlayerDataSettings.Disabled) return "";
                        return pData.Players.TryGetValue(player.userID, out var pinfo) && !string.IsNullOrEmpty(pinfo.Country)
                            ? pinfo.Country
                            : "";
                    case "wipetimeremaining":
                        if (!WipeTimer.serverinstance) return "##";

                        var now = DateTimeOffset.UtcNow
                            .AddDays(WipeTimer.daysToAddTest)
                            .AddHours(WipeTimer.hoursToAddTest);
                        var wipe = WipeTimer.serverinstance.GetWipeTime(now);
                        var timeSpan = wipe - now;

                        return option switch
                        {
                            "d" => timeSpan.Days.ToString(),
                            "h" => timeSpan.Hours.ToString(),
                            "th" => ((int)timeSpan.TotalHours).ToString(),
                            _ => FormatTime(timeSpan, m: false, s: false),
                        };
                    case "hacklocation":
                        if (triggerDefinition.Key == "CrateHacked")
                            return pData.Players.TryGetValue(player.userID, out var pinfo2) && !string.IsNullOrEmpty(pinfo2.HackLocation)
                                ? pinfo2.HackLocation
                                : MapHelper.PositionToString(player.transform.position);
                        return "";
                    default:
                        return match.Value; // fallback to the original text if unknown
                }
            });

            StringBuilder builder = new StringBuilder(resolved);
            PlaceholderAPI?.CallHook("ProcessPlaceholders", player.IPlayer, builder);
            return builder.ToString();
        }

        private void Log(string text)
        {
            LogToFile("debug", $"[{DateTime.Now}] {text}", this);
        }

        #endregion

        #region Hooks

        private void UnsubscribeHooks()
        {
            Unsubscribe(nameof(OnPlayerChat));
            Unsubscribe(nameof(OnPlayerDisconnected));
            Unsubscribe(nameof(CanHackCrate));
            Unsubscribe(nameof(OnEntityDeath));
            Unsubscribe(nameof(OnPlayerSleepEnded));
            Unsubscribe(nameof(OnUserPermissionGranted));
            Unsubscribe(nameof(OnUserPermissionRevoked));
            Unsubscribe(nameof(OnUserGroupAdded));
            Unsubscribe(nameof(OnUserGroupRemoved));
            Unsubscribe(nameof(OnEnterZone));
            Unsubscribe(nameof(OnExitZone));
            Unsubscribe(nameof(OnPlayerEnteredMonument));
            Unsubscribe(nameof(OnPlayerExitedMonument));
            
            if(_config.PlayerDataSettings.Disabled)
                Unsubscribe(nameof(OnClientAuth));
        }

        private void SubscribeHooks()
        {
            foreach (var trigger in TriggerDefinitions)
            {
                if (!trigger.Value.Enabled) continue;
                
                if (string.IsNullOrEmpty(trigger.Value.Hooks))
                    continue;

                if (_config.Actions.Any(s => s.Type == trigger.Key && s.IsEnabled()))
                    foreach (string hook in trigger.Value.Hooks.Split(','))
                        Subscribe(hook);
            }
        }

        private void OnPlayerChat(BasePlayer player, string message, Chat.ChatChannel chatchannel)
        {
            if (chatchannel != Chat.ChatChannel.Global && chatchannel != Chat.ChatChannel.Team) return;

            foreach (var keywords in _autoReplyPatterns.Where(regex => regex.Key.Match(message).Success))
                NextTick(() => ProcessReplies(player, new(){"AutoReply"}, keywords.Value.Target, chatchannel == Chat.ChatChannel.Team && _config.BroadcastToTeamOnly));
        }

        private void OnClientAuth(Network.Connection connection)
        {
            pData.Players.TryGetValue(connection.userid, out var pinfo);
            FetchConnectionCountry(connection, pinfo);
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (!player.userID.IsSteamId()) return;

            if (pData.Players.TryGetValue(player.userID, out var pinfo))
                pinfo.justJoined = true;
            else
                pData.Players.Add(player.userID, pinfo = new PlayerInfo { TipsActive = true, isNew = true, justJoined = !HasActiveNewPlayerJoinedAction });

            if (!_config.PlayerDataSettings.Disabled && string.IsNullOrEmpty(pinfo.Country))
                FetchConnectionCountry(player.Connection, pinfo);
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            if (player != null)
                ProcessReplies(player, new(){"PlayerDisconnected"});
        }

        private void CanHackCrate(BasePlayer player, HackableLockedCrate crate)
        {
            if (!player || !player.userID.IsSteamId()) return;

            string monumentName;
            string hackLocation;

            if (OnCargoShip(crate))
            {
                monumentName = "Cargo Ship";
                hackLocation = monumentName;
            }
            else
            {
                var monument = GetMonumentFromPosition(crate.transform.position);
                string hackGrid = MapHelper.PositionToString(crate.transform.position);

                if (monument != null)
                {
                    monumentName = monument.DisplayName;
                    hackLocation = $"{monumentName} ({hackGrid})";
                }
                else
                {
                    monumentName = string.Empty;
                    hackLocation = hackGrid;
                }
            }

            if (_config.CrateHackSettings.ExcludedMonuments.Contains(monumentName)) return;

            if (!pData.Players.TryGetValue(player.userID, out var pinfo))
            {
                pinfo = new PlayerInfo { TipsActive = true };
                pData.Players[player.userID] = pinfo;
            }

            pinfo.HackLocation = hackLocation;

            ProcessReplies(player, new() { "CrateHacked" });
        }

        private void OnEntityDeath(BasePlayer player, HitInfo hitInfo)
        {
            if (player == null || !player.userID.IsSteamId())
                return;

            if (pData.Players.TryGetValue(player.userID, out var pinfo))
            {
                pinfo.wasDead = true;
                ProcessReplies(player, new() {"PlayerDead"}, "FromEntityDeath");
            }
        }

        private void OnPlayerSleepEnded(BasePlayer player)
        {
            if (player.IsDead() || !player.IsConnected) return;
            ProcessReplies(player, new() {"NewPlayerJoined", "PlayerConnected", "PlayerDead"});
        }

        private void OnUserPermissionGranted(string id, string permName)
        {
            var player = BasePlayer.FindByID(Convert.ToUInt64(id));
            if (player != null)
                ProcessReplies(player, new() {"PermissionGranted"}, permName);
        }

        private void OnUserPermissionRevoked(string id, string permName)
        {
            var player = BasePlayer.FindByID(Convert.ToUInt64(id));
            if (player != null)
                ProcessReplies(player, new() {"PermissionRevoked"}, permName);
        }

        private void OnUserGroupAdded(string userId, string groupName)
        {
            var player = BasePlayer.FindByID(Convert.ToUInt64(userId));
            if (player != null)
                ProcessReplies(player, new() {"AddedToGroup"}, groupName);
        }

        private void OnUserGroupRemoved(string userId, string groupName)
        {
            var player = BasePlayer.FindByID(Convert.ToUInt64(userId));
            if (player != null)
                ProcessReplies(player, new() {"RemovedFromGroup"}, groupName);
        }

        private void OnEnterZone(string ZoneID, BasePlayer player)
        {
            if (player != null)
                ProcessReplies(player, new() {"EnteredZone"}, ZoneID);
        }

        private void OnExitZone(string ZoneID, BasePlayer player)
        {
            if (player != null)
                ProcessReplies(player, new() {"LeftZone"}, ZoneID);
        }

        private void OnPlayerEnteredMonument(string monumentID, BasePlayer player, string type, string oldMonumentID)
        {
            if (player != null)
                ProcessReplies(player, new() {"EnteredMonument"}, monumentID);
        }

        private void OnPlayerExitedMonument(string monumentID, BasePlayer player, string type, string reason, string newMonumentID)
        {
            if (player != null)
                ProcessReplies(player, new() {"LeftMonument"}, monumentID);
        }

        #endregion

        #region Commands

        private void TipsToggleChatCMD(IPlayer user, string command, string[] args)
        {
            var player = user.Object as BasePlayer;
            if (player == null)
                return;

            var isActive = ToggleTipsActive(player.userID);

            ulong iconSteamId;
            ulong.TryParse(_config.IconSteamId, out iconSteamId);
            
            SendMessage(player,
                isActive
                    ? lang.GetMessage("toggle_enabled", this, player.UserIDString)
                    : lang.GetMessage("toggle_disabled", this, player.UserIDString), IconSteamId: iconSteamId);
        }

        private void CustomChatCMD(IPlayer user, string command, string[] args)
        {
            var player = user.Object as BasePlayer;
            if (player == null)
                return;

            ProcessReplies(player, new() {"ChatCommand"}, command);
        }

        #endregion

        #region TriggerDefinition

        Dictionary<string, TriggerDefinition> TriggerDefinitions;

        private class TriggerDefinition
        {
            public bool Enabled { get; set; } = true;
            public string Key { get; set; }
            public string Hooks { get; set; }
            public bool RequiresTarget { get; set; }
            public bool UsesIsGlobalBroadcast = true;
            public bool UsesDontTriggerAdmin = true;
            public bool UsesGenericCooldown { get; set; }
            public bool UsesPlayerCooldown { get; set; }
            public bool HasTriggerOwner = true;
            public List<string> Variables = new();
            public string DisableReason { get; set; }
        }

        private void InitTriggerDefinitions()
        {
            TriggerDefinitions = new();

            TriggerDefinitions.Add("Timed", new() { Key = "Timed", RequiresTarget = false, UsesIsGlobalBroadcast = false, UsesDontTriggerAdmin = false, HasTriggerOwner = false });
            TriggerDefinitions.Add("ChatCommand", new() { Key = "ChatCommand", Hooks = nameof(OnPlayerChat), RequiresTarget = true, UsesGenericCooldown = true });
            TriggerDefinitions.Add("AutoReply", new() { Key = "AutoReply", Hooks = nameof(OnPlayerChat), RequiresTarget = true, UsesGenericCooldown = true });
            TriggerDefinitions.Add("NewPlayerJoined", new() { Key = "NewPlayerJoined", Hooks = nameof(OnPlayerSleepEnded), RequiresTarget = false });
            TriggerDefinitions.Add("PlayerConnected", new() { Key = "PlayerConnected", Hooks = nameof(OnPlayerSleepEnded), RequiresTarget = false });
            TriggerDefinitions.Add("PlayerDisconnected", new() { Key = "PlayerDisconnected", Hooks = nameof(OnPlayerDisconnected), RequiresTarget = false });
            TriggerDefinitions.Add("PermissionGranted", new() { Key = "PermissionGranted", Hooks = nameof(OnUserPermissionGranted), RequiresTarget = true });
            TriggerDefinitions.Add("PermissionRevoked", new() { Key = "PermissionRevoked", Hooks = nameof(OnUserPermissionRevoked), RequiresTarget = true });
            TriggerDefinitions.Add("AddedToGroup", new() { Key = "AddedToGroup", Hooks = nameof(OnUserGroupAdded), RequiresTarget = true });
            TriggerDefinitions.Add("RemovedFromGroup", new() { Key = "RemovedFromGroup", Hooks = nameof(OnUserGroupRemoved), RequiresTarget = true });
            TriggerDefinitions.Add("PlayerDead", new() { Key = "PlayerDead", Hooks = $"{nameof(OnEntityDeath)},{nameof(OnPlayerSleepEnded)}", RequiresTarget = false });
            TriggerDefinitions.Add("CrateHacked", new() { Key = "CrateHacked", Hooks = nameof(CanHackCrate), RequiresTarget = false, Variables = new() {"{hacklocation}"}});
            TriggerDefinitions.Add("EnteredZone", new() { Key = "EnteredZone", Hooks = nameof(OnEnterZone), RequiresTarget = true, UsesPlayerCooldown = true });
            TriggerDefinitions.Add("LeftZone", new() { Key = "LeftZone", Hooks = nameof(OnExitZone), RequiresTarget = true, UsesPlayerCooldown = true });
            TriggerDefinitions.Add("EnteredMonument", new() { Key = "EnteredMonument", Hooks = nameof(OnPlayerEnteredMonument), RequiresTarget = true, UsesPlayerCooldown = true });
            TriggerDefinitions.Add("LeftMonument", new() { Key = "LeftMonument", Hooks = nameof(OnPlayerExitedMonument), RequiresTarget = true, UsesPlayerCooldown = true });
        }

        #endregion

        #region Config

        bool HasActiveNewPlayerJoinedAction;
        private void ProcessConfig()
        {
            HasActiveNewPlayerJoinedAction = !_config.PlayerDataSettings.Disabled && _config.Actions.Any(s => s.Type == "NewPlayerJoined" && s.IsEnabled());
            Lang_CacheActions();
        }

        private class PluginConfig
        {
            [JsonProperty(Order = 1000)]
            public VersionNumber Version;

            [JsonProperty(Order = 1, PropertyName = "Chat Icon (Steam Id)")]
            public string IconSteamId { get; set; }

            [JsonProperty(Order = 2, PropertyName = "Toggle Chat Command")]
            public string ToggleCommand { get; set; }

            [JsonProperty(Order = 3, PropertyName = "AutoReply `Broadcast to all` option to broadcast to team only if keywords sent from team chat")]
            public bool BroadcastToTeamOnly { get; set; }

            [JsonProperty(Order = 4, PropertyName = "Replies' Language Settings")]
            public Config_RepliesLangSettings RepliesLangSettings = new();

            [JsonProperty(Order = 5, PropertyName = "Cooldown Settings")]
            public Config_CooldownSettings CooldownSettings = new();

            [JsonProperty(Order = 6, PropertyName = "Player Data Settings")]
            public Config_PlayerDataSettings PlayerDataSettings = new();

            [JsonProperty(Order = 7, PropertyName = "Crate Hack Trigger Settings")]
            public Config_CrateHackSettings CrateHackSettings = new();

            [JsonProperty(Order = 8, PropertyName = "Sample Types for Reference (Do Not Edit)")]
            public string SampleTypes { get; set; }
            
            [JsonProperty(Order = 9, PropertyName = "Actions")]
            public List<Config_Action> Actions { get; set; }

            //Obsolete
            [JsonProperty("Messages")] public List<Config_Action> _ObsMessages;
            [JsonProperty("Chat Icon (SteamId)")] public ulong _ObsIconSteamId = 0;
            [JsonProperty("Replies Server Languages (Creates lang file for each in data/AutomatedMessages/lang)")] public List<string> _ObsServerLangs;
            [JsonProperty("Default Server Language")] public string _ObsDefaultLang;
            [JsonProperty("AutoReply Cooldown (in seconds)")] public int _ObsAutoReplyCooldown = -1;
            [JsonProperty("ChatCommand Cooldown (in seconds)")] public int _ObsChatCommandCooldown = -1;
            [JsonProperty("ZoneManager Cooldown (in seconds)")] public int _ObsZoneManagerCooldown = -1;
            [JsonProperty("MonumentWatcher Cooldown (in seconds)")] public int _ObsMonumentWatcherCooldown = -1;

            public bool ShouldSerialize_ObsMessages() => _ObsMessages != null;
            public bool ShouldSerialize_ObsIconSteamId() => _ObsIconSteamId != 0;
            public bool ShouldSerialize_ObsServerLangs() => _ObsServerLangs != null;
            public bool ShouldSerialize_ObsDefaultLang() => _ObsDefaultLang != null;
            public bool ShouldSerialize_ObsAutoReplyCooldown() => _ObsAutoReplyCooldown != -1;
            public bool ShouldSerialize_ObsChatCommandCooldown() => _ObsChatCommandCooldown != -1;
            public bool ShouldSerialize_ObsZoneManagerCooldown() => _ObsZoneManagerCooldown != -1;
            public bool ShouldSerialize_ObsMonumentWatcherCooldown() => _ObsMonumentWatcherCooldown != -1;
            //
        }

        private class Config_RepliesLangSettings
        {
            [JsonProperty(Order = 0, PropertyName = "Server Languages (Creates lang file for each in data/AutomatedMessages/lang)")]
            public List<string> ServerLangs { get; set; }

            [JsonProperty(Order = 1, PropertyName = "Default Server Language")]
            public string DefaultLang { get; set; }
        }

        private class Config_CooldownSettings
        {
            [JsonProperty("AutoReply Cooldown (in seconds)")] 
            public int AutoReplyCooldown { get; set; }
            
            [JsonProperty(PropertyName = "ChatCommand Cooldown (in seconds)")]
            public int ChatCommandCooldown { get; set; }

            [JsonProperty(PropertyName = "ZoneManager Cooldown (in seconds)")]
            public int ZoneManagerCooldown { get; set; }

            [JsonProperty(PropertyName = "MonumentWatcher Cooldown (in seconds)")]
            public int MonumentWatcherCooldown { get; set; }
        }

        private class Config_PlayerDataSettings
        {
            [JsonProperty("Disable saving data file")] public bool Disabled { get; set; }
            
            [JsonProperty("Reset on new wipe")] 
            public bool ResetOnWipe { get; set; }
        }

        private class Config_CrateHackSettings
        {
            [JsonProperty(Order = 0, PropertyName = "Excluded Monuments")] public List<string> ExcludedMonuments { get; set; } = new();

            [JsonProperty(Order = 1, PropertyName = "Supported Monuments for Reference (Do Not Edit)")]
            public string SupportedMonuments { get; set; }
        }
        
        private class Config_Action
        {
            [JsonProperty("Id")]
            public string Id = GenerateId();

            [JsonProperty(Order = 0, PropertyName = "Enabled")]
            public bool Enabled { get; set; }

            [JsonProperty("Type (Check Sample Types above for Reference)")]
            public string Type = "";

            [JsonProperty("Broadcast to all?")]
            public bool IsGlobalBroadcast { get; set; }

            [JsonProperty("Don't trigger for admins")]
            public bool DontTriggerAdmin { get; set; }

            [JsonProperty("Send in chat")] 
            public bool SendInChat = true;

            [JsonProperty("Send as game tip")]
            public bool SendAsGameTip { get; set; }

            [JsonProperty("Game tip color (true = blue - false = red)")]
            public bool IsBlueGameTip = true;

            [JsonProperty("Interval between messages in minutes (if Type = Timed)")]
            public int Interval = 0;

            [JsonProperty("Target")]
            public string Target = "";
            
            [JsonProperty("Custom chat icon (Steam Id)")]
            public string CustomIconSteamId = "0";
            
            [JsonProperty("Use trigger owner chat icon")]
            public bool UseTriggerOwnerIcon { get; set; }

            [JsonProperty("Permissions")]
            public List<string> Permissions = new();

            [JsonProperty("Groups")]
            public List<string> Groups = new();

            [JsonProperty("Blacklisted Permissions")]
            public List<string> BlacklistedPerms = new();

            [JsonProperty("Blacklisted Groups")]
            public List<string> BlacklistedGroups = new();

            [JsonProperty("Player Can Disable?")]
            public bool PlayerCanDisable { get; set; }

            [JsonIgnore] public bool OnCooldownAll;
            [JsonIgnore] public List<ulong> OnCooldown { get; set; }
            [JsonIgnore] public Dictionary<string, Lang_Action> CachedLangActions { get; set; }

            public Config_Action Clone(bool newGuid = false)
            {
                return new Config_Action
                {
                    Id = newGuid ? GenerateId() : Id,
                    Enabled = Enabled,
                    Type = Type,
                    IsGlobalBroadcast = IsGlobalBroadcast,
                    DontTriggerAdmin = DontTriggerAdmin,
                    SendInChat = SendInChat,
                    SendAsGameTip = SendAsGameTip,
                    IsBlueGameTip = IsBlueGameTip,
                    Interval = Interval,
                    Target = Target,
                    UseTriggerOwnerIcon = UseTriggerOwnerIcon,
                    CustomIconSteamId = CustomIconSteamId,
                    Permissions = new(Permissions),
                    Groups = new(Groups),
                    BlacklistedPerms = new(BlacklistedPerms),
                    BlacklistedGroups = new(BlacklistedGroups),
                    PlayerCanDisable = PlayerCanDisable,
                    CachedLangActions = null
                };
            }

            private static string GenerateId()
            {
                ulong timestamp = (ulong)DateTime.UtcNow.Ticks;
                uint randomPart = (uint)UnityEngine.Random.Range(0, int.MaxValue);
                return ((timestamp << 32) | randomPart).ToString();
            }

            public bool IsEnabled() => Enabled && (SendInChat || SendAsGameTip);
            
            //Obsolete
            [JsonProperty("Messages (Random if more than one)")] public List<string> _ObsMessages;
            [JsonProperty("Replies")] public List<string> _ObsReplies;
            
            public bool ShouldSerialize_ObsMessages() => _ObsMessages != null;
            public bool ShouldSerialize_ObsReplies() => _ObsReplies != null;
            //
        }

        private PluginConfig GetDefaultConfig()
        {
            PluginConfig result = new()
            {
                IconSteamId = "0",
                Version = Version,
                ToggleCommand = "tips",
                CooldownSettings = new()
                {
                    AutoReplyCooldown = 30,
                    ChatCommandCooldown = 30,
                    ZoneManagerCooldown = 0,
                    MonumentWatcherCooldown = 15
                },
                PlayerDataSettings = new()
                {
                    ResetOnWipe = true
                },
                CrateHackSettings = new()
                {
                    SupportedMonuments = string.Join(" | ", MonumentsRadius.Values.Select(s => s.DisplayName))
                },
                SampleTypes = string.Join(" | ", TriggerDefinitions.Keys),
                RepliesLangSettings = new()
                {
                    ServerLangs = new() {"en", "es-ES", "it", "ru", "zh-CN"},
                    DefaultLang = GetOxideLanguage()
                },
                BroadcastToTeamOnly = true,
                Actions = new()
            };

            return result;
        }

        private void CheckForConfigUpdates()
        {
            bool requiresSave = false;
            PluginConfig tmpDefaultConfig = null;

            if (_config == null)
            {
                tmpDefaultConfig ??= GetDefaultConfig();
                _config = tmpDefaultConfig;
                requiresSave = true;
            }

            //1.0.8 update
            if (_config.Version < new VersionNumber(1, 0, 8))
            {
                tmpDefaultConfig ??= GetDefaultConfig();
                _config.ToggleCommand = tmpDefaultConfig.ToggleCommand;
                requiresSave = true;
            }

            //1.0.16 update
            if (_config.Version < new VersionNumber(1, 0, 16))
            {
                tmpDefaultConfig ??= GetDefaultConfig();
                _config._ObsAutoReplyCooldown = tmpDefaultConfig.CooldownSettings.AutoReplyCooldown;
                requiresSave = true;
            }

            //2.0.0 update
            if (_config.Version < new VersionNumber(2, 0, 0))
            {

                if (_config._ObsMessages != null)
                    _config.Actions = new(_config._ObsMessages);
                else
                    _config.Actions = new();

                _config._ObsMessages = null;

                foreach (var action in _config.Actions)
                {
                    if (action._ObsMessages != null)
                        action._ObsReplies = new(action._ObsMessages);
                    else
                        action._ObsReplies = new();

                    action._ObsMessages = null;
                }
                requiresSave = true;
            }

            //2.0.6 update
            if (_config.Version < new VersionNumber(2, 0, 6))
            {
                tmpDefaultConfig ??= GetDefaultConfig();
                _config.IconSteamId = _config._ObsIconSteamId.ToString();
                _config._ObsIconSteamId = 0;
                _config._ObsChatCommandCooldown = tmpDefaultConfig.CooldownSettings.ChatCommandCooldown;
                _config._ObsZoneManagerCooldown = tmpDefaultConfig.CooldownSettings.ZoneManagerCooldown;
                _config._ObsMonumentWatcherCooldown = tmpDefaultConfig.CooldownSettings.MonumentWatcherCooldown;
                _config._ObsServerLangs = new(){ GetOxideLanguage() };

                RepliesLangData = new();

                foreach (string serverLang in _config._ObsServerLangs)
                {
                    if (!AvailableLangs.ContainsKey(serverLang)) continue;
                    if (RepliesLangData.ContainsKey(serverLang)) continue;

                    Lang_Root langRoot = new();

                    foreach (var triggerDefinition in TriggerDefinitions)
                        if (!langRoot.Triggers.ContainsKey(triggerDefinition.Key))
                            langRoot.Triggers.Add(triggerDefinition.Key, new());

                    RepliesLangData.Add(serverLang, langRoot);
                }

                foreach (var action in _config.Actions)
                {
                    if (action._ObsReplies != null)
                    {
                        foreach (var langRoot in RepliesLangData)
                        {
                            if(langRoot.Value.Triggers.TryGetValue(action.Type, out Lang_Trigger langTrigger))
                            {
                                langTrigger.Actions ??= new();

                                if(langRoot.Key == GetOxideLanguage())
                                    langTrigger.Actions.Add(action.Id, new() { Replies = action._ObsReplies });
                                else
                                    langTrigger.Actions.Add(action.Id, new());
                            }
                        }

                        action._ObsReplies = null;
                    }
                }

                Lang_SaveRepliesData();
                requiresSave = true;
            }
            
            //2.0.8 update
            if (_config.Version < new VersionNumber(2, 0, 8))
            {
                tmpDefaultConfig ??= GetDefaultConfig();
                _config._ObsDefaultLang = tmpDefaultConfig.RepliesLangSettings.DefaultLang;
            }

            //2.0.9 update
            if (_config.Version < new VersionNumber(2, 0, 9))
            {
                tmpDefaultConfig ??= GetDefaultConfig();
                _config.BroadcastToTeamOnly = tmpDefaultConfig.BroadcastToTeamOnly;
                requiresSave = true;
            }

            //2.2.2 update
            if (_config.Version < new VersionNumber(2, 2, 2))
            {
                tmpDefaultConfig ??= GetDefaultConfig();
                _config.CooldownSettings ??= new();

                if (_config._ObsAutoReplyCooldown != -1) _config.CooldownSettings.AutoReplyCooldown = _config._ObsAutoReplyCooldown;
                if (_config._ObsChatCommandCooldown != -1) _config.CooldownSettings.ChatCommandCooldown = _config._ObsChatCommandCooldown;
                if (_config._ObsZoneManagerCooldown != -1) _config.CooldownSettings.ZoneManagerCooldown = _config._ObsZoneManagerCooldown;
                if (_config._ObsMonumentWatcherCooldown != -1) _config.CooldownSettings.MonumentWatcherCooldown = _config._ObsMonumentWatcherCooldown;
                _config._ObsAutoReplyCooldown = -1;
                _config._ObsChatCommandCooldown = -1;
                _config._ObsZoneManagerCooldown = -1;
                _config._ObsMonumentWatcherCooldown = -1;

                _config.RepliesLangSettings ??= new();
                if (_config._ObsDefaultLang != null) _config.RepliesLangSettings.DefaultLang = _config._ObsDefaultLang;
                if (_config._ObsServerLangs != null) _config.RepliesLangSettings.ServerLangs = new (_config._ObsServerLangs);
                _config._ObsDefaultLang = null;
                _config._ObsServerLangs = null;

                _config.PlayerDataSettings ??= new();
                _config.PlayerDataSettings.ResetOnWipe = tmpDefaultConfig.PlayerDataSettings.ResetOnWipe;
                requiresSave = true;
            }

            requiresSave = !ValidateConfigRepliesLangSettings();
            requiresSave = !ValidateConfigSampleTypes();
            requiresSave = !ValidateConfigCrateHackSupportedMonuments();

            if (_config.Version != Version)
                requiresSave = true;

            if (requiresSave)
            {
                _config.Version = Version;

                Puts("Config updated.");
                SaveConfig();
            }
        }
        
        private bool ValidateConfigSampleTypes()
        {
            bool wasValid = true;
            
            string sampleTypes = string.Join(" | ", TriggerDefinitions.Keys);
            if(_config.SampleTypes != sampleTypes)
            {
                _config.SampleTypes = sampleTypes;
                wasValid = false;
            }

            return wasValid;
        } 
        
        private bool ValidateConfigCrateHackSupportedMonuments()
        {
            bool wasValid = true;

            string supportedMonuments = string.Join(" | ", MonumentsRadius.Values.Select(s => s.DisplayName));
            if(_config.CrateHackSettings.SupportedMonuments != supportedMonuments)
            {
                _config.CrateHackSettings.SupportedMonuments = supportedMonuments;
                wasValid = false;
            }

            return wasValid;
        } 

        private bool ValidateConfigRepliesLangSettings()
        {
            bool wasValid = true;
            
            _config.RepliesLangSettings ??= new();
            
            //Validate default lang and repair if invalid
            if(!AvailableLangs.ContainsKey(_config.RepliesLangSettings.DefaultLang ?? ""))
            {
                if (CorrectedLangNames.TryGetValue(_config.RepliesLangSettings.DefaultLang ?? "", out string changedTo))
                {
                    PrintWarning($"Config default language {_config.RepliesLangSettings.DefaultLang} has been changed to '{changedTo}'");
                    _config.RepliesLangSettings.DefaultLang = changedTo;
                }
                else
                    _config.RepliesLangSettings.DefaultLang = GetOxideLanguage();
                wasValid = false;
            }
            
            //Make sure server langs list contains atleast the default lang
            if (_config.RepliesLangSettings.ServerLangs == null || _config.RepliesLangSettings.ServerLangs.Count == 0)
            {
                _config.RepliesLangSettings.ServerLangs = new(){ _config.RepliesLangSettings.DefaultLang };
                wasValid = false;
            }
            else
            {
                //Remove invalid lang codes and replace corrected ones
                List<string> langsRemoved = new();
                for (int i = _config.RepliesLangSettings.ServerLangs.Count - 1; i >= 0; i--)
                {
                    if (!AvailableLangs.ContainsKey(_config.RepliesLangSettings.ServerLangs[i]))
                    {
                        if (CorrectedLangNames.TryGetValue(_config.RepliesLangSettings.ServerLangs[i], out string changedTo))
                        {
                            PrintWarning($"Config reply language {_config.RepliesLangSettings.ServerLangs[i]} has been changed to '{changedTo}'");
                            _config.RepliesLangSettings.ServerLangs[i] = changedTo;
                            wasValid = false;
                        }
                        else
                        {
                            langsRemoved.Add(_config.RepliesLangSettings.ServerLangs[i]);
                            _config.RepliesLangSettings.ServerLangs.RemoveAt(i);
                            wasValid = false;
                        }
                    }
                }

                //Print invalid langs removed if any
                if (langsRemoved.Count > 0)
                    PrintWarning($"Language code{(langsRemoved.Count > 1 ? "s" : "")} \"{string.Join(", ", langsRemoved)}\" {(langsRemoved.Count > 1 ? "are" : "is")} not available or not in correct code (removed from config), the following are the available options:\n{string.Join(", ", AvailableLangs.Keys)}.");
                
                //Add default language to serverlangs list if not present
                if(!_config.RepliesLangSettings.ServerLangs.Contains(_config.RepliesLangSettings.DefaultLang))
                {
                    _config.RepliesLangSettings.ServerLangs.Add(_config.RepliesLangSettings.DefaultLang);
                    wasValid = false;
                }
            }

            return wasValid;
        }

        protected override void LoadDefaultConfig() => _config = GetDefaultConfig();

        protected override void LoadConfig()
        {
            InitTriggerDefinitions();
            base.LoadConfig();
            _config = Config.ReadObject<PluginConfig>();

            CheckForConfigUpdates();
            Lang_LoadRepliesConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        #endregion

        #region Data

        private PlayersData pData;

        private void OnServerSave() => timer.Once(UnityEngine.Random.Range(5f, 10f), SaveData);

        private bool wiped { get; set; }

        private void OnNewSave(string filename) => wiped = true;

        private void SaveData() => SavePlayersData();

        private class PlayersData
        {
            public Dictionary<ulong, PlayerInfo> Players = new();
        }
        
        private void LoadPlayersData()
        {
            if (_config.PlayerDataSettings.Disabled)
            {
                pData = new();
                return;
            }
            
            pData = Interface.Oxide.DataFileSystem.ReadObject<PlayersData>($"{nameof(AutomatedMessages)}/PlayerData");
            if (pData == null || (wiped && _config.PlayerDataSettings.ResetOnWipe))
            {
                PrintWarning("No player data found! Creating a new data file.");
                pData = new();
                SavePlayersData();
            }
        }

        private void SavePlayersData()
        {
            if (_config.PlayerDataSettings.Disabled) return;
            
            Interface.Oxide.DataFileSystem.WriteObject($"{nameof(AutomatedMessages)}/PlayerData", pData);
        }

        private bool ToggleTipsActive(ulong playerid)
        {
            if (!pData.Players.TryGetValue(playerid, out var playerInfo))
                pData.Players.Add(playerid, playerInfo = new PlayerInfo { TipsActive = true, isNew = true });
            else
                playerInfo.TipsActive = !playerInfo.TipsActive;

            return playerInfo.TipsActive;
        }

        private class PlayerInfo
        {
            public string Country = "";

            public bool TipsActive = true;

            [JsonIgnore]
            public bool wasDead;

            [JsonIgnore]
            public bool justJoined;

            [JsonIgnore]
            public bool isNew;

            [JsonIgnore]
            public string HackLocation = "";
        }

        #endregion

        #region Helpers

        private void SendMessage(BasePlayer player, string message, bool SendInChat = true, ulong IconSteamId = 0, bool SendAsGameTip = false, bool IsBlueGameTip = true)
        {
            if (player == null) return;

            if (SendInChat)
            {
                if (message.StartsWith("customicon:"))
                {
                    var firstsplit = message.Split(new[] { ' ' }, 2);

                    if (firstsplit.Length == 2)
                    {
                        message = firstsplit[1];
                        var secondsplit = firstsplit[0].Split(new[] { ':' }, 2);
                        if (secondsplit.Length == 2 && ulong.TryParse(secondsplit[1], out ulong msgIcon))
                            IconSteamId = msgIcon;
                    }
                }
                
                player.SendConsoleCommand("chat.add", 2, IconSteamId, message);
            }

            if (SendAsGameTip)
                player.ShowToast(IsBlueGameTip ? GameTip.Styles.Blue_Long : GameTip.Styles.Red_Normal, message);
        }

        private bool MessageIsEligible(Config_Action configMessage, string userID)
        {
            bool isEligible = false;

            if((configMessage.Permissions == null || configMessage.Permissions.Count == 0) &&
                (configMessage.Groups == null || configMessage.Groups.Count == 0))
                isEligible = true;
            else
            {
                if (configMessage.Permissions != null)
                    foreach (var perm in configMessage.Permissions)
                        if (permission.UserHasPermission(userID, perm))
                        {
                            isEligible = true;
                            break;
                        }
                if (configMessage.Groups != null)
                    foreach (var group in configMessage.Groups)
                        if (permission.UserHasGroup(userID, group))
                        {
                            isEligible = true;
                            break;
                        }
            }

            if (!isEligible) return false;

            if (configMessage.BlacklistedPerms != null)
                foreach (var perm in configMessage.BlacklistedPerms)
                    if (permission.UserHasPermission(userID, perm))
                        return false;

            if (configMessage.BlacklistedGroups != null)
                foreach (var group in configMessage.BlacklistedGroups)
                    if (permission.UserHasGroup(userID, group))
                        return false;

            return true;
        }

        private Dictionary<ulong, string> FetchedCountries = new();

        private void FetchConnectionCountry(Network.Connection connection, PlayerInfo pinfo = null, int trials = 0)
        {
            if (connection == null || (pinfo != null && !string.IsNullOrEmpty(pinfo.Country))) return;

            if (FetchedCountries.TryGetValue(connection.userid, out var fetchedCountry))
            {
                if (pinfo != null)
                    pinfo.Country = fetchedCountry;

                return;
            }
            
            try
            {
                webrequest.Enqueue($"https://get.geojs.io/v1/ip/country/{connection.ipaddress.Split(':')[0]}.json", null, (code, response) =>
                {
                    if (response == null || code != 200)
                        return;

                    Dictionary<string, object> objects = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);

                    if (objects.TryGetValue("name", out var country))
                    {
                        if (pinfo != null) 
                            pinfo.Country = country.ToString();
                        else
                            FetchedCountries.Add(connection.userid, country.ToString());
                    }
                
                }, this, RequestMethod.GET);
            }
            catch
            {
                if (trials < 3)
                    FetchConnectionCountry(connection, pinfo, trials+1);
            }
        }

        private bool OnCargoShip(BaseEntity entity)
        {
            return entity?.GetComponentInParent<CargoShip>();
        }

        private MonumentRef GetMonumentFromPosition(Vector3 position)
        {
            foreach (var monument in TerrainMeta.Path.Monuments)
            {
                var monumentkey = MonumentsRadius.Keys.ToList().Find(s => monument.name.Contains(s));
                if (monumentkey != null)
                {
                    float distance = Vector3.Distance(monument.transform.position, position);
                    if (distance <= MonumentsRadius[monumentkey].Radius)
                        return MonumentsRadius[monumentkey];
                }
            }

            return null;
        }

        private readonly Dictionary<string, MonumentRef> MonumentsRadius = new Dictionary<string, MonumentRef>()
        {
            {"supermarket", new MonumentRef(){ DisplayName = "Abandoned Supermarket", Radius = 40f }},
            {"gas_station", new MonumentRef(){ DisplayName = "Oxum's Gas Station", Radius = 70f }},
            {"warehouse", new MonumentRef(){ DisplayName = "Mining Outpost", Radius = 44f }},
            {"ferry_terminal", new MonumentRef(){ DisplayName = "Ferry Terminal", Radius = 150f }},
            {"harbor_1", new MonumentRef(){ DisplayName = "Large Harbor", Radius = 250f }},
            {"harbor_2", new MonumentRef(){ DisplayName = "Small Harbor", Radius = 230f }},
            {"sphere_tank", new MonumentRef(){ DisplayName = "Dome", Radius = 100f }},
            {"junkyard", new MonumentRef(){ DisplayName = "Junk Yard", Radius = 180f }},
            {"radtown_small", new MonumentRef(){ DisplayName = "Sewer Branch", Radius = 120f }},
            {"satellite_dish", new MonumentRef(){ DisplayName = "Satellite Dish", Radius = 160f }},
            {"oilrig_1", new MonumentRef(){ DisplayName = "Large Oil Rig", Radius = 80f }},
            {"oilrig_2", new MonumentRef(){ DisplayName = "Small Oil Rig", Radius = 50f }},
            {"trainyard", new MonumentRef(){ DisplayName = "Train Yard", Radius = 225f }},
            {"powerplant", new MonumentRef(){ DisplayName = "Power Plant", Radius = 205f }},
            {"water_treatment_plant", new MonumentRef(){ DisplayName = "Water Treatment Plant", Radius = 230f }},
            {"excavator", new MonumentRef(){ DisplayName = "Giant Excavator Pit", Radius = 240f }},
            {"nuclear_missile_silo", new MonumentRef(){ DisplayName = "Nuclear Missile Silo", Radius = 50f }},
            {"arctic_research_base", new MonumentRef(){ DisplayName = "Arctic Research Base", Radius = 200f }},
            {"airfield", new MonumentRef(){ DisplayName = "Airfield", Radius = 355f }},
            {"launch_site", new MonumentRef(){ DisplayName = "Launch Site", Radius = 535f }},
            {"military_tunnel", new MonumentRef(){ DisplayName = "Military Tunnels", Radius = 265f }},
            {"radtown_1", new MonumentRef(){ DisplayName = "Radtown", Radius = 120f }}
        };

        private class MonumentRef { public string DisplayName { get; set; } public float Radius { get; set; } }

        private Dictionary<string, string> CachedHexToRGBA = new();
        //copied from ZoneManager
        private string HEXToRGBA(string hexColor, float alpha = 100f)
        {
            if (hexColor.StartsWith("#"))
                hexColor = hexColor.Substring(1);

            if (CachedHexToRGBA.TryGetValue(hexColor, out string cached))
                return $"{cached} {alpha / 100f}";
            else
            {
                int red = int.Parse(hexColor.Substring(0, 2), NumberStyles.AllowHexSpecifier);
                int green = int.Parse(hexColor.Substring(2, 2), NumberStyles.AllowHexSpecifier);
                int blue = int.Parse(hexColor.Substring(4, 2), NumberStyles.AllowHexSpecifier);
            
                CachedHexToRGBA[hexColor] = $"{(double)red / 255} {(double)green / 255} {(double)blue / 255}";;
                return $"{CachedHexToRGBA[hexColor]} {alpha / 100f}";
            }
        }

        #endregion

        #region Lang

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>()
            {
                {"toggle_enabled", "<color=#F3D428>Tips enabled.</color>"},
                {"toggle_disabled", "<color=#fc3a3a>Tips disabled.</color>"},
                {"UI_Edit_Title", "Automated Messages"},
                {"UI_Edit_Cancel", "Cancel"},
                {"UI_Edit_Save", "Save"},
                {"UI_Edit_Back", "Back"},
                {"UI_Edit_Add", "Add"},
                {"UI_Edit_AddNew", "Add new"},
                {"UI_Edit_Duplicate", "Duplicate"},
                {"UI_Edit_Delete", "Delete"},
                {"UI_Edit_Preview", "Preview"},
                {"UI_Edit_Done", "Done"},
                {"UI_Edit_PreviewInChat", "Preview in chat"},
                {"UI_Edit_PreviewGameTip", "Preview game tip"},
                {"UI_Edit_SaveAndExit", "Save & Exit"},
                {"UI_Edit_SaveWarning", "Saving will reset running timers"},
                {"UI_Edit_LanguagesTitle", "Replies Languages:"},
                {"UI_Edit_Settings", "Settings"},
                {"UI_Edit_Settings_IconSteamId", "Chat Icon (Steam Id)"},
                {"UI_Edit_Settings_ToggleCommand", "Toggle Chat Command"},
                {"UI_Edit_Settings_AutoReplyCooldown", "Chat Bot Cooldown (in seconds)"},
                {"UI_Edit_Settings_ChatCommandCooldown", "Chat Command Cooldown (in seconds)"},
                {"UI_Edit_Settings_ZoneManagerCooldown", "Zone Manager Cooldown (in seconds)"},
                {"UI_Edit_Settings_MonumentWatcherCooldown", "Monument Watcher Cooldown (in seconds)"},
                {"UI_Edit_Settings_BroadcastToTeamOnly", "Chat Bot `Broadcast to all` option to broadcast to team only if keywords sent from team chat"},
                {"UI_Edit_Triggers", "Triggers"},
                {"UI_Edit_Trigger_Timed", "Timer"},
                {"UI_Edit_Trigger_ChatCommand", "Chat Command"},
                {"UI_Edit_Trigger_AutoReply", "Chat Bot"},
                {"UI_Edit_Trigger_NewPlayerJoined", "New Player Joined"},
                {"UI_Edit_Trigger_PlayerConnected", "Player Connected"},
                {"UI_Edit_Trigger_PlayerDisconnected", "Player Disconnected"},
                {"UI_Edit_Trigger_PermissionGranted", "Permission Granted"},
                {"UI_Edit_Trigger_PermissionRevoked", "Permission Revoked"},
                {"UI_Edit_Trigger_AddedToGroup", "Added to Group"},
                {"UI_Edit_Trigger_RemovedFromGroup", "Removed from Group"},
                {"UI_Edit_Trigger_PlayerDead", "Player Death"},
                {"UI_Edit_Trigger_CrateHacked", "Hacking Crate"},
                {"UI_Edit_Trigger_EnteredZone", "Zone Manager | Entered"},
                {"UI_Edit_Trigger_LeftZone", "Zone Manager | Exited"},
                {"UI_Edit_Trigger_EnteredMonument", "Monument Watcher | Entered"},
                {"UI_Edit_Trigger_LeftMonument", "Monument Watcher | Exited"},
                {"UI_Edit_Actions", "Actions"},
                {"UI_Edit_Actions_Empty", "It's empty here..."},
                {"UI_Edit_Action", "Action"},
                {"UI_Edit_Action_Active", "Active"},
                {"UI_Edit_Action_Inactive", "Inactive"},
                {"UI_Edit_Action_Replies", "Replies"},
                {"UI_Edit_Action_Permissions", "Permissions"},
                {"UI_Edit_Action_Groups", "Groups"},
                {"UI_Edit_Action_ExcludedPermissions", "Excluded Permissions"},
                {"UI_Edit_Action_ExcludedGroups", "Excluded Groups"},
                {"UI_Edit_Action_SendInChat", "Send in chat"},
                {"UI_Edit_Action_SendAsGameTip", "Send as game tip"},
                {"UI_Edit_Action_IsBlueGameTip", "Blue game tip"},
                {"UI_Edit_Action_IsRedGameTip", "Red game tip"},
                {"UI_Edit_Action_CanDisable", "Player can disable using <color=#DFFFAA>/{0}</color> command"},
                {"UI_Edit_Action_ExcludeAdmin", "Don't trigger for Admins  <i>(if broadcast to all enabled)</i>"},
                {"UI_Edit_Action_BroadcastToAll", "Broadcast to all eligible"},
                {"UI_Edit_Action_UseTriggerOwnerIcon", "Use trigger owner chat icon"},
                {"UI_Edit_Action_CustomIconSteamId", "Custom chat icon (Steam Id)"},
                {"UI_Edit_Action_Target_AutoReply", "Keywords <i>(comma separated)</i>"},
                {"UI_Edit_Action_Target_ChatCommand", "Chat Command <i>(variants comma separated)</i>"},
                {"UI_Edit_Action_Target_PermissionGranted", "Target permission"},
                {"UI_Edit_Action_Target_PermissionRevoked", "Target permission"},
                {"UI_Edit_Action_Target_AddedToGroup", "Target group"},
                {"UI_Edit_Action_Target_RemovedFromGroup", "Target group"},
                {"UI_Edit_Action_Target_EnteredZone", "Zone ID"},
                {"UI_Edit_Action_Target_LeftZone", "Zone ID"},
                {"UI_Edit_Action_Target_EnteredMonument", "Monument ID"},
                {"UI_Edit_Action_Target_LeftMonument", "Monument ID"},
                {"UI_Edit_Reply_TextEditor", "Text Editor"},
                {"UI_Edit_Reply_AvailableVariables", "<i>AVAILABLE VARIABLES</i>\n\n{0}\n\n<size=12><color=#a1a1a1>{1}</color></size>"},
                {"UI_Edit_ChatPreviewMode_Title", "Automated Messages\n<color=gray>Chat Preview Mode</color>"},
                {"UI_Edit_ChatPreviewMode_Exit", "Back to editor"},
                {"UI_Edit_GameTipPreviewMode_Title", "Automated Messages\n<color=gray>Game Tip Preview Mode</color>"},
                {"UI_Edit_GameTipPreviewMode_Exit", "Back to editor"}
            }, this, "en");
        }

        readonly Dictionary<string, string> AvailableLangs = new() { { "af", "Afrikaans" }, { "ar", "Arabic" }, { "ca", "català" }, { "cs", "čeština" }, { "da", "dansk" }, { "de", "Deutsch" }, { "el", "ελληνικά" }, { "en-PT", "Pirate Aaargh!" }, { "en", "English" }, { "es-ES", "español" }, { "fi", "suomi" }, { "fr", "français" }, { "hu", "magyar" }, { "it", "italiano" }, { "ja", "日本語" }, { "ko", "한국어" }, { "nl", "Nederlands" }, { "no", "norsk" }, { "pl", "polski" }, { "pt-PT", "Português - PT" }, { "pt-BR", "Português - BR" }, { "ro", "românește" }, { "ru", "Русский язык" }, { "sr", "српски" }, { "sv", "svenska" }, { "tr", "Türkçe" }, { "uk", "українська мова" }, { "vi", "Tiếng Việt" }, { "zh-CN", "简体中文" }, { "zh-TW", "繁體中文" } };
        readonly Dictionary<string, string> CorrectedLangNames = new(){ ["es"] = "es-ES", ["pt"] = "pt-PT", ["zh"] = "zh-CN" };
        
        Dictionary<string, Lang_Root> RepliesLangData = new();
        private class Lang_Root { public Dictionary<string, Lang_Trigger> Triggers = new(); }
        private class Lang_Trigger { public Dictionary<string, Lang_Action> Actions = new(); }
        private class Lang_Action 
        { 
            public List<string> Replies = new();
            [JsonIgnore]
            public int ReplyIndex { get; set; }

            public Lang_Action Clone()
            {
                return new()
                {
                    Replies = new(Replies)
                };
            }
        }

        private void Lang_LoadRepliesConfig()
        {
            RepliesLangData = new();

            bool requiresSave = false;

            foreach (string serverLang in _config.RepliesLangSettings.ServerLangs)
            {
                if (RepliesLangData.ContainsKey(serverLang)) continue;

                Lang_Root langRoot = new();

                string langPath = $"{Name}/lang/{serverLang}";
                if (Interface.Oxide.DataFileSystem.ExistsDatafile(langPath))
                    langRoot = Interface.Oxide.DataFileSystem.ReadObject<Lang_Root>(langPath);
                else
                {
                    requiresSave = true;
                    foreach (var corrLang in CorrectedLangNames)
                        if (serverLang == corrLang.Value && Interface.Oxide.DataFileSystem.ExistsDatafile($"{Name}/lang/{corrLang.Key}"))
                        {
                            langRoot = Interface.Oxide.DataFileSystem.ReadObject<Lang_Root>($"{Name}/lang/{corrLang.Key}");
                            break;
                        }
                }
                
                //Add missing triggers
                foreach (var triggerDefinition in TriggerDefinitions)
                    if (!langRoot.Triggers.ContainsKey(triggerDefinition.Key))
                    {
                        langRoot.Triggers.Add(triggerDefinition.Key, new());
                        requiresSave = true;
                    }

                //Remove extra actions
                List<string> actionsToRemove = new();

                foreach (var langTrigger in langRoot.Triggers)
                {
                    foreach (var langAction in langTrigger.Value.Actions)
                    {
                        if (!_config.Actions.Any(s => s.Type == langTrigger.Key && s.Id == langAction.Key))
                            actionsToRemove.Add(langAction.Key);
                    }

                    foreach (var actionToRemove in actionsToRemove)
                    {
                        langTrigger.Value.Actions.Remove(actionToRemove);
                        requiresSave = true;
                    }

                    actionsToRemove.Clear();
                }

                //Add missing actions
                foreach (var configAction in _config.Actions)
                {
                    foreach (var langTrigger in langRoot.Triggers)
                    {
                        if (configAction.Type != langTrigger.Key) continue;

                        if (!langTrigger.Value.Actions.Any(s => s.Key == configAction.Id))
                        {
                            langTrigger.Value.Actions.Add(configAction.Id, new());
                            requiresSave = true;
                        }
                    }
                }

                RepliesLangData.Add(serverLang, langRoot);
            }

            if (requiresSave)
            {
                Lang_SaveRepliesData();
                PrintWarning("Lang files updated due to missing file or mismatch.");
            }
        }

        private void Lang_SaveRepliesData()
        {
            foreach (var langRoot in RepliesLangData)
                Interface.Oxide.DataFileSystem.WriteObject($"{Name}/lang/{langRoot.Key}", langRoot.Value);
        }

        private void Lang_CacheActions()
        {
            foreach (var configAction in _config.Actions)
            {
                configAction.CachedLangActions = new();

                foreach (string language in _config.RepliesLangSettings.ServerLangs)
                    configAction.CachedLangActions.Add(language, Lang_GetAction(language, configAction.Type, configAction.Id));
            }

            Puts($"Cached replies for \"{string.Join(", ", _config.RepliesLangSettings.ServerLangs)}\" language{((_config.RepliesLangSettings.ServerLangs.Count == 1) ? "" : "s")}.");
        }

        private bool Lang_TryGetAction(BasePlayer player, string triggerKey, string actionId, string language, out Lang_Action lang_Action)
        {
            if (!RepliesLangData.ContainsKey(language))
            {
                lang_Action = null;
                return false;
            }

            lang_Action = Lang_GetAction(language, triggerKey, actionId);
            return true;
        }

        private Lang_Action Lang_GetAction(string language, string triggerKey, string actionId) => RepliesLangData[language].Triggers[triggerKey].Actions[actionId];

        private string GetOxideLanguage()
        {
            string oxideLang = lang.GetServerLanguage();

            if (!AvailableLangs.ContainsKey(oxideLang) && !CorrectedLangNames.TryGetValue(oxideLang, out oxideLang))
                return "en";

            return oxideLang;
        }
        
        #endregion

        #region UI

        #region Models/Fields

        const string Layer = "Overlay";
        const string uimodal = $"{PREFIX_LONG}.modal";
        const string chatpreview = $"{PREFIX_LONG}.chatpreview";
        const string gametippreview = $"{PREFIX_LONG}.gametippreview";

        private class UISession
        {
            public string SelectedRepliesLang { get; set; }
            public UISettings Settings { get; set; }
            public List<UITrigger> Triggers { get; set; }
        }
        
        private class UISettings
        {
            public string IconSteamId { get; set; }
            
            public string ToggleCommand { get; set; }

            public int AutoReplyCooldown;
            public int ChatCommandCooldown;
            public int ZoneManagerCooldown;
            public int MonumentWatcherCooldown;

            public List<string> ServerLangs { get; set; }
            public string DefaultLang { get; set; }
            
            public bool BroadcastToTeamOnly { get; set; }
        }

        private class UITrigger
        {
            public TriggerDefinition TriggerDefinition { get; set; }
            public List<Config_Action> Config_Actions { get; set; }
            public Dictionary<string, Lang_Trigger> LangsTriggers { get; set; }
        }

        Dictionary<ulong, UISession> UISessions = new();

        #endregion

        #region Functions

        private string FormatTime(double time, bool d = true, bool h = true, bool m = true, bool s = true) => FormatTime(TimeSpan.FromSeconds((float)time), d, h, m, s);

        private string FormatTime(TimeSpan t, bool d = true, bool h = true, bool m = true, bool s = true)
        {
            List<string> shortForm = new();
            if (d && t.Days > 0)
                shortForm.Add(string.Format("{0} day" + (t.Days > 1 ? "s" : ""), t.Days.ToString()));
            if (h && t.Hours > 0)
                shortForm.Add(string.Format("{0} hour" + (t.Hours > 1 ? "s" : ""), t.Hours.ToString()));
            if (m && t.Minutes > 0)
                shortForm.Add(string.Format("{0} minute" + (t.Minutes > 1 ? "s" : ""), t.Minutes.ToString()));
            if (s && t.Seconds > 0)
                shortForm.Add(string.Format("{0} second" + (t.Seconds > 1 ? "s" : ""), t.Seconds.ToString()));

            return string.Join(", ", shortForm);
        }

        private UISession GetUISession(ulong userID)
        {
            UISession uiSession;
            if (UISessions.TryGetValue(userID, out uiSession))
                return uiSession;
            else
                return null;
        }

        private UITrigger GetUITrigger(UISession uiSession, string triggerKey) => uiSession.Triggers.FirstOrDefault(s => s.TriggerDefinition.Key == triggerKey);

        private UITrigger GetUITrigger(ulong userID, string triggerKey)
        {
            UISession uiSession = GetUISession(userID);
            if (uiSession != null)
                return GetUITrigger(uiSession, triggerKey);
            else
                return null;
        }

        private Config_Action GetUIAction(UITrigger uiTrigger, int actionIndex)
        {
            if (uiTrigger == null || uiTrigger.Config_Actions.Count < actionIndex + 1) return null;

            return uiTrigger.Config_Actions[actionIndex];
        }

        private Config_Action GetUIAction(ulong userID, string triggerKey, int actionIndex)
        {
            UITrigger uiTrigger = GetUITrigger(userID, triggerKey);

            if (uiTrigger == null || uiTrigger.Config_Actions.Count < actionIndex + 1) return null;

            return uiTrigger.Config_Actions[actionIndex];
        }

        private void UpdateConfigFromUISession(UISession uiSession)
        {
            _config.IconSteamId = uiSession.Settings.IconSteamId;
            _config.IconSteamId = uiSession.Settings.IconSteamId;
            _config.ToggleCommand = uiSession.Settings.ToggleCommand;
            _config.CooldownSettings.AutoReplyCooldown = uiSession.Settings.AutoReplyCooldown;
            _config.CooldownSettings.ChatCommandCooldown = uiSession.Settings.ChatCommandCooldown;
            _config.CooldownSettings.ZoneManagerCooldown = uiSession.Settings.ZoneManagerCooldown;
            _config.CooldownSettings.MonumentWatcherCooldown = uiSession.Settings.MonumentWatcherCooldown;
            
            _config.BroadcastToTeamOnly = uiSession.Settings.BroadcastToTeamOnly;
            
            _config.Actions = new();

            foreach (var trigger in uiSession.Triggers)
            {
                foreach (var configaction in trigger.Config_Actions)
                {
                    _config.Actions.Add(configaction.Clone());
                }

                foreach (var langRoot in RepliesLangData)
                {
                    langRoot.Value.Triggers[trigger.TriggerDefinition.Key] = new();
                    foreach (var langaction in trigger.LangsTriggers[langRoot.Key].Actions)
                        langRoot.Value.Triggers[trigger.TriggerDefinition.Key].Actions.Add(langaction.Key, langaction.Value.Clone());
                }
            }


            SaveConfig();
            Lang_SaveRepliesData();
            ProcessConfig();
            Puts("Config updated");
            DisableNonfunctionalTriggers();

            UnsubscribeHooks();
            SubscribeHooks();
            InitTimers();
            InitAutoReply();
            UnregisterCachedCommands();
            RegisterConfigCommands();
        }

        #endregion

        private void UI_ShowEditor(BasePlayer player)
        {
            if (!UISessions.TryGetValue(player.userID, out var session))
            {
                session = new();
                session.SelectedRepliesLang = _config.RepliesLangSettings.DefaultLang;
                
                session.Settings = new();
                session.Settings.IconSteamId = _config.IconSteamId;
                session.Settings.ToggleCommand = _config.ToggleCommand;
                session.Settings.AutoReplyCooldown = _config.CooldownSettings.AutoReplyCooldown;
                session.Settings.ChatCommandCooldown = _config.CooldownSettings.ChatCommandCooldown;
                session.Settings.ZoneManagerCooldown = _config.CooldownSettings.ZoneManagerCooldown;
                session.Settings.MonumentWatcherCooldown = _config.CooldownSettings.MonumentWatcherCooldown;
                session.Settings.DefaultLang = _config.RepliesLangSettings.DefaultLang;
                session.Settings.ServerLangs = new(_config.RepliesLangSettings.ServerLangs);
                session.Settings.BroadcastToTeamOnly = _config.BroadcastToTeamOnly;
                
                session.Triggers = new();

                foreach (var trigger in TriggerDefinitions)
                {
                    UITrigger uiTrigger = new();
                    uiTrigger.TriggerDefinition = trigger.Value;
                    uiTrigger.Config_Actions = new();
                    uiTrigger.LangsTriggers = new();

                    foreach (var action in _config.Actions.Where(s => s.Type == trigger.Key))
                        uiTrigger.Config_Actions.Add(action.Clone());

                    foreach (string language in _config.RepliesLangSettings.ServerLangs)
                    {
                        uiTrigger.LangsTriggers.Add(language, new());
                        foreach (var action in RepliesLangData[language].Triggers[trigger.Key].Actions)
                            uiTrigger.LangsTriggers[language].Actions.Add(action.Key, action.Value.Clone());
                    }

                    session.Triggers.Add(uiTrigger);
                }

                UISessions.Add(player.userID, session);
            }

            CuiElementContainer cont = new();

            //Parent panel
            cont.Add(
                new CuiPanel
                {
                    CursorEnabled = true,
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    Image = { Color = HEXToRGBA("#000000", 80), Material = "assets/content/ui/uibackgroundblur.mat" }
                },
                Layer,
                uimodal,
                uimodal
            );

            //Title
            cont.Add(
                new CuiElement
                {
                    Parent = uimodal,
                    Name = $"{uimodal}.title",
                    Components =
                    {
                        new CuiTextComponent() { Color = HEXToRGBA("#DCDCDC"), Text = lang.GetMessage("UI_Edit_Title", this, player.UserIDString).ToUpper(), FontSize = 24, Align = TextAnchor.MiddleCenter },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-200 275", OffsetMax = "200 325" }
                    }
                }
            );

            int panelWidth = 1000;
            int panelHeight = 500;

            int saveExitWidth = 100;
            int saveWidth = 70;
            int savesGap = 5;
            int buttonsHeight = 25;

            //Save & Exit button
            cont.Add(
                new CuiButton
                {
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{(panelWidth / 2) - saveExitWidth} -{(panelHeight / 2) + buttonsHeight + savesGap}", OffsetMax = $"{(panelWidth / 2)} -{(panelHeight / 2) + savesGap}" },
                    Text = { Text = lang.GetMessage("UI_Edit_SaveAndExit", this, player.UserIDString).ToUpper(), FontSize = 15, Align = TextAnchor.MiddleCenter, Color = HEXToRGBA("#90CAF3") },
                    Button = { Color = HEXToRGBA("#376E92", 95), Command = $"{PREFIX_SHORT}.edit.savecancel saveexit" }
                },
                $"{uimodal}"
            );

            //Save button
            cont.Add(
                new CuiButton
                {
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{(panelWidth / 2) - saveExitWidth - savesGap - saveWidth} -{(panelHeight / 2) + buttonsHeight + savesGap}", OffsetMax = $"{(panelWidth / 2) - saveExitWidth - savesGap} -{(panelHeight / 2) + savesGap}" },
                    Text = { Text = lang.GetMessage("UI_Edit_Save", this, player.UserIDString).ToUpper(), FontSize = 15, Align = TextAnchor.MiddleCenter, Color = HEXToRGBA("#DFFFAA") },
                    Button = { Color = HEXToRGBA("#79A62F", 95), Command = $"{PREFIX_SHORT}.edit.savecancel save" }
                },
                $"{uimodal}"
            );

            //Save notice
            cont.Add(
                new CuiElement
                {
                    Parent = $"{uimodal}",
                    Name = $"{uimodal}.savenotice",
                    Components =
                    {
                        new CuiTextComponent() { Color = "1 1 1 1", Text = $"<i>{lang.GetMessage("UI_Edit_SaveWarning", this, player.UserIDString)}</i>".ToUpper(), FontSize = 12, Align = TextAnchor.MiddleRight, Font = "robotocondensed-regular.ttf" },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{(panelWidth / 2) - saveExitWidth - savesGap * 4 - saveWidth - 300} -{(panelHeight / 2) + buttonsHeight + savesGap}", OffsetMax = $"{(panelWidth / 2) - saveExitWidth - savesGap * 4 - saveWidth} -{(panelHeight / 2) + savesGap}" },
                    }
                }
            );

            //Cancel button
            cont.Add(
                new CuiButton
                {
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"-{(panelWidth / 2)} -{(panelHeight / 2) + buttonsHeight + savesGap}", OffsetMax = $"-{(panelWidth / 2) - 75} -{(panelHeight / 2) + savesGap}" },
                    Text = { Text = lang.GetMessage("UI_Edit_Cancel", this, player.UserIDString).ToUpper(), FontSize = 15, Align = TextAnchor.MiddleCenter, Color = HEXToRGBA("#FFC3B9") },
                    Button = { Color = HEXToRGBA("#CE422B", 95), Command = $"{PREFIX_SHORT}.edit.savecancel cancel" }
                },
                $"{uimodal}"
            );

            CuiHelper.AddUi(player, cont);

            UI_ShowTriggersView(player, session, panelWidth, panelHeight, 30);
        }

        private void UI_ShowTriggersView(BasePlayer player, UISession session = null, int panelWidth = 1000, int panelHeight = 500, int panelInnerMargin = 30)
        {
            if (session == null && !UISessions.TryGetValue(player.userID, out session))
                return;

            CuiElementContainer cont = new();

            //Center panel
            cont.Add(
                new CuiPanel
                {
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"-{panelWidth / 2} -{panelHeight / 2}", OffsetMax = $"{panelWidth / 2} {panelHeight / 2}" },
                    Image = { Color = HEXToRGBA("#1E2020", 95) }
                },
                uimodal,
                $"{uimodal}.triggersview",
                $"{uimodal}.triggersview"
            );

            int titleHeight = 30;
            
            //Settings button
            cont.Add(
                new CuiButton
                {
                    RectTransform = { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = $"-{85} -{35}", OffsetMax = $"-15 -15" },
                    Text = { Text = lang.GetMessage("UI_Edit_Settings", this, player.UserIDString).ToUpper(), FontSize = 12, Align = TextAnchor.MiddleCenter, Color = HEXToRGBA("#90CAF3") },
                    Button = { Color = HEXToRGBA("#376E92", 95), Command = $"{PREFIX_SHORT}.ui.cmd GoToSettings" }
                },
                $"{uimodal}.triggersview"
            );

            //Sub-title
            cont.Add(
                new CuiElement
                {
                    Parent = $"{uimodal}.triggersview",
                    Name = $"{uimodal}.triggersview.title",
                    Components =
                    {
                        new CuiTextComponent() { Color = HEXToRGBA("#DCDCDC"), Text = lang.GetMessage("UI_Edit_Triggers", this, player.UserIDString).ToUpper(), FontSize = 16, Align = TextAnchor.UpperLeft },
                        new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0.5 1", OffsetMin = $"{panelInnerMargin} -{panelInnerMargin + titleHeight}", OffsetMax = $"-{panelInnerMargin} -{panelInnerMargin}" }
                    }
                }
            );

            //Scrollviewer
            CuiElement scrollViewer = new CuiElement
            {
                Parent = $"{uimodal}.triggersview",
                Name = $"{uimodal}.triggersview.scrollviewer",
                Components =
                {
                    new CuiImageComponent() { Color = "0 0 0 0" },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"{panelInnerMargin} {panelInnerMargin}", OffsetMax = $"-{panelInnerMargin - 15} -{panelInnerMargin + titleHeight}" },
                    new CuiNeedsCursorComponent()
                }
            };

            int xStart = 0;
            int yStart = 0;
            int itemSize = 125;
            int itemsGap = 10;
            int scrollviewerWidth = panelWidth - panelInnerMargin * 2;
            int finalContentHeight = 0;
            int minContentHeight = panelHeight - panelInnerMargin * 2 - titleHeight;

            CuiElementContainer scrollContent = new();

            //Triggers list
            foreach (var trigger in session.Triggers)
            {
                if (xStart + itemSize > scrollviewerWidth)
                {
                    xStart = 0;
                    yStart += itemSize + itemsGap;
                }

                int actionCount = trigger.Config_Actions.Count;
                int activeActionCount = trigger.Config_Actions.Where(s => s.IsEnabled()).Length;

                scrollContent.Add(
                    new CuiElement
                    {
                        Parent = $"{uimodal}.triggersview.scrollviewer",
                        Name = $"{uimodal}.triggersview.{trigger.TriggerDefinition.Key}",
                        Components =
                        {
                            new CuiImageComponent() { Color = HEXToRGBA(trigger.TriggerDefinition.Enabled && activeActionCount > 0 ? "#648135" : "#393C3C", 95) },
                            new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{xStart} -{yStart + itemSize}", OffsetMax = $"{xStart + itemSize} -{yStart}" }
                        }
                    }
                );

                finalContentHeight = yStart + itemSize;
                xStart += itemSize + itemsGap;

                scrollContent.Add(
                    new CuiElement
                    {
                        Parent = $"{uimodal}.triggersview.{trigger.TriggerDefinition.Key}",
                        Name = $"{uimodal}.triggersview.{trigger.TriggerDefinition.Key}.text",
                        Components =
                        {
                            new CuiTextComponent() { Color = "1 1 1 1", Text = lang.GetMessage($"UI_Edit_Trigger_{trigger.TriggerDefinition.Key}", this, player.UserIDString), FontSize = 16, Align = TextAnchor.UpperLeft },
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"10 10", OffsetMax = $"-10 -10" }
                        }
                    }
                );

                if (trigger.TriggerDefinition.Enabled)
                {
                    scrollContent.Add(
                        new CuiElement
                        {
                            Parent = $"{uimodal}.triggersview.{trigger.TriggerDefinition.Key}",
                            Name = $"{uimodal}.triggersview.{trigger.TriggerDefinition.Key}.count",
                            Components =
                            {
                                new CuiTextComponent() { Color = "1 1 1 1", Text = $"{actionCount}", FontSize = 18, Align = TextAnchor.LowerLeft },
                                new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"10 10", OffsetMax = $"-10 -10" }
                            }
                        }
                    );

                    scrollContent.Add(
                        new CuiElement
                        {
                            Parent = $"{uimodal}.triggersview.{trigger.TriggerDefinition.Key}",
                            Name = $"{uimodal}.triggersview.{trigger.TriggerDefinition.Key}.arrow",
                            Components =
                            {
                                new CuiTextComponent() { Color = "1 1 1 1", Text = "→", FontSize = 18, Align = TextAnchor.LowerRight },
                                new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"10 10", OffsetMax = $"-10 -10" }
                            }
                        }
                    );
                }
                else
                {
                    scrollContent.Add(
                        new CuiElement
                        {
                            Parent = $"{uimodal}.triggersview.{trigger.TriggerDefinition.Key}",
                            Name = $"{uimodal}.triggersview.{trigger.TriggerDefinition.Key}.disabledtext",
                            Components =
                            {
                                new CuiTextComponent() { Color = HEXToRGBA("#CE422B", 100), Text = $"<b>DISABLED</b>: <i>{trigger.TriggerDefinition.DisableReason.ToUpper()}</i>", FontSize = 11, 
                                    Align = TextAnchor.LowerLeft, Font = "robotocondensed-regular.ttf" },
                                new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "10 10", OffsetMax = "-10 -10" }
                            }
                        }
                    );
                }
                    
                scrollContent.Add(new CuiElement
                {
                    Parent = $"{uimodal}.triggersview.{trigger.TriggerDefinition.Key}",
                    Name = $"{uimodal}.triggersview.{trigger.TriggerDefinition.Key}.button",
                    Components =
                    {
                        new CuiButtonComponent() { Color = "0 0 0 0", Command = $"{PREFIX_SHORT}.ui.cmd GoToActions {trigger.TriggerDefinition.Key}" },
                        new CuiRectTransformComponent() { AnchorMin = "0 0", AnchorMax = "1 1" }
                    }
                });
            }

            //Now add ScrollViewComponent based on final content height
            scrollViewer.Components.Add(
                new CuiScrollViewComponent
                {
                    Vertical = true,
                    Horizontal = false,
                    MovementType = ScrollRect.MovementType.Elastic,
                    Elasticity = 0.3f,
                    Inertia = true,
                    DecelerationRate = 0.5f,
                    ScrollSensitivity = 25f,
                    ContentTransform = new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 -{(minContentHeight > finalContentHeight ? minContentHeight : finalContentHeight)}", OffsetMax = $"0 0" },
                    VerticalScrollbar = new CuiScrollbar { Invert = false, Size = 5f, TrackColor = HEXToRGBA("#121414", 90), HandleColor = HEXToRGBA("#393C3C", 85), HighlightColor = HEXToRGBA("#393C3C", 95) }
                }
            );

            //Now add scrollviewer and content
            cont.Add(scrollViewer);
            cont.AddRange(scrollContent);

            CuiHelper.AddUi(player, cont);
        }
        
        private void UI_ShowSettingsView(BasePlayer player, UISession session = null, int panelWidth = 1000, int panelHeight = 500, int panelInnerMargin = 30)
        {
            if (session == null && !UISessions.TryGetValue(player.userID, out session))
                return;

            string parent = $"{uimodal}.settingsview";

            CuiElementContainer cont = new();

            //Center panel
            cont.Add(
                new CuiPanel
                {
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"-{panelWidth / 2} -{panelHeight / 2}", OffsetMax = $"{panelWidth / 2} {panelHeight / 2}" },
                    Image = { Color = HEXToRGBA("#1E2020", 95) }
                },
                uimodal,
                parent,
                parent
            );

            int titleHeight = 30;
            int triggersButtonWidth = 75;
            int backButtonWidth = 60;
            int subtitleInnerGap = 1;

            //Back button
            cont.Add(
                new CuiButton
                {
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{panelInnerMargin} -{panelInnerMargin + 20}", OffsetMax = $"{panelInnerMargin + backButtonWidth} -{panelInnerMargin}" },
                    Text = { Text = lang.GetMessage("UI_Edit_Back", this, player.UserIDString).ToUpper(), FontSize = 12, Align = TextAnchor.MiddleCenter, Color = HEXToRGBA("#90CAF3") },
                    Button = { Color = HEXToRGBA("#376E92", 95), Command = $"{PREFIX_SHORT}.ui.cmd GoToTriggers" }
                },
                parent
            );

            //Sub-title
            cont.Add(
                new CuiElement
                {
                    Parent = parent,
                    Name = $"{uimodal}.settingsview.title",
                    Components =
                    {
                        new CuiTextComponent() { Color = HEXToRGBA("#DCDCDC"), Text = lang.GetMessage($"UI_Edit_Settings", this, player.UserIDString).ToUpper(), FontSize = 16, Align = TextAnchor.UpperCenter },
                        new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"{panelInnerMargin + triggersButtonWidth + subtitleInnerGap} -{panelInnerMargin + titleHeight}", OffsetMax = $"-{panelInnerMargin + triggersButtonWidth + subtitleInnerGap} -{panelInnerMargin}" }
                    }
                }
            );

            string editPanel = $"{parent}.editpanel";

            int formPanelInnerMargin = 20;

            //Back panel
            cont.Add(
                new CuiPanel
                {
                    CursorEnabled = true,
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"{panelInnerMargin} {panelInnerMargin}", OffsetMax = $"-{panelInnerMargin} -{panelInnerMargin + titleHeight}" },
                    Image = { Color = HEXToRGBA("#151717", 95) }
                },
                parent, editPanel, editPanel
            );
            
            //Scrollviewer
            CuiElement scrollViewer = new CuiElement
            {
                Parent = editPanel,
                Name = $"{editPanel}.scrollviewer",
                Components =
                {
                    new CuiImageComponent() { Color = "1 0 1 0" },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"0 {formPanelInnerMargin}", OffsetMax = $"-5 -{formPanelInnerMargin}" },
                    new CuiNeedsCursorComponent()
                }
            };

            CuiElementContainer scrollContent = new();

            int yStart = 0;
            int fieldsGap = 15;
            
            //CustomIconSteamId Textbox
            UI_TextBox(
                session.Settings.IconSteamId, lang.GetMessage($"UI_Edit_Settings_IconSteamId", this, player.UserIDString), $"{PREFIX_SHORT}.ui.texteditor Settings SettingsTextbox {nameof(PluginConfig.IconSteamId)}", scrollContent, $"{editPanel}.scrollviewer",
                new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0.49 1", OffsetMin = $"{formPanelInnerMargin} -{yStart + 30}", OffsetMax = $"-{formPanelInnerMargin - 5} -{yStart}" }
            );
            //yStart += 30 + fieldsGap;
            
            //ToggleCommand Textbox
            UI_TextBox(
                session.Settings.ToggleCommand, lang.GetMessage($"UI_Edit_Settings_ToggleCommand", this, player.UserIDString), $"{PREFIX_SHORT}.ui.texteditor Settings SettingsTextbox {nameof(PluginConfig.ToggleCommand)}", scrollContent, $"{editPanel}.scrollviewer",
                new CuiRectTransformComponent { AnchorMin = "0.51 1", AnchorMax = "1 1", OffsetMin = $"0 -{yStart + 30}", OffsetMax = $"-{formPanelInnerMargin - 5} -{yStart}" }
            );
            yStart += 30 + fieldsGap;
            
            //AutoReplyCooldown Textbox
            UI_TextBox(
                session.Settings.AutoReplyCooldown.ToString(), lang.GetMessage($"UI_Edit_Settings_AutoReplyCooldown", this, player.UserIDString), $"{PREFIX_SHORT}.ui.texteditor Settings SettingsTextbox {nameof(PluginConfig.CooldownSettings.AutoReplyCooldown)}", scrollContent, $"{editPanel}.scrollviewer",
                new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0.49 1", OffsetMin = $"{formPanelInnerMargin} -{yStart + 30}", OffsetMax = $"-{formPanelInnerMargin - 5} -{yStart}" }
            );
            //yStart += 30 + fieldsGap;
            
            //ChatCommandCooldown Textbox
            UI_TextBox(
                session.Settings.ChatCommandCooldown.ToString(), lang.GetMessage($"UI_Edit_Settings_ChatCommandCooldown", this, player.UserIDString), $"{PREFIX_SHORT}.ui.texteditor Settings SettingsTextbox {nameof(PluginConfig.CooldownSettings.ChatCommandCooldown)}", scrollContent, $"{editPanel}.scrollviewer",
                new CuiRectTransformComponent { AnchorMin = "0.51 1", AnchorMax = "1 1", OffsetMin = $"0 -{yStart + 30}", OffsetMax = $"-{formPanelInnerMargin - 5} -{yStart}" }
            );
            yStart += 30 + fieldsGap;
            
            //ZoneManagerCooldown Textbox
            UI_TextBox(
                session.Settings.ZoneManagerCooldown.ToString(), lang.GetMessage($"UI_Edit_Settings_ZoneManagerCooldown", this, player.UserIDString), $"{PREFIX_SHORT}.ui.texteditor Settings SettingsTextbox {nameof(PluginConfig.CooldownSettings.ZoneManagerCooldown)}", scrollContent, $"{editPanel}.scrollviewer",
                new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0.49 1", OffsetMin = $"{formPanelInnerMargin} -{yStart + 30}", OffsetMax = $"-{formPanelInnerMargin - 5} -{yStart}" }
            );
            //yStart += 30 + fieldsGap;
            
            //MonumentWatcherCooldown Textbox
            UI_TextBox(
                session.Settings.MonumentWatcherCooldown.ToString(), lang.GetMessage($"UI_Edit_Settings_MonumentWatcherCooldown", this, player.UserIDString), $"{PREFIX_SHORT}.ui.texteditor Settings SettingsTextbox {nameof(PluginConfig.CooldownSettings.MonumentWatcherCooldown)}", scrollContent, $"{editPanel}.scrollviewer",
                new CuiRectTransformComponent { AnchorMin = "0.51 1", AnchorMax = "1 1", OffsetMin = $"0 -{yStart + 30}", OffsetMax = $"-{formPanelInnerMargin - 5} -{yStart}" }
            );
            yStart += 30 + fieldsGap;
            
            //BroadcastToTeamOnly Checkbox
            UI_CheckBox(
                session.Settings.BroadcastToTeamOnly, nameof(PluginConfig.BroadcastToTeamOnly), lang.GetMessage("UI_Edit_Settings_BroadcastToTeamOnly", this, player.UserIDString), $"{PREFIX_SHORT}.ui.cmd SettingsCB", scrollContent, $"{editPanel}.scrollviewer",
                new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"{formPanelInnerMargin} -{yStart + 20}", OffsetMax = $"-{formPanelInnerMargin - 5} -{yStart}" }
            );
            yStart += 20 + fieldsGap;

            int minScrollviewerHeight = panelHeight - titleHeight - panelInnerMargin * 2 - formPanelInnerMargin * 2;
            scrollViewer.Components.Add(
                new CuiScrollViewComponent
                {
                    Vertical = true,
                    Horizontal = false,
                    MovementType = ScrollRect.MovementType.Elastic,
                    Elasticity = 0.3f,
                    Inertia = true,
                    DecelerationRate = 0.5f,
                    ScrollSensitivity = 25f,
                    ContentTransform = new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 -{(yStart > minScrollviewerHeight ? yStart : minScrollviewerHeight)}", OffsetMax = $"0 0" },
                    VerticalScrollbar = new CuiScrollbar { Invert = false, Size = 5f, TrackColor = HEXToRGBA("#121414", 90), HandleColor = HEXToRGBA("#262929", 85), HighlightColor = HEXToRGBA("#262929", 95) }
                }
            );

            cont.Add(scrollViewer);
            cont.AddRange(scrollContent);
            

            CuiHelper.AddUi(player, cont);
        }

        private void UI_ShowActionsView(BasePlayer player, string selectedTrigger, UISession session = null, int panelWidth = 1000, int panelHeight = 500, int panelInnerMargin = 30)
        {
            if (session == null && !UISessions.TryGetValue(player.userID, out session))
                return;

            UITrigger uiTrigger = session.Triggers.FirstOrDefault(s => s.TriggerDefinition.Key == selectedTrigger);

            if (uiTrigger == null)
                return;

            CuiElementContainer cont = new();

            //Center panel
            cont.Add(
                new CuiPanel
                {
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"-{panelWidth / 2} -{panelHeight / 2}", OffsetMax = $"{panelWidth / 2} {panelHeight / 2}" },
                    Image = { Color = HEXToRGBA("#1E2020", 95) }
                },
                uimodal,
                $"{uimodal}.actionsview",
                $"{uimodal}.actionsview"
            );

            int titleHeight = 30;
            int triggersButtonWidth = 75;
            int backButtonWidth = 60;
            int subtitleInnerGap = 1;

            //Back button
            cont.Add(
                new CuiButton
                {
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{panelInnerMargin} -{panelInnerMargin + 20}", OffsetMax = $"{panelInnerMargin + backButtonWidth} -{panelInnerMargin}" },
                    Text = { Text = lang.GetMessage("UI_Edit_Back", this, player.UserIDString).ToUpper(), FontSize = 12, Align = TextAnchor.MiddleCenter, Color = HEXToRGBA("#90CAF3") },
                    Button = { Color = HEXToRGBA("#376E92", 95), Command = $"{PREFIX_SHORT}.ui.cmd GoToTriggers" }
                },
                $"{uimodal}.actionsview"
            );

            //Sub-title
            cont.Add(
                new CuiElement
                {
                    Parent = $"{uimodal}.actionsview",
                    Name = $"{uimodal}.actionsview.title",
                    Components =
                    {
                        new CuiTextComponent() { Color = HEXToRGBA("#DCDCDC"), Text = lang.GetMessage($"UI_Edit_Trigger_{uiTrigger.TriggerDefinition.Key}", this, player.UserIDString).ToUpper(), FontSize = 16, Align = TextAnchor.UpperCenter },
                        new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"{panelInnerMargin + triggersButtonWidth + subtitleInnerGap} -{panelInnerMargin + titleHeight}", OffsetMax = $"-{panelInnerMargin + triggersButtonWidth + subtitleInnerGap} -{panelInnerMargin}" }
                    }
                }
            );


            CuiHelper.AddUi(player, cont);

            UI_ShowActionsList(player, uiTrigger, session.SelectedRepliesLang);
        }

        private void UI_ShowActionsList(BasePlayer player, UITrigger uiTrigger, string selectedRepliesLang, int selectedActionIndex = 0, int titleHeight = 30, int panelInnerMargin = 30)
        {
            CuiElementContainer cont = new();

            string parent = $"{uimodal}.actionsview";
            string listPanel = $"{uimodal}.actionsview.list";
            int barInnerMargin = 15;

            //Back panel
            cont.Add(
                new CuiPanel
                {
                    CursorEnabled = true,
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0.25 1", OffsetMin = $"{panelInnerMargin} {panelInnerMargin}", OffsetMax = $"0 -{panelInnerMargin + titleHeight}" },
                    Image = { Color = HEXToRGBA("#151717", 90) }
                },
                parent, listPanel, listPanel
            );

            int addBtnWidth = 60;

            //Add button
            cont.Add(
                new CuiButton
                {
                    RectTransform = { AnchorMin = "1 0", AnchorMax = "1 0", OffsetMin = $"-{addBtnWidth} -20", OffsetMax = $"0 0" },
                    Text = { Text = lang.GetMessage("UI_Edit_AddNew", this, player.UserIDString).ToUpper(), FontSize = 12, Align = TextAnchor.MiddleCenter, Color = HEXToRGBA("#90CAF3") },
                    Button = { Color = HEXToRGBA("#376E92", 95), Command = $"{PREFIX_SHORT}.ui.cmd AddNewAction {uiTrigger.TriggerDefinition.Key}" }
                },
                listPanel
            );

            int subtitleHeight = 25;

            //Sub-title
            cont.Add(
                new CuiElement
                {
                    Parent = listPanel,
                    Name = $"{listPanel}.title",
                    Components =
                    {
                        new CuiTextComponent() { Color = HEXToRGBA("#DCDCDC"), Text = lang.GetMessage("UI_Edit_Actions", this, player.UserIDString).ToUpper(), FontSize = 14, Align = TextAnchor.UpperLeft },
                        new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"{barInnerMargin} -{barInnerMargin + subtitleHeight}", OffsetMax = $"-{barInnerMargin} -{barInnerMargin}" }
                    }
                }
            );

            int totalCount = uiTrigger.Config_Actions.Count;
            if (totalCount > 0)
            {
                int itemHeight = 35;
                int gapHeight = 5;
                int minContentHeight = itemHeight * 9 + gapHeight * 8;
                int finalContentHeight = itemHeight * totalCount + gapHeight * (totalCount - 1);

                //Scrollviewer
                cont.Add(
                    new CuiElement
                    {
                        Parent = $"{listPanel}",
                        Name = $"{listPanel}.actionlist",
                        Components =
                        {
                            new CuiImageComponent() { Color = "1 0 1 0" },
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"{barInnerMargin} {barInnerMargin}", OffsetMax = $"-{barInnerMargin - 10} -{barInnerMargin + subtitleHeight}" },
                            new CuiNeedsCursorComponent(),
                            new CuiScrollViewComponent {
                                Vertical = true, Horizontal = false, MovementType = ScrollRect.MovementType.Elastic, Elasticity = 0.3f, Inertia = true, DecelerationRate = 0.5f, ScrollSensitivity = 25f,
                                ContentTransform = new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 -{(minContentHeight > finalContentHeight ? minContentHeight : finalContentHeight)}", OffsetMax = $"0 0" },
                                VerticalScrollbar = new CuiScrollbar { Invert = false, Size = 5f, TrackColor = HEXToRGBA("#121414", 90), HandleColor = HEXToRGBA("#262929", 85), HighlightColor = HEXToRGBA("#262929", 95) }
                            }
                        }
                    }
                );

                int actionIndex = 0;
                float usedHeight = 0;

                //Action list
                foreach (Config_Action action in uiTrigger.Config_Actions)
                {
                    string bgcolor = HEXToRGBA("#393C3C");

                    if (actionIndex == selectedActionIndex)
                        bgcolor = HEXToRGBA("#5A6060");

                    //Action panel
                    cont.Add(
                        new CuiPanel
                        {
                            CursorEnabled = true,
                            RectTransform = { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 -{usedHeight + itemHeight}", OffsetMax = $"-15 -{usedHeight}" },
                            Image = { Color = bgcolor }
                        },
                        $"{listPanel}.actionlist",
                        $"{listPanel}.actionlist.{actionIndex}"
                    );

                    string text = $"{lang.GetMessage("UI_Edit_Action", this, player.UserIDString)} {actionIndex + 1}";

                    if (uiTrigger.TriggerDefinition.RequiresTarget && !string.IsNullOrEmpty(action.Target))
                        text = $"{action.Target}";

                    switch (uiTrigger.TriggerDefinition.Key)
                    {
                        case "Timed":
                            if (action.Interval > 0)
                                text = $"Every {FormatTime(action.Interval * 60)}";
                            break;
                        case "ChatCommand":
                            if (!string.IsNullOrEmpty(action.Target))
                                text = string.Join(" ", action.Target.Split(',').Where(s => !string.IsNullOrEmpty(s.Trim())).Select(s => "/" + s.Trim()));
                            break;
                    }

                    //Action text
                    cont.Add(
                        new CuiElement
                        {
                            Parent = $"{listPanel}.actionlist.{actionIndex}",
                            Name = $"{listPanel}.actionlist.{actionIndex}.text",
                            Components =
                            {
                                new CuiTextComponent() { Color = HEXToRGBA("#FFFFFF"), Text = text, FontSize = 14, Align = TextAnchor.MiddleCenter },
                                new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0.95 1" }
                            }
                        }
                    );

                    //Action select button
                    cont.Add(
                        new CuiButton
                        {
                            RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                            Button = { Color = "0 0 0 0", Close = "", Command = $"{PREFIX_SHORT}.ui.cmd GoToActionForm {uiTrigger.TriggerDefinition.Key} {actionIndex}" }
                        },
                        $"{listPanel}.actionlist.{actionIndex}"
                    );

                    usedHeight += itemHeight + gapHeight;

                    actionIndex++;
                }
            }
            else
            {
                cont.Add(
                    new CuiElement
                    {
                        Parent = listPanel,
                        Name = $"{listPanel}.emptylist",
                        Components =
                        {
                            new CuiTextComponent() { Color = "1 1 1 0.8", Text = lang.GetMessage("UI_Edit_Actions_Empty", this, player.UserIDString).ToUpper(), FontSize = 14, Align = TextAnchor.MiddleLeft },
                            new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"{barInnerMargin} -{(barInnerMargin + 35 + titleHeight + 10)}", OffsetMax = $"0 -{barInnerMargin}" }
                        }
                    }
                );
            }

            // CuiHelper.DestroyUi(player, listPanel);
            CuiHelper.AddUi(player, cont);

            if (totalCount > 0)
                UI_ShowSelectedAction(player, uiTrigger, selectedActionIndex, selectedRepliesLang);
            else
            {
                CuiHelper.DestroyUi(player, $"{uimodal}.actionsview.editpanel");
                CuiHelper.DestroyUi(player, $"{uimodal}.langsview");
            }
        }

        private void UI_UpdateSelectedActionInList(BasePlayer player, UITrigger uiTrigger, int actionIndex)
        {
            CuiElementContainer cont = new();

            string text = $"Action {actionIndex + 1}";

            if (uiTrigger.TriggerDefinition.RequiresTarget && !string.IsNullOrEmpty(uiTrigger.Config_Actions[actionIndex].Target))
                text = $"{uiTrigger.Config_Actions[actionIndex].Target}";

            switch (uiTrigger.TriggerDefinition.Key)
            {
                case "Timed":
                    if (uiTrigger.Config_Actions[actionIndex].Interval > 0)
                        text = $"Every {FormatTime(uiTrigger.Config_Actions[actionIndex].Interval * 60)}";
                    break;
                case "ChatCommand":
                    if (!string.IsNullOrEmpty(uiTrigger.Config_Actions[actionIndex].Target))
                        text = $"/{uiTrigger.Config_Actions[actionIndex].Target}";
                    break;
            }

            //Action text
            cont.Add(
                new CuiElement
                {
                    Name = $"{uimodal}.actionsview.list.actionlist.{actionIndex}.text",
                    Components = { new CuiTextComponent() { Text = text } },
                    Update = true
                }
            );

            CuiHelper.AddUi(player, cont);
        }

        private void UI_ShowSelectedAction(BasePlayer player, UITrigger uiTrigger, int actionIndex, string selectedRepliesLang, int titleHeight = 30, int panelInnerMargin = 30)
        {
            CuiElementContainer cont = new();

            string parent = $"{uimodal}.actionsview";
            string editPanel = $"{parent}.editpanel";

            int formPanelInnerMargin = 20;

            Config_Action Config_Action = uiTrigger.Config_Actions[actionIndex];

            //Back panel
            cont.Add(
                new CuiPanel
                {
                    CursorEnabled = true,
                    RectTransform = { AnchorMin = "0.25 0", AnchorMax = "1 1", OffsetMin = $"{10} {panelInnerMargin}", OffsetMax = $"-{panelInnerMargin} -{panelInnerMargin + titleHeight}" },
                    Image = { Color = HEXToRGBA("#151717", 95) }
                },
                parent, editPanel, editPanel
            );

            int duplicateBtnWidth = 70;
            int deleteBtnWidth = 50;

            //Duplicate button
            cont.Add(
                new CuiButton
                {
                    RectTransform = { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = $"-{deleteBtnWidth + 5 + duplicateBtnWidth} 0", OffsetMax = $"-{deleteBtnWidth + 5} 20" },
                    Text = { Text = lang.GetMessage("UI_Edit_Duplicate", this, player.UserIDString).ToUpper(), FontSize = 12, Align = TextAnchor.MiddleCenter, Color = HEXToRGBA("#90CAF3") },
                    Button = { Color = HEXToRGBA("#376E92", 95), Command = $"{PREFIX_SHORT}.ui.cmd DuplicateAction {uiTrigger.TriggerDefinition.Key} {actionIndex}" }
                },
                editPanel
            );

            //Delete button
            cont.Add(
                new CuiButton
                {
                    RectTransform = { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = $"-{deleteBtnWidth} 0", OffsetMax = $"0 20" },
                    Text = { Text = lang.GetMessage("UI_Edit_Delete", this, player.UserIDString).ToUpper(), FontSize = 12, Align = TextAnchor.MiddleCenter, Color = HEXToRGBA("#FFC3B9") },
                    Button = { Color = HEXToRGBA("#CE422B", 95), Command = $"{PREFIX_SHORT}.ui.cmd DeleteAction {uiTrigger.TriggerDefinition.Key} {actionIndex}" }
                },
                editPanel
            );

            //Scrollviewer
            CuiElement scrollViewer = new CuiElement
            {
                Parent = editPanel,
                Name = $"{editPanel}.scrollviewer",
                Components =
                {
                    new CuiImageComponent() { Color = "1 0 1 0" },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"0 {formPanelInnerMargin}", OffsetMax = $"-5 -{formPanelInnerMargin}" },
                    new CuiNeedsCursorComponent()
                }
            };

            CuiElementContainer scrollContent = new();

            int yStart = 0;
            int fieldsGap = 15;

            //Checkbox
            UI_CheckBox(
                uiTrigger.Config_Actions[actionIndex].Enabled, nameof(Config_Action.Enabled), lang.GetMessage("UI_Edit_Action_Active", this, player.UserIDString), $"{PREFIX_SHORT}.ui.cmd ActionCB {uiTrigger.TriggerDefinition.Key} {actionIndex}", scrollContent, $"{editPanel}.scrollviewer",
                new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"{formPanelInnerMargin} -{yStart + 20}", OffsetMax = $"-{formPanelInnerMargin - 5} -{yStart}" },
                lang.GetMessage("UI_Edit_Action_Inactive", this, player.UserIDString)
            );
            yStart += 20 + fieldsGap;

            //Replies
            UI_Listbox(
                player, "Action", nameof(Lang_Action.Replies), uiTrigger.LangsTriggers[selectedRepliesLang].Actions[Config_Action.Id].Replies, lang.GetMessage("UI_Edit_Action_Replies", this, player.UserIDString).ToUpper(), $"{PREFIX_SHORT}.ui.cmd %cmdname% {uiTrigger.TriggerDefinition.Key} {actionIndex} %index%", scrollContent, $"{editPanel}.scrollviewer",
                new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"{formPanelInnerMargin} -{yStart + 145}", OffsetMax = $"-{formPanelInnerMargin - 5} -{yStart}" }, minItemsInView: 3, withSorting: true, previewButton: true
            );
            yStart += 145 + fieldsGap;

            if (uiTrigger.TriggerDefinition.Key == "Timed")
            {
                //Textbox
                UI_TextBox(
                    uiTrigger.Config_Actions[actionIndex].Interval.ToString(), $"Interval in minutes", $"{PREFIX_SHORT}.ui.texteditor Action ActionTextbox {uiTrigger.TriggerDefinition.Key} {actionIndex} {nameof(Config_Action.Interval)}", scrollContent, $"{editPanel}.scrollviewer",
                    new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"{formPanelInnerMargin} -{yStart + 30}", OffsetMax = $"-{formPanelInnerMargin - 5} -{yStart}" }
                );
                yStart += 30 + fieldsGap;
            }

            if (uiTrigger.TriggerDefinition.RequiresTarget)
            {
                //Textbox
                UI_TextBox(
                    uiTrigger.Config_Actions[actionIndex].Target, lang.GetMessage($"UI_Edit_Action_Target_{uiTrigger.TriggerDefinition.Key}", this, player.UserIDString), $"{PREFIX_SHORT}.ui.texteditor Action ActionTextbox {uiTrigger.TriggerDefinition.Key} {actionIndex} {nameof(Config_Action.Target)}", scrollContent, $"{editPanel}.scrollviewer",
                    new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"{formPanelInnerMargin} -{yStart + 30}", OffsetMax = $"-{formPanelInnerMargin - 5} -{yStart}" }
                );
                yStart += 30 + fieldsGap;
            }
            
            //SendInChat Checkbox
            UI_CheckBox(
                uiTrigger.Config_Actions[actionIndex].SendInChat, nameof(Config_Action.SendInChat), lang.GetMessage("UI_Edit_Action_SendInChat", this, player.UserIDString), $"{PREFIX_SHORT}.ui.cmd ActionCB {uiTrigger.TriggerDefinition.Key} {actionIndex}", scrollContent, $"{editPanel}.scrollviewer",
                new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0.49 1", OffsetMin = $"{formPanelInnerMargin} -{yStart + 20}", OffsetMax = $"-{formPanelInnerMargin - 5} -{yStart}" }
            );

            if (!_config.PlayerDataSettings.Disabled)
            {
                //PlayerCanDisable Checkbox
                UI_CheckBox(
                    uiTrigger.Config_Actions[actionIndex].PlayerCanDisable, nameof(Config_Action.PlayerCanDisable), string.Format(lang.GetMessage("UI_Edit_Action_CanDisable", this, player.UserIDString), _config.ToggleCommand), $"{PREFIX_SHORT}.ui.cmd ActionCB {uiTrigger.TriggerDefinition.Key} {actionIndex}", scrollContent, $"{editPanel}.scrollviewer",
                    new CuiRectTransformComponent { AnchorMin = "0.51 1", AnchorMax = "1 1", OffsetMin = $"0 -{yStart + 20}", OffsetMax = $"-{formPanelInnerMargin - 5} -{yStart}" }
                );
            }
            yStart += 20 + fieldsGap;
            
            //SendAsGameTip Checkbox
            UI_CheckBox(
                uiTrigger.Config_Actions[actionIndex].SendAsGameTip, nameof(Config_Action.SendAsGameTip), lang.GetMessage("UI_Edit_Action_SendAsGameTip", this, player.UserIDString), $"{PREFIX_SHORT}.ui.cmd ActionCB {uiTrigger.TriggerDefinition.Key} {actionIndex}", scrollContent, $"{editPanel}.scrollviewer",
                new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0.49 1", OffsetMin = $"{formPanelInnerMargin} -{yStart + 20}", OffsetMax = $"-{formPanelInnerMargin - 5} -{yStart}" }
            );
            
            //IsBlueGameTip Checkbox
            UI_CheckBox(
                uiTrigger.Config_Actions[actionIndex].IsBlueGameTip, nameof(Config_Action.IsBlueGameTip), lang.GetMessage("UI_Edit_Action_IsBlueGameTip", this, player.UserIDString), $"{PREFIX_SHORT}.ui.cmd ActionCB {uiTrigger.TriggerDefinition.Key} {actionIndex}", scrollContent, $"{editPanel}.scrollviewer",
                new CuiRectTransformComponent { AnchorMin = "0.51 1", AnchorMax = "1 1", OffsetMin = $"0 -{yStart + 20}", OffsetMax = $"-{formPanelInnerMargin - 5} -{yStart}" },
                lang.GetMessage("UI_Edit_Action_IsRedGameTip", this, player.UserIDString), enabledHex: "#376E92"
            );
            yStart += 20 + fieldsGap;

            if (uiTrigger.TriggerDefinition.UsesIsGlobalBroadcast)
            {
                //IsGlobalBroadcast Checkbox
                UI_CheckBox(
                    uiTrigger.Config_Actions[actionIndex].IsGlobalBroadcast, nameof(Config_Action.IsGlobalBroadcast), lang.GetMessage("UI_Edit_Action_BroadcastToAll", this, player.UserIDString), $"{PREFIX_SHORT}.ui.cmd ActionCB {uiTrigger.TriggerDefinition.Key} {actionIndex}", scrollContent, $"{editPanel}.scrollviewer",
                    new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0.49 1", OffsetMin = $"{formPanelInnerMargin} -{yStart + 20}", OffsetMax = $"-{formPanelInnerMargin - 5} -{yStart}" }
                );
                
                
                if (uiTrigger.TriggerDefinition.UsesDontTriggerAdmin)
                {
                    //DontTriggerAdmin Checkbox
                    UI_CheckBox(
                        uiTrigger.Config_Actions[actionIndex].DontTriggerAdmin, nameof(Config_Action.DontTriggerAdmin), lang.GetMessage("UI_Edit_Action_ExcludeAdmin", this, player.UserIDString), $"{PREFIX_SHORT}.ui.cmd ActionCB {uiTrigger.TriggerDefinition.Key} {actionIndex}", scrollContent, $"{editPanel}.scrollviewer",
                        new CuiRectTransformComponent { AnchorMin = "0.51 1", AnchorMax = "1 1", OffsetMin = $"0 -{yStart + 20}", OffsetMax = $"-{formPanelInnerMargin - 5} -{yStart}" }
                    );
                }
                yStart += 20 + fieldsGap;
            }

            if (uiTrigger.TriggerDefinition.HasTriggerOwner)
            {
			    //UseTriggerOwnerIcon Checkbox
                UI_CheckBox(
                    uiTrigger.Config_Actions[actionIndex].UseTriggerOwnerIcon, nameof(Config_Action.UseTriggerOwnerIcon), lang.GetMessage("UI_Edit_Action_UseTriggerOwnerIcon", this, player.UserIDString), $"{PREFIX_SHORT}.ui.cmd ActionCB {uiTrigger.TriggerDefinition.Key} {actionIndex}", scrollContent, $"{editPanel}.scrollviewer",
                    new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0.49 1", OffsetMin = $"{formPanelInnerMargin} -{yStart + 20}", OffsetMax = $"-{formPanelInnerMargin - 5} -{yStart}" }
                );
                yStart += 20 + fieldsGap;
            }
            
            //CustomIconSteamId Textbox
            UI_TextBox(
                uiTrigger.Config_Actions[actionIndex].CustomIconSteamId, lang.GetMessage($"UI_Edit_Action_CustomIconSteamId", this, player.UserIDString), $"{PREFIX_SHORT}.ui.texteditor Action ActionTextbox {uiTrigger.TriggerDefinition.Key} {actionIndex} {nameof(Config_Action.CustomIconSteamId)}", scrollContent, $"{editPanel}.scrollviewer",
                new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"{formPanelInnerMargin} -{yStart + 30}", OffsetMax = $"-{formPanelInnerMargin - 5} -{yStart}" }
            );
            yStart += 30 + fieldsGap;

            //Permissions
            UI_Listbox(
                player, "Action", nameof(Config_Action.Permissions), uiTrigger.Config_Actions[actionIndex].Permissions, lang.GetMessage("UI_Edit_Action_Permissions", this, player.UserIDString).ToUpper(), $"{PREFIX_SHORT}.ui.cmd %cmdname% {uiTrigger.TriggerDefinition.Key} {actionIndex} %index%", scrollContent, $"{editPanel}.scrollviewer",
                new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0.49 1", OffsetMin = $"{formPanelInnerMargin} -{yStart + 110}", OffsetMax = $"0 -{yStart}" }
            );
            //yStart += 115 + fieldsGap;

            //Groups
            UI_Listbox(
                player, "Action", nameof(Config_Action.Groups), uiTrigger.Config_Actions[actionIndex].Groups, lang.GetMessage("UI_Edit_Action_Groups", this, player.UserIDString).ToUpper(), $"{PREFIX_SHORT}.ui.cmd %cmdname% {uiTrigger.TriggerDefinition.Key} {actionIndex} %index%", scrollContent, $"{editPanel}.scrollviewer",
                new CuiRectTransformComponent { AnchorMin = "0.51 1", AnchorMax = "1 1", OffsetMin = $"0 -{yStart + 110}", OffsetMax = $"-{formPanelInnerMargin - 5} -{yStart}" }
            );
            yStart += 115 + fieldsGap;

            //Blacklisted Permissions
            UI_Listbox(
                player, "Action", nameof(Config_Action.BlacklistedPerms), uiTrigger.Config_Actions[actionIndex].BlacklistedPerms, lang.GetMessage("UI_Edit_Action_ExcludedPermissions", this, player.UserIDString).ToUpper(), $"{PREFIX_SHORT}.ui.cmd %cmdname% {uiTrigger.TriggerDefinition.Key} {actionIndex} %index%", scrollContent, $"{editPanel}.scrollviewer",
                new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0.49 1", OffsetMin = $"{formPanelInnerMargin} -{yStart + 110}", OffsetMax = $"0 -{yStart}" }
            );
            //yStart += 115 + fieldsGap;

            //Blacklisted Groups
            UI_Listbox(
                player, "Action", nameof(Config_Action.BlacklistedGroups), uiTrigger.Config_Actions[actionIndex].BlacklistedGroups, lang.GetMessage("UI_Edit_Action_ExcludedGroups", this, player.UserIDString).ToUpper(), $"{PREFIX_SHORT}.ui.cmd %cmdname% {uiTrigger.TriggerDefinition.Key} {actionIndex} %index%", scrollContent, $"{editPanel}.scrollviewer",
                new CuiRectTransformComponent { AnchorMin = "0.51 1", AnchorMax = "1 1", OffsetMin = $"0 -{yStart + 110}", OffsetMax = $"-{formPanelInnerMargin - 5} -{yStart}" }
            );
            yStart += 110 + fieldsGap;

            scrollViewer.Components.Add(
                new CuiScrollViewComponent
                {
                    Vertical = true,
                    Horizontal = false,
                    MovementType = ScrollRect.MovementType.Elastic,
                    Elasticity = 0.3f,
                    Inertia = true,
                    DecelerationRate = 0.5f,
                    ScrollSensitivity = 25f,
                    ContentTransform = new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 -{yStart}", OffsetMax = $"0 0" },
                    VerticalScrollbar = new CuiScrollbar { Invert = false, Size = 5f, TrackColor = HEXToRGBA("#121414", 90), HandleColor = HEXToRGBA("#262929", 85), HighlightColor = HEXToRGBA("#262929", 95) }
                }
            );

            cont.Add(scrollViewer);
            cont.AddRange(scrollContent);

            CuiHelper.AddUi(player, cont);

            UI_ShowRepliesLanguages(player, UISessions[player.userID].SelectedRepliesLang, uiTrigger, actionIndex);
        }

        private void UI_ShowRepliesLanguages(BasePlayer player, string selectedRepliesLang, UITrigger uiTrigger, int actionIndex, int centerPanelWidth = 1000, int centerPanelHeight = 500)
        {
            CuiElementContainer cont = new();

            string parent = $"{uimodal}";
            string listPanel = $"{uimodal}.langsview";

            int panelWidth = 640 - centerPanelWidth / 2 - 30;
            //Back panel
            cont.Add(
                new CuiPanel
                {
                    CursorEnabled = true,
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{centerPanelWidth / 2 + 15} -{centerPanelHeight / 2}", OffsetMax = $"{centerPanelWidth / 2 + 15 + panelWidth} {centerPanelHeight / 2}" },
                    Image = { Color = "0 0 0 0" }
                },
                parent, listPanel, listPanel
            );

            int titleHeight = 25;
            int gapHeight = 5;

            //Langs Title
            cont.Add(
                new CuiElement
                {
                    Parent = $"{listPanel}",
                    Components =
                    {
                        new CuiTextComponent() { Color = HEXToRGBA("#DCDCDC"), Text = $"<i>{lang.GetMessage("UI_Edit_LanguagesTitle", this, player.UserIDString).ToUpper()}</i>", FontSize = 11, Align = TextAnchor.UpperLeft, Font = "robotocondensed-regular.ttf" },
                        new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 -{titleHeight}", OffsetMax = "0 0" }
                    }
                }
            );

            //Scrollviewer
            CuiElement scrollViewer = new CuiElement
            {
                Parent = $"{listPanel}",
                Name = $"{listPanel}.scrollviewer",
                Components =
                {
                    new CuiImageComponent() { Color = "0 0 0 0" },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0.85 1", OffsetMin = $"0 0", OffsetMax = $"0 -{titleHeight + gapHeight}" },
                    new CuiNeedsCursorComponent()
                }
            };

            int yStart = 0;
            int itemHeight = 20;
            int finalContentHeight = 0;
            int minContentHeight = centerPanelHeight - titleHeight - gapHeight;

            CuiElementContainer scrollContent = new CuiElementContainer();

            //Langs list
            foreach (string language in UISessions[player.userID].Settings.ServerLangs)
            {
                if(!AvailableLangs.ContainsKey(language)) continue;
                
                bool selected = language == selectedRepliesLang;

                //Lang panel
                scrollContent.Add(
                    new CuiPanel
                    {
                        CursorEnabled = true,
                        RectTransform = { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 -{yStart + itemHeight}", OffsetMax = $"-10 -{yStart}" },
                        Image = { Color = selected ? HEXToRGBA("#79A62F", 80) : HEXToRGBA("#5A6060", 50) }
                    },
                    $"{listPanel}.scrollviewer",
                    $"{listPanel}.scrollviewer.{language}"
                );

                finalContentHeight = yStart + itemHeight;
                yStart += itemHeight + gapHeight;

                //Lang text
                scrollContent.Add(
                    new CuiElement
                    {
                        Parent = $"{listPanel}.scrollviewer.{language}",
                        Components =
                        {
                            new CuiTextComponent() { Color = selected ? "1 1 1 1" : "1 1 1 0.5", Text = $"{AvailableLangs[language].ToUpper()}", FontSize = 10, Align = TextAnchor.MiddleCenter },
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0.95 1" }
                        }
                    }
                );

                //Lang select button
                scrollContent.Add(
                    new CuiButton
                    {
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                        Button = { Color = "0 0 0 0", Close = "", Command = $"{PREFIX_SHORT}.ui.cmd ChangeRepliesLang {language} {uiTrigger.TriggerDefinition.Key} {actionIndex}" }
                    },
                    $"{listPanel}.scrollviewer.{language}"
                );
            }

            //Now add ScrollViewComponent based on final content height
            scrollViewer.Components.Add(
                new CuiScrollViewComponent
                {
                    Vertical = true, Horizontal = false, MovementType = ScrollRect.MovementType.Elastic, Elasticity = 0.3f, Inertia = true, DecelerationRate = 0.5f, ScrollSensitivity = 30f,
                    ContentTransform = new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 -{(minContentHeight > finalContentHeight ? minContentHeight : finalContentHeight)}", OffsetMax = $"0 0" },
                    VerticalScrollbar = new CuiScrollbar { Invert = false, Size = 2f, AutoHide = true, TrackColor = HEXToRGBA("#121414", 15), HandleColor = HEXToRGBA("#828282", 15), HighlightColor = HEXToRGBA("#828282", 20), PressedColor = HEXToRGBA("#828282", 15) }
                }
            );

            //Now add scrollviewer and content
            cont.Add(scrollViewer);
            cont.AddRange(scrollContent);

            CuiHelper.AddUi(player, cont);
        }

        private void UI_Listbox(BasePlayer player, string view, string key, List<string> list, string label, string command, CuiElementContainer cont, string parent, CuiRectTransformComponent RectTransform, int minItemsInView = 2, bool withSorting = false, bool previewButton = false)
        {
            string listPanel = $"{uimodal}.list.{key}";

            int listInnerMargin = 10;

            //Back panel
            cont.Add(
                new CuiPanel
                {
                    CursorEnabled = true,
                    RectTransform = { AnchorMin = RectTransform.AnchorMin, AnchorMax = RectTransform.AnchorMax, OffsetMin = RectTransform.OffsetMin, OffsetMax = RectTransform.OffsetMax },
                    Image = { Color = HEXToRGBA("#0A0C0C", 95) }
                },
                parent, listPanel, listPanel
            );

            int addBtnWidth = 35;

            //Add button
            cont.Add(
                new CuiButton
                {
                    RectTransform = { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = $"-{addBtnWidth} -17", OffsetMax = $"0 0" },
                    Text = { Text = lang.GetMessage("UI_Edit_Add", this, player.UserIDString).ToUpper(), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = HEXToRGBA("#90CAF3") },
                    Button = { Color = HEXToRGBA("#376E92", 95), Command = command.Replace("%cmdname%", $"{view}AddListItem").Replace("%index%", $"{key}") }
                },
                listPanel
            );

            int subtitleHeight = 20;

            //Sub-title
            cont.Add(
                new CuiElement
                {
                    Parent = listPanel,
                    Name = $"{listPanel}.title",
                    Components =
                    {
                        new CuiTextComponent() { Color = HEXToRGBA("#FFFFFF"), Text = label, FontSize = 13, Align = TextAnchor.UpperLeft },
                        new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0.8 1", OffsetMin = $"{listInnerMargin} -{listInnerMargin + subtitleHeight}", OffsetMax = $"-{listInnerMargin} -{listInnerMargin}" }
                    }
                }
            );

            UI_Listbox_Scrollviewer(player, view, key, listPanel, list, listInnerMargin, subtitleHeight, command, withSorting, previewButton, minItemsInView, ref cont);
        }

        private void UI_Listbox_Scrollviewer(BasePlayer player, string view, string key, string listPanel, List<string> list, int listInnerMargin, int subtitleHeight, string command, bool withSorting, bool previewButton, int minItemsInView, ref CuiElementContainer cont, bool scrollToBottom = false)
        {
            int totalCount = list.Count;
            int scrollCount = totalCount > minItemsInView ? totalCount : minItemsInView;

            float itemHeight = 30;
            float gapHeight = 4;

            string contentAnchorMin = scrollToBottom ? "0 0" : "0 1";
            string contentAnchorMax = scrollToBottom ? "1 0" : "1 1";
            string contentOffsetMin = scrollToBottom ? $"0 0" : $"0 -{scrollCount * itemHeight + (scrollCount - 1) * gapHeight}";
            string contentOffsetMax = scrollToBottom ? $"0 {scrollCount * itemHeight + (scrollCount - 1) * gapHeight}" : $"0 0";

            //Scrollviewer
            cont.Add(
                new CuiElement
                {
                    Parent = listPanel,
                    Name = $"{listPanel}.scrollviewer",
                    DestroyUi = $"{listPanel}.scrollviewer",
                    Components =
                    {
                        new CuiImageComponent() { Color = "1 0 1 0" },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"{listInnerMargin} {listInnerMargin}", OffsetMax = $"-{listInnerMargin} -{listInnerMargin + subtitleHeight + gapHeight}" },
                        new CuiNeedsCursorComponent(),
                        new CuiScrollViewComponent
                        {
                            Vertical = true, Horizontal = false, MovementType = ScrollRect.MovementType.Elastic, Elasticity = 0.3f, Inertia = true, DecelerationRate = 0.5f, ScrollSensitivity = 15f,
                            ContentTransform = new CuiRectTransformComponent { AnchorMin = contentAnchorMin, AnchorMax = contentAnchorMax, OffsetMin = contentOffsetMin, OffsetMax = contentOffsetMax },
                            VerticalScrollbar = new CuiScrollbar { Invert = false, Size = 5f, TrackColor = HEXToRGBA("#121414", 90), HandleColor = HEXToRGBA("#262929", 85), HighlightColor = HEXToRGBA("#262929", 95) }
                        }
                    }
                }
            );

            string altColor1 = HEXToRGBA("#393C3C", 95);
            string altColor2 = HEXToRGBA("#393C3C", 60);

            int itemIndex = 0;
            float usedHeight = 0;

            foreach (string text in list)
            {
                string bgcolor = itemIndex % 2 == 0 ? altColor1 : altColor2;

                cont.Add(
                    new CuiPanel
                    {
                        CursorEnabled = true,
                        RectTransform = { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 -{usedHeight + itemHeight}", OffsetMax = $"-10 -{usedHeight}" },
                        Image = { Color = bgcolor }
                    },
                    $"{listPanel}.scrollviewer", $"{listPanel}.scrollviewer.{itemIndex}"
                );

                cont.Add(
                    new CuiElement
                    {
                        Parent = $"{listPanel}.scrollviewer.{itemIndex}",
                        Name = $"{listPanel}.scrollviewer.{itemIndex}.text",
                        Components =
                        {
                            new CuiTextComponent() { Color = "1 1 1 1", Text = text, FontSize = 12, Align = TextAnchor.UpperLeft },
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"{(withSorting ? 52 : 10)} 0", OffsetMax = $"{(previewButton ? -100 : -50)} -9" }
                        }
                    }
                );

                cont.Add(
                    new CuiButton
                    {
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"{(withSorting ? 52 : 0)} 0", OffsetMax = $"0 0" },
                        Button = { Color = "0 0 0 0", Close = "", Command = command.Replace("%cmdname%", $"{view}SelectListItem").Replace("%index%", $"{key} {itemIndex.ToString()}") }
                    },
                    $"{listPanel}.scrollviewer.{itemIndex}"
                );

                if (withSorting)
                {
                    //Move up
                    cont.Add(
                        new CuiButton
                        {
                            RectTransform = { AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = $"5 5", OffsetMax = $"25 -5" },
                            Button =
                            {
                                Color = HEXToRGBA("#79A62F", 70), 
                                Command = command.Replace("%cmdname%", $"{view}MoveUpListItem").Replace("%index%", $"{key} {itemIndex.ToString()}"),
                                Sprite = "Assets/Icons/elevator_up.png"
                            }
                        },
                        $"{listPanel}.scrollviewer.{itemIndex}"
                    );
                
                    //Move down
                    cont.Add(
                        new CuiButton
                        {
                            RectTransform = { AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = $"27 5", OffsetMax = $"47 -5" },
                            Button = { 
                                Color = HEXToRGBA("#79A62F", 70), 
                                Command = command.Replace("%cmdname%", $"{view}MoveDownListItem").Replace("%index%", $"{key} {itemIndex.ToString()}"),
                                Sprite = "Assets/Icons/elevator_down.png"
                            }
                        },
                        $"{listPanel}.scrollviewer.{itemIndex}"
                    );
                }

                //Delete button
                cont.Add(
                    new CuiButton
                    {
                        RectTransform = { AnchorMin = "1 0", AnchorMax = "1 1", OffsetMin = $"-45 5", OffsetMax = $"-5 -5" },
                        Text = { Text = lang.GetMessage("UI_Edit_Delete", this, player.UserIDString).ToUpper(), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = HEXToRGBA("#FFC3B9") },
                        Button = { Color = HEXToRGBA("#CE422B", 95), Command = command.Replace("%cmdname%", $"{view}DeleteListItem").Replace("%index%", $"{key} {itemIndex.ToString()}") }
                    },
                    $"{listPanel}.scrollviewer.{itemIndex}"
                );

                if (previewButton)
                {
                    //Preview button
                    cont.Add(
                        new CuiButton
                        {
                            RectTransform = { AnchorMin = "1 0", AnchorMax = "1 1", OffsetMin = $"-95 5", OffsetMax = $"-50 -5" },
                            Text = { Text = lang.GetMessage("UI_Edit_Preview", this, player.UserIDString).ToUpper(), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = HEXToRGBA("#DFFFAA") },
                            Button = { Color = HEXToRGBA("#79A62F", 95), Command = command.Replace("%cmdname%", $"{view}PreviewReply").Replace("%index%", $"{key} {itemIndex.ToString()}") }
                        },
                        $"{listPanel}.scrollviewer.{itemIndex}"
                    );
                }

                usedHeight += itemHeight + gapHeight;

                itemIndex++;
            }
        }

        private void UI_Listbox_Update(BasePlayer player, string view, string key, string command, List<string> list, CuiElementContainer cont, string label = null, bool scrollToBottom = false, int minItemsInView = 2, bool withSorting = false, bool previewButton = false)
        {
            string listPanel = $"{uimodal}.list.{key}";

            int listInnerMargin = 10;

            int subtitleHeight = 20;

            if (!string.IsNullOrEmpty(label))
            {
                //Sub-title
                cont.Add(
                    new CuiElement
                    {
                        Name = $"{listPanel}.title",
                        Components = { new CuiTextComponent() { Text = label } },
                        Update = true
                    }
                );
            }

            UI_Listbox_Scrollviewer(player, view, key, listPanel, list, listInnerMargin, subtitleHeight, command, withSorting, previewButton, minItemsInView, ref cont, scrollToBottom);
        }

        private void UI_CheckBox(bool isChecked, string key, string label, string command, CuiElementContainer cont, string parent, CuiRectTransformComponent RectTransform, string uncheckedText = null, string enabledHex = "#648135", string disabledHex = "#CE422B")
        {
            string cbName = $"{uimodal}.cb.{key}";

            //Back panel
            cont.Add(new CuiElement { Parent = parent, Name = cbName, DestroyUi = cbName, Components = { new CuiImageComponent() { Color = "1 0 0 0" }, RectTransform } });

            string checkedColor = isChecked ? HEXToRGBA(enabledHex) : HEXToRGBA(disabledHex);

            int toggleWidth = 40;

            cont.Add(
                new CuiElement
                {
                    Parent = cbName,
                    Name = $"{cbName}.toggle",
                    Components = {
                        new CuiImageComponent() { Color = checkedColor },
                        new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = $"0 -10", OffsetMax = $"{toggleWidth} 10" }
                    }
                }
            );

            int handleXAnchor = isChecked ? 1 : 0;
            int handleXOffsetMin = isChecked ? -20 : 2;
            int handleXOffsetMax = isChecked ? -2 : 20;

            cont.Add(
                new CuiElement
                {
                    Parent = $"{cbName}.toggle",
                    Name = $"{cbName}.toggle.handle",
                    Components =
                    {
                        new CuiImageComponent() { Color = HEXToRGBA("#151617") },
                        new CuiRectTransformComponent { AnchorMin = $"{handleXAnchor} 0", AnchorMax = $"{handleXAnchor} 1", OffsetMin = $"{handleXOffsetMin} 2", OffsetMax = $"{handleXOffsetMax} -2" }
                    }
                }
            );

            cont.Add(
                new CuiButton
                {
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    Button = { Color = "0 0 0 0", Close = "", Command = $"{command} {key}" }
                },
                cbName
            );

            string labelText = !string.IsNullOrEmpty(uncheckedText) && !isChecked ? uncheckedText : label;

            cont.Add(
                new CuiElement
                {
                    Parent = cbName,
                    Name = $"{cbName}.button",
                    Components =
                    {
                        new CuiTextComponent() { Color = "1 1 1 1", Text = labelText, FontSize = 12, Align = TextAnchor.MiddleLeft },
                        new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "1 0.5", OffsetMin = $"{toggleWidth + 10} -10", OffsetMax = $"0 10" }
                    }
                }
            );
        }

        private void UI_CheckBox_Update(bool isChecked, string key, CuiElementContainer cont, string label = null, string uncheckedText = null, string enabledHex = "#648135", string disabledHex = "#CE422B")
        {
            string cbName = $"{uimodal}.cb.{key}";

            string checkedColor = isChecked ? HEXToRGBA(enabledHex) : HEXToRGBA(disabledHex);

            cont.Add(
                new CuiElement
                {
                    Name = $"{cbName}.toggle",
                    Components = { new CuiImageComponent() { Color = checkedColor } },
                    Update = true
                }
            );

            int handleXAnchor = isChecked ? 1 : 0;
            int handleXOffsetMin = isChecked ? -20 : 2;
            int handleXOffsetMax = isChecked ? -2 : 20;

            cont.Add(
                new CuiElement
                {
                    Name = $"{cbName}.toggle.handle",
                    Components = { new CuiRectTransformComponent { AnchorMin = $"{handleXAnchor} 0", AnchorMax = $"{handleXAnchor} 1", OffsetMin = $"{handleXOffsetMin} 2", OffsetMax = $"{handleXOffsetMax} -2" } },
                    Update = true
                }
            );

            string labelText = !string.IsNullOrEmpty(uncheckedText) && !isChecked ? uncheckedText : label;

            if (!string.IsNullOrEmpty(labelText))
            {
                cont.Add(
                    new CuiElement
                    {
                        Name = $"{cbName}.button",
                        Components = { new CuiTextComponent() { Text = labelText } },
                        Update = true
                    }
                );
            }
        }

        private void UI_TextBox(string value, string label, string command, CuiElementContainer cont, string parent, CuiRectTransformComponent RectTransform, string uncheckedText = null)
        {
            string cbName = CuiHelper.GetGuid();

            //Back panel
            cont.Add(new CuiElement { Parent = parent, Name = cbName, Components = { new CuiImageComponent() { Color = "1 0 0 0" }, RectTransform } });

            cont.Add(new CuiElement { Parent = cbName, Name = $"{cbName}.textbox.wrapper", Components = { new CuiImageComponent() { Color = HEXToRGBA("#0A0C0C", 90) }, new CuiRectTransformComponent { AnchorMin = "0.51 0", AnchorMax = "1 1" } } });
            cont.Add(new CuiElement
            {
                Parent = $"{cbName}.textbox.wrapper",
                Name = $"{cbName}.textbox",
                Components =
                {
                    new CuiInputFieldComponent { Align = TextAnchor.MiddleLeft, Command = command, FontSize = 12, Text = value, NeedsKeyboard = true, LineType = InputField.LineType.SingleLine },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "5 5", OffsetMax = "-5 -5" }
                }
            });

            cont.Add(
                new CuiElement
                {
                    Parent = cbName,
                    Components =
                    {
                        new CuiTextComponent() { Color = "1 1 1 1", Text = label, FontSize = 12, Align = TextAnchor.MiddleLeft },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0.49 1" }
                    }
                }
            );
        }

        private void UI_ShowTextEditor(BasePlayer player, string view, string text, string title, string args, string listType, int listItemIndex)
        {
            CuiElementContainer cont = new();

            string texteditormodal = $"{uimodal}.replyeditor";

            //Parent panel
            cont.Add(
                new CuiPanel
                {
                    CursorEnabled = true,
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    Image = { Color = HEXToRGBA("#000000", 95), Material = "assets/content/ui/uibackgroundblur.mat" }
                },
                uimodal,
                texteditormodal,
                texteditormodal
            );

            //Title
            cont.Add(
                new CuiElement
                {
                    Parent = texteditormodal,
                    Name = $"{texteditormodal}.title",
                    Components =
                    {
                        new CuiTextComponent() { Color = HEXToRGBA("#DCDCDC"), Text = title.ToUpper(), FontSize = 24, Align = TextAnchor.MiddleCenter },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-200 50", OffsetMax = "200 100" }
                    }
                }
            );

            //center panel
            cont.Add(
                new CuiPanel
                {
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-300.45592 -25", OffsetMax = "300.45592 25" },
                    Image = { Color = HEXToRGBA("#272B2B", 90) }
                },
                texteditormodal,
                $"{texteditormodal}.panel"
            );

            cont.Add(new CuiElement
            {
                Parent = $"{texteditormodal}.panel",
                Name = $"{texteditormodal}.panel.textbox",
                Components =
                {
                    new CuiInputFieldComponent { Color = HEXToRGBA("#DDDDDD"),  Align = TextAnchor.MiddleLeft,
                        Command = $"{PREFIX_SHORT}.ui.textboxcmd {view} {args} {listType} {listItemIndex}",
                        FontSize = 20, Text = text, NeedsKeyboard = true, LineType = InputField.LineType.SingleLine },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "20 5", OffsetMax = "-20 -5" }
                }
            });

            int doneWidth = 100;
            string closeCommand = $"{PREFIX_SHORT}.ui.cmd {view}RefreshList {args} {listType} {listItemIndex}";

            //Done button
            cont.Add(
                new CuiButton
                {
                    RectTransform = { AnchorMin = "1 0", AnchorMax = "1 0", OffsetMin = $"-{doneWidth} -{30}", OffsetMax = $"0 -5" },
                    Text = { Text = "DONE", FontSize = 15, Align = TextAnchor.MiddleCenter, Color = HEXToRGBA("#90CAF3") },
                    Button = { Color = HEXToRGBA("#376E92", 95), Close = texteditormodal, Command = closeCommand }
                },
                $"{texteditormodal}.panel"
            );

            CuiHelper.AddUi(player, cont);
        }

        private void UI_ShowMultilineTextEditor(BasePlayer player, string text, string title, TriggerDefinition triggerDefinition, int actionIndex, int listItemIndex)
        {
            CuiElementContainer cont = new();

            string texteditormodal = $"{uimodal}.replyeditor";

            //Parent panel
            cont.Add(
                new CuiPanel
                {
                    CursorEnabled = true,
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    Image = { Color = HEXToRGBA("#000000", 60), Material = "assets/content/ui/uibackgroundblur.mat" }
                },
                uimodal,
                texteditormodal,
                texteditormodal
            );

            //Title
            cont.Add(
                new CuiElement
                {
                    Parent = texteditormodal,
                    Name = $"{texteditormodal}.title",
                    Components =
                    {
                        new CuiTextComponent() { Color = HEXToRGBA("#DCDCDC"), Text = title.ToUpper(), FontSize = 24, Align = TextAnchor.MiddleCenter },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-200 225", OffsetMax = "200 275" }
                    }
                }
            );

            //center panel
            cont.Add(
                new CuiPanel
                {
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-400 -200", OffsetMax = "400 200" },
                    Image = { Color = HEXToRGBA("#393C3C", 95) }
                },
                texteditormodal,
                $"{texteditormodal}.panel"
            );
            
            List<string> allVars = new(triggerDefinition.Variables);
            allVars.AddRange(AvailableVariables);
            
            if(_config.PlayerDataSettings.Disabled)
                allVars.Remove("{playercountry}");
            
            string tips = string.Format(lang.GetMessage("UI_Edit_Reply_AvailableVariables", this, player.UserIDString), string.Join("\n", allVars), "+ Placeholder API Supported");
            //Tips
            cont.Add(
                new CuiElement
                {
                    Parent = $"{texteditormodal}.panel",
                    Components =
                    {
                        new CuiTextComponent() { Color = HEXToRGBA("#DCDCDC"), Text = tips, FontSize = 13, Align = TextAnchor.UpperLeft, Font = "robotocondensed-regular.ttf" },
                        new CuiRectTransformComponent { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = "20 -200", OffsetMax = "200 0" }
                    }
                }
            );

            int doneWidth = 100;

            string closeCommand = $"{PREFIX_SHORT}.ui.cmd ActionRefreshList {triggerDefinition.Key} {actionIndex} Replies {listItemIndex}";

            //Done button
            cont.Add(
                new CuiButton
                {
                    RectTransform = { AnchorMin = "1 0", AnchorMax = "1 0", OffsetMin = $"-{doneWidth} -{30}", OffsetMax = $"0 -5" },
                    Text = { Text = lang.GetMessage("UI_Edit_Done", this, player.UserIDString).ToUpper(), FontSize = 15, Align = TextAnchor.MiddleCenter, Color = HEXToRGBA("#90CAF3") },
                    Button = { Color = HEXToRGBA("#376E92", 95), Close = texteditormodal, Command = closeCommand }
                },
                $"{texteditormodal}.panel"
            );

            //Preview button
            cont.Add(
                new CuiButton
                {
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = $"0 -{30}", OffsetMax = $"{130} -5" },
                    Text = { Text = lang.GetMessage("UI_Edit_PreviewInChat", this, player.UserIDString).ToUpper(), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = HEXToRGBA("#DFFFAA") },
                    Button = { Color = HEXToRGBA("#79A62F", 95), Command = $"{PREFIX_SHORT}.ui.cmd ActionPreviewReply {triggerDefinition.Key} {actionIndex} Replies {listItemIndex} chat" }
                },
                $"{texteditormodal}.panel"
            );

            //Preview game tip button
            cont.Add(
                new CuiButton
                {
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = $"{135} -{30}", OffsetMax = $"{270} -5" },
                    Text = { Text = lang.GetMessage("UI_Edit_PreviewGameTip", this, player.UserIDString).ToUpper(), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = HEXToRGBA("#DFFFAA") },
                    Button = { Color = HEXToRGBA("#79A62F", 95), Command = $"{PREFIX_SHORT}.ui.cmd ActionPreviewReply {triggerDefinition.Key} {actionIndex} Replies {listItemIndex} gametip" }
                },
                $"{texteditormodal}.panel"
            );

            cont.Add(
                new CuiPanel
                {
                    CursorEnabled = true,
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"0 0", OffsetMax = $"0 0" },
                    Image = { Color = "1 1 1 0" }
                },
                $"{texteditormodal}.panel",
                $"{texteditormodal}.panel.innerpanel"
            );

            cont.Add(
                new CuiElement
                {
                    Parent = $"{texteditormodal}.panel.innerpanel",
                    Name = $"{texteditormodal}.panel.innerpanel.subtitle1",
                    Components =
                    {
                        new CuiTextComponent() { Color = HEXToRGBA("#DCDCDC"), Text = lang.GetMessage("UI_Edit_Preview", this, player.UserIDString).ToUpper(), FontSize = 14, Align = TextAnchor.UpperLeft, Font = "robotocondensed-bold.ttf" },
                        new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "20 -40", OffsetMax = "-20 -20" }
                    }
                }
            );

            cont.Add(
                new CuiElement
                {
                    Parent = $"{texteditormodal}.panel.innerpanel",
                    Name = $"{texteditormodal}.panel.innerpanel.preview",
                    Components =
                    {
                            new CuiImageComponent() { Color = "1 0 1 0" },
                            new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "1 1", OffsetMin = "20 10", OffsetMax = "-20 -45" },
                            new CuiNeedsCursorComponent(),
                            new CuiScrollViewComponent {
                                Vertical = true, Horizontal = false, MovementType = ScrollRect.MovementType.Elastic, Elasticity = 0.3f, Inertia = true, DecelerationRate = 0.5f, ScrollSensitivity = 10f,
                                ContentTransform = new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 -{500}", OffsetMax = $"0 0" },
                                VerticalScrollbar = new CuiScrollbar { Size = 8f, TrackColor = HEXToRGBA("#121414", 90), HandleColor = HEXToRGBA("#262929", 85), HighlightColor = HEXToRGBA("#262929", 95) }
                            }
                    }
                }
            );

            cont.Add(
                new CuiElement
                {
                    Parent = $"{texteditormodal}.panel.innerpanel.preview",
                    Name = $"{texteditormodal}.panel.innerpanel.preview.text",
                    Components =
                    {
                        new CuiTextComponent() { Color = "1 1 1 1", Text = text.Replace("\t", "".PadLeft(4)), FontSize = 14, Align = TextAnchor.UpperLeft, Font = "robotocondensed-bold.ttf" },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMax = "0 0" }
                    }
                }
            );

            cont.Add(
                new CuiElement
                {
                    Parent = $"{texteditormodal}.panel.innerpanel",
                    Name = $"{texteditormodal}.panel.innerpanel.subtitle2",
                    Components =
                    {
                        new CuiTextComponent() { Color = HEXToRGBA("#DCDCDC"), Text = lang.GetMessage("UI_Edit_Reply_TextEditor", this, player.UserIDString).ToUpper(), FontSize = 14, Align = TextAnchor.UpperLeft, Font = "robotocondensed-bold.ttf" },
                        new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "1 0.5", OffsetMin = "20 -30", OffsetMax = "-20 -10" }
                    }
                }
            );

            cont.Add(
                new CuiPanel
                {
                    CursorEnabled = true,
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.5", OffsetMin = "20 20", OffsetMax = "-20 -30" },
                    Image = { Color = HEXToRGBA("#1F2222", 95) }
                },
                $"{texteditormodal}.panel.innerpanel", $"{texteditormodal}.panel.innerpanel.tap"
            );

            var textLines = text.Split('\n');

            int lineHeight = 25;
            int numOfLines = 20;
            int linesGap = 1;

            int totalContentHeight = lineHeight * numOfLines + linesGap * (numOfLines - 1);

            //Scrollviewer
            cont.Add(
                new CuiElement
                {
                    Parent = $"{texteditormodal}.panel.innerpanel.tap",
                    Name = $"{texteditormodal}.panel.innerpanel.tap.scrollviewer",
                    Components =
                    {
                            new CuiImageComponent() { Color = "1 0 1 0" },
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" },
                            new CuiNeedsCursorComponent(),
                            new CuiScrollViewComponent {
                                Vertical = true, Horizontal = true, MovementType = ScrollRect.MovementType.Elastic, Elasticity = 0.3f, Inertia = true, DecelerationRate = 0.5f, ScrollSensitivity = 10f,
                                ContentTransform = new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"0 -{totalContentHeight}", OffsetMax = $"2000 0" },
                                VerticalScrollbar = new CuiScrollbar { Size = 8f, TrackColor = HEXToRGBA("#121414", 90), HandleColor = HEXToRGBA("#262929", 85), HighlightColor = HEXToRGBA("#262929", 95) },
                                HorizontalScrollbar = new CuiScrollbar { Invert = true, Size = 8f, TrackColor = HEXToRGBA("#121414", 90), HandleColor = HEXToRGBA("#262929", 85), HighlightColor = HEXToRGBA("#262929", 95) }
                            }
                    }
                }
            );

            int yStart = 2;

            for (int i = 0; i < numOfLines; i++)
            {
                cont.Add(
                    new CuiPanel { RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"25 -{yStart + lineHeight}", OffsetMax = $"2000 -{yStart}" }, Image = { Color = HEXToRGBA("#171818", 70) } },
                    $"{texteditormodal}.panel.innerpanel.tap.scrollviewer",
                    $"{texteditormodal}.panel.innerpanel.tap.scrollviewer.line"
                );

                //Line number
                cont.Add(
                    new CuiElement
                    {
                        Parent = $"{texteditormodal}.panel.innerpanel.tap.scrollviewer",
                        Components =
                        {
                            new CuiTextComponent() { Color = HEXToRGBA("#393C3C", 90), Text = (i+1).ToString(), FontSize = 12, Align = TextAnchor.MiddleRight, Font = "robotocondensed-bold.ttf" },
                            new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"0 -{yStart + lineHeight}", OffsetMax = $"20 -{yStart}" }
                        }
                    }
                );

                yStart += lineHeight + linesGap;

                string linetext = textLines.Length > i ? textLines[i] : "";

                //Line text
                cont.Add(new CuiElement
                {
                    Parent = $"{texteditormodal}.panel.innerpanel.tap.scrollviewer.line",
                    Components =
                    {
                        new CuiInputFieldComponent { Align = TextAnchor.MiddleLeft, CharsLimit = 600, Command = $"{PREFIX_SHORT}.ui.texteditor Action ActionReply {triggerDefinition.Key} {actionIndex} {listItemIndex} {i.ToString()}", FontSize = 14, Text = linetext, NeedsKeyboard = true, LineType = InputField.LineType.SingleLine },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "5 0", OffsetMax = "0 0" }
                    }
                });
            }

            CuiHelper.AddUi(player, cont);
        }

        private void UI_ShowChatPreviewMode(BasePlayer player, int interval = -1)
        {
            CuiElementContainer cont = new();

            //Parent panel
            cont.Add(
                new CuiPanel
                {
                    CursorEnabled = true,
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" },
                    Image = { Color = "0 0 0 0" }
                },
                Layer,
                chatpreview,
                chatpreview
            );

            //Bg panel 1
            cont.Add(
                new CuiPanel
                {
                    CursorEnabled = true,
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "400 0", OffsetMax = "0 0" },
                    Image = { Color = HEXToRGBA("#000000", 90) }
                },
                chatpreview
            );

            //Bg panel 2
            cont.Add(
                new CuiPanel
                {
                    CursorEnabled = true,
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "0 -100", OffsetMax = "400 0" },
                    Image = { Color = HEXToRGBA("#000000", 90) }
                },
                chatpreview
            );

            //Bg panel 3
            cont.Add(
                new CuiPanel
                {
                    CursorEnabled = true,
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "0 0", OffsetMax = "400 100" },
                    Image = { Color = HEXToRGBA("#000000", 90) }
                },
                chatpreview
            );

            //Title
            cont.Add(
                new CuiElement
                {
                    Parent = chatpreview,
                    Name = $"{chatpreview}.title",
                    Components =
                    {
                        new CuiTextComponent() { Color = HEXToRGBA("#DCDCDC"), Text = lang.GetMessage("UI_Edit_ChatPreviewMode_Title", this, player.UserIDString).ToUpper(), FontSize = 24, Align = TextAnchor.MiddleCenter },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-200 25", OffsetMax = "200 100" }
                    }
                }
            );

            int exitWidth = 140;

            //Exit button
            cont.Add(
                new CuiButton
                {
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"-{exitWidth / 2} -{35}", OffsetMax = $"{exitWidth / 2} 0" },
                    Text = { Text = lang.GetMessage("UI_Edit_ChatPreviewMode_Exit", this, player.UserIDString).ToUpper(), FontSize = 16, Align = TextAnchor.MiddleCenter, Color = HEXToRGBA("#90CAF3") },
                    Button = { Color = HEXToRGBA("#376E92", 95), Close = chatpreview, Command = $"{PREFIX_SHORT}.ui.cmd ExitChatPreview" }
                },
                $"{chatpreview}"
            );

            //Hide modalui
            cont.Add(new CuiElement { Name = uimodal, Components = { new CuiRectTransformComponent() { OffsetMin = "-5000 0", OffsetMax = "-5000 0" } }, Update = true });

            CuiHelper.AddUi(player, cont);

            if (interval >= 0)
            {
                timer.Once(interval, () =>
                {
                    cont.Add(new CuiElement { Name = uimodal, Components = { new CuiRectTransformComponent() { OffsetMin = "0 0", OffsetMax = "0 0" } }, Update = true });
                    CuiHelper.AddUi(player, cont);
                });
            }
        }
        
        private void UI_ShowGameTipPreviewMode(BasePlayer player, int interval = -1)
        {
            CuiElementContainer cont = new();

            //Parent panel
            cont.Add(
                new CuiPanel
                {
                    CursorEnabled = true,
                    RectTransform = { AnchorMin = "0 0.4", AnchorMax = "1 1" },
                    Image = { Color = HEXToRGBA("#000000", 90) }
                },
                Layer,
                gametippreview,
                gametippreview
            );

            //Title
            cont.Add(
                new CuiElement
                {
                    Parent = gametippreview,
                    Name = $"{gametippreview}.title",
                    Components =
                    {
                        new CuiTextComponent() { Color = HEXToRGBA("#DCDCDC"), Text = lang.GetMessage("UI_Edit_GameTipPreviewMode_Title", this, player.UserIDString).ToUpper(), FontSize = 24, Align = TextAnchor.MiddleCenter },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-200 25", OffsetMax = "200 100" }
                    }
                }
            );

            int exitWidth = 140;

            //Exit button
            cont.Add(
                new CuiButton
                {
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"-{exitWidth / 2} -{35}", OffsetMax = $"{exitWidth / 2} 0" },
                    Text = { Text = lang.GetMessage("UI_Edit_GameTipPreviewMode_Exit", this, player.UserIDString).ToUpper(), FontSize = 16, Align = TextAnchor.MiddleCenter, Color = HEXToRGBA("#90CAF3") },
                    Button = { Color = HEXToRGBA("#376E92", 95), Close = gametippreview, Command = $"{PREFIX_SHORT}.ui.cmd ExitGameTipPreview" }
                },
                $"{gametippreview}"
            );

            //Hide modalui
            cont.Add(new CuiElement { Name = uimodal, Components = { new CuiRectTransformComponent() { OffsetMin = "-5000 0", OffsetMax = "-5000 0" } }, Update = true });

            CuiHelper.AddUi(player, cont);

            if (interval >= 0)
            {
                timer.Once(interval, () =>
                {
                    cont.Add(new CuiElement { Name = uimodal, Components = { new CuiRectTransformComponent() { OffsetMin = "0 0", OffsetMax = "0 0" } }, Update = true });
                    CuiHelper.AddUi(player, cont);
                });
            }
        }

        #region Commands

        [ChatCommand($"{PREFIX_SHORT}.edit")]
        void OpenAMEditor(BasePlayer player, string command, string[] args)
        {
            if (player == null || !permission.UserHasPermission(player.UserIDString, PERM_ADMIN))
            {
                SendReply(player, "You do not have permission to use this command");
                return;
            }

            UI_ShowEditor(player);
        }

        [ConsoleCommand($"{PREFIX_SHORT}.edit")]
        void OpenAMEditorConsole(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !permission.UserHasPermission(player.UserIDString, PERM_ADMIN))
            {
                SendReply(player, "You do not have permission to use this command");
                return;
            }

            UI_ShowEditor(player);
        }

        [ConsoleCommand($"{PREFIX_SHORT}.edit.savecancel")]
        void AMEditorSaveCancelConsole(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !permission.UserHasPermission(player.UserIDString, PERM_ADMIN))
            {
                SendReply(player, "You do not have permission to use this command");
                return;
            }

            if (!arg.HasArgs())
                return;

            bool shouldSave = false;
            bool clearSession = false;

            switch (arg.Args[0])
            {
                case "save":
                    shouldSave = true;
                    break;
                case "saveexit":
                    shouldSave = true;
                    clearSession = true;
                    CuiHelper.DestroyUi(player, uimodal);
                    break;
                case "cancel":
                    clearSession = true;
                    CuiHelper.DestroyUi(player, uimodal);
                    break;
            }

            if (shouldSave)
            {
                if (UISessions.TryGetValue(player.userID, out var uiSession))
                {
                    UpdateConfigFromUISession(uiSession);
                }
            }

            if (clearSession)
                UISessions.Remove(player.userID);
        }

        [ConsoleCommand($"{PREFIX_SHORT}.ui.cmd")]
        void AMEditorSelectViewConsole(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !permission.UserHasPermission(player.UserIDString, PERM_ADMIN))
            {
                SendReply(player, "You do not have permission to use this command");
                return;
            }

            //PrintWarning(string.Join(", ", arg.Args));

            if (!arg.HasArgs())
                return;

            UISession uiSession = UISessions[player.userID];
            UITrigger uiTrigger;
            int actionIndex = -1;
            Config_Action uiAction;
            int listItemIndex = -1;
            CuiElementContainer cont = new();
            Lang_Action langAction;

            switch (arg.Args[0])
            {
                case "GoToSettings":
                    CuiHelper.DestroyUi(player, $"{uimodal}.triggersview");
                    UI_ShowSettingsView(player);
                    break;
                case "GoToTriggers":
                    CuiHelper.DestroyUi(player, $"{uimodal}.settingsview");
                    CuiHelper.DestroyUi(player, $"{uimodal}.actionsview");
                    CuiHelper.DestroyUi(player, $"{uimodal}.langsview"); 
                    UI_ShowTriggersView(player);
                    break;
                case "GoToActions":
                    if (!arg.HasArgs(2))
                        return;

                    CuiHelper.DestroyUi(player, $"{uimodal}.triggersview");
                    UI_ShowActionsView(player, arg.Args[1]);
                    break;
                case "SettingsCB":
                    if (!arg.HasArgs(2))
                        return;

                    switch (arg.Args[1])
                    {
                        case "BroadcastToTeamOnly":
                            uiSession.Settings.BroadcastToTeamOnly = !uiSession.Settings.BroadcastToTeamOnly;

                            UI_CheckBox_Update(uiSession.Settings.BroadcastToTeamOnly, nameof(UISession.Settings.BroadcastToTeamOnly), cont);
                            CuiHelper.AddUi(player, cont);
                            break;
                    }
                    break;
                case "GoToActionForm":
                    if (!arg.HasArgs(3))
                        return;

                    uiTrigger = GetUITrigger(player.userID, arg.Args[1]);
                    if (uiTrigger == null)
                        return;

                    int selected = int.Parse(arg.Args[2]);

                    for (int i = 0; i < uiTrigger.Config_Actions.Count; i++)
                        if (i != selected)
                            cont.Add(new CuiElement { Name = $"{uimodal}.actionsview.list.actionlist.{i}", Components = { new CuiImageComponent() { Color = HEXToRGBA("#393C3C", 95) } }, Update = true });

                    cont.Add(new CuiElement { Name = $"{uimodal}.actionsview.list.actionlist.{selected}", Components = { new CuiImageComponent() { Color = HEXToRGBA("#5A6060", 95) } }, Update = true });

                    CuiHelper.AddUi(player, cont);

                    UI_ShowSelectedAction(player, uiTrigger, selected, uiSession.SelectedRepliesLang);
                    break;
                case "AddNewAction":
                    if (!arg.HasArgs(2))
                        return;

                    uiTrigger = GetUITrigger(player.userID, arg.Args[1]);
                    if (uiTrigger == null)
                        return;

                    uiAction = new();
                    uiAction.Type = uiTrigger.TriggerDefinition.Key;
                    uiAction.Groups.Add("default");

                    foreach (string language in _config.RepliesLangSettings.ServerLangs)
                        uiTrigger.LangsTriggers[language].Actions.Add(uiAction.Id, new());

                    uiTrigger.Config_Actions.Add(uiAction);
                    UI_ShowActionsList(player, uiTrigger, uiSession.SelectedRepliesLang, uiTrigger.Config_Actions.Count - 1);
                    break;
                case "DuplicateAction":
                    if (!arg.HasArgs(3))
                        return;

                    uiTrigger = GetUITrigger(player.userID, arg.Args[1]);
                    if (uiTrigger == null)
                        return;

                    actionIndex = int.Parse(arg.Args[2]);

                    uiAction = GetUIAction(uiTrigger, actionIndex);
                    if (uiAction == null)
                        return;

                    Config_Action clonedAction = uiAction.Clone(true);

                    foreach (string language in _config.RepliesLangSettings.ServerLangs)
                        uiTrigger.LangsTriggers[language].Actions.Add(clonedAction.Id, uiTrigger.LangsTriggers[language].Actions[uiAction.Id].Clone());

                    uiTrigger.Config_Actions.Add(clonedAction);
                    UI_ShowActionsList(player, uiTrigger, uiSession.SelectedRepliesLang, uiTrigger.Config_Actions.Count - 1);
                    break;
                case "DeleteAction":
                    if (!arg.HasArgs(3))
                        return;

                    uiTrigger = GetUITrigger(player.userID, arg.Args[1]);
                    if (uiTrigger == null)
                        return;

                    actionIndex = int.Parse(arg.Args[2]);

                    uiAction = GetUIAction(uiTrigger, actionIndex);
                    if (uiAction == null)
                        return;

                    foreach (string language in _config.RepliesLangSettings.ServerLangs)
                        uiTrigger.LangsTriggers[language].Actions.Remove(uiAction.Id);

                    uiTrigger.Config_Actions.Remove(uiAction);

                    UI_ShowActionsList(player, uiTrigger, uiSession.SelectedRepliesLang);
                    break;
                case "ActionCB":
                    if (!arg.HasArgs(4))
                        return;

                    uiTrigger = GetUITrigger(player.userID, arg.Args[1]);
                    if (uiTrigger == null)
                        return;

                    actionIndex = int.Parse(arg.Args[2]);

                    uiAction = GetUIAction(uiTrigger, actionIndex);
                    if (uiAction == null)
                        return;

                    switch (arg.Args[3])
                    {
                        case "Enabled":
                            uiAction.Enabled = !uiAction.Enabled;

                            UI_CheckBox_Update(uiAction.Enabled, nameof(Config_Action.Enabled), cont, lang.GetMessage("UI_Edit_Action_Active", this, player.UserIDString), lang.GetMessage("UI_Edit_Action_Inactive", this, player.UserIDString));
                            CuiHelper.AddUi(player, cont);
                            break;
                        case "SendInChat":
                            uiAction.SendInChat = !uiAction.SendInChat;

                            UI_CheckBox_Update(uiAction.SendInChat, nameof(Config_Action.SendInChat), cont);
                            CuiHelper.AddUi(player, cont);
                            break;
                        case "SendAsGameTip":
                            uiAction.SendAsGameTip = !uiAction.SendAsGameTip;

                            UI_CheckBox_Update(uiAction.SendAsGameTip, nameof(Config_Action.SendAsGameTip), cont);
                            CuiHelper.AddUi(player, cont);
                            break;
                        case "IsBlueGameTip":
                            uiAction.IsBlueGameTip = !uiAction.IsBlueGameTip;

                            UI_CheckBox_Update(uiAction.IsBlueGameTip, nameof(Config_Action.IsBlueGameTip), cont, lang.GetMessage("UI_Edit_Action_IsBlueGameTip", this, player.UserIDString), lang.GetMessage("UI_Edit_Action_IsRedGameTip", this, player.UserIDString), enabledHex: "#376E92");
                            CuiHelper.AddUi(player, cont);
                            break;
                        case "IsGlobalBroadcast":
                            uiAction.IsGlobalBroadcast = !uiAction.IsGlobalBroadcast;

                            UI_CheckBox_Update(uiAction.IsGlobalBroadcast, nameof(Config_Action.IsGlobalBroadcast), cont);
                            CuiHelper.AddUi(player, cont);
                            break;
                        case "PlayerCanDisable":
                            uiAction.PlayerCanDisable = !uiAction.PlayerCanDisable;

                            UI_CheckBox_Update(uiAction.PlayerCanDisable, nameof(Config_Action.PlayerCanDisable), cont);
                            CuiHelper.AddUi(player, cont);
                            break;
                        case "DontTriggerAdmin":
                            uiAction.DontTriggerAdmin = !uiAction.DontTriggerAdmin;

                            UI_CheckBox_Update(uiAction.DontTriggerAdmin, nameof(Config_Action.DontTriggerAdmin), cont);
                            CuiHelper.AddUi(player, cont);
                            break;
                        case "UseTriggerOwnerIcon":
                            uiAction.UseTriggerOwnerIcon = !uiAction.UseTriggerOwnerIcon;

                            UI_CheckBox_Update(uiAction.UseTriggerOwnerIcon, nameof(Config_Action.UseTriggerOwnerIcon), cont);
                            CuiHelper.AddUi(player, cont);
                            break;
                    }
                    break;
                case "ActionDeleteListItem":
                    if (!arg.HasArgs(5))
                        return;

                    uiTrigger = GetUITrigger(player.userID, arg.Args[1]);
                    if (uiTrigger == null)
                        return;

                    actionIndex = int.Parse(arg.Args[2]);

                    uiAction = GetUIAction(uiTrigger, actionIndex);
                    if (uiAction == null)
                        return;

                    langAction = uiTrigger.LangsTriggers[uiSession.SelectedRepliesLang].Actions[uiAction.Id];

                    switch (arg.Args[3])
                    {
                        case "Replies":
                            listItemIndex = int.Parse(arg.Args[4]);
                            uiTrigger.LangsTriggers[uiSession.SelectedRepliesLang].Actions[uiAction.Id].Replies.RemoveAt(listItemIndex);

                            UI_Listbox_Update(player, "Action", nameof(Lang_Action.Replies), $"{PREFIX_SHORT}.ui.cmd %cmdname% {uiTrigger.TriggerDefinition.Key} {actionIndex} %index%", langAction.Replies, cont, minItemsInView: 3, withSorting: true, previewButton: true);
                            CuiHelper.AddUi(player, cont);
                            break;
                        case "Permissions":
                            listItemIndex = int.Parse(arg.Args[4]);
                            uiAction.Permissions.RemoveAt(listItemIndex);

                            UI_Listbox_Update(player, "Action", nameof(Config_Action.Permissions), $"{PREFIX_SHORT}.ui.cmd %cmdname% {uiTrigger.TriggerDefinition.Key} {actionIndex} %index%", uiAction.Permissions, cont);
                            CuiHelper.AddUi(player, cont);
                            break;
                        case "Groups":
                            listItemIndex = int.Parse(arg.Args[4]);
                            uiAction.Groups.RemoveAt(listItemIndex);

                            UI_Listbox_Update(player, "Action", nameof(Config_Action.Groups), $"{PREFIX_SHORT}.ui.cmd %cmdname% {uiTrigger.TriggerDefinition.Key} {actionIndex} %index%", uiAction.Groups, cont);
                            CuiHelper.AddUi(player, cont);
                            break;
                        case "BlacklistedPerms":
                            listItemIndex = int.Parse(arg.Args[4]);
                            uiAction.BlacklistedPerms.RemoveAt(listItemIndex);

                            UI_Listbox_Update(player, "Action", nameof(Config_Action.BlacklistedPerms), $"{PREFIX_SHORT}.ui.cmd %cmdname% {uiTrigger.TriggerDefinition.Key} {actionIndex} %index%", uiAction.BlacklistedPerms, cont);
                            CuiHelper.AddUi(player, cont);
                            break;
                        case "BlacklistedGroups":
                            listItemIndex = int.Parse(arg.Args[4]);
                            uiAction.BlacklistedGroups.RemoveAt(listItemIndex);

                            UI_Listbox_Update(player, "Action", nameof(Config_Action.BlacklistedGroups), $"{PREFIX_SHORT}.ui.cmd %cmdname% {uiTrigger.TriggerDefinition.Key} {actionIndex} %index%", uiAction.BlacklistedGroups, cont);
                            CuiHelper.AddUi(player, cont);
                            break;
                    }
                    break;
                case "ActionMoveUpListItem":
                    if (!arg.HasArgs(5))
                        return;

                    uiTrigger = GetUITrigger(player.userID, arg.Args[1]);
                    if (uiTrigger == null)
                        return;

                    actionIndex = int.Parse(arg.Args[2]);

                    uiAction = GetUIAction(uiTrigger, actionIndex);
                    if (uiAction == null)
                        return;

                    langAction = uiTrigger.LangsTriggers[uiSession.SelectedRepliesLang].Actions[uiAction.Id];

                    switch (arg.Args[3])
                    {
                        case "Replies":
                            listItemIndex = int.Parse(arg.Args[4]);
                            
                            var replies = uiTrigger.LangsTriggers[uiSession.SelectedRepliesLang].Actions[uiAction.Id].Replies;
                            if (listItemIndex > 0)
                            {
                                (replies[listItemIndex], replies[listItemIndex - 1]) = (replies[listItemIndex - 1], replies[listItemIndex]);
                                
                                UI_Listbox_Update(player, "Action", nameof(Lang_Action.Replies), $"{PREFIX_SHORT}.ui.cmd %cmdname% {uiTrigger.TriggerDefinition.Key} {actionIndex} %index%", langAction.Replies, cont, minItemsInView: 3, withSorting: true, previewButton: true);
                                CuiHelper.AddUi(player, cont);
                            }
                            break;
                    }
                    break;
                case "ActionMoveDownListItem":
                    if (!arg.HasArgs(5))
                        return;

                    uiTrigger = GetUITrigger(player.userID, arg.Args[1]);
                    if (uiTrigger == null)
                        return;

                    actionIndex = int.Parse(arg.Args[2]);

                    uiAction = GetUIAction(uiTrigger, actionIndex);
                    if (uiAction == null)
                        return;

                    langAction = uiTrigger.LangsTriggers[uiSession.SelectedRepliesLang].Actions[uiAction.Id];

                    switch (arg.Args[3])
                    {
                        case "Replies":
                            listItemIndex = int.Parse(arg.Args[4]);
                            
                            var replies = uiTrigger.LangsTriggers[uiSession.SelectedRepliesLang].Actions[uiAction.Id].Replies;
                            if (listItemIndex < replies.Count - 1)
                            {
                                (replies[listItemIndex], replies[listItemIndex + 1]) = (replies[listItemIndex + 1], replies[listItemIndex]);
                                
                                UI_Listbox_Update(player, "Action", nameof(Lang_Action.Replies), $"{PREFIX_SHORT}.ui.cmd %cmdname% {uiTrigger.TriggerDefinition.Key} {actionIndex} %index%", langAction.Replies, cont, minItemsInView: 3, withSorting: true, previewButton: true);
                                CuiHelper.AddUi(player, cont);
                            }
                            break;
                    }
                    break;
                case "ActionAddListItem":
                    if (!arg.HasArgs(4))
                        return;

                    uiTrigger = GetUITrigger(player.userID, arg.Args[1]);
                    if (uiTrigger == null)
                        return;

                    actionIndex = int.Parse(arg.Args[2]);

                    uiAction = GetUIAction(uiTrigger, actionIndex);
                    if (uiAction == null)
                        return;

                    langAction = uiTrigger.LangsTriggers[uiSession.SelectedRepliesLang].Actions[uiAction.Id];

                    switch (arg.Args[3])
                    {
                        case "Replies":
                            langAction.Replies.Add("<color=yellow>Tips</color> for writing a reply message:\nThis text is in a new line.\n\tThis text is indented.");

                            UI_Listbox_Update(player, "Action", nameof(Lang_Action.Replies), $"{PREFIX_SHORT}.ui.cmd %cmdname% {uiTrigger.TriggerDefinition.Key} {actionIndex} %index%", langAction.Replies, cont, scrollToBottom: true, minItemsInView: 3, withSorting: true, previewButton: true);
                            CuiHelper.AddUi(player, cont);
                            listItemIndex = langAction.Replies.Count - 1;
                            UI_ShowMultilineTextEditor(player, langAction.Replies.Last(), "Reply Editor", uiTrigger.TriggerDefinition, actionIndex, listItemIndex);
                            break;
                        case "Permissions":
                            uiAction.Permissions.Add("pluginname.permission");

                            UI_Listbox_Update(player, "Action", nameof(Config_Action.Permissions), $"{PREFIX_SHORT}.ui.cmd %cmdname% {uiTrigger.TriggerDefinition.Key} {actionIndex} %index%", uiAction.Permissions, cont, scrollToBottom: true);
                            CuiHelper.AddUi(player, cont);
                            listItemIndex = uiAction.Permissions.Count - 1;
                            UI_ShowTextEditor(player, "Action", uiAction.Permissions.Last(), "Permission Name", $"{uiTrigger.TriggerDefinition.Key} {actionIndex}", nameof(Config_Action.Permissions), listItemIndex);
                            break;
                        case "Groups":
                            uiAction.Groups.Add("");

                            UI_Listbox_Update(player, "Action", nameof(Config_Action.Groups), $"{PREFIX_SHORT}.ui.cmd %cmdname% {uiTrigger.TriggerDefinition.Key} {actionIndex} %index%", uiAction.Groups, cont, scrollToBottom: true);
                            CuiHelper.AddUi(player, cont);
                            listItemIndex = uiAction.Groups.Count - 1;
                            UI_ShowTextEditor(player, "Action", uiAction.Groups.Last(), "Group Name", $"{uiTrigger.TriggerDefinition.Key} {actionIndex}", nameof(Config_Action.Groups), listItemIndex);
                            break;
                        case "BlacklistedPerms":
                            uiAction.BlacklistedPerms.Add("pluginname.permission");

                            UI_Listbox_Update(player, "Action", nameof(Config_Action.BlacklistedPerms), $"{PREFIX_SHORT}.ui.cmd %cmdname% {uiTrigger.TriggerDefinition.Key} {actionIndex} %index%", uiAction.BlacklistedPerms, cont, scrollToBottom: true);
                            CuiHelper.AddUi(player, cont);
                            listItemIndex = uiAction.BlacklistedPerms.Count - 1;
                            UI_ShowTextEditor(player, "Action", uiAction.BlacklistedPerms.Last(), "Permission Name", $"{uiTrigger.TriggerDefinition.Key} {actionIndex}", nameof(Config_Action.BlacklistedPerms), listItemIndex);
                            break;
                        case "BlacklistedGroups":
                            uiAction.BlacklistedGroups.Add("");

                            UI_Listbox_Update(player, "Action", nameof(Config_Action.BlacklistedGroups), $"{PREFIX_SHORT}.ui.cmd %cmdname% {uiTrigger.TriggerDefinition.Key} {actionIndex} %index%", uiAction.BlacklistedGroups, cont, scrollToBottom: true);
                            CuiHelper.AddUi(player, cont);
                            listItemIndex = uiAction.BlacklistedGroups.Count - 1;
                            UI_ShowTextEditor(player, "Action", uiAction.BlacklistedGroups.Last(), "Group Name", $"{uiTrigger.TriggerDefinition.Key} {actionIndex}", nameof(Config_Action.BlacklistedGroups), listItemIndex);
                            break;
                    }
                    break;
                case "ActionSelectListItem":
                    if (!arg.HasArgs(5))
                        return;

                    uiTrigger = GetUITrigger(player.userID, arg.Args[1]);
                    if (uiTrigger == null)
                        return;

                    actionIndex = int.Parse(arg.Args[2]);

                    uiAction = GetUIAction(uiTrigger, actionIndex);
                    if (uiAction == null)
                        return;

                    langAction = uiTrigger.LangsTriggers[uiSession.SelectedRepliesLang].Actions[uiAction.Id];

                    switch (arg.Args[3])
                    {
                        case "Replies":
                            listItemIndex = int.Parse(arg.Args[4]);
                            UI_ShowMultilineTextEditor(player, langAction.Replies[listItemIndex], "Reply Editor", uiTrigger.TriggerDefinition, actionIndex, listItemIndex);
                            break;
                        case "Permissions":
                            listItemIndex = int.Parse(arg.Args[4]);
                            UI_ShowTextEditor(player, "Action", uiAction.Permissions[listItemIndex], "Permission Name", $"{uiTrigger.TriggerDefinition.Key} {actionIndex}", nameof(Config_Action.Permissions), listItemIndex);
                            break;
                        case "Groups":
                            listItemIndex = int.Parse(arg.Args[4]);
                            UI_ShowTextEditor(player, "Action", uiAction.Groups[listItemIndex], "Group Name", $"{uiTrigger.TriggerDefinition.Key} {actionIndex}", nameof(Config_Action.Groups), listItemIndex);
                            break;
                        case "BlacklistedPerms":
                            listItemIndex = int.Parse(arg.Args[4]);
                            UI_ShowTextEditor(player, "Action", uiAction.BlacklistedPerms[listItemIndex], "Permission Name", $"{uiTrigger.TriggerDefinition.Key} {actionIndex}", nameof(Config_Action.BlacklistedPerms), listItemIndex);
                            break;
                        case "BlacklistedGroups":
                            listItemIndex = int.Parse(arg.Args[4]);
                            UI_ShowTextEditor(player, "Action", uiAction.BlacklistedGroups[listItemIndex], "Group Name", $"{uiTrigger.TriggerDefinition.Key} {actionIndex}", nameof(Config_Action.BlacklistedGroups), listItemIndex);
                            break;
                    }
                    break;
                case "ActionRefreshList":
                    if (!arg.HasArgs(5))
                        return;

                    uiTrigger = GetUITrigger(player.userID, arg.Args[1]);
                    if (uiTrigger == null)
                        return;

                    actionIndex = int.Parse(arg.Args[2]);

                    uiAction = GetUIAction(uiTrigger, actionIndex);
                    if (uiAction == null)
                        return;

                    langAction = uiTrigger.LangsTriggers[uiSession.SelectedRepliesLang].Actions[uiAction.Id];

                    switch (arg.Args[3])
                    {
                        case "Replies":
                            UI_Listbox_Update(player, "Action", nameof(Lang_Action.Replies), $"{PREFIX_SHORT}.ui.cmd %cmdname% {uiTrigger.TriggerDefinition.Key} {actionIndex} %index%", langAction.Replies, cont, scrollToBottom: true, minItemsInView: 3, withSorting: true, previewButton: true);
                            CuiHelper.AddUi(player, cont);
                            break;
                        case "Permissions":
                            UI_Listbox_Update(player, "Action", nameof(Config_Action.Permissions), $"{PREFIX_SHORT}.ui.cmd %cmdname% {uiTrigger.TriggerDefinition.Key} {actionIndex} %index%", uiAction.Permissions, cont, scrollToBottom: true);
                            CuiHelper.AddUi(player, cont);
                            break;
                        case "Groups":
                            UI_Listbox_Update(player, "Action", nameof(Config_Action.Groups), $"{PREFIX_SHORT}.ui.cmd %cmdname% {uiTrigger.TriggerDefinition.Key} {actionIndex} %index%", uiAction.Groups, cont, scrollToBottom: true);
                            CuiHelper.AddUi(player, cont);
                            break;
                        case "BlacklistedPerms":
                            UI_Listbox_Update(player, "Action", nameof(Config_Action.BlacklistedPerms), $"{PREFIX_SHORT}.ui.cmd %cmdname% {uiTrigger.TriggerDefinition.Key} {actionIndex} %index%", uiAction.BlacklistedPerms, cont, scrollToBottom: true);
                            CuiHelper.AddUi(player, cont);
                            break;
                        case "BlacklistedGroups":
                            UI_Listbox_Update(player, "Action", nameof(Config_Action.BlacklistedGroups), $"{PREFIX_SHORT}.ui.cmd %cmdname% {uiTrigger.TriggerDefinition.Key} {actionIndex} %index%", uiAction.BlacklistedGroups, cont, scrollToBottom: true);
                            CuiHelper.AddUi(player, cont);
                            break;
                    }
                    break;
                case "ActionPreviewReply":
                    if (!arg.HasArgs(5))
                        return;

                    uiTrigger = GetUITrigger(player.userID, arg.Args[1]);
                    if (uiTrigger == null)
                        return;

                    actionIndex = int.Parse(arg.Args[2]);

                    uiAction = GetUIAction(uiTrigger, actionIndex);
                    if (uiAction == null)
                        return;

                    langAction = uiTrigger.LangsTriggers[uiSession.SelectedRepliesLang].Actions[uiAction.Id];

                    switch (arg.Args[3])
                    {
                        case "Replies":
                            listItemIndex = int.Parse(arg.Args[4]);

                            bool isGameTip = false;

                            if (arg.HasArgs(6))
                                isGameTip = arg.Args[5] == "gametip";
                            else if(!uiAction.SendInChat && uiAction.SendAsGameTip)
                                isGameTip = true;

                            if (isGameTip)
                            {
                                UI_ShowGameTipPreviewMode(player);
                                timer.Once(0.5f, () =>
                                {
                                    SendMessage(player, DefineVariables(player, langAction.Replies[listItemIndex], uiTrigger.TriggerDefinition), false, 0, true, uiAction.IsBlueGameTip);
                                });
                            }
                            else
                            {
                                SendMessage(player, DefineVariables(player, langAction.Replies[listItemIndex], uiTrigger.TriggerDefinition), IconSteamId: GetActionIcon(uiAction, player, uiSession.Settings.IconSteamId));
                                UI_ShowChatPreviewMode(player);
                            }
                            
                            break;
                    }
                    break;
                case "ExitChatPreview":
                    CuiHelper.DestroyUi(player, chatpreview);
                    cont.Add(new CuiElement { Name = uimodal, Components = { new CuiRectTransformComponent() { OffsetMin = "0 0", OffsetMax = "0 0" } }, Update = true });
                    CuiHelper.AddUi(player, cont);
                    break;
                case "ExitGameTipPreview":
                    CuiHelper.DestroyUi(player, gametippreview);
                    cont.Add(new CuiElement { Name = uimodal, Components = { new CuiRectTransformComponent() { OffsetMin = "0 0", OffsetMax = "0 0" } }, Update = true });
                    CuiHelper.AddUi(player, cont);
                    break;
                case "ChangeRepliesLang":
                    if (!arg.HasArgs(4))
                        return;

                    string newLang = arg.Args[1];
                    if(uiSession.SelectedRepliesLang == newLang)
                        return;
                    uiSession.SelectedRepliesLang = newLang;

                    uiTrigger = GetUITrigger(player.userID, arg.Args[2]);
                    if (uiTrigger == null)
                        return;

                    actionIndex = int.Parse(arg.Args[3]);

                    uiAction = GetUIAction(uiTrigger, actionIndex);
                    if (uiAction == null)
                        return;

                    langAction = uiTrigger.LangsTriggers[uiSession.SelectedRepliesLang].Actions[uiAction.Id];

                    UI_ShowRepliesLanguages(player, newLang, uiTrigger, actionIndex);
                    UI_Listbox_Update(player, "Action", nameof(Lang_Action.Replies), $"{PREFIX_SHORT}.ui.cmd %cmdname% {uiTrigger.TriggerDefinition.Key} {actionIndex} %index%", langAction.Replies, cont, scrollToBottom: true, minItemsInView: 3, withSorting: true, previewButton: true);
                    CuiHelper.AddUi(player, cont);
                    break;
            }
        }

        [ConsoleCommand($"{PREFIX_SHORT}.ui.textboxcmd")]
        void AMEditorTextboxConsole(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !permission.UserHasPermission(player.UserIDString, PERM_ADMIN))
            {
                SendReply(player, "You do not have permission to use this command");
                return;
            }

            if (!arg.HasArgs(3))
                return;

            switch (arg.Args[0])
            {
                case "Action":
                    if (!arg.HasArgs(5))
                        return;

                    string text = string.Join(" ", arg.Args.Skip(5));

                    UITrigger uiTrigger = GetUITrigger(player.userID, arg.Args[1]);
                    if (uiTrigger == null)
                        return;

                    int actionIndex = int.Parse(arg.Args[2]);

                    Config_Action uiAction = GetUIAction(uiTrigger, actionIndex);
                    if (uiAction == null)
                        return;

                    int listItemIndex = -1;

                    switch (arg.Args[3])
                    {
                        case "Permissions":
                            listItemIndex = int.Parse(arg.Args[4]);
                            uiAction.Permissions[listItemIndex] = text;
                            break;
                        case "Groups":
                            listItemIndex = int.Parse(arg.Args[4]);
                            uiAction.Groups[listItemIndex] = text;
                            break;
                        case "BlacklistedPerms":
                            listItemIndex = int.Parse(arg.Args[4]);
                            uiAction.BlacklistedPerms[listItemIndex] = text;
                            break;
                        case "BlacklistedGroups":
                            listItemIndex = int.Parse(arg.Args[4]);
                            uiAction.BlacklistedGroups[listItemIndex] = text;
                            break;
                    }
                    break;
            }
        }

        private void LongInputCMD(IPlayer user, string command, string[] args)
        {
            var player = user.Object as BasePlayer;
            if (player == null) return;
            
            if (args.Length < 3)
                return;
            
            //PrintWarning($"{string.Join(" ", args)}");
            
            UISession uiSession = UISessions[player.userID];

            switch (args[0])
            {
                case "Action":
                    UITrigger uiTrigger = GetUITrigger(player.userID, args[2]);
                    if (uiTrigger == null)
                        return;

                    int actionIndex = int.Parse(args[3]);
                    Config_Action uiAction = GetUIAction(uiTrigger, actionIndex);
                    if (uiAction == null)
                        return;

                    Lang_Action langAction = uiTrigger.LangsTriggers[uiSession.SelectedRepliesLang].Actions[uiAction.Id];

                    string text = "";

                    switch (args[1])
                    {
                        case "ActionReply":
                            text = string.Join(" ", args.Skip(6));

                            int listItemIndex = int.Parse(args[4]);
                            int lineIndex = int.Parse(args[5]);

                            List<string> lines = langAction.Replies[listItemIndex].Split('\n').ToList();

                            if (lines.Count <= lineIndex)
                            {
                                int diff = (lineIndex + 1) - lines.Count;
                                for (int i = 0; i < diff; i++)
                                    lines.Add("");
                            }
                            lines[lineIndex] = text.Replace("\\t", "\t");

                            string replymodal = $"{uimodal}.replyeditor";
                            string finalText = string.Join("\n", lines).TrimEnd('\n', ' ');

                            langAction.Replies[listItemIndex] = finalText;

                            CuiElementContainer cont = new()
                            {
                                new CuiElement
                                {
                                    Name = $"{replymodal}.panel.innerpanel.preview.text",
                                    Components = { new CuiTextComponent() { Text = finalText.Replace("\t", "".PadLeft(4)) } },
                                    Update = true
                                }
                            };
                            CuiHelper.AddUi(player, cont);
                            break;
                        case "ActionTextbox":
                            text = string.Join(" ", args.Skip(5));
                            switch (args[4])
                            {
                                case "Interval":
                                    int.TryParse(text, out uiAction.Interval);
                                    UI_UpdateSelectedActionInList(player, uiTrigger, actionIndex);
                                    break;
                                case "Target":
                                    uiAction.Target = text;
                                    UI_UpdateSelectedActionInList(player, uiTrigger, actionIndex);
                                    break;
                                case "CustomIconSteamId":
                                    uiAction.CustomIconSteamId = text;
                                    break;
                            }
                            break;
                    }
                    break;
                case "Settings":
                    text = string.Join(" ", args.Skip(3));

                    switch (args[1])
                    {
                        case "SettingsTextbox":
                            switch (args[2])
                            {
                                case "IconSteamId":
                                    uiSession.Settings.IconSteamId = text;
                                    break;
                                case "ToggleCommand":
                                    uiSession.Settings.ToggleCommand = text;
                                    break;
                                case "AutoReplyCooldown":
                                    int.TryParse(text, out uiSession.Settings.AutoReplyCooldown);
                                    break;
                                case "ChatCommandCooldown":
                                    int.TryParse(text, out uiSession.Settings.ChatCommandCooldown);
                                    break;
                                case "ZoneManagerCooldown":
                                    int.TryParse(text, out uiSession.Settings.ZoneManagerCooldown);
                                    break;
                                case "MonumentWatcherCooldown":
                                    int.TryParse(text, out uiSession.Settings.MonumentWatcherCooldown);
                                    break;
                                case "DefaultLang":
                                    if (AvailableLangs.ContainsKey(text.Trim()))
                                    {
                                        uiSession.Settings.DefaultLang = text;
                                        uiSession.SelectedRepliesLang = text;
                                    }
                                    break;
                            }
                            break;
                    }
                    break;
            }
        }

        #endregion

        #endregion
    }
}

#region Extension Methods

namespace Oxide.Plugins.AutomatedMessagesMethods
{
    public static class ExtensionMethods
    {
        //Some copied from AdminRadar
        public static T ElementAt<T>(this IEnumerable<T> a, int b) { using (var c = a.GetEnumerator()) { while (c.MoveNext()) { if (b == 0) { return c.Current; } b--; } } return default(T); }
        public static T FirstOrDefault<T>(this IEnumerable<T> a, Func<T, bool> b = null) { using (var c = a.GetEnumerator()) { while (c.MoveNext()) { if (b == null || b(c.Current)) { return c.Current; } } } return default(T); }
        public static List<T> ToList<T>(this IEnumerable<T> a, Func<T, bool> b = null) { var c = new List<T>(); using (var d = a.GetEnumerator()) { while (d.MoveNext()) { if (b == null || b(d.Current)) { c.Add(d.Current); } } } return c; }
        public static string[] ToLower(this IEnumerable<string> a, Func<string, bool> b = null) { var c = new List<string>(); using (var d = a.GetEnumerator()) { while (d.MoveNext()) { if (b == null || b(d.Current)) { c.Add(d.Current.ToLower()); } } } return c.ToArray(); }
        public static T[] Take<T>(this IList<T> a, int b) { var c = new List<T>(); for (int i = 0; i < a.Count; i++) { if (c.Count == b) { break; } c.Add(a[i]); } return c.ToArray(); }
        public static IEnumerable<V> Select<T, V>(this IEnumerable<T> a, Func<T, V> b) { var c = new List<V>(); using (var d = a.GetEnumerator()) { while (d.MoveNext()) { c.Add(b(d.Current)); } } return c; }
        public static T[] Where<T>(this IEnumerable<T> a, Func<T, bool> b) { var c = new List<T>(); using (var d = a.GetEnumerator()) { while (d.MoveNext()) { if (b(d.Current)) { c.Add(d.Current); } } } return c.ToArray(); }
        public static IEnumerable<T> OfType<T>(this IEnumerable<object> a) { foreach (object b in a) { if (b is T) { yield return (T)b; } } }
        public static float Sum<T>(this IEnumerable<T> a, Func<T, float> b) { float c = 0; if (a == null) return c; foreach (T d in a) { if (d == null) continue; c = checked(c + b(d)); } return c; }
        public static int Sum<T>(this IEnumerable<T> a, Func<T, int> b) { int c = 0; if (a == null) return c; foreach (T d in a) { if (d == null) continue; c = checked(c + b(d)); } return c; }
        public static bool Any<TSource>(this IEnumerable<TSource> a, Func<TSource, bool> b) { if (a == null) return false; using (var c = a.GetEnumerator()) { while (c.MoveNext()) if (b(c.Current)) return true; } return false; }
        public static string[] Skip(this string[] a, int b) { if (a.Length == 0 || b >= a.Length) { return Array.Empty<string>(); } int n = a.Length - b; string[] c = new string[n]; Array.Copy(a, b, c, 0, n); return c; }
        public static TSource Last<TSource>(this IList<TSource> a) => a[a.Count - 1];
    }
}

#endregion 