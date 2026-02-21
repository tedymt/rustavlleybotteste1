using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("BarrelBash", "Ridamees", "1.4.3")]
    [Description("Barrel breaking event where the player with the most barrels destroyed gets a prize.")]
    public class BarrelBash : RustPlugin
    {
		[PluginReference]
		private Plugin Economics;
		[PluginReference]
		private Plugin ServerRewards;
		
        private Dictionary<ulong, int> leaderboard = new Dictionary<ulong, int>();
        private bool eventActive = false;
		private PluginConfig config;
        private const string UI_BARRELBASH = "UI_BarrelBash";
        private Timer leaderboardUpdateTimer;		
        private float eventDuration;	
		private float eventStartTime;
        public enum RewardType
		{
			ItemType = 1,
			Command = 2,
			Economics = 3,
			ServerRewards = 4
		}
		private List<string> gameTips = new List<string>();
        private Timer gameTipTimer;
		private bool enableChatMessages;
		private float gameTipDuration;
		private bool enableGameTips;
		private string eventStartMessage;
		private string eventEndMessage;
		private string noParticipantsMessage;
		private float autoStartInterval;
        private Timer autoStartTimer;
		private const string permissionStartBarrelBash = "barrelbash.start";
		private int minimumPlayerCount;
		private bool playStartEventSound;
		private RewardType[] rewardType = new RewardType[6];
		private string[] rewardItemType = new string[6];
		private int[] rewardAmount = new int[6];
		private bool[] rewardEnabled = new bool[6];
		private string[] rewardCommand = new string[6];
		private string[] rewardCommandDescription = new string[6];
		private string[] rewardEconomicsDescription = new string[6];
		private string[] rewardServerRewardsDescription = new string[6];
		private string[] winnerAnnouncementMessage = new string[6];
		private int[] ServerRewardsAmount = new int[6];
		private double[] EconomicsRewardAmount = new double[6];
		public class Reward
		{
			[JsonProperty("Display name(For message and GameTip)")]
			public string DisplayName = "";
			[JsonProperty("Command(steamId={0}, playerName={1})")]
				public string Command = "";
		}
		private void HideGameTipForAllPlayers()
		{
			foreach (var basePlayer in BasePlayer.activePlayerList)
			{
				basePlayer.SendConsoleCommand("gametip.hidegametip");
			}
		}
		public class PluginConfig
		{
			[JsonProperty("Only Command Start Event")] public bool OnlyCommandStartEvent { get; set; } = false;
			[JsonProperty("Minimum Players To Start Event")] public int MinimumPlayerCount { get; set; } = 1;			
			[JsonProperty("Show Leaderboard")] public bool ShowLeaderboard { get; set; } = true;
			[JsonProperty("Leaderboard Location (top right, top center, top left | left center, right center | bottom left)")] public string UILocation { get; set; } = "top right";
			[JsonProperty("How long will Event last (seconds)")] public float EventDuration { get; set; } = 180f;
			[JsonProperty("How often will the event be launched(Seconds)")] public float AutoStartInterval { get; set; } = 1200f;			
			
			[JsonProperty("Show Event GameTip Notifications")] public bool EnableGameTips { get; set; } = true;
			[JsonProperty("Shown GameTip Duration (seconds)")] public float GameTipDuration { get; set; } = 7f;
			[JsonProperty("Show Event Chat Notifications")] public bool EnableChatMessages { get; set; } = true;
			
			[JsonProperty("Notification of Event Start(Chat and GameTip)")] public string EventStartMessage { get; set; } = "<color=orange>Barrel</color><color=red>Bash</color> event has started! Destroy <color=orange>barrels</color> to gain points!";
			[JsonProperty("Notification When nobody participates in the event")] public string NoParticipantsMessage { get; set; } = "No one participated in <color=orange>Barrel</color><color=red>Bash</color>";		
			[JsonProperty("Notification Sound of Event Start")] public bool PlayStartEventSound { get; set; } = true;
			[JsonProperty("Winner Multiple Announcements Delay(seconds)")] public float timeBetweenRewards { get; set; } = 3f;			
			[JsonProperty("Winner Rewards, make sure to change each value to your liking(1-6)")]
			public Dictionary<int, RewardConfig> RewardConfigs { get; set; } = new Dictionary<int, RewardConfig>
			{
				{ 1, new RewardConfig { RewardEnabled = true } }, 
				{ 2, new RewardConfig() },
				{ 3, new RewardConfig() }, 
				{ 4, new RewardConfig() },
				{ 5, new RewardConfig() },
				{ 6, new RewardConfig() }
			};			
		} 
		public class RewardConfig
		{
			[JsonProperty("Reward Enabled")] public bool RewardEnabled { get; set; } = false;
			[JsonProperty("Reward Type (1 = Item, 2 = Command, 3 = Economics(Plugin REQ), 4 = ServerRewards(Plugin REQ)")] public int RewardType { get; set; } = 1;
			[JsonProperty("Reward Item")] public string RewardItemType { get; set; } = "scrap";
			[JsonProperty("Reward Item Amount")] public int RewardAmount { get; set; } = 420;
			[JsonProperty("Reward Item Custom Name")] public string RewardItemCustomName { get; set; } = "";
			[JsonProperty("Reward Item Skin ID")] public ulong RewardItemSkinID { get; set; } = 0;			
			[JsonProperty("Reward Command")] public string RewardCommand { get; set; } = "oxide.usergroup add {player.id} vip";
			[JsonProperty("Reward Command Display Name (Chat and GameTip)")] public string RewardCommandDescription { get; set; } = "VIP";
			[JsonProperty("Reward Economics Display Name (Chat and GameTip)")] public string RewardEconomicsDescription { get; set; } = "Balance";
			[JsonProperty("Reward ServerRewards Display Name (Chat and GameTip)")] public string RewardServerRewardsDescription { get; set; } = "RP";			
			[JsonProperty("Reward Economics Plugin Amount")] public double EconomicsRewardAmount { get; set; } = 420.0;
			[JsonProperty("Reward ServerRewards Plugin Amount")] public int ServerRewardsAmount { get; set; } = 420;
			[JsonProperty("Notification of Who Won (supports {player_name}, {reward_display_name}, {barrels_destroyed})")] public string WinnerAnnouncementMessage { get; set; } = "<color=yellow>{player_name}</color> has won 1. place in <color=orange>Barrel</color><color=red>Bash</color> event and received a prize of <color=red>{reward_display_name}</color>!";
		}
        protected override void LoadDefaultConfig()
		{
			if (Config.Exists())
			{
				config = Config.ReadObject<PluginConfig>();
			}
			if (config == null)
			{
				config = new PluginConfig();
			}
			if (config.RewardConfigs == null)
			{
				config.RewardConfigs = new Dictionary<int, RewardConfig>();
			}
			for (int i = 1; i <= 6; i++)
			{
				if (!config.RewardConfigs.ContainsKey(i))
				{
					config.RewardConfigs[i] = new RewardConfig();
					
				}
			}
			Config.WriteObject(config, true);
		}
		private void LoadConfigVariables()  
		{
			eventDuration = config.EventDuration;
			minimumPlayerCount = config.MinimumPlayerCount;
			enableGameTips = config.EnableGameTips;
			enableChatMessages = config.EnableChatMessages;
			gameTipDuration = config.GameTipDuration;
			eventStartMessage = config.EventStartMessage;
			autoStartInterval = config.AutoStartInterval;
			noParticipantsMessage = config.NoParticipantsMessage;
			playStartEventSound = config.PlayStartEventSound;
			for (int i = 1; i < 6; i++)
			{
				rewardEnabled[i] = config.RewardConfigs[i].RewardEnabled;
				rewardType[i] = (RewardType)config.RewardConfigs[i].RewardType;
				rewardItemType[i] = config.RewardConfigs[i].RewardItemType;
				rewardAmount[i] = config.RewardConfigs[i].RewardAmount;
				rewardCommand[i] = config.RewardConfigs[i].RewardCommand;
				rewardCommandDescription[i] = config.RewardConfigs[i].RewardCommandDescription;
				rewardEconomicsDescription[i] = config.RewardConfigs[i].RewardEconomicsDescription;
				rewardServerRewardsDescription[i] = config.RewardConfigs[i].RewardServerRewardsDescription;
				winnerAnnouncementMessage[i] = config.RewardConfigs[i].WinnerAnnouncementMessage;
				ServerRewardsAmount[i] = config.RewardConfigs[i].ServerRewardsAmount;
				EconomicsRewardAmount[i] = config.RewardConfigs[i].EconomicsRewardAmount;
			}
		}
		private void Init()
		{
			LoadDefaultConfig(); 
			LoadConfigVariables();
			StartAutoStartTimer();
			permission.RegisterPermission(permissionStartBarrelBash, this);
			CheckRequiredPlugins();
		}
		private void StartAutoStartTimer()
        {
            autoStartTimer?.Destroy();
            autoStartTimer = timer.Every(autoStartInterval, () =>
            {
                if (!eventActive && BasePlayer.activePlayerList.Count >= minimumPlayerCount)
                {
                    StartEvent();
                }
            });
        }
		private bool manuallyStarted = false;
		[ChatCommand("startbarrelbash")]
		private void StartBarrelBashChatCommand(BasePlayer player, string command, string[] args)
		{
			if (!permission.UserHasPermission(player.UserIDString, "barrelbash.start"))
			{
				player.ChatMessage(lang.GetMessage("NoPermission", this, player.UserIDString));
				return;
			}
			if (eventActive)
			{
				player.ChatMessage("The Barrel Bash event is already running.");
				return;
			}
			manuallyStarted = true;
			StartEvent();
			manuallyStarted = false;
		}
		[ConsoleCommand("startbarrelbash")]
		private void StartBarrelBashConsoleCommand(ConsoleSystem.Arg arg)
		{
			var player = arg.Player();
			if (player != null && !permission.UserHasPermission(player.UserIDString, "barrelbash.start"))
			{
				player.ChatMessage(lang.GetMessage("NoPermission", this, player.UserIDString));
				return;
			}
			if (eventActive)
			{
				string message = "The Barrel Bash event is already running.";
				if (player != null)
					player.ChatMessage(message);
				else
					Puts(message);

				return;
			}
			manuallyStarted = true;
			StartEvent();
			manuallyStarted = false;
		}
        private void StartEvent()
		{
			if (config.OnlyCommandStartEvent && !manuallyStarted)
			{
				return;
			}
			leaderboard.Clear();
			LoadConfigVariables();
			Subscribe(nameof(OnEntityDeath));
			eventActive = true;
			Puts($"BarrelBash Event Started");
			eventStartTime = Time.realtimeSinceStartup;
			UpdateLeaderboardUIForAllPlayers();
			leaderboardUpdateTimer = timer.Every(1f, () => UpdateLeaderboardUIForAllPlayers());
			timer.Once(eventDuration, () =>
			{
				EndEvent();
			});
			SendGameTipToAllPlayers(eventStartMessage);
			PlayStartEventSound();
		}
		private void PlayStartEventSound()
		{
			if (!playStartEventSound) return;

			string soundPath = "assets/bundled/prefabs/fx/item_unlock.prefab";
			foreach (var basePlayer in BasePlayer.activePlayerList)
			{
				Effect.server.Run(soundPath, basePlayer, 0, Vector3.zero, Vector3.forward);
			}
		}
		private void EndEvent()
		{
			leaderboardUpdateTimer.Destroy();
			Unsubscribe(nameof(OnEntityDeath));
			HideLeaderboardUIForAllPlayers();
			if (leaderboard.Count == 0)
			{
				SendGameTipToAllPlayers(noParticipantsMessage);
			}  
			else
			{
				var orderedLeaderboard = leaderboard.OrderByDescending(x => x.Value).ToList();
				int rewardCount = config.RewardConfigs.Count;
				for (int i = 0; i < rewardCount && i < orderedLeaderboard.Count; i++)
				{
					var winner = orderedLeaderboard[i];
					BasePlayer winningPlayer = BasePlayer.FindByID(winner.Key);
					if (winningPlayer != null)
					{
						RewardConfig reward;
						if (config.RewardConfigs.TryGetValue(i + 1, out reward) && reward.RewardEnabled)
						{
							string rewardDisplayName;
							string rewardDescription;
							switch (reward.RewardType)
							{
								case 1:  // Item
									GiveItem(winningPlayer, reward.RewardItemType, reward.RewardAmount, reward.RewardItemSkinID);
									ItemDefinition itemDefinition = ItemManager.FindItemDefinition(reward.RewardItemType);
									string itemDisplayName = string.IsNullOrEmpty(reward.RewardItemCustomName) ? itemDefinition.displayName.english : reward.RewardItemCustomName;
									rewardDisplayName = $"{reward.RewardAmount} {itemDisplayName}";
									rewardDescription = itemDisplayName;
									break;
								case 2:  // Command
									ExecuteCommand(reward.RewardCommand.Replace("{player.id}", winningPlayer.userID.ToString()).Replace("{player.name}", winningPlayer.displayName));
									rewardDisplayName = reward.RewardCommandDescription;
									rewardDescription = reward.RewardCommandDescription;
									break;
								case 3:  // Economics
									Economics.Call("Deposit", winningPlayer.userID, reward.EconomicsRewardAmount);
									Puts($"Economics reward of {reward.EconomicsRewardAmount} has been sent to {winningPlayer.displayName} ({winningPlayer.userID})");
									rewardDisplayName = $"{reward.EconomicsRewardAmount} {reward.RewardEconomicsDescription}";
									rewardDescription = reward.RewardEconomicsDescription;
									break;
								case 4:  // ServerRewards
									ServerRewards.Call("AddPoints", winningPlayer.userID, reward.ServerRewardsAmount);
									Puts($"ServerRewards reward of {reward.ServerRewardsAmount} has been sent to {winningPlayer.displayName} ({winningPlayer.userID})");
									rewardDisplayName = $"{reward.ServerRewardsAmount} {reward.RewardServerRewardsDescription}";
									rewardDescription = reward.RewardServerRewardsDescription;
									break;
								default:
									rewardDisplayName = "Unknown";
									rewardDescription = "Unknown";
									break;
							}
							string winnerAnnouncement = reward.WinnerAnnouncementMessage.Replace("{player_name}", winningPlayer.displayName)
																							.Replace("{reward_display_name}", rewardDisplayName)
																							.Replace("{barrels_destroyed}", winner.Value.ToString());
							timer.Once(config.timeBetweenRewards * i, () =>
							{
								SendGameTipToAllPlayers(winnerAnnouncement);
							});
						}
					}
				}
			}
			eventActive = false;
			if (!config.OnlyCommandStartEvent)
			{
				StartAutoStartTimer();
			}
		}
		private void GiveRewards(BasePlayer player, RewardConfig reward)
		{
			if (Economics && reward.EconomicsRewardAmount > 0)
			{
				Economics.Call("Deposit", player.userID, reward.EconomicsRewardAmount);
			}
			if (ServerRewards && reward.ServerRewardsAmount > 0)
			{
				ServerRewards.Call("AddPoints", player.userID, reward.ServerRewardsAmount);
			}
			if (!string.IsNullOrEmpty(reward.RewardItemType) && reward.RewardAmount > 0)
			{
				GiveItem(player, reward.RewardItemType, reward.RewardAmount);
			}
			if (!string.IsNullOrEmpty(reward.RewardCommand))
			{
				ExecuteCommand(reward.RewardCommand);
			}
		}
		private void SendGameTipToAllPlayers(string message)
        {
            if (enableGameTips)
            {
                ShowGameTipToAllPlayers(message, gameTipDuration);
            }
            if (enableChatMessages)
            {
                PrintToChat(message);
            }
        }
		private void ShowGameTipToAllPlayers(string message, float displayDuration)
		{
			foreach (var basePlayer in BasePlayer.activePlayerList)
			{
				basePlayer.SendConsoleCommand("gametip.showgametip", message);
				timer.Once(displayDuration, () => basePlayer.SendConsoleCommand("gametip.hidegametip"));
			}
		}
	    private void StartGameTipTimer()
		{
			timer.Once(gameTipDuration, () =>
			{
				gameTipTimer = timer.Every(15f, () =>
				{
					string gameTip = GetRandomGameTip();
					SendGameTipToAllPlayers(gameTip);
				});
			});
		}
        private string GetRandomGameTip()
        {
            if (gameTips.Count == 0)
            {
                return "No game tips available.";
            }
            int randomIndex = UnityEngine.Random.Range(0, gameTips.Count);
            return gameTips[randomIndex];
        }
    private void OnEntityDeath(BaseEntity entity, HitInfo info)
	{
		if (entity == null || info == null) return;
		string[] barrelShortPrefabNames = { "loot-barrel-1", "loot-barrel-2", "loot_barrel_1", "loot_barrel_2", "oil_barrel" };
		if (!barrelShortPrefabNames.Contains(entity.ShortPrefabName)) return;
		BasePlayer attacker = info.Initiator?.ToPlayer();
		if (attacker == null) return;
		ulong playerID = attacker.userID;
		if (!leaderboard.ContainsKey(playerID))
		{
			leaderboard[playerID] = 0;
		}
		leaderboard[playerID]++;
	}
	private void UpdateLeaderboardUIForAllPlayers()
	{
		if (config.ShowLeaderboard)
		{
			foreach (var player in BasePlayer.activePlayerList)
			{
				ShowLeaderboardUI(player);
			}
		}
	}
    private void UpdateTimerUIForAllPlayers()
	{
		float timeRemaining = eventDuration - (Time.realtimeSinceStartup - eventStartTime);
		string timerText = $"Time remaining: {Mathf.FloorToInt(timeRemaining / 60)}m {Mathf.FloorToInt(timeRemaining % 60)}s";
		foreach (var player in BasePlayer.activePlayerList)
		{
			CuiHelper.DestroyUi(player, "UI_BARRELBASH_TIMER");
			CuiHelper.AddUi(player, new CuiElementContainer
			{
				{
					new CuiLabel
					{
						Text = { Text = timerText, FontSize = 16, Align = TextAnchor.MiddleCenter },
						RectTransform = { AnchorMin = "0.1 0.1", AnchorMax = "0.9 0.3" }
					},
	"UI_BARRELBASH_TIMER"
				}
			});
		}
	}
    private void HideLeaderboardUIForAllPlayers()
    {
        foreach (var player in BasePlayer.activePlayerList)
        {
            HideLeaderboardUI(player);
        }
    }
    private void ShowLeaderboardUI(BasePlayer player)
	{
		var leaderboardEntries = leaderboard.OrderByDescending(x => x.Value).Take(6).ToList();
		int rank = 1;
		string uiContentLeft = "";
		string uiContentRight = "";
		if (leaderboardEntries.Count == 0)
		{
			uiContentLeft = "Destory Barrels to Gain Points!";
		}
		else
		{
			foreach (var entry in leaderboardEntries.Take(3))
			{
				BasePlayer entryPlayer = BasePlayer.FindByID(entry.Key);
				string playerName = entryPlayer != null ? entryPlayer.displayName : "Unknown";
				if (playerName.Length > 9)
				{
					playerName = playerName.Substring(0, 9);
				}
				uiContentLeft += $"<color=gray>{rank}.</color> <color=white>{playerName}</color> <color=red>{entry.Value}</color> <color=orange>barrels</color>\n";
				rank++;
			}
			foreach (var entry in leaderboardEntries.Skip(3).Take(3))
			{
				BasePlayer entryPlayer = BasePlayer.FindByID(entry.Key);
				string playerName = entryPlayer != null ? entryPlayer.displayName : "Unknown";
				if (playerName.Length > 9)
			{
				playerName = playerName.Substring(0, 9);
			}
				uiContentRight += $"<color=gray>{rank}.</color> <color=white>{playerName}</color> <color=red>{entry.Value}</color> <color=orange>barrels</color>\n";
				rank++;
			}
		}
		CuiHelper.DestroyUi(player, UI_BARRELBASH);
		var elements = new CuiElementContainer();	
		string anchorMin, anchorMax;
		switch (config.UILocation)
		{
			case "top left":
				anchorMin = "0.01 0.85";
				anchorMax = "0.3 0.99";
				break;
			case "top center":
				anchorMin = "0.35 0.85";
				anchorMax = "0.65 0.99";
				break;
			case "top right":
				anchorMin = "0.7 0.85";
				anchorMax = "0.99 0.99";
				break;
			case "left center":
				anchorMin = "0.001 0.40";
				anchorMax = "0.3 0.60";
			break;
			case "right center":
				anchorMin = "0.7 0.40";
				anchorMax = "0.99 0.60";
			break;
			case "bottom left":
        		anchorMin = "0.01 0.01";
        		anchorMax = "0.3 0.15";
        		break;
			default:
				anchorMin = "0.7 0.85";
				anchorMax = "0.99 0.99";
				break;
		}	
		var mainPanel = elements.Add(new CuiPanel
		{
			Image = { Color = "0 0 0 0" },
			RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
			CursorEnabled = false,
		}, "Hud", UI_BARRELBASH);
		elements.Add(new CuiLabel
		{
			Text = { Text = "<color=orange>Barrel</color><color=red>Bash</color>", FontSize = 22, Align = TextAnchor.MiddleCenter },
			RectTransform = { AnchorMin = "0.1 0.70", AnchorMax = "0.9 0.99" }
		}, mainPanel);
		elements.Add(new CuiLabel
		{
    		Text = { Text = uiContentLeft, FontSize = 16, Align = TextAnchor.MiddleLeft },
    		RectTransform = { AnchorMin = "0.05 0.15", AnchorMax = "0.55 0.7" }
		}, mainPanel);
		elements.Add(new CuiLabel
		{
    		Text = { Text = uiContentRight, FontSize = 16, Align = TextAnchor.MiddleLeft },
   		 RectTransform = { AnchorMin = "0.55 0.15", AnchorMax = "1.05 0.7" }
		}, mainPanel);
		float timeRemaining = eventDuration - (Time.realtimeSinceStartup - eventStartTime);
		string timerText = $"Time remaining: {Mathf.FloorToInt(timeRemaining / 60)}m {Mathf.FloorToInt(timeRemaining % 60)}s";
		elements.Add(new CuiLabel
		{
			Text = { Text = timerText, FontSize = 16, Align = TextAnchor.MiddleCenter },
			RectTransform = { AnchorMin = "0.1 -0.23", AnchorMax = "0.9 0.3" }
		}, mainPanel, "UI_BARRELBASH_TIMER");
		CuiHelper.AddUi(player, elements);
	}
    private void HideLeaderboardUI(BasePlayer player)
    {
        CuiHelper.DestroyUi(player, UI_BARRELBASH);
    }
    private void GiveItem(BasePlayer player, string itemShortName, int amount, ulong skinId = 0)
	{
		ItemDefinition itemDefinition = ItemManager.FindItemDefinition(itemShortName);
		if (itemDefinition != null)
		{
			player.GiveItem(ItemManager.Create(itemDefinition, amount, skinId));
		}
		else
		{
			PrintWarning($"Unable to give item '{itemShortName}' to player '{player.displayName}', item not found.");
		}
	}
	private void ExecuteCommand(string command)
	{
		if (!string.IsNullOrEmpty(command))
		{
			ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), command);
		}
		else
		{
			PrintWarning("Unable to execute empty command.");
		}
	}
	private void SaveWinnerToFile(BasePlayer winningPlayer)
	{
		string logFilePath = $"{Interface.Oxide.LogDirectory}/BarrelBashWinners.log";
		using (StreamWriter file = File.AppendText(logFilePath))
		{
			string formattedDate = DateTime.Now.ToString("MM/dd/yyyy HH:mm");
			file.WriteLine($"Winner {winningPlayer.displayName} [{winningPlayer.userID}] [{formattedDate}]");
		}
	}
	private void CheckRequiredPlugins()
	{
		Economics = plugins.Find("Economics");
		ServerRewards = plugins.Find("ServerRewards");
		if (Economics == null)
		{
			Puts($"Warning: Economics plugin is not loaded. Some features may not work as expected.");
		}
		if (ServerRewards == null)
		{
			Puts($"Warning: ServerRewards plugin is not loaded. Some features may not work as expected.");
		}
	}
	private void ClearUIAndGameTipsForAllPlayers()
	{
		foreach (BasePlayer player in BasePlayer.activePlayerList)
		{
			player.SendConsoleCommand("gametip.hidegametip");
			CuiHelper.DestroyUi(player, UI_BARRELBASH);
		}
		leaderboardUpdateTimer?.Destroy();
		gameTipTimer?.Destroy();
        autoStartTimer?.Destroy();
	}
	private void Unload()
	{
		ClearUIAndGameTipsForAllPlayers();
		Unsubscribe(nameof(OnEntityDeath));
	}
}
}