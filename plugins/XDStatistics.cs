using UnityEngine;
using System;
using Oxide.Game.Rust.Cui;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Plugins;
using Newtonsoft.Json;
using System.Linq;
using Newtonsoft.Json.Linq;
using Rust;
using System.Text;
using System.Collections;
using UnityEngine.Networking;
using Oxide.Core.Libraries;
using System.Text.RegularExpressions;
using Facepunch;
using Oxide.Plugins.XDStatisticsExtensionMethods;
using Rust.Ai.Gen2;
using UnityEngine.UI;

namespace Oxide.Plugins
{
    [Info("XDStatistics", "DezLife", "3.2.8")]
    [Description("Multifunctional statistics for your server!")]
    class XDStatistics : RustPlugin
    {
        #region ReferencePlugins
        [PluginReference] Plugin Friends, Clans, Battles, Duel, Economics, IQEconomic, ServerRewards, GameStoresRUST, RustStore, Duelist, EventHelper, ArenaTournament, ZLevelsRemastered, SkillTree, IQFakeActive;
        private bool IsFriends(ulong userID, ulong targetID)
        {
            if (Friends is not null)
                return Friends.Call("HasFriend", userID, targetID) is true;
    
            return RelationshipManager.ServerInstance.playerToTeam.TryGetValue(userID, out RelationshipManager.PlayerTeam team) && team.members.Contains(targetID);
        }

        private bool IsClans(string userID, string targetID)
        {
            if (Clans is null) return false;
    
            string tagUserID = Clans.Call("GetClanOf", userID) as string;
            string tagTargetID = Clans.Call("GetClanOf", targetID) as string;
    
            return tagUserID is not null && tagUserID == tagTargetID;
        }
        
        private bool IsDuel(ulong userID)
        {
            object playerId = ObjectCache.Get(userID);
            BasePlayer player = (Duel is not null || Duelist is not null) ? BasePlayer.FindByID(userID) : null;

            return EventAtEvent(playerId) 
                   || IsPlayerOnBattle(playerId) 
                   || IsPlayerOnActiveDuel(player)
                   || InEvent(player) 
                   || IsOnTournament(playerId);

            bool EventAtEvent(object id) => EventHelper?.Call("EMAtEvent", id) is true;
    
            bool IsPlayerOnBattle(object id) => Battles?.Call<bool>("IsPlayerOnBattle", id) == true;
    
            bool IsPlayerOnActiveDuel(BasePlayer pl) => Duel?.Call<bool>("IsPlayerOnActiveDuel", pl) == true;
    
            bool InEvent(BasePlayer pl) => Duelist?.Call<bool>("inEvent", pl) == true;
    
            bool IsOnTournament(object id) => ArenaTournament?.Call<bool>("IsOnTournament", id) == true;
        }

        #endregion
        
        #region Var
        private const bool RU = false;
        public static XDStatistics Instance;

        private ImageUI _imageUI;
        private const string PermAdmin = "XDStatistics.admin";
        private const string PermReset = "XDStatistics.reset";
        private const string PermAvailability = "XDStatistics.availability";
        
        private Dictionary<string, ItemDisplayName> _itemName = new();
        private Dictionary<CatType, Dictionary<ulong, CashedData>> cashedTopUser = new();
        
        public class CashedData
        {
            public readonly int value;
            public readonly float valueScore;
            public readonly string playerName;

            public int FullValue()
            {
                if (valueScore == 0)
                    return value;
                return (int)valueScore;
            }
            
            public CashedData(int value, string playerName, float valueScore = 0)
            {
                this.value = value;
                this.playerName = playerName;
                this.valueScore = valueScore;
            }
        }
        
        private Dictionary<uint, string> _prefabID2Item = new();
        private Dictionary<string, string> _prefabNameItem = new()
        {
            ["40mm_grenade_he"] = "multiplegrenadelauncher",
            ["grenade.beancan.deployed"] = "grenade.beancan",
            ["grenade.f1.deployed"] = "grenade.f1",
            ["grenade.molotov.deployed"] = "grenade.molotov",
            ["grenade.flashbang.deployed"] = "grenade.flashbang",
            ["explosive.satchel.deployed"] = "explosive.satchel",
            ["explosive.timed.deployed"] = "explosive.timed",
            ["rocket_basic"] = "ammo.rocket.basic",
            ["rocket_hv"] = "ammo.rocket.hv",
            ["rocket_fire"] = "ammo.rocket.fire",
            ["survey_charge.deployed"] = "surveycharge"
        };
        private List<int> _alowedSeedId = new(){ 1548091822, 1771755747, 1112162468, 1367190888,-1962971928, -2086926071, 44433072, -567909622, 1272194103, 854447607,1660145984, 1783512007, -858312878 };

        #endregion

        #region Enum

        private enum CatType
        {
            Score,
            Killer,
            KillerNpc,
            KillerAnimal,
            PlayedTime,
            Gather,
            Explosive,
            Builder,
            Fermer
        }
        
        private enum StoreType
        {
            GameStore,
            MoscowOVH,
            None
        }

        #endregion
                                                                                                                                                                                                                                          
        #region Lang
        
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["PRINT_TOP_CATEGORY_Killer"] = "Top 5 <color=#4286f4>Killers</color>\n{0}</size>",
                ["PRINT_TOP_CATEGORY_KillerNpc"] = "Top 5 <color=#4286f4>NPC killers</color>\n{0}</size>",
                ["PRINT_TOP_CATEGORY_KillerAnimal"] = "Top 5 <color=#4286f4>Animal killers</color>\n{0}</size>",
                ["PRINT_TOP_CATEGORY_Gather"] = "Top 5 <color=#4286f4>Farmers</color>\n{0}</size>",
                ["PRINT_TOP_CATEGORY_Explosive"] = "Top 5 <color=#4286f4>Explosions</color>\n{0}</size>",
                ["PRINT_TOP_CATEGORY_PlayedTime"] = "Top 5 <color=#4286f4>Most Time Played</color>\n{0}</size>",
                ["PRINT_TOP_CATEGORY_Builder"] = "Top 5 <color=#4286f4>Builders</color>\n{0}</size>",
                ["PRINT_TOP_CATEGORY_Score"] = "Top 5 <color=#4286f4>Most points</color>\n{0}</size>",
                ["PRINT_TOP_CATEGORY_Fermer"] = "Top 5 <color=#4286f4>Fermer</color>\n{0}</size>",
                
                ["STAT_TOP_PLAYER_WIPE_Score"] = "Congratulations!\nYou successfully held the {0} position in the previous wipe in the category <color=#4286f4>HIGHEST SCORE</color>\nYou received a well deserved Reward!",
                ["STAT_TOP_PLAYER_WIPE_PlayedTime"] = "Congratulations!\nYou successfully held the {0} position in the previous wipe in the category <color=#4286f4>MOST TIME PLAYED</color>\nYou received a well deserved Reward!",
                ["STAT_TOP_PLAYER_WIPE_Explosive"] = "Congratulations!\nYou successfully held the {0} position in the previous wipe in the category <color=#4286f4>MOST EXPLOSIONS</color>\nYou received a well deserved Reward!",
                ["STAT_TOP_PLAYER_WIPE_Gather"] = "Congratulations!\nYou successfully held the {0} position in the previous wipe in the category <color=#4286f4>MOST CROPS FARMED</color>\nYou received a well deserved Reward!",
                ["STAT_TOP_PLAYER_WIPE_Killer"] = "Congratulations!\nYou successfully held the {0} position in the previous wipe in the category <color=#4286f4>MOST KILLS</color>\nYou received a well deserved Reward!",
                ["STAT_TOP_PLAYER_WIPE_KillerNpc"] = "Congratulations!\nYou successfully held the {0} position in the previous wipe in the category <color=#4286f4>NPC Killer</color>\nYou received a well deserved Reward!",
                ["STAT_TOP_PLAYER_WIPE_KillerAnimal"] = "Congratulations!\nYou successfully held the {0} position in the previous wipe in the category <color=#4286f4>ANIMAL Killer</color>\nYou received a well deserved Reward!",
                ["STAT_TOP_PLAYER_WIPE_Builder"] = "Congratulations!\nYou successfully held the {0} position in the previous wipe in the category <color=#4286f4>Builder</color>\nYou received a well deserved Reward!",
                ["STAT_TOP_PLAYER_WIPE_Fermer"] = "Congratulations!\nYou successfully held the {0} position in the previous wipe in the category <color=#4286f4>Fermer</color>\nYou received a well deserved Reward!",
                
                ["STAT_USER_TOTAL_GATHERED"] = "Total Gathered:",
                ["STAT_USER_TOTAL_EXPLODED"] = "Total Explosions:",
                ["STAT_USER_TOTAL_GROWED"] = "Total Farmed:",
                ["STAT_UI_MY_STAT"] = "My Statistics",
                ["STAT_UI_TOP_TEN"] = "Top 10 Players",
                ["STAT_UI_SEARCH"] = "Search",
                ["STAT_UI_INFO"] = "Player Information {0}",
                ["STAT_UI_ACTIVITY"] = "Activity",
                ["STAT_UI_ACTIVITY_TODAY"] = "Today: {0}",
                ["STAT_UI_ACTIVITY_TOTAL"] = "All Time: {0}",
                ["STAT_UI_SETTINGS"] = "Settings",
                ["STAT_UI_PLACE_TOP"] = "Place On Top: {0}",
                ["STAT_UI_SCORE"] = "Score: {0}",
                ["STAT_UI_PVP"] = "PvP Statistics",
                ["STAT_UI_FAVORITE_WEAPON"] = "Favorite Weapon",
                ["STAT_UI_PVP_KILLS"] = "Kills",
                ["STAT_UI_PVP_KILLS_NPC"] = "Kills NPC",
                ["STAT_UI_PVP_DEATH"] = "Deaths",
                ["STAT_UI_PVP_KDR"] = "K/D",
                ["STAT_UI_FAVORITE_WEAPON_KILLS"] = "Kills: {0}\nHits: {1}",
                ["STAT_UI_FAVORITE_WEAPON_NOT_DATA"] = "Data is still being calculated..",
                ["STAT_UI_OTHER_STAT"] = "Other Statistics",
                ["STAT_UI_HIDE_STAT"] = "Public Profile",
                ["STAT_UI_CONFIRM"] = "Are You Sure?",
                ["STAT_UI_CONFIRM_YES"] = "Yes",
                ["STAT_UI_CONFIRM_NO"] = "No",
                ["STAT_UI_RESET_STAT"] = "Reset Statistics",
                ["STAT_UI_CRATE_OPEN"] = "Crates Opened: {0}",
                ["STAT_UI_BARREL_KILL"] = "Barrels Destroyed: {0}",
                ["STAT_UI_ANIMAL_KILL"] = "Animal Kills: {0}",
                ["STAT_UI_HELI_KILL"] = "Helicopter Kills: {0}",
                ["STAT_UI_BRADLEY_KILL"] = "Bradley Kills: {0}",
                ["STAT_UI_NPC_KILL"] = "NPC Kills: {0}",
                ["STAT_UI_BTN_MORE"] = "Show More",
                ["STAT_UI_CATEGORY_GATHER"] = "Gather",
                ["STAT_UI_CATEGORY_EXPLOSED"] = "Explosions",
                ["STAT_UI_CATEGORY_PLANT"] = "Farming",
                ["STAT_UI_CATEGORY_TOP_KILLER"] = "Top 10 Killers",
                ["STAT_UI_CATEGORY_TOP_KILLER_ANIMALS"] = "Top 10 Animal Killers",
                ["STAT_UI_CATEGORY_TOP_NPCKILLER"] = "Top 10 NPC Killers",
                ["STAT_UI_CATEGORY_TOP_TIME"] = "Top 10 Most Time Played",
                ["STAT_UI_CATEGORY_TOP_GATHER"] = "Top 10 Gatherers",
                ["STAT_UI_CATEGORY_TOP_SCORE"] = "Top 10 Most Score",
                ["STAT_UI_CATEGORY_TOP_EXPLOSED"] = "Top 10 Explosions",
                ["STAT_UI_TOP_TEN_FOOTER"] = "The leaderboard updates every 3 minutes",
                ["STAT_PRINT_WIPE"] = "Wipe Detected. Data was successfully cleared!",
                ["STAT_PRINT_LOAD"] = "[SplitDatafile] Loaded {0} players | Cleared old players {1}",
                ["STAT_CMD_1"] = "No Permission!!",
                ["STAT_CMD_3"] = "The specified playername could not be found. Please use their SteamID.",
                ["STAT_CMD_4"] = "Found several players with similar names: {0}",
                ["STAT_CMD_5"] = "Player not found!",
                ["STAT_CMD_6"] = "Player {0} is already ignored",
                ["STAT_CMD_7"] = "You have successfully added a player {0} to the ignore list",
                ["STAT_CMD_8"] = "The player {0} is not in the ignore list",
                ["STAT_CMD_9"] = "You have successfully removed the player {0} from the ignore list",
                ["STAT_CMD_10"] = "Player {0} successfully credited {1} score",
                ["STAT_CMD_11"] = "Player {0} successfully removed {1} score",
                ["STAT_CMD_12"] = "List of available categories:\n{0}",
                ["STAT_COMMAND_SYNTAX_ERROR"] = "Incorrect syntax! Use: {0}",
                ["STAT_INVALID_PLAYER_ID_INPUT"] = "Invalid input! Please enter a valid player ID.",
                ["STAT_NOT_A_STEAM_ID"] = "The entered ID is not a SteamID. Please check and try again.",
                ["STAT_PLAYER_NOT_FOUND_BY_STEAMID"] = "Player with the specified Steam ID not found.",
                ["STAT_ADMIN_HIDE_STAT"] = "You've been added to the ignore list. You will not have access to the statistics. If this is an error, Please contact the Administrator!",
                ["STAT_TOP_VK_SCORE"] = "Топ {0} игрока по очкам\n {1}",
                ["STAT_TOP_VK_KILLER"] = "Топ {0} игрока по убийствам\n {1}",
                ["STAT_TOP_VK_TIME"] = "Топ {0} игрока по онлайну\n {1}",
                ["STAT_TOP_VK_FARM"] = "Топ {0} игрока по фарму\n {1}",
                ["STAT_TOP_VK_RAID"] = "Топ {0} игрока по рейдам\n {1}",
                ["STAT_TOP_VK_KILLER_NPC"] = "Топ {0} игрока по убийствам NPC\n {1}",
                ["STAT_TOP_VK_KILLER_ANIMAL"] = "Топ {0} игрока по убийствам животных\n {1}",

            }, this);
            
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["PRINT_TOP_CATEGORY_Killer"] = "Топ 5 <color=#4286f4>киллеры</color>\n{0}</size>",
                ["PRINT_TOP_CATEGORY_KillerNpc"] = "Топ 5 <color=#4286f4>убийцы NPC</color>\n{0}</size>",
                ["PRINT_TOP_CATEGORY_KillerAnimal"] = "Топ 5 <color=#4286f4>убийцы животных</color>\n{0}</size>",
                ["PRINT_TOP_CATEGORY_Gather"] = "Топ 5 <color=#4286f4>фармеры</color>\n{0}</size>",
                ["PRINT_TOP_CATEGORY_Explosive"] = "Топ 5 <color=#4286f4>рейдеры</color>\n{0}</size>",
                ["PRINT_TOP_CATEGORY_PlayedTime"] = "Топ 5 <color=#4286f4>долгожителей</color>\n{0}</size>",
                ["PRINT_TOP_CATEGORY_Builder"] = "Топ 5 <color=#4286f4>строители</color>\n{0}</size>",
                ["PRINT_TOP_CATEGORY_Score"] = "Топ 5 по <color=#4286f4>очкам</color>\n{0}</size>",
                ["PRINT_TOP_CATEGORY_Fermer"] = "Топ 5 <color=#4286f4>фермеров</color>\n{0}</size>",
                
                ["STAT_TOP_PLAYER_WIPE_Score"] = "Поздравляю!\nВ прошлом вайпе вы успешно удерживали {0} позицию в категории <color=#4286f4>SCORE</color>\nВы заслужено получаете награду!",
                ["STAT_TOP_PLAYER_WIPE_PlayedTime"] = "Поздравляю!\nВ прошлом вайпе вы успешно удерживали {0} позицию в категории <color=#4286f4>Долгожитель</color>\nВы заслужено получаете награду!",
                ["STAT_TOP_PLAYER_WIPE_Explosive"] = "Поздравляю!\nВ прошлом вайпе вы успешно удерживали {0} позицию в категории <color=#4286f4>Рейдер</color>\nВы заслужено получаете награду!",
                ["STAT_TOP_PLAYER_WIPE_Gather"] = "Поздравляю!\nВ прошлом вайпе вы успешно удерживали {0} позицию в категории <color=#4286f4>Добытчик</color>\nВы заслужено получаете награду!",
                ["STAT_TOP_PLAYER_WIPE_Killer"] = "Поздравляю!\nВ прошлом вайпе вы успешно удерживали {0} позицию в категории <color=#4286f4>Киллер</color>\nВы заслужено получаете награду!",
                ["STAT_TOP_PLAYER_WIPE_KillerNpc"] = "Поздравляю!\nВ прошлом вайпе вы успешно удерживали {0} позицию в категории <color=#4286f4>Убийца нпс</color>\nВы заслужено получаете награду!",
                ["STAT_TOP_PLAYER_WIPE_KillerAnimal"] = "Поздравляю!\nВ прошлом вайпе вы успешно удерживали {0} позицию в категории <color=#4286f4>Убийца животных</color>\nВы заслужено получаете награду!",
                ["STAT_TOP_PLAYER_WIPE_Builder"] = "Поздравляю!\nВ прошлом вайпе вы успешно удерживали {0} позицию в категории <color=#4286f4>Строитель</color>\nВы заслужено получаете награду!",
                ["STAT_TOP_PLAYER_WIPE_Fermer"] = "Поздравляю!\nВ прошлом вайпе вы успешно удерживали {0} позицию в категории <color=#4286f4>Фермер</color>\nВы заслужено получаете награду!",
                
                ["STAT_USER_TOTAL_GATHERED"] = "Всего добыто:",
                ["STAT_USER_TOTAL_EXPLODED"] = "Всего взорвано:",
                ["STAT_USER_TOTAL_GROWED"] = "Всего выращено:",
                ["STAT_UI_MY_STAT"] = "Моя Статистика",
                ["STAT_UI_TOP_TEN"] = "Топ 10 игроков",
                ["STAT_UI_SEARCH"] = "Поиск",
                ["STAT_UI_INFO"] = "Информация о профиле {0}",
                ["STAT_UI_ACTIVITY"] = "Активность",
                ["STAT_UI_ACTIVITY_TODAY"] = "Сегодня: {0}",
                ["STAT_UI_ACTIVITY_TOTAL"] = "За все время: {0}",
                ["STAT_UI_SETTINGS"] = "Настройки",
                ["STAT_UI_PLACE_TOP"] = "Место в топе: {0}",
                ["STAT_UI_SCORE"] = "SCORE: {0}",
                ["STAT_UI_PVP"] = "PVP статистика",
                ["STAT_UI_FAVORITE_WEAPON"] = "Фаворитное оружие",
                ["STAT_UI_PVP_KILLS"] = "Убийств",
                ["STAT_UI_PVP_KILLS_NPC"] = "Убийств NPC",
                ["STAT_UI_PVP_DEATH"] = "Смертей",
                ["STAT_UI_PVP_KDR"] = "K/D",
                ["STAT_UI_FAVORITE_WEAPON_KILLS"] = "Убийств: {0}\nПопаданий: {1}",
                ["STAT_UI_FAVORITE_WEAPON_NOT_DATA"] = "Данные еще собираются...",
                ["STAT_UI_OTHER_STAT"] = "Другая статистика",
                ["STAT_UI_HIDE_STAT"] = "Общедоступный профиль",
                ["STAT_UI_CONFIRM"] = "Вы уверены ?",
                ["STAT_UI_CONFIRM_YES"] = "Да",
                ["STAT_UI_CONFIRM_NO"] = "Нет",
                ["STAT_UI_RESET_STAT"] = "Обнулить статистику",
                ["STAT_UI_CRATE_OPEN"] = "Открыто ящиков: {0}",
                ["STAT_UI_BARREL_KILL"] = "Разбито бочек: {0}",
                ["STAT_UI_ANIMAL_KILL"] = "Убито животных: {0}",
                ["STAT_UI_HELI_KILL"] = "Сбито вертолетов: {0}",
                ["STAT_UI_BRADLEY_KILL"] = "Танков уничтожено: {0}",
                ["STAT_UI_NPC_KILL"] = "Убито NPC: {0}",
                ["STAT_UI_BTN_MORE"] = "Показать еще",
                ["STAT_UI_CATEGORY_GATHER"] = "Добыча",
                ["STAT_UI_CATEGORY_EXPLOSED"] = "Взрывчатка",
                ["STAT_UI_CATEGORY_PLANT"] = "Фермерство",
                ["STAT_UI_CATEGORY_TOP_KILLER"] = "Топ 10 киллеров",
                ["STAT_UI_CATEGORY_TOP_KILLER_ANIMALS"] = "Топ 10 убийц животных",
                ["STAT_UI_CATEGORY_TOP_NPCKILLER"] = "Топ 10 убийц npc",
                ["STAT_UI_CATEGORY_TOP_TIME"] = "Топ 10 по онлайну",
                ["STAT_UI_CATEGORY_TOP_GATHER"] = "Топ 10 фармил",
                ["STAT_UI_CATEGORY_TOP_SCORE"] = "Топ 10 по очкам",
                ["STAT_UI_CATEGORY_TOP_EXPLOSED"] = "Топ 10 рейдеров",
                ["STAT_UI_TOP_TEN_FOOTER"] = "Таблица лидеров обновляется каждые 3 минуты",
                ["STAT_PRINT_WIPE"] = "Произошел вайп. Данные успешно удалены!",
                ["STAT_PRINT_LOAD"] = "[SplitDatafile] Загружено {0} игроков | Очистили старых игроков {1}",
                ["STAT_CMD_1"] = "Недостаточно прав!",
                ["STAT_CMD_3"] = "Указанный игрок не найден. Для более точного поиска укажите его SteamID.",
                ["STAT_CMD_4"] = "Найдено несколько игроков с похожим именем: {0}",
                ["STAT_CMD_5"] = "Игрок не найден!",
                ["STAT_CMD_6"] = "Игрок {0} уже игнорируется",
                ["STAT_CMD_7"] = "Вы успешно добавили игрока {0} в игнор лист",
                ["STAT_CMD_8"] = "Игрока {0} нет в списке игнорируемых",
                ["STAT_CMD_9"] = "Вы успешно убрали игрока {0} из игнор листа",
                ["STAT_CMD_10"] = "Игроку {0} успешно зачислено {1} очков",
                ["STAT_CMD_11"] = "Игроку {0} успешно снято {1} очков",
                ["STAT_CMD_12"] = "Список доступных категорий:\n{0}",
                ["STAT_COMMAND_SYNTAX_ERROR"] = "Неверный синтаксис! Используйте: {0}",
                ["STAT_INVALID_PLAYER_ID_INPUT"] = "Неверный ввод! Пожалуйста, введите корректный ID игрока.",
                ["STAT_NOT_A_STEAM_ID"] = "Введенный ID не является SteamID. Пожалуйста, проверьте и попробуйте снова.",
                ["STAT_PLAYER_NOT_FOUND_BY_STEAMID"] = "Игрок с указанным Steam ID не найден.",
                ["STAT_ADMIN_HIDE_STAT"] = "Вы добавлены в игнор лист. У вас нет доступа к статистики, если это ошибка, свяжитесь с администратором!",
                ["STAT_TOP_VK_SCORE"] = "Топ {0} игрока по очкам\n {1}",
                ["STAT_TOP_VK_KILLER"] = "Топ {0} игрока по убийствам\n {1}",
                ["STAT_TOP_VK_TIME"] = "Топ {0} игрока по онлайну\n {1}",
                ["STAT_TOP_VK_FARM"] = "Топ {0} игрока по фарму\n {1}",
                ["STAT_TOP_VK_RAID"] = "Топ {0} игрока по рейдам\n {1}",
                ["STAT_TOP_VK_KILLER_NPC"] = "Топ {0} игрока по убийствам NPC\n {1}",
                ["STAT_TOP_VK_KILLER_ANIMAL"] = "Топ {0} игрока по убийствам животных\n {1}",

            }, this, "ru");
        }

        #endregion
        
        #region Configuration
        private Configuration _config;

        private class Configuration
        {
            public class SettingsInterface
            {
                [JsonProperty(RU ? "Цвет плашки заднего фона в топ 10 за 1 место" : "Background color in the top 10 for 1st place")]
                public string ColorTop1 = "1 0.8431373 0 0.49";
                [JsonProperty(RU ? "Цвет плашки заднего фона в топ 10 за 2 место" : "Background color in the top 10 for 2nd place")]
                public string ColorTop2 = "0.7529412 0.7529412 0.7529412 0.49";
                [JsonProperty(RU ? "Цвет плашки заднего фона в топ 10 за 3 место" : "Background color in the top 10 for 3rd place")]
                public string ColorTop3 = "0.80392 0.49803 0.1960784 0.49";
                
                [JsonProperty(RU ? "Использовать альтернативный задний фон ?" : "Should I use an alternative background?")]
                public bool useCustomBackGround = false;
                
                [JsonProperty(RU ? "Цвет заднего фона" : "Background color")]
                public string BackgroundColor = "0.235 0.227 0.180 0.90";
                [JsonProperty(RU ? "Цвет спрайта и материала" : "The color of the sprite and the material")]
                public string BackgroundColorSpriteAndMaterial = "0.141 0.137 0.109 1";
                [JsonProperty("Sprite")]
                public string BackgroundSprite = "assets/content/ui/ui.background.transparent.radial.psd";
                [JsonProperty("Material")]
                public string BackgroundMaterial = "assets/content/ui/namefontmaterial.mat";
            }
            public class DiscordMessage
            {
                [JsonProperty(RU ? "Отправлять в Discord топ-5 лучших игроков по разным категориям?" : "Send top 5 best players in various categories to Discord?")]
                public bool discordTopFiveUse = false;
                [JsonProperty(RU ? "Список категорий для отправки случайных топ 5 игроков в категории в Discord (Список доступных категорий - stat.category list)" : "List of categories for sending random top 5 players in a category on Discord (List of available categories - stat.category list)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
                public List<string> discordCategories = new()
                {
                    "Score",
                    "Killer",
                    "KillerNpc",
                    "KillerAnimal",
                    "PlayedTime",
                    "Gather",
                    "Explosive",
                    "Builder",
                    "Fermer"
                };
                [JsonIgnore]
                public List<CatType> _discordCategories = new();
                [JsonProperty(RU ? "Как часто отправлять сообщение? (Секунды)" : "How often should the message be sent? (Seconds)")]
                public int discordSendTopTime = 600;
                [JsonProperty(RU ? "WebHook Discord" : "Discord WebHook")]
                public string weebHook = string.Empty;
                [JsonProperty(RU ? "Цвет линии в сообщении (или несколько цветов)" : "The color(s) of the line in the message", ObjectCreationHandling = ObjectCreationHandling.Replace)]
                public int[] colorLines = new int[] { 53380, 9359868, 11253955 };
                [JsonProperty(RU ? "Дополнительный текст к сообщению" : "Additional text for the message")]
                public string message = string.Empty;
            }
            
            public class ChatMessage
            {
                [JsonProperty(RU ? "Отправлять в чат сообщения с топ-5 игроками по разным категориям" : "Send chat messages with top 5 players in various categories")]
                public bool chatSendTop = true;
                [JsonProperty(RU ? "Список категорий для отправки случайных топ 5 игроков в категории в игровой чат (Список доступных категорий - stat.category list)" : "List of categories for sending random top 5 players in a category to the game chat (List of available categories - stat.category list)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
                public List<string> chatCategories = new()
                {
                    "Score",
                    "Killer",
                    "KillerNpc",
                    "KillerAnimal",
                    "PlayedTime",
                    "Gather",
                    "Explosive",
                    "Builder",
                    "Fermer"
                };
                [JsonIgnore]
                public List<CatType> _chatCategories = new();
                [JsonProperty(RU ? "Как часто отправлять сообщение? (Секунды)" : "How often should the message be sent? (Seconds)")]
                public int chatSendTopTime = 600;
            }

            public class Settings
            {
                [JsonProperty(RU ? "Чат-команды для открытия статистики" : "Chat commands for opening statistics", ObjectCreationHandling = ObjectCreationHandling.Replace)]
                public List<string> chatCommandOpenStat = new List<string> { "stat", "top" };
                [JsonProperty(RU ? "Консольная команда для открытия статистики" : "Console command to open statistics")]
                public string consoleCommandOpenStat = "stat";
                [JsonProperty(RU ? "Включить возможность сбросить свою статистику? (требуется XDStatistics.reset)" : "Enable the ability to reset your stats? (requires XDStatistics.reset)")]
                public bool dropStatUse = false;
                [JsonProperty(RU
                    ? "Включить возможность скрыть свою статистику от других пользователей? (требуется XDStatistics.availability)"
                    : "Enable the ability to hide your statistics from other users? (requires XDStatistics.availability)")]
                public bool availabilityUse = true;
                [JsonProperty(RU ? "Очищать данные при вайпе" : "Clear data when wiped")]
                public bool wipeData = true;
                [JsonProperty(RU ? "Как часто (в минутах) будут сохраняться данные?" : "How often (in minutes) will data be saved?")]
                public int dataSaveTime = 30;
                [JsonProperty(RU ? "Учитывать убийства NPC для выбора любимого оружия?" : "Consider NPC kills to choose your favorite weapon?")]
                public bool npsDeathUse = false;
                [JsonProperty(RU ? "Учитывать убийства NPC в основную статистику убийств" : "Include NPC kills in the main kill statistics")]
                public bool npsDeathUseKils = false;
                [JsonProperty(RU ? "У вас сервер в режиме PVE?" : "Is your server in PVE mode?")]
                public bool pveServerMode = false;
                [JsonProperty(RU ? "Список игроков (SteamID), которые не будут включены в статистику" : "List of players (SteamID) who will not be included in the statistics",
                    ObjectCreationHandling = ObjectCreationHandling.Replace)]
                public List<ulong> ignoreList = new List<ulong>();
                [JsonProperty(RU ? "IQFakeActive : Использовать совместную работу (true - да/false - нет)" : "IQFakeActive : Use collaboration (true - yes/false - no)")]
                public Boolean IQFakeActiveUse;
            }
            
            public class SettingsIgnore
            {
                [JsonProperty(RU ? "Засчитывать убийства спящих(оффлайн) игроков?" : "Count killings of sleeping(offline) players?")]
                public bool countSleepingPlayerKills = true;
                [JsonProperty(RU ? "Засчитывать убийства игроков, у которых нет в инвентаре предметов из категории оружия?" : "Count killings of players without any weapons in inventory?")]
                public bool countKillsWithoutWeapons = true;
                [JsonProperty(RU ? "Черный список предметов для игнорирования (Эти предметы не будут считаться оружием)" : "Blacklist of items to ignore (These items won't be considered as weapons)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
                public List<string> itemsToIgnoreList = new() { "Rock", };
            }
            

            public class SettingsScore
            {
                [JsonProperty(RU ? "Очки за крафт" : "Points for crafting")]
                public float craftScore = 1;
                [JsonProperty(RU ? "Очки за разбивание бочек" : "Points for breaking barrels")]
                public float barrelScore = 1;
                [JsonProperty(RU ? "Очки за установку строительных блоков" : "Points for placing building blocks")]
                public float BuildingScore = 1;
                [JsonProperty(RU ? "Очки за использование взрывчатых предметов" : "Points for using explosive items")]
                public Dictionary<string, float> ExplosionScore = new();
                [JsonProperty(RU ? "Очки за добычу ресурсов" : "Points for gathering resources")]
                public Dictionary<string, float> GatherScore = new();
                [JsonProperty(RU ? "Очки за найденный скрап" : "Points for collected scrap")]
                public float ScrapScore = 0.5f;
                [JsonProperty(RU ? "Очки за сбор урожая (с плантации)" : "Points for harvesting (from plantation)")]
                public float PlantScore = 0.2f;
                [JsonProperty(RU ? "Очки за убийство животных" : "Points for killing animals")]
                public float AnimalScore = 1;
                [JsonProperty(RU ? "Очки за сбитие вертолета" : "Points for shooting down a helicopter")]
                public float HeliScore = 5;
                [JsonProperty(RU ? "Очки за взрыв танка" : "Points for blowing up a tank")]
                public float BradleyScore = 5;
                [JsonProperty(RU ? "Очки за убийство NPC" : "Points for killing NPCs")]
                public float NpcScore = 5;
                [JsonProperty(RU ? "Очки за убийство игроков" : "Points for killing players")]
                public float PlayerScore = 10;
                [JsonProperty(RU ? "Очки за проведенное время на сервере (за каждую минуту)" : "Points for time spent on the server (per minute)")]
                public float TimeScore = 0.2f;
                [JsonProperty(RU ? "Сколько очков отнять за самоубийство?" : "How many points to deduct for suicide?")]
                public float SuicideScore = 2;
                [JsonProperty(RU ? "Сколько очков отнять за смерть?" : "How many points to deduct for death?")]
                public float DeathScore = 1;
                [JsonProperty(RU ? "Черный список ресурсов и взрывчатых предметов" : "Blacklist of resources and explosive items", ObjectCreationHandling = ObjectCreationHandling.Replace)]
                public List<string> blackListed = new() { "ammo.rifle.explosive" };
                [JsonProperty(RU ? "Черный список предметов для крафта" : "Blacklist of crafting items", ObjectCreationHandling = ObjectCreationHandling.Replace)]
                public List<string> blackListedCraft = new() { "photo", "note" };
            }

            public class SettingsPrize
            {

                [JsonProperty(RU ? "Использовать автоматическую выдачу наград при вайпе сервера" : "Enable automatic prize distribution on server wipe")]
                public bool prizeUse = false;
                
                [JsonProperty(RU ? "[GameStores] ID магазина" : "[RUSSIA][GameStores] Shop ID")]
                public string ShopID = "";
                [JsonProperty(RU ? "[GameStores] ID сервера" : "[RUSSIA][GameStores] Server ID")]
                public string ServerID = "";
                [JsonProperty(RU ? "[GameStores] Секретный ключ" : "[RUSSIA][GameStores] Secret Key")]
                public string SecretKey = "";
                
                
                [JsonProperty(RU ? "Настройка наград по категориям (Key - Название категории) (Список доступных категорий - stat.category list)" : "Configure prizes by category (Key - Category name) (Available categories - stat.category list)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
                public Dictionary<string, List<Reward>> PrizeByCategory = new()
                {
                    ["Score"] = new List<Reward>()
                    {
                        new()
                        {
                            top = 1
                        },
                        new()
                        {
                            top = 2
                        },
                        new()
                        {
                            top = 3
                        }
                    }
                };

                [JsonIgnore] public List<CatType> rewardsCategoryList = new();
                public class Reward
                {
                    [JsonProperty(RU ? "За какое место вручать данную награду награду" : "Which ranking to award? (from 1 to 3). If you don't need to award this category, remove all related rewards.")]
                    public int top = 1;
                    
                    [JsonProperty(RU ? "Дополнительный текст, получаемый игроком при входе на сервер (например, информация о полученной награде)" : "Additional text received by the player upon server entry (e.g., information about the reward received)")]
                    public string entryRewardMessage = "";
                    
                    [JsonProperty(RU ? "Выдавать награду после того как игрок зайдет на сервер ? (Нужно для типов наград где требуется чтоб игрок был онлайн. Например экономики)" : "Give the reward after the player logs into the server? (Needed for reward types that require the player to be online, such as economy rewards)")]
                    public bool rewardOnConnected = false;
                    
                    public class CommandPrize
                    {
                        [JsonProperty(RU ? "Выдавать награду в виде команды?" : "Award prize as a command?")]
                        public bool commandPrizeUse = false;
                        [JsonProperty(RU ? "Команды, используемые в качестве приза (%STEAMID% - Player ID)" : "Commands used as prizes (%STEAMID% - Player ID)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
                        public List<string> commandPrizeList = new()
                        {
                            "[Examples] oxide.grant user %STEAMID% Plugins.Permissions",
                            "[Examples - IQPermissions] grantperm %STEAMID% Permission Time"
                        };
                    }
                    
                    public class EconomicsPrize
                    {
                        [JsonProperty(RU ? "Выдавать награду в виде экономики (IQEconomic/Economics/ServerRewards)" : "Award prize in economic currency (IQEconomic/Economics/ServerRewards)")]
                        public bool economicsPrizeUse = false;
                        [JsonProperty(RU ? "Количество начисляемой валюты в экономику (IQEconomic/Economics/ServerRewards)" : "Amount of currency to credit in economy (IQEconomic/Economics/ServerRewards)")]
                        public int balanceEconomicsPlus = 100;
                    }
                    
                    public class MarketPlacePrize
                    {
                        [JsonProperty(RU ? "Выдавать награду в виде баланса в магазине (GameStore/MoscowOVH)" : "[RUSSIA]Award prize as store balance (GameStore/MoscowOVH)?")]
                        public bool marketplacePrizeUse = false;
                        [JsonProperty(RU ? "Выбор магазина для награды (GameStore - 0, MoscowOVH - 1, None - 2)" : "[RUSSIA]Select store for prize distribution (GameStore - 0, MoscowOVH - 1, None - 2)")]
                        public StoreType StoreType = StoreType.None;
                        [JsonProperty(RU ? "Сумма начисления на баланс (GameStore/MoscowOVH)" : "[RUSSIA]Amount to credit to balance (GameStore/MoscowOVH)")]
                        public int balancePlus = 30;
                        [JsonProperty(RU ? "Сообщение для истории покупок в GameStore" : "[RUSSIA]GameStore purchase history message")]
                        public string balancePlusMess = string.Empty;
                    }
                    
                    [JsonProperty(RU ? "Настройка команд как награды" : "Configure commands as rewards")]
                    public CommandPrize rewardCommandsConfiguration = new();
                    [JsonProperty(RU ? "Настройка награды в виде экономики (IQEconomic/Economics/ServerRewards)" : "Configure economic rewards (IQEconomic/Economics/ServerRewards)")]
                    public EconomicsPrize rewardEconomicsConfiguration = new();
                    [JsonProperty(RU ? "Настройка награды в виде баланса (GameStore/MoscowOVH)" : "[RUSSIA]Configure prize as balance (GameStore/MoscowOVH)")]
                    public MarketPlacePrize rewardMarketPlaceConfiguration = new();

                    public void RewardPlayer(string userID)
                    {
                        if (rewardCommandsConfiguration is { commandPrizeUse: true })
                            foreach (string command in rewardCommandsConfiguration.commandPrizeList)
                                Instance.Server.Command(command.Replace("%STEAMID%", userID));
                        

                        if (rewardEconomicsConfiguration is { economicsPrizeUse: true })
                        {
                            if (Instance?.Economics)
                            {
                                Instance.Economics.Call("Deposit", ulong.Parse(userID), (double)rewardEconomicsConfiguration.balanceEconomicsPlus);
                            }
                            else if (Instance?.IQEconomic)
                            {
                                Instance.IQEconomic.Call("API_SET_BALANCE", ulong.Parse(userID), rewardEconomicsConfiguration.balanceEconomicsPlus);
                            }
                            else if (Instance?.ServerRewards)
                            {
                                Instance.ServerRewards.Call("AddPoints", ulong.Parse(userID), rewardEconomicsConfiguration.balanceEconomicsPlus);
                            }
                        }

                        if (rewardMarketPlaceConfiguration is { marketplacePrizeUse : true })
                        {
                            switch (rewardMarketPlaceConfiguration.StoreType)
                            {
                                case StoreType.GameStore:
                                    string message = string.IsNullOrWhiteSpace(rewardMarketPlaceConfiguration.balancePlusMess)
                                        ? "API XDStatistics"
                                        : rewardMarketPlaceConfiguration.balancePlusMess;
                                    string uri = $"https://gamestores.app/api?shop_id={Instance._config.settingsPrize.ShopID}&secret={Instance._config.settingsPrize.SecretKey}&server={Instance._config.settingsPrize.ServerID}&action=moneys&type=plus&steam_id={userID}&amount={rewardMarketPlaceConfiguration.balancePlus}&mess={message}";
                                    Instance.webrequest.Enqueue(uri, "", (code, response) => 
                                    {
                                        switch (code)
                                        {
                                            case 0:
                                            {
                                                Instance.PrintError("Api does not responded to a request");
                                                break;
                                            }
                                            case 200:
                                            {
                                                break;
                                            }
                                            case 404:
                                            {
                                                Instance.PrintError($"Please check your configuration! {code}");
                                                break;
                                            }
                                        }
                                    }, Instance);
                                    break;
                                case StoreType.MoscowOVH:
                                    if (Instance?.RustStore)
                                    {
                                        Instance.RustStore.CallHook("APIChangeUserBalance", ulong.Parse(userID), rewardMarketPlaceConfiguration.balancePlus, new Action<string>(result =>
                                        {
                                            if (result == "SUCCESS")
                                                return;
                                            Interface.Oxide.LogDebug($"Баланс игрока {ulong.Parse(userID)} не был изменен, ошибка: {result}");
                                        }));
                                    }
                                    break;
                                case StoreType.None:
                                    break;
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }
                        }
                    }
                }
            }

            [JsonProperty(RU ? "Основные настройки плагина" : "Basic Plugin Settings")]
            public Settings settings = new Settings();
            [JsonProperty(RU ? "Настройка начисления очков" : "Points Allocation Settings")]
            public SettingsScore settingsScore = new SettingsScore();
            [JsonProperty(RU ? "Настройки наград для лидеров по выбраным категории в конце вайпа" : "Reward settings for leaders in selected categories at the end of the wipe")] 
            public SettingsPrize settingsPrize = new SettingsPrize();
            [JsonProperty(RU ? "Настройки интерфейса" : "Interface Settings")]
            public SettingsInterface settingsInterface = new SettingsInterface();
            [JsonProperty(RU ? "Настройка отправки топ игроков в Discord" : "Setting up the sending of top players to Discord")]
            public DiscordMessage discordMessage = new DiscordMessage();
            [JsonProperty(RU ? "Настройка отправки топ игроков в игровой чат" : "Setting up the sending of top players to the game chat")]
            public ChatMessage chatMessage = new ChatMessage();
            [JsonProperty(RU ? "Настройка параметров статистики убийств" : "Configure kill statistics settings")]
            public SettingsIgnore settingsIgnore = new SettingsIgnore();
        }
        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null)
                    LoadDefaultConfig();
                ValidateConfig();
                SaveConfig();
            }
            catch (JsonException ex)
            {
                Debug.LogException(ex);
                LoadDefaultConfig();
            }
        }

        private void ValidateConfig()
        {
            if (_config.settingsScore.GatherScore.Count == 0)
            {
                _config.settingsScore.GatherScore = new Dictionary<string, float>
                {
                    ["wood"] = 0.3f,
                    ["stones"] = 0.6f,
                    ["metal.ore"] = 1,
                    ["sulfur.ore"] = 1.5f,
                    ["hq.metal.ore"] = 2,
                };
            }
            if (_config.settingsScore.ExplosionScore.Count == 0)
            {
                _config.settingsScore.ExplosionScore = new Dictionary<string, float>
                {
                    ["explosive.timed"] = 2,
                    ["explosive.satchel"] = 0.7f,
                    ["grenade.beancan"] = 0.3f,
                    ["grenade.f1"] = 0.1f,
                    ["ammo.rocket.basic"] = 1,
                    ["ammo.rocket.hv"] = 0.5f,
                    ["ammo.rocket.fire"] = 0.7f,
                    ["ammo.rifle.explosive"] = 0.02f,
                };
            }

            if (_config.settingsPrize.prizeUse && _config.settingsPrize.PrizeByCategory.Count > 0)
            {
                foreach (KeyValuePair<string, List<Configuration.SettingsPrize.Reward>> pbc in _config.settingsPrize.PrizeByCategory)
                    if (Enum.TryParse(pbc.Key, out CatType categoryType))
                        _config.settingsPrize.rewardsCategoryList.Add(categoryType);
                    else
                        PrintError($"[Configuration Reward ERROR] Invalid category type: {pbc.Key}. Removing from list.\nAvailable types: {string.Join(", ", Enum.GetNames(typeof(CatType)))}");
            }
            
            ProcessCategories(_config.chatMessage.chatSendTop, _config.chatMessage.chatCategories, _config.chatMessage._chatCategories);
            ProcessCategories(_config.discordMessage.discordTopFiveUse, _config.discordMessage.discordCategories, _config.discordMessage._discordCategories);

            return;

            void ProcessCategories(bool condition, List<string> categories, ICollection<CatType> targetList)
            {
                if (!condition || categories.Count == 0) return;

                foreach (string category in categories)
                {
                    if (Enum.TryParse(category, out CatType categoryType))
                    {
                        targetList.Add(categoryType);
                    }
                    else
                    {
                        PrintError($"[Configuration] Invalid category type: {category}. Removing from list.\nAvailable types: {string.Join(", ", Enum.GetNames(typeof(CatType)))}");
                    }
                }
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

        #region Data
        private static List<ulong> ignoreReservedPlayer = new();
        private static Dictionary<ulong, List<PrizePlayer>> _prizePlayerData = new();
        
        private class PrizePlayer
        {
            public CatType catType;
            public int value;
            public int top;
            public bool isRewarded;
        }
        

        private void LoadDataIgnoreList() => ignoreReservedPlayer = Interface.Oxide.DataFileSystem.ReadObject<List<ulong>>("XDStatistics/IgnoredPlayers");
        private void SaveDataIgnoreList() => Interface.Oxide.DataFileSystem.WriteObject("XDStatistics/IgnoredPlayers", ignoreReservedPlayer);
        private void LoadDataPrize() => _prizePlayerData = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, List<PrizePlayer>>>("XDStatistics/PlayersRewarded");
        private void SaveDataPrize() => Interface.Oxide.DataFileSystem.WriteObject("XDStatistics/PlayersRewarded", _prizePlayerData);
        
        #endregion

        #region SplitDatafile

        private abstract class SplitDatafile<T> where T : SplitDatafile<T>, new()
        {
            public static Dictionary<ulong, T> _players = new();
            
            protected static string[] GetFiles(string baseFolder)
            {
                try
                {
                    int json = ".json".Length;
                    string[] paths = Interface.Oxide.DataFileSystem.GetFiles(baseFolder, "*.json");
                    for (int i = 0; i < paths.Length; i++)
                    {
                        string path = paths[i];
                        int separatorIndex = path.LastIndexOf("/", StringComparison.Ordinal);
                        paths[i] = path.Substring(separatorIndex + 1, path.Length - separatorIndex - 1 - json);
                    }
            
                    return paths;
                }
                catch
                {
                    return Array.Empty<string>();
                }
            }
            protected static void Delete(string baseFolder, ulong userId)
            {
                if (!_players.Remove(userId, out T _))
                    return; 
                try
                {
                    Interface.Oxide.DataFileSystem.DeleteDataFile(baseFolder + userId);
                }
                catch (Exception e)
                {
                    Interface.Oxide.LogError(e.ToString());
                }
            }
            protected static T Save(string baseFolder, ulong userId)
            {
                T data;
                if (!_players.TryGetValue(userId, out data))
                    return null;

                Interface.Oxide.DataFileSystem.WriteObject(baseFolder + userId, data);
                return data;
            }

            protected static T Get(string baseFolder, ulong userId)
            {
                T data;
                if (_players.TryGetValue(userId, out data))
                    return data;
                
                return null;
            }

            protected static T Load(string baseFolder, ulong userId)
            {
                T data = null;
                
                try
                {
                    data = Interface.Oxide.DataFileSystem.ReadObject<T>(baseFolder + userId);
                }
                catch (Exception e)
                {
                    Interface.Oxide.LogError(e.ToString());
                }
                
                return _players[userId] = data;
            }

            protected static T GetOrLoad(string baseFolder, ulong userId)
            {
                T data;
                if (_players.TryGetValue(userId, out data))
                    return data;


                return Load(baseFolder, userId);
            }

            protected static T GetOrCreate(string baseFolder, ulong userId)
            {
                return GetOrLoad(baseFolder, userId) ?? (_players[userId] = new T());
            }
            
            protected static T ClearAndSave(string baseFolder, ulong userId)
            {
                if (_players.TryGetValue(userId, out T data))
                {
                    data = new T();
                    _players[userId] = data;

                    Interface.Oxide.DataFileSystem.WriteObject(baseFolder + userId, data);
                    return data;
                }
    
                return null;
            }
        }

        private class PlayerInfo : SplitDatafile<PlayerInfo>
        {
            private const string BaseFolder = "XDStatistics" + "/" + "Players" + "/";
            public static PlayerInfo Save(ulong userId) => Save(BaseFolder, userId);
            public static void Delete (ulong userId) => Delete(BaseFolder, userId);
            public static PlayerInfo Get(ulong userId) => Get(BaseFolder, userId);
            public static PlayerInfo Load(ulong userId) => Load(BaseFolder, userId);
            public static PlayerInfo Clear(ulong userId) => ClearAndSave(BaseFolder, userId);
            public static PlayerInfo GetOrLoad(ulong userId) => GetOrLoad(BaseFolder, userId);
            public static PlayerInfo GetOrCreate(ulong userId) => GetOrCreate(BaseFolder, userId);
            public static string[] GetFiles() => GetFiles(BaseFolder);
            
            #region NewData

            #region MiscData

            [JsonIgnore]
            public bool IsFake;
            [JsonIgnore]
            public bool IsIgnore;
            [JsonIgnore]
            public bool IsChanged = false;
            
            public string Name = string.Empty;
            public bool HidedStatistics;
            
            internal string GetPlayerName(ulong steamId) => string.IsNullOrWhiteSpace(Name) ? 
                Instance.covalence.Players.FindPlayerById(steamId.ToString())?.Name ?? "UNKNOWN" : Name;

            #endregion

            #region Score & Methods
            
            public float Score;
            internal string ScoreFormatted() => Score.ToString("F2");

            private void AddScore(float score) => Score += score;
            private void RemoveScore(float score) => Score -= score;

            #endregion
            
            #region CropGrowthData & Methods

            public Dictionary<string, int> CropGrowthStats { get; } = new();
            public int TotalCropsGrown { get; set; }

            public void AddOrUpdateCrop(string shortName, int count, float score = 0)
            {
                if (!CropGrowthStats.TryAdd(shortName, count))
                {
                    CropGrowthStats[shortName] += count;
                }

                TotalCropsGrown += count;
                AddScore(score);
            }

            #endregion

            #region ExplosiveUsage & Methods
            
            public Dictionary<string, int> ExplosiveUsageStats { get; } = new() 
            {
                ["explosive.timed"] = 0,
                ["explosive.satchel"] = 0,
                ["grenade.beancan"] = 0,
                ["grenade.f1"] = 0,
                ["ammo.rocket.basic"] = 0,
                ["ammo.rocket.hv"] = 0,
                ["ammo.rocket.fire"] = 0,
                ["ammo.rifle.explosive"] = 0
            };
            public int TotalExplosivesUsed { get; set; }

            public void AddOrUpdateExplosive(BaseEntity entity, string shortname = "")
            {
                string weaponName = string.IsNullOrWhiteSpace(shortname) ? string.Empty : shortname;

                if (entity != null)
                {
                    if (!Instance._prefabID2Item.TryGetValue(entity.prefabID, out weaponName))
                    {
                        Instance._prefabNameItem.TryGetValue(entity.ShortPrefabName, out weaponName);
                    }
                } 

                if (!string.IsNullOrEmpty(weaponName))
                {
                    if (ExplosiveUsageStats.ContainsKey(weaponName) && !Instance._config.settingsScore.blackListed.Contains(weaponName))
                    {
                        ExplosiveUsageStats[weaponName]++;
                        TotalExplosivesUsed++;
                        
                        if (Instance._config.settingsScore.ExplosionScore.TryGetValue(weaponName, out float scoreValue))
                            AddScore(scoreValue);
                    }
                }
            }
            
            #endregion

            #region ResourceGathering & Methods

            private static readonly Dictionary<string, string> ResourceMappings = new()
            {
                { "stones", "stones" },
                { "wood", "wood" },
                { "metal.ore", "metal.ore" },
                { "metal.fragments", "metal.ore" },
                { "sulfur.ore", "sulfur.ore" },
                { "sulfur", "sulfur.ore" },
                { "hq.metal.ore", "hq.metal.ore" },
                { "metal.refined", "hq.metal.ore" },
                { "scrap", "scrap" }
            };
            public Dictionary<string, int> ResourceGatheringStats { get; } = new() 
            {
                ["wood"] = 0,
                ["stones"] = 0,
                ["metal.ore"] = 0,
                ["sulfur.ore"] = 0,
                ["hq.metal.ore"] = 0,
                ["scrap"] = 0,
            };
            public int TotalResourcesGathered { get; set; }

            public void AddOrUpdateResource(string shortName, int count, bool isGiveScore = false)
            {
                if (Instance._config.settingsScore.blackListed.Contains(shortName))
                    return;
                if (ResourceMappings.TryGetValue(shortName, out string mappedName))
                {
                    ResourceGatheringStats.TryGetValue(mappedName, out int currentCount);
                    ResourceGatheringStats[mappedName] = currentCount + count;
                    TotalResourcesGathered += count;

                    if (isGiveScore)
                        if (mappedName == "scrap")
                            AddScore(Instance._config.settingsScore.ScrapScore);
                        else
                            if (Instance._config.settingsScore.GatherScore.TryGetValue(mappedName, out float scoreValue))
                                AddScore(scoreValue);
                }
            }

            #endregion

            #region WeaponUsed & Methods

            public Dictionary<string, WeaponInfo> WeaponUsed { get; } = new();
            
            public class WeaponInfo
            {
                public int Kills { get; set; }
                public int Headshots { get; set; }
                public int Shots { get; set; }
            }
            
            public void AddOrUpdateWeapon(HitInfo hitinfo, bool kill = false)
            {
                if (hitinfo?.WeaponPrefab == null) return;

                string weaponName = null;

                if (hitinfo.Weapon != null && hitinfo.Weapon.GetCachedItem() != null && hitinfo.Weapon.GetCachedItem().info != null)
                {
                    weaponName = hitinfo.Weapon.GetCachedItem().info.shortname;
                }
                else
                {
                    if (!Instance._prefabID2Item.TryGetValue(hitinfo.WeaponPrefab.prefabID, out weaponName))
                    {
                        Instance._prefabNameItem.TryGetValue(hitinfo.WeaponPrefab.ShortPrefabName, out weaponName);
                    }
                }

                if (string.IsNullOrEmpty(weaponName)) return;

                weaponName = weaponName switch 
                {
                    "rifle.ak.ice" or "rifle.ak.diver" => "rifle.ak",
                    _ => weaponName
                };
                
                if (!WeaponUsed.TryGetValue(weaponName, out WeaponInfo weaponInfo))
                {
                    weaponInfo = new WeaponInfo();
                    WeaponUsed[weaponName] = weaponInfo;
                }
                
                weaponInfo.Shots++;
                if (hitinfo.isHeadshot) weaponInfo.Headshots++;
                if (kill) weaponInfo.Kills++;
            }

            #endregion

            #region PvpStats & Methods

            public PvpStats PlayerPvpStats { get; private set; } = new();

            internal class PvpStatsBuilder // Fluent Interface & Builder Pattern
            {
                private readonly PvpStats _stats;

                public PvpStatsBuilder(PvpStats stats)
                {
                    _stats = stats ?? new PvpStats();
                }

                public PvpStatsBuilder WithKills(int kill = 1)
                {
                    _stats.Kills += kill;
                    return this;
                }

                public PvpStatsBuilder WithKillsNpc()
                {
                    _stats.KillsNpc++;
                    return this;
                }
                
                public PvpStatsBuilder WithKillsAnimal()
                {
                    _stats.KillsAnimal++;
                    return this;
                }

                public PvpStatsBuilder WithDeaths()
                {
                    _stats.Deaths++;
                    return this;
                }
                
                public PvpStatsBuilder WithSuicides()
                {
                    _stats.Suicides++;
                    return this;
                }
                
                public PvpStatsBuilder WithShots()
                {
                    _stats.Shots++;
                    return this;
                }
                
                public PvpStatsBuilder WithHeadshots(int head = 0)
                {
                    _stats.Headshots += head;
                    return this;
                }
                
                public PvpStatsBuilder WithHeliKill()
                {
                    _stats.HeliKill++;
                    return this;
                }
                
                public PvpStatsBuilder WithBradleyKill()
                {
                    _stats.BradleyKill++;
                    return this;
                }

                public PvpStats Build()
                {
                    return _stats;
                }
            }
            
            public class PvpStats
            {
                public int Kills { get; set; }
                public int KillsNpc { get; set; }
                public int KillsAnimal { get; set; }
                public int Deaths { get; set; }
                public int Suicides { get; set; }
                public int Shots { get; set; }
                public int Headshots { get; set; }
                public int HeliKill { get; set; }
                public int BradleyKill { get; set; }
                
                public string CalculateKDR(int kills)
                {
                    if (Deaths == 0)
                        return kills == 0 ? "0" : "Infinity";

                    float kdr = (float)kills / Deaths;
                    return kdr.ToString("F2");
                }
            }
            
            public void UpdatePvpStats(Action<PvpStatsBuilder> updateAction, float score = 0, bool scoreAdd = true)
            {
                PvpStatsBuilder builder = new(PlayerPvpStats);
                updateAction(builder);
                PlayerPvpStats = builder.Build();

                if (scoreAdd)
                    AddScore(score);
                else
                    RemoveScore(score);
            }


            #endregion

            #region MiscStats & Methods

            public PlayerMiscStats MiscStats { get; } = new ();
            public class PlayerMiscStats
            {
                public int CrateOpen { get; set; }
                public int BarrelsDestroyed { get; set; }
                public int BuildingsPlaced { get; set; }
                public int TotalItemsCrafted { get; set; }
            }
            
            public void IncrementCrateOpen(int amount = 1) => MiscStats.CrateOpen += amount;
            public void IncrementBuildingsPlaced(int amount = 1, float score = 0)
            {
                MiscStats.BuildingsPlaced += amount;
                AddScore(score);
            }
            public void IncrementTotalItemsCrafted(int amount = 1, float score = 0)
            {
                MiscStats.TotalItemsCrafted += amount;
                AddScore(score);
            } 
            public void IncrementBarrelsDestroyed(int amount = 1, float score = 0)
            {
                MiscStats.BarrelsDestroyed += amount;
                AddScore(score);
            } 

            #endregion

            #region PlayedTime & Methods

            public PlayedTime playedTime { get; } = new ();

            public class PlayedTime
            {
                public string DayNumber = DateTime.Now.ToShortDateString();
                public int PlayedForWipe;
                public int PlayedToday;
            }
            
            public void AddPlayedTime()
            {
                playedTime.PlayedToday++;
                playedTime.PlayedForWipe++;
                AddScore(Instance._config.settingsScore.TimeScore);
                
                string currentDate = DateTime.Now.ToShortDateString();
                if (playedTime.DayNumber != currentDate)
                {
                    playedTime.PlayedToday = 0;
                    playedTime.DayNumber = currentDate;
                }
            }

            #endregion

            #endregion

            public static void PlayerClearData(ulong id)
            {
                BasePlayer player = BasePlayer.FindByID(id);
                PlayerInfo playerData =  Clear(id);
                playerData.Name = player.displayName ?? "UNKNOWN";
            }
        }

        #region DataFileMethods

        private void DeleteDataFiles()
        {
            List<ulong> keysToDelete = PlayerInfo._players.Select(player => player.Key).ToList();

            foreach (ulong key in keysToDelete)
                PlayerInfo.Delete(key);
            
            PlayerInfo._players.Clear();
        }
        private void SaveDataFiles()
        {
            foreach (KeyValuePair<ulong, PlayerInfo> player in PlayerInfo._players)
                if (player.Value.IsChanged)
                    PlayerInfo.Save(player.Key);
        }

        private void LoadDataFiles()
        {
            string[] players = PlayerInfo.GetFiles();
            List<ulong> playersToDelete = new();
            string today = DateTime.Now.ToShortDateString();
            foreach (string player in players)
            {
                if (ulong.TryParse(player, out ulong userId))
                {
                    PlayerInfo info = PlayerInfo.Load(userId);
            
                    if(info.playedTime.DayNumber != today && info.playedTime.PlayedForWipe <= 15)
                    {
                        playersToDelete.Add(userId);
                    }
                }
            }

            #region SaveMemorry

            bool isStarted = BasePlayer.activePlayerList.Count <= 0;

            if (isStarted)
            {
                foreach (ulong userId in playersToDelete)
                {
                    PlayerInfo.Delete(userId);
                }
            }
            else
            {
                foreach (ulong userId in playersToDelete)
                {
                    if (BasePlayer.FindByID(userId) == null)
                    {
                        PlayerInfo.Delete(userId);
                    }
                }
            }

            #endregion

            PrintWarning("STAT_PRINT_LOAD".GetAdaptedMessage(null, players.Length, playersToDelete.Count));
        }

        #endregion

        #endregion

        #region IQFake
        private readonly DateTime RealTime = DateTime.UtcNow.Date;
        private int WipeTime;

        private bool IsReadyIQFakeActive()
        {
            if (IQFakeActive != null && _config.settings.IQFakeActiveUse)
                return IQFakeActive.Call<bool>("IsReady");

            return false;
        }

        private class FakePlayer
        {
            [JsonProperty("userId")]
            public string userId;
            [JsonProperty("displayName")]
            public string displayName;
            public bool isMuted;
        }
        
        private void UpdateFakePlayers()
        {
            WipeTime = RealTime.Subtract(SaveRestore.SaveCreatedTime.Date).Days;
            
            foreach (KeyValuePair<ulong, PlayerInfo> player in PlayerInfo._players.Where(x => x.Value.IsFake))
                    NextTick(() => {PlayerInfo._players.Remove(player.Key);});
            
            List<FakePlayer> _fakePlayers = GetFakePlayerList();

            if (_fakePlayers != null)
            {
                foreach (FakePlayer fakePlayer in _fakePlayers)
                {
                    ulong userid = ulong.Parse(fakePlayer.userId);

                    PlayerInfo info = new()
                    {
                        IsFake = true,
                        Name = GetCorrectName(fakePlayer.displayName),
                        HidedStatistics = true,
                        Score = Core.Random.Range(-2.0f, 15.3f) * WipeTime,
                        playedTime = { PlayedForWipe = random(130, 478)},
                        TotalResourcesGathered = random(999, 15734),
                        TotalExplosivesUsed = WipeTime > 0 ? random(4, 11) : 0,
                        TotalCropsGrown = random(23, 34),
                        PlayerPvpStats = { Kills = random(0, 16), KillsNpc = random(0, 6)}
                    };

                    PlayerInfo._players.TryAdd(userid, info);
                }   
            }
            else
            {
                timer.Once(60, UpdateFakePlayers);
            }

            return;

            int random(int min, int max)
            {
                int rnd = Core.Random.Range(min, max) * (WipeTime + 1);
                return rnd;
            }
        }

        private List<FakePlayer> GetFakePlayerList()
        {
            if (!IsReadyIQFakeActive()) return null;
            JObject jsonData = IQFakeActive.Call<JObject>("GetOnlyListFakePlayers");
 
            if (!jsonData.TryGetValue("players", out JToken playersToken)) return null;
            List<FakePlayer> playerList = playersToken.ToObject<List<FakePlayer>>();
            return playerList;
        }

        #endregion

        #region TopPlayersCashMethods

        private void UpdateTopCash() => cashedTopUser = GetTopPlayersByAllCategories();

        private Dictionary<CatType, Dictionary<ulong, CashedData>> GetTopPlayersByAllCategories(List<CatType> catTypes = null)
        {
            Dictionary<CatType, Dictionary<ulong, CashedData>> result = new();
            CatType[] values = catTypes == null ? (CatType[]) Enum.GetValues(typeof(CatType)) : catTypes.ToArray();
            if (PlayerInfo._players == null || PlayerInfo._players.Count == 0)
                return result;
            foreach (CatType catType in values)
            {
                Dictionary<ulong, CashedData> playerRatings = new();

                foreach (KeyValuePair<ulong, PlayerInfo> player in PlayerInfo._players)
                {
                    if(player.Value.IsIgnore || ignoreReservedPlayer.Contains(player.Key) || _config.settings.ignoreList.Contains(player.Key))
                        continue;

                    CashedData data = catType switch
                    {
                        CatType.Score => new CashedData(0, player.Value.Name, player.Value.Score),
                        CatType.Killer => new CashedData(player.Value.PlayerPvpStats.Kills, player.Value.Name),
                        CatType.PlayedTime => new CashedData(player.Value.playedTime.PlayedForWipe, player.Value.Name),
                        CatType.Gather => new CashedData(player.Value.TotalResourcesGathered, player.Value.Name),
                        CatType.Explosive => new CashedData(player.Value.TotalExplosivesUsed, player.Value.Name),
                        CatType.KillerNpc => new CashedData(player.Value.PlayerPvpStats.KillsNpc, player.Value.Name),
                        CatType.KillerAnimal => new CashedData(player.Value.PlayerPvpStats.KillsAnimal, player.Value.Name),
                        CatType.Builder => new CashedData(player.Value.MiscStats.BuildingsPlaced, player.Value.Name),
                        CatType.Fermer => new CashedData(player.Value.TotalCropsGrown, player.Value.Name),
                        _ => throw new ArgumentOutOfRangeException()
                    };

                    playerRatings.Add(player.Key, data);
                }

                Dictionary<ulong, CashedData> sortedPlayers = playerRatings.OrderByDescending(p => p.Value.FullValue()).Take(15).
                    ToDictionary(p => p.Key, p => p.Value);
                result.Add(catType, sortedPlayers);
            }

            return result;
        }

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

        #region StatHooks

        #region Gather
        
        private void OnDispenserBonusReceived(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            if (player == null || item == null)
                return;
            PlayerInfo playerstat = PlayerInfo.GetOrCreate(player.userID);
            playerstat.AddOrUpdateResource(item.info.shortname, item.amount, true);
        }
        private void OnDispenserGathered(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            if (player == null || item == null)
                return;
            
            PlayerInfo playerstat = PlayerInfo.GetOrCreate(player.userID);
            playerstat.AddOrUpdateResource(item.info.shortname, item.amount);
        }
        
        private void OnCollectiblePickedup(CollectibleEntity collectible, BasePlayer player, Item item)
        {
            if (player == null || collectible == null || item == null)
                return;
            
            PlayerInfo playerstat = PlayerInfo.GetOrCreate(player.userID);
            

            if (item.info.category == ItemCategory.Food || _alowedSeedId.Contains(item.info.itemid))
            {
                if(item.info.shortname.Contains("seed")) return;
                playerstat.AddOrUpdateCrop(item.info.shortname, item.amount, _config.settingsScore.PlantScore);
            }
            else
            {
                playerstat.AddOrUpdateResource(item.info.shortname, item.amount, true);
            }
        }
        
        private void OnGrowableGathered(GrowableEntity plant, Item item, BasePlayer player)
        {
            if (player == null || item == null)
                return;
            
            if (item.info.category == ItemCategory.Food || _alowedSeedId.Contains(item.info.itemid))
            {
                if(item.info.shortname.Contains("seed")) return;

                PlayerInfo playerStat = PlayerInfo.GetOrCreate(player.userID);
                playerStat.AddOrUpdateCrop(item.info.shortname, item.amount, _config.settingsScore.PlantScore);
            }
           
        }

        #endregion
        private void OnItemCraftFinished(ItemCraftTask task, Item item, ItemCrafter crafter)
        {
            if(_config.settingsScore.blackListedCraft.Contains(item.info.shortname))
                return;

            BasePlayer player = crafter == null ? null : crafter.owner;
            if (player != null && player.IsPlayer())
            {
                PlayerInfo playerInfo = PlayerInfo.GetOrCreate(crafter.owner.userID);
                playerInfo.IncrementTotalItemsCrafted(item.amount,  _config.settingsScore.craftScore);
            }
        }
        
        private void OnEntityBuilt(Planner plan, GameObject go)
        {
            BasePlayer player = plan != null ? plan.GetOwnerPlayer() : null;
            if (player == null)
                return;

            BaseEntity entity = go != null ? go.ToBaseEntity() : null;
            if (entity == null)
                return;
            
            BuildingBlock bBlock = entity as BuildingBlock;
            
            NextTick(() =>
            {
                if (bBlock == null || bBlock.IsDestroyed) return;
                PlayerInfo playerstat = PlayerInfo.GetOrCreate(player.userID);
                playerstat.IncrementBuildingsPlaced(score: _config.settingsScore.BuildingScore);
            });
        }
         
        private void OnTimedExplosiveExplode(TimedExplosive explosive, Vector3 explosionFxPos)
        {
            if (explosive == null) return;

            if (explosive.creatorEntity is BasePlayer player && player.IsPlayer())
            {
                BaseEntity entity = explosive.LookupPrefab();
                if (entity == null) return;

                PlayerInfo playerstat = PlayerInfo.GetOrCreate(player.userID);
                playerstat.AddOrUpdateExplosive(entity);
            }
        }
        
        private void OnWeaponFired(BaseProjectile projectile, BasePlayer player)
        {
            if (projectile == null || player == null || !player.IsPlayer()) return;

            if (projectile.primaryMagazine == null || projectile.primaryMagazine.ammoType == null) return;

            PlayerInfo playerstat = PlayerInfo.GetOrCreate(player.userID);
            playerstat.AddOrUpdateExplosive(null, projectile.primaryMagazine.ammoType.shortname);
        }
        private void OnContainerDropItems(ItemContainer container)
        {
            if (container == null || !container.entityOwner || !container.entityOwner.ShortPrefabName.Contains("barrel"))
                return;

            if (container.entityOwner is LootContainer lootContainer)
            {
                BasePlayer player = lootContainer.lastAttacker as BasePlayer;

                if (player && player.IsPlayer())
                {
                    PlayerInfo playerstat = PlayerInfo.GetOrCreate(player.userID);
                    
                    foreach (Item item in container.itemList)
                        if (item.info.shortname == "scrap" && item.skin == 0)
                            playerstat.AddOrUpdateResource(item.info.shortname, item.amount, true);
                }
            }
        }
        
        private void OnLootEntity(BasePlayer player, LootContainer entity)
        {
            if (!entity || !player || !player.IsPlayer() || entity.LastLootedBy != 0UL) 
                return;
            
            PlayerInfo playerstat = PlayerInfo.GetOrCreate(player.userID);
            playerstat.IncrementCrateOpen();

            foreach (Item item in entity.inventory.itemList)
                if (item.info.shortname == "scrap" && item.skin == 0)
                    playerstat.AddOrUpdateResource(item.info.shortname, item.amount, true);
        }

        #region Death
        private void OnEntityDeath(LootContainer entity, HitInfo info)
        {
            if (entity == null || info == null)
                return;
            
            BasePlayer player = info.InitiatorPlayer;
            if (player == null || !player.IsPlayer())
                return;
            
            if (entity.ShortPrefabName.Contains("barrel"))
            {
                PlayerInfo playerstat = PlayerInfo.GetOrCreate(player.userID);
                playerstat.IncrementBarrelsDestroyed(score: _config.settingsScore.barrelScore);
            }
        }

        private Dictionary<NetworkableId, ulong> heliCashed = new();
        private void OnPatrolHelicopterKill(PatrolHelicopter entity, HitInfo info)
        {
            if (entity == null || info == null || info.InitiatorPlayer == null)
                return;

            BasePlayer player = info.InitiatorPlayer;
            if (player.IsPlayer())
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
                UpdatePlayerStats(playerId);
                heliCashed.Remove(entity.net.ID);
            }
            else
            {
                if (entity.myAI != null && entity.myAI._targetList is { Count: > 0 } targetList)
                {
                    BasePlayer player = targetList[^1].ply;

                    if (player != null && player.IsPlayer())
                    {
                        UpdatePlayerStats(player.userID);
                    }
                }
            }
        }

        private void UpdatePlayerStats(ulong playerId)
        {
            PlayerInfo playerstat = PlayerInfo.GetOrCreate(playerId);
            playerstat.UpdatePvpStats(builder => builder.WithHeliKill(), Instance._config.settingsScore.HeliScore);
        }
        private void OnEntityDeath(BradleyAPC entity, HitInfo info)
        {
            if (entity == null || info == null) return;
            
            BasePlayer player = info.InitiatorPlayer;

            if (player != null && player.IsPlayer())
            {
                PlayerInfo playerstat = PlayerInfo.GetOrCreate(player.userID);
                playerstat.UpdatePvpStats(builder => builder.WithBradleyKill(), score: Instance._config.settingsScore.BradleyScore);
            }
        }
        
        private void OnEntityDeath(BaseAnimalNPC entity, HitInfo info)
        {
            if (entity == null || info == null) return;
            
            BasePlayer player = info.InitiatorPlayer;

            if (player != null && player.IsPlayer())
            {
                PlayerInfo playerstat = PlayerInfo.GetOrCreate(player.userID);
                playerstat.UpdatePvpStats(builder => builder.WithKillsAnimal(), score: Instance._config.settingsScore.AnimalScore);
            }
        }
        
        private void OnEntityDeath(BaseNPC2 entity, HitInfo info)
        {
            if (entity == null || info == null) return;
            
            BasePlayer player = info.InitiatorPlayer;

            if (player != null && player.IsPlayer())
            {
                PlayerInfo playerstat = PlayerInfo.GetOrCreate(player.userID);
                playerstat.UpdatePvpStats(builder => builder.WithKillsAnimal(), score: Instance._config.settingsScore.AnimalScore);
            }
        }

        private Dictionary<BasePlayer, HitInfo> playersWounded = new();

        private void OnPlayerWound(BasePlayer player, HitInfo info)
        {
            if(player == null || !player.IsPlayer() || info == null) return;

            if (info.InitiatorPlayer != null || info.Initiator != null)
                playersWounded.TryAdd(player, info);
        }
        
        private void OnPlayerRecovered(BasePlayer player)
        {
            if(player == null || !player.IsPlayer()) return;
            playersWounded.Remove(player);
        }

        private bool IsWeaponFree(BasePlayer player)
        {
            List<Item> items = Pool.Get<List<Item>>();
            try
            {
                player.inventory.GetAllItems(items);
                foreach (Item item in items)
                {
                    if (item.info.category == ItemCategory.Weapon && !_config.settingsIgnore.itemsToIgnoreList
                            .Any(itemToIgnore => string.Equals(itemToIgnore, item.info.shortname, StringComparison.OrdinalIgnoreCase)))
                    {
                        return false;
                    }
                }
                return true;
            }
            finally
            {
                Pool.Free(ref items);
            }
        }
        
        private void OnPlayerDeath(BasePlayer player, HitInfo hitInfo)
        {
            if(player == null) return;
            
            if (player.IsPlayer())
            {
                if(!_config.settingsIgnore.countSleepingPlayerKills && !player.IsConnected) return;
                if(!_config.settingsIgnore.countKillsWithoutWeapons && IsWeaponFree(player)) return;

                PlayerInfo playerstat = PlayerInfo.GetOrCreate(player.userID);

                if (hitInfo?.InitiatorPlayer != null)
                {
                    BasePlayer attacker = hitInfo.InitiatorPlayer;
                    
                    if (attacker.userID == player.userID || hitInfo.damageTypes.GetMajorityDamageType() is DamageType.Suicide or DamageType.Fall)
                    {
                        playerstat.UpdatePvpStats(builder => builder.WithSuicides().WithDeaths(), score: Instance._config.settingsScore.SuicideScore, scoreAdd: false);
                        return;
                    }
                    
                    if (attacker.IsPlayer())
                    {
                        PlayerInfo playerAttacker = PlayerInfo.GetOrCreate(attacker.userID);
                    
                        if (IsFriends(attacker.userID.Get(), player.userID.Get()) ||
                            IsClans(attacker.UserIDString, player.UserIDString) ||
                            IsDuel(attacker.userID.Get())) return;
                        
                        playerstat.UpdatePvpStats(builder => builder.WithDeaths(), score: Instance._config.settingsScore.DeathScore, scoreAdd: false);
                        
                        playerAttacker.AddOrUpdateWeapon(hitInfo, true);
                        playerAttacker.UpdatePvpStats(builder => builder.WithKills(), score: Instance._config.settingsScore.PlayerScore);
                    }
                    else
                    {
                        playerstat.UpdatePvpStats(builder => builder.WithDeaths(), score: Instance._config.settingsScore.DeathScore, scoreAdd: false);
                    }
                }
                else if(hitInfo?.Initiator != null)
                {
                    playerstat.UpdatePvpStats(builder => builder.WithDeaths(), score: Instance._config.settingsScore.DeathScore, scoreAdd: false);
                }
                else
                {
                    
                    if (playersWounded.Remove(player, out HitInfo info))
                    {
                        OnPlayerDeath(player, info);
                        return;
                    }
                    
                    if (hitInfo != null)
                    {
                        switch (hitInfo.damageTypes.GetMajorityDamageType())
                        {
                            case DamageType.Cold:
                            case DamageType.Drowned:
                            case DamageType.ElectricShock:
                            case DamageType.Radiation:
                                playerstat.UpdatePvpStats(builder => builder.WithDeaths(), score: Instance._config.settingsScore.DeathScore, scoreAdd: false);
                                break;


                            case DamageType.Suicide:
                            case DamageType.Fall:
                                playerstat.UpdatePvpStats(builder => builder.WithSuicides().WithDeaths(), score: Instance._config.settingsScore.SuicideScore, scoreAdd: false);
                                break;
                        }
                        return;
                    }
                    
                    playerstat.UpdatePvpStats(builder => builder.WithSuicides().WithDeaths(), score: Instance._config.settingsScore.SuicideScore, scoreAdd: false);
                }
            }
            else
            {
                if (hitInfo != null && hitInfo.InitiatorPlayer != null)
                {
                    BasePlayer attacker = hitInfo.InitiatorPlayer;
                    if(!attacker.IsPlayer()) return;
                    
                    PlayerInfo playerstat = PlayerInfo.GetOrCreate(attacker.userID);
                    playerstat.UpdatePvpStats(builder => builder.WithKillsNpc().WithKills(_config.settings.npsDeathUseKils ? 1 : 0), score: Instance._config.settingsScore.NpcScore);
                    
                    if (_config.settings.npsDeathUse)
                        playerstat.AddOrUpdateWeapon(hitInfo, true);
                } 
            }
        }

        #endregion

        private void OnPlayerAttack(BasePlayer attacker, HitInfo hitinfo)
        {
            if (hitinfo == null || attacker == null || !attacker.IsPlayer())
                return;
            
            if (hitinfo.HitEntity is BasePlayer player)
            {
                PlayerInfo playerStatInitiator = PlayerInfo.GetOrCreate(attacker.userID);

                if (player.IsPlayer())
                {
                    playerStatInitiator.AddOrUpdateWeapon(hitinfo);
                    playerStatInitiator.UpdatePvpStats(builder => builder.WithShots().WithHeadshots(hitinfo.isHeadshot ? 1 : 0));
                }
                else if (_config.settings.npsDeathUse)
                {
                    playerStatInitiator.AddOrUpdateWeapon(hitinfo);
                    playerStatInitiator.UpdatePvpStats(builder => builder.WithShots().WithHeadshots(hitinfo.isHeadshot ? 1 : 0));
                }
            }
        }

        #endregion

        #region BaseHooks
        private void Unload()
        {
            if (IsObjectNull(Instance))
                return;

            SaveDataFiles();
            SaveDataIgnoreList();
            SaveDataPrize();
            
            foreach (BasePlayer player in BasePlayer.activePlayerList)
                CuiHelper.DestroyUi(player, UI_INTERFACE);
            
            if (!IsObjectNull(_imageUI))
            {
                _imageUI.UnloadImages();
                _imageUI = null;
            }

            PlayerInfo._players = new();
            Instance = null;
        }

        private  void OnServerShutdown() => Unload();
        
        private void Init()
        {
            Instance = this;
            LoadDataIgnoreList();
            LoadDataFiles();
            LoadDataPrize();
        }
        private void OnNewSave()
        {
            if (_config.settingsPrize.prizeUse)
                ParseTopUserForPrize();
            else if (_config.settings.wipeData)
                ClearData();
        }

        private void ClearData()
        {
            DeleteDataFiles();
            
            ignoreReservedPlayer?.Clear();
            NextTick(SaveDataIgnoreList);
            
            PrintWarning("STAT_PRINT_WIPE".GetAdaptedMessage());
        }
        
        private void OnPlayerDisconnected(BasePlayer player)
        {
            PlayerInfo dataPlayer = PlayerInfo.Save(player.userID);
            dataPlayer.IsChanged = false;
            
            #region SaveMemorry

            if (dataPlayer.playedTime.PlayedToday <= 5 && dataPlayer.playedTime.PlayedForWipe <= 6)
                PlayerInfo.Delete(player.userID);

            #endregion
        }
       
        private void OnPlayerConnected(BasePlayer player)
        {
            ulong userID = player.userID;
            PlayerInfo dataPlayer = PlayerInfo.GetOrCreate(userID);
            dataPlayer.IsIgnore = ignoreReservedPlayer.Contains(userID);
            dataPlayer.Name = GetCorrectName(player.displayName);

            string today = DateTime.Now.ToShortDateString();
            if (dataPlayer.playedTime.DayNumber != today)
            {
                dataPlayer.playedTime.PlayedToday = 0;
                dataPlayer.playedTime.DayNumber = today;
            }

            dataPlayer.IsChanged = true;
            PlayerInfo.Save(userID);

            if (_prizePlayerData.TryGetValue(userID, out _))
                SendMsgRewardWipe(player);
        }
        private void OnServerInitialized()
        {
            #region LoadItemList
            foreach (ItemDefinition itemDef in ItemManager.GetItemDefinitions())
            {
                Item newItem = ItemManager.CreateByName(itemDef.shortname, 1, 0);

                BaseEntity heldEntity = newItem.GetHeldEntity();
                if (heldEntity != null)
                {
                    _prefabID2Item[heldEntity.prefabID] = itemDef.shortname;
                }

                if (itemDef.TryGetComponent(out ItemModDeployable itemModDeployable) && itemModDeployable.entityPrefab != null)
                {
                    string deployablePrefab = itemModDeployable.entityPrefab.resourcePath;

                    if (!string.IsNullOrEmpty(deployablePrefab))
                    {
                        GameObject prefab = GameManager.server.FindPrefab(deployablePrefab);
                        if (prefab != null && prefab.TryGetComponent(out BaseEntity baseEntity))
                        {
                            string shortPrefabName = baseEntity.ShortPrefabName;

                            if (!string.IsNullOrEmpty(shortPrefabName))
                            {
                                _prefabNameItem.TryAdd(shortPrefabName, itemDef.shortname);
                            }
                        }
                    }
                }

                newItem.Remove();
            }
            #endregion
            
            foreach (BasePlayer player in BasePlayer.activePlayerList)
                OnPlayerConnected(player);
                
            _imageUI = new ImageUI();
            _imageUI.DownloadImage();
            
            FetchAndParseItems();
            foreach (string cmds in _config.settings.chatCommandOpenStat)
                cmd.AddChatCommand(cmds, this, nameof(MainMenuStat));
            cmd.AddConsoleCommand(_config.settings.consoleCommandOpenStat, this, nameof(ConsoleCommandOpenMenu));

            #region PermReg
            RegisterPermissionIfNotExists(PermAdmin);
            RegisterPermissionIfNotExists(PermAvailability);
            RegisterPermissionIfNotExists(PermReset);
            #endregion
            
            if(_config.settings.IQFakeActiveUse)
                UpdateFakePlayers();
            
            NextTick(OnServerInitializedEx);
        }

        private void OnServerInitializedEx()
        {
            timer.Once(60f, CheckInMinute);

            if (_config.chatMessage.chatSendTop && _config.chatMessage._chatCategories.Count > 0)
                timer.Once(_config.chatMessage.chatSendTopTime, ChatPrintTopFive);
            if (_config.discordMessage.discordTopFiveUse && _config.discordMessage._discordCategories.Count > 0 && !string.IsNullOrEmpty(_config.discordMessage.weebHook))
                timer.Once(_config.discordMessage.discordSendTopTime, DiscordPrintTopFive);
            
            timer.Every(3 * 60, UpdateTopCash);
            timer.Every(_config.settings.dataSaveTime * 60, () => { SaveDataFiles(); SaveDataIgnoreList(); SaveDataPrize(); });

            UpdateTopCash();
        }
        
        #endregion
        
        #region UI
        public const string UI_INTERFACE = "INTERFACE_STATS";
        public const string UI_MENU_BUTTON = "MENU_BUTTON";
        public const string UI_USER_STAT = "USER_STAT";
        public const string UI_USER_STAT_INFO = "USER_STAT_INFO";
        public const string UI_CLOSE_MENU = "CLOSE_MENU";
        public const string UI_SEARCH_USER = "SEARCH_USER";
        public const string UI_TOP_TEN_USER = "TOP_TEN_USER";

        private void CloseLayer(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UI_USER_STAT);
            CuiHelper.DestroyUi(player, UI_SEARCH_USER);
            CuiHelper.DestroyUi(player, UI_TOP_TEN_USER);
        }

        #region MainPage
        private void MainMenuStat(BasePlayer player)
        {
            if (ignoreReservedPlayer.Contains(player.userID))
            {
                PrintToChat(player, "STAT_ADMIN_HIDE_STAT".GetAdaptedMessage(player.UserIDString));
                return;
            }
            
            string background = _imageUI.GetImage("1");
            var container = new CuiElementContainer();
            if (_config.settingsInterface.useCustomBackGround)
            {
                container.Add(new CuiPanel
                {
                    CursorEnabled = true,
                    Image = { Color = _config.settingsInterface.BackgroundColor },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                }, "OverlayNonScaled", UI_INTERFACE);
                container.Add(new CuiPanel
                {
                    CursorEnabled = true,
                    Image = {Color = _config.settingsInterface.BackgroundColorSpriteAndMaterial, Sprite = _config.settingsInterface.BackgroundSprite, Material = _config.settingsInterface.BackgroundMaterial},
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                }, UI_INTERFACE, "BACKGROUND");
            }
            else
            {
                container.Add(new CuiPanel
                {
                    CursorEnabled = true,
                    Image = { Color = "0 0 0 0" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                }, "OverlayNonScaled", UI_INTERFACE);

                container.Add(new CuiElement
                {
                    Name = "BACKGROUND",
                    Parent = UI_INTERFACE,
                    Components = {
                        new CuiRawImageComponent { Color = "1 1 1 1", Png = background },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1"}
                    }
                });
            }

            container.Add(new CuiElement
            {
                Name = UI_CLOSE_MENU,
                Parent = "BACKGROUND",
                Components = {
                    new CuiRawImageComponent { Color = "1 1 1 1", Png =  _imageUI.GetImage("14") },
                    new CuiRectTransformComponent { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = "-340 -56.798", OffsetMax = "-305 -27.942" }
                }
            });

            container.Add(new CuiButton
            {
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Button = { Close = UI_INTERFACE, Color = "0 0 0 0" },
                Text = { Text = "" }
            }, UI_CLOSE_MENU);

            CuiHelper.DestroyUi(player, UI_INTERFACE);
            CuiHelper.AddUi(player, container);
            MenuButton(player);
        }
        private void MenuButton(BasePlayer player, int page = 0)
        {
            var container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = "1 1 1 0" },
                RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-132.238 -75.531", OffsetMax = "122.197 -48.564" }
            }, UI_INTERFACE, UI_MENU_BUTTON);
            container.Add(new CuiButton
            {
                RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "-0.001 -13.483", OffsetMax = "88.113 13.484" },
                Button = { Command = $"UI_HandlerStat Page_swap 0", Color = "0 0 0 0" },
                Text = { Text = "STAT_UI_MY_STAT".GetAdaptedMessage(player.UserIDString), FontSize = 10, Font = "robotocondensed-regular.ttf", Align = TextAnchor.MiddleCenter }
            }, UI_MENU_BUTTON, "BUTTON_MY_STAT");

            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = page == 0 ? "0.2988604 0.6886792 0.120194 0.6431373" : "1 1 1 0.3" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-44.057 -13.483", OffsetMax = "44.057 -11.642" }
            }, "BUTTON_MY_STAT", "Panel_8193");

            container.Add(new CuiButton
            {
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-39.106 -13.483", OffsetMax = "65.745 13.484" },
                Button = { Command = $"UI_HandlerStat Page_swap 1", Color = "0 0 0 0" },
                Text = { Text = "STAT_UI_TOP_TEN".GetAdaptedMessage(player.UserIDString), FontSize = 10, Font = "robotocondensed-regular.ttf", Align = TextAnchor.MiddleCenter }
            }, UI_MENU_BUTTON, "BUTTON_TOPTEN_USER");

            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = page == 1 ? "0.2988604 0.6886792 0.120194 0.64313732" : "1 1 1 0.3" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-52.425 -13.484", OffsetMax = "52.425 -11.642" }
            }, "BUTTON_TOPTEN_USER", "Panel_8193");

            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-61.472 -13.483", OffsetMax = "0.216 13.484" }
            }, UI_MENU_BUTTON, "BUTTON_PAGE_SEARCH");

            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = page == 2 ? "0.2988604 0.6886792 0.120194 0.6431373" : "1 1 1 0.3" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-30.844 -13.484", OffsetMax = "30.845 -11.642" }
            }, "BUTTON_PAGE_SEARCH");
            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-30.844 -8.36", OffsetMax = "12.862 8.361" },
                Text = { Text = "STAT_UI_SEARCH".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, "BUTTON_PAGE_SEARCH");

            container.Add(new CuiElement
            {
                Parent = "BUTTON_PAGE_SEARCH",
                Components = {
                    new CuiImageComponent { Color = "1 1 1 1", Sprite = "assets/icons/examine.png" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "12.862 -6.5", OffsetMax = "25.862 6.5" }
                }
            });
            container.Add(new CuiButton
            {
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Button = { Command = $"UI_HandlerStat Page_swap 2", Color = "0 0 0 0" },
                Text = { Text = "" }
            }, "BUTTON_PAGE_SEARCH");

            CuiHelper.DestroyUi(player, UI_MENU_BUTTON);
            CuiHelper.AddUi(player, container);
            if (page == 0)
                UserStat(player);
            else if (page == 1)
                TopTen(player);
            else
                SearchPageUser(player);
        }
        #endregion

        #region SearchPage
        private void SearchPageUser(BasePlayer player, string target = "")
        {
            string SearchName = "";
            var container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = "1 1 1 0" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-378.454 -264.835", OffsetMax = "381.998 266.939" }
            }, UI_INTERFACE, UI_SEARCH_USER);

            container.Add(new CuiElement
            {
                Name = "SEARCH_LINE",
                Parent = UI_SEARCH_USER,
                Components = {
                    new CuiRawImageComponent { Color = "1 1 1 1", Png =  _imageUI.GetImage("18") },
                    new CuiRectTransformComponent {AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-181.67 -31.1", OffsetMax = "149.27 -7.1" }
                }
            });

            container.Add(new CuiElement
            {
                Name = "LOUPE_SEARCH_IMG",
                Parent = "SEARCH_LINE",
                Components = {
                    new CuiImageComponent { Color = "1 1 1 1", Sprite = "assets/icons/examine.png" },
                    new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "13.87 -10", OffsetMax = "33.87 10" }
                }
            });

            container.Add(new CuiElement
            {
                Name = "INPUT_SEARCH",
                Parent = "SEARCH_LINE",
                Components = {
                    new CuiInputFieldComponent { Text = SearchName, Command = $"UI_HandlerStat listplayer {SearchName}", Color = "1 1 1 1", FontSize = 10, Align = TextAnchor.MiddleLeft, NeedsKeyboard = true, CharsLimit = 45 },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-119.86 -9.314", OffsetMax = "129.03 9.591" }
                }
            });

            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-181.67 -1.95", OffsetMax = "149.27 212.35" }
            }, UI_SEARCH_USER, "LIST_USER_SEARCH");

            int y = 0, x = 0;
            string targetLower = target.ToLower();

            foreach (KeyValuePair<ulong, PlayerInfo> players in PlayerInfo._players.Where(z => z.Value.Name.ToLower().Contains(targetLower) && !_config.settings.ignoreList.Contains(z.Key)))
            {
                string LockStatus = players.Value.HidedStatistics ? "assets/icons/lock.png" : "assets/icons/unlock.png";
                string Command = players.Value.HidedStatistics ? "" : $"UI_HandlerStat GoStatPlayers {players.Key}";
                string nickName =  "<color=white>" + GetCorrectName(players.Value.GetPlayerName(player.userID), 14) + "</color>";
                if (permission.UserHasPermission(player.UserIDString, PermAdmin))
                    Command = $"UI_HandlerStat GoStatPlayers {players.Key}";

                container.Add(new CuiElement
                {
                    Name = "USER_IN_SEARCH",
                    Parent = "LIST_USER_SEARCH",
                    Components = {
                        new CuiImageComponent() {Color = "0.968627453 0.921631568632 0.882352948 0.03529412", Material = "assets/content/ui/namefontmaterial.mat"},
                        new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{-164.971 + (x * 112.586)} {84.138 - (y * 26.281)}", OffsetMax = $"{-62.801 + (x * 112.586)} {105.623 - (y * 26.281)}" }
                    }
                });

                container.Add(new CuiElement
                {
                    Name = "USER_HIDE_PROFILE",
                    Parent = "USER_IN_SEARCH",
                    Components = {
                    new CuiImageComponent { Color = "1 1 1 1", Sprite = LockStatus },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-45.68 -7.5", OffsetMax = "-30.68 7.5" }
                }
                });
                container.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-26.832 -10.743", OffsetMax = "48.365 10.743" },
                    Text = { Text = nickName , Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }
                }, "USER_IN_SEARCH");

                container.Add(new CuiButton
                {
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    Button = { Command = Command, Color = "0 0 0 0" },
                    Text = { Text = ""}
                }, "USER_IN_SEARCH");

                x++;
                if (x == 3)
                {
                    x = 0;
                    y++;
                    if (y == 8)
                        break;
                }
            }
            CloseLayer(player);
            CuiHelper.AddUi(player, container);
        }
        #endregion

        #region UserInfoPage
        private void UserStat(BasePlayer player, ulong target = 0)
        {
            ulong userid = target == 0 ? player.userID : target;
            PlayerInfo statInfo = PlayerInfo.GetOrCreate(userid);
            
            string color = BasePlayer.FindByID(userid) != null ? "0.55 0.78 0.24 1" : "0.8 0.28 0.2 1";
            int kills = _config.settings.pveServerMode ? statInfo.PlayerPvpStats.KillsNpc : statInfo.PlayerPvpStats.Kills;
            string titleKills = _config.settings.pveServerMode ? "STAT_UI_PVP_KILLS_NPC".GetAdaptedMessage(player.UserIDString) : "STAT_UI_PVP_KILLS".GetAdaptedMessage(player.UserIDString);
            string pageTitle =  "<color=white>" + statInfo.Name + "</color>";
            var container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-400.254 -331.554", OffsetMax = "393.974 274.446" }
            }, UI_INTERFACE, UI_USER_STAT);

            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1"}
            }, UI_USER_STAT, UI_USER_STAT_INFO);

            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "14.908 -69.438", OffsetMax = "227.492 -53.162" },
                Text = { Text = "STAT_UI_INFO".GetAdaptedMessage(player.UserIDString, pageTitle), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }
            }, UI_USER_STAT_INFO, "INFO_USER_NICK");
            
            container.Add(new CuiElement
            {
                Name = "USER_AVATAR_LAYER",
                Parent = UI_USER_STAT_INFO,
                Components = {
                    new CuiRawImageComponent { Color = color, Png =  _imageUI.GetImage("2") },
                    new CuiRectTransformComponent {AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "15 -130", OffsetMax = "68 -77" }
                }
            });

            container.Add(new CuiElement
            {
                Name = "AVATAR_ON_STEAM",
                Parent = "USER_AVATAR_LAYER",
                Components = {
                    new CuiRawImageComponent { SteamId = target == 0 ? player.UserIDString : target.ToString(), Color = "1 1 1 1"},
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-25 -25", OffsetMax = "25 25" }
                }
            });

            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "15.477 -179.883", OffsetMax = "87.323 -161.717" },
                Text = { Text = "STAT_UI_ACTIVITY".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }
            }, UI_USER_STAT_INFO, "USER_ACTIVE");

            if (target == 0 && (_config.settings.availabilityUse && (permission.UserHasPermission(player.UserIDString, PermAvailability) ||  _config.settings.dropStatUse && permission.UserHasPermission(player.UserIDString, PermReset))))
            {
                container.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "15.477 -290.083", OffsetMax = "87.323 -271.917" },
                    Text = { Text = "STAT_UI_SETTINGS".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }
                }, UI_USER_STAT_INFO, "USER_SETINGS");
            }

            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "14.8 -151.281", OffsetMax = "227.38 -134.319" },
                Text = { Text = "STAT_UI_PLACE_TOP".GetAdaptedMessage(player.UserIDString, GetTopScore(target == 0 ? player.userID : target)), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = "0.8538514 0.8491456 0.8867924 1" }
            }, UI_USER_STAT_INFO, "TOP_IN_USER");

            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "14.69 -201.181", OffsetMax = "227.27 -184.219" },
                Text = { Text = "STAT_UI_ACTIVITY_TODAY".GetAdaptedMessage(player.UserIDString, TimeHelper.FormatTime(TimeSpan.FromMinutes(statInfo.playedTime.PlayedToday), 5, lang.GetLanguage(player.UserIDString))), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = "0.8538514 0.8491456 0.8867924 1" }
            }, UI_USER_STAT_INFO, "TODAY_ACTIVE_USER");

            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-382.31 79.219", OffsetMax = "-169.73 96.181" },
                Text = { Text = "STAT_UI_ACTIVITY_TOTAL".GetAdaptedMessage(player.UserIDString, TimeHelper.FormatTime(TimeSpan.FromMinutes(statInfo.playedTime.PlayedForWipe), 5, lang.GetLanguage(player.UserIDString))), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = "0.8538514 0.8491456 0.8867924 1" }
            }, UI_USER_STAT_INFO, "ALLTIME_ACTIVE_USER");

            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-382.31 56.919", OffsetMax = "-169.73 73.881" },
                Text = { Text = "STAT_UI_SCORE".GetAdaptedMessage(player.UserIDString, statInfo.ScoreFormatted()), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = "0.8538514 0.8491456 0.8867924 1" }
            }, UI_USER_STAT_INFO, "SCORE_USER");

            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = "-209.5 -67.825", OffsetMax = "-130.043 -53.66" },
                Text = { Text = "STAT_UI_PVP".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }
            }, UI_USER_STAT_INFO, "PVP_STAT_USER");

            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-209.505 36.618", OffsetMax = "-13.975 50.782" },
                Text = { Text = "STAT_UI_FAVORITE_WEAPON".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }
            }, UI_USER_STAT_INFO, "RIFLE_FAVORITE_USER");

            #region KillStat
            container.Add(new CuiElement
            {
                Name = "KILL_STAT_PLAYER",
                Parent = UI_USER_STAT_INFO,
                Components = {
                    new CuiImageComponent() { Color = "0.968627453 0.921631568632 0.882352948 0.03529412", Material = "assets/content/ui/namefontmaterial.mat", ImageType = Image.Type.Tiled},
                    new CuiRectTransformComponent { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = "-209.389 -123.243", OffsetMax = "-14.469 -78.4" }
                }
            });

            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "19 -7.014", OffsetMax = "97 8.414" },
                Text = { Text = titleKills, Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }
            }, "KILL_STAT_PLAYER", "LABEL_KILL_AMOUNT");

            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-98 -7.014", OffsetMax = "-16.845 8.414" },
                Text = { Text = kills.ToString(), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleRight, Color = "0.8538514 0.8491456 0.8867924 1" }
            }, "KILL_STAT_PLAYER", "LABEL_KILL_AMOUNTTWO");
            #endregion

            #region DeathStat
            container.Add(new CuiElement
            {
                Name = "KILLSHOT_STAT_PLAYER",
                Parent = UI_USER_STAT_INFO,
                Components = {
                    new CuiImageComponent() { Color = "0.968627453 0.921631568632 0.882352948 0.03529412", Material = "assets/content/ui/namefontmaterial.mat", ImageType = Image.Type.Tiled},
                    new CuiRectTransformComponent { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = "-209.389 -173.691", OffsetMax = "-14.469 -128.849" }
                }
            });

            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "19 -7.014", OffsetMax = "97 8.414" },
                Text = { Text = "STAT_UI_PVP_DEATH".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }
            }, "KILLSHOT_STAT_PLAYER", "LABEL_KILL_AMOUNT");

            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-98 -7.014", OffsetMax = "-16.845 8.414" },
                Text = { Text = statInfo.PlayerPvpStats.Deaths.ToString(), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleRight, Color = "0.8538514 0.8491456 0.8867924 1" }
            }, "KILLSHOT_STAT_PLAYER", "LABEL_KILL_AMOUNTTWO");
            #endregion

            #region KDRStat
            container.Add(new CuiElement
            {
                Name = "DEATCH_STAT_PLAYER",
                Parent = UI_USER_STAT_INFO,
                Components = {
                    new CuiImageComponent() { Color = "0.968627453 0.921631568632 0.882352948 0.03529412", Material = "assets/content/ui/namefontmaterial.mat", ImageType = Image.Type.Tiled},
                    new CuiRectTransformComponent { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = "-209.39 -224.721", OffsetMax = "-14.47 -179.879" }
                }
            });
            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "19 -7.014", OffsetMax = "97 8.414" },
                Text = { Text = "STAT_UI_PVP_KDR".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }
            }, "DEATCH_STAT_PLAYER", "LABEL_KILL_AMOUNT");

            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-98 -7.014", OffsetMax = "-16.845 8.414" },
                Text = { Text = statInfo.PlayerPvpStats.CalculateKDR(kills), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleRight, Color = "0.8538514 0.8491456 0.8867924 1" }
            }, "DEATCH_STAT_PLAYER", "LABEL_KILL_AMOUNTTWO");
            #endregion

            #region FavoriteWeapon
            container.Add(new CuiElement
            {
                Name = "FAVORITE_WEAPON_STAT_PLAYER",
                Parent = UI_USER_STAT_INFO,
                Components = {
                    new CuiImageComponent() { Color = "0.968627453 0.921631568632 0.882352948 0.03529412", Material = "assets/content/ui/namefontmaterial.mat", ImageType = Image.Type.Tiled},
                    new CuiRectTransformComponent { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = "-210.15 -324.607", OffsetMax = "-13.977 -278.569" }
                }
            });

            KeyValuePair<string, PlayerInfo.WeaponInfo> weaponTop = statInfo.WeaponUsed.OrderByDescending(x => x.Value.Kills).Take(1).FirstOrDefault();
            if (weaponTop.Key != null)
            {
                container.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "46.321 -16.5", OffsetMax = "176.509 16.5" },
                    Text = { Text = "STAT_UI_FAVORITE_WEAPON_KILLS".GetAdaptedMessage(player.UserIDString, weaponTop.Value.Kills, weaponTop.Value.Shots), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
                }, "FAVORITE_WEAPON_STAT_PLAYER", "LABEL_KILL_AMOUNT");

                container.Add(new CuiElement
                {
                    Name = "WEAPON_IMG_USER",
                    Parent = "FAVORITE_WEAPON_STAT_PLAYER",
                    Components = {
                    new CuiImageComponent { ItemId = ItemManager.FindItemDefinition(weaponTop.Key).itemid, },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-88.5 -16.5", OffsetMax = "-55.5 16.5" }
                }
                });
            }
            else
            {
                container.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    Text = { Text = "STAT_UI_FAVORITE_WEAPON_NOT_DATA".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
                }, "FAVORITE_WEAPON_STAT_PLAYER", "LABEL_KILL_AMOUNT");
            }
            #endregion

            #region OtherStat

            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-209.498 -63.383", OffsetMax = "-79.669 -49.219" },
                Text = { Text = "STAT_UI_OTHER_STAT".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }
            }, UI_USER_STAT_INFO, "OTHER_STAT_LABEL");

            #endregion

            CloseLayer(player);
            CuiHelper.DestroyUi(player, "USER_STAT");
            CuiHelper.AddUi(player, container);
            CategoryStatUser(player, target);
            OtherStatUser(player, target);
            if (target == 0)
            {
                if (_config.settings.dropStatUse && permission.UserHasPermission(player.UserIDString, PermReset))
                    ButtonDropStat(player, statInfo);
                if (_config.settings.availabilityUse && permission.UserHasPermission(player.UserIDString, PermAvailability))
                    ButtonHideStat(player, statInfo);
            }
        }
        private void ButtonHideStat(BasePlayer player, PlayerInfo info)
        {
            var container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "13.865 -8.619", OffsetMax = "160.34 6.226" }
            }, UI_USER_STAT_INFO, "BUTTON_HIDE_STAT");

            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-120.779 -7.423", OffsetMax = "21.525 9.815" },
                Text = { Text = "STAT_UI_HIDE_STAT".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = "0.8538514 0.8491456 0.8867924 1" }
            }, "BUTTON_HIDE_STAT", "LABEL_HIDE_USER");


            if (!info.HidedStatistics)
            {
                container.Add(new CuiElement
                {
                    Name = "CHECK_BOX_HIDE",
                    Parent = "BUTTON_HIDE_STAT",
                    Components = {
                        new CuiRawImageComponent { Color = "1 1 1 1", Png =  _imageUI.GetImage("4")},
                        new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "1.381 -6.404", OffsetMax = "14.381 6.596" }
                    }
                });
            }
            else
            {
                container.Add(new CuiElement
                {
                    Name = "CHECK_BOX_HIDE",
                    Parent = "BUTTON_HIDE_STAT",
                    Components = {
                    new CuiRawImageComponent { Color = "1 1 1 1", Png =  _imageUI.GetImage("3")},
                    new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "1.381 -6.404", OffsetMax = "14.381 6.596" }
                }
                });
            }
            container.Add(new CuiButton
            {
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Button = { Command = "UI_HandlerStat hidestat", Color = "0 0 0 0" },
                Text = { Text = "" }
            }, "BUTTON_HIDE_STAT");

            CuiHelper.DestroyUi(player, "BUTTON_HIDE_STAT");
            CuiHelper.AddUi(player, container);
        }

        private void DialogConfirmationDropStat(BasePlayer player)
        {
            var container = new CuiElementContainer();
            container.Add(new CuiElement
            {
                Name = "CONFIRMATIONS",
                Parent = UI_USER_STAT_INFO,
                Components = {
                    new CuiRawImageComponent { Color = "1 1 1 1", Png =  _imageUI.GetImage("6") },
                    new CuiRectTransformComponent {AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "13.64 -104.387", OffsetMax = "160.12 -45.246" }
                }
            });

            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-73.24 -30", OffsetMax = "73.24 -0.43" },
                Text = { Text = "STAT_UI_CONFIRM".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, "CONFIRMATIONS");

            container.Add(new CuiElement
            {
                Name = "CONFIRMATIONS_YES",
                Parent = "CONFIRMATIONS",
                Components = {
                    new CuiRawImageComponent { Color = "1 1 1 1", Png =  _imageUI.GetImage("5")},
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "11.28 6.7", OffsetMax = "46.28 23.7" }
                }
            });

            container.Add(new CuiButton
            {
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-22.5 -11", OffsetMax = "22.5 11" },
                Button = { Command = "UI_HandlerStat confirm_yes", Color = "0 0 0 0" },
                Text = { Text = "STAT_UI_CONFIRM_YES".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "0 0 0 1" }
            }, "CONFIRMATIONS_YES", "LABEL_YES");

            container.Add(new CuiElement
            {
                Name = "CONFIRMATIONS_NO",
                Parent = "CONFIRMATIONS",
                Components = {
                    new CuiRawImageComponent { Color = "1 1 1 1", Png =  _imageUI.GetImage("5")},
                    new CuiRectTransformComponent { AnchorMin = "1 0", AnchorMax = "1 0", OffsetMin = "-46.8 6.7", OffsetMax = "-11.8 23.7" }
                }
            });

            container.Add(new CuiButton
            {
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-22.5 -11", OffsetMax = "22.5 11" },
                Button = { Close = "CONFIRMATIONS", Color = "0 0 0 0" },
                Text = { Text = "STAT_UI_CONFIRM_NO".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "0 0 0 1" }
            }, "CONFIRMATIONS_NO", "LABEL_NO");

            CuiHelper.DestroyUi(player, "CONFIRMATIONS");
            CuiHelper.AddUi(player, container);
        }

        private void ButtonDropStat(BasePlayer player, PlayerInfo info)
        {
            var container = new CuiElementContainer();
            
            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "13.75 -29.559", OffsetMax = "160.23 -15.727" }
            }, UI_USER_STAT_INFO, "BUTTON_REFRESH_STAT");

            container.Add(new CuiElement
            {
                Name = "USER_REFRESH_STAT",
                Parent = "BUTTON_REFRESH_STAT",
                Components = {
                    new CuiImageComponent { Color = "1 1 1 1", Sprite = "assets/icons/clear_list.png" },
                    new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "1.74 -6.5", OffsetMax = "14.74 6.5" }
                }
            });

            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-120.55 -6.916", OffsetMax = "21.75 8.202" },
                Text = { Text = "STAT_UI_RESET_STAT".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = "0.8538514 0.8491456 0.8867924 1" }
            }, "BUTTON_REFRESH_STAT", "LABEL_REFRESH_USER");

            container.Add(new CuiButton
            {
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Button = { Command = "UI_HandlerStat confirm", Color = "0 0 0 0" },
                Text = { Text = "" }
            }, "BUTTON_REFRESH_STAT");

            CuiHelper.DestroyUi(player, "BUTTON_REFRESH_STAT");
            CuiHelper.AddUi(player, container);
        }


        private void OtherStatUser(BasePlayer player, ulong target = 0, int statType = 0)
        {
            var container = new CuiElementContainer();
            PlayerInfo statInfo = PlayerInfo.GetOrCreate(target == 0 ? player.userID : target);
            

            if (statType == 0)
            {
                #region CrateStat
                container.Add(new CuiElement
                {
                    Name = "CRATE_STAT",
                    Parent = UI_USER_STAT_INFO,
                    Components = {
                        new CuiImageComponent() { Color = "0.968627453 0.921631568632 0.882352948 0.03529412", Material = "assets/content/ui/namefontmaterial.mat"},
                        new CuiRectTransformComponent { AnchorMin = "1 0", AnchorMax = "1 0", OffsetMin = "-210.146 181.391", OffsetMax = "-13.974 227.429" }
                }
                });
                
                container.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "48.359 -7.014", OffsetMax = "176.509 8.414" },
                    Text = { Text = "STAT_UI_CRATE_OPEN".GetAdaptedMessage(player.UserIDString, statInfo.MiscStats.CrateOpen), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
                }, "CRATE_STAT");
                
                container.Add(new CuiElement
                {
                    Parent = "CRATE_STAT",
                    Components = {
                    new CuiRawImageComponent { Color = "1 1 1 1", Png =  _imageUI.GetImage("13") },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-88.5 -16.5", OffsetMax = "-55.5 16.5" }
                }
                });
                #endregion
            
                #region BarrelStat
                container.Add(new CuiElement
                {
                    Name = "BARREL_STAT",
                    Parent = UI_USER_STAT_INFO,
                    Components = {
                        new CuiImageComponent() { Color = "0.968627453 0.921631568632 0.882352948 0.03529412", Material = "assets/content/ui/namefontmaterial.mat"},
                        new CuiRectTransformComponent { AnchorMin = "1 0", AnchorMax = "1 0", OffsetMin = "-210.146 129.171", OffsetMax = "-13.974 175.209" }
                    }
                });
            
                container.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "48.359 -7.014", OffsetMax = "176.509 8.414" },
                    Text = { Text = "STAT_UI_BARREL_KILL".GetAdaptedMessage(player.UserIDString, statInfo.MiscStats.BarrelsDestroyed), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
                }, "BARREL_STAT");
            
                container.Add(new CuiElement
                {
                    Parent = "BARREL_STAT",
                    Components = {
                    new CuiRawImageComponent { Color = "1 1 1 1",  Png =  _imageUI.GetImage("12") },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-88.5 -16.5", OffsetMax = "-55.5 16.5" }
                }
                });
                #endregion
            
                #region AnimalKillStat
                container.Add(new CuiElement
                {
                    Name = "ANIMALKILL_STAT",
                    Parent = UI_USER_STAT_INFO,
                    Components = {
                        new CuiImageComponent() { Color = "0.968627453 0.921631568632 0.882352948 0.03529412", Material = "assets/content/ui/namefontmaterial.mat"},
                    new CuiRectTransformComponent { AnchorMin = "1 0", AnchorMax = "1 0", OffsetMin = "-210.146 76.951", OffsetMax = "-13.974 122.989" }
                }
                });
            
                container.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "48.359 -7.014", OffsetMax = "176.509 8.414" },
                    Text = { Text = "STAT_UI_ANIMAL_KILL".GetAdaptedMessage(player.UserIDString, statInfo.PlayerPvpStats.KillsAnimal), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
                }, "ANIMALKILL_STAT");
            
                container.Add(new CuiElement
                {
                    Parent = "ANIMALKILL_STAT",
                    Components = {
                    new CuiRawImageComponent { Color = "1 1 1 1", Png =  _imageUI.GetImage("17") },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-88.5 -16.5", OffsetMax = "-55.5 16.5" }
                }
                });
                #endregion
            }
            else
            {
                #region HeliStat
                container.Add(new CuiElement
                {
                    Name = "Heli_STAT",
                    Parent = UI_USER_STAT_INFO,
                    Components = {
                        new CuiImageComponent() { Color = "0.968627453 0.921631568632 0.882352948 0.03529412", Material = "assets/content/ui/namefontmaterial.mat"},
                    new CuiRectTransformComponent { AnchorMin = "1 0", AnchorMax = "1 0", OffsetMin = "-210.146 181.391", OffsetMax = "-13.974 227.429" }
                }
                });
            
                container.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "48.359 -7.014", OffsetMax = "176.509 8.414" },
                    Text = { Text = "STAT_UI_HELI_KILL".GetAdaptedMessage(player.UserIDString, statInfo.PlayerPvpStats.HeliKill), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
                }, "Heli_STAT");
            
                container.Add(new CuiElement
                {
                    Parent = "Heli_STAT",
                    Components = {
                    new CuiRawImageComponent { Color = "1 1 1 1", Png =  _imageUI.GetImage("15") },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-88.5 -16.5", OffsetMax = "-55.5 16.5" }
                }
                });
                #endregion
            
                #region BradleyStat
                container.Add(new CuiElement
                {
                    Name = "BRADLEY_STAT",
                    Parent = UI_USER_STAT_INFO,
                    Components = {
                        new CuiImageComponent() { Color = "0.968627453 0.921631568632 0.882352948 0.03529412", Material = "assets/content/ui/namefontmaterial.mat"},
                    new CuiRectTransformComponent { AnchorMin = "1 0", AnchorMax = "1 0", OffsetMin = "-210.146 129.171", OffsetMax = "-13.974 175.209" }
                }
                });
            
                container.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "48.359 -7.014", OffsetMax = "176.509 8.414" },
                    Text = { Text = "STAT_UI_BRADLEY_KILL".GetAdaptedMessage(player.UserIDString, statInfo.PlayerPvpStats.BradleyKill), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
                }, "BRADLEY_STAT");
            
                container.Add(new CuiElement
                {
                    Parent = "BRADLEY_STAT",
                    Components = {
                    new CuiRawImageComponent { Color = "1 1 1 1",  Png =  _imageUI.GetImage("16") },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-88.5 -16.5", OffsetMax = "-55.5 16.5" }
                }
                });
                #endregion
            
                #region NpcKillStat
                container.Add(new CuiElement
                {
                    Name = "NPCKILL_STAT",
                    Parent = UI_USER_STAT_INFO,
                    Components = {
                        new CuiImageComponent() { Color = "0.968627453 0.921631568632 0.882352948 0.03529412", Material = "assets/content/ui/namefontmaterial.mat"},
                    new CuiRectTransformComponent { AnchorMin = "1 0", AnchorMax = "1 0", OffsetMin = "-210.146 76.951", OffsetMax = "-13.974 122.989" }
                }
                });
            
                container.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "48.359 -7.014", OffsetMax = "176.509 8.414" },
                    Text = { Text = "STAT_UI_NPC_KILL".GetAdaptedMessage(player.UserIDString, statInfo.PlayerPvpStats.KillsNpc), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
                }, "NPCKILL_STAT");
            
                container.Add(new CuiElement
                {
                    Name = "NPC_IMG_USER",
                    Parent = "NPCKILL_STAT",
                    Components = {
                    new CuiRawImageComponent { Color = "1 1 1 1", Png =  _imageUI.GetImage("11") },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-88.5 -16.5", OffsetMax = "-55.5 16.5" }
                }
                });
                #endregion
            }

            container.Add(new CuiButton
            {
                Button = { Color = "1 1 1 0.1", Command = $"UI_HandlerStat ShowMoreStat {target} {statType}" },
                Text = { Text = "STAT_UI_BTN_MORE".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.8538514 0.8491456 0.8867924 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "186.97 -254.682", OffsetMax = "383.15 -233.6" }
            }, UI_USER_STAT_INFO, "SHOW_MORE_STAT");

            CuiHelper.DestroyUi(player, "SHOW_MORE_STAT");
            CuiHelper.DestroyUi(player, "CRATE_STAT");
            CuiHelper.DestroyUi(player, "BARREL_STAT");
            CuiHelper.DestroyUi(player, "NPCKILL_STAT");
            CuiHelper.DestroyUi(player, "Heli_STAT");
            CuiHelper.DestroyUi(player, "BRADLEY_STAT");
            CuiHelper.DestroyUi(player, "ANIMALKILL_STAT");
            CuiHelper.AddUi(player, container);
        }

        #region CategoryAndStat
       
        private void CategoryStatUser(BasePlayer player, ulong target = 0, int cat = 0)
        {
            var container = new CuiElementContainer();
            PlayerInfo statInfo = PlayerInfo.GetOrCreate(target == 0 ? player.userID : target);
            Dictionary<string, int> list = GetCategory(statInfo, cat);
            #region line
            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = "0.5803922 0.572549 0.6117647 0.4313726" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"151.69 {181.046 - ((list.Count - 1) * 50.729)}", OffsetMax = $"153.21 225.49" }
            }, UI_USER_STAT_INFO, "STAT_LINE");

            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = "0.5803922 0.572549 0.6117647 1" },
                RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = $"-0.761 {-23.343 - ((list.Count - 1) * 30.729)}", OffsetMax = "0.761 0.17" }
            }, "STAT_LINE", "STAT_LINE_CHILD");
            #endregion

            #region USER STAT BUTTON CATEGORY
            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-146.988 -71.4", OffsetMax = "146.388 -53.16" }
            }, UI_USER_STAT_INFO, "MENU_USER_STAT");

            container.Add(new CuiButton
            {
                RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "0.312 -9.12", OffsetMax = "84.704 9.121" },
                Button = { Command = $"UI_HandlerStat changeCategory {target} 0", Color = "0 0 0 0" },
                Text = { Text = "STAT_UI_CATEGORY_GATHER".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleCenter, }
            }, "MENU_USER_STAT", "Panel_5655");

            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = cat == 0 ? "0.2988604 0.6886792 0.120194 0.6431373" : "0 0 0 0" },
                RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "-42.347 0", OffsetMax = "42.196 1.871" }
            }, "Panel_5655", "Panel_8052");

            container.Add(new CuiButton
            {
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-61.983 -9.121", OffsetMax = "31.232 9.12" },
                Button = { Command = $"UI_HandlerStat changeCategory {target} 1", Color = "0 0 0 0" },
                Text = { Text = "STAT_UI_CATEGORY_EXPLOSED".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleCenter, }
            }, "MENU_USER_STAT", "Panel_56551");

            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = cat == 1 ? "0.2988604 0.6886792 0.120194 0.6431373" : "0 0 0 0" },
                RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "-46.608 0", OffsetMax = "46.608 1.871" }
            }, "Panel_56551", "Panel_8052");

            container.Add(new CuiButton
            {
                RectTransform = { AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-115.459 -9.12", OffsetMax = "0.001 9.121" },
                Button = { Command = $"UI_HandlerStat changeCategory {target} 2", Color = "0 0 0 0" },
                Text = { Text = "STAT_UI_CATEGORY_PLANT".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleCenter, }
            }, "MENU_USER_STAT", "Panel_56553");

            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = cat == 2 ? "0.2988604 0.6886792 0.120194 0.6431373" : "0 0 0 0" },
                RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "-57.73 0", OffsetMax = "57.73 1.871" }
            }, "Panel_56553", "Panel_8052");
            #endregion

            #region USER STAT CATEGORY
            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-146.991 -124.121", OffsetMax = "146.389 226.031" }
            }, "USER_STAT_INFO", "MAIN_LIST_STAT_USER");

            int y = 0;
            string userLang = lang.GetLanguage(player.UserIDString);
            foreach (KeyValuePair<string, int> item in list)
            {
                float fade = 0.15f * y;
                string itemName;
                if (_itemName.ContainsKey(item.Key))
                    itemName = userLang == "ru" ? _itemName[item.Key].RUdisplayName : _itemName[item.Key].ENdisplayName;
                else
                {
                    itemName = "";
                    ItemDefinition itemDefinition = ItemManager.FindItemDefinition(item.Key);
                    if (itemDefinition != null && itemDefinition.displayName is { english: not null })
                    {
                        itemName = itemDefinition.displayName.english;
                    }
                }
                container.Add(new CuiPanel
                {
                    FadeOut = 0.30f,
                    RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = $"-146.118 {-44.977 - (y * 50.729)}", OffsetMax = $"146.692 {-0.765 - (y * 50.729)}" },
                    Image = { Color = "0.968627453 0.921631568632 0.882352948 0.03529412",  Material = "assets/content/ui/namefontmaterial.mat", FadeIn = fade },
                }, "MAIN_LIST_STAT_USER", "STAT_USER_LINE");

                string name = cat == 0 ? item.Value.ToFormattedString() : item.Value.ToString();

                container.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "26.03 -10.534", OffsetMax = "126.03 12.574" },
                    Text = { Text = name, Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleRight, Color = "0.8538514 0.8491456 0.8867924 1", FadeIn = fade }
                }, "STAT_USER_LINE", "STAT_USER_AMOUNT");

                if (item.Key == "all")
                {
                    string langGet = cat switch
                    {
                        0 => "STAT_USER_TOTAL_GATHERED",
                        1 => "STAT_USER_TOTAL_EXPLODED",
                        _ => "STAT_USER_TOTAL_GROWED"
                    };

                    container.Add(new CuiLabel
                    {
                        RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-128 -10.534", OffsetMax = "50 12.574" },
                        Text = { Text = langGet.GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1", FadeIn = fade }
                    }, "STAT_USER_LINE", "ALL_TOTAL");
                }
                else
                {
                    container.Add(new CuiElement
                    {
                        Name = "IMAGE_ITEM",
                        Parent = "STAT_USER_LINE",
                        Components = {
                            new CuiImageComponent { ItemId = ItemManager.FindItemDefinition(item.Key).itemid, FadeIn = fade},
                            new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-128 -17.5", OffsetMax = "-93 17.5" }
                        }
                    });
                    
                    container.Add(new CuiLabel
                    {
                        RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-73.17 -10.534", OffsetMax = "50 12.574" },
                        Text = { Text = itemName, Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = "0.8538514 0.8491456 0.8867924 1" , FadeIn = fade}
                    }, "STAT_USER_LINE");
                }
                y++;
            }

            #endregion

            CuiHelper.DestroyUi(player, "MENU_USER_STAT");
            CuiHelper.DestroyUi(player, "MAIN_LIST_STAT_USER");
            CuiHelper.DestroyUi(player, "STAT_LINE");
            CuiHelper.AddUi(player, container);
        }
        #endregion
        #endregion

        #region TopTenUserStatPage
        
        private void TopTen(BasePlayer player)
        {
            var container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = "1 1 1 0" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-640 -360.001", OffsetMax = "640 266.939" }
            }, UI_INTERFACE, UI_TOP_TEN_USER);

            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-599.3 -237.224", OffsetMax = "589.334 271.441" }
            }, UI_TOP_TEN_USER, "TOP_10_TABLE");
            
            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "-203.171 70.3525", OffsetMax = "203.171 107.4625" },
                Text = { Text = "STAT_UI_TOP_TEN_FOOTER".GetAdaptedMessage(player.UserIDString), Font = "permanentmarker.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, UI_TOP_TEN_USER);

            
            if (_config.settings.pveServerMode)
            {
                #region KillerNPC
                container.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "9.2 -20.655", OffsetMax = "176.343 -3.545" },
                    Text = { Text = "STAT_UI_CATEGORY_TOP_KILLER_ANIMALS".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }
                }, "TOP_10_TABLE");

                container.Add(new CuiElement
                {
                    Name = "TOP_TABLE_0",
                    Parent = "TOP_10_TABLE",
                    Components = {
                    new CuiRawImageComponent { Color = "1 1 1 0.3",  Png =  _imageUI.GetImage("20") },
                    new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "9.575 -441.751", OffsetMax = "177.214 -26.861" }
                }
                });

                List<(ulong playerId, PlayerInfo playerInfo, int value, float score)> animalKiller = GetPlayerInfosByCategory(CatType.KillerAnimal);
                
                int y = 0;

                foreach ((ulong playerId, PlayerInfo playerInfo, int value, float score) item in animalKiller)
                {
                    (string LockStatus, string Command, string NickName, string Color) processedItem = ProcessItem(item.playerId, item.playerInfo, player, y);

                    container.Add(new CuiPanel
                    {
                        CursorEnabled = false,
                        Image = { Color = processedItem.Color },
                        RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"-83.82 {177.277 - (y * 42.863)}", OffsetMax = $"83.82 {207.448 - (y * 42.863)}" }
                    }, "TOP_TABLE_0", "USER_INFO");
                    container.Add(new CuiElement
                    {
                        Name = "USER_STAT_HIDE",
                        Parent = "USER_INFO",
                        Components =
                        {
                            new CuiImageComponent { Color = "1 1 1 1", Sprite = processedItem.LockStatus },
                            new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-80.2 -6.5", OffsetMax = "-67.2 6.5" }
                        }
                    });

                    container.Add(new CuiElement
                    {
                        Parent = "USER_INFO",
                        Components =
                        {
                            new CuiTextComponent { Text = processedItem.NickName, Font = "robotocondensed-regular.ttf", FontSize = 9, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
                            new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-64.822 -6.724", OffsetMax = "7.522 6.724" }
                        }
                    });

                    container.Add(new CuiLabel
                    {
                        RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "7.522 -6.724", OffsetMax = "76.934 6.724" },
                        Text = { Text = item.value.ToString(), Font = "robotocondensed-regular.ttf", FontSize = 9, Align = TextAnchor.MiddleRight, Color = "1 1 1 1" }
                    }, "USER_INFO");

                    container.Add(new CuiButton
                    {
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                        Button = { Command = processedItem.Command, Color = "0 0 0 0" },
                        Text = { Text = "" }
                    }, "USER_INFO");
                    y++;
                }

                #endregion
            }
            else
            {
                #region Killer

                container.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "9.2 -20.655", OffsetMax = "176.343 -3.545" },
                    Text = { Text = "STAT_UI_CATEGORY_TOP_KILLER".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }
                }, "TOP_10_TABLE");

                container.Add(new CuiElement
                {
                    Name = "TOP_TABLE_0",
                    Parent = "TOP_10_TABLE",
                    Components = {
                    new CuiRawImageComponent { Color = "1 1 1 0.3",  Png =  _imageUI.GetImage("20") },
                    new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "9.575 -441.751", OffsetMax = "177.214 -26.861" }
                }
                });

                List<(ulong playerId, PlayerInfo playerInfo, int value, float score)> killer = GetPlayerInfosByCategory(CatType.Killer);
                
                int y = 0;

                foreach ((ulong playerId, PlayerInfo playerInfo, int value, float score) item in killer)
                {
                    (string LockStatus, string Command, string NickName, string Color) processedItem = ProcessItem(item.playerId, item.playerInfo, player, y);

                    container.Add(new CuiPanel
                    {
                        CursorEnabled = false,
                        Image = { Color = processedItem.Color },
                        RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"-83.82 {177.277 - (y * 42.863)}", OffsetMax = $"83.82 {207.448 - (y * 42.863)}" }
                    }, "TOP_TABLE_0", "USER_INFO");
                    container.Add(new CuiElement
                    {
                        Name = "USER_STAT_HIDE",
                        Parent = "USER_INFO",
                        Components =
                        {
                            new CuiImageComponent { Color = "1 1 1 1", Sprite = processedItem.LockStatus },
                            new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-80.2 -6.5", OffsetMax = "-67.2 6.5" }
                        }
                    });

                    container.Add(new CuiElement
                    {
                        Parent = "USER_INFO",
                        Components =
                        {
                            new CuiTextComponent { Text = processedItem.NickName, Font = "robotocondensed-regular.ttf", FontSize = 9, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
                            new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-64.822 -6.724", OffsetMax = "7.522 6.724" }
                        }
                    });

                    container.Add(new CuiLabel
                    {
                        RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "7.522 -6.724", OffsetMax = "76.934 6.724" },
                        Text = { Text = item.value.ToString(), Font = "robotocondensed-regular.ttf", FontSize = 9, Align = TextAnchor.MiddleRight, Color = "1 1 1 1" }
                    }, "USER_INFO");

                    container.Add(new CuiButton
                    {
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                        Button = { Command = processedItem.Command, Color = "0 0 0 0" },
                        Text = { Text = "" }
                    }, "USER_INFO");
                    y++;
                }

                #endregion
            }

            #region Npc_Killer
            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "209.129 -20.655", OffsetMax = "376.271 -3.545" },
                Text = { Text = "STAT_UI_CATEGORY_TOP_NPCKILLER".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }
            }, "TOP_10_TABLE");

            container.Add(new CuiElement
            {
                Name = "TOP_TABLE_1",
                Parent = "TOP_10_TABLE",
                Components = {
                    new CuiRawImageComponent { Color = "1 1 1 0.3", Png =  _imageUI.GetImage("20") },
                    new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "210.281 -441.751", OffsetMax = "377.919 -26.861" }
                }
            });
            List<(ulong playerId, PlayerInfo playerInfo, int value, float score)> killerNpc = GetPlayerInfosByCategory(CatType.KillerNpc);
                
            int i = 0;

            foreach ((ulong playerId, PlayerInfo playerInfo, int value, float score) item in killerNpc)
            {
                (string LockStatus, string Command, string NickName, string Color) processedItem = ProcessItem(item.playerId, item.playerInfo, player, i);

                container.Add(new CuiPanel
                {
                    CursorEnabled = false,
                    Image = { Color = processedItem.Color },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"-83.82 {177.277 - (i * 42.863)}", OffsetMax = $"83.82 {207.448 - (i * 42.863)}" }
                }, "TOP_TABLE_1", "USER_INFO");

                container.Add(new CuiElement
                {
                    Name = "USER_STAT_HIDE",
                    Parent = "USER_INFO",
                    Components =
                    {
                        new CuiImageComponent { Color = "1 1 1 1", Sprite = processedItem.LockStatus },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-80.2 -6.5", OffsetMax = "-67.2 6.5" }
                    }
                });

                container.Add(new CuiElement
                {
                    Parent = "USER_INFO",
                    Components =
                    {
                        new CuiTextComponent { Text = processedItem.NickName, Font = "robotocondensed-regular.ttf", FontSize = 9, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-64.822 -6.724", OffsetMax = "7.522 6.724" }
                    }
                });

                container.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "7.522 -6.724", OffsetMax = "76.934 6.724" },
                    Text = { Text = item.value.ToString(), Font = "robotocondensed-regular.ttf", FontSize = 9, Align = TextAnchor.MiddleRight, Color = "1 1 1 1" }
                }, "USER_INFO");
                container.Add(new CuiButton
                {
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    Button = { Command = processedItem.Command, Color = "0 0 0 0" },
                    Text = { Text = "" }
                }, "USER_INFO");
                i++;
            }

            #endregion

            #region Time

            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "409.229 -20.655", OffsetMax = "576.371 -3.545" },
                Text = { Text = "STAT_UI_CATEGORY_TOP_TIME".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }
            }, "TOP_10_TABLE");

            container.Add(new CuiElement
            {
                Name = "TOP_TABLE_2",
                Parent = "TOP_10_TABLE",
                Components = {
                    new CuiRawImageComponent { Color = "1 1 1 0.3", Png =  _imageUI.GetImage("20") },
                    new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = "409.231 -441.751", OffsetMax = "576.869 -26.861" }
                }
            });
            List<(ulong playerId, PlayerInfo playerInfo, int value, float score)> time = GetPlayerInfosByCategory(CatType.PlayedTime);
                
            int c = 0;

            foreach ((ulong playerId, PlayerInfo playerInfo, int value, float score) item in time)
            {
                (string LockStatus, string Command, string NickName, string Color) processedItem = ProcessItem(item.playerId, item.playerInfo, player, c);

                container.Add(new CuiPanel
                {
                    CursorEnabled = false,
                    Image = { Color = processedItem.Color },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"-83.82 {177.277 - (c * 42.863)}", OffsetMax = $"83.82 {207.448 - (c * 42.863)}" }
                }, "TOP_TABLE_2", "USER_INFO");
                container.Add(new CuiElement
                {
                    Name = "USER_STAT_HIDE",
                    Parent = "USER_INFO",
                    Components =
                    {
                        new CuiImageComponent { Color = "1 1 1 1", Sprite = processedItem.LockStatus },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-80.2 -6.5", OffsetMax = "-67.2 6.5" }
                    }
                });
                container.Add(new CuiElement
                {
                    Parent = "USER_INFO",
                    Components =
                    {
                        new CuiTextComponent { Text = processedItem.NickName, Font = "robotocondensed-regular.ttf", FontSize = 9, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-64.822 -6.724", OffsetMax = "7.522 6.724" }
                    }
                });

                container.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "7.522 -6.724", OffsetMax = "76.934 6.724" },
                    Text =
                    {
                        Text = TimeHelper.FormatTime(TimeSpan.FromMinutes(item.value), 5, lang.GetLanguage(player.UserIDString)),
                        Font = "robotocondensed-regular.ttf", FontSize = 9, Align = TextAnchor.MiddleRight, Color = "1 1 1 1"
                    }
                }, "USER_INFO");
                container.Add(new CuiButton
                {
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    Button = { Command = processedItem.Command, Color = "0 0 0 0" },
                    Text = { Text = "" }
                }, "USER_INFO");
                c++;
            }

            #endregion

            #region Farm
            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = "-580.571 -20.655", OffsetMax = "-413.429 -3.545" },
                Text = { Text = "STAT_UI_CATEGORY_TOP_GATHER".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }
            }, "TOP_10_TABLE");

            container.Add(new CuiElement
            {
                Name = "TOP_TABLE_3",
                Parent = "TOP_10_TABLE",
                Components = {
                    new CuiRawImageComponent { Color = "1 1 1 0.3", Png =  _imageUI.GetImage("20") },
                    new CuiRectTransformComponent { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = "-580.099 -441.751", OffsetMax = "-412.461 -26.861" }
                }
            });
            List<(ulong playerId, PlayerInfo playerInfo, int value, float score)> farm = GetPlayerInfosByCategory(CatType.Gather);
                
            int f = 0;

            foreach ((ulong playerId, PlayerInfo playerInfo, int value, float score) item in farm)
            {
                (string LockStatus, string Command, string NickName, string Color) processedItem = ProcessItem(item.playerId, item.playerInfo, player, f);
                container.Add(new CuiPanel
                {
                    CursorEnabled = false,
                    Image = { Color = processedItem.Color },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"-83.82 {177.277 - (f * 42.863)}", OffsetMax = $"83.82 {207.448 - (f * 42.863)}" }
                }, "TOP_TABLE_3", "USER_INFO");

                container.Add(new CuiElement
                {
                    Name = "USER_STAT_HIDE",
                    Parent = "USER_INFO",
                    Components =
                    {
                        new CuiImageComponent { Color = "1 1 1 1", Sprite = processedItem.LockStatus },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-80.2 -6.5", OffsetMax = "-67.2 6.5" }
                    }
                });

                container.Add(new CuiElement
                {
                    Parent = "USER_INFO",
                    Components =
                    {
                        new CuiTextComponent { Text = processedItem.NickName, Font = "robotocondensed-regular.ttf", FontSize = 9, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-64.822 -6.724", OffsetMax = "7.522 6.724" }
                    }
                });

                container.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "7.522 -6.724", OffsetMax = "76.934 6.724" },
                    Text =
                    {
                        Text = item.value.ToFormattedString(), Font = "robotocondensed-regular.ttf", FontSize = 9,
                        Align = TextAnchor.MiddleRight, Color = "1 1 1 1"
                    }
                }, "USER_INFO");
                container.Add(new CuiButton
                {
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    Button = { Command = processedItem.Command, Color = "0 0 0 0" },
                    Text = { Text = "" }
                }, "USER_INFO");
                f++;
            }

            #endregion

            #region Explosion

            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = "-381.151 -20.655", OffsetMax = "-214.009 -3.545" },
                Text = { Text = "STAT_UI_CATEGORY_TOP_EXPLOSED".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }
            }, "TOP_10_TABLE");


            container.Add(new CuiElement
            {
                Name = "TOP_TABLE_5",
                Parent = "TOP_10_TABLE",
                Components = {
                    new CuiRawImageComponent { Color = "1 1 1 0.3", Png =  _imageUI.GetImage("20") },
                    new CuiRectTransformComponent { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = "-380.679 -441.751", OffsetMax = "-213.041 -26.861"  }
                }
            });
            
            List<(ulong playerId, PlayerInfo playerInfo, int value, float score)> exp = GetPlayerInfosByCategory(CatType.Explosive);
                
            int z = 0;

            foreach ((ulong playerId, PlayerInfo playerInfo, int value, float score) item in exp)
            {
                (string LockStatus, string Command, string NickName, string Color) processedItem = ProcessItem(item.playerId, item.playerInfo, player, z);
                container.Add(new CuiPanel
                {
                    CursorEnabled = false,
                    Image = { Color = processedItem.Color },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"-83.82 {177.277 - (z * 42.863)}", OffsetMax = $"83.82 {207.448 - (z * 42.863)}" }
                }, "TOP_TABLE_5", "USER_INFO");

                container.Add(new CuiElement
                {
                    Name = "USER_STAT_HIDE",
                    Parent = "USER_INFO",
                    Components = {
                        new CuiImageComponent { Color = "1 1 1 1", Sprite = processedItem.LockStatus },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-80.2 -6.5", OffsetMax = "-67.2 6.5" }
                    }
                });
                
                container.Add(new CuiElement
                {
                    Parent = "USER_INFO",
                    Components = {
                        new CuiTextComponent {Text = processedItem.NickName, Font = "robotocondensed-regular.ttf", FontSize = 9, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1"},
                        new CuiRectTransformComponent {AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-64.822 -6.724", OffsetMax = "7.522 6.724" }
                    }
                });

                container.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "7.522 -6.724", OffsetMax = "76.934 6.724" },
                    Text = { Text = item.value.ToString(), Font = "robotocondensed-regular.ttf", FontSize = 9, Align = TextAnchor.MiddleRight, Color = "1 1 1 1" }
                }, "USER_INFO");
                container.Add(new CuiButton
                {
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    Button = { Command = processedItem.Command, Color = "0 0 0 0" },
                    Text = { Text = "" }
                }, "USER_INFO");
                z++;
            }
            #endregion

            #region Score
            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = "-180.871 -20.655", OffsetMax = "-13.729 -3.545" },
                Text = { Text = "STAT_UI_CATEGORY_TOP_SCORE".GetAdaptedMessage(player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }
            }, "TOP_10_TABLE");

            container.Add(new CuiElement
            {
                Name = "TOP_TABLE_4",
                Parent = "TOP_10_TABLE",
                Components = {
                    new CuiRawImageComponent { Color = "1 1 1 0.3",  Png = _imageUI.GetImage("20") },
                    new CuiRectTransformComponent { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = "-180.419 -441.751", OffsetMax = "-12.781 -26.861"}
                }
            });
            List<(ulong playerId, PlayerInfo playerInfo, int value, float score)> score = GetPlayerInfosByCategory(CatType.Score);
                
            int s = 0;

            foreach ((ulong playerId, PlayerInfo playerInfo, int value, float score) item in score)
            {
                (string LockStatus, string Command, string NickName, string Color) processedItem = ProcessItem(item.playerId, item.playerInfo, player, s);
                container.Add(new CuiPanel
                {
                    CursorEnabled = false,
                    Image = { Color = processedItem.Color },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"-83.82 {177.277 - (s * 42.863)}", OffsetMax = $"83.82 {207.448 - (s * 42.863)}" }
                }, "TOP_TABLE_4", "USER_INFO");

                container.Add(new CuiElement
                {
                    Name = "USER_STAT_HIDE",
                    Parent = "USER_INFO",
                    Components = {
                        new CuiImageComponent { Color = "1 1 1 1", Sprite = processedItem.LockStatus },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-80.2 -6.5", OffsetMax = "-67.2 6.5" }
                    }
                });

                container.Add(new CuiElement
                {
                    Parent = "USER_INFO",
                    Components = {
                        new CuiTextComponent {Text = processedItem.NickName, Font = "robotocondensed-regular.ttf", FontSize = 9, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1"},
                        new CuiRectTransformComponent {AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-64.822 -6.724", OffsetMax = "7.522 6.724" }
                    }
                });

                container.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "7.522 -6.724", OffsetMax = "76.934 6.724" },
                    Text = { Text = item.score.ToString("F2"), Font = "robotocondensed-regular.ttf", FontSize = 9, Align = TextAnchor.MiddleRight, Color = "1 1 1 1" }
                }, "USER_INFO");
                container.Add(new CuiButton
                {
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    Button = { Command = processedItem.Command, Color = "0 0 0 0" },
                    Text = { Text = "" }
                }, "USER_INFO");
                s++;
            }
            #endregion

            CloseLayer(player);
            CuiHelper.AddUi(player, container);
            return;

            (string LockStatus, string Command, string NickName, string Color) ProcessItem(ulong playerId, PlayerInfo playerInfo, BasePlayer player, int rank)
            {
                string lockStatus = playerInfo.HidedStatistics ? "assets/icons/lock.png" : "assets/icons/unlock.png";
                string command = playerInfo.HidedStatistics ? "" : $"UI_HandlerStat GoStatPlayers {playerId}";
                if (permission.UserHasPermission(player.UserIDString, PermAdmin))
                    command = $"UI_HandlerStat GoStatPlayers {playerId}";
                string nickName = $"<color=white>{GetCorrectName(playerInfo.Name, 17)}</color>";
                string color = rank switch
                {
                    0 => _config.settingsInterface.ColorTop1,
                    1 => _config.settingsInterface.ColorTop2,
                    2 => _config.settingsInterface.ColorTop3,
                    _ => "0 0 0 0"
                };
    
                return (lockStatus, command, nickName, color);
            }
            
            
            List<(ulong playerId, PlayerInfo playerInfo, int value, float scoreValue)> GetPlayerInfosByCategory(CatType category)
            {
                List<(ulong, PlayerInfo, int, float)> result = new();

                if (cashedTopUser.TryGetValue(category, out Dictionary<ulong, CashedData> playersInCategory))
                {
                    foreach ((ulong playerId, CashedData data) in playersInCategory)
                    {
                        if (result.Count >= 10) break;
                        PlayerInfo playerInfo = PlayerInfo.Get(playerId);
                        if (playerInfo != null)
                            result.Add((playerId, playerInfo, data.value, data.valueScore ));
                    }
                }

                return result;
            }
        }
        #endregion

        #endregion

        #region OtherMetods
        
        public static bool IsObjectNull(object obj) => ReferenceEquals(obj, null);
        
        private void SendMsgRewardWipe(BasePlayer player)
        {
            if (!_prizePlayerData.TryGetValue(player.userID, out List<PrizePlayer> playerGrant))
                return;

            const string langKey = "STAT_TOP_PLAYER_WIPE_";

            foreach (PrizePlayer item in playerGrant)
            {
                string categoryKey = item.catType.ToString();
                if (_config.settingsPrize.PrizeByCategory.TryGetValue(categoryKey, out List<Configuration.SettingsPrize.Reward> rewards))
                {
                    Configuration.SettingsPrize.Reward reward = rewards.FirstOrDefault(r => r.top == item.top);
                    if (reward != null)
                    {
                        if(!item.isRewarded)
                            reward.RewardPlayer(player.UserIDString);
                        
                        string message = $"{langKey + item.catType}".GetAdaptedMessage(player.UserIDString, item.top) + "\n" + reward.entryRewardMessage;
                        player.ChatMessage(message);
                    }
                }
            }

            NextTick(() => 
            { 
                _prizePlayerData.Remove(player.userID); 
                SaveDataPrize(); 
            });
        }

        private void ParseTopUserForPrize()
        {
            _prizePlayerData.Clear();
    
            Dictionary<CatType, Dictionary<ulong, CashedData>> TopPlayers = GetTopPlayersByAllCategories(_config.settingsPrize.rewardsCategoryList);

            foreach (KeyValuePair<CatType, Dictionary<ulong, CashedData>> topPlayer in TopPlayers)
            {
                if (!_config.settingsPrize.PrizeByCategory.TryGetValue(topPlayer.Key.ToString(), out List<Configuration.SettingsPrize.Reward> rewards) || !rewards.Any())
                    continue;

                foreach ((KeyValuePair<ulong, CashedData> player, int index) in topPlayer.Value.Select((value, index) => (value, index)))
                {
                    int top = index + 1;
                    Configuration.SettingsPrize.Reward reward = rewards.FirstOrDefault(x => x.top == top);

                    if (!_prizePlayerData.ContainsKey(player.Key))
                    {
                        _prizePlayerData[player.Key] = new List<PrizePlayer>();
                    }

                    _prizePlayerData[player.Key].Add(new PrizePlayer
                    {
                        catType = topPlayer.Key,
                        value = player.Value.FullValue(),
                        top = top,
                        isRewarded = !reward?.rewardOnConnected ?? false,
                    });
                    if (reward is { rewardOnConnected: false })
                        reward.RewardPlayer(player.Key.ToString());
                    
                    if (top >= rewards.Count)
                        break;
                }
            }

            SaveDataPrize();
           if (_config.settings.wipeData) 
               ClearData();
        }



        private (string, string) GetRandomTopPlayer(List<CatType> categoryList)
        {
            CatType randomCatType = categoryList.GetRandom();
            string playerstat = string.Empty, langKey = "PRINT_TOP_CATEGORY_" + randomCatType;
            
            Dictionary<ulong, CashedData> topUsersByCategory = GetTopUserByCatType(randomCatType) ?? new Dictionary<ulong, CashedData>();

            int top = 1;
            foreach (KeyValuePair<ulong, CashedData> topUser in topUsersByCategory)
            {
                if (top > 5) break;
                
                playerstat += $"<color=#faec84>{top}</color>. {topUser.Value.playerName} : {GetFormat(randomCatType, topUser.Value)}\n";
                top++;
            }
            
            return (langKey, playerstat);
            
            Dictionary<ulong, CashedData> GetTopUserByCatType(CatType catType) => cashedTopUser.GetValueOrDefault(catType);

            string GetFormat(CatType catType, CashedData data)
            {
                return catType switch
                {
                    CatType.Score => $"{data.valueScore:F2}",
                    CatType.PlayedTime => $"{TimeHelper.FormatTime(TimeSpan.FromMinutes(data.value), 5, lang.GetServerLanguage())}",
                    CatType.Gather => $"{data.value.ToFormattedString()}",
                    _ => $"{data.value}"
                };
            }
        }

        private void ChatPrintTopFive()
        {
            (string, string) data = GetRandomTopPlayer(_config.chatMessage._chatCategories);

            if (!string.IsNullOrWhiteSpace(data.Item1) && !string.IsNullOrWhiteSpace(data.Item2))
                foreach (BasePlayer item in BasePlayer.activePlayerList)
                    item.ChatMessage(data.Item1.GetAdaptedMessage(item.UserIDString, data.Item2));
            
            timer.Once(_config.chatMessage.chatSendTopTime, ChatPrintTopFive);
        }
        private static readonly Regex CleanUpRegex = new Regex("<.*?>|{.*?}", RegexOptions.Compiled);

        private string CleanUpString(string input)
        {
            return CleanUpRegex.Replace(input, string.Empty);
        }

        private void DiscordPrintTopFive()
        {
            (string, string) data = GetRandomTopPlayer(_config.discordMessage._discordCategories);

            if (!string.IsNullOrWhiteSpace(data.Item1) && !string.IsNullOrWhiteSpace(data.Item2))
                SendDiscordMessage(CleanUpString(lang.GetMessage(data.Item1, this)), new List<string> { data.Item2 }, false);

            timer.Once(_config.discordMessage.discordSendTopTime, DiscordPrintTopFive);
        }
        #endregion

        #region ApiLoadData

        private class ItemTranslate
        {
            public string shortName { get; set; }
            public string ENdisplayName { get; set; }
            public string RUdisplayName { get; set; }
        }

        private class ItemDisplayName
        {
            public string ENdisplayName { get; set; }
            public string RUdisplayName { get; set; }
        }

        private void FetchAndParseItems()
        {
            const string url = "https://api.skyplugins.ru/api/getitemlist";

            webrequest.Enqueue(url, null, (code, response) =>
            {
                if (code == 200)
                {
                    List<ItemTranslate> items = JsonConvert.DeserializeObject<List<ItemTranslate>>(response);

                    foreach (ItemTranslate item in items)
                    {
                        _itemName[item.shortName] = new ItemDisplayName
                        {
                            ENdisplayName = item.ENdisplayName,
                            RUdisplayName = item.RUdisplayName
                        };
                    }
                }
                else
                {
                    Debug.Log($"Error fetching data: {response}");
                }
            }, this);
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
                    { "16", new ImageData() },
                    { "17", new ImageData() },
                    { "18", new ImageData() },
                    { "19", new ImageData() },
                    { "20", new ImageData() },
                };
            }

            private class ImageData
            {
                public ImageStatus Status = ImageStatus.NotLoaded;
                public string Id { get; set; }
            }

            public string GetImage(string name)
            {
                if (_images.TryGetValue(name, out ImageData image) && image.Status == ImageStatus.Loaded)
                    return image.Id;
                return null;
            }

            public void DownloadImage()
            {
                KeyValuePair<string, ImageData>? imageToDownload = null;
                foreach (KeyValuePair<string, ImageData> img in _images)
                {
                    if (img.Value.Status != ImageStatus.NotLoaded) continue;
                    imageToDownload = img;
                    break;
                }

                if (imageToDownload.HasValue)
                {
                    ServerMgr.Instance.StartCoroutine(ProcessDownloadImage(imageToDownload.Value));
                    return;
                }

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
                    Instance.PrintError(RU 
                        ? $"Не удалось загрузить следующие изображения: {images}. Возможно, вы не загрузили их в папку '{_printPath}'."
                        : $"Failed to load the following images: {images}. Perhaps you did not upload them to the '{_printPath}' folder.");
                    Interface.Oxide.UnloadPlugin(Instance.Name);
                    return;
                }

                Instance.Puts(RU 
                    ? $"{_images.Count} изображений успешно загружено!"
                    : $"{_images.Count} images downloaded successfully!");
            }
            
            public void UnloadImages()
            {
                foreach (KeyValuePair<string, ImageData> item in _images)
                {
                    if (item.Value.Status == ImageStatus.Loaded && item.Value?.Id != null)
                    {
                        FileStorage.server.Remove(uint.Parse(item.Value.Id), FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID);
                    }
                }

                _images?.Clear();
            }

            private IEnumerator ProcessDownloadImage(KeyValuePair<string, ImageData> image)
            {
                string url = $"file://{Interface.Oxide.DataDirectory}/{_paths}{image.Key}.png";

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

        #region Help
        
        private void RegisterPermissionIfNotExists(string perm)
        {
            if (!string.IsNullOrEmpty(perm) && !permission.PermissionExists(perm, this))
            {
                permission.RegisterPermission(perm, this);
            }
        }
        
        private Dictionary<string, int> GetCategory(PlayerInfo statInfo, int cat)
        {
            Dictionary<string, int> result = new();

            switch (cat)
            {
                case 0:
                    List<KeyValuePair<string, int>> resourceGathering = statInfo.ResourceGatheringStats.ToList();
                    resourceGathering.Sort((a, b) => b.Value.CompareTo(a.Value));
                    AddItems(resourceGathering, 8);
                    result.Add("all", statInfo.TotalResourcesGathered);
                    break;
                case 1:
                    AddItems(statInfo.ExplosiveUsageStats.ToList(), 8);
                    result.Add("all", statInfo.TotalExplosivesUsed);
                    break;
                case 2:
                    List<KeyValuePair<string, int>> orderedHarvesting = statInfo.CropGrowthStats.ToList();
                    orderedHarvesting.Sort((a, b) => b.Value.CompareTo(a.Value));
                    AddItems(orderedHarvesting, 9);
                    result.Add("all", statInfo.TotalCropsGrown);
                    break;
                default:
                    return null;
            }

            return result;

            void AddItems(IReadOnlyList<KeyValuePair<string, int>> source, int count)
            {
                for (int i = 0; i < Math.Min(count, source.Count); i++)
                {
                    if (_config.settingsScore.blackListed.Contains(source[i].Key))
                        continue;
                    
                    result.Add(source[i].Key, source[i].Value);
                }
            }
        }

        private void CheckInMinute()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.userID.IsSteamId())
                    continue;
                PlayerInfo playerStatInitiator = PlayerInfo.GetOrCreate(player.userID);
                playerStatInitiator.AddPlayedTime();
            }

            timer.Once(60f, CheckInMinute);
        }
        private static class TimeHelper
        {
            public static string FormatTime(TimeSpan time, int maxSubstr = 5, string language = "ru")
            {
                return language == "ru" ? FormatTimeRussian(time, maxSubstr) : FormatTimeDefault(time);
            }

            private static string FormatTimeRussian(TimeSpan time, int maxSubstr)
            {
                List<string> substrings = new List<string>();

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

        private int GetTopScore(ulong userid)
        {
            if (_config.settings.ignoreList.Contains(userid))
                return -1;

            float userScore = PlayerInfo._players[userid].Score;

            return 1 + PlayerInfo._players.Count(player => !_config.settings.ignoreList.Contains(player.Key) && player.Value.Score > userScore);
        }
        private string GetCorrectName(string name, int length = 2147483647) => name.ToPrintable(length).EscapeRichText().Trim();

        #endregion

        #region ConsoleCommand

        [ConsoleCommand("stat.category")]
        void StatCategory(ConsoleSystem.Arg arg)
        {
            if (arg is null) return;

            BasePlayer player = arg.Player();
            if (player != null && !player.IsAdmin)
            {
                PrintToConsole(player, "STAT_CMD_1".GetAdaptedMessage(player.UserIDString));
                return;
            }
            
            if (!arg.HasArgs(1))
            {
                SendConsoleMessage(player, "STAT_COMMAND_SYNTAX_ERROR".GetAdaptedMessage(player?.UserIDString, "stat.category list"));
                return;
            }
            
            switch (arg.Args[0])
            {
                case "l":
                case "list":
                    SendConsoleMessage(player, "STAT_CMD_12".GetAdaptedMessage(player?.UserIDString, string.Join(", ", Enum.GetNames(typeof(CatType)))));
                    break;
                default:
                    SendConsoleMessage(player, "STAT_COMMAND_SYNTAX_ERROR".GetAdaptedMessage(player?.UserIDString, "stat.category list"));
                    break;
            }
        }

        #endregion

        #region Command
        private void ConsoleCommandOpenMenu(ConsoleSystem.Arg arg)
        {
            if (arg.Player() == null)
                return;
            if (ignoreReservedPlayer.Contains(arg.Player().userID))
            {
                PrintToChat(arg.Player(), "STAT_ADMIN_HIDE_STAT".GetAdaptedMessage( arg.Player().UserIDString));
                return;
            }
            MainMenuStat(arg.Player());
        }
        
        private void SendConsoleMessage(BasePlayer player, string message)
        {
            if(player != null)
                player.ConsoleMessage(message);
            else
                PrintWarning(message);
        }

        [ConsoleCommand("stat.score")]
        void StatScoreGive(ConsoleSystem.Arg arg)
        {
            if (arg is null) return;

            BasePlayer player = arg.Player();
            if (player != null && !player.IsAdmin)
            {
                PrintToConsole(player, "STAT_CMD_1".GetAdaptedMessage(player.UserIDString));
                return;
            }
            
            if (!arg.HasArgs(3))
            {
                SendConsoleMessage(player, "STAT_COMMAND_SYNTAX_ERROR".GetAdaptedMessage(player?.UserIDString, "stat.score [give/remove] STEAMID SCORE"));
                return;
            }
            
            if(!ulong.TryParse(arg.GetString(1), out ulong playerid))
            {
                SendConsoleMessage(player, "STAT_INVALID_PLAYER_ID_INPUT".GetAdaptedMessage(player?.UserIDString));
                return;
            }
            
            if (!playerid.IsSteamId())
            {
                SendConsoleMessage(player, "STAT_NOT_A_STEAM_ID".GetAdaptedMessage(player?.UserIDString));
                return;
            }

            if (!int.TryParse(arg.Args[2], out int score))
            {
                Puts("Invalid score format.");
                return;
            }

            PlayerInfo playerInfo = PlayerInfo.Get(playerid);
            if (playerInfo is null)
            {
                SendConsoleMessage(player, "STAT_PLAYER_NOT_FOUND_BY_STEAMID".GetAdaptedMessage(player?.UserIDString));
                return;
            }
            
            switch (arg.Args[0])
            {
                case "give":
                {
                    playerInfo.Score += score;
                    Puts("STAT_CMD_10".GetAdaptedMessage(null, playerid, score));
                    break;
                }
                case "remove":
                {
                    playerInfo.Score -= score;
                    Puts("STAT_CMD_11".GetAdaptedMessage(null, playerid, score));
                    break;
                }
            }
        }

        [ConsoleCommand("stat.wipe")]
        void StatWipeStat(ConsoleSystem.Arg arg)
        {
            if (arg is null) return;

            BasePlayer player = arg.Player();
            if (player != null && !player.IsAdmin)
            {
                PrintToConsole(player, "STAT_CMD_1".GetAdaptedMessage(player.UserIDString));
                return;
            }

            if (_config.settingsPrize.prizeUse)
                ParseTopUserForPrize();
            else if (_config.settings.wipeData)
                ClearData();
        }

        [ConsoleCommand("stat.ignore")]
        private void CmdIgnorePlayer(ConsoleSystem.Arg arg)
        {
            
            if (arg is null) return;

            BasePlayer player = arg.Player();
            if (player != null && !player.IsAdmin)
            {
                PrintToConsole(player, "STAT_CMD_1".GetAdaptedMessage(player.UserIDString));
                return;
            }

            
            if (!arg.HasArgs(2))
            {
                SendConsoleMessage(player, "STAT_COMMAND_SYNTAX_ERROR".GetAdaptedMessage(player?.UserIDString, "stat.ignore <add/remove> <Steam ID>"));
                return;
            }
            
            if(!ulong.TryParse(arg.GetString(1), out ulong playerid))
            {
                SendConsoleMessage(player, "STAT_INVALID_PLAYER_ID_INPUT".GetAdaptedMessage(player?.UserIDString));
                return;
            }
            
            if (!playerid.IsSteamId())
            {
                SendConsoleMessage(player, "STAT_NOT_A_STEAM_ID".GetAdaptedMessage(player?.UserIDString));
                return;
            }
            

            if (!PlayerInfo._players.ContainsKey(playerid))
            {
                SendConsoleMessage(player, "STAT_CMD_5".GetAdaptedMessage(player?.UserIDString));
                return;
            }

            PlayerInfo playerInfo = PlayerInfo.Get(playerid);
            string playerName = playerid + " : " +  playerInfo.GetPlayerName(playerid);
            switch (arg.Args[0])
            {
                case "add":
                case "a":
                    {
                        if (ignoreReservedPlayer.Contains(playerid))
                        {
                            SendConsoleMessage(player, "STAT_CMD_6".GetAdaptedMessage(player?.UserIDString, playerName));
                            break;
                        }
                        ignoreReservedPlayer.Add(playerid);
                        playerInfo.IsIgnore = true;
                        
                        SendConsoleMessage(player, "STAT_CMD_7".GetAdaptedMessage(player?.UserIDString, playerName));
                        break;
                    }
                case "r":
                case "remove":
                    {
                        if (!ignoreReservedPlayer.Contains(playerid))
                        {
                            SendConsoleMessage(player, "STAT_CMD_8".GetAdaptedMessage(player?.UserIDString, playerName));
                            break;
                        }
                        ignoreReservedPlayer.Remove(playerid);
                        playerInfo.IsIgnore = false;
                        
                        SendConsoleMessage(player, "STAT_CMD_9".GetAdaptedMessage(player?.UserIDString, playerName));
                        break;
                    }
            }
        }

        [ConsoleCommand("UI_HandlerStat")]
        private void CmdConsoleHandler(ConsoleSystem.Arg args)
        {
            BasePlayer player = args.Player();
            PlayerInfo playerInfo = PlayerInfo.GetOrCreate(player.userID);
            if (player != null && args.HasArgs(1))
            {
                switch (args.Args[0])
                {
                    case "hidestat":
                        {
                            if (_config.settings.availabilityUse && !permission.UserHasPermission(player.UserIDString, PermAvailability))
                                return;
                            playerInfo.HidedStatistics = !playerInfo.HidedStatistics;
                            ButtonHideStat(player, playerInfo);
                            break;
                        }
                    case "confirm":
                        {
                            DialogConfirmationDropStat(player);
                            break;
                        }
                    case "confirm_yes":
                        {
                            if (_config.settings.dropStatUse && !permission.UserHasPermission(player.UserIDString, PermReset))
                                return;
                            PlayerInfo.PlayerClearData(player.userID);
                            UserStat(player);
                            break;
                        }
                    case "changeCategory":
                        {
                            ulong target = ulong.Parse(args.Args[1]);
                            int cat = int.Parse(args.Args[2]);
                            CategoryStatUser(player, target, cat);
                            break;
                        }
                    case "ShowMoreStat":
                        {
                            ulong target = ulong.Parse(args.Args[1]);
                            int cat = int.Parse(args.Args[2]);
                            OtherStatUser(player, target, cat == 0 ? 1 : 0);
                            break;
                        }
                    case "listplayer":
                        {
                            if (args.Args.Length > 1)
                            {
                                string seaecher = args.Args[1].ToLower();
                                SearchPageUser(player, seaecher);
                            }
                            else
                                SearchPageUser(player);
                            break;
                        }
                    case "GoStatPlayers":
                        {
                            ulong id = ulong.Parse(args.Args[1]);
                            UserStat(player, id);
                            break;
                        }
                    case "Page_swap":
                        {
                            int cat = int.Parse(args.Args[1]);
                            MenuButton(player, cat);
                            break;
                        }
                }
            }
        }
        #endregion

        private void SendDiscordMessage(string title, List<string> embeds, bool inline = false)
        {
            Embed embed = new Embed();
            foreach (string item in embeds)
            {
                embed.AddField(title, item, inline, _config.discordMessage.colorLines.GetRandom());
            }
            webrequest.Enqueue(_config.discordMessage.weebHook, new DiscordMessage(_config.discordMessage.message, embed).ToJson(), (code, response) => { },
                this,
                RequestMethod.POST, new Dictionary<string, string>
                {
                    { "Content-Type", "application/json" }
                });
        }

        #region Discord Stuff

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

        #endregion

        #region Api

        private JObject API_GetAllPlayerStat(ulong id) => JObject.FromObject(PlayerInfo.Get(id));
        private JObject API_GetPlayerPlayedTime(ulong id) => JObject.FromObject(PlayerInfo.Get(id)?.playedTime);
        private Dictionary<string, int> API_GetGathered(ulong id) => PlayerInfo.Get(id)?.ResourceGatheringStats;
        private int? API_GetAllGathered(ulong id) => PlayerInfo.Get(id).TotalResourcesGathered;
        private int? API_GetGathered(ulong id, string shortname)
        {
            Dictionary<string, int> data = API_GetGathered(id);
            int amount;
            if (data?.TryGetValue(shortname, out amount) == true)
                return amount;
            return null;
        }
        #endregion
    }
}
      
namespace Oxide.Plugins.XDStatisticsExtensionMethods
{
    public static class ExtensionMethods
    {
        private static readonly Lang Lang = Interface.Oxide.GetLibrary<Lang>();
		
        #region GetLang
		
        public static string GetAdaptedMessage(this string langKey, in string userID, params object[] args)
        {
            string message = Lang.GetMessage(langKey, XDStatistics.Instance, userID);
			
            StringBuilder stringBuilder = Pool.Get<StringBuilder>();

            try
            {
                string str = stringBuilder.AppendFormat(message, args).ToString();
                return str;
            }
            finally
            {
                stringBuilder.Clear();
                Pool.FreeUnmanaged(ref stringBuilder);
            }
        }
        public static string GetAdaptedMessage(this string langKey, in string userID = null)
        {
            return Lang.GetMessage(langKey, XDStatistics.Instance, userID);
        }
		
        #endregion
        
        public static string ToFormattedString(this int count)
        {
            return count switch
            {
                >= 1_000_000 => (count / 1_000_000.0).ToString("F1") + "M",
                >= 1_000 => (count / 1_000.0).ToString("F1") + "K",
                _ => count.ToString("N0")
            };
        }
        
        public static bool IsPlayer(this BasePlayer player) => player.userID.IsSteamId();
    }
}