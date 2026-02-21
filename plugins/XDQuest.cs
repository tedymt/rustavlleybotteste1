using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Rust;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Facepunch;
using Oxide.Core.Libraries;
using UnityEngine;
using UnityEngine.Networking;
using Random = Oxide.Core.Random;
using Oxide.Plugins.XDQuestExtensionMethods;

namespace Oxide.Plugins
{
	[Info("XDQuest", "DezLife", "8.5.5")]
	[Description("Расширенная квест система для вашего сервера!")]
	public class XDQuest : RustPlugin 
	{
		#region ReferencePlugins

		[PluginReference] Plugin CopyPaste, ImageLibrary, IQChat, Friends, Clans, EventHelper, Battles, Duel, Duelist, ArenaTournament, Notify, SkillTree, CustomVendingSetup;

		private void SendChat(BasePlayer player, string message, ConVar.Chat.ChatChannel channel = ConVar.Chat.ChatChannel.Global)
		{
			if (IQChat)
				if (_config.settingsIQChat.UIAlertUse)
					IQChat?.Call("API_ALERT_PLAYER_UI", player, message);
				else IQChat?.Call("API_ALERT_PLAYER", player, message, _config.settingsIQChat.CustomPrefix, _config.settingsIQChat.CustomAvatar);
			else player.SendConsoleCommand("chat.add", channel, 0, message);
		}

		private bool IsFriends(ulong userID, ulong targetID)
		{
			if (Friends is not null)
				return Friends.Call("HasFriend", userID, targetID) is true;
    
			return RelationshipManager.ServerInstance.playerToTeam.TryGetValue(userID, out RelationshipManager.PlayerTeam team) && team.members.Contains(targetID);
		}

		private bool IsClans(string userID, string targetID)
		{
			if (Clans)
			{
				string tagUserID = (string)Clans?.Call("GetClanOf", userID);
				string tagTargetID = (string)Clans?.Call("GetClanOf", targetID);
				if (tagUserID == null && tagTargetID == null)
				{
					return false;
				}

				return tagUserID == tagTargetID;
			}
			else
			{
				return false;
			}
		}

		private bool IsDuel(ulong userID)
		{
			object playerId = ObjectCache.Get(userID);
			BasePlayer player = null;
			if (Duel != null || Duelist != null)
				player = BasePlayer.FindByID(userID);

			object result = EventHelper?.Call("EMAtEvent", playerId);
			if (result is bool && ((bool)result) == true)
				return true;


			if (Battles != null && Battles.Call<bool>("IsPlayerOnBattle", playerId))
				return true;


			if (Duel != null && Duel.Call<bool>("IsPlayerOnActiveDuel", player))
				return true;
			if (Duelist != null && Duelist.Call<bool>("inEvent", player))
				return true;

			if (ArenaTournament != null && ArenaTournament.Call<bool>("IsOnTournament", playerId))
				return true;

			return false;
		}

		private string GetImage(string shortname, ulong skin = 0)
		{
			return (string)ImageLibrary?.Call("GetImage", shortname, skin);
		}

		private bool AddImage(string url, string shortname, ulong skin = 0)
		{
			return (bool)ImageLibrary?.Call("AddImage", url, shortname, skin);
		}

		private bool HasImage(string imageName, ulong imageId = 0)
		{
			return (bool)ImageLibrary?.Call("HasImage", imageName, imageId);
		}

		#endregion

		#region Variables

		private const bool RU = false;
		
		public static XDQuest? Instance;
		private MonumentInfo _monument;
		private NPCMissionProvider _npc;
		private SafeZone _safeZone;
		private AudioZoneController _audioZoneController;
		private ImageUI _imageUI;


		private List<BaseEntity> _houseNpc = new();
		
		public static Timer QuestCooldownsTimer;

		private Dictionary<long, Quest> _questList = new();

		private Dictionary<ulong, PlayerData> _playersInfo = new();
		private QuestStatistics _questStatistics = new();

		private class PlayerData
		{
			public List<long> CompletedQuestIds = new();
			public Dictionary<long, double> PlayerQuestCooldowns = new();
			public List<PlayerQuest> CurrentPlayerQuests = new();

			public double? GetCooldownForQuest(long questId)
			{
				if (PlayerQuestCooldowns == null)
				{
					return null;
				}

				double cooldown;
				if (PlayerQuestCooldowns.TryGetValue(questId, out cooldown))
				{
					return cooldown;
				}

				return null;
			}
		}

		#endregion

		#region Const

		private const string BOOM_BOX_PREFAB = "assets/prefabs/voiceaudio/boombox/boombox.deployed.prefab";
		private const string SPHERE_PREFAB = "assets/prefabs/visualization/sphere.prefab";
		private const string MISSIONPROVIDER_TEST = "assets/prefabs/npc/bandit/shopkeepers/missionprovider_test.prefab";
		private const string QUEST_BUILDING_NAME = "XDQuestV2";
		private const ulong QUEST_BUILDING_OWNER = 3774732;
		#endregion

		#region Lang

		protected override void LoadDefaultMessages()
		{
			lang.RegisterMessages(new Dictionary<string, string>
			{
				["XDQUEST_CopyPasteError"] = "There was a problem with CopyPaste! Contact the Developer!\nDiscord: @DezLife\nnvk.com/dezlife",
				["XDQUEST_CopyPasteSuccessfully"] = "The building has spawned successfully!",
				["XDQUEST_BuildingPasteError"] = "There was a problem with spawning the Building! Contact the Developer!\nDiscord: @DezLife\nvk.com/dezlife",
				["XDQUEST_MissingOutPost"] = "Your map doesnt have an Outpost Monument. Please use a custom spawn point.",
				["XDQUEST_MissingQuests"]
					= "You do not have a file with tasks, the plugin will not work correctly! Create one on the Website - https://xdquest.skyplugins.ru/ or use the included one.",
				["XDQUEST_FileNotLoad"] = "The construction file was not found : {0}. Move it to the copy paste folder",
				["XDQUEST_UI_TASKLIST"] = "Quest List",
				["XDQUEST_UI_Awards"] = "Rewards",
				["XDQUEST_UI_TASKCount"] = "<color=#42a1f5>{0}</color> QUESTS",
				["XDQUEST_UI_CHIPperformed"] = "Completed",
				["XDQUEST_UI_CHIPInProgress"] = "In progress",
				["XDQUEST_UI_QUESTREPEATCAN"] = "Yes",
				["XDQUEST_UI_QUESTREPEATfForbidden"] = "No",
				["XDQUEST_UI_Missing"] = "Missing",
				["XDQUEST_UI_InfoRepeatInCD"] = "Repeat {0}  |  Cooldown {1}  |  Hand in {2}",
				["XDQUEST_UI_QuestNecessary"] = "Needed",
				["XDQUEST_UI_QuestNotNecessary"] = "Not needed",
				["XDQUEST_UI_QuestBtnPerformed"] = "COMPLETED",
				["XDQUEST_UI_QuestBtnTake"] = "TAKE",
				["XDQUEST_UI_QuestBtnPass"] = "COMPLETE",
				["XDQUEST_UI_QuestBtnDelivery"] = "DELIVER",
				["XDQUEST_UI_QuestBtnRefuse"] = "REFUSE",
				["XDQUEST_UI_ACTIVEOBJECTIVES"] = "Objective: {0}",
				["XDQUEST_UI_MiniQLInfo"] = "{0}\nProgress: {1} / {2}\nQuest: {3}",
				["XDQUEST_UI_MiniQLInfoDelivery"] = "{0}\nQuest: {3}",
				["XDQUEST_UI_CMDPosChange"]
					= "You have successfully changed the position for building within the Outpost.\n(You need to reload the plugin)\nYou can configure the building's rotation in the config",
				["XDQUEST_UI_CMDCustomPosAdd"]
					= "You have successfully added a custom building position.\n(You need to reload the plugin)\nYou can rotate the building in the config!\nRemember to enable the option to spawn a building on a custom position in the config.",
				["XDQUEST_UI_QuestLimit"] = "You have to many <color=#4286f4>unfinished</color> Quests",
				["XDQUEST_UI_AlreadyTaken"] = "You have already <color=#4286f4>taken</color> this Quest!",
				["XDQUEST_UI_NotPerm"] = "You do not have the rights to perform this Quest.",
				["XDQUEST_UI_AlreadyDone"] = "You have already <color=#4286f4>completed</color> this Quest!",
				["XDQUEST_UI_TookTasks"] = "You have <color=#4286f4>successfully</color> accepted the Quest {0}",
				["XDQUEST_UI_ACTIVECOLDOWN"] = "This Quest is on Cooldown.",
				["XDQUEST_UI_LackOfSpace"] = "Your inventory is full! Clear some space and try again!",
				["XDQUEST_UI_QuestsCompleted"] = "Quest Completed! Enjoy your reward!",
				["XDQUEST_UI_PassedTasks"] = "So this Quest was to much for you? \n Try again later!",
				["XDQUEST_UI_ActiveQuestCount"] = "You have no active Quests.",
				["XDQUEST_Finished_QUEST"] = "You have completed the task: <color=#4286f4>{0}</color>",
				["XDQUEST_Finished_QUEST_ALL"] = "Player <color=#4286f4>{0}</color> just completed a task: <color=#4286f4>{1}</color> and got a reward!",
				["XDQUEST_UI_InsufficientResources"] = "You don't have {0}, you should definitely bring this to Sidorovich",
				["XDQUEST_UI_InsufficientResourcesSkin"] = "You don’t have the required item, you need to bring it to Sidorovich",
				["XDQUEST_UI_NotResourcesAmount"] = "You don't have enough {0}, you need {1}",
				["XDQUEST_SoundLoadErrorExt"] = "The voice file {0} is missing, upload it using this path - (/data/XDQuest/Sounds). Or remove it from the configuration",
				["XDQUEST_UI_CATEGORY"] = "CATEGORIES",
				["XDQUEST_UI_CATEGORY_ONE"] = "Available tasks",
				["XDQUEST_UI_CATEGORY_TWO"] = "Active tasks",
				["XDQUEST_UI_TASKS_LIST_EMPTY"] = "Quest list is empty",
				["XDQUEST_UI_TASKS_INFO_EMPTY"] = "Select a task to see information about it",
				["XDQUEST_REPEATABLE_QUEST_AVAILABLE_AGAIN"] = "You can participate in the quest \"<color=#4286f4>{0}</color>\" again! \nDon't miss your chance!",
				["XDQUEST_STAT_1"] = "Main Statistics",
				["XDQUEST_STAT_2"] = "**Tasks Completed:** {0}\n\n**Total Tasks Taken:** {1}\n\n**Tasks Declined:** {2}",
				["XDQUEST_STAT_3"] = "Top 5 Tasks",
				["XDQUEST_STAT_4"] = "**🔥 Frequently Performed Tasks:**\n{0}\n\n**❄️ Rarely Performed Tasks:**\n{1}\n",
				["XDQUEST_STAT_CMD_1"] = "Your statistics collection is disabled. Activate this feature in the configuration settings!",
				["XDQUEST_STAT_CMD_2"] = "You haven't set a webhook. Please specify it in the configuration settings and try again!",
				["XDQUEST_STAT_CMD_3"] = "Statistical data has been successfully sent!",
				["XDQUEST_INSUFFICIENT_PERMISSIONS_ERROR"] = "You don't have sufficient permissions to use this command.",
				["XDQUEST_COMMAND_SYNTAX_ERROR"] = "Incorrect syntax! Use: xdquest.player.reset [steamid64]",
				["XDQUEST_INVALID_PLAYER_ID_INPUT"] = "Invalid input! Please enter a valid player ID.",
				["XDQUEST_NOT_A_STEAM_ID"] = "The entered ID is not a SteamID. Please check and try again.",
				["XDQUEST_PLAYER_PROGRESS_RESET"] = "The player's progress has been successfully reset!",
				["XDQUEST_PLAYER_NOT_FOUND_BY_STEAMID"] = "Player with the specified Steam ID not found.",

			}, this);

			lang.RegisterMessages(new Dictionary<string, string>
			{
				["XDQUEST_CopyPasteError"] = "Возникла проблема с CopyPaste! Обратитесь к разработчику\nDiscord: @DezLife\nvk.com/dezlife",
				["XDQUEST_CopyPasteSuccessfully"] = "Постройка успешно заспавнена!",
				["XDQUEST_BuildingPasteError"] = "Ошибка спавна поостройки! Обратитесь к разработчику\nDiscord: @DezLife\nvk.com/dezlife",
				["XDQUEST_MissingOutPost"] = "У вас отсутствует outpost, вы можете использовать кастомную позицию для постройки",
				["XDQUEST_MissingQuests"]
					= "У вас отсутсвует файл с заданиями, плагин будет работать не коректно!  Создайте его на сайте - https://xdquest.skyplugins.ru/ или используйте стандартный",
				["XDQUEST_FileNotLoad"] = "Не найден файл постройки : {0}. Переместите его в папку copypaste",
				["XDQUEST_UI_TASKLIST"] = "СПИСОК ЗАДАНИЙ",
				["XDQUEST_UI_Awards"] = "Награды",
				["XDQUEST_UI_TASKCount"] = "<color=#42a1f5>{0}</color> ЗАДАНИЙ",
				["XDQUEST_UI_CHIPperformed"] = "выполнено",
				["XDQUEST_UI_CHIPInProgress"] = "выполняется",
				["XDQUEST_UI_QUESTREPEATCAN"] = "можно",
				["XDQUEST_UI_QUESTREPEATfForbidden"] = "нельзя",
				["XDQUEST_UI_Missing"] = "отсутствует",
				["XDQUEST_UI_InfoRepeatInCD"] = "Повторно брать {0}  |  Кд на повторное взятие {1}  |  Сдать добытое {2}",
				["XDQUEST_UI_QuestNecessary"] = "нужно",
				["XDQUEST_UI_QuestNotNecessary"] = "не нужно",
				["XDQUEST_UI_QuestBtnPerformed"] = "ВЫПОЛНЕНО",
				["XDQUEST_UI_QuestBtnTake"] = "ВЗЯТЬ",
				["XDQUEST_UI_QuestBtnPass"] = "ЗАВЕРШИТЬ",
				["XDQUEST_UI_QuestBtnDelivery"] = "ДОСТАВИТЬ",
				["XDQUEST_UI_QuestBtnRefuse"] = "ОТКАЗАТЬСЯ",
				["XDQUEST_UI_ACTIVEOBJECTIVES"] = "АКТИВНЫЕ ЗАДАЧИ: {0}",
				["XDQUEST_UI_MiniQLInfo"] = "{0}\nПрогресс: {1} / {2}\nЗадача: {3}",
				["XDQUEST_UI_MiniQLInfoDelivery"] = "{0}\nЗадача: {3}",
				["XDQUEST_UI_CMDPosChange"]
					= "Вы успешно изменили позицию для постройки в пределах OutPost.\n(Вам нужно перезагрузить плагин)\nНастроить поворот постройки можно в конфиге",
				["XDQUEST_UI_CMDCustomPosAdd"]
					= "Вы успешно добавили кастомную позицию для постройки.\n(Вам нужно перезагрузить плагин)\nПовернуть ее можно в конфиге!\nТак же не забудъте включить в конфиге возможность спавнить постройку на кастомной позиции",
				["XDQUEST_UI_QuestLimit"] = "У тебя слишком много <color=#4286f4>не законченных</color> заданий!",
				["XDQUEST_UI_AlreadyTaken"] = "Вы уже <color=#4286f4>взяли</color> это задание!",
				["XDQUEST_UI_NotPerm"] = "У вас нет прав для выполнения данного задания.",
				["XDQUEST_UI_AlreadyDone"] = "Вы уже <color=#4286f4>выполняли</color> это задание!",
				["XDQUEST_UI_TookTasks"] = "Вы <color=#4286f4>успешно</color> взяли задание {0}",
				["XDQUEST_UI_ACTIVECOLDOWN"] = "В данный момент вы не можете взять этот квест",
				["XDQUEST_UI_LackOfSpace"] = "Эй, погоди, ты всё <color=#4286f4>не унесёшь</color>, освободи место!",
				["XDQUEST_UI_QuestsCompleted"] = "Спасибо, держи свою <color=#4286f4>награду</color>!",
				["XDQUEST_UI_PassedTasks"] = "Жаль что ты <color=#4286f4>не справился</color> с заданием!\nВ любом случае, ты можешь попробовать ещё раз!",
				["XDQUEST_UI_ActiveQuestCount"] = "У вас нет активных заданий.",
				["XDQUEST_Finished_QUEST"] = "Вы выполнили задание: <color=#4286f4>{0}</color>",
				["XDQUEST_Finished_QUEST_ALL"] = "Игрок <color=#4286f4>{0}</color>  только что выполнил задание: <color=#4286f4>{1}</color> и получил награду!",
				["XDQUEST_UI_InsufficientResources"] = "У вас нету {0}, нужно обязательно принести это сидоровичу",
				["XDQUEST_UI_InsufficientResourcesSkin"] = "У вас нету нужного предмета, нужно обязательно принести это сидоровичу",
				["XDQUEST_UI_NotResourcesAmount"] = "У вас не достаточно {0},  нужно {1}",
				["XDQUEST_SoundLoadErrorExt"] = "Отсутсвует голосовой файл {0}, загрузите его по этому пути - (/data/XDQuest/Sounds). Или удалите его из конфигурации",
				["XDQUEST_UI_CATEGORY"] = "КАТЕГОРИИ",
				["XDQUEST_UI_CATEGORY_ONE"] = "Доступные задания",
				["XDQUEST_UI_CATEGORY_TWO"] = "Активные задания",
				["XDQUEST_UI_TASKS_LIST_EMPTY"] = "Список заданий пуст",
				["XDQUEST_UI_TASKS_INFO_EMPTY"] = "Выбирите задания чтоб увидеть информацию о нем",
				["XDQUEST_REPEATABLE_QUEST_AVAILABLE_AGAIN"] = "Снова можно принять участие в квесте \"<color=#4286f4>{0}</color>\"! \nНе упустите свой шанс!",
				["XDQUEST_STAT_1"] = "Основная статистика",
				["XDQUEST_STAT_2"] = "**Выполнено заданий:** {0}\n\n**Всего взято заданий:** {1}\n\n**Отказов от заданий:** {2}",
				["XDQUEST_STAT_3"] = "Топ 5 заданий",
				["XDQUEST_STAT_4"] = "**🔥 Часто выполняемые задания:**\n{0}\n\n**❄️ Редко выполняемые задания:**\n{1}\n",
				["XDQUEST_STAT_CMD_1"] = "Ваш сбор статистики отключен. Активируйте эту функцию в конфигурации!",
				["XDQUEST_STAT_CMD_2"] = "У вас не задан webhook. Пожалуйста, укажите его в конфигурации и попробуйте ещё раз!",
				["XDQUEST_STAT_CMD_3"] = "Статистические данные успешно отправлены!",
				["XDQUEST_INSUFFICIENT_PERMISSIONS_ERROR"] = "У вас недостаточно прав для использования этой команды.",
				["XDQUEST_COMMAND_SYNTAX_ERROR"] = "Неверный синтаксис! Используйте: xdquest.player.reset [steamid64]",
				["XDQUEST_INVALID_PLAYER_ID_INPUT"] = "Неверный ввод! Пожалуйста, введите корректный ID игрока.",
				["XDQUEST_NOT_A_STEAM_ID"] = "Введенный ID не является SteamID. Пожалуйста, проверьте и попробуйте снова.",
				["XDQUEST_PLAYER_PROGRESS_RESET"] = "Прогресс игрока успешно сброшен!",
				["XDQUEST_PLAYER_NOT_FOUND_BY_STEAMID"] = "Игрок с указанным Steam ID не найден.",
			}, this, "ru");
		}

		#endregion

		#region Configuration

		private enum SpawnMode
		{
			Construction,
			NPC
		}
		private Configuration _config;

		private class Configuration
		{
			public class Settings
			{
				[JsonProperty(RU ? "Макс. количество одновременных квестов" : "Max number of concurrent quests")]
				public int questCount = 3;

				[JsonProperty(RU ? "Воспроизведение звукового эффекта при выполнении задания" : "Play sound effect upon task completion")]
				public bool SoundEffect = true;

				[JsonProperty(RU ? "Эфект" : "Effect")]
				public string Effect = "assets/prefabs/locks/keypad/effects/lock.code.lock.prefab";

				[JsonProperty(RU ? "Отчищать прогресс игроков при вайпе ?" : "Clear player progress when wipe ?")]
				public bool useWipe = true;
				[JsonProperty(RU ? "Отчищать разрешения игроков при вайпе?" : "Clean up player permissions when wiping?")]
				public bool useWipePermission = true;

				[JsonProperty(RU ? "Имя файла с заданиями" : "Quests file name")]
				public string questListDataName = "Quest";

				[JsonProperty(RU ? "Команды открытия списка квестов с прогрессом" : "Commands to open quest list with progress", ObjectCreationHandling = ObjectCreationHandling.Replace)]
				public string[] questListProgress = { "qlist", "quest" };

				[JsonProperty(RU ? "Активировать радио для NPC в здании?" : "Activate radio for NPC in the building?")]
				public bool useRadio = true;

				[JsonProperty(RU ? "URL радиостанции для воспроизведения в здании" : "Radio station URL to play in the building")]
				public string RadioStation = "sonisradio.facepunch.com";
				
				[JsonProperty(RU ? "Идентификатор камеры в доме NPC" : "The ID of the camera in the NPC's house")]
				public string CameraId = "121314";

				[JsonProperty(RU ? "Оповещать всех игроков о завершении задания?" : "Notify all players on task completion?")]
				public bool sandNotifyAllPlayer = false;

				[JsonProperty(RU ? "[Skill Tree] Игнорировать бонус из плагина Skill Tree при добычи" : "[Skill Tree] Ignore bonus from Skill Tree plugin when mining")]
				public bool UseSkillTreeIgnoreHooks = false;
			}

			public class MapSettings
			{
				[JsonProperty(RU
					? "Использовать метку на игровой карте? (Требуется https://umod.org/plugins/marker-manager)"
					: "Use a mark on the game map? (Requires https://umod.org/plugins/marker-manager)")]
				public bool mapUse = false;

				[JsonProperty(RU ? "Наименование метки на карте" : "Name of the map marker")]
				public string nameMarkerMap = "QUEST ROOM";

				[JsonProperty(RU ? "Цвет маркера (без #)" : "Marker color (without #)")]
				public string colorMarker = "f3ecad";

				[JsonProperty(RU ? "Цвет обводки (без #)" : "Outline color (without #)")]
				public string colorOutline = "f3ecad";
			}

			public class SettingsNpc
			{
				[JsonProperty(RU ? "Экипировка NPC" : "NPC Outfit")]
				public List<NPCOutfit> Wear = new List<NPCOutfit>();

				[JsonProperty(RU ? "Активировать возможность общения с NPC" : "Enable communication with NPCs")]
				public bool soundUse = true;

				public class NPCOutfit
				{
					[JsonProperty("ShortName")] public string ShortName;

					[JsonProperty("SkinId")] public ulong SkinId;
				}
			}

			public class SettingsIQChat
			{
				[JsonProperty(RU ? "IQChat : Кастомный префикс в чате" : "IQChat : Custom prefix in chat")]
				public string CustomPrefix = "Quest";

				[JsonProperty(RU ? "IQChat : Кастомный аватар в чате(Если требуется)" : "IQChat : Custom chat avatar (If required)")]
				public string CustomAvatar = "0";

				[JsonProperty(RU ? "IQChat : Использовать UI уведомление (true - да/false - нет)" : "IQChat : Use UI notification (true - yes/false - no)")]
				public bool UIAlertUse;
			}

			public class SettingsNotify
			{
				[JsonProperty(RU ? "Включить уведомления (Требуется - https://codefling.com/plugins/notify)" : "Enable notifications (Is required - https://codefling.com/plugins/notify)")]
				public bool useNotify = false;

				[JsonProperty(RU ? "Тип уведомления (Требуется - https://codefling.com/plugins/notify)" : "Notification Type (Is required - https://codefling.com/plugins/notify)")]
				public int typeNotify = 0;
			}

			public class StatisticsCollectionSettings
			{
				[JsonProperty(RU ? "Включить сбор статистики и публикацию в дискорд?" : "Enable statistics collection and publication to Discord?")]
				public bool useStatistics = false;

				[JsonProperty(RU ? "Веб-хук дискорд для публикации статистики" : "Discord webhook for statistics publication")]
				public string discordWebhookUrl = "";

				[JsonProperty(RU ? "Как часто публиковать статистику? (Сек)" : "How often to publish statistics? (Sec)")]
				public float publishFrequency = 21600;
			}

			public class SpawnSettings
			{
				[JsonProperty(RU ? "Режим спавна (0 - Construction или 1 - NPC)" : "Spawn mode (0 - Construction or 1 - NPC)")]
				public SpawnMode Mode = SpawnMode.Construction;
				
				[JsonProperty(RU ? "Использовать пользовательскую позицию?" : "Use custom position?")]
				public bool UseCustomPosition = false;
				
				[JsonProperty(RU ? "Настройки пользовательской позиции" : "Custom position settings")]
				public PositionSettings CustomPosition = new();
				
				[JsonProperty(RU ? "Настройки позиции, привязанной к монументу" : "Position settings tied to a monument")]
				public MonumentPositionSettings MonumentPosition = new();
				
				[JsonProperty(RU ? "Настройки безопасной зоны" : "Safe zone settings")]
				public SafeZoneSettings SafeZone = new();
				
				public class PositionSettings
				{
					[JsonProperty(RU ? "Координаты позиции (/quest.saveposition)" : "Position coordinates(/quest.saveposition)")]
					public Vector3 Position = Vector3.zero;

					[JsonProperty(RU ? "Угол поворота (0-360 градусов)" : "Rotation angle (0-360 degrees)")]
					public float Rotation = 0;
				}
				
				public class MonumentPositionSettings
				{
					[JsonProperty(RU ? "Координаты относительно монумента (/quest.saveposition.outpost)" : "Position coordinates relative to monument (/quest.saveposition.outpost)")]
					public Vector3 Position = new(-2.1f, 1.4f, 30.3f);

					[JsonProperty(RU ? "Угол поворота (0-360 градусов)" : "Rotation angle (0-360 degrees)")]
					public float Rotation = 271F;
				}
				
				public class SafeZoneSettings
				{
					[JsonProperty(RU ? "Включить безопасную зону?" : "Enable safe zone?")]
					public bool Enable = false;

					[JsonProperty(RU ? "Радиус безопасной зоны" : "Safe zone radius")]
					public float Radius = 25f;
				}
			}

			[JsonProperty(RU ? "Общие настройки" : "General Settings")]
			public Settings settings = new Settings();
			
			[JsonProperty(RU ? "Настройки спавна" : "Spawn Settings")]
			public SpawnSettings spawnSettings = new SpawnSettings();

			[JsonProperty(RU ? "Настройки меток на игровой карте" : "Map Marker Settings")]
			public MapSettings mapSettings = new MapSettings();

			[JsonProperty(RU ? "Настройки параметров NPC" : "NPC Parameters Settings")]
			public SettingsNpc settingsNpc = new SettingsNpc();
			
			[JsonProperty(RU ? "Настройка сбора статистики" : "Statistics collection settings")]
			public StatisticsCollectionSettings statisticsCollectionSettings = new StatisticsCollectionSettings();

			[JsonProperty(RU ? "Настройки IQChat (если применимо)" : "IQChat Settings (if applicable)")]
			public SettingsIQChat settingsIQChat = new SettingsIQChat();

			[JsonProperty(RU ? "Настройки уведомлений" : "Notification Settings")]
			public SettingsNotify settingsNotify = new SettingsNotify();
		}

		protected override void LoadConfig()
		{
			base.LoadConfig();
			try
			{
				_config = Config.ReadObject<Configuration>();
				if (_config == null)
				{
					throw new Exception();
				}

				SaveConfig();
			}
			catch
			{
				for (int i = 0; i < 3; i++)
				{
					PrintError("Configuration file is corrupt! Check your config file at https://jsonlint.com/");
				}

				LoadDefaultConfig();
			}

			ValidateConfig();
			SaveConfig();
		}

		private void ValidateConfig()
		{
			if (_config.settingsNpc.Wear.Count == 0)
			{
				_config.settingsNpc.Wear = new List<Configuration.SettingsNpc.NPCOutfit>
				{
					new Configuration.SettingsNpc.NPCOutfit
					{
						ShortName = "hazmatsuit.nomadsuit",
						SkinId = 0,
					}
				};
			}
		}

		protected override void SaveConfig()
		{
			Config.WriteObject(_config);
		}

		protected override void LoadDefaultConfig()
		{
			_config = new Configuration();
		}

		#endregion

		#region QuestData

		private class PlayerQuest
		{
			public long ParentQuestID;
			public QuestType ParentQuestType;

			public ulong UserID;

			public bool Finished;
			public int Count;

			public void AddCount(int amount = 1)
			{
				Count += amount;
				BasePlayer player = BasePlayer.FindByID(UserID);
				Quest parentQuest = Instance._questList[ParentQuestID];
				if (parentQuest.ActionCount <= Count)
				{
					Count = parentQuest.ActionCount;
					if (player != null && player.IsConnected)
					{
						if (Instance._config.settings.SoundEffect)
						{
							Instance.RunEffect(player, Instance._config.settings.Effect);
						}

						if (Instance._config.settingsNotify.useNotify && Instance.Notify)
						{
							Instance.Notify.CallHook("SendNotify", player, Instance._config.settingsNotify.typeNotify, 
								"XDQUEST_Finished_QUEST".GetAdaptedMessage(player.UserIDString, parentQuest.GetDisplayName(Instance.lang.GetLanguage(player.UserIDString))));
						}
						else
						{
							Instance.SendChat(player, "XDQUEST_Finished_QUEST".GetAdaptedMessage(player.UserIDString, parentQuest.GetDisplayName(Instance.lang.GetLanguage(player.UserIDString))));
						}

						if (Instance._config.settings.sandNotifyAllPlayer)
						{
							foreach (BasePlayer players in BasePlayer.activePlayerList)
							{
								Instance.SendChat(players, "XDQUEST_Finished_QUEST_ALL".GetAdaptedMessage( players.UserIDString, player.displayName,
									parentQuest.GetDisplayName(Instance.lang.GetLanguage(player.UserIDString))));
							}
						}

						Interface.CallHook("OnQuestCompleted", player, parentQuest.GetDisplayName(Instance.lang.GetLanguage(player.UserIDString)));
						Instance._questStatistics.GatherTaskStatistics(TaskType.TaskExecution, ParentQuestID);
						Instance._questStatistics.GatherTaskStatistics(TaskType.Completed);
					}

					Finished = true;
				}

				if (Instance._openMiniQuestListPlayers.Contains(UserID))
					Instance.OpenMQL_CMD(player);
			}
		}

		private enum AudioTriggerTypes
		{
			Greeting,
			Farewell,
			TaskAcceptance,
			TaskCompletion
		}
		public enum TaskType
		{
			Completed,
			Taken,
			Declined,
			TaskExecution
		}

		private enum QuestType
		{
			IQPlagueSkill,
			IQHeadReward,
			IQCases,
			OreBonus,
			XDChinookIvent,
			Gather,
			EntityKill,
			Craft,
			Research,
			Loot,
			Grade,
			Swipe,
			Deploy,
			PurchaseFromNpc,
			HackCrate,
			RecycleItem,
			Growseedlings,
			RaidableBases,
			Fishing,
			BossMonster,
			HarborEvent,
			SatelliteDishEvent,
			Sputnik,
			AbandonedBases,
			Delivery,
			IQDronePatrol,
			GasStationEvent,
			Triangulation,
			FerryTerminalEvent,
			Convoy,
			Caravan,
			IQDefenderSupply
		}

		private enum PrizeType
		{
			Item,
			BluePrint,
			CustomItem,
			Command
		}

		private class Quest
		{
			internal class Prize
			{
				public string PrizeName;
				public PrizeType PrizeType;
				public string ItemShortName;
				public int ItemAmount;
				public string CustomItemName;
				public ulong ItemSkinID;
				public string PrizeCommand;
				public string CommandImageUrl;
				public bool IsHidden;
			}

			public long QuestID;
			public string QuestDisplayName;
			public string QuestDisplayNameMultiLanguage;
			public string QuestDescription;
			public string QuestDescriptionMultiLanguage;
			public string QuestMissions;
			public string QuestMissionsMultiLanguage;

			public string QuestPermission;
			public QuestType QuestType;
			public string Target;
			public int ActionCount;
			public bool IsRepeatable;
			public bool IsMultiLanguage;
			public bool IsReturnItemsRequired;
			public int Cooldown;
			
			[JsonIgnore]
			public bool IsMoreTarget = false;
			[JsonIgnore]
			public string[] Targets;
			public List<Prize> PrizeList = new List<Prize>();

			public string GetDisplayName(string language) => language == "ru" || IsMultiLanguage == false ? QuestDisplayName : QuestDisplayNameMultiLanguage;
			public string GetDescription(string language) => language == "ru" || IsMultiLanguage == false ? QuestDescription : QuestDescriptionMultiLanguage;
			public string GetMissions(string language) => language == "ru" || IsMultiLanguage == false ? QuestMissions : QuestMissionsMultiLanguage;
		}

		#endregion

		#region MetodsBuildingAndNpc
		
		private void HandleSpawn()
		{
			Vector3 resultVector = GetResultVector();
			float resultRotCorrection = GetResultRotation();
			
			switch (_config.spawnSettings.Mode)
			{
				case SpawnMode.Construction:
					SpawnConstruction(resultVector, resultRotCorrection);
					break;

				case SpawnMode.NPC:
					SpawnNPCs(resultVector, resultRotCorrection);
					break;
			}
			
			if (_config.mapSettings.mapUse)
				CreateMapMarker(resultVector);

			if (_config.spawnSettings.UseCustomPosition && _config.spawnSettings.SafeZone.Enable)
			{
				_safeZone = new GameObject().AddComponent<SafeZone>();
				_safeZone.Activate(resultVector, _config.spawnSettings.SafeZone.Radius);
			}
		}
		
		private void SpawnConstruction(Vector3 position, float rotation)
		{
			List<string> options = new() { "stability", "true", "deployables", "true", "autoheight", "false", "entityowner", "false", "enablesaving", "false" };

			object success = CopyPaste.Call("TryPasteFromVector3", position, rotation, QUEST_BUILDING_NAME, options.ToArray());
			if (success is string)
				PrintWarning("XDQUEST_CopyPasteError".GetAdaptedMessage());
		}
		
		private void SpawnNPCs(Vector3 position, float rotationNpc)
		{
			Quaternion rotation = Quaternion.Euler(0, RadianToDegree(rotationNpc), 0);

			InitializeNpc(position, rotation);
		}
		
		private void InitializeBoomBox(Transform npcTransform)
		{
			DeployableBoomBox boomBox = null;
			SphereEntity sphereEntity = null;
			try
			{
				Vector3 pos = npcTransform.position + Vector3.up;
				Quaternion rot = npcTransform.rotation;

				boomBox = GameManager.server.CreateEntity(BOOM_BOX_PREFAB, pos, rot) as DeployableBoomBox;
				if (boomBox == null)
				{
					Debug.LogError("Unable to create DeployableBoomBox entity.");
					return;
				}

				boomBox.enableSaving = false;
				boomBox.Spawn();

				UnityEngine.Object.Destroy(boomBox.GetComponent<DestroyOnGroundMissing>());
				UnityEngine.Object.Destroy(boomBox.GetComponent<GroundWatch>());

				boomBox.BoxController.SetFlag(BaseEntity.Flags.Reserved8, true);
				boomBox.BoxController.ConditionLossRate = 0;

				sphereEntity = InitializeSphereEntity(boomBox);
				if (sphereEntity == null)
				{
					if (boomBox != null && !boomBox.IsDestroyed)
						boomBox.Kill();
					return;
				}

				_audioZoneController = new GameObject().AddComponent<AudioZoneController>();
				_audioZoneController.Activate(pos, boomBox, 3.2f);
			}
			catch (Exception e)
			{
				Debug.LogError("An error occurred while initializing BoomBox: " + e);
				if (boomBox != null && !boomBox.IsDestroyed)
					boomBox.Kill();
				if (sphereEntity != null && !sphereEntity.IsDestroyed)
					sphereEntity.Kill();
			}
		}

		private SphereEntity InitializeSphereEntity(DeployableBoomBox boomBox)
		{
			SphereEntity sphereEntity = null;
			try
			{
				Transform boomBoxTransform = boomBox.transform;
				boomBoxTransform.GetPositionAndRotation(out Vector3 pos, out Quaternion rot);
                sphereEntity = GameManager.server.CreateEntity(SPHERE_PREFAB, pos, rot) as SphereEntity;
				if (sphereEntity == null)
				{
					Debug.LogError("Unable to create SphereEntity entity.");
					return null;
				}

				sphereEntity.currentRadius = 0.1f;
				sphereEntity.lerpRadius = 0.1f;
				sphereEntity.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
				sphereEntity.EnableSaving(boomBox.enableSaving);

				sphereEntity.SetParent(boomBox.GetParentEntity());
				sphereEntity.Spawn();

				boomBox.transform.localPosition = Vector3.zero;
				boomBox.SetParent(sphereEntity, worldPositionStays: false, sendImmediate: true);
				NextTick(() =>
				{
					_houseNpc.Add(boomBox);
					_houseNpc.Add(sphereEntity);
				});
			}
			catch (Exception e)
			{
				Debug.LogError("An error occurred while initializing SphereEntity: " + e);
				if (sphereEntity != null && !sphereEntity.IsDestroyed)
					sphereEntity.Kill();
				return null;
			}

			return sphereEntity;
		}
		

		private void InitializeNpc(Vector3 pos, Quaternion rot)
		{
			_npc = GameManager.server.CreateEntity(MISSIONPROVIDER_TEST, pos, rot) as NPCMissionProvider;
			if (_npc == null)
			{
				Debug.LogError($"Initializing NPC failed! NPCMissionProvider Component == null");
				return;
			}
			
			_npc.userID = 21;
			_npc.UserIDString = _npc.userID.ToString();
			_npc.displayName = Name;
			_npc.Spawn();
			_npc.inventory.containerWear.Clear();
			_npc.SendNetworkUpdate();
			
			#region NpcWearStart

			if (_config.settingsNpc.Wear.Count > 0)
				foreach (Configuration.SettingsNpc.NPCOutfit t in _config.settingsNpc.Wear)
				{
					Item newItem = ItemManager.CreateByName(t.ShortName, 1, t.SkinId);
					if (newItem == null)
					{
						Debug.LogError($"Failed to create item! ({t.ShortName})");
						continue;
					}

					if (!newItem.MoveToContainer(_npc.inventory.containerWear))
					{
						newItem.Remove();
					}
				}

			#endregion

			if (_config.settingsNpc.soundUse)
			{
				InitializeBoomBox(_npc.transform);
			}
		}

		private void ClearEnt()
		{
			List<BaseEntity> entities = Pool.Get<List<BaseEntity>>();

			Vis.Entities(GetResultVector(), 20f, entities, LayerMask.GetMask("Construction", "Deployable", "Deployed", "Debris", "Default", "Player (Server)"));

			foreach (BaseEntity entity in entities)
			{
				if(entity == null || entity.IsDestroyed || (entity.OwnerID != QUEST_BUILDING_OWNER && !entity.PrefabName.Contains("missionprovider_test")))
					continue;
				entity.Kill();
			}
			
			Pool.FreeUnmanaged(ref entities);

			timer.Once(5f, HandleSpawn);
		}
		
		private bool IsIgnoreClass(BaseEntity entity) => entity is Workbench or BaseVehicle;
		
		private void OnPasteFinished(List<BaseEntity> pastedEntities, string fileName)
		{
			if (!string.Equals(fileName, QUEST_BUILDING_NAME, StringComparison.CurrentCultureIgnoreCase))
				return;

			_houseNpc = pastedEntities;
			try
			{
				foreach (BaseEntity item in _houseNpc)
				{
					if (item == null || item.IsDestroyed || item.transform == null)
						continue;
					
					item.OwnerID = QUEST_BUILDING_OWNER;

					if (item is CeilingLight or SimpleLight or BaseOven or DigitalClock)
						item.SetFlag(BaseEntity.Flags.On, true);
					if (item is ElectricalHeater)
						item.SetFlag(BaseEntity.Flags.Reserved8, true);
					if (item is WeaponRack weaponRack)
						foreach (BaseEntity child in weaponRack.children)
						{
							if (child != null && !child.IsDestroyed)
							{
								child.enableSaving = false;
								BaseEntity.saveList.Remove(child);
							}
						}

					if (item is CCTV_RC cctvRc)
					{
						cctvRc.UpdateIdentifier(_config.settings.CameraId);
						cctvRc.SetFlag(BaseEntity.Flags.Reserved8, true);
						cctvRc.UpdateRCAccess(true);
					}
					
					if (item is NeonSign neonSign)
					{
						neonSign.SetFlag(BaseEntity.Flags.Reserved8, true);
						neonSign.isAnimating = true;
						if (neonSign.isAnimating)
						{
							neonSign.CancelInvoke(neonSign.animationLoopAction);
							neonSign.InvokeRepeating(neonSign.animationLoopAction, 2f, 2f);
						}
						
						DestroyUnneededComponents(neonSign);
						neonSign.SendNetworkUpdate();
					}
					
					if (item.prefabID == 1447082346)
					{
						item.transform.GetPositionAndRotation(out Vector3 positionCopy, out Quaternion rotationCopy);
                        InitializeNpc(positionCopy, rotationCopy);
						
                        item.Kill();
                        continue;
					}

					if (item is DeployableBoomBox boomBox)
					{
						if (_config.settings.useRadio)
						{
							NextTick(() =>
							{
								boomBox.BoxController.CurrentRadioIp = _config.settings.RadioStation;
								boomBox.BoxController.ConditionLossRate = 0;
								boomBox.BoxController.baseEntity.ClientRPC(RpcTarget.NetworkGroup("OnRadioIPChanged"), boomBox.BoxController.CurrentRadioIp);
								if (!boomBox.BoxController.IsOn())
								{
									boomBox.BoxController.ServerTogglePlay(true);
								}

								boomBox.BoxController.baseEntity.SendNetworkUpdate();
							});
						}
					}

					if (item is BuildingBlock buildingBlock)
						buildingBlock.StopBeingRotatable();
					
					if (item is DecayEntity decayEntity)
					{
						decayEntity.decay = null;
						decayEntity.decayVariance = 0;
						decayEntity.ResetUpkeepTime();
						decayEntity.DecayTouch();
						decayEntity.CancelInvoke(nameof(DecayEntity.DecayTick));
						BuildingManager.server.decayEntities.Remove(decayEntity);
					}

					if (item is BaseCombatEntity combatEntity)
					{
						combatEntity.pickup.enabled = false;
					}

					if (item is Door door)
					{
						door.pickup.enabled = false;
						door.canTakeLock = false;
						door.canTakeCloser = false;
					}

					if (!IsIgnoreClass(item))
					{
						item.SetFlag(BaseEntity.Flags.Busy, true);
						item.SetFlag(BaseEntity.Flags.Locked, true);
					}
				}

				PrintWarning("XDQUEST_CopyPasteSuccessfully".GetAdaptedMessage());
			}
			catch (Exception ex)
			{
				PrintError("XDQUEST_BuildingPasteError".GetAdaptedMessage());
				Log(ex.ToString(), "LogError");
			}
		}
		
		private void DestroyUnneededComponents(BaseEntity entity)
		{
			UnityEngine.Object.Destroy(entity.GetComponent<DestroyOnGroundMissing>());
			UnityEngine.Object.Destroy(entity.GetComponent<GroundWatch>());
		}

		#endregion

		#region Scripts

		private class AudioZoneController : FacepunchBehaviour
		{
			private float _zoneRadius;
			private Vector3 _position;

			private DeployableBoomBox _boomBox;
			private Cassette _cassette;
			private Dictionary<AudioTriggerTypes, Dictionary<uint, float>> _sounds = new();

			private Coroutine _coroutine;


			private SphereCollider _sphereCollider;

			private void Awake()
			{
				GameObject o = gameObject;
				o.layer = (int)Layer.Reserved1;
				o.name = "QuestHouseNpc";
				enabled = false;
			}

			public void Activate(Vector3 pos, DeployableBoomBox boomBox, float radius)
			{
				_position = pos;
				_zoneRadius = radius;
				_boomBox = boomBox;
				transform.position = _position;
				SetupCassete();
				UpdateCollider();
				SetupSounds();
				gameObject.SetActive(true);
				enabled = true;
			}

			#region Setup

			private void SetupSounds()
			{
				foreach (NpcSound sound in Instance._cachedSounds.Values)
				{
					uint ids = FileStorage.server.Store(sound.voiceData, FileStorage.Type.ogg, _cassette.net.ID);
					if (!_sounds.ContainsKey(sound.audioType))
					{
						_sounds[sound.audioType] = new Dictionary<uint, float>();
					}

					_sounds[sound.audioType][ids] = sound.durationSeconds;
				}
			}

			private void SetupCassete()
			{
				Item item = ItemManager.CreateByName("cassette");
				_cassette = ItemModAssociatedEntity<Cassette>.GetAssociatedEntity(item);
				_cassette.MaxCassetteLength = 36f;
				item.MoveToContainer(_boomBox.inventory);
				item.MarkDirty();
				_boomBox.OnCassetteInserted(_cassette);
			}

			private void UpdateCollider()
			{
				_sphereCollider = gameObject.GetComponent<SphereCollider>();
				{
					if (_sphereCollider == null)
					{
						_sphereCollider = gameObject.AddComponent<SphereCollider>();
						_sphereCollider.isTrigger = true;
						_sphereCollider.name = "QuestHouseNpc";
					}

					_sphereCollider.radius = _zoneRadius;
				}
			}

			#endregion

			#region TriggerHook

			private void OnTriggerEnter(Collider col)
			{
				BasePlayer player = GetValidPlayer(col);
				if (player != null)
				{
					ProcessPlayerInteraction(player, AudioTriggerTypes.Greeting);
				}
			}

			private void OnTriggerExit(Collider col)
			{
				BasePlayer player = GetValidPlayer(col);
				if (player != null)
				{
					ProcessPlayerInteraction(player, AudioTriggerTypes.Farewell);
					player.SendConsoleCommand("CloseMainUI");
					if (Instance._openMiniQuestListPlayers.Contains(player.userID))
					{
						CuiHelper.DestroyUi(player, MINI_QUEST_LIST);
						Instance.OpenMQL_CMD(player);
					}
				}
			}

			private void OnDestroy()
			{
				if (_coroutine != null) ServerMgr.Instance.StopCoroutine(_coroutine);
				if (_boomBox != null && !_boomBox.IsDestroyed) _boomBox.Kill();

				Destroy(gameObject);
				CancelInvoke();
			}

			#endregion

			#region Help

			private BasePlayer GetValidPlayer(Collider col)
			{
				BasePlayer player = col.GetComponentInParent<BasePlayer>();
				return (player != null && !player.IsNpc && player.userID.IsSteamId()) ? player : null;
			}

			#endregion

			#region Sound

			private void ProcessPlayerInteraction(BasePlayer player, AudioTriggerTypes triggerTypes)
			{
				if (player.IsVisible(Instance._npc.eyes.position))
				{
					StartPlayingSound(triggerTypes);
				}
			}

			public void StartPlayingSound(AudioTriggerTypes triggerTypes)
			{
				if (_coroutine != null) return;
				Dictionary<uint, float> subDictionary;
				if (_sounds.TryGetValue(triggerTypes, out subDictionary))
				{
					List<uint> keys = new List<uint>(subDictionary.Keys);

					uint randomKey = keys[Random.Range(0, keys.Count)];

					float duration;
					if (subDictionary.TryGetValue(randomKey, out duration))
					{
						_coroutine = StartCoroutine(PlaySound(randomKey, duration));
					}
					else
					{
						Debug.LogError($"No duration found for sound key {randomKey}");
					}
				}
				else
				{
					Debug.LogError($"No sounds found for trigger type {triggerTypes}");
				}
			}

			private IEnumerator PlaySound(uint randomKey, float duration)
			{
				_cassette.SetAudioId(randomKey, (ulong)Random.Range(76561197960265728, 76561199999999999));
				yield return CoroutineEx.waitForSeconds(1f);

				_boomBox.UpdateFromInput(1, 1);
				yield return CoroutineEx.waitForSeconds(duration);
				StopSound();
			}

			private void StopSound()
			{
				_boomBox.UpdateFromInput(0, 1);

				if (_coroutine != null)
				{
					StopCoroutine(_coroutine);
					_coroutine = null;
				}
			}

			#endregion
		}

		private class SafeZone : MonoBehaviour
		{
			private Vector3 _position;
			private float _radius;

			public void Activate(Vector3 pos, float radius)
			{
				_position = pos;
				_radius = radius;
				transform.position = _position;
				UpdateCollider();

				TriggerSafeZone safeZone = gameObject.GetComponent<TriggerSafeZone>();
				safeZone = safeZone ? safeZone : gameObject.AddComponent<TriggerSafeZone>();
				safeZone.maxAltitude = 10;
				safeZone.maxDepth = 1;
				safeZone.interestLayers = Layers.Mask.Player_Server;
				safeZone.enabled = true;
			}

			private void UpdateCollider()
			{
				if (!gameObject.TryGetComponent(out SphereCollider sphereCollider))
				{
					sphereCollider = gameObject.AddComponent<SphereCollider>();
					sphereCollider.gameObject.layer = 18;
					sphereCollider.isTrigger = true;
				}

				sphereCollider.radius = _radius;
			}
		}

		#endregion

		#region MapMarkers

		private void CreateMapMarker(Vector3 pos)
		{
			Interface.CallHook("API_CreateMarker", pos, Name, 0, 3f, 0.2f, _config.mapSettings.nameMarkerMap, _config.mapSettings.colorMarker, _config.mapSettings.colorOutline);
		}

		private void DeleteMapMarker()
		{
			Interface.CallHook("API_RemoveMarker", Name);
		}

		#endregion

		#region Hooks

		#region QuestHook

		#region Type Upgrade

		private object OnStructureUpgrade(BaseCombatEntity entity, BasePlayer player, BuildingGrade.Enum grade)
		{
			QuestProgress(player.userID, QuestType.Grade, ((int)grade).ToString());
			return null;
		}

		#endregion

		#region IQPlagueSkill

		private void StudySkill(BasePlayer player, string name)
		{
			QuestProgress(player.userID, QuestType.IQPlagueSkill, name);
		}

		#endregion

		#region HeadReward

		private void KillHead(BasePlayer player)
		{
			QuestProgress(player.userID, QuestType.IQHeadReward);
		}

		#endregion

		#region IqCase

		private void OnOpenedCase(BasePlayer player, string name)
		{
			QuestProgress(player.userID, QuestType.IQCases, name);
		}

		#endregion

		#region Chinook

		private void LootHack(BasePlayer player)
		{
			QuestProgress(player.userID, QuestType.XDChinookIvent);
		}

		#endregion

		#region Gather

		#region GatherFix

		private void GatherHooksSub()
		{
			foreach (string hook in _gatherHooks.Concat(_gatherHooksSkillTree))
				Unsubscribe(hook);
		
			if (_config.settings.UseSkillTreeIgnoreHooks)
			{
				foreach (string hook in _gatherHooksSkillTree)
					Subscribe(hook);
			}
			else
			{
				foreach (string hook in _gatherHooks)
					Subscribe(hook);
			}
		}

		private string[] _gatherHooks =
		{
			"OnCollectiblePickedup",
			"OnDispenserGathered",
			"OnDispenserBonusReceived",
		};

		private string[] _gatherHooksSkillTree =
		{
			"STCanReceiveYield",
			"OnSkillTreeHandleDispenser",
		};
		
		
		#endregion

		private void OnDispenserGathered(ResourceDispenser dispenser, BasePlayer player, Item item)
		{
			if(player == null) return;
			QuestProgress(player.userID, QuestType.Gather, item.info.shortname, "", null, item.amount);
		}
		
		private void OnDispenserBonusReceived(ResourceDispenser dispenser, BasePlayer player, Item item) => OnDispenserGathered(dispenser, player, item);

		private void OnCollectiblePickedup(CollectibleEntity collectible, BasePlayer player, Item item)
		{
			if (player == null || item == null)
				return;
			
			QuestProgress(player.userID, QuestType.Gather, item.info.shortname, "", null, item.amount);
		}

		private void STCanReceiveYield(BasePlayer player, GrowableEntity entity, Item item)
		{
			if (player == null || item == null || item.info == null) return;
			QuestProgress(player.userID, QuestType.Gather, item.info.shortname, "", null, item.amount);
		}

		private void STCanReceiveYield(BasePlayer player, CollectibleEntity entity, ItemAmount ia)
		{
			if (player == null || ia == null || ia.itemDef == null) return;
			QuestProgress(player.userID, QuestType.Gather, ia.itemDef.shortname, "", null, (int)ia.amount);
		}

		private void OnSkillTreeHandleDispenser(BasePlayer player, BaseEntity entity, Item item)
		{
			if (player == null || item == null || item.info == null) return;
			QuestProgress(player.userID, QuestType.Gather, item.info.shortname, "", null, item.amount);
		}


		#endregion

		#region Craft

		private void OnItemCraftFinished(ItemCraftTask task, Item item, ItemCrafter crafter)
		{
			QuestProgress(crafter.owner.userID, QuestType.Craft, task.blueprint.targetItem.shortname, "", null, item.amount);
		}

		#endregion

		#region Research

		private void OnTechTreeNodeUnlock(Workbench workbench, TechTreeData.NodeInstance node, BasePlayer player)
		{
			QuestProgress(player.userID, QuestType.Research, node.itemDef.shortname);
		}

		private void OnItemResearch(ResearchTable table, Item targetItem, BasePlayer player)
		{
			QuestProgress(player.userID, QuestType.Research, targetItem.info.shortname);
		}

		#endregion

		#region Deploy

		private void OnEntityBuilt(Planner plan, GameObject go)
		{
			if(plan == null) return;
			BasePlayer player = plan.GetOwnerPlayer();
			if (player == null || go == null || plan.GetItem() == null)
			{
				return;
			}
			BaseEntity ent = go.ToBaseEntity();
			if (ent == null || ent.skinID == 11543256361)
			{
				return;
			}
			
			QuestProgress(player.userID, QuestType.Deploy, plan.GetItem().info.shortname);
		}

		#endregion
		
		#region Loot

		#region OnLootEntity

		private HashSet<ulong> Looted = new();
		
		private void OnEntityDestroy(BaseEntity entity)
		{
			if (entity == null) return;
			ulong net = entity.net?.ID.Value ?? 0;
			if (Looted.Contains(net))
				Looted.Remove(net);
		}
		private void OnLootEntity(BasePlayer player, BaseEntity entity)
		{
			if (entity == null || player == null)
				return;
			ulong netId = entity.net?.ID.Value ?? 0;
			if (!Looted.Add(netId))
				return;


			switch (entity)
			{
				case LootContainer lootContainer:
					if (lootContainer.inventory != null)
						QuestProgress(player.userID, QuestType.Loot, "", "", lootContainer.inventory.itemList);
					break;
				
				case LootableCorpse lootableCorpse:
					if(lootableCorpse.playerSteamID.IsSteamId())
						return;

					if (lootableCorpse.containers != null)
					{
						foreach (ItemContainer container in lootableCorpse.containers)
							if (container != null)
								QuestProgress(player.userID, QuestType.Loot, "", "", container.itemList);
					}
					break;
				
				case DroppedItemContainer droppedItemContainer:
					if(droppedItemContainer.prefabID != 1519640547 || droppedItemContainer.playerSteamID.IsSteamId())
						return;

					if (droppedItemContainer.inventory != null)
						QuestProgress(player.userID, QuestType.Loot, "", "", droppedItemContainer.inventory.itemList);
					break;
			}
		}
		
		private void OnContainerDropItems(ItemContainer container)
		{
			if (container == null || container.entityOwner == null)
				return;

			string prefabName = container.entityOwner.ShortPrefabName;
			if (prefabName == null || (!prefabName.Contains("barrel") && !prefabName.Contains("roadsign")))
				return;

			if (container.entityOwner is LootContainer lootContainer)
			{
				ulong netId = lootContainer.net?.ID.Value ?? 0;
				if (!Looted.Add(netId))
					return;

				if (lootContainer.lastAttacker is BasePlayer player)
				{
					QuestProgress(player.userID, QuestType.Loot, "", "", lootContainer.inventory.itemList);
				}
			}
		}

		#endregion

		#endregion

		#region Swipe

		private void OnCardSwipe(CardReader cardReader, Keycard card, BasePlayer player)
		{
			if (card == null || cardReader == null || player == null) return;
			if (!cardReader.HasFlag(BaseEntity.Flags.On) && card.accessLevel == cardReader.accessLevel)
				QuestProgress(player.userID, QuestType.Swipe, card.accessLevel.ToString());
		}

		#endregion

		#region EntityKill/взорвать/уничтожить что либо
		
		private void OnPlayerDeath(BasePlayer player, HitInfo info)
		{
			if (player == null || info == null || !player.userID.IsSteamId())
				return;
			BasePlayer attacker = info.InitiatorPlayer;
			if (attacker == null)
				return;

			if (IsFriends(player.userID.Get(), attacker.userID.Get()) || IsClans(player.UserIDString, attacker.UserIDString) || IsDuel(attacker.userID.Get()) || player.userID == attacker.userID)
				return;

			QuestProgress(attacker.userID, QuestType.EntityKill, "player");
		}
		
		private Dictionary<NetworkableId, ulong> heliCashed = new();
		private void OnPatrolHelicopterKill(PatrolHelicopter entity, HitInfo info)
		{
			if (entity == null || info == null || info.InitiatorPlayer == null)
				return;

			BasePlayer player = info.InitiatorPlayer;
			if (player.userID.IsSteamId())
			{
				heliCashed[entity.net.ID] = player.userID;
			}
		}
        
		private void OnEntityKill(PatrolHelicopter entity)
		{
			if (entity == null || entity.net == null)
				return;

			if (heliCashed.TryGetValue(entity.net.ID, out ulong playerId))
			{
				QuestProgress(playerId, QuestType.EntityKill, entity.ShortPrefabName.ToLowerInvariant());
				heliCashed.Remove(entity.net.ID);
			}
			else
			{
				if (entity.myAI != null && entity.myAI._targetList is { Count: > 0 } targetList)
				{
					BasePlayer player = targetList[^1].ply;

					if (player != null && player.userID.IsSteamId())
					{
						QuestProgress(player.userID, QuestType.EntityKill, entity.ShortPrefabName.ToLowerInvariant());
					}
				}
			}
		}
		
		private static List<string> excludedNames = new()
		{
			"corpse", "servergibs", "player", "rug.bear.deployed"
		};
		
		private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
		{
			try
			{ 
				if (entity == null || info == null)
					return;

				string targetName = entity.ShortPrefabName;
				
				if (excludedNames.Contains(targetName))
					return;

				if (targetName == "testridablehorse")
					targetName = "horse";

				BasePlayer player = info.InitiatorPlayer;

				if (entity.GetComponent<PatrolHelicopter>() != null)
					return;
        
				if (player != null && !player.IsNpc && entity.ToPlayer() != player)
					QuestProgress(player.userID, QuestType.EntityKill, targetName.ToLower());
			}
			catch (Exception ex)
			{
				Debug.LogError($"Ошибка при обработке смерти сущности: {ex.Message}");
			}
		}


		#endregion

		#region Покупки у НПС
		
		void OnCustomVendingSetupGiveSoldItem(NPCVendingMachine machine, Item soldItem, BasePlayer buyer)
		{
			QuestProgress(buyer.userID, QuestType.PurchaseFromNpc, soldItem.info.shortname, "", null, soldItem.amount);
		}

		void OnNpcGiveSoldItem(NPCVendingMachine machine, Item soldItem, BasePlayer buyer)
		{
			if (CustomVendingSetup?.Call("API_IsCustomized", machine) is true)
				return;

			QuestProgress(buyer.userID, QuestType.PurchaseFromNpc, soldItem.info.shortname, "", null, soldItem.amount);
		}

		#endregion

		#region Взлом ящика

		private void OnCrateHack(HackableLockedCrate crate)
		{
			if (crate.originalHackerPlayerId.IsSteamId())
			{
				QuestProgress(crate.originalHackerPlayerId, QuestType.HackCrate);
			}
		}

		#endregion

		#region RecycleItem (Игрок не должен выходить из интерфейса переработчика)

		private Dictionary<ulong, BasePlayer> _recyclePlayer = new();

		private void OnRecyclerToggle(Recycler recycler, BasePlayer player)
		{
			if (!recycler.IsOn())
			{
				if (!_recyclePlayer.TryAdd(recycler.net.ID.Value, player))
				{
					_recyclePlayer.Remove(recycler.net.ID.Value);
					_recyclePlayer.Add(recycler.net.ID.Value, player);
				}
			}
			else if (_recyclePlayer.ContainsKey(recycler.net.ID.Value))
			{
				_recyclePlayer.Remove(recycler.net.ID.Value);
			}
		}
		
		private void OnItemRecycle(Item item, Recycler recycler)
		{
			BasePlayer value;
			if (_recyclePlayer.TryGetValue(recycler.net.ID.Value, out value))
			{
				int num2 = 1;
				if (item.amount > 1)
				{
					num2 = Mathf.CeilToInt(Mathf.Min(item.amount, item.info.stackable * 0.1f));
				}
				QuestProgress(value.userID, QuestType.RecycleItem, item.info.shortname, "", null, num2);
			}
		}

		#endregion

		#region Growseedlings

		private void OnGrowableGathered(GrowableEntity plant, Item item, BasePlayer player)
		{
			QuestProgress(player.userID, QuestType.Growseedlings, item.info.shortname, "", null, item.amount);
		}

		#endregion

		#region Raidable Bases (Nivex)

		private void OnRaidableBaseCompleted(Vector3 location, int mode, bool allowPVP, string id, float spawnTime, float despawnTime, float loadingTime, ulong ownerId, BasePlayer owner,
			List<BasePlayer> raiders)
		{
			BasePlayer player = owner ? owner : (raiders?.Count != 0 ? raiders[0] : null);
			if (player != null)
			{
				QuestProgress(player.userID, QuestType.RaidableBases, mode.ToString(), "", null);
			}
		}

		#endregion

		#region Fishing

		private void OnFishCatch(Item fish, BaseFishingRod fishingRod, BasePlayer player)
		{
			if (player == null || fish == null)
				return;

			QuestProgress(player.userID, QuestType.Fishing, fish.info.shortname, "", null, fish.amount);
		}

		#endregion

		#region BossMonster

		private void OnBossKilled(ScientistNPC boss, BasePlayer attacker)
		{
			if (boss == null || attacker == null)
				return;

			QuestProgress(attacker.userID, QuestType.BossMonster, boss.displayName, "", null);
		}

		#endregion

		#region HarborEvent

		private void OnHarborEventWinner(ulong winnerId)
		{
			QuestProgress(winnerId, QuestType.HarborEvent);
		}

		#endregion

		#region SatelliteDishEvent

		private void OnSatDishEventWinner(ulong winnerId)
		{
			QuestProgress(winnerId, QuestType.SatelliteDishEvent);
		}

		#endregion

		#region Sputnik

		private void OnSputnikEventWin(ulong userID)
		{
			QuestProgress(userID, QuestType.Sputnik);
		}

		#endregion

		#region AbandonedBases

		private void OnAbandonedBaseEnded(Vector3 center, bool allowPVP, List<BasePlayer> intruders)
		{
			if (intruders.Count <= 0)
				return;

			foreach (BasePlayer player in intruders)
			{
				QuestProgress(player.userID, QuestType.AbandonedBases);
			}
		}

		#endregion

		#region IQDronePatrol

		private void OnDroneKilled(BasePlayer player, Drone drone, string KeyDrone)
		{
			if (player == null || drone == null)
				return;

			QuestProgress(player.userID, QuestType.IQDronePatrol, KeyDrone, "", null);
		}

		#endregion

		#region IQDefenderSupply

		private void OnLootedDefenderSupply(BasePlayer player, int levelDropInt)
		{
			if (player == null)
				return;
			
			QuestProgress(player.userID, QuestType.IQDefenderSupply, levelDropInt.ToString(), "", null);
		}

		#endregion

		#region GasStationEvent

		private void OnGasStationEventWinner(ulong userID)
		{
			QuestProgress(userID, QuestType.GasStationEvent);
		}

		#endregion

		#region Triangulation 

		private void OnTriangulationWinner(ulong userID)
		{
			QuestProgress(userID, QuestType.Triangulation);
		}

		#endregion

		#region FerryTerminalEvent

		private void OnFerryTerminalEventWinner(ulong userID)
		{
			QuestProgress(userID, QuestType.FerryTerminalEvent);
		}

		#endregion

		#region Convoy

		private void OnConvoyEventWin(ulong userID)
		{
			QuestProgress(userID, QuestType.Convoy);
		}

		#endregion

		#region Caravan
		
		private void OnCaravanEventWin(ulong userID)
		{
			QuestProgress(userID, QuestType.Caravan);
		}

		#endregion

		#endregion

		private object OnNpcConversationStart(NPCTalking npcTalking, BasePlayer player, ConversationData conversationData)
		{
			if (npcTalking != _npc || player == null) return null;
			MainUi(player);
			return false;
		}

		private void OnNewSave()
		{
			if (_config.settings.useWipe)
			{
				_playersInfo?.Clear();
				SaveData();
			}

			if (_config.settings.useWipePermission)
			{
				ClearPermission();
			}
		}


		private void Init()
		{
			Instance = this;
			LoadPlayerData();
			LoadQuestStatisticsData();
			LoadQuestData();
		}
		
		private void OnServerInitialized()
		{
			_monument = TerrainMeta.Path.Monuments.FirstOrDefault(p => p.name.ToLower().Contains("compound") && p.IsSafeZone);

			if (!ImageLibrary)
			{
				UnloadWithMessage("ERROR! Plugin ImageLibrary not found!");
				return;
			}

			if (!CopyPaste)
			{
				UnloadWithMessage("Check if you have the 'Copy Paste' plugin installed");
				return;
			}

			if (CopyPaste.Version < new VersionNumber(4, 2, 0))
			{
				UnloadWithMessage("You have an old version of Copy Paste!\nplease update the plugin to the latest version (4.1.37 or higher) - https://umod.org/plugins/copy-paste");
				return;
			}

			if (_monument == null && !_config.spawnSettings.UseCustomPosition)
			{
				UnloadWithMessage("XDQUEST_MissingOutPost".GetAdaptedMessage());
				return;
			}

			if (_questList.Count == 0)
			{
				PrintError("XDQUEST_MissingQuests".GetAdaptedMessage());
				return;
			}

			foreach (string cmds in _config.settings.questListProgress)
				cmd.AddChatCommand(cmds, this, nameof(OpenMQL_CMD));

			DownloadImages();
			LoadDataCopyPaste();
			LoadDataSounds();
			GatherHooksSub();

			_imageUI = new ImageUI();
			_imageUI.DownloadImage();

			foreach (BasePlayer player in BasePlayer.activePlayerList)
				OnPlayerConnected(player);
            
			QuestCooldownsTimer = timer.Every(70f, CheckQuestCooldowns);

			if (_config.statisticsCollectionSettings.useStatistics && !string.IsNullOrEmpty(_config.statisticsCollectionSettings.discordWebhookUrl))
			{
				timer.Every(_config.statisticsCollectionSettings.publishFrequency, GrabAndPostStatistics);
			}
		}

		private void CheckQuestCooldowns()
		{
			foreach (BasePlayer player in BasePlayer.activePlayerList)
				CheckPlayerCooldowns(player);
		}

		private void CheckPlayerCooldowns(BasePlayer player)
		{
			PlayerData playerData;
			if (_playersInfo.TryGetValue(player.userID, out playerData))
			{
				List<long> questsToRemove = Pool.Get<List<long>>();

				foreach (KeyValuePair<long, double> cooldownForQuest in playerData.PlayerQuestCooldowns)
				{
					if (CurrentTime() >= cooldownForQuest.Value + 30f)
					{
						questsToRemove.Add(cooldownForQuest.Key);

						if (_questList.TryGetValue(cooldownForQuest.Key, out Quest quest))
						{
							string userId = player.UserIDString;
							SendChat(player, "XDQUEST_REPEATABLE_QUEST_AVAILABLE_AGAIN".GetAdaptedMessage(userId, quest.GetDisplayName(lang.GetLanguage(userId))));
						}
					}
				}

				foreach (long questId in questsToRemove)
					playerData.PlayerQuestCooldowns.Remove(questId);
				
				Pool.FreeUnmanaged(ref questsToRemove);
			}
		}

		private void OnPlayerConnected(BasePlayer player)
		{
			ulong UserId = player.userID.Get();
			PlayerData playerData;
			if (!_playersInfo.TryGetValue(UserId, out playerData))
			{
				_playersInfo.Add(UserId, new PlayerData());
			}
			else
			{
				List<PlayerQuest> questsToRemove = new();

				foreach (PlayerQuest item in playerData.CurrentPlayerQuests)
				{
					KeyValuePair<long, Quest>? currentQuest = null;

					foreach (KeyValuePair<long, Quest> pair in _questList)
					{
						if (pair.Key == item.ParentQuestID && pair.Value.QuestType == item.ParentQuestType)
						{
							currentQuest = pair;
							break;
						}
					}

					if (currentQuest?.Value == null)
					{
						questsToRemove.Add(item);
					}
				}

				NextTick(() =>
				{
					foreach (PlayerQuest questToRemove in questsToRemove)
					{
						playerData.CurrentPlayerQuests.Remove(questToRemove);
					}

					CheckPlayerCooldowns(player);
				});
			}
		}

		private void OnServerSave()
		{
			timer.Once(10f, SaveData);
		}

		private void OnPlayerDisconnected(BasePlayer player)
		{
			ulong UserId = player.userID.Get();

			_openMiniQuestListPlayers.Remove(UserId);
		}

		private void OnServerShutdown() => Unload();

		private void Unload()
		{
			if (IsObjectNull(Instance))
				return;

			if (!IsObjectNull(QuestCooldownsTimer))
			{
				QuestCooldownsTimer.Destroy();
			}

			if (_config.mapSettings.mapUse)
				DeleteMapMarker();
			if (_imageUI != null)
			{
				_imageUI.UnloadImages();
				_imageUI = null;
			}

			Instance = null;
			QuestCooldownsTimer = null;
			SaveData();
			DestroyObjects();
			RemoveHouseNpCs();
			if (_npc != null)
				_npc.KillMessage();
			
			ClearPlayersData();
		}

	    private	void OnEntityTakeDamage(BuildingBlock victim, HitInfo info)
		{
			if (victim != null && victim.OwnerID == QUEST_BUILDING_OWNER)
			{
				info?.damageTypes.ScaleAll(0);
			}
		}
		
		private object CanBuild(Planner planner, Construction prefab, Construction.Target target)
		{
			if (target.entity == null) return null;
			if (target.entity.OwnerID == QUEST_BUILDING_OWNER)
				return false;
			return null;
		}


		private	object CanAffordUpgrade(BasePlayer player, BuildingBlock block, BuildingGrade.Enum grade)
		{
			if (block.OwnerID == QUEST_BUILDING_OWNER)
				return false;
			return null;
		}

		#endregion

		#region HelpMetods

		#region HelpUnload

		private void UnloadWithMessage(string message)
		{
			NextTick(() =>
			{
				PrintError(message);
				Interface.Oxide.UnloadPlugin(Name);
			});
		}

		private void DestroyObjects()
		{
			if (_audioZoneController != null)
				UnityEngine.Object.DestroyImmediate(_audioZoneController);

			if (_safeZone != null)
				UnityEngine.Object.DestroyImmediate(_safeZone);
		}

		private void RemoveHouseNpCs()
		{
			foreach (BaseEntity entity in _houseNpc)
			{
				if (entity != null && !entity.IsDestroyed)
					entity.Kill();
			}
		}

		private void ClearPlayersData()
		{
			foreach (BasePlayer p in BasePlayer.activePlayerList)
			{
				CuiHelper.DestroyUi(p, MINI_QUEST_LIST);
				CuiHelper.DestroyUi(p, LAYERS);
			}
		}

		#endregion

		private static bool IsObjectNull(object obj) => ReferenceEquals(obj, null);

		private static string GetFileNameWithoutExtension(string filePath)
		{
			int lastDirectorySeparatorIndex = filePath.LastIndexOfAny(new[] { '\\', '/' });
			int lastDotIndex = filePath.LastIndexOf('.');

			if (lastDotIndex > lastDirectorySeparatorIndex)
			{
				return filePath.Substring(lastDirectorySeparatorIndex + 1, lastDotIndex - lastDirectorySeparatorIndex - 1);
			}

			return filePath.Substring(lastDirectorySeparatorIndex + 1);
		}

		private void RunEffect(BasePlayer player, string path)
		{
			Effect effect = new Effect();
			Transform transform = player.transform;
			effect.Init(Effect.Type.Generic, transform.position, transform.forward);
			effect.pooledString = path;
			EffectNetwork.Send(effect, player.net.connection);
		}
		
		private void ClearPermission()
		{
			string[] allPermissions = permission.GetPermissions();
			const string permissionPrefix = "XDQuest.";

			foreach (string perm in allPermissions)
			{
				if (perm.Equals($"{permissionPrefix}default", StringComparison.OrdinalIgnoreCase))
					continue;

				if (perm.StartsWith(permissionPrefix, StringComparison.OrdinalIgnoreCase))
				{
					string[] usersWithPermission = permission.GetPermissionUsers(perm);

					foreach (string userEntry in usersWithPermission)
					{
						string steamId = ExtractSteamId(userEntry);
						permission.RevokeUserPermission(steamId, perm);
					}
				}
			}
		}

		private string ExtractSteamId(string userEntry)
		{
			int separatorIndex = userEntry.IndexOf('(');
			return separatorIndex > 0 ? userEntry[..separatorIndex] : userEntry;
		}

		private static class TimeHelper
		{
			public static string FormatTime(TimeSpan time, int maxSubstr = 5, string language = "ru")
			{
				return language == "ru" ? FormatTimeRussian(time, maxSubstr) : FormatTimeDefault(time);
			}

			private static string FormatTimeRussian(TimeSpan time, int maxSubstr)
			{
				List<string> substrings = new();

				if (time.Days != 0 && substrings.Count < maxSubstr)
				{
					substrings.Add(Format(time.Days, "д"));
				}

				if (time.Hours != 0 && substrings.Count < maxSubstr)
				{
					substrings.Add(Format(time.Hours, "ч"));
				}

				if (time.Minutes != 0 && substrings.Count < maxSubstr)
				{
					substrings.Add(Format(time.Minutes, "м"));
				}

				if (time.Days == 0 && time.Seconds != 0 && substrings.Count < maxSubstr)
				{
					substrings.Add(Format(time.Seconds, "с"));
				}

				if (substrings.Count == 0)
				{
					substrings.Add("0с");
				}

				return string.Join(" ", substrings);
			}

			private static string FormatTimeDefault(TimeSpan time)
			{
				List<string> parts = new List<string>();

				if (time.Days > 0)
				{
					parts.Add($"{time.Days} day{(time.Days == 1 ? string.Empty : "s")}");
				}

				if (time.Hours > 0)
				{
					parts.Add($"{time.Hours} hour{(time.Hours == 1 ? string.Empty : "s")}");
				}

				if (time.Minutes > 0)
				{
					parts.Add($"{time.Minutes} minute{(time.Minutes == 1 ? string.Empty : "s")}");
				}

				if (time.Seconds > 0)
				{
					parts.Add($"{time.Seconds} second{(time.Seconds == 1 ? string.Empty : "s")}");
				}

				if (parts.Count == 0)
				{
					parts.Add("0 seconds");
				}

				return string.Join(", ", parts);
			}

			private static string Format(int units, string form)
			{
				return $"{units}{form}";
			}
		}

		private static double CurrentTime()
		{
			return Facepunch.Math.Epoch.Current;
		}

		private Vector3 GetResultVector()
		{
			if (_config.spawnSettings.UseCustomPosition)
			{
				return _config.spawnSettings.CustomPosition.Position;
			}

			Transform transform = _monument.transform;
			return transform.position + transform.rotation * _config.spawnSettings.MonumentPosition.Position;
		}

		private float GetResultRotation()
		{
			if (_config.spawnSettings.UseCustomPosition)
			{
				return -DegreeToRadian(_config.spawnSettings.CustomPosition.Rotation);
			}

			float cityAngleRad = DegreeToRadian(_monument.transform.rotation.eulerAngles.y);
			float myAngleRad = DegreeToRadian(_config.spawnSettings.MonumentPosition.Rotation);
			return cityAngleRad - myAngleRad;
		}

		private float DegreeToRadian(float angle)
		{
			return (float)(Math.PI * angle / 180.0f);
		}
		private float RadianToDegree(float radians)
		{
			return (float)(radians * 180.0f / Math.PI);
		}

		private void Log(string msg, string file)
		{
			LogToFile(file, $"[{DateTime.Now}] {msg}", this);
		}

		#endregion

		#region NewUi

		private List<ulong> _openMiniQuestListPlayers = new();
		private const string MINI_QUEST_LIST = "Mini_QuestList";
		private const string LAYERS = "UI_QuestMain";
		private const string LAYER_MAIN_BACKGROUND = "UI_QuestMainBackground";
		private const string XDQUEST_CATEGORY_MAIN = "XDQUEST_CATEGORY_MAIN";

		#region MainUI

		private void MainUi(BasePlayer player)
		{
			CuiElementContainer container = new CuiElementContainer
			{
				{
					new CuiPanel
					{
						CursorEnabled = true,
						Image = { Color = "1 1 1 0" },
						RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
					},
					"OverlayNonScaled",
					LAYERS
				},


				new CuiElement
				{
					Name = LAYER_MAIN_BACKGROUND,
					Parent = LAYERS,
					Components =
					{
						new CuiRawImageComponent { Color = "1 1 1 1", Png = _imageUI.GetImage("1") },
						new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
					}
				},

				new CuiElement
				{
					Name = "CloseUIImage",
					Parent = LAYER_MAIN_BACKGROUND,
					Components =
					{
						new CuiRawImageComponent { Color = "1 1 1 1", Png = _imageUI.GetImage("2") },
						new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "96.039 87.558", OffsetMax = "135.315 114.647" }
					}
				},

				{
					new CuiButton
					{
						Button = { Color = "1 1 1 0", Command = "CloseMainUI" },
						Text = { Text = "", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0 0 0 1" },
						RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "96.039 87.558", OffsetMax = "135.315 114.647" }
					},
					LAYER_MAIN_BACKGROUND,
					"BtnCloseUI"
				},

				{
					new CuiLabel
					{
						RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "96.227 191.4", OffsetMax = "208.973 211.399" },
						Text =
						{
							Text = "XDQUEST_UI_TASKLIST".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft,
							Color = "0.7169812 0.7169812 0.7169812 1"
						}
					},
					LAYER_MAIN_BACKGROUND,
					"LabelQuestList"
				},

				{
					new CuiLabel
					{
						RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-269.184 -102.227", OffsetMax = "-197.242 -72.373" },
						Text =
						{
							Text = "XDQUEST_UI_Awards".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1"
						}
					},
					LAYER_MAIN_BACKGROUND,
					"PrizeTitle"
				},

				{
					new CuiLabel
					{
						RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "250.187 191.399", OffsetMax = "350.187 211.401" },
						Text =
						{
							Text = "XDQUEST_UI_TASKCount".GetAdaptedMessage(player.UserIDString, 0), Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleRight,
							Color = "1 1 1 1"
						}
					},
					LAYER_MAIN_BACKGROUND,
					"LabelQuestCount", "LabelQuestCount"
				}
			};

			CuiHelper.DestroyUi(player, "UI_QuestMain");
			CuiHelper.AddUi(player, container);
			Category(player, UICategory.Available);
			QuestListUI(player, UICategory.Available);
			QuestInfo(player, 0, UICategory.Available);
		}

		private void UpdateTasksCount(BasePlayer player, int count)
		{
			CuiElementContainer container = new CuiElementContainer
			{
				{
					new CuiLabel
					{
						RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "250.187 191.399", OffsetMax = "350.187 211.401" },
						Text =
						{
							Text = "XDQUEST_UI_TASKCount".GetAdaptedMessage(player.UserIDString, count), Font = "robotocondensed-regular.ttf", FontSize = 14,
							Align = TextAnchor.MiddleRight, Color = "1 1 1 1"
						}
					},
					LAYER_MAIN_BACKGROUND,
					"LabelQuestCount", "LabelQuestCount"
				}
			};
			CuiHelper.AddUi(player, container);
		}

		#endregion

		#region Category

		private enum UICategory
		{
			Available,
			Taken,
		}

		private List<Quest> GetQuestsByCategory(UICategory category, ulong playerId)
		{
			List<Quest> result = new List<Quest>();

			PlayerData playerData = _playersInfo[playerId];
			if (playerData == null)
			{
				return result;
			}

			switch (category)
			{
				case UICategory.Available:
					foreach (Quest quest in _questList.Values)
					{
						if (!string.IsNullOrEmpty(quest.QuestPermission) && !permission.UserHasPermission(playerId.ToString(), $"{Name}." + quest.QuestPermission)) continue;

						bool isQuestAlreadyTaken = playerData.CurrentPlayerQuests.Exists(pq => pq.ParentQuestID == quest.QuestID);
						bool isQuestCd = playerData.PlayerQuestCooldowns.ContainsKey(quest.QuestID);
						bool isQuestAlreadyFinish = playerData.CompletedQuestIds.Contains(quest.QuestID);

						if (!isQuestAlreadyTaken && !isQuestAlreadyFinish && !isQuestCd)
						{
							result.Add(quest);
						}
					}

					break;

				case UICategory.Taken:
					foreach (PlayerQuest playerQuest in playerData.CurrentPlayerQuests)
					{
						Quest value;
						if (_questList.TryGetValue(playerQuest.ParentQuestID, out value))
						{
							result.Add(value);
						}
					}

					foreach (long questId in playerData.PlayerQuestCooldowns.Keys)
					{
						Quest value;
						if (_questList.TryGetValue(questId, out value))
						{
							result.Add(value);
						}
					}

					break;
			}

			return result;
		}

		private void Category(BasePlayer player, UICategory category)
		{
			string color1 = category == UICategory.Available ? "0.4509804 0.5529412 0.2705882 0.8392157" : "0.6431373 0.6509804 0.654902 0.4";
			string color2 = category == UICategory.Taken ? "0.4509804 0.5529412 0.2705882 0.8392157" : "0.6431373 0.6509804 0.654902 0.4";
			string img = _imageUI.GetImage("16");
			CuiElementContainer container = new CuiElementContainer();

			container.Add(new CuiPanel
			{
				CursorEnabled = false,
				Image = { Color = "1 1 1 0" },
				RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-541.65 234.6", OffsetMax = "-284.33 337.6" }
			}, LAYER_MAIN_BACKGROUND, XDQUEST_CATEGORY_MAIN, XDQUEST_CATEGORY_MAIN);

			container.Add(new CuiElement
			{
				Name = "XDQUEST_CATEGORY_SPRITE",
				Parent = XDQUEST_CATEGORY_MAIN,
				Components =
				{
					new CuiRawImageComponent { Color = "0.7529412 0.5137255 0.04705882 1", Sprite = "assets/icons/Favourite_active.png", Material = "assets/icons/iconmaterial.mat", },
					new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "1.3 -22.5", OffsetMax = "21.3 -2.5" }
				}
			});

			container.Add(new CuiLabel
			{
				RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-103.66 -25", OffsetMax = "125.915 0" },
				Text =
				{
					Text = "XDQUEST_UI_CATEGORY".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft,
					Color = "0.7169812 0.7169812 0.7169812 1"
				}
			}, XDQUEST_CATEGORY_MAIN, "XDQUEST_CATEGORY_TITLE");


			container.Add(new CuiElement
			{
				Name = "XDQUEST_CATEGORY_BTN_1",
				Parent = XDQUEST_CATEGORY_MAIN,
				Components =
				{
					new CuiRawImageComponent { Color = color1, Png = img },
					new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "0 4.8", OffsetMax = "150 22.8" }
				}
			});

			container.Add(new CuiButton
			{
				RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
				Button = { Color = "0 0 0 0", Command = category == UICategory.Available ? "" : $"UI_Handler category {UICategory.Available.ToString()}" },
				Text =
				{
					Text = "XDQUEST_UI_CATEGORY_ONE".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1"
				}
			}, "XDQUEST_CATEGORY_BTN_1");

			container.Add(new CuiElement
			{
				Name = "XDQUEST_CATEGORY_BTN_2",
				Parent = XDQUEST_CATEGORY_MAIN,
				Components =
				{
					new CuiRawImageComponent { Color = color2, Png = img },
					new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "0 -17.2", OffsetMax = "150 0.8" }
				}
			});

			container.Add(new CuiButton
			{
				RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
				Button = { Color = "0 0 0 0", Command = category == UICategory.Taken ? "" : $"UI_Handler category {UICategory.Taken.ToString()}" },
				Text =
				{
					Text = "XDQUEST_UI_CATEGORY_TWO".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1"
				}
			}, "XDQUEST_CATEGORY_BTN_2");

			CuiHelper.DestroyUi(player, XDQUEST_CATEGORY_MAIN);
			CuiHelper.AddUi(player, container);
		}

		#endregion

		#region QuestList

		private void AddPageButton(string direction, string parentId, string command, CuiElementContainer container)
		{
			string buttonName = direction == "UP" ? "UPBTN" : "DOWNBTN";
			string imageName = direction == "UP" ? "3" : "4";
			string offsetMin = direction == "UP" ? "182.89 87.565" : "139.598 87.568";
			string offsetMax = direction == "UP" ? "221.51 114.635" : "178.326 114.632";

			container.Add(new CuiElement
			{
				Parent = parentId,
				Name = buttonName,
				Components =
				{
					new CuiRawImageComponent { Png = _imageUI.GetImage(imageName), Color = "1 1 1 1" },
					new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = offsetMin, OffsetMax = offsetMax }
				}
			});

			container.Add(new CuiButton
			{
				RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
				Button = { Color = "0 0 0 0", Command = command },
				Text = { Text = "" }
			}, buttonName);
		}

		private void QuestListUI(BasePlayer player, UICategory category, int page = 0)
		{
			List<PlayerQuest> playerQuests = _playersInfo[player.userID].CurrentPlayerQuests;
			if (playerQuests == null)
			{
				return;
			}

			int y = 0;
			CuiElementContainer container = new CuiElementContainer
			{
				{
					new CuiPanel
					{
						Image = { Color = "0 0 0 0" },
						RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "96.23 -234.241", OffsetMax = "347.79 181.441" }
					},
					LAYER_MAIN_BACKGROUND,
					"QuestListPanel", "QuestListPanel"
				}
			};
			List<Quest> ql = GetQuestsByCategory(category, player.userID);
			if (page == 0)
				UpdateTasksCount(player, ql.Count);


			#region PageSettings

			if (page != 0)
			{
				AddPageButton("UP", LAYER_MAIN_BACKGROUND, $"UI_Handler page {page - 1} {category.ToString()}", container);
			}

			if (page + 1 < (int)Math.Ceiling((double)ql.Count / 6))
			{
				AddPageButton("DOWN", LAYER_MAIN_BACKGROUND, $"UI_Handler page {page + 1} {category.ToString()}", container);
			}

			#endregion

			if (ql.Count <= 0)
			{
				container.Add(new CuiLabel
				{
					RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
					Text =
					{
						Text = "XDQUEST_UI_TASKS_LIST_EMPTY".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 15, Align = TextAnchor.MiddleCenter,
						Color = "1 1 1 1"
					}
				}, "QuestListPanel");
			}

			foreach (Quest item in ql.Page(page, 6))
			{
				container.Add(new CuiElement
				{
					Name = "Quest",
					Parent = "QuestListPanel",
					Components =
					{
						new CuiRawImageComponent { Color = $"1 1 1 1", Png = _imageUI.GetImage("5") },
						new CuiRectTransformComponent
							{ AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = $"-125.78 {-67.933 - (y * 69.413)}", OffsetMax = $"125.78 {-1.06 - (y * 69.413)}" }
					}
				});
				container.Add(new CuiLabel
				{
					RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-109.661 -33", OffsetMax = "113.14 -12.085" },
					Text =
					{
						Text = item.GetDisplayName(lang.GetLanguage(player.UserIDString)), Font = "robotocondensed-bold.ttf", FontSize = 13, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1"
					}
				}, "Quest", "QuestName");
				if (category == UICategory.Taken)
				{
					PlayerQuest foundQuest = playerQuests.Find(quest => quest.ParentQuestID == item.QuestID);
					if (foundQuest != null)
					{
						string img, txt;
						if (foundQuest.Finished)
						{
							img = "15";
							txt = "XDQUEST_UI_CHIPperformed".GetAdaptedMessage(player.UserIDString);
						}
						else
						{
							img = "14";
							txt = "XDQUEST_UI_CHIPInProgress".GetAdaptedMessage(player.UserIDString);
						}

						container.Add(new CuiElement
						{
							Name = "QuestBar",
							Parent = "Quest",
							Components =
							{
								new CuiRawImageComponent { Color = "1 1 1 1", Png = _imageUI.GetImage(img) },
								new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "17.19 -16.717", OffsetMax = "97.902 -2.411" }
							}
						});
						container.Add(new CuiLabel
						{
							RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-34.924 -7.153", OffsetMax = "40.356 7.153" },
							Text = { Text = txt, Font = "robotocondensed-bold.ttf", FontSize = 10, Align = TextAnchor.UpperCenter, Color = "1 1 1 1" }
						}, "QuestBar", "BarLabel");
					}
				}


				container.Add(new CuiButton
				{
					RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMax = "0 0" },
					Button = { Color = "0 0 0 0", Command = $"UI_Handler questinfo {item.QuestID} {category.ToString()} {page}" },
					Text = { Text = "" }
				}, $"Quest");
				y++;
			}

			CuiHelper.DestroyUi(player, "DOWNBTN");
			CuiHelper.DestroyUi(player, "UPBTN");
			CuiHelper.AddUi(player, container);
		}

		#endregion

		#region QuestInfo

		private void QuestInfo(BasePlayer player, long questID, UICategory category, int page = 0)
		{
			List<PlayerQuest> playerQuests = _playersInfo[player.userID].CurrentPlayerQuests;
			if (playerQuests == null)
			{
				return;
			}

			PlayerQuest foundQuest = playerQuests.Find(quest => quest.ParentQuestID == questID);
			Quest quests = null;
			Quest value;
			if (_questList.TryGetValue(questID, out value))
				quests = value;
			string playerLaunguage = lang.GetLanguage(player.UserIDString);

			CuiElementContainer container = new CuiElementContainer();

			container.Add(new CuiPanel
			{
				Image = { Color = "1 1 1 0" },
				RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-280.488 -234.241", OffsetMax = "564.144 212.279" }
			}, LAYER_MAIN_BACKGROUND, "QuestInfoPanel", "QuestInfoPanel");

			if (questID == 0 || quests == null)
			{
				container.Add(new CuiLabel
				{
					RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-398.895 -289.293", OffsetMax = "106.815 -76.2" },
					Text =
					{
						Text = "XDQUEST_UI_TASKS_INFO_EMPTY".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 19, Align = TextAnchor.MiddleCenter,
						Color = "1 1 1 1"
					}
				}, "QuestInfoPanel");

				CuiHelper.AddUi(player, container);
				return;
			}


			container.Add(new CuiLabel
			{
				RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "23.704 -42.956", OffsetMax = "420.496 -16.044" },
				Text = { Text = quests.GetDisplayName(playerLaunguage), Font = "robotocondensed-bold.ttf", FontSize = 19, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }
			}, "QuestInfoPanel", "QuestName");

			string userepeat = quests.IsRepeatable ? "XDQUEST_UI_QUESTREPEATCAN".GetAdaptedMessage(player.UserIDString) : "XDQUEST_UI_QUESTREPEATfForbidden".GetAdaptedMessage(player.UserIDString);
			string useCooldown = quests.Cooldown > 0
				? TimeHelper.FormatTime(TimeSpan.FromSeconds(quests.Cooldown), 5, playerLaunguage)
				: "XDQUEST_UI_Missing".GetAdaptedMessage(player.UserIDString);
			string bring = quests.IsReturnItemsRequired ? "XDQUEST_UI_QuestNecessary".GetAdaptedMessage(player.UserIDString) : "XDQUEST_UI_QuestNotNecessary".GetAdaptedMessage(player.UserIDString);
			container.Add(new CuiLabel
			{
				RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "23.705 -54.066", OffsetMax = "420.495 -40.134" },
				Text =
				{
					Text = "XDQUEST_UI_InfoRepeatInCD".GetAdaptedMessage(player.UserIDString, userepeat, useCooldown, bring), Font = "robotocondensed-regular.ttf", FontSize = 10,
					Align = TextAnchor.UpperLeft, Color = "0.9607844 0.5843138 0.1960784 1"
				}
			}, "QuestInfoPanel", "QuestInfo2");

			container.Add(new CuiLabel
			{
				RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-398.895 -289.293", OffsetMax = "106.815 -76.2" },
				Text = { Text = quests.GetDescription(playerLaunguage), Font = "robotocondensed-regular.ttf", FontSize = 16, Align = TextAnchor.UpperLeft, Color = "1 1 1 1" }
			}, "QuestInfoPanel", "QuestDescription");

			#region QuestButton

			string buttonText = "", imageID = "", command = "", checkBox = "10";
			double? cooldownForQuest = _playersInfo[player.userID].GetCooldownForQuest(questID);

			if (foundQuest == null)
			{
				if (cooldownForQuest.HasValue)
				{
					imageID = "6";
					command = $"UI_Handler coldown";
				}
				else
				{
					if (!quests.IsRepeatable && _playersInfo[player.userID].CompletedQuestIds.Contains(quests.QuestID))
					{
						buttonText = "XDQUEST_UI_QuestBtnPerformed".GetAdaptedMessage(player.UserIDString);
						imageID = "6";
						command = $"UI_Handler get {questID} {category.ToString()} {page}";
					}
					else
					{
						buttonText = "XDQUEST_UI_QuestBtnTake".GetAdaptedMessage(player.UserIDString);
						imageID = "7";
						command = $"UI_Handler get {questID} {category.ToString()} {page}";
					}
				}
			}
			else if (foundQuest.Finished)
			{
				buttonText = "XDQUEST_UI_QuestBtnPass".GetAdaptedMessage(player.UserIDString);
				imageID = "7";
				command = $"UI_Handler finish {questID} {category.ToString()} {page}";
				checkBox = "11";
			}
			else if (foundQuest.ParentQuestType == QuestType.Delivery)
			{
				container.Add(new CuiElement
				{
					Name = LAYERS + "QuestButtonImageA",
					Parent = "QuestInfoPanel",
					Components =
					{
						new CuiRawImageComponent { Color = "1 1 1 1", Png = _imageUI.GetImage("7") },
						new CuiRectTransformComponent { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = "-416.142 -49.709", OffsetMax = "-306.058 -7.691" }
					}
				});
				
				container.Add(new CuiButton
				{
					Button = { Color = "0 0 0 0", Command = $"UI_Handler finish {questID} {category.ToString()} {page}" },
					Text = { Text = "XDQUEST_UI_QuestBtnDelivery".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
					RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1"}
				}, LAYERS + "QuestButtonImageA", LAYERS + "ButtonQuestA", LAYERS + "ButtonQuestA");
				
				container.Add(new CuiElement
				{
					Name = LAYERS + "QuestButtonImageC",
					Parent = "QuestInfoPanel",
					Components =
					{
						new CuiRawImageComponent { Color = "1 1 1 1", Png = _imageUI.GetImage("6") },
						new CuiRectTransformComponent { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = "-296.142 -49.709", OffsetMax = "-186.058 -7.691" }
					}
				});
				
				container.Add(new CuiButton
				{
					Button = { Color = "0 0 0 0", Command = $"UI_Handler finish {questID} {category.ToString()} {page} true" },
					Text = { Text = "XDQUEST_UI_QuestBtnRefuse".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
					RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1"}
				}, LAYERS + "QuestButtonImageC", LAYERS + "ButtonQuestC", LAYERS + "ButtonQuestC");
			}
			else
			{
				buttonText = "XDQUEST_UI_QuestBtnRefuse".GetAdaptedMessage(player.UserIDString);
				imageID = "6";
				command = $"UI_Handler finish {questID} {category.ToString()} {page}";
			}

			if (foundQuest is not { ParentQuestType: QuestType.Delivery })
			{
				container.Add(new CuiElement
				{
					Name = LAYERS + "QuestButtonImage",
					Parent = "QuestInfoPanel",
					Components =
					{
						new CuiRawImageComponent { Color = "1 1 1 1", Png = _imageUI.GetImage(imageID) },
						new CuiRectTransformComponent { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = "-416.142 -49.709", OffsetMax = "-306.058 -7.691" }
					}
				});

				if (cooldownForQuest.HasValue)
				{
					container.Add(new CuiButton
					{
						Button = { Color = "0 0 0 0", Command = command },
						Text = { Color = "1 1 1 1" },
						RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-55.039 -21.01", OffsetMax = "55.041 21.009" }
					}, LAYERS + "QuestButtonImage", LAYERS + "ButtonQuest");
					
					container.Add(new CuiElement
					{
						Parent = LAYERS + "ButtonQuest",
						Update = false, 
						Components =
						{
							new CuiCountdownComponent { StartTime = (float)(cooldownForQuest.Value - CurrentTime()), TimerFormat = TimerFormat.HoursMinutesSeconds, DestroyIfDone = true,},
							new CuiTextComponent { Text = $"%TIME_LEFT%", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter },
						}
					});
				}
				else
				{
					container.Add(new CuiButton
					{
						Button = { Color = "0 0 0 0", Command = command },
						Text = { Text = buttonText, Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
						RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-55.039 -21.01", OffsetMax = "55.041 21.009" }
					}, LAYERS + "QuestButtonImage", LAYERS + "ButtonQuest", LAYERS + "ButtonQuest");
				}
			}

			#endregion

			#region QuestCheckBox

			container.Add(new CuiElement
			{
				Name = "QuestCheckBox",
				Parent = "QuestInfoPanel",
				Components =
				{
					new CuiRawImageComponent { Color = "1 1 1 1", Png = _imageUI.GetImage("9") },
					new CuiRectTransformComponent { AnchorMin = "1 0", AnchorMax = "1 0", OffsetMin = "-279.228 1.334", OffsetMax = "-1.217 125.64" }
				}
			});

			if (foundQuest?.ParentQuestType != QuestType.Delivery)
			{
				container.Add(new CuiElement
				{
					Name = "CheckBoxImg",
					Parent = "QuestCheckBox",
					Components =
					{
						new CuiRawImageComponent { Color = "1 1 1 1", Png = _imageUI.GetImage(checkBox) },
						new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "20.729 -35.467", OffsetMax = "38.205 -18.005" }
					}
				});
			}
			

			container.Add(new CuiLabel
			{
				RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-91.326 -55.693", OffsetMax = "136.647 -16.904" },
				Text = { Text = quests.GetMissions(playerLaunguage), Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.UpperLeft, Color = "1 1 1 1" }
			}, "QuestCheckBox", "CheckBoxTxt");

			if (foundQuest != null && foundQuest.ParentQuestType != QuestType.Delivery)
			{
				double factor = 278.005 * foundQuest.Count / quests.ActionCount;
				container.Add(new CuiPanel
				{
					CursorEnabled = false,
					Image = { Color = "0.3843138 0.3686275 0.3843138 0.9137255" },
					RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "-0.000 -0.153", OffsetMax = $"278.005 40.106" }
				}, "QuestCheckBox", "QuestProgresBar");
				container.Add(new CuiPanel
				{
					CursorEnabled = false,
					Image = { Color = "0.4462442 0.8679245 0.5786404 0.6137255" },
					RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "-0.000 -0.153", OffsetMax = $"{factor} 40.106" }
				}, "QuestProgresBar");
				container.Add(new CuiLabel
				{
					RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-139.005 -20.129", OffsetMax = "139.005 20.13" },
					Text = { Text = $"{foundQuest.Count} / {quests.ActionCount}", Font = "robotocondensed-bold.ttf", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
				}, "QuestProgresBar", "Progres");
			}

			#endregion

			#region PrizeList

			string prizeImage = _imageUI.GetImage("8");
			int i = 0;
			foreach (Quest.Prize prize in quests.PrizeList)
			{
				if(prize.IsHidden) continue;
				
				string prizeLayer = "QuestInfo" + $".{i}";
				
				int x = i % 4;
				int y = i / 4;
				
				container.Add(new CuiElement
				{
					Name = prizeLayer,
					Parent = "QuestInfoPanel",
					Components =
					{
						new CuiRawImageComponent { Color = "1 1 1 1", Png = prizeImage },
						new CuiRectTransformComponent
						{
							AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = $"{23.42 + (x * 120.912)} {79.39 - (y * 78.345)}",
							OffsetMax = $"{129.555 + (x * 120.912)} {125.9 - (y * 78.345)}"
						}
					}
				});


				switch (prize.PrizeType)
				{
					case PrizeType.Item:
						container.Add(new CuiElement
						{
							Parent = prizeLayer,
							Components =
							{
								new CuiImageComponent { Color = "1 1 1 1", ItemId = ItemManager.FindItemDefinition(prize.ItemShortName).itemid },
								new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-10.059 -20.625", OffsetMax = "32.941 22.375" }
							}
						});
						break;
					case PrizeType.BluePrint:
						container.Add(new CuiElement
						{
							Parent = prizeLayer,
							Components =
							{
								new CuiImageComponent { Color = "1 1 1 1", ItemId = ItemManager.FindItemDefinition("blueprintbase").itemid },
								new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-10.059 -20.625", OffsetMax = "32.941 22.375" }
							}
						});
						container.Add(new CuiElement
						{
							Parent = prizeLayer,
							Components =
							{
								new CuiImageComponent { Color = "1 1 1 1", ItemId = ItemManager.FindItemDefinition(prize.ItemShortName).itemid },
								new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-10.059 -20.625", OffsetMax = "32.941 22.375" }
							}
						});
						break;
					case PrizeType.CustomItem:
						container.Add(new CuiElement
						{
							Parent = prizeLayer,
							Components =
							{
								new CuiImageComponent { Color = "1 1 1 1", ItemId = ItemManager.FindItemDefinition(prize.ItemShortName).itemid, SkinId = prize.ItemSkinID },
								new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-10.059 -20.625", OffsetMax = "32.941 22.375" }
							}
						});
						break;
					case PrizeType.Command:
						container.Add(new CuiElement
						{
							Parent = prizeLayer,
							Components =
							{
								new CuiRawImageComponent { Color = "1 1 1 1", Png = GetImage(prize.CommandImageUrl) },
								new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-10.059 -20.625", OffsetMax = "32.941 22.375" }
							}
						});
						break;
					default:
						throw new ArgumentOutOfRangeException();
				}

				container.Add(new CuiLabel
				{
					RectTransform = { AnchorMin = "1 0", AnchorMax = "1 0", OffsetMin = "-61.669 0.67", OffsetMax = "-5.931 17.33" },
					Text = { Text = $"x{prize.ItemAmount}", Font = "robotocondensed-regular.ttf", FontSize = 11, Align = TextAnchor.MiddleRight, Color = "1 1 1 1" }
				}, prizeLayer);
				
				if (y == 2)
				{
					break;
				}
				i++;
			}

			#endregion

			CuiHelper.AddUi(player, container);
		}

		#endregion

		#region MiniQuestList

		private void OpenMQL_CMD(BasePlayer player)
		{
			UIMiniQuestList(player);
		}

		private void UIMiniQuestList(BasePlayer player, int page = 0)
		{
			List<PlayerQuest> playerQuests = _playersInfo[player.userID].CurrentPlayerQuests;
			if (playerQuests == null)
			{
				return;
			}

			if (playerQuests.Count == 0)
			{
				SendReply(player, "XDQUEST_UI_ActiveQuestCount".GetAdaptedMessage(player.UserIDString));
				if (_openMiniQuestListPlayers.Contains(player.userID))
				{
					_openMiniQuestListPlayers.Remove(player.userID);
				}

				return;
			}

			if (!_openMiniQuestListPlayers.Contains(player.userID))
			{
				_openMiniQuestListPlayers.Add(player.userID);
			}

			playerQuests.Sort(delegate(PlayerQuest x, PlayerQuest y)
			{
				if (x.Finished && !y.Finished) return -1;
				if (!x.Finished && y.Finished) return 1;
				return 0;
			});
			string playerLaunguage = lang.GetLanguage(player.UserIDString);
			const int size = 72;
			string image = _imageUI.GetImage("5");
			string imageTwo = _imageUI.GetImage("13");
			int questCount = playerQuests.Count, qc = -72 * questCount;
			double ds = 207.912 + qc;
			CuiElementContainer container = new CuiElementContainer
			{
				{
					new CuiPanel
					{
						CursorEnabled = false,
						Image = { Color = "1 1 1 0" },
						RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = $"0 {ds}", OffsetMax = "304.808 303.288" }
					},
					"Overlay",
					MINI_QUEST_LIST, MINI_QUEST_LIST
				},

				{
					new CuiButton
					{
						Button = { Color = "0 0 0 0", Command = "CloseMiniQuestList" },
						Text = { Text = "x", Font = "robotocondensed-regular.ttf", FontSize = 15, Align = TextAnchor.MiddleCenter, Color = "1 0 0 1" },
						RectTransform = { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = "-20 -20", OffsetMax = "0 0" }
					},
					MINI_QUEST_LIST,
					"MiniQuestClosseBtn"
				},
				{
					new CuiLabel
					{
						RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "3.825 -23.035", OffsetMax = "173.821 0" },
						Text =
						{
							Text = "XDQUEST_UI_ACTIVEOBJECTIVES".GetAdaptedMessage(player.UserIDString, playerQuests.Count), Font = "robotocondensed-bold.ttf", FontSize = 12,
							Align = TextAnchor.MiddleLeft, Color = "1 1 1 1"
						}
					},
					MINI_QUEST_LIST,
					"LabelMiniQuestPanel"
				}
			};


			int i = 0;
			foreach (PlayerQuest quest in playerQuests.Page(page, 8))
			{
				Quest currentQuest = _questList[quest.ParentQuestID];
				string color = quest.Finished ? "0.1960784 0.7176471 0.4235294 1" : "0.9490197 0.3764706 0.3960785 1";
				bool isDelivery = currentQuest.QuestType == QuestType.Delivery;
				container.Add(new CuiElement
				{
					Name = "MiniQuestImage",
					Parent = MINI_QUEST_LIST,
					Components =
					{
						new CuiRawImageComponent { Color = "0 0 0 1", Png = image },
						new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"3.829 {-90.188 - i * size}", OffsetMax = $"299.599 {-23.035 - i * size}" }
					}
				});
				container.Add(new CuiElement
				{
					Name = "ImgForMiniQuest",
					Parent = "MiniQuestImage",
					Components =
					{
						new CuiRawImageComponent { Color = color, Png = imageTwo },
						new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "0.112 -33.576", OffsetMax = "12.577 33.577" }
					}
				});
				string qtext = isDelivery ? "XDQUEST_UI_MiniQLInfoDelivery" : "XDQUEST_UI_MiniQLInfo";
				container.Add(new CuiElement
				{
					Name = "LabelForMiniQuest",
					Parent = "MiniQuestImage",
					Components =
					{
						new CuiTextComponent
						{
							Text = qtext.GetAdaptedMessage(player.UserIDString, currentQuest.GetDisplayName(playerLaunguage), quest.Count, currentQuest.ActionCount, currentQuest.GetMissions(playerLaunguage)),
							Font = "robotocondensed-regular.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1"
						},
						new CuiOutlineComponent { Color = "0 0 0 1", Distance = "0.6 0.6" },
						new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "14.925 -28.867", OffsetMax = "283.625 28.867" }
					}
				});
				i++;
			}

			#region Page

			int pageCount = (int)Math.Ceiling((double)playerQuests.Count / 8);
			if (pageCount > 1)
			{
				container.Add(new CuiPanel
				{
					CursorEnabled = false,
					Image = { Color = "1 1 1 0" },
					RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"3.829 {-126.593 - (i - 1) * size}", OffsetMax = $"145.353 {-90.187 - (i - 1) * size}" }
				}, MINI_QUEST_LIST, "Panel_1410");
				container.Add(new CuiLabel
				{
					RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-22.598 -11.514", OffsetMax = "21.517 11.514" },
					Text = { Text = $"{page + 1}/{pageCount}", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
				}, "Panel_1410");
				if (page + 1 < pageCount)
				{
					container.Add(new CuiElement
					{
						Parent = "Panel_1410",
						Name = "DOWNBTN",
						Components =
						{
							new CuiRawImageComponent { Png = _imageUI.GetImage("4"), Color = "1 1 1 1" },
							new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-61.326 -13.326", OffsetMax = "-22.598 13.535" }
						}
					});

					container.Add(new CuiButton
					{
						RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
						Button = { Color = "0 0 0 0", Command = $"UI_Handler pageQLIST {page + 1}" },
						Text = { Text = "" }
					}, "DOWNBTN");
				}

				if (page > 0)
				{
					container.Add(new CuiElement
					{
						Parent = "Panel_1410",
						Name = "UPBTN",
						Components =
						{
							new CuiRawImageComponent { Png = _imageUI.GetImage("3"), Color = "1 1 1 1" },
							new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "21.517 -13.326", OffsetMax = "60.138 13.743" }
						}
					});

					container.Add(new CuiButton
					{
						RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
						Button = { Color = "0 0 0 0", Command = $"UI_Handler pageQLIST {page - 1}" },
						Text = { Text = "" }
					}, "UPBTN");
				}
			}

			#endregion

			CuiHelper.AddUi(player, container);
		}

		#endregion

		#region Notice

		private void UINottice(BasePlayer player, string msg, string sprite = "assets/icons/warning.png", string color = "0.76 0.34 0.10 1.00")
		{
			CuiElementContainer container = new CuiElementContainer
			{
				new CuiElement
				{
					FadeOut = 0.30f,
					Name = "QuestUiNotice",
					Parent = LAYER_MAIN_BACKGROUND,
					Components =
					{
						new CuiRawImageComponent { Color = "1 1 1 1", Png = _imageUI.GetImage("12"), FadeIn = 0.30f },
						new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "315 -110", OffsetMax = "610 -43" }
					}
				},

				new CuiElement
				{
					FadeOut = 0.30f,
					Name = "NoticeFeed",
					Parent = "QuestUiNotice",
					Components =
					{
						new CuiRawImageComponent { Color = color, Png = _imageUI.GetImage("13"), FadeIn = 0.30f },
						new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "0.276 -33.458", OffsetMax = "12.692 33.459" }
					}
				},
				//container.Add(new CuiElement
				//{
				//    Parent = "QuestUi",
				//    Components = {
				//        new CuiRawImageComponent { Color = HexToRustFormat(color), Png = GetImage("16"), FadeIn = 0.30f },
				//        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "0.451 -23.243", OffsetMax = "1.3422 12.543" }
				//    }
				//});

				new CuiElement
				{
					FadeOut = 0.30f,
					Name = "NoticeSprite",
					Parent = "QuestUiNotice",
					Components =
					{
						new CuiImageComponent { Color = "1 1 1 1", Sprite = sprite, FadeIn = 0.30f },
						new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "23.5 -15.5", OffsetMax = "54.5 15.5" }
					}
				},

				{
					new CuiLabel
					{
						RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-78.262 -33.458", OffsetMax = "143.522 33.459" },
						Text = { Text = msg, Font = "robotocondensed-regular.ttf", FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1", FadeIn = 0.30f }
					},
					"QuestUiNotice",
					"NoticeText"
				}
			};

			CuiHelper.DestroyUi(player, "NoticeText");
			CuiHelper.DestroyUi(player, "NoticeSprite");
			CuiHelper.DestroyUi(player, "NoticeFeed");
			CuiHelper.DestroyUi(player, "QuestUiNotice");
			CuiHelper.AddUi(player, container);

			DeleteNotification(player);
		}

		private readonly Dictionary<BasePlayer, Timer> _playerTimer = new Dictionary<BasePlayer, Timer>();

		private void DeleteNotification(BasePlayer player)
		{
			Timer timers = timer.Once(3.5f, () =>
			{
				CuiHelper.DestroyUi(player, "NoticeText");
				CuiHelper.DestroyUi(player, "NoticeSprite");
				CuiHelper.DestroyUi(player, "NoticeFeed");
				CuiHelper.DestroyUi(player, "QuestUiNotice");
			});

			if (_playerTimer.ContainsKey(player))
			{
				if (_playerTimer[player] != null && !_playerTimer[player].Destroyed) _playerTimer[player].Destroy();
				_playerTimer[player] = timers;
			}
			else _playerTimer.Add(player, timers);
		}

		#endregion

		#endregion

		#region Helper Classes

		private static class ObjectCache
		{
			private static readonly object True = true;
			private static readonly object False = false;

			private static class StaticObjectCache<T>
			{
				private static readonly Dictionary<T, object> CacheByValue = new Dictionary<T, object>();

				public static object Get(T value)
				{
					object cachedObject;
					if (!CacheByValue.TryGetValue(value, out cachedObject))
					{
						cachedObject = value;
						CacheByValue[value] = cachedObject;
					}

					return cachedObject;
				}
			}

			public static object Get<T>(T value)
			{
				return StaticObjectCache<T>.Get(value);
			}

			public static object Get(bool value)
			{
				return value ? True : False;
			}
		}

		#endregion

		#region Command
		
		private void SendConsoleMessage(BasePlayer player, string message)
		{
			if(player != null)
				player.ConsoleMessage(message);
			else
				PrintWarning(message);
		}
		
		[ConsoleCommand("xdquest.player.reset")]
		private void PlayerDataReset(ConsoleSystem.Arg arg)
		{
			BasePlayer player = arg.Player();
			if (player != null && !player.IsAdmin)
			{
				player.ConsoleMessage("XDQUEST_INSUFFICIENT_PERMISSIONS_ERROR".GetAdaptedMessage(player.UserIDString));
				return;
			}
			
			if (!arg.HasArgs())
			{
				SendConsoleMessage(player, "XDQUEST_COMMAND_SYNTAX_ERROR".GetAdaptedMessage(PlayerOrNull(player)));
				return;
			}

			ulong playerid;
			if(!ulong.TryParse(arg.GetString(0), out playerid))
			{
				SendConsoleMessage(player, "XDQUEST_INVALID_PLAYER_ID_INPUT".GetAdaptedMessage(PlayerOrNull(player)));
				return;
			}

			if (!playerid.IsSteamId())
			{
				SendConsoleMessage(player, "XDQUEST_NOT_A_STEAM_ID".GetAdaptedMessage(PlayerOrNull(player)));
				return;
			}
			
			if (_playersInfo.ContainsKey(playerid))
			{
				_playersInfo[playerid] = new PlayerData();
				SendConsoleMessage(player, "XDQUEST_PLAYER_PROGRESS_RESET".GetAdaptedMessage(PlayerOrNull(player)));
			}
			else
			{
				SendConsoleMessage(player, "XDQUEST_PLAYER_NOT_FOUND_BY_STEAMID".GetAdaptedMessage(PlayerOrNull(player)));
			}
		}
		
		[ConsoleCommand("xdquest.stat")]
		private void StatisticsPost (ConsoleSystem.Arg arg)
		{
			BasePlayer player = arg.Player();
			if (player != null && !player.IsAdmin)
			{
				player.ConsoleMessage("XDQUEST_INSUFFICIENT_PERMISSIONS_ERROR".GetAdaptedMessage(player.UserIDString));
				return;
			}
			
			if (!_config.statisticsCollectionSettings.useStatistics)
			{
				SendConsoleMessage(player, "XDQUEST_STAT_CMD_1".GetAdaptedMessage(PlayerOrNull(player)));
				return;
			}

			if (string.IsNullOrEmpty(_config.statisticsCollectionSettings.discordWebhookUrl))
			{
				SendConsoleMessage(player, "XDQUEST_STAT_CMD_2".GetAdaptedMessage(PlayerOrNull(player)));
				return;
			}
			
			GrabAndPostStatistics();
		}

		private string PlayerOrNull(BasePlayer player)
		{
			return player != null ? player.UserIDString : null;
		}

		[ConsoleCommand("CloseMiniQuestList")]
		void CloseMiniQuestList(ConsoleSystem.Arg arg)
		{
			CuiHelper.DestroyUi(arg.Player(), MINI_QUEST_LIST);
			if (_openMiniQuestListPlayers.Contains(arg.Player().userID))
			{
				_openMiniQuestListPlayers.Remove(arg.Player().userID);
			}
		}

		[ConsoleCommand("CloseMainUI")]
		void CloseLayerPlayer(ConsoleSystem.Arg arg)
		{
			CuiHelper.DestroyUi(arg.Player(), LAYERS);
		}

		[ChatCommand("quest.saveposition")]
		void CustomPosSave(BasePlayer player)
		{
			if(!player.IsAdmin) return;
			_config.spawnSettings.CustomPosition.Position = player.transform.position;
			SaveConfig();
			SendChat(player, "XDQUEST_UI_CMDCustomPosAdd".GetAdaptedMessage(player.UserIDString));
		}
		
		[ChatCommand("quest.saveposition.outpost")]
		void OutPostPosSave(BasePlayer player)
		{
			if(!player.IsAdmin)
				return;
			if (_monument == null)
			{
				SendChat(player, "XDQUEST_MissingOutPost".GetAdaptedMessage(player.UserIDString));
				return;
			}
			Transform transform = _monument.transform;
    
			Vector3 myWorldPosition = player.transform.position;

			Vector3 relativeLocalPosition = transform.InverseTransformPoint(myWorldPosition);
			_config.spawnSettings.MonumentPosition.Position = relativeLocalPosition;
			SaveConfig();
			SendChat(player, "XDQUEST_UI_CMDPosChange".GetAdaptedMessage(player.UserIDString));
		}

		[ChatCommand("quest.tphouse")]
		void TpToQuestHouse(BasePlayer player)
		{
			if (player.IsAdmin)
			{
				player.Teleport(GetResultVector());
			}
		}

		[ConsoleCommand("UI_Handler")]
		private void CmdConsoleHandler(ConsoleSystem.Arg args)
		{
			BasePlayer player = args.Player();
			List<PlayerQuest> playerQuests = _playersInfo[player.userID].CurrentPlayerQuests;
			if (playerQuests == null)
			{
				return;
			}

			if (player != null && args.HasArgs())
			{
				switch (args.Args[0])
				{
					case "get":
					{
						UICategory category;
						int pageIndex;
						if (args.HasArgs(4) && long.TryParse(args.Args[1],  out long questID) && Enum.TryParse(args.Args[2], out category) && int.TryParse(args.Args[3], out pageIndex))
						{
							Quest currentQuest = _questList[questID];
							if (currentQuest != null)
							{
								if (playerQuests.Count >= _config.settings.questCount)
								{
									UINottice(player, "XDQUEST_UI_QuestLimit".GetAdaptedMessage(player.UserIDString));
									return;
								}

								if (playerQuests.Exists(p => p.ParentQuestID == currentQuest.QuestID))
								{
									UINottice(player, "XDQUEST_UI_AlreadyTaken".GetAdaptedMessage(player.UserIDString));
									return;
								}

								if (!string.IsNullOrEmpty(currentQuest.QuestPermission) && !permission.UserHasPermission(player.UserIDString, $"{Name}." + currentQuest.QuestPermission))
								{
									UINottice(player, "XDQUEST_UI_NotPerm".GetAdaptedMessage(player.UserIDString));
									return;
								}

								if (!currentQuest.IsRepeatable && _playersInfo[player.userID].CompletedQuestIds.Contains(currentQuest.QuestID))
								{
									UINottice(player, "XDQUEST_UI_AlreadyDone".GetAdaptedMessage(player.UserIDString));
									return;
								}

								if (_playersInfo[player.userID].CompletedQuestIds.Contains(currentQuest.QuestID))
								{
									UINottice(player, "XDQUEST_UI_AlreadyDone".GetAdaptedMessage(player.UserIDString));
									return;
								}

								playerQuests.Add(new PlayerQuest() { UserID = player.userID, ParentQuestID = currentQuest.QuestID, ParentQuestType = currentQuest.QuestType });
								_questStatistics.GatherTaskStatistics(TaskType.Taken);

								QuestListUI(player, category, pageIndex);
								QuestInfo(player, questID, category, pageIndex);
								UINottice(player, "XDQUEST_UI_TookTasks".GetAdaptedMessage(player.UserIDString, currentQuest.GetDisplayName(lang.GetLanguage(player.UserIDString))));
								if (_config.settingsNpc.soundUse)
								{
									Instance._audioZoneController.StartPlayingSound(AudioTriggerTypes.TaskAcceptance);
								}
							}
						}

						break;
					}
					case "page":
					{
						int pageIndex;
						UICategory category;
						if (int.TryParse(args.Args[1], out pageIndex) && Enum.TryParse(args.Args[2], out category))
						{
							QuestListUI(player, category, pageIndex);
						}

						break;
					}
					case "category":
					{
						UICategory category;
						bool isParsed = Enum.TryParse(args.Args[1], out category);
						if (isParsed)
						{
							Category(player, category);
							QuestListUI(player, category);
						}

						break;
					}
					case "pageQLIST":
					{
						int pageIndex;
						if (int.TryParse(args.Args[1], out pageIndex))
						{
							UIMiniQuestList(player, pageIndex);
						}

						break;
					}
					case "coldown":
					{
						UINottice(player, "XDQUEST_UI_ACTIVECOLDOWN".GetAdaptedMessage(player.UserIDString));
						break;
					}
					case "questinfo":
					{
						long questIndex;
						UICategory category;
						int pageIndex;
						if (long.TryParse(args.Args[1], out questIndex) && Enum.TryParse(args.Args[2], out category) && int.TryParse(args.Args[3], out pageIndex))
						{
							QuestInfo(player, questIndex, category, pageIndex);
						}

						break;
					}
					case "finish":
					{
						long questID;
						UICategory category;
						int pageIndex;
						bool cancel = args.HasArgs(5) && bool.TryParse(args.Args[4], out cancel);
						if (args.HasArgs(4) && long.TryParse(args.Args[1], out questID) && Enum.TryParse(args.Args[2], out category) && int.TryParse(args.Args[3], out pageIndex))
						{
							Quest globalQuest = _questList[questID];
							if (globalQuest != null)
							{
								PlayerQuest currentQuest = playerQuests.Find(quest => quest.ParentQuestID == globalQuest.QuestID);
								if (currentQuest == null)
								{
									return;
								}

								if (currentQuest.Finished || (currentQuest.ParentQuestType == QuestType.Delivery && cancel == false))
								{
									int count = 0;
									foreach (Quest.Prize prize in globalQuest.PrizeList)
										if (prize.PrizeType != PrizeType.Command)
											count++;

									if (24 - player.inventory.containerMain.itemList.Count < count)
									{
										UINottice(player, "XDQUEST_UI_LackOfSpace".GetAdaptedMessage(player.UserIDString));
										return;
									}

									if (globalQuest.IsReturnItemsRequired)
									{
										ulong skins;
										if (globalQuest.QuestType is QuestType.Loot or QuestType.Delivery && ulong.TryParse(globalQuest.Target, out skins))
										{
											if (!TakeSkinIdItemsForQuest(player, globalQuest, skins))
												return;
										}
										else if (globalQuest.QuestType is QuestType.Gather or QuestType.Loot or QuestType.Craft or QuestType.PurchaseFromNpc or QuestType.Growseedlings or QuestType.Fishing or QuestType.Delivery)
										{
											if (!TakeItemsNeededForQuest(player, globalQuest))
												return;
										}
									}

									UINottice(player, "XDQUEST_UI_QuestsCompleted".GetAdaptedMessage(player.UserIDString));

									currentQuest.Finished = false;
									GiveQuestReward(player, globalQuest.PrizeList);
									if (!globalQuest.IsRepeatable)
									{
										_playersInfo[player.userID].CompletedQuestIds.Add(currentQuest.ParentQuestID);
									}
									else if (globalQuest.Cooldown > 0)
									{
										_playersInfo[player.userID].PlayerQuestCooldowns[currentQuest.ParentQuestID] = CurrentTime() + globalQuest.Cooldown;
									}

									playerQuests.Remove(currentQuest);
									QuestListUI(player, category, pageIndex);
									QuestInfo(player, questID, category, pageIndex);
									if (_config.settingsNpc.soundUse)
									{
										Instance._audioZoneController.StartPlayingSound(AudioTriggerTypes.TaskCompletion);
									}
								}
								else
								{
									UINottice(player, "XDQUEST_UI_PassedTasks".GetAdaptedMessage(player.UserIDString));
									playerQuests.Remove(currentQuest);
									QuestListUI(player, category, pageIndex);
									QuestInfo(player, questID, category, pageIndex);
									_questStatistics.GatherTaskStatistics(TaskType.Declined);
								}
							}
							else
							{
								UINottice(player, "Вы <color=#4286f4>не брали</color> этого задания!");
							}
						}

						break;
					}
				}
			}
		}

		#endregion

		#region HelpQuestsMetods

		#region Rewards and bring items

		private void GiveQuestReward(BasePlayer player, List<Quest.Prize> prizeList)
		{
			foreach (Quest.Prize check in prizeList)
			{
				switch (check.PrizeType)
				{
					case PrizeType.Item:
						Item newItem = ItemManager.CreateByPartialName(check.ItemShortName, check.ItemAmount);
						player.GiveItem(newItem, BaseEntity.GiveItemReason.Crafted);
						break;
					case PrizeType.Command:
						Server.Command(check.PrizeCommand.Replace("%STEAMID%", player.UserIDString));
						break;
					case PrizeType.CustomItem:
						Item customItem = ItemManager.CreateByPartialName(check.ItemShortName, check.ItemAmount, check.ItemSkinID);
						customItem.name = check.CustomItemName;
						player.GiveItem(customItem, BaseEntity.GiveItemReason.Crafted);
						break;
					case PrizeType.BluePrint:
						Item itemBp = ItemManager.Create(ItemManager.blueprintBaseDef);
						ItemDefinition targetItem = ItemManager.FindItemDefinition(check.ItemShortName);
						if (targetItem == null) continue;
						itemBp.blueprintTarget = targetItem.isRedirectOf != null ? targetItem.isRedirectOf.itemid : targetItem.itemid;
						player.GiveItem(itemBp, BaseEntity.GiveItemReason.Crafted);
						break;
					default:
						throw new ArgumentOutOfRangeException();
				}
			}
		}

		private bool TakeItemsNeededForQuest(BasePlayer player, Quest globalQuest)
		{
			ItemDefinition idItem = ItemManager.FindItemDefinition(globalQuest.Target);
			int? item = null;
			if (player != null && player.inventory != null)
				item = player.inventory.GetAmount(idItem.itemid);
			
			if (item is 0 or null)
			{
				UINottice(player, "XDQUEST_UI_InsufficientResources".GetAdaptedMessage(player.UserIDString, idItem.displayName.english));
				return false;
			}

			if (item < globalQuest.ActionCount)
			{
				UINottice(player, "XDQUEST_UI_NotResourcesAmount".GetAdaptedMessage(player.UserIDString, idItem.displayName.english, globalQuest.ActionCount));
				return false;
			}

			if (item >= globalQuest.ActionCount)
			{
				player.inventory.Take(null, idItem.itemid, globalQuest.ActionCount);
			}

			return true;
		}

		private bool TakeSkinIdItemsForQuest(BasePlayer player, Quest globalQuest, ulong skins)
		{
			List<Item> acceptedItems = Pool.Get<List<Item>>();
			int itemAmount = 0;
			int amountQuest = globalQuest.ActionCount;
			string itemName = string.Empty;
			List<Item> items = Pool.Get<List<Item>>();
			player.inventory.GetAllItems(items);
			foreach (Item item in items)
			{
				if (item.skin == skins)
				{
					acceptedItems.Add(item);
					itemAmount += item.amount;
					itemName = item.GetName();
				}
			}
			Pool.Free(ref items);
			if (acceptedItems.Count == 0)
			{
				UINottice(player, "XDQUEST_UI_InsufficientResourcesSkin".GetAdaptedMessage(player.UserIDString));
				return false;
			}

			if (itemAmount < amountQuest)
			{
				UINottice(player, "XDQUEST_UI_NotResourcesAmount".GetAdaptedMessage(player.UserIDString, itemName, amountQuest));
				return false;
			}

			foreach (Item use in acceptedItems)
			{
				if (use.amount >= amountQuest)
				{
					use.amount -= amountQuest;
					if (use.amount == 0)
					{
						use.RemoveFromContainer();
						use.Remove();
					}

					amountQuest = 0;
				}
				else
				{
					amountQuest -= use.amount;
					use.RemoveFromContainer();
					use.Remove();
				}

				if (amountQuest == 0)
				{
					break;
				}
			}
			
			Pool.Free(ref acceptedItems);
			player.inventory.SendSnapshot();
			return true;
		}

		#endregion

		#region QuestProgress

		private void QuestProgress(ulong playerUserID, QuestType questType, string entName = "", string skinId = "", List<Item> items = null, int count = 1)
		{
			if (!_playersInfo.TryGetValue(playerUserID, out PlayerData playerData))
				return;

			List<PlayerQuest> playerQuests = playerData.CurrentPlayerQuests.FindAll(x => x.ParentQuestType == questType && !x.Finished);

			foreach (PlayerQuest quest in playerQuests)
			{
				Quest parentQuest = _questList[quest.ParentQuestID];
				if (string.IsNullOrEmpty(entName) && items == null)
				{
					quest.AddCount(count);
					return;
				}
				
				if (items != null)
				{
					ulong skinIditem;
					bool isSkinID = ulong.TryParse(parentQuest.Target, out skinIditem);
					foreach (Item item in items)
					{
						if(item.info.shortname.Equals(parentQuest.Target, StringComparison.OrdinalIgnoreCase) || (isSkinID && item.skin.Equals(skinIditem)))
							quest.AddCount(item.amount);
					}
					continue;
				}
				
				switch (questType)
				{
					case QuestType.IQCases:
					case QuestType.HarborEvent:
					case QuestType.SatelliteDishEvent:
					case QuestType.Sputnik:
					case QuestType.Caravan:
					case QuestType.Convoy:
					case QuestType.GasStationEvent:
					case QuestType.FerryTerminalEvent:
					case QuestType.Triangulation:
					case QuestType.AbandonedBases:
					case QuestType.IQDronePatrol:
					case QuestType.IQDefenderSupply:
					case QuestType.IQHeadReward:
					{
						if (parentQuest.Target.Equals(entName) || parentQuest.Target.Equals("0") || parentQuest.Target.Equals("999"))
							quest.AddCount(count);
						break;
					}
					case QuestType.Swipe:
					{
						if (parentQuest.Target.Equals(entName))
							quest.AddCount(count);
						break;
					}
					case QuestType.EntityKill:
					{
						if (parentQuest.IsMoreTarget)
						{
							foreach (string target in parentQuest.Targets)
							{
								if (entName.Equals(target, StringComparison.OrdinalIgnoreCase))
									quest.AddCount(count);
							}
						}
						else
						{
							if (entName.Equals(parentQuest.Target, StringComparison.OrdinalIgnoreCase))
								quest.AddCount(count);
						}
						break;
					}
					default:
					{
						if (entName.Equals(parentQuest.Target, StringComparison.OrdinalIgnoreCase) || skinId.Equals(parentQuest.Target))
							quest.AddCount(count);
						break;
					}
				}
			}
			
			Interface.CallHook("OnQuestProgress", playerUserID, (int)questType, entName, skinId, items, count);
		}

		#endregion

		#endregion

		#region SoundNpc

		private class NpcSound
		{
			public AudioTriggerTypes audioType;
			public float durationSeconds;

			[JsonConverter(typeof(SoundFileConverter))]
			public byte[] voiceData = Array.Empty<byte>();
		}

		private class SoundFileConverter : JsonConverter
		{
			public override bool CanConvert(Type objectType)
			{
				return objectType == typeof(byte[]);
			}

			public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
			{
				JToken value = JToken.Load(reader);
				return Convert.FromBase64String(value.ToString());
			}

			public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
			{
			}
		}

		private readonly Dictionary<string, NpcSound> _cachedSounds = new Dictionary<string, NpcSound>();

		private void LoadDataSounds()
		{
			string[] files = Interface.Oxide.DataFileSystem.GetFiles($"{Name}/Sounds/");
			foreach (string filePath in files)
			{
				string fileName = GetFileNameWithoutExtension(filePath);
				try
				{
					NpcSound data = Interface.Oxide.DataFileSystem.ReadObject<NpcSound>($"{Name}/Sounds/{fileName}");
					if (data.voiceData != null && data.voiceData.Length > 0)
					{
						_cachedSounds.Add(fileName, data);
					}
					else
					{
						PrintError($"File {fileName} is corrupted and cannot be loaded!");
					}
				}
				catch (Exception ex)
				{
					PrintError($"Error loading file {fileName}: {ex.Message}");
				}
			}
		}

		#endregion

		#region ApiLoadData

		private void LoadDataCopyPaste()
		{
			string filePath = $"copypaste/{QUEST_BUILDING_NAME}";

			if (!Interface.Oxide.DataFileSystem.ExistsDatafile(filePath))
			{
				PrintError("XDQUEST_FileNotLoad".GetAdaptedMessage(null, QUEST_BUILDING_NAME));
				NextTick(() => { Interface.Oxide.UnloadPlugin(Name); });
				return;
			}

			ClearEnt();
		}

		private void DownloadImages()
		{
			foreach (KeyValuePair<long, Quest> img in _questList)
			foreach (Quest.Prize typeimg in img.Value.PrizeList)
			{
				if (typeimg.PrizeType == PrizeType.Command)
				{
					if (!HasImage(typeimg.CommandImageUrl))
					{
						AddImage(typeimg.CommandImageUrl, typeimg.CommandImageUrl);
					}
				}
			}
		}

		#endregion

		#region ImageLoader

		private class ImageUI
		{
			private readonly string _paths;
			private readonly string _printPath;
			private readonly Dictionary<string, ImageData> _images;

			private enum ImageStatus
			{
				NotLoaded,
				Loaded,
				Failed
			}

			public ImageUI()
			{
				_paths = Instance.Name + "/Images/";
				_printPath = "data/" + _paths;
				_images = new Dictionary<string, ImageData>
				{
					{ "1", new ImageData() },
					{ "2", new ImageData() },
					{ "3", new ImageData() },
					{ "4", new ImageData() },
					{ "5", new ImageData() },
					{ "6", new ImageData() },
					{ "7", new ImageData() },
					{ "8", new ImageData() },
					{ "9", new ImageData() },
					{ "10", new ImageData() },
					{ "11", new ImageData() },
					{ "12", new ImageData() },
					{ "13", new ImageData() },
					{ "14", new ImageData() },
					{ "15", new ImageData() },
					{ "16", new ImageData() }
				};
			}

			private class ImageData
			{
				public ImageStatus Status = ImageStatus.NotLoaded;
				public string Id { get; set; }
			}

			public string GetImage(string name)
			{
				ImageData image;
				if (_images.TryGetValue(name, out image) && image.Status == ImageStatus.Loaded)
					return image.Id;
				return null;
			}

			public void DownloadImage()
			{
				KeyValuePair<string, ImageData>? image = null;
				foreach (KeyValuePair<string, ImageData> img in _images)
				{
					if (img.Value.Status == ImageStatus.NotLoaded)
					{
						image = img;
						break;
					}
				}

				if (image != null)
				{
					ServerMgr.Instance.StartCoroutine(ProcessDownloadImage(image.Value));
				}
				else
				{
					List<string> failedImages = new List<string>();

					foreach (KeyValuePair<string, ImageData> img in _images)
					{
						if (img.Value.Status == ImageStatus.Failed)
						{
							failedImages.Add(img.Key);
						}
					}

					if (failedImages.Count > 0)
					{
						string images = string.Join(", ", failedImages);
						Instance.PrintError(!RU
							? $"Failed to load the following images: {images}. Perhaps you did not upload them to the '{_printPath}' folder."
							: $"Не удалось загрузить следующие изображения: {images}. Возможно, вы не загрузили их в папку '{_printPath}'.");
						Interface.Oxide.UnloadPlugin(Instance.Name);
					}
					else
					{
						Instance.Puts(!RU
							? $"{_images.Count} images downloaded successfully!"
							: $"{_images.Count} изображений успешно загружено!");
					}
				}
			}

			public void UnloadImages()
			{
				foreach (KeyValuePair<string, ImageData> item in _images)
					if (item.Value.Status == ImageStatus.Loaded)
						if (item.Value?.Id != null)
							FileStorage.server.Remove(uint.Parse(item.Value.Id), FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID);

				_images?.Clear();
			}

			private IEnumerator ProcessDownloadImage(KeyValuePair<string, ImageData> image)
			{
				string url = "file://" + Interface.Oxide.DataDirectory + "/" + _paths + image.Key + ".png";

				using UnityWebRequest www = UnityWebRequestTexture.GetTexture(url);
				yield return www.SendWebRequest();

				if (www.result is UnityWebRequest.Result.ConnectionError or UnityWebRequest.Result.ProtocolError)
				{
					image.Value.Status = ImageStatus.Failed;
				}
				else
				{
					Texture2D tex = DownloadHandlerTexture.GetContent(www);
					image.Value.Id = FileStorage.server.Store(tex.EncodeToPNG(), FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID).ToString();
					image.Value.Status = ImageStatus.Loaded;
					UnityEngine.Object.DestroyImmediate(tex);
				}

				DownloadImage();
			}
		}

		#endregion

		#region Statistics
		private void GrabAndPostStatistics()
		{
			PrintError("XDQUEST_FileNotLoad".GetAdaptedMessage(null, QUEST_BUILDING_NAME));

			FancyMessage.Embed embed = new("XDQUEST_STAT_1".GetAdaptedMessage(),"XDQUEST_STAT_2".GetAdaptedMessage(null, _questStatistics.CompletedTasks, _questStatistics.TakenTasks, _questStatistics.DeclinedTasks));
            
			string mostExecutedInfo = ExtractQuestInfo(_questStatistics.GetTop5MostExecutedTasks());
			string leastExecutedInfo = ExtractQuestInfo(_questStatistics.GetTop5LeastExecutedTasks());
			
			FancyMessage.Embed embed2 = new("XDQUEST_STAT_3".GetAdaptedMessage(), "XDQUEST_STAT_4".GetAdaptedMessage(null, mostExecutedInfo, leastExecutedInfo));
			List<FancyMessage.Embed> embeds = new() { embed, embed2 };
			FancyMessage message = new(ConVar.Server.hostname ,embeds);

			string jsonEmbed = message.ToJson();

			SendDiscordNotification(jsonEmbed);
		}
		
		private readonly Dictionary<string, string> _headersDiscord = new()
		{
			{"Content-Type", "application/json"}
		};
		private void SendDiscordNotification(string json)
		{
			string url = $"{_config.statisticsCollectionSettings.discordWebhookUrl}?wait=true";
			webrequest.Enqueue(url, json, (code, response) =>
			{
				if (code == 200)
				{
					PrintWarning("XDQUEST_STAT_CMD_3".GetAdaptedMessage());
				}
				else
				{
					PrintError($"[SendDiscordNotification] Error: {code}\n{response}");
				}
			}, this, RequestMethod.POST, _headersDiscord, 10F);
		}
		private string ExtractQuestInfo(Dictionary<long, int> quests)
		{
			StringBuilder questInfoBuilder = new StringBuilder();

			foreach (KeyValuePair<long, int> questPair in quests)
			{
				Quest foundQuest;
				if (_questList.TryGetValue(questPair.Key, out foundQuest))
				{
					questInfoBuilder.AppendLine($"- **{foundQuest.GetDisplayName(lang.GetServerLanguage())}**: {questPair.Value}");
				}
			}

			return questInfoBuilder.ToString();
		}

		#endregion
		
		#region DiscordClass

        public class FancyMessage
        {
            [JsonProperty("content")] public string Content;

            [JsonProperty("username")] public string Username;

            [JsonProperty("avatar_url")] public string AvatarUrl;

            [JsonProperty("embeds")] public List<Embed> Embeds;

            public FancyMessage(string content, List<Embed> embeds)
            {
                Content = content;
                Username = "XDQuest Statistics";
                AvatarUrl = "https://i.imgur.com/RxgzxNW.jpg";
                Embeds = embeds;
            }

            public string ToJson() => JsonConvert.SerializeObject(this);

            public class Embed
            {
                [JsonProperty("title")] public string Title { get; }
                [JsonProperty("description")] public string Description { get; }
                [JsonProperty("color")] public int Color { get; }
                [JsonProperty("timestamp")] public string Timestamp { get; }

                public Embed(string title, string description = "")
                {
                    Title = title;
                    Description = description;
                    Color = 16689937;
                    Timestamp = DateTime.UtcNow.ToString("o");
                }
            }
            
        }

        #endregion

		#region Data
		private List<Quest> LoadQuestList()
		{
			return Interface.Oxide.DataFileSystem.ExistsDatafile($"{Name}/{_config.settings.questListDataName}")
				? Interface.Oxide.DataFileSystem.ReadObject<List<Quest>>($"{Name}/{_config.settings.questListDataName}")
				: null;
		}

		private void LoadQuestData()
		{
			List<Quest> questList = LoadQuestList();
			if (questList != null)
			{
				HashSet<long> currentQuestIds = new();
				foreach (Quest quest in questList)
				{
					currentQuestIds.Add(quest.QuestID);
					_questList.Add(quest.QuestID, quest);
					_questStatistics.TaskExecutionCounts.TryAdd(quest.QuestID, 0);
				}

				List<long> keysToRemove = new();
				foreach (long taskId in _questStatistics.TaskExecutionCounts.Keys)
					if (!currentQuestIds.Contains(taskId))
						keysToRemove.Add(taskId);
				
				foreach (long key in keysToRemove)
					_questStatistics.TaskExecutionCounts.Remove(key);
				SaveData();
			}
			else
			{
				_questList = new Dictionary<long, Quest>();
			}

			if (_questList.Count > 0)
			{
				foreach (Quest quest in _questList.Values)
				{
					if (!string.IsNullOrEmpty(quest.QuestPermission) && !permission.PermissionExists($"{Name}." + quest.QuestPermission, this))
					{
						permission.RegisterPermission($"{Name}." + quest.QuestPermission, this);
					}

					if (quest.QuestType == QuestType.EntityKill)
					{
						if (quest.Target.Contains(","))
						{
							quest.IsMoreTarget = true;
							quest.Targets = quest.Target.Split(',');
						}
					}
				}
			}
		}
		private class QuestStatistics
		{
			public int CompletedTasks, TakenTasks, DeclinedTasks;

			public Dictionary<long, int> TaskExecutionCounts = new();

			#region Metods
			public void GatherTaskStatistics(TaskType taskType, long? taskId = null)
			{
				switch (taskType)
				{
					case TaskType.Completed:
						CompletedTasks += 1;
						break;
					case TaskType.Taken:
						TakenTasks += 1;
						break;
					case TaskType.Declined:
						DeclinedTasks += 1;
						break;
					case TaskType.TaskExecution:
						if (taskId.HasValue)
						{
							if (!TaskExecutionCounts.TryAdd(taskId.Value, 1))
							{
								TaskExecutionCounts[taskId.Value] += 1;
							}
						}
						else
						{
							throw new ArgumentNullException("For TaskExecution type, taskId must be provided.");
						}

						break;
					default:
						throw new ArgumentException("Unknown task type");
				}
			}
            
			public Dictionary<long, int> GetTop5MostExecutedTasks()
			{
				List<KeyValuePair<long, int>> topTasks = new List<KeyValuePair<long, int>>();
    
				foreach (KeyValuePair<long, int> task in TaskExecutionCounts)
				{
					if (topTasks.Count < 5)
					{
						topTasks.Add(task);
						topTasks.Sort((a, b) => b.Value.CompareTo(a.Value));
					}
					else
					{
						if (task.Value > topTasks[4].Value)
						{
							topTasks[4] = task;
							topTasks.Sort((a, b) => b.Value.CompareTo(a.Value));
						}
					}
				}

				Dictionary<long, int> result = new Dictionary<long, int>();
				foreach (KeyValuePair<long, int> kvp in topTasks)
				{
					result[kvp.Key] = kvp.Value;
				}

				return result;
			}
        
			public Dictionary<long, int> GetTop5LeastExecutedTasks()
			{
				List<KeyValuePair<long, int>> leastTasks = new List<KeyValuePair<long, int>>();
    
				foreach (KeyValuePair<long, int> task in TaskExecutionCounts)
				{
					if (leastTasks.Count < 5)
					{
						leastTasks.Add(task);
						leastTasks.Sort((a, b) => a.Value.CompareTo(b.Value));
					}
					else
					{
						if (task.Value < leastTasks[4].Value)
						{
							leastTasks[4] = task;
							leastTasks.Sort((a, b) => a.Value.CompareTo(b.Value));
						}
					}
				}

				Dictionary<long, int> result = new Dictionary<long, int>();
				foreach (KeyValuePair<long, int> kvp in leastTasks)
				{
					result[kvp.Key] = kvp.Value;
				}

				return result;
			}

			#endregion
		}

		private void LoadQuestStatisticsData()
		{
			_questStatistics = Interface.Oxide.DataFileSystem.ReadObject<QuestStatistics>(this.Name + $"/QuestStatistics");
		}
		private void LoadPlayerData()
		{
			_playersInfo = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, PlayerData>>(this.Name + $"/PlayerInfo");
		}

		private void SaveData()
		{
			Interface.Oxide.DataFileSystem.WriteObject(this.Name + $"/PlayerInfo", _playersInfo);
			Interface.Oxide.DataFileSystem.WriteObject(this.Name + $"/QuestStatistics", _questStatistics);
		}

		#endregion
	}
}

namespace Oxide.Plugins.XDQuestExtensionMethods
{
	public static class ExtensionMethods
	{
		private static readonly Lang Lang = Interface.Oxide.GetLibrary<Lang>();

		#region GetLang
		
		public static string GetAdaptedMessage(this string langKey, in string userID, params object[] args)
		{
			string message = Lang.GetMessage(langKey, XDQuest.Instance, userID);
			
			StringBuilder stringBuilder = Pool.Get<StringBuilder>();
		
			try
			{
				return stringBuilder.AppendFormat(message, args).ToString();
			}
			finally
			{
				stringBuilder.Clear();
				Pool.FreeUnmanaged(ref stringBuilder);
			}
		}
		
		public static string GetAdaptedMessage(this string langKey, in string userID = null)
		{
			return Lang.GetMessage(langKey, XDQuest.Instance, userID);
		}
		
		#endregion
		
		#region Pagination

		public static IEnumerable<T> Page<T>(this List<T> source, int page, int pageSize)
		{
			int start = page * pageSize;
			int end = start + pageSize;
			for (int i = start; i < end && i < source.Count; i++)
			{
				yield return source[i];
			}
		}

		#endregion
	}
}