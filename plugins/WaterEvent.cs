using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Reflection;
using Facepunch;
using UnityEngine.Networking;
using Oxide.Plugins.WaterEventExtensionMethods;

namespace Oxide.Plugins
{
    [Info("WaterEvent", "KpucTaJl", "2.2.1")]
    internal class WaterEvent : RustPlugin
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
            if (_config.PluginVersion < new VersionNumber(2, 1, 4))
            {
                _config.DistanceAlerts = 0f;
                _config.PveMode.ScaleDamage.Add("Turret", 2f);
            }
            _config.PluginVersion = Version;
            Puts("Config update completed!");
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        public class ItemConfig
        {
            [JsonProperty("ShortName")] public string ShortName { get; set; }
            [JsonProperty(En ? "Minimum" : "Минимальное кол-во")] public int MinAmount { get; set; }
            [JsonProperty(En ? "Maximum" : "Максимальное кол-во")] public int MaxAmount { get; set; }
            [JsonProperty(En ? "Chance [0.0-100.0]" : "Шанс выпадения предмета [0.0-100.0]")] public float Chance { get; set; }
            [JsonProperty(En ? "Is this a blueprint? [true/false]" : "Это чертеж? [true/false]")] public bool IsBluePrint { get; set; }
            [JsonProperty("SkinID (0 - default)")] public ulong SkinId { get; set; }
            [JsonProperty(En ? "Name (empty - default)" : "Название (empty - default)")] public string Name { get; set; }
        }

        public class LootTableConfig
        {
            [JsonProperty(En ? "Minimum numbers of items" : "Минимальное кол-во элементов")] public int Min { get; set; }
            [JsonProperty(En ? "Maximum numbers of items" : "Максимальное кол-во элементов")] public int Max { get; set; }
            [JsonProperty(En ? "Use minimum and maximum values? [true/false]" : "Использовать минимальное и максимальное значение? [true/false]")] public bool UseCount { get; set; }
            [JsonProperty(En ? "List of items" : "Список предметов")] public List<ItemConfig> Items { get; set; }
        }

        public class PrefabConfig
        {
            [JsonProperty(En ? "Chance [0.0-100.0]" : "Шанс выпадения [0.0-100.0]")] public float Chance { get; set; }
            [JsonProperty(En ? "The path to the prefab" : "Путь к prefab-у")] public string PrefabDefinition { get; set; }
        }

        public class PrefabLootTableConfig
        {
            [JsonProperty(En ? "Minimum numbers of prefabs" : "Минимальное кол-во prefab-ов")] public int Min { get; set; }
            [JsonProperty(En ? "Maximum numbers of prefabs" : "Максимальное кол-во prefab-ов")] public int Max { get; set; }
            [JsonProperty(En ? "Use minimum and maximum values? [true/false]" : "Использовать минимальное и максимальное значение? [true/false]")] public bool UseCount { get; set; }
            [JsonProperty(En ? "List of prefabs" : "Список prefab-ов")] public List<PrefabConfig> Prefabs { get; set; }
        }

        public class EntityCrateConfig
        {
            [JsonProperty("Prefab")] public string Prefab { get; set; }
            [JsonProperty(En ? "Is it necessary for loot to appear in the container? [true/false]" : "Могут ли появляться предметы в этом ящике? [true/false]")] public bool IsOwnLootTable { get; set; }
            [JsonProperty(En ? "Own loot table" : "Собственная таблица лута")] public LootTableConfig LootTable { get; set; }
        }

        public class CrateConfig
        {
            [JsonProperty("Prefab")] public string Prefab { get; set; }
            [JsonProperty(En ? "Which loot table should the plugin use? (0 - default; 1 - own; 2 - AlphaLoot; 3 - CustomLoot; 4 - loot table of the Rust objects; 5 - combine the 1 and 4 methods)" : "Какую таблицу лута необходимо использовать? (0 - стандартную; 1 - собственную; 2 - AlphaLoot; 3 - CustomLoot; 4 - таблица предметов объектов Rust; 5 - совместить 1 и 4 методы)")] public int TypeLootTable { get; set; }
            [JsonProperty(En ? "Loot table from prefabs (if the loot table type is 4 or 5)" : "Таблица предметов из prefab-ов (если тип таблицы предметов - 4 или 5)")] public PrefabLootTableConfig PrefabLootTable { get; set; }
            [JsonProperty(En ? "Own loot table (if the loot table type is 1 or 5)" : "Собственная таблица предметов (если тип таблицы предметов - 1 или 5)")] public LootTableConfig OwnLootTable { get; set; }
            [JsonProperty(En ? "Item Multipliers" : "Множители предметов")] public Dictionary<string, float> ScaleItems { get; set; }
        }

        public class HackCrateConfig
        {
            [JsonProperty(En ? "Location of all Crates" : "Расположение всех ящиков")] public HashSet<LocationConfig> Coordinates { get; set; }
            [JsonProperty(En ? "Time to unlock the Crates [sec.]" : "Время разблокировки ящиков [sec.]")] public float UnlockTime { get; set; }
            [JsonProperty(En ? "Increase the event time if it's not enough to unlock the locked crate? [true/false]" : "Увеличивать время ивента, если недостаточно чтобы разблокировать заблокированный ящик? [true/false]")] public bool IncreaseEventTime { get; set; }
            [JsonProperty(En ? "Which loot table should the plugin use? (0 - default; 1 - own; 2 - AlphaLoot; 3 - CustomLoot; 4 - loot table of the Rust objects; 5 - combine the 1 and 4 methods)" : "Какую таблицу лута необходимо использовать? (0 - стандартную; 1 - собственную; 2 - AlphaLoot; 3 - CustomLoot; 4 - таблица предметов объектов Rust; 5 - совместить 1 и 4 методы)")] public int TypeLootTable { get; set; }
            [JsonProperty(En ? "Loot table from prefabs (if the loot table type is 4 or 5)" : "Таблица предметов из prefab-ов (если тип таблицы предметов - 4 или 5)")] public PrefabLootTableConfig PrefabLootTable { get; set; }
            [JsonProperty(En ? "Own loot table (if the loot table type is 1 or 5)" : "Собственная таблица предметов (если тип таблицы предметов - 1 или 5)")] public LootTableConfig OwnLootTable { get; set; }
            [JsonProperty(En ? "Item Multipliers" : "Множители предметов")] public Dictionary<string, float> ScaleItems { get; set; }
        }

        public class LocationConfig
        {
            [JsonProperty(En ? "Position" : "Позиция")] public string Position { get; set; }
            [JsonProperty(En ? "Rotation" : "Вращение")] public string Rotation { get; set; }
        }

        public class ColorConfig
        {
            [JsonProperty("r")] public float R { get; set; }
            [JsonProperty("g")] public float G { get; set; }
            [JsonProperty("b")] public float B { get; set; }
        }

        public class MarkerConfig
        {
            [JsonProperty(En ? "Use map marker? [true/false]" : "Использовать маркер на карте? [true/false]")] public bool Enabled { get; set; }
            [JsonProperty(En ? "Type (0 - simple, 1 - advanced)" : "Тип (0 - упрощенный, 1 - расширенный)")] public int Type { get; set; }
            [JsonProperty(En ? "Background radius (if the marker type is 0)" : "Радиус фона (если тип маркера - 0)")] public float Radius { get; set; }
            [JsonProperty(En ? "Background transparency" : "Прозрачность фона")] public float Alpha { get; set; }
            [JsonProperty(En ? "Color" : "Цвет")] public ColorConfig Color { get; set; }
            [JsonProperty(En ? "Text" : "Текст")] public string Text { get; set; }
        }

        public class PointConfig
        {
            [JsonProperty(En ? "Enabled? [true/false]" : "Включен? [true/false]")] public bool Enabled { get; set; }
            [JsonProperty(En ? "Text" : "Текст")] public string Text { get; set; }
            [JsonProperty(En ? "Size" : "Размер")] public int Size { get; set; }
            [JsonProperty(En ? "Color" : "Цвет")] public string Color { get; set; }
        }

        public class GuiConfig
        {
            [JsonProperty(En ? "Do you use the countdown GUI? [true/false]" : "Использовать ли GUI обратного отсчета? [true/false]")] public bool IsGui { get; set; }
            [JsonProperty("OffsetMin Y")] public string OffsetMinY { get; set; }
        }

        public class ChatConfig
        {
            [JsonProperty(En ? "Do you use the chat? [true/false]" : "Использовать ли чат? [true/false]")] public bool IsChat { get; set; }
            [JsonProperty(En ? "Prefix of chat messages" : "Префикс сообщений в чате")] public string Prefix { get; set; }
        }

        public class GameTipConfig
        {
            [JsonProperty(En ? "Use Facepunch Game Tips (notification bar above hotbar)? [true/false]" : "Использовать ли Facepunch Game Tip (оповещения над слотами быстрого доступа игрока)? [true/false]")] public bool IsGameTip { get; set; }
            [JsonProperty(En ? "Style (0 - Blue Normal, 1 - Red Normal, 2 - Blue Long, 3 - Blue Short, 4 - Server Event)" : "Стиль (0 - Blue Normal, 1 - Red Normal, 2 - Blue Long, 3 - Blue Short, 4 - Server Event)")] public int Style { get; set; }
        }

        public class GuiAnnouncementsConfig
        {
            [JsonProperty(En ? "Do you use the GUI Announcements? [true/false]" : "Использовать ли GUI Announcements? [true/false]")] public bool IsGuiAnnouncements { get; set; }
            [JsonProperty(En ? "Banner color" : "Цвет баннера")] public string BannerColor { get; set; }
            [JsonProperty(En ? "Text color" : "Цвет текста")] public string TextColor { get; set; }
            [JsonProperty(En ? "Adjust Vertical Position" : "Отступ от верхнего края")] public float ApiAdjustVPosition { get; set; }
        }

        public class NotifyConfig
        {
            [JsonProperty(En ? "Do you use the Notify? [true/false]" : "Использовать ли Notify? [true/false]")] public bool IsNotify { get; set; }
            [JsonProperty(En ? "Type" : "Тип")] public int Type { get; set; }
        }

        public class DiscordConfig
        {
            [JsonProperty(En ? "Do you use the Discord? [true/false]" : "Использовать ли Discord? [true/false]")] public bool IsDiscord { get; set; }
            [JsonProperty("Webhook URL")] public string WebhookUrl { get; set; }
            [JsonProperty(En ? "Embed Color (DECIMAL)" : "Цвет полосы (DECIMAL)")] public int EmbedColor { get; set; }
            [JsonProperty(En ? "Keys of required messages" : "Ключи необходимых сообщений")] public HashSet<string> Keys { get; set; }
        }

        public class EconomyConfig
        {
            [JsonProperty(En ? "Which economy plugins do you want to use? (Economics, Server Rewards, IQEconomic)" : "Какие плагины экономики вы хотите использовать? (Economics, Server Rewards, IQEconomic)")] public HashSet<string> Plugins { get; set; }
            [JsonProperty(En ? "The minimum value that a player must collect to get points for the economy" : "Минимальное значение, которое игрок должен заработать, чтобы получить баллы за экономику")] public double Min { get; set; }
            [JsonProperty(En ? "Looting of crates" : "Ограбление ящиков")] public Dictionary<string, double> Crates { get; set; }
            [JsonProperty(En ? "Destruction of doors" : "Уничтожение дверей")] public Dictionary<string, double> Doors { get; set; }
            [JsonProperty(En ? "Killing an NPC" : "Убийство NPC")] public double Npc { get; set; }
            [JsonProperty(En ? "Hacking a locked crate" : "Взлом заблокированного ящика")] public double LockedCrate { get; set; }
            [JsonProperty(En ? "Opening a security door with a card" : "Открытие двери карточкой")] public Dictionary<string, double> Cards { get; set; }
            [JsonProperty(En ? "List of commands that are executed in the console at the end of the event ({steamid} - the player who collected the highest number of points)" : "Список команд, которые выполняются в консоли по окончанию ивента ({steamid} - игрок, который набрал наибольшее кол-во баллов)")] public HashSet<string> Commands { get; set; }
        }

        public class PveModeConfig
        {
            [JsonProperty(En ? "Use the PVE mode of the plugin? [true/false]" : "Использовать PVE режим работы плагина? [true/false]")] public bool Pve { get; set; }
            [JsonProperty(En ? "The amount of damage that the player has to do to become the Event Owner" : "Кол-во урона, которое должен нанести игрок, чтобы стать владельцем ивента")] public float Damage { get; set; }
            [JsonProperty(En ? "Damage Multipliers for calculate to become the Event Owner" : "Коэффициенты урона для подсчета, чтобы стать владельцем ивента")] public Dictionary<string, float> ScaleDamage { get; set; }
            [JsonProperty(En ? "Can the non-owner of the event loot the crates? [true/false]" : "Может ли не владелец ивента грабить ящики? [true/false]")] public bool LootCrate { get; set; }
            [JsonProperty(En ? "Can the non-owner of the event hack locked crates? [true/false]" : "Может ли не владелец ивента взламывать заблокированные ящики? [true/false]")] public bool HackCrate { get; set; }
            [JsonProperty(En ? "Can the non-owner of the event loot NPC corpses? [true/false]" : "Может ли не владелец ивента грабить трупы NPC? [true/false]")] public bool LootNpc { get; set; }
            [JsonProperty(En ? "Can the non-owner of the event deal damage to the NPC? [true/false]" : "Может ли не владелец ивента наносить урон по NPC? [true/false]")] public bool DamageNpc { get; set; }
            [JsonProperty(En ? "Can the non-owner of the event deal damage to the AutoTurret? [true/false]" : "Может ли не владелец ивента наносить урон по турелям? [true/false]")] public bool DamageTurret { get; set; }
            [JsonProperty(En ? "Can an Npc attack a non-owner of the event? [true/false]" : "Может ли Npc атаковать не владельца ивента? [true/false]")] public bool TargetNpc { get; set; }
            [JsonProperty(En ? "Can an AutoTurret attack a non-owner of the event? [true/false]" : "Может ли турель атаковать не владельца ивента? [true/false]")] public bool TargetTurret { get; set; }
            [JsonProperty(En ? "Allow the non-owner of the event to enter the event zone? [true/false]" : "Разрешать входить внутрь зоны ивента не владельцу ивента? [true/false]")] public bool CanEnter { get; set; }
            [JsonProperty(En ? "Allow a player who has an active cooldown of the Event Owner to enter the event zone? [true/false]" : "Разрешать входить внутрь зоны ивента игроку, у которого активен кулдаун на получение статуса владельца ивента? [true/false]")] public bool CanEnterCooldownPlayer { get; set; }
            [JsonProperty(En ? "The time that the Event Owner may not be inside the event zone [sec.]" : "Время, которое владелец ивента может не находиться внутри зоны ивента [сек.]")] public int TimeExitOwner { get; set; }
            [JsonProperty(En ? "The time until the end of Event Owner status when it is necessary to warn the player [sec.]" : "Время таймера до окончания действия статуса владельца ивента, когда необходимо предупредить игрока [сек.]")] public int AlertTime { get; set; }
            [JsonProperty(En ? "Prevent the actions of the RestoreUponDeath plugin in the event zone? [true/false]" : "Запрещать работу плагина RestoreUponDeath в зоне действия ивента? [true/false]")] public bool RestoreUponDeath { get; set; }
            [JsonProperty(En ? "The time that the player can`t become the Event Owner, after the end of the event and the player was its owner [sec.]" : "Время, которое игрок не сможет стать владельцем ивента, после того как ивент окончен и игрок был его владельцем [sec.]")] public double CooldownOwner { get; set; }
            [JsonProperty(En ? "Darkening the dome (0 - disables the dome)" : "Затемнение купола (0 - отключает купол)")] public int Darkening { get; set; }
        }

        public class NpcBelt
        {
            [JsonProperty("ShortName")] public string ShortName { get; set; }
            [JsonProperty(En ? "Amount" : "Кол-во")] public int Amount { get; set; }
            [JsonProperty("SkinID (0 - default)")] public ulong SkinId { get; set; }
            [JsonProperty(En ? "Mods" : "Модификации на оружие")] public HashSet<string> Mods { get; set; }
            [JsonProperty(En ? "Ammo" : "Боеприпасы")] public string Ammo { get; set; }
        }

        public class NpcWear
        {
            [JsonProperty("ShortName")] public string ShortName { get; set; }
            [JsonProperty("SkinID (0 - default)")] public ulong SkinId { get; set; }
        }

        public class NpcConfig
        {
            [JsonProperty(En ? "Name" : "Название")] public string Name { get; set; }
            [JsonProperty(En ? "Health" : "Кол-во ХП")] public float Health { get; set; }
            [JsonProperty(En ? "Roam Range" : "Дальность патрулирования местности")] public float RoamRange { get; set; }
            [JsonProperty(En ? "Chase Range" : "Дальность погони за целью")] public float ChaseRange { get; set; }
            [JsonProperty(En ? "Attack Range Multiplier" : "Множитель радиуса атаки")] public float AttackRangeMultiplier { get; set; }
            [JsonProperty(En ? "Sense Range" : "Радиус обнаружения цели")] public float SenseRange { get; set; }
            [JsonProperty(En ? "Target Memory Duration [sec.]" : "Длительность памяти цели [sec.]")] public float MemoryDuration { get; set; }
            [JsonProperty(En ? "Scale damage" : "Множитель урона")] public float DamageScale { get; set; }
            [JsonProperty(En ? "Aim Cone Scale" : "Множитель разброса")] public float AimConeScale { get; set; }
            [JsonProperty(En ? "Detect the target only in the NPC's viewing vision cone? [true/false]" : "Обнаруживать цель только в углу обзора NPC? [true/false]")] public bool CheckVisionCone { get; set; }
            [JsonProperty(En ? "Vision Cone" : "Угол обзора")] public float VisionCone { get; set; }
            [JsonProperty(En ? "Speed" : "Скорость")] public float Speed { get; set; }
            [JsonProperty(En ? "Disable radio effects? [true/false]" : "Отключать эффекты рации? [true/false]")] public bool DisableRadio { get; set; }
            [JsonProperty(En ? "Is this a stationary NPC? [true/false]" : "Это стационарный NPC? [true/false]")] public bool Stationary { get; set; }
            [JsonProperty(En ? "Remove a corpse after death? (it is recommended to use the true value to improve performance) [true/false]" : "Удалять труп после смерти? (рекомендуется использовать значение true для повышения производительности) [true/false]")] public bool IsRemoveCorpse { get; set; }
            [JsonProperty(En ? "Wear items" : "Одежда")] public HashSet<NpcWear> WearItems { get; set; }
            [JsonProperty(En ? "Belt items" : "Быстрые слоты")] public HashSet<NpcBelt> BeltItems { get; set; }
            [JsonProperty(En ? "Kit (it is recommended to use the previous 2 settings to improve performance)" : "Kit (рекомендуется использовать предыдущие 2 пункта настройки для повышения производительности)")] public string Kit { get; set; }
            [JsonProperty(En ? "Which loot table should the plugin use? (0 - default; 1 - own; 2 - AlphaLoot; 3 - CustomLoot; 4 - loot table of the Rust objects; 5 - combine the 1 and 4 methods)" : "Какую таблицу предметов необходимо использовать? (0 - стандартную; 1 - собственную; 2 - AlphaLoot; 3 - CustomLoot; 4 - таблица предметов объектов Rust; 5 - совместить 1 и 4 методы)")] public int TypeLootTable { get; set; }
            [JsonProperty(En ? "Loot table from prefabs (if the loot table type is 4 or 5)" : "Таблица предметов из prefab-ов (если тип таблицы предметов - 4 или 5)")] public PrefabLootTableConfig PrefabLootTable { get; set; }
            [JsonProperty(En ? "Own loot table (if the loot table type is 1 or 5)" : "Собственная таблица предметов (если тип таблицы предметов - 1 или 5)")] public LootTableConfig OwnLootTable { get; set; }
        }

        public class SpawnInfoNpc
        {
            [JsonProperty(En ? "Minimum" : "Минимальное кол-во")] public int Min { get; set; }
            [JsonProperty(En ? "Maximum" : "Максимальное кол-во")] public int Max { get; set; }
            [JsonProperty(En ? "List of locations" : "Список расположений")] public HashSet<string> Positions { get; set; }
        }

        public class DoorsToScientists
        {
            [JsonProperty(En ? "Position of doors (not edited)" : "Координаты дверей (не редактируется)")] public HashSet<string> Doors { get; set; }
            [JsonProperty(En ? "NPCs setting" : "Настройка NPC")] public Dictionary<string, SpawnInfoNpc> Scientists { get; set; }
        }

        public class TurretConfig
        {
            [JsonProperty(En ? "Can Auto Turret appear? [true/false]" : "Использовать турели? [true/false]")] public bool IsTurret { get; set; }
            [JsonProperty(En ? "Health" : "Кол-во ХП")] public float Hp { get; set; }
            [JsonProperty(En ? "Weapon ShortName" : "ShortName оружия")] public string ShortNameWeapon { get; set; }
            [JsonProperty(En ? "Ammo ShortName" : "ShortName патронов")] public string ShortNameAmmo { get; set; }
            [JsonProperty(En ? "Number of ammo" : "Кол-во патронов")] public int CountAmmo { get; set; }
        }

        private class PluginConfig
        {
            [JsonProperty(En ? "Minimum time between events [sec.]" : "Минимальное время между ивентами [sec.]")] public float MinStartTime { get; set; }
            [JsonProperty(En ? "Maximum time between events [sec.]" : "Максимальное время между ивентами [sec.]")] public float MaxStartTime { get; set; }
            [JsonProperty(En ? "Is active the timer on to start the event? [true/false]" : "Активен ли таймер для запуска ивента? [true/false]")] public bool EnabledTimer { get; set; }
            [JsonProperty(En ? "Duration of the event [sec.]" : "Время проведения ивента [sec.]")] public int FinishTime { get; set; }
            [JsonProperty(En ? "Time before the starting of the event after receiving a chat message [sec.]" : "Время до начала ивента после сообщения в чате [sec.]")] public float PreStartTime { get; set; }
            [JsonProperty(En ? "Notification time until the end of the event [sec.]" : "Время оповещения до окончания ивента [sec.]")] public int PreFinishTime { get; set; }
            [JsonProperty(En ? "Time to spawn each object during a submarine appears on the map [sec.]" : "Время для спавна каждого объекта при появлении подводной лодки на карте [sec.]")] public float Delay { get; set; }
            [JsonProperty(En ? "Deployed Crates setting" : "Настройка появляемого лута в шкафах для переодевания, маленьких/больших ящиках и ящиках для хранения")] public HashSet<EntityCrateConfig> EntityCrates { get; set; }
            [JsonProperty(En ? "Crates setting" : "Настройка ящиков")] public HashSet<CrateConfig> DefaultCrates { get; set; }
            [JsonProperty(En ? "Locked Crates setting" : "Настройка заблокированных ящиков")] public HackCrateConfig HackCrate { get; set; }
            [JsonProperty(En ? "NPC presets settings" : "Настройка пресетов NPC")] public Dictionary<string, NpcConfig> Npc { get; set; }
            [JsonProperty(En ? "NPCs setting outside" : "Настройка NPC снаружи")] public Dictionary<string, SpawnInfoNpc> OutsideNpc { get; set; }
            [JsonProperty(En ? "List of doors and NPCs inside the submarine" : "Список дверей и NPC внутри подводной лодки")] public HashSet<DoorsToScientists> InsideNpc { get; set; }
            [JsonProperty(En ? "Marker configuration on the map" : "Настройка маркера на карте")] public MarkerConfig Marker { get; set; }
            [JsonProperty(En ? "Main marker settings for key event points shown on players screen" : "Настройки основного маркера на экране игрока")] public PointConfig MainPoint { get; set; }
            [JsonProperty(En ? "Additional marker settings for key event points shown on players screen" : "Настройки дополнительного маркера на экране игрока")] public PointConfig AdditionalPoint { get; set; }
            [JsonProperty(En ? "GUI setting" : "Настройки GUI")] public GuiConfig Gui { get; set; }
            [JsonProperty(En ? "Chat setting" : "Настройки чата")] public ChatConfig Chat { get; set; }
            [JsonProperty(En ? "Facepunch Game Tips setting" : "Настройка сообщений Facepunch Game Tip")] public GameTipConfig GameTip { get; set; }
            [JsonProperty(En ? "GUI Announcements setting" : "Настройка GUI Announcements")] public GuiAnnouncementsConfig GuiAnnouncements { get; set; }
            [JsonProperty(En ? "Notify setting" : "Настройка Notify")] public NotifyConfig Notify { get; set; }
            [JsonProperty(En ? "The distance from the event to the player for global alerts (0 - no limit)" : "Расстояние от ивента до игрока для глобальных оповещений (0 - нет ограничений)")] public float DistanceAlerts { get; set; }
            [JsonProperty(En ? "Discord setting (only for users DiscordMessages plugin)" : "Настройка оповещений в Discord (только для тех, кто использует плагин DiscordMessages)")] public DiscordConfig Discord { get; set; }
            [JsonProperty(En ? "Radius of the event zone" : "Радиус зоны ивента")] public float Radius { get; set; }
            [JsonProperty(En ? "Do you create a PVP zone in the event area? (only for users TruePVE plugin) [true/false]" : "Создавать зону PVP в зоне проведения ивента? (только для тех, кто использует плагин TruePVE) [true/false]")] public bool IsCreateZonePvp { get; set; }
            [JsonProperty(En ? "PVE Mode Setting (only for users PveMode plugin)" : "Настройка PVE режима работы плагина (только для тех, кто использует плагин PveMode)")] public PveModeConfig PveMode { get; set; }
            [JsonProperty(En ? "Interrupt the teleport in a submarine? (only for users NTeleportation plugin) [true/false]" : "Запрещать телепорт на подводной лодке? (только для тех, кто использует плагин NTeleportation) [true/false]")] public bool NTeleportationInterrupt { get; set; }
            [JsonProperty(En ? "Economy setting (total values will be added up and rewarded at the end of the event)" : "Настройка экономики (конечное значение суммируется и будет выдано игрокам по окончанию ивента)")] public EconomyConfig Economy { get; set; }
            [JsonProperty(En ? "List of commands banned in the event zone" : "Список команд запрещенных в зоне ивента")] public HashSet<string> Commands { get; set; }
            [JsonProperty(En ? "Setting AutoTurrets" : "Настройка турелей")] public TurretConfig Turret { get; set; }
            [JsonProperty(En ? "Setting the doors damage coefficient" : "Настройка коэффициента наносимого урона по дверям")] public Dictionary<string, float> ScaleDamage { get; set; }
            [JsonProperty(En ? "Door Health" : "Кол-во HP дверей")] public float DoorHealth { get; set; }
            [JsonProperty(En ? "Distance from the submarine to the building block" : "Расстояние от подводной лодки до постройки игрока")] public float DistanceToBlock { get; set; }
            [JsonProperty(En ? "Custom positions for the event to appear on the map" : "Кастомные позиции для появления ивента на карте")] public HashSet<string> Positions { get; set; }
            [JsonProperty(En ? "Do Workbenches work in a submarine? [true/false]" : "Работают ли верстаки на подводной лодке? [true/false]")] public bool IsWorkbench { get; set; }
            [JsonProperty(En ? "Do Repair Bench work in a submarine? [true/false]" : "Работают ли ремонтные верстаки на подводной лодке? [true/false]")] public bool IsRepairBench { get; set; }
            [JsonProperty(En ? "Do Research Table work in a submarine? [true/false]" : "Работают ли столы для исследования на подводной лодке? [true/false]")] public bool IsResearchTable { get; set; }
            [JsonProperty(En ? "Do Mixing Table work in a submarine? [true/false]" : "Работают ли столы для смешивания на подводной лодке? [true/false]")] public bool IsMixingTable { get; set; }
            [JsonProperty(En ? "Do Locker work in a submarine? [true/false]" : "Работают ли шкафы для переодевания на подводной лодке? [true/false]")] public bool IsLocker { get; set; }
            [JsonProperty(En ? "Do Storage Box work in a submarine? [true/false]" : "Работают ли ящики на подводной лодке? [true/false]")] public bool IsBoxStorage { get; set; }
            [JsonProperty(En ? "Configuration version" : "Версия конфигурации")] public VersionNumber PluginVersion { get; set; }

            public static PluginConfig DefaultConfig()
            {
                return new PluginConfig
                {
                    MinStartTime = 10800f,
                    MaxStartTime = 10800f,
                    EnabledTimer = true,
                    FinishTime = 5400,
                    PreStartTime = 600f,
                    PreFinishTime = 300,
                    Delay = 0.001f,
                    EntityCrates = new HashSet<EntityCrateConfig>
                    {
                        new EntityCrateConfig
                        {
                            Prefab = "assets/prefabs/deployable/locker/locker.deployed.prefab",
                            IsOwnLootTable = false,
                            LootTable = new LootTableConfig
                            {
                                Min = 0, Max = 1,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 100, MaxAmount = 200, Chance = 50f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new EntityCrateConfig
                        {
                            Prefab = "assets/prefabs/deployable/large wood storage/box.wooden.large.prefab",
                            IsOwnLootTable = false,
                            LootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 100, MaxAmount = 200, Chance = 50f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new EntityCrateConfig
                        {
                            Prefab = "assets/prefabs/deployable/woodenbox/woodbox_deployed.prefab",
                            IsOwnLootTable = false,
                            LootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 100, MaxAmount = 200, Chance = 50f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        }
                    },
                    DefaultCrates = new HashSet<CrateConfig>
                    {
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/underwater_labs/tech_parts_1.prefab",
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 50f, PrefabDefinition = "assets/bundled/prefabs/radtown/underwater_labs/tech_parts_1.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 3, Max = 3, UseCount = true,
                                Items = new List<ItemConfig>
                                {
                                    new ItemConfig { ShortName = "explosive.timed", MinAmount = 1, MaxAmount = 2, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "ammo.rocket.basic", MinAmount = 2, MaxAmount = 4, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "scrap", MinAmount = 8, MaxAmount = 8, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" }
                                }
                            },
                            ScaleItems = new Dictionary<string, float> { ["explosive.timed"] = 1f }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/underwater_labs/tech_parts_2.prefab",
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/underwater_labs/tech_parts_2.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 3, Max = 3, UseCount = true,
                                Items = new List<ItemConfig>
                                {
                                    new ItemConfig { ShortName = "explosive.timed", MinAmount = 1, MaxAmount = 2, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "ammo.rocket.basic", MinAmount = 2, MaxAmount = 4, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "scrap", MinAmount = 8, MaxAmount = 8, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" }
                                }
                            },
                            ScaleItems = new Dictionary<string, float> { ["explosive.timed"] = 1f }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/underwater_labs/crate_ammunition.prefab",
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/underwater_labs/crate_ammunition.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 2, Max = 2, UseCount = true,
                                Items = new List<ItemConfig>
                                {
                                    new ItemConfig { ShortName = "explosive.timed", MinAmount = 1, MaxAmount = 2, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "ammo.rocket.basic", MinAmount = 2, MaxAmount = 4, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" }
                                }
                            },
                            ScaleItems = new Dictionary<string, float> { ["explosive.timed"] = 1f }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/underwater_labs/crate_fuel.prefab",
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/underwater_labs/crate_fuel.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 2, Max = 2, UseCount = true,
                                Items = new List<ItemConfig>
                                {
                                    new ItemConfig { ShortName = "explosive.timed", MinAmount = 1, MaxAmount = 2, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "ammo.rocket.basic", MinAmount = 2, MaxAmount = 4, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" }
                                }
                            },
                            ScaleItems = new Dictionary<string, float> { ["explosive.timed"] = 1f }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/underwater_labs/crate_tools.prefab",
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/underwater_labs/crate_tools.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 3, Max = 3, UseCount = true,
                                Items = new List<ItemConfig>
                                {
                                    new ItemConfig { ShortName = "explosive.timed", MinAmount = 1, MaxAmount = 2, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "ammo.rocket.basic", MinAmount = 2, MaxAmount = 4, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "scrap", MinAmount = 5, MaxAmount = 5, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" }
                                }
                            },
                            ScaleItems = new Dictionary<string, float> { ["explosive.timed"] = 1f }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_underwater_basic.prefab",
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_underwater_basic.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 2, Max = 2, UseCount = true,
                                Items = new List<ItemConfig>
                                {
                                    new ItemConfig { ShortName = "explosive.timed", MinAmount = 1, MaxAmount = 2, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "ammo.rocket.basic", MinAmount = 2, MaxAmount = 4, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" }
                                }
                            },
                            ScaleItems = new Dictionary<string, float> { ["explosive.timed"] = 1f }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_underwater_advanced.prefab",
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_underwater_advanced.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 2, Max = 2, UseCount = true,
                                Items = new List<ItemConfig>
                                {
                                    new ItemConfig { ShortName = "explosive.timed", MinAmount = 1, MaxAmount = 2, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "ammo.rocket.basic", MinAmount = 2, MaxAmount = 4, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" }
                                }
                            },
                            ScaleItems = new Dictionary<string, float> { ["explosive.timed"] = 1f }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/underwater_labs/crate_medical.prefab",
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/underwater_labs/crate_medical.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 2, Max = 2, UseCount = true,
                                Items = new List<ItemConfig>
                                {
                                    new ItemConfig { ShortName = "explosive.timed", MinAmount = 1, MaxAmount = 2, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "ammo.rocket.basic", MinAmount = 2, MaxAmount = 4, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" }
                                }
                            },
                            ScaleItems = new Dictionary<string, float> { ["explosive.timed"] = 1f }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_elite.prefab",
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_elite.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 3, Max = 3, UseCount = true,
                                Items = new List<ItemConfig>
                                {
                                    new ItemConfig { ShortName = "explosive.timed", MinAmount = 2, MaxAmount = 4, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "ammo.rocket.basic", MinAmount = 4, MaxAmount = 8, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "scrap", MinAmount = 25, MaxAmount = 25, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" }
                                }
                            },
                            ScaleItems = new Dictionary<string, float> { ["explosive.timed"] = 1f }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/underwater_labs/crate_elite.prefab",
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/underwater_labs/crate_elite.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 3, Max = 3, UseCount = true,
                                Items = new List<ItemConfig>
                                {
                                    new ItemConfig { ShortName = "explosive.timed", MinAmount = 2, MaxAmount = 4, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "ammo.rocket.basic", MinAmount = 4, MaxAmount = 8, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "scrap", MinAmount = 25, MaxAmount = 25, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" }
                                }
                            },
                            ScaleItems = new Dictionary<string, float> { ["explosive.timed"] = 1f }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/underwater_labs/crate_food_1.prefab",
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/underwater_labs/crate_food_1.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 2, Max = 2, UseCount = true,
                                Items = new List<ItemConfig>
                                {
                                    new ItemConfig { ShortName = "explosive.timed", MinAmount = 1, MaxAmount = 2, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "ammo.rocket.basic", MinAmount = 2, MaxAmount = 4, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" }
                                }
                            },
                            ScaleItems = new Dictionary<string, float> { ["explosive.timed"] = 1f }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/underwater_labs/crate_food_2.prefab",
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/underwater_labs/crate_food_2.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 2, Max = 2, UseCount = true,
                                Items = new List<ItemConfig>
                                {
                                    new ItemConfig { ShortName = "explosive.timed", MinAmount = 1, MaxAmount = 2, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "ammo.rocket.basic", MinAmount = 2, MaxAmount = 4, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" }
                                }
                            },
                            ScaleItems = new Dictionary<string, float> { ["explosive.timed"] = 1f }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_normal.prefab",
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_normal.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 3, Max = 3, UseCount = true,
                                Items = new List<ItemConfig>
                                {
                                    new ItemConfig { ShortName = "explosive.timed", MinAmount = 1, MaxAmount = 2, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "ammo.rocket.basic", MinAmount = 2, MaxAmount = 4, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "scrap", MinAmount = 5, MaxAmount = 5, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" }
                                }
                            },
                            ScaleItems = new Dictionary<string, float> { ["explosive.timed"] = 1f }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/underwater_labs/crate_normal.prefab",
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/underwater_labs/crate_normal.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 3, Max = 3, UseCount = true,
                                Items = new List<ItemConfig>
                                {
                                    new ItemConfig { ShortName = "explosive.timed", MinAmount = 1, MaxAmount = 2, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "ammo.rocket.basic", MinAmount = 2, MaxAmount = 4, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "scrap", MinAmount = 8, MaxAmount = 8, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" }
                                }
                            },
                            ScaleItems = new Dictionary<string, float> { ["explosive.timed"] = 1f }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_normal_2.prefab",
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_normal_2.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 3, Max = 3, UseCount = true,
                                Items = new List<ItemConfig>
                                {
                                    new ItemConfig { ShortName = "explosive.timed", MinAmount = 1, MaxAmount = 2, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "ammo.rocket.basic", MinAmount = 2, MaxAmount = 4, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "scrap", MinAmount = 8, MaxAmount = 8, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" }
                                }
                            },
                            ScaleItems = new Dictionary<string, float> { ["explosive.timed"] = 1f }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/underwater_labs/crate_normal_2.prefab",
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/underwater_labs/crate_normal_2.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 3, Max = 3, UseCount = true,
                                Items = new List<ItemConfig>
                                {
                                    new ItemConfig { ShortName = "explosive.timed", MinAmount = 1, MaxAmount = 2, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "ammo.rocket.basic", MinAmount = 2, MaxAmount = 4, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "scrap", MinAmount = 5, MaxAmount = 5, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" }
                                }
                            },
                            ScaleItems = new Dictionary<string, float> { ["explosive.timed"] = 1f }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_normal_2_food.prefab",
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_normal_2_food.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 2, Max = 2, UseCount = true,
                                Items = new List<ItemConfig>
                                {
                                    new ItemConfig { ShortName = "explosive.timed", MinAmount = 1, MaxAmount = 2, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "ammo.rocket.basic", MinAmount = 2, MaxAmount = 4, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" }
                                }
                            },
                            ScaleItems = new Dictionary<string, float> { ["explosive.timed"] = 1f }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_normal_2_medical.prefab",
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_normal_2_medical.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 2, Max = 2, UseCount = true,
                                Items = new List<ItemConfig>
                                {
                                    new ItemConfig { ShortName = "explosive.timed", MinAmount = 1, MaxAmount = 2, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "ammo.rocket.basic", MinAmount = 2, MaxAmount = 4, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" }
                                }
                            },
                            ScaleItems = new Dictionary<string, float> { ["explosive.timed"] = 1f }
                        }
                    },
                    HackCrate = new HackCrateConfig
                    {
                        Coordinates = new HashSet<LocationConfig>
                        {
                            new LocationConfig { Position = "(-3.768, 3.1, 22.509)", Rotation = "(0, 90, 0)" },
                            new LocationConfig { Position = "(3.028, 0.082, 5.223)", Rotation = "(0, 0, 0)" }
                        },
                        UnlockTime = 600f,
                        IncreaseEventTime = true,
                        TypeLootTable = 5,
                        PrefabLootTable = new PrefabLootTableConfig
                        {
                            Min = 1,
                            Max = 1,
                            UseCount = true,
                            Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100.0f, PrefabDefinition = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab" } }
                        },
                        OwnLootTable = new LootTableConfig
                        {
                            Min = 2,
                            Max = 2,
                            UseCount = true,
                            Items = new List<ItemConfig>
                            {
                                new ItemConfig { ShortName = "explosive.timed", MinAmount = 4, MaxAmount = 8, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" },
                                new ItemConfig { ShortName = "ammo.rocket.basic", MinAmount = 8, MaxAmount = 16, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" }
                            }
                        },
                        ScaleItems = new Dictionary<string, float> { ["explosive.timed"] = 1f },
                    },
                    Npc = new Dictionary<string, NpcConfig>
                    {
                        ["SeaDevil_1"] = new NpcConfig
                        {
                            Name = "SeaDevil",
                            Health = 120f,
                            RoamRange = 5f,
                            ChaseRange = 100f,
                            AttackRangeMultiplier = 5f,
                            SenseRange = 75f,
                            MemoryDuration = 20f,
                            DamageScale = 0.7f,
                            AimConeScale = 0f,
                            CheckVisionCone = false,
                            VisionCone = 135f,
                            Speed = 7.5f,
                            DisableRadio = true,
                            Stationary = false,
                            IsRemoveCorpse = true,
                            WearItems = new HashSet<NpcWear>
                            {
                                new NpcWear { ShortName = "burlap.shirt", SkinId = 2216143685 },
                                new NpcWear { ShortName = "burlap.trousers", SkinId = 2216144342 },
                                new NpcWear { ShortName = "shoes.boots", SkinId = 0 },
                                new NpcWear { ShortName = "burlap.gloves", SkinId = 0 },
                                new NpcWear { ShortName = "hat.dragonmask", SkinId = 0 }
                            },
                            BeltItems = new HashSet<NpcBelt>
                            {
                                new NpcBelt { ShortName = "rifle.m39", Amount = 1, SkinId = 0, Mods = new HashSet<string> { "weapon.mod.flashlight", "weapon.mod.small.scope", "weapon.mod.silencer" }, Ammo = string.Empty },
                                new NpcBelt { ShortName = "syringe.medical", Amount = 5, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty },
                                new NpcBelt { ShortName = "grenade.f1", Amount = 2, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty },
                                new NpcBelt { ShortName = "grenade.smoke", Amount = 1, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty }
                            },
                            Kit = string.Empty,
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_oilrig.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Items = new List<ItemConfig>
                                {
                                    new ItemConfig { ShortName = "scrap", MinAmount = 5, MaxAmount = 10, Chance = 50f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "syringe.medical", MinAmount = 1, MaxAmount = 2, Chance = 70f, IsBluePrint = false, SkinId = 0, Name = "" }
                                }
                            }
                        },
                        ["SeaDevil_2"] = new NpcConfig
                        {
                            Name = "SeaDevil",
                            Health = 200f,
                            RoamRange = 5f,
                            ChaseRange = 100f,
                            AttackRangeMultiplier = 5f,
                            SenseRange = 150f,
                            MemoryDuration = 10f,
                            DamageScale = 0.2f,
                            AimConeScale = 0f,
                            CheckVisionCone = false,
                            VisionCone = 135f,
                            Speed = 7.5f,
                            DisableRadio = true,
                            Stationary = false,
                            IsRemoveCorpse = true,
                            WearItems = new HashSet<NpcWear>
                            {
                                new NpcWear { ShortName = "burlap.shirt", SkinId = 2216143685 },
                                new NpcWear { ShortName = "burlap.trousers", SkinId = 2216144342 },
                                new NpcWear { ShortName = "shoes.boots", SkinId = 0 },
                                new NpcWear { ShortName = "burlap.gloves", SkinId = 0 },
                                new NpcWear { ShortName = "hat.dragonmask", SkinId = 0 }
                            },
                            BeltItems = new HashSet<NpcBelt>
                            {
                                new NpcBelt { ShortName = "rifle.bolt", Amount = 1, SkinId = 0, Mods = new HashSet<string> { "weapon.mod.flashlight", "weapon.mod.small.scope", "weapon.mod.silencer" }, Ammo = string.Empty }
                            },
                            Kit = string.Empty,
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_oilrig.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Items = new List<ItemConfig>
                                {
                                    new ItemConfig { ShortName = "scrap", MinAmount = 5, MaxAmount = 10, Chance = 50f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "syringe.medical", MinAmount = 1, MaxAmount = 2, Chance = 70f, IsBluePrint = false, SkinId = 0, Name = "" }
                                }
                            }
                        },
                        ["Mariner"] = new NpcConfig
                        {
                            Name = "Mariner",
                            Health = 125f,
                            RoamRange = 4f,
                            ChaseRange = 60f,
                            AttackRangeMultiplier = 1f,
                            SenseRange = 30f,
                            MemoryDuration = 20f,
                            DamageScale = 1.35f,
                            AimConeScale = 1f,
                            CheckVisionCone = false,
                            VisionCone = 135f,
                            Speed = 7.5f,
                            DisableRadio = true,
                            Stationary = false,
                            IsRemoveCorpse = true,
                            WearItems = new HashSet<NpcWear>
                            {
                                new NpcWear { ShortName = "metal.facemask", SkinId = 2296503845 },
                                new NpcWear { ShortName = "hoodie", SkinId = 2304560839 },
                                new NpcWear { ShortName = "pants", SkinId = 2304559261 },
                                new NpcWear { ShortName = "shoes.boots", SkinId = 0 }
                            },
                            BeltItems = new HashSet<NpcBelt>
                            {
                                new NpcBelt { ShortName = "rifle.lr300", Amount = 1, SkinId = 0, Mods = new HashSet<string> { "weapon.mod.flashlight", "weapon.mod.holosight" }, Ammo = string.Empty },
                                new NpcBelt { ShortName = "syringe.medical", Amount = 5, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty },
                                new NpcBelt { ShortName = "grenade.f1", Amount = 1, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty }
                            },
                            Kit = string.Empty,
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_oilrig.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Items = new List<ItemConfig>
                                {
                                    new ItemConfig { ShortName = "scrap", MinAmount = 5, MaxAmount = 10, Chance = 50f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "syringe.medical", MinAmount = 1, MaxAmount = 2, Chance = 70f, IsBluePrint = false, SkinId = 0, Name = "" }
                                }
                            }
                        }
                    },
                    OutsideNpc = new Dictionary<string, SpawnInfoNpc>
                    {
                        ["SeaDevil_1"] = new SpawnInfoNpc
                        {
                            Min = 4,
                            Max = 4,
                            Positions = new HashSet<string>
                            {
                                "(0.0, 8.8, -9.1)",
                                "(2.4, 8.4, 19.5)",
                                "(-2.2, 8.4, 19.4)",
                                "(0.1, 8.8, -18.3)",
                                "(-0.1, 8.8, 39.7)",
                                "(-0.1, 8.8, 0.1)"
                            }
                        },
                        ["SeaDevil_2"] = new SpawnInfoNpc
                        {
                            Min = 1,
                            Max = 1,
                            Positions = new HashSet<string>
                            {
                                "(12.3, 0.6, -0.7)",
                                "(-12.3, 0.6, -0.9)"
                            }
                        }
                    },
                    InsideNpc = new HashSet<DoorsToScientists>
                    {
                        new DoorsToScientists
                        {
                            Doors = new HashSet<string>
                            {
                                "(-6.568, 5.511, -19.545)",
                                "(-1.5, 3, -18)"
                            },
                            Scientists = new Dictionary<string, SpawnInfoNpc>
                            {
                                ["Mariner"] = new SpawnInfoNpc
                                {
                                    Min = 1, Max = 1,
                                    Positions = new HashSet<string>
                                    {
                                        "(-3.0, 3.1, -18.3)"
                                    }
                                }
                            }
                        },
                        new DoorsToScientists
                        {
                            Doors = new HashSet<string>
                            {
                                "(-1.5, 3, -18)",
                                "(1.5, 3, -18)",
                                "(0, 3, -16.5)"
                            },
                            Scientists = new Dictionary<string, SpawnInfoNpc>
                            {
                                ["Mariner"] = new SpawnInfoNpc
                                {
                                    Min = 1, Max = 1,
                                    Positions = new HashSet<string>
                                    {
                                        "(0.0, 3.1, -17.9)"
                                    }
                                }
                            }
                        },
                        new DoorsToScientists
                        {
                            Doors = new HashSet<string>
                            {
                                "(1.5, 3, -18)",
                                "(6.568, 5.511, -19.5)"
                            },
                            Scientists = new Dictionary<string, SpawnInfoNpc>
                            {
                                ["Mariner"] = new SpawnInfoNpc
                                {
                                    Min = 1, Max = 1,
                                    Positions = new HashSet<string>
                                    {
                                        "(2.9, 3.1, -18.3)"
                                    }
                                }
                            }
                        },
                        new DoorsToScientists
                        {
                            Doors = new HashSet<string>
                            {
                                "(0, 3, -16.5)",
                                "(0, 3, -7.5)",
                                "(0, 3, -10.5)"
                            },
                            Scientists = new Dictionary<string, SpawnInfoNpc>
                            {
                                ["Mariner"] = new SpawnInfoNpc
                                {
                                    Min = 3, Max = 3,
                                    Positions = new HashSet<string>
                                    {
                                        "(-3.1, 3.1, -14.5)",
                                        "(3.5, 3.1, -14.5)",
                                        "(0.0, 3.1, -9.2)"
                                    }
                                }
                            }
                        },
                        new DoorsToScientists
                        {
                            Doors = new HashSet<string>
                            {
                                "(0, 3, -7.5)",
                                "(0, 3, 28.5)"
                            },
                            Scientists = new Dictionary<string, SpawnInfoNpc>
                            {
                                ["Mariner"] = new SpawnInfoNpc
                                {
                                    Min = 6, Max = 6,
                                    Positions = new HashSet<string>
                                    {
                                        "(-2.9, 3.1, -6.1)",
                                        "(-2.7, 3.1, 11.4)",
                                        "(2.7, 3.1, 12.3)",
                                        "(-1.2, 3.1, 16.9)",
                                        "(-1.3, 3.1, 22.5)",
                                        "(3.0, 3.1, 27.2)"
                                    }
                                }
                            }
                        },
                        new DoorsToScientists
                        {
                            Doors = new HashSet<string>
                            {
                                "(-1.5, 3, 0)",
                                "(-1.5, 3, 3)"
                            },
                            Scientists = new Dictionary<string, SpawnInfoNpc>
                            {
                                ["Mariner"] = new SpawnInfoNpc
                                {
                                    Min = 3, Max = 3,
                                    Positions = new HashSet<string>
                                    {
                                        "(1.8, 3.1, -1.8)",
                                        "(1.9, 3.1, 3.2)",
                                        "(1.5, 3.1, 7.8)"
                                    }
                                }
                            }
                        },
                        new DoorsToScientists
                        {
                            Doors = new HashSet<string>
                            {
                                "(0, 3, 28.5)",
                                "(0, 3, 37.5)",
                                "(0, 3, 31.5)"
                            },
                            Scientists = new Dictionary<string, SpawnInfoNpc>
                            {
                                ["Mariner"] = new SpawnInfoNpc
                                {
                                    Min = 2, Max = 2,
                                    Positions = new HashSet<string>
                                    {
                                        "(2.9, 3.1, 30.3)",
                                        "(-3.3, 3.1, 34.5)"
                                    }
                                }
                            }
                        },
                        new DoorsToScientists
                        {
                            Doors = new HashSet<string>
                            {
                                "(0, 3, 37.5)",
                                "(1.5, 3, 39)",
                                "(-1.5, 3, 39)"
                            },
                            Scientists = new Dictionary<string, SpawnInfoNpc>
                            {
                                ["Mariner"] = new SpawnInfoNpc
                                {
                                    Min = 1, Max = 1,
                                    Positions = new HashSet<string>
                                    {
                                        "(0.0, 3.1, 39.0)"
                                    }
                                }
                            }
                        },
                        new DoorsToScientists
                        {
                            Doors = new HashSet<string>
                            {
                                "(-1.5, 3, 39)",
                                "(-6.568, 5.511, 37.541)"
                            },
                            Scientists = new Dictionary<string, SpawnInfoNpc>
                            {
                                ["Mariner"] = new SpawnInfoNpc
                                {
                                    Min = 1, Max = 1,
                                    Positions = new HashSet<string>
                                    {
                                        "(-3.0, 3.1, 39.4)"
                                    }
                                }
                            }
                        },
                        new DoorsToScientists
                        {
                            Doors = new HashSet<string>
                            {
                                "(1.5, 3, 39)",
                                "(6.568, 5.511, 37.5)"
                            },
                            Scientists = new Dictionary<string, SpawnInfoNpc>
                            {
                                ["Mariner"] = new SpawnInfoNpc
                                {
                                    Min = 1, Max = 1,
                                    Positions = new HashSet<string>
                                    {
                                        "(3.4, 3.1, 39.2)"
                                    }
                                }
                            }
                        },
                        new DoorsToScientists
                        {
                            Doors = new HashSet<string>
                            {
                                "(-3, 0, -16.5)",
                                "(0, 0, -16.5)",
                                "(3, 0, -16.5)",
                                "(0, 0, -10.5)",
                                "(6, 0, -7.5)",
                                "(0, 0, -7.5)"
                            },
                            Scientists = new Dictionary<string, SpawnInfoNpc>
                            {
                                ["Mariner"] = new SpawnInfoNpc
                                {
                                    Min = 4, Max = 4,
                                    Positions = new HashSet<string>
                                    {
                                        "(-5.6, 0.1, -14.7)",
                                        "(5.7, 0.1, -14.4)",
                                        "(-5.5, 0.1, -9.5)",
                                        "(5.6, 0.1, -9.5)"
                                    }
                                }
                            }
                        },
                        new DoorsToScientists
                        {
                            Doors = new HashSet<string>
                            {
                                "(0, 0, -7.5)",
                                "(0, 0, 1.5)",
                                "(4.5, 0, -3)"
                            },
                            Scientists = new Dictionary<string, SpawnInfoNpc>
                            {
                                ["Mariner"] = new SpawnInfoNpc
                                {
                                    Min = 2, Max = 2,
                                    Positions = new HashSet<string>
                                    {
                                        "(-4.0, 0.1, -3.0)",
                                        "(0.1, 0.1, -2.6)"
                                    }
                                }
                            }
                        },
                        new DoorsToScientists
                        {
                            Doors = new HashSet<string>
                            {
                                "(6, 0, -7.5)",
                                "(0, 0, 1.5)",
                                "(4.5, 0, -3)",
                                "(-3, 0, 4.5)"
                            },
                            Scientists = new Dictionary<string, SpawnInfoNpc>
                            {
                                ["Mariner"] = new SpawnInfoNpc
                                {
                                    Min = 2, Max = 2,
                                    Positions = new HashSet<string>
                                    {
                                        "(6.0, 0.1, -3.9)",
                                        "(5.9, 0.1, 2.6)"
                                    }
                                }
                            }
                        },
                        new DoorsToScientists
                        {
                            Doors = new HashSet<string>
                            {
                                "(-3, 0, 4.5)",
                                "(-3, 0, 28.5)"
                            },
                            Scientists = new Dictionary<string, SpawnInfoNpc>
                            {
                                ["Mariner"] = new SpawnInfoNpc
                                {
                                    Min = 5, Max = 5,
                                    Positions = new HashSet<string>
                                    {
                                        "(-5.3, 0.1, 5.9)",
                                        "(2.9, 0.1, 9.1)",
                                        "(6.7, 0.1, 15.0)",
                                        "(-6.1, 0.1, 25.6)",
                                        "(3.0, 0.1, 27.0)"
                                    }
                                }
                            }
                        },
                        new DoorsToScientists
                        {
                            Doors = new HashSet<string>
                            {
                                "(-3, 0, 28.5)",
                                "(0, 0, 31.5)",
                                "(-3, 0, 37.5)",
                                "(0, 0, 37.5)"
                            },
                            Scientists = new Dictionary<string, SpawnInfoNpc>
                            {
                                ["Mariner"] = new SpawnInfoNpc
                                {
                                    Min = 3, Max = 3,
                                    Positions = new HashSet<string>
                                    {
                                        "(5.5, 0.1, 31.1)",
                                        "(3.8, 0.1, 35.4)",
                                        "(-2.7, 0.1, 35.5)"
                                    }
                                }
                            }
                        },
                        new DoorsToScientists
                        {
                            Doors = new HashSet<string>
                            {
                                "(-4.5, 0, 36)"
                            },
                            Scientists = new Dictionary<string, SpawnInfoNpc>
                            {
                                ["Mariner"] = new SpawnInfoNpc
                                {
                                    Min = 1, Max = 1,
                                    Positions = new HashSet<string>
                                    {
                                        "(-5.2, 0.1, 30.4)"
                                    }
                                }
                            }
                        }
                    },
                    Marker = new MarkerConfig
                    {
                        Enabled = true,
                        Type = 1,
                        Radius = 0.37967f,
                        Alpha = 0.35f,
                        Color = new ColorConfig { R = 0.81f, G = 0.25f, B = 0.15f },
                        Text = "WaterEvent"
                    },
                    MainPoint = new PointConfig
                    {
                        Enabled = true,
                        Text = "◈",
                        Size = 45,
                        Color = "#CCFF00"
                    },
                    AdditionalPoint = new PointConfig
                    {
                        Enabled = false,
                        Text = "◆",
                        Size = 25,
                        Color = "#FFC700"
                    },
                    Gui = new GuiConfig
                    {
                        IsGui = true,
                        OffsetMinY = "-56"
                    },
                    Chat = new ChatConfig
                    {
                        IsChat = true,
                        Prefix = "[WaterEvent]"
                    },
                    GameTip = new GameTipConfig
                    {
                        IsGameTip = false,
                        Style = 2
                    },
                    GuiAnnouncements = new GuiAnnouncementsConfig
                    {
                        IsGuiAnnouncements = false,
                        BannerColor = "Orange",
                        TextColor = "White",
                        ApiAdjustVPosition = 0.03f
                    },
                    Notify = new NotifyConfig
                    {
                        IsNotify = false,
                        Type = 0
                    },
                    DistanceAlerts = 0f,
                    Discord = new DiscordConfig
                    {
                        IsDiscord = false,
                        WebhookUrl = "https://support.discordapp.com/hc/en-us/articles/228383668-Intro-to-Webhooks",
                        EmbedColor = 13516583,
                        Keys = new HashSet<string>
                        {
                            "PreStart",
                            "Start",
                            "PreFinish",
                            "Finish",
                            "OpenBlueDoor",
                            "OpenRedDoor",
                            "HackCrate"
                        }
                    },
                    Radius = 69f,
                    IsCreateZonePvp = false,
                    PveMode = new PveModeConfig
                    {
                        Pve = false,
                        Damage = 500f,
                        ScaleDamage = new Dictionary<string, float>
                        {
                            ["Npc"] = 1f,
                            ["Turret"] = 2f
                        },
                        LootCrate = false,
                        HackCrate = false,
                        LootNpc = false,
                        DamageNpc = false,
                        DamageTurret = false,
                        TargetNpc = false,
                        TargetTurret = false,
                        CanEnter = false,
                        CanEnterCooldownPlayer = true,
                        TimeExitOwner = 450,
                        AlertTime = 120,
                        RestoreUponDeath = true,
                        CooldownOwner = 86400,
                        Darkening = 12
                    },
                    NTeleportationInterrupt = false,
                    Economy = new EconomyConfig
                    {
                        Plugins = new HashSet<string> { "Economics", "Server Rewards", "IQEconomic", "XPerience" },
                        Min = 0,
                        Crates = new Dictionary<string, double>
                        {
                            ["tech_parts_1"] = 0.1,
                            ["tech_parts_2"] = 0.1,
                            ["crate_ammunition"] = 0.2,
                            ["crate_fuel"] = 0.1,
                            ["crate_tools"] = 0.1,
                            ["crate_underwater_basic"] = 0.2,
                            ["crate_underwater_advanced"] = 0.3,
                            ["crate_medical"] = 0.1,
                            ["crate_elite"] = 0.4,
                            ["crate_food_1"] = 0.1,
                            ["crate_food_2"] = 0.1,
                            ["crate_normal"] = 0.2,
                            ["crate_normal_2"] = 0.1,
                            ["crate_normal_2_food"] = 0.1,
                            ["crate_normal_2_medical"] = 0.1
                        },
                        Doors = new Dictionary<string, double>
                        {
                            ["door.double.hinged.toptier"] = 0.4,
                            ["door.hinged.toptier"] = 0.4,
                            ["wall.frame.cell.gate"] = 0.2
                        },
                        Npc = 0.3,
                        LockedCrate = 0.5,
                        Cards = new Dictionary<string, double>
                        {
                            ["blue"] = 0.4,
                            ["red"] = 0.5
                        },
                        Commands = new HashSet<string>()
                    },
                    Commands = new HashSet<string>
                    {
                        "/remove",
                        "remove.toggle"
                    },
                    Turret = new TurretConfig
                    {
                        IsTurret = true,
                        Hp = 500f,
                        ShortNameWeapon = "rifle.ak",
                        ShortNameAmmo = "ammo.rifle",
                        CountAmmo = 150
                    },
                    ScaleDamage = new Dictionary<string, float>
                    {
                        ["torpedostraight"] = 1f,
                        ["explosive.timed.deployed"] = 2f,
                        ["explosive.satchel.deployed"] = 1f,
                        ["rocket_basic"] = 2f,
                        ["40mm_grenade_he"] = 1f,
                        ["grenade.f1.deployed"] = 1f,
                        ["grenade.beancan.deployed"] = 1f,
                        ["rocket_hv"] = 1f,
                        ["lr300.entity"] = 1f,
                        ["m249.entity"] = 1f,
                        ["ak47u.entity"] = 1f,
                        ["m39.entity"] = 1f,
                        ["semi_auto_rifle.entity"] = 1f
                    },
                    DoorHealth = 800f,
                    DistanceToBlock = 0f,
                    Positions = new HashSet<string>(),
                    IsWorkbench = true,
                    IsRepairBench = true,
                    IsResearchTable = false,
                    IsMixingTable = false,
                    IsLocker = false,
                    IsBoxStorage = false,
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
                ["PreStart"] = "{0} The Submarine will traverse the seas near the island in <color=#55aaff>{1}</color>!",
                ["Start"] = "{0} The Submarine has breached. It's en route to grid <color=#55aaff>{1}</color>\nThe Submarine <color=#ce3f27>will self destruct</color> in <color=#55aaff>{2}</color>!\nCCTVs: <color=#55aaff>Submarine1, Submarine2, Submarine3, Submarine4</color>",
                ["PreFinish"] = "{0} The Submarine <color=#ce3f27>will self destruct</color> in <color=#55aaff>{1}</color>!",
                ["Finish"] = "{0} The Submarine <color=#ce3f27>self destructed</color>!",
                ["SetOwner"] = "{0} Player <color=#55aaff>{1}</color> <color=#738d43>has received</color> the owner status for the <color=#55aaff>Water Event</color>",
                ["EventActive"] = "{0} This event is active now. To finish this event (<color=#55aaff>/waterstop</color>), then (<color=#55aaff>/waterstart</color>) to start the next one!",
                ["EnterPVP"] = "{0} You <color=#ce3f27>have entered</color> the PVP zone, now other players <color=#ce3f27>can damage</color> you!",
                ["ExitPVP"] = "{0} You <color=#738d43>have left</color> the PVP zone, now other players <color=#738d43>cannot damage</color> you!",
                ["OpenBlueDoor"] = "{0} <color=#55aaff>{1}</color> <color=#738d43>has opened</color> The Blue Security Door on The Submarine!",
                ["OpenRedDoor"] = "{0} <color=#55aaff>{1}</color> <color=#738d43>has opened</color> The Red Security Door on The Submarine!",
                ["HackCrate"] = "{0} <color=#55aaff>{1}</color> <color=#738d43>is hacking</color> a locked crate inside The Submarine!",
                ["NTeleportation"] = "{0} You <color=#ce3f27>cannot</color> teleport into the event zone!",
                ["SendEconomy"] = "{0} You <color=#738d43>have earned</color> <color=#55aaff>{1}</color> points in economics for participating in the event",
                ["NoCommand"] = "{0} You <color=#ce3f27>cannot</color> use this command in the event zone!"
            }, this);

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["PreStart"] = "{0} Через <color=#55aaff>{1}</color> подводная лодка с учеными будет проплывать мимо острова!",
                ["Start"] = "{0} Подводная лодка пробита и терпит крушение в квадрате <color=#55aaff>{1}</color>\nЧерез <color=#55aaff>{2}</color> подводная лодка <color=#ce3f27>уничтожится</color>!\nКамеры: <color=#55aaff>Submarine1, Submarine2, Submarine3, Submarine4</color>",
                ["PreFinish"] = "{0} Подводная лодка <color=#ce3f27>уничтожится</color> через <color=#55aaff>{1}</color>!",
                ["Finish"] = "{0} Подводная лодка <color=#ce3f27>уничтожена</color>!",
                ["SetOwner"] = "{0} Игрок <color=#55aaff>{1}</color> <color=#738d43>получил</color> статус владельца ивента для <color=#55aaff>Water Event</color>",
                ["EventActive"] = "{0} Ивент в данный момент активен, сначала завершите текущий ивент (<color=#55aaff>/waterstop</color>), чтобы начать следующий!",
                ["EnterPVP"] = "{0} Вы <color=#ce3f27>вошли</color> в PVP зону, теперь другие игроки <color=#ce3f27>могут</color> наносить вам урон!",
                ["ExitPVP"] = "{0} Вы <color=#738d43>вышли</color> из PVP зоны, теперь другие игроки <color=#738d43>не могут</color> наносить вам урон!",
                ["OpenBlueDoor"] = "{0} <color=#55aaff>{1}</color> <color=#738d43>открыл</color> синюю дверь на подводной лодке!",
                ["OpenRedDoor"] = "{0} <color=#55aaff>{1}</color> <color=#738d43>открыл</color> красную дверь на подводной лодке!",
                ["HackCrate"] = "{0} <color=#55aaff>{1}</color> <color=#738d43>начал</color> взлом заблокированного ящика на подводной лодке!",
                ["NTeleportation"] = "{0} Вы <color=#ce3f27>не можете</color> телепортироваться в зоне ивента!",
                ["SendEconomy"] = "{0} Вы <color=#738d43>получили</color> <color=#55aaff>{1}</color> баллов в экономику за прохождение ивента",
                ["NoCommand"] = "{0} Вы <color=#ce3f27>не можете</color> использовать данную команду в зоне ивента!"
            }, this, "ru");
        }

        private string GetMessage(string langKey, string userId) => lang.GetMessage(langKey, _ins, userId);

        private string GetMessage(string langKey, string userId, params object[] args) => (args.Length == 0) ? GetMessage(langKey, userId) : string.Format(GetMessage(langKey, userId), args);
        #endregion Lang

        #region Data
        public class Prefab { public string Path; public string Pos; public string Rot; public bool IsSubEntity; }

        internal HashSet<Prefab> Prefabs { get; set; } = null;
        internal HashSet<Prefab> Crates { get; set; } = null;

        private void LoadData()
        {
            Prefabs = Interface.Oxide.DataFileSystem.ReadObject<HashSet<Prefab>>("WaterEvent/submarine");
            if (Prefabs == null || Prefabs.Count == 0)
            {
                PrintError("The submarine.json file is empty or it doesn't exist");
                Interface.Oxide.UnloadPlugin(Name);
                return;
            }

            Crates = Interface.Oxide.DataFileSystem.ReadObject<HashSet<Prefab>>("WaterEvent/crates");
            if (Crates == null || Crates.Count == 0)
            {
                PrintError("The crates.json file is empty or it doesn't exist");
                Interface.Oxide.UnloadPlugin(Name);
                return;
            }
        }
        #endregion Data

        #region Oxide Hooks
        private static WaterEvent _ins;

        private void Init()
        {
            _ins = this;
            ToggleHooks(false);
        }

        private void OnServerInitialized()
        {
            LoadData();
            CheckAllLootTables();
            ServerMgr.Instance.StartCoroutine(DownloadImages());
            StartTimer();
        }

        private void Unload()
        {
            if (Controller != null) Finish();
            _ins = null;
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null) return null;

            if (entity.ShortPrefabName.Contains("barrel")) return null;

            if (entity is AutoTurret)
            {
                AutoTurret turret = entity as AutoTurret;
                if (Controller.Turrets.Contains(turret))
                {
                    if (turret.health - info.damageTypes.Total() <= 0f) turret.inventory.ClearItemsContainer();
                    return null;
                }
            }

            if (entity is Door)
            {
                Door door = entity as Door;
                if (Controller.Doors.Contains(door))
                {
                    if (ActivePveMode)
                    {
                        BasePlayer attacker = info.InitiatorPlayer;
                        if (attacker.IsPlayer() && PveMode.Call("CanActionEvent", Name, attacker) != null) return true;
                    }
                    float scale;
                    if (info.WeaponPrefab != null && _config.ScaleDamage.TryGetValue(info.WeaponPrefab.ShortPrefabName, out scale)) info.damageTypes.ScaleAll(scale);
                    return null;
                }
            }

            if (Controller.Entities.Contains(entity)) return true;

            return null;
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (!_config.Marker.Enabled || Controller == null || !player.IsPlayer()) return;
            if (player.HasPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot)) timer.In(2f, () => OnPlayerConnected(player));
            else Controller.UpdateMapMarkers();
        }

        private object OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (player.IsPlayer() && Controller.Players.Contains(player)) Controller.ExitPlayer(player);
            return null;
        }

        private void OnEntityDeath(ScientistNPC npc, HitInfo info)
        {
            if (npc == null || info == null) return;

            if (!Controller.Scientists.Contains(npc)) return;

            BasePlayer attacker = info.InitiatorPlayer;
            if (!attacker.IsPlayer()) return;

            ActionEconomy(attacker.userID, "Npc");
        }

        private void OnEntityDeath(Door door, HitInfo info)
        {
            if (door == null || info == null) return;

            if (!Controller.Doors.Contains(door)) return;

            BasePlayer attacker = info.InitiatorPlayer;
            if (!attacker.IsPlayer()) return;

            ActionEconomy(attacker.userID, "Doors", door.ShortPrefabName);
        }

        private object OnOvenToggle(BaseOven oven, BasePlayer player)
        {
            if (oven != null && Controller.Entities.Contains(oven)) return true;
            else return null;
        }

        private object CanUseWires(BasePlayer player)
        {
            if (player != null && Controller.Players.Contains(player)) return true;
            else return null;
        }

        private object CanBuild(Planner planner, Construction prefab, Construction.Target target)
        {
            if (planner == null) return null;

            BasePlayer player = planner.GetOwnerPlayer();
            if (player == null) return null;

            if (Controller.Players.Contains(player)) return true;
            return null;
        }

        private object CanChangeGrade(BasePlayer player, BuildingBlock block, BuildingGrade.Enum grade, ulong skin)
        {
            if (block != null && Controller.Entities.Contains(block)) return false;
            else return null;
        }

        private object OnStructureRotate(BuildingBlock block, BasePlayer player)
        {
            if (block != null && Controller.Entities.Contains(block)) return true;
            else return null;
        }

        private void OnCardSwipe(CardReader cardReader, Keycard card, BasePlayer player)
        {
            if (cardReader == null || card == null || player == null) return;
            if (cardReader == Controller.CardReaderBlue && !Controller.DoorBlue.IsOpen())
            {
                object hook = Interface.Oxide.CallHook("OnCardSwipeWaterEvent", cardReader, card, player);
                if (cardReader.accessLevel == card.accessLevel || (hook is bool && (bool)hook))
                {
                    if (ActivePveMode && PveMode.Call("CanActionEvent", Name, player) != null) return;
                    Controller.DoorBlue.SetOpen(true);
                    Controller.IsOpenDoorBlueOnce = true;
                    ActionEconomy(player.userID, "Cards", "blue");
                    timer.In(10f, () =>
                    {
                        Controller.DoorBlue.SetOpen(false);
                        cardReader.ResetIOState();
                    });
                    AlertToAllPlayers("OpenBlueDoor", _config.Chat.Prefix, player.displayName);
                }
            }
            else if (cardReader == Controller.CardReaderRed && !Controller.DoorRed.IsOpen())
            {
                object hook = Interface.Oxide.CallHook("OnCardSwipeWaterEvent", cardReader, card, player);
                if (cardReader.accessLevel == card.accessLevel || (hook is bool && (bool)hook))
                {
                    if (ActivePveMode && PveMode.Call("CanActionEvent", Name, player) != null) return;
                    Controller.DoorRed.SetOpen(true);
                    Controller.IsOpenDoorRedOnce = true;
                    ActionEconomy(player.userID, "Cards", "red");
                    timer.In(10f, () =>
                    {
                        Controller.DoorRed.SetOpen(false);
                        cardReader.ResetIOState();
                    });
                    AlertToAllPlayers("OpenRedDoor", _config.Chat.Prefix, player.displayName);
                }
            }
        }

        private void OnButtonPress(PressButton button, BasePlayer player)
        {
            if (button == null || player == null) return;
            if (button == Controller.ButtonBlue && !Controller.DoorBlue.IsOpen())
            {
                if (ActivePveMode && PveMode.Call("CanActionEvent", Name, player) != null) return;
                Controller.DoorBlue.SetOpen(true);
                timer.In(10f, () => Controller.DoorBlue.SetOpen(false));
            }
            else if (button == Controller.ButtonRed && !Controller.DoorBlue.IsOpen())
            {
                if (ActivePveMode && PveMode.Call("CanActionEvent", Name, player) != null) return;
                Controller.DoorRed.SetOpen(true);
                timer.In(10f, () => Controller.DoorRed.SetOpen(false));
            }
        }

        private object OnEntityKill(BaseEntity entity)
        {
            if (entity == null || Controller == null) return null;

            if (entity.ShortPrefabName.Contains("barrel")) return null;

            if (entity is LootContainer)
            {
                LootContainer crate = entity as LootContainer;

                if (crate.net != null && LootableCrates.Contains(crate.net.ID.Value)) LootableCrates.Remove(crate.net.ID.Value);

                if (Controller.Crates.Contains(crate)) Controller.Crates.Remove(crate);
                if (crate is HackableLockedCrate)
                {
                    HackableLockedCrate hackCrate = crate as HackableLockedCrate;
                    if (Controller.HackCrates.Contains(hackCrate)) Controller.HackCrates.Remove(hackCrate);
                }

                return null;
            }

            if (entity is Door)
            {
                Door door = entity as Door;
                if (Controller.Doors.Contains(door))
                {
                    Controller.KillDoor(door.transform.position);
                    Controller.Doors.Remove(door);
                    return null;
                }
            }

            if (entity is AutoTurret)
            {
                AutoTurret turret = entity as AutoTurret;
                if (Controller.Turrets.Contains(turret))
                {
                    Controller.Turrets.Remove(turret);
                    return null;
                }
            }

            if (Controller.Entities.Contains(entity) && !Controller.KillEntities) return true;

            return null;
        }

        private Dictionary<ulong, BasePlayer> StartHackCrates { get; } = new Dictionary<ulong, BasePlayer>();

        private void CanHackCrate(BasePlayer player, HackableLockedCrate crate)
        {
            if (player == null || crate == null) return;
            if (Controller.HackCrates.Contains(crate))
            {
                if (StartHackCrates.ContainsKey(crate.net.ID.Value)) StartHackCrates[crate.net.ID.Value] = player;
                else StartHackCrates.Add(crate.net.ID.Value, player);
            }
        }

        private void OnCrateHack(HackableLockedCrate crate)
        {
            if (crate == null) return;
            ulong crateId = crate.net.ID.Value;
            BasePlayer player;
            if (StartHackCrates.TryGetValue(crateId, out player))
            {
                StartHackCrates.Remove(crateId);
                if (_config.HackCrate.IncreaseEventTime && Controller.TimeToFinish < (int)_config.HackCrate.UnlockTime) Controller.TimeToFinish += (int)_config.HackCrate.UnlockTime;
                ActionEconomy(player.userID, "LockedCrate");
                AlertToAllPlayers("HackCrate", _config.Chat.Prefix, player.displayName);
            }
        }

        private HashSet<ulong> LootableCrates { get; } = new HashSet<ulong>();

        private void OnLootEntity(BasePlayer player, LootContainer container)
        {
            if (!player.IsPlayer() || container == null || LootableCrates.Contains(container.net.ID.Value)) return;
            if (Controller.Crates.Contains(container))
            {
                LootableCrates.Add(container.net.ID.Value);
                ActionEconomy(player.userID, "Crates", container.ShortPrefabName);
                CrateConfig crateConfig = _config.DefaultCrates.FirstOrDefault(x => x.Prefab == container.PrefabName);
                if (crateConfig == null || crateConfig.ScaleItems.Count == 0) return;
                foreach (Item item in container.inventory.itemList)
                {
                    float scale;
                    if (crateConfig.ScaleItems.TryGetValue(item.info.shortname, out scale))
                    {
                        item.amount = (int)(item.amount * scale);
                        item.MarkDirty();
                    }
                }
            }
            else if (container is HackableLockedCrate && Controller.HackCrates.Contains(container as HackableLockedCrate))
            {
                LootableCrates.Add(container.net.ID.Value);
                if (_config.HackCrate.ScaleItems.Count == 0) return;
                foreach (Item item in container.inventory.itemList)
                {
                    float scale;
                    if (_config.HackCrate.ScaleItems.TryGetValue(item.info.shortname, out scale))
                    {
                        item.amount = (int)(item.amount * scale);
                        item.MarkDirty();
                    }
                }
            }
        }

        private object OnEntityEnter(TargetTrigger trigger, BasePlayer target)
        {
            if (trigger == null || target.IsPlayer()) return null;
            AutoTurret attacker = trigger.GetComponentInParent<AutoTurret>();
            if (attacker == null || !Controller.Turrets.Contains(attacker)) return null;
            return true;
        }

        private object OnPlayerCommand(BasePlayer player, string command, string[] args)
        {
            if (player != null && Controller.Players.Contains(player))
            {
                command = "/" + command;
                if (_config.Commands.Contains(command.ToLower()))
                {
                    AlertToPlayer(player, GetMessage("NoCommand", player.UserIDString, _config.Chat.Prefix));
                    return true;
                }
            }
            return null;
        }

        private object OnServerCommand(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.cmd == null) return null;
            BasePlayer player = arg.Player();
            if (player != null && Controller.Players.Contains(player))
            {
                if (_config.Commands.Contains(arg.cmd.Name.ToLower()) || _config.Commands.Contains(arg.cmd.FullName.ToLower()))
                {
                    AlertToPlayer(player, GetMessage("NoCommand", player.UserIDString, _config.Chat.Prefix));
                    return true;
                }
            }
            return null;
        }

        private object OnCargoShipEgress(CargoShip cargo)
        {
            if (cargo == null || Controller == null) return null;

            Vector3 position = Controller.transform.position;
            Vector3 pos1 = cargo.transform.position;
            Vector3 pos2 = pos1 + (pos1 - Vector3.zero).normalized * 10000f;

            float distance1 = Vector3.Distance(position, pos1);
            float distance2 = Vector3.Distance(position, pos2);
            float distance12 = Vector3.Distance(pos1, pos2);

            float p = (distance1 + distance2 + distance12) / 2;

            double distance = (2 / distance12) * Math.Sqrt(p * (p - distance1) * (p - distance2) * (p - distance12));

            if (distance < 152f)
            {
                cargo.egressing = false;
                cargo.Invoke(cargo.StartEgress, 120f);
                return true;
            }
            else return null;
        }
        #endregion Oxide Hooks

        #region Controller
        internal HashSet<Vector3> Marker { get; } = new HashSet<Vector3>
        {
            new Vector3(48f, 0f, 6f),
            new Vector3(48f, 0f, 4f),
            new Vector3(48f, 0f, 2f),
            new Vector3(48f, 0f, 0f),
            new Vector3(48f, 0f, -2f),
            new Vector3(48f, 0f, -4f),
            new Vector3(48f, 0f, -6f),
            new Vector3(46f, 0f, 14f),
            new Vector3(46f, 0f, 12f),
            new Vector3(46f, 0f, 10f),
            new Vector3(46f, 0f, 8f),
            new Vector3(46f, 0f, 6f),
            new Vector3(46f, 0f, 4f),
            new Vector3(46f, 0f, 2f),
            new Vector3(46f, 0f, 0f),
            new Vector3(46f, 0f, -2f),
            new Vector3(46f, 0f, -4f),
            new Vector3(46f, 0f, -6f),
            new Vector3(46f, 0f, -8f),
            new Vector3(46f, 0f, -10f),
            new Vector3(46f, 0f, -12f),
            new Vector3(46f, 0f, -14f),
            new Vector3(44f, 0f, 20f),
            new Vector3(44f, 0f, 18f),
            new Vector3(44f, 0f, 16f),
            new Vector3(44f, 0f, 14f),
            new Vector3(44f, 0f, 12f),
            new Vector3(44f, 0f, 10f),
            new Vector3(44f, 0f, 8f),
            new Vector3(44f, 0f, -8f),
            new Vector3(44f, 0f, -10f),
            new Vector3(44f, 0f, -12f),
            new Vector3(44f, 0f, -14f),
            new Vector3(44f, 0f, -16f),
            new Vector3(44f, 0f, -18f),
            new Vector3(44f, 0f, -20f),
            new Vector3(42f, 0f, 24f),
            new Vector3(42f, 0f, 22f),
            new Vector3(42f, 0f, 20f),
            new Vector3(42f, 0f, 18f),
            new Vector3(42f, 0f, 16f),
            new Vector3(42f, 0f, -16f),
            new Vector3(42f, 0f, -18f),
            new Vector3(42f, 0f, -20f),
            new Vector3(42f, 0f, -22f),
            new Vector3(40f, 0f, 26f),
            new Vector3(40f, 0f, 24f),
            new Vector3(40f, 0f, 22f),
            new Vector3(40f, 0f, 20f),
            new Vector3(40f, 0f, -20f),
            new Vector3(40f, 0f, -22f),
            new Vector3(40f, 0f, -24f),
            new Vector3(40f, 0f, -26f),
            new Vector3(38f, 0f, 30f),
            new Vector3(38f, 0f, 28f),
            new Vector3(38f, 0f, 26f),
            new Vector3(38f, 0f, 24f),
            new Vector3(38f, 0f, -24f),
            new Vector3(38f, 0f, -26f),
            new Vector3(38f, 0f, -28f),
            new Vector3(36f, 0f, 32f),
            new Vector3(36f, 0f, 30f),
            new Vector3(36f, 0f, 28f),
            new Vector3(36f, 0f, -28f),
            new Vector3(36f, 0f, -30f),
            new Vector3(36f, 0f, -32f),
            new Vector3(34f, 0f, 34f),
            new Vector3(34f, 0f, 32f),
            new Vector3(34f, 0f, 30f),
            new Vector3(34f, 0f, -30f),
            new Vector3(34f, 0f, -32f),
            new Vector3(34f, 0f, -34f),
            new Vector3(32f, 0f, 36f),
            new Vector3(32f, 0f, 34f),
            new Vector3(32f, 0f, 32f),
            new Vector3(32f, 0f, -32f),
            new Vector3(32f, 0f, -34f),
            new Vector3(32f, 0f, -36f),
            new Vector3(30f, 0f, 38f),
            new Vector3(30f, 0f, 36f),
            new Vector3(30f, 0f, 34f),
            new Vector3(30f, 0f, -34f),
            new Vector3(30f, 0f, -36f),
            new Vector3(28f, 0f, 38f),
            new Vector3(28f, 0f, 36f),
            new Vector3(28f, 0f, -36f),
            new Vector3(28f, 0f, -38f),
            new Vector3(26f, 0f, 40f),
            new Vector3(26f, 0f, 38f),
            new Vector3(26f, 0f, 2f),
            new Vector3(26f, 0f, 0f),
            new Vector3(26f, 0f, -2f),
            new Vector3(26f, 0f, -4f),
            new Vector3(26f, 0f, -6f),
            new Vector3(26f, 0f, -8f),
            new Vector3(26f, 0f, -10f),
            new Vector3(26f, 0f, -12f),
            new Vector3(26f, 0f, -38f),
            new Vector3(26f, 0f, -40f),
            new Vector3(24f, 0f, 42f),
            new Vector3(24f, 0f, 40f),
            new Vector3(24f, 0f, 6f),
            new Vector3(24f, 0f, 4f),
            new Vector3(24f, 0f, 2f),
            new Vector3(24f, 0f, 0f),
            new Vector3(24f, 0f, -2f),
            new Vector3(24f, 0f, -4f),
            new Vector3(24f, 0f, -6f),
            new Vector3(24f, 0f, -8f),
            new Vector3(24f, 0f, -10f),
            new Vector3(24f, 0f, -12f),
            new Vector3(24f, 0f, -14f),
            new Vector3(24f, 0f, -16f),
            new Vector3(24f, 0f, -18f),
            new Vector3(24f, 0f, -38f),
            new Vector3(24f, 0f, -40f),
            new Vector3(22f, 0f, 42f),
            new Vector3(22f, 0f, 40f),
            new Vector3(22f, 0f, 10f),
            new Vector3(22f, 0f, 8f),
            new Vector3(22f, 0f, 6f),
            new Vector3(22f, 0f, 4f),
            new Vector3(22f, 0f, 2f),
            new Vector3(22f, 0f, 0f),
            new Vector3(22f, 0f, -2f),
            new Vector3(22f, 0f, -4f),
            new Vector3(22f, 0f, -6f),
            new Vector3(22f, 0f, -8f),
            new Vector3(22f, 0f, -10f),
            new Vector3(22f, 0f, -12f),
            new Vector3(22f, 0f, -14f),
            new Vector3(22f, 0f, -16f),
            new Vector3(22f, 0f, -18f),
            new Vector3(22f, 0f, -20f),
            new Vector3(22f, 0f, -40f),
            new Vector3(22f, 0f, -42f),
            new Vector3(20f, 0f, 44f),
            new Vector3(20f, 0f, 42f),
            new Vector3(20f, 0f, 12f),
            new Vector3(20f, 0f, 10f),
            new Vector3(20f, 0f, 8f),
            new Vector3(20f, 0f, 6f),
            new Vector3(20f, 0f, 4f),
            new Vector3(20f, 0f, 2f),
            new Vector3(20f, 0f, 0f),
            new Vector3(20f, 0f, -12f),
            new Vector3(20f, 0f, -14f),
            new Vector3(20f, 0f, -16f),
            new Vector3(20f, 0f, -18f),
            new Vector3(20f, 0f, -20f),
            new Vector3(20f, 0f, -22f),
            new Vector3(20f, 0f, -40f),
            new Vector3(20f, 0f, -42f),
            new Vector3(20f, 0f, -44f),
            new Vector3(18f, 0f, 44f),
            new Vector3(18f, 0f, 42f),
            new Vector3(18f, 0f, 16f),
            new Vector3(18f, 0f, 14f),
            new Vector3(18f, 0f, 12f),
            new Vector3(18f, 0f, 10f),
            new Vector3(18f, 0f, 8f),
            new Vector3(18f, 0f, 6f),
            new Vector3(18f, 0f, 4f),
            new Vector3(18f, 0f, -16f),
            new Vector3(18f, 0f, -18f),
            new Vector3(18f, 0f, -20f),
            new Vector3(18f, 0f, -22f),
            new Vector3(18f, 0f, -24f),
            new Vector3(18f, 0f, -42f),
            new Vector3(18f, 0f, -44f),
            new Vector3(16f, 0f, 46f),
            new Vector3(16f, 0f, 44f),
            new Vector3(16f, 0f, 18f),
            new Vector3(16f, 0f, 16f),
            new Vector3(16f, 0f, 14f),
            new Vector3(16f, 0f, 12f),
            new Vector3(16f, 0f, 10f),
            new Vector3(16f, 0f, 8f),
            new Vector3(16f, 0f, -20f),
            new Vector3(16f, 0f, -22f),
            new Vector3(16f, 0f, -24f),
            new Vector3(16f, 0f, -26f),
            new Vector3(16f, 0f, -42f),
            new Vector3(16f, 0f, -44f),
            new Vector3(14f, 0f, 46f),
            new Vector3(14f, 0f, 44f),
            new Vector3(14f, 0f, 20f),
            new Vector3(14f, 0f, 18f),
            new Vector3(14f, 0f, 16f),
            new Vector3(14f, 0f, 14f),
            new Vector3(14f, 0f, 12f),
            new Vector3(14f, 0f, -22f),
            new Vector3(14f, 0f, -24f),
            new Vector3(14f, 0f, -26f),
            new Vector3(14f, 0f, -28f),
            new Vector3(14f, 0f, -44f),
            new Vector3(14f, 0f, -46f),
            new Vector3(12f, 0f, 46f),
            new Vector3(12f, 0f, 44f),
            new Vector3(12f, 0f, 24f),
            new Vector3(12f, 0f, 22f),
            new Vector3(12f, 0f, 20f),
            new Vector3(12f, 0f, 18f),
            new Vector3(12f, 0f, 16f),
            new Vector3(12f, 0f, 14f),
            new Vector3(12f, 0f, -24f),
            new Vector3(12f, 0f, -26f),
            new Vector3(12f, 0f, -28f),
            new Vector3(12f, 0f, -44f),
            new Vector3(12f, 0f, -46f),
            new Vector3(10f, 0f, 46f),
            new Vector3(10f, 0f, 44f),
            new Vector3(10f, 0f, 26f),
            new Vector3(10f, 0f, 24f),
            new Vector3(10f, 0f, 22f),
            new Vector3(10f, 0f, 20f),
            new Vector3(10f, 0f, 18f),
            new Vector3(10f, 0f, -24f),
            new Vector3(10f, 0f, -26f),
            new Vector3(10f, 0f, -28f),
            new Vector3(10f, 0f, -30f),
            new Vector3(10f, 0f, -44f),
            new Vector3(10f, 0f, -46f),
            new Vector3(8f, 0f, 48f),
            new Vector3(8f, 0f, 46f),
            new Vector3(8f, 0f, 28f),
            new Vector3(8f, 0f, 26f),
            new Vector3(8f, 0f, 24f),
            new Vector3(8f, 0f, 22f),
            new Vector3(8f, 0f, 20f),
            new Vector3(8f, 0f, -26f),
            new Vector3(8f, 0f, -28f),
            new Vector3(8f, 0f, -30f),
            new Vector3(8f, 0f, -44f),
            new Vector3(8f, 0f, -46f),
            new Vector3(6f, 0f, 48f),
            new Vector3(6f, 0f, 46f),
            new Vector3(6f, 0f, 30f),
            new Vector3(6f, 0f, 28f),
            new Vector3(6f, 0f, 26f),
            new Vector3(6f, 0f, 24f),
            new Vector3(6f, 0f, 22f),
            new Vector3(6f, 0f, -26f),
            new Vector3(6f, 0f, -28f),
            new Vector3(6f, 0f, -30f),
            new Vector3(6f, 0f, -32f),
            new Vector3(6f, 0f, -46f),
            new Vector3(6f, 0f, -48f),
            new Vector3(4f, 0f, 48f),
            new Vector3(4f, 0f, 46f),
            new Vector3(4f, 0f, 32f),
            new Vector3(4f, 0f, 30f),
            new Vector3(4f, 0f, 28f),
            new Vector3(4f, 0f, 26f),
            new Vector3(4f, 0f, -26f),
            new Vector3(4f, 0f, -28f),
            new Vector3(4f, 0f, -30f),
            new Vector3(4f, 0f, -32f),
            new Vector3(4f, 0f, -46f),
            new Vector3(4f, 0f, -48f),
            new Vector3(2f, 0f, 48f),
            new Vector3(2f, 0f, 46f),
            new Vector3(2f, 0f, 36f),
            new Vector3(2f, 0f, 34f),
            new Vector3(2f, 0f, 32f),
            new Vector3(2f, 0f, 30f),
            new Vector3(2f, 0f, 28f),
            new Vector3(2f, 0f, -26f),
            new Vector3(2f, 0f, -28f),
            new Vector3(2f, 0f, -30f),
            new Vector3(2f, 0f, -32f),
            new Vector3(2f, 0f, -46f),
            new Vector3(2f, 0f, -48f),
            new Vector3(0f, 0f, 48f),
            new Vector3(0f, 0f, 46f),
            new Vector3(0f, 0f, 36f),
            new Vector3(0f, 0f, 34f),
            new Vector3(0f, 0f, 32f),
            new Vector3(0f, 0f, 30f),
            new Vector3(0f, 0f, -16f),
            new Vector3(0f, 0f, -18f),
            new Vector3(0f, 0f, -20f),
            new Vector3(0f, 0f, -26f),
            new Vector3(0f, 0f, -28f),
            new Vector3(0f, 0f, -30f),
            new Vector3(0f, 0f, -32f),
            new Vector3(0f, 0f, -46f),
            new Vector3(0f, 0f, -48f),
            new Vector3(-2f, 0f, 48f),
            new Vector3(-2f, 0f, 46f),
            new Vector3(-2f, 0f, 36f),
            new Vector3(-2f, 0f, 34f),
            new Vector3(-2f, 0f, 32f),
            new Vector3(-2f, 0f, 30f),
            new Vector3(-2f, 0f, 28f),
            new Vector3(-2f, 0f, -16f),
            new Vector3(-2f, 0f, -18f),
            new Vector3(-2f, 0f, -20f),
            new Vector3(-2f, 0f, -26f),
            new Vector3(-2f, 0f, -28f),
            new Vector3(-2f, 0f, -30f),
            new Vector3(-2f, 0f, -32f),
            new Vector3(-2f, 0f, -46f),
            new Vector3(-2f, 0f, -48f),
            new Vector3(-4f, 0f, 48f),
            new Vector3(-4f, 0f, 46f),
            new Vector3(-4f, 0f, 34f),
            new Vector3(-4f, 0f, 32f),
            new Vector3(-4f, 0f, 30f),
            new Vector3(-4f, 0f, 28f),
            new Vector3(-4f, 0f, 26f),
            new Vector3(-4f, 0f, -16f),
            new Vector3(-4f, 0f, -18f),
            new Vector3(-4f, 0f, -20f),
            new Vector3(-4f, 0f, -26f),
            new Vector3(-4f, 0f, -28f),
            new Vector3(-4f, 0f, -30f),
            new Vector3(-4f, 0f, -32f),
            new Vector3(-4f, 0f, -46f),
            new Vector3(-4f, 0f, -48f),
            new Vector3(-6f, 0f, 48f),
            new Vector3(-6f, 0f, 46f),
            new Vector3(-6f, 0f, 32f),
            new Vector3(-6f, 0f, 30f),
            new Vector3(-6f, 0f, 28f),
            new Vector3(-6f, 0f, 26f),
            new Vector3(-6f, 0f, 24f),
            new Vector3(-6f, 0f, 22f),
            new Vector3(-6f, 0f, -14f),
            new Vector3(-6f, 0f, -16f),
            new Vector3(-6f, 0f, -18f),
            new Vector3(-6f, 0f, -20f),
            new Vector3(-6f, 0f, -26f),
            new Vector3(-6f, 0f, -28f),
            new Vector3(-6f, 0f, -30f),
            new Vector3(-6f, 0f, -32f),
            new Vector3(-6f, 0f, -46f),
            new Vector3(-6f, 0f, -48f),
            new Vector3(-8f, 0f, 48f),
            new Vector3(-8f, 0f, 46f),
            new Vector3(-8f, 0f, 30f),
            new Vector3(-8f, 0f, 28f),
            new Vector3(-8f, 0f, 26f),
            new Vector3(-8f, 0f, 24f),
            new Vector3(-8f, 0f, 22f),
            new Vector3(-8f, 0f, 20f),
            new Vector3(-8f, 0f, -12f),
            new Vector3(-8f, 0f, -14f),
            new Vector3(-8f, 0f, -16f),
            new Vector3(-8f, 0f, -18f),
            new Vector3(-8f, 0f, -20f),
            new Vector3(-8f, 0f, -26f),
            new Vector3(-8f, 0f, -28f),
            new Vector3(-8f, 0f, -30f),
            new Vector3(-8f, 0f, -44f),
            new Vector3(-8f, 0f, -46f),
            new Vector3(-10f, 0f, 46f),
            new Vector3(-10f, 0f, 44f),
            new Vector3(-10f, 0f, 26f),
            new Vector3(-10f, 0f, 24f),
            new Vector3(-10f, 0f, 22f),
            new Vector3(-10f, 0f, 20f),
            new Vector3(-10f, 0f, 18f),
            new Vector3(-10f, 0f, -8f),
            new Vector3(-10f, 0f, -10f),
            new Vector3(-10f, 0f, -12f),
            new Vector3(-10f, 0f, -14f),
            new Vector3(-10f, 0f, -16f),
            new Vector3(-10f, 0f, -18f),
            new Vector3(-10f, 0f, -24f),
            new Vector3(-10f, 0f, -26f),
            new Vector3(-10f, 0f, -28f),
            new Vector3(-10f, 0f, -30f),
            new Vector3(-10f, 0f, -44f),
            new Vector3(-10f, 0f, -46f),
            new Vector3(-12f, 0f, 46f),
            new Vector3(-12f, 0f, 44f),
            new Vector3(-12f, 0f, 24f),
            new Vector3(-12f, 0f, 22f),
            new Vector3(-12f, 0f, 20f),
            new Vector3(-12f, 0f, 18f),
            new Vector3(-12f, 0f, 16f),
            new Vector3(-12f, 0f, 14f),
            new Vector3(-12f, 0f, -6f),
            new Vector3(-12f, 0f, -8f),
            new Vector3(-12f, 0f, -10f),
            new Vector3(-12f, 0f, -12f),
            new Vector3(-12f, 0f, -14f),
            new Vector3(-12f, 0f, -16f),
            new Vector3(-12f, 0f, -24f),
            new Vector3(-12f, 0f, -26f),
            new Vector3(-12f, 0f, -28f),
            new Vector3(-12f, 0f, -30f),
            new Vector3(-12f, 0f, -44f),
            new Vector3(-12f, 0f, -46f),
            new Vector3(-14f, 0f, 46f),
            new Vector3(-14f, 0f, 44f),
            new Vector3(-14f, 0f, 22f),
            new Vector3(-14f, 0f, 20f),
            new Vector3(-14f, 0f, 18f),
            new Vector3(-14f, 0f, 16f),
            new Vector3(-14f, 0f, 14f),
            new Vector3(-14f, 0f, 12f),
            new Vector3(-14f, 0f, -6f),
            new Vector3(-14f, 0f, -8f),
            new Vector3(-14f, 0f, -10f),
            new Vector3(-14f, 0f, -12f),
            new Vector3(-14f, 0f, -14f),
            new Vector3(-14f, 0f, -22f),
            new Vector3(-14f, 0f, -24f),
            new Vector3(-14f, 0f, -26f),
            new Vector3(-14f, 0f, -28f),
            new Vector3(-14f, 0f, -44f),
            new Vector3(-14f, 0f, -46f),
            new Vector3(-16f, 0f, 46f),
            new Vector3(-16f, 0f, 44f),
            new Vector3(-16f, 0f, 18f),
            new Vector3(-16f, 0f, 16f),
            new Vector3(-16f, 0f, 14f),
            new Vector3(-16f, 0f, 12f),
            new Vector3(-16f, 0f, 10f),
            new Vector3(-16f, 0f, 8f),
            new Vector3(-16f, 0f, -6f),
            new Vector3(-16f, 0f, -8f),
            new Vector3(-16f, 0f, -10f),
            new Vector3(-16f, 0f, -20f),
            new Vector3(-16f, 0f, -22f),
            new Vector3(-16f, 0f, -24f),
            new Vector3(-16f, 0f, -26f),
            new Vector3(-16f, 0f, -42f),
            new Vector3(-16f, 0f, -44f),
            new Vector3(-18f, 0f, 44f),
            new Vector3(-18f, 0f, 42f),
            new Vector3(-18f, 0f, 16f),
            new Vector3(-18f, 0f, 14f),
            new Vector3(-18f, 0f, 12f),
            new Vector3(-18f, 0f, 10f),
            new Vector3(-18f, 0f, 8f),
            new Vector3(-18f, 0f, 6f),
            new Vector3(-18f, 0f, -18f),
            new Vector3(-18f, 0f, -20f),
            new Vector3(-18f, 0f, -22f),
            new Vector3(-18f, 0f, -24f),
            new Vector3(-18f, 0f, -26f),
            new Vector3(-18f, 0f, -42f),
            new Vector3(-18f, 0f, -44f),
            new Vector3(-20f, 0f, 44f),
            new Vector3(-20f, 0f, 42f),
            new Vector3(-20f, 0f, 14f),
            new Vector3(-20f, 0f, 12f),
            new Vector3(-20f, 0f, 10f),
            new Vector3(-20f, 0f, 8f),
            new Vector3(-20f, 0f, 6f),
            new Vector3(-20f, 0f, 4f),
            new Vector3(-20f, 0f, 2f),
            new Vector3(-20f, 0f, 0f),
            new Vector3(-20f, 0f, -12f),
            new Vector3(-20f, 0f, -14f),
            new Vector3(-20f, 0f, -16f),
            new Vector3(-20f, 0f, -18f),
            new Vector3(-20f, 0f, -20f),
            new Vector3(-20f, 0f, -22f),
            new Vector3(-20f, 0f, -24f),
            new Vector3(-20f, 0f, -40f),
            new Vector3(-20f, 0f, -42f),
            new Vector3(-20f, 0f, -44f),
            new Vector3(-22f, 0f, 42f),
            new Vector3(-22f, 0f, 40f),
            new Vector3(-22f, 0f, 10f),
            new Vector3(-22f, 0f, 8f),
            new Vector3(-22f, 0f, 6f),
            new Vector3(-22f, 0f, 4f),
            new Vector3(-22f, 0f, 2f),
            new Vector3(-22f, 0f, 0f),
            new Vector3(-22f, 0f, -2f),
            new Vector3(-22f, 0f, -4f),
            new Vector3(-22f, 0f, -6f),
            new Vector3(-22f, 0f, -8f),
            new Vector3(-22f, 0f, -10f),
            new Vector3(-22f, 0f, -12f),
            new Vector3(-22f, 0f, -14f),
            new Vector3(-22f, 0f, -16f),
            new Vector3(-22f, 0f, -18f),
            new Vector3(-22f, 0f, -20f),
            new Vector3(-22f, 0f, -40f),
            new Vector3(-22f, 0f, -42f),
            new Vector3(-24f, 0f, 42f),
            new Vector3(-24f, 0f, 40f),
            new Vector3(-24f, 0f, 6f),
            new Vector3(-24f, 0f, 4f),
            new Vector3(-24f, 0f, 2f),
            new Vector3(-24f, 0f, 0f),
            new Vector3(-24f, 0f, -2f),
            new Vector3(-24f, 0f, -4f),
            new Vector3(-24f, 0f, -6f),
            new Vector3(-24f, 0f, -8f),
            new Vector3(-24f, 0f, -10f),
            new Vector3(-24f, 0f, -12f),
            new Vector3(-24f, 0f, -14f),
            new Vector3(-24f, 0f, -16f),
            new Vector3(-24f, 0f, -18f),
            new Vector3(-24f, 0f, -38f),
            new Vector3(-24f, 0f, -40f),
            new Vector3(-24f, 0f, -42f),
            new Vector3(-26f, 0f, 40f),
            new Vector3(-26f, 0f, 38f),
            new Vector3(-26f, 0f, 2f),
            new Vector3(-26f, 0f, 0f),
            new Vector3(-26f, 0f, -2f),
            new Vector3(-26f, 0f, -4f),
            new Vector3(-26f, 0f, -6f),
            new Vector3(-26f, 0f, -8f),
            new Vector3(-26f, 0f, -10f),
            new Vector3(-26f, 0f, -12f),
            new Vector3(-26f, 0f, -14f),
            new Vector3(-26f, 0f, -38f),
            new Vector3(-26f, 0f, -40f),
            new Vector3(-28f, 0f, 38f),
            new Vector3(-28f, 0f, 36f),
            new Vector3(-28f, 0f, -36f),
            new Vector3(-28f, 0f, -38f),
            new Vector3(-30f, 0f, 38f),
            new Vector3(-30f, 0f, 36f),
            new Vector3(-30f, 0f, 34f),
            new Vector3(-30f, 0f, -34f),
            new Vector3(-30f, 0f, -36f),
            new Vector3(-30f, 0f, -38f),
            new Vector3(-32f, 0f, 36f),
            new Vector3(-32f, 0f, 34f),
            new Vector3(-32f, 0f, -32f),
            new Vector3(-32f, 0f, -34f),
            new Vector3(-32f, 0f, -36f),
            new Vector3(-34f, 0f, 34f),
            new Vector3(-34f, 0f, 32f),
            new Vector3(-34f, 0f, -30f),
            new Vector3(-34f, 0f, -32f),
            new Vector3(-34f, 0f, -34f),
            new Vector3(-36f, 0f, 32f),
            new Vector3(-36f, 0f, 30f),
            new Vector3(-36f, 0f, 28f),
            new Vector3(-36f, 0f, -28f),
            new Vector3(-36f, 0f, -30f),
            new Vector3(-36f, 0f, -32f),
            new Vector3(-38f, 0f, 30f),
            new Vector3(-38f, 0f, 28f),
            new Vector3(-38f, 0f, 26f),
            new Vector3(-38f, 0f, -26f),
            new Vector3(-38f, 0f, -28f),
            new Vector3(-38f, 0f, -30f),
            new Vector3(-40f, 0f, 26f),
            new Vector3(-40f, 0f, 24f),
            new Vector3(-40f, 0f, 22f),
            new Vector3(-40f, 0f, -22f),
            new Vector3(-40f, 0f, -24f),
            new Vector3(-40f, 0f, -26f),
            new Vector3(-42f, 0f, 24f),
            new Vector3(-42f, 0f, 22f),
            new Vector3(-42f, 0f, 20f),
            new Vector3(-42f, 0f, 18f),
            new Vector3(-42f, 0f, -18f),
            new Vector3(-42f, 0f, -20f),
            new Vector3(-42f, 0f, -22f),
            new Vector3(-42f, 0f, -24f),
            new Vector3(-44f, 0f, 20f),
            new Vector3(-44f, 0f, 18f),
            new Vector3(-44f, 0f, 16f),
            new Vector3(-44f, 0f, 14f),
            new Vector3(-44f, 0f, 12f),
            new Vector3(-44f, 0f, 10f),
            new Vector3(-44f, 0f, -10f),
            new Vector3(-44f, 0f, -12f),
            new Vector3(-44f, 0f, -14f),
            new Vector3(-44f, 0f, -16f),
            new Vector3(-44f, 0f, -18f),
            new Vector3(-44f, 0f, -20f),
            new Vector3(-46f, 0f, 16f),
            new Vector3(-46f, 0f, 14f),
            new Vector3(-46f, 0f, 12f),
            new Vector3(-46f, 0f, 10f),
            new Vector3(-46f, 0f, 8f),
            new Vector3(-46f, 0f, 6f),
            new Vector3(-46f, 0f, 4f),
            new Vector3(-46f, 0f, 2f),
            new Vector3(-46f, 0f, 0f),
            new Vector3(-46f, 0f, -2f),
            new Vector3(-46f, 0f, -4f),
            new Vector3(-46f, 0f, -6f),
            new Vector3(-46f, 0f, -8f),
            new Vector3(-46f, 0f, -10f),
            new Vector3(-46f, 0f, -12f),
            new Vector3(-46f, 0f, -14f),
            new Vector3(-46f, 0f, -16f),
            new Vector3(-48f, 0f, 8f),
            new Vector3(-48f, 0f, 6f),
            new Vector3(-48f, 0f, 4f),
            new Vector3(-48f, 0f, 2f),
            new Vector3(-48f, 0f, 0f),
            new Vector3(-48f, 0f, -2f),
            new Vector3(-48f, 0f, -4f),
            new Vector3(-48f, 0f, -6f),
            new Vector3(-48f, 0f, -8f)
        };

        internal HashSet<string> TrashList { get; } = new HashSet<string>
        {
            "rowboat",
            "rhib",
            "tugboat",
            "kayak",
            "submarinesolo.entity",
            "submarineduo.entity",
            "junkpile_water_a",
            "junkpile_water_b",
            "junkpile_water_c",
            "divesite_a",
            "divesite_b",
            "divesite_c"
        };

        private ControllerWaterEvent Controller { get; set; } = null;
        private bool Active { get; set; } = false;

        private void StartTimer()
        {
            if (!_config.EnabledTimer) return;
            timer.In(UnityEngine.Random.Range(_config.MinStartTime, _config.MaxStartTime), () =>
            {
                if (!Active) Start(null);
                else Puts("This event is active now. To finish this event (waterstop), then to start the next one");
            });
        }

        private void Start(BasePlayer player)
        {
            if (!PluginExistsForStart("NpcSpawn")) return;
            CheckVersionPlugin();
            Active = true;
            AlertToAllPlayers("PreStart", _config.Chat.Prefix, GetTimeFormat((int)_config.PreStartTime));
            timer.In(_config.PreStartTime, () =>
            {
                Puts($"{Name} has begun");
                CalculateSpawnPos();
                if (SpawnPos == Vector3.zero)
                {
                    Active = false;
                    Puts($"{Name} has ended");
                    StartTimer();
                    return;
                }
                ToggleHooks(true);
                Controller = new GameObject().AddComponent<ControllerWaterEvent>();
                Controller.Init(player);
                AlertToAllPlayers("Start", _config.Chat.Prefix, MapHelper.GridToString(MapHelper.PositionToGrid(SpawnPos)), GetTimeFormat(_config.FinishTime));
            });
        }

        private void Finish()
        {
            ToggleHooks(false);
            if (ActivePveMode) PveMode.Call("EventRemovePveMode", Name, true);
            if (Controller != null) UnityEngine.Object.Destroy(Controller.gameObject);
            Active = false;
            SpawnPos = Vector3.zero;
            SendBalance();
            LootableCrates.Clear();
            AlertToAllPlayers("Finish", _config.Chat.Prefix);
            Interface.Oxide.CallHook($"On{Name}End");
            Puts($"{Name} has ended");
            StartTimer();
        }

        internal class ControllerWaterEvent : FacepunchBehaviour
        {
            private PluginConfig _config => _ins._config;

            private SphereCollider SphereCollider { get; set; } = null;

            private VendingMachineMapMarker VendingMarker { get; set; } = null;
            private HashSet<MapMarkerGenericRadius> Markers { get; } = new HashSet<MapMarkerGenericRadius>();

            internal bool KillEntities { get; set; } = false;
            internal HashSet<BaseEntity> Entities { get; } = new HashSet<BaseEntity>();

            internal HashSet<Door> Doors { get; } = new HashSet<Door>();
            private HashSet<int> ActivatedDoorsToScientists { get; } = new HashSet<int>();

            private Coroutine SpawnEntitiesCoroutine { get; set; } = null;

            internal HashSet<LootContainer> Crates { get; } = new HashSet<LootContainer>();
            internal HashSet<HackableLockedCrate> HackCrates { get; } = new HashSet<HackableLockedCrate>();

            internal Door DoorBlue { get; set; } = null;
            internal CardReader CardReaderBlue { get; set; } = null;
            internal PressButton ButtonBlue { get; set; } = null;
            internal bool IsOpenDoorBlueOnce { get; set; } = false;

            internal Door DoorRed { get; set; } = null;
            internal CardReader CardReaderRed { get; set; } = null;
            internal PressButton ButtonRed { get; set; } = null;
            internal bool IsOpenDoorRedOnce { get; set; } = false;

            internal HashSet<AutoTurret> Turrets { get; } = new HashSet<AutoTurret>();
            internal HashSet<ScientistNPC> Scientists { get; } = new HashSet<ScientistNPC>();

            internal int TimeToFinish { get; set; } = _ins._config.FinishTime;

            internal HashSet<BasePlayer> Players { get; } = new HashSet<BasePlayer>();
            internal BasePlayer Owner { get; set; } = null;

            private void Awake()
            {
                transform.position = _ins.SpawnPos;

                gameObject.layer = 3;
                SphereCollider = gameObject.AddComponent<SphereCollider>();
                SphereCollider.isTrigger = true;
                SphereCollider.radius = _config.Radius;

                CheckTrash(transform.position, 69f);
            }

            internal void Init(BasePlayer player)
            {
                SpawnEntitiesCoroutine = ServerMgr.Instance.StartCoroutine(SpawnEntities(player));
            }

            private void OnDestroy()
            {
                if (SpawnEntitiesCoroutine != null) ServerMgr.Instance.StopCoroutine(SpawnEntitiesCoroutine);
                CancelInvoke(InvokeUpdates);

                if (SphereCollider != null) Destroy(SphereCollider);

                if (VendingMarker.IsExists()) VendingMarker.Kill();
                foreach (MapMarkerGenericRadius marker in Markers) if (marker.IsExists()) marker.Kill();

                foreach (BasePlayer player in Players) CuiHelper.DestroyUi(player, "Tabs_KpucTaJl");

                foreach (ScientistNPC npc in Scientists) if (npc.IsExists()) npc.Kill();

                foreach (LootContainer crate in Crates) if (crate.IsExists()) crate.Kill();
                foreach (HackableLockedCrate crate in HackCrates) if (crate.IsExists()) crate.Kill();

                KillEntities = true;
                foreach (BaseEntity entity in Entities) if (entity.IsExists()) entity.Kill();
            }

            private void OnTriggerEnter(Collider other) => EnterPlayer(other.GetComponentInParent<BasePlayer>());

            internal void EnterPlayer(BasePlayer player)
            {
                if (!player.IsPlayer()) return;
                if (Players.Contains(player)) return;
                Players.Add(player);
                Interface.Oxide.CallHook($"OnPlayerEnter{_ins.Name}", player);
                if (_config.IsCreateZonePvp) _ins.AlertToPlayer(player, _ins.GetMessage("EnterPVP", player.UserIDString, _config.Chat.Prefix));
                if (_config.Gui.IsGui) UpdateGui(player);
            }

            private void OnTriggerExit(Collider other) => ExitPlayer(other.GetComponentInParent<BasePlayer>());

            internal void ExitPlayer(BasePlayer player)
            {
                if (!player.IsPlayer()) return;
                if (!Players.Contains(player)) return;
                Players.Remove(player);
                Interface.Oxide.CallHook($"OnPlayerExit{_ins.Name}", player);
                if (_config.IsCreateZonePvp) _ins.AlertToPlayer(player, _ins.GetMessage("ExitPVP", player.UserIDString, _config.Chat.Prefix));
                if (_config.Gui.IsGui) CuiHelper.DestroyUi(player, "Tabs_KpucTaJl");
            }

            private void InvokeUpdates()
            {
                if (_config.Gui.IsGui) foreach (BasePlayer player in Players) UpdateGui(player);
                if (_config.Marker.Enabled) UpdateVendingMarker();
                UpdateMarkerForPlayers();
                UpdateTimeToFinish();
            }

            private void UpdateGui(BasePlayer player)
            {
                Dictionary<string, string> dic = new Dictionary<string, string> { ["Clock_KpucTaJl"] = GetTimeFormat(TimeToFinish) };
                if (Crates.Count + HackCrates.Count > 0) dic.Add("Crate_KpucTaJl", $"{Crates.Count + HackCrates.Count}");
                _ins.CreateTabs(player, dic);
            }

            private void SpawnMapMarker(MarkerConfig config)
            {
                if (!config.Enabled) return;

                MapMarkerGenericRadius background = GameManager.server.CreateEntity("assets/prefabs/tools/map/genericradiusmarker.prefab", transform.position) as MapMarkerGenericRadius;
                background.Spawn();
                background.radius = config.Type == 0 ? config.Radius : 0.37967f;
                background.alpha = config.Alpha;
                background.color1 = new Color(config.Color.R, config.Color.G, config.Color.B);
                background.color2 = new Color(config.Color.R, config.Color.G, config.Color.B);
                Markers.Add(background);

                if (config.Type == 1)
                {
                    foreach (Vector3 pos in _ins.Marker)
                    {
                        MapMarkerGenericRadius marker = GameManager.server.CreateEntity("assets/prefabs/tools/map/genericradiusmarker.prefab", transform.position + pos) as MapMarkerGenericRadius;
                        marker.Spawn();
                        marker.radius = 0.008f;
                        marker.alpha = 1f;
                        marker.color1 = new Color(config.Color.R, config.Color.G, config.Color.B);
                        marker.color2 = new Color(config.Color.R, config.Color.G, config.Color.B);
                        Markers.Add(marker);
                    }
                }

                VendingMarker = GameManager.server.CreateEntity("assets/prefabs/deployable/vendingmachine/vending_mapmarker.prefab", transform.position) as VendingMachineMapMarker;
                VendingMarker.Spawn();

                UpdateVendingMarker();
                UpdateMapMarkers();
            }

            private void UpdateVendingMarker()
            {
                VendingMarker.markerShopName = $"{_config.Marker.Text}\n{GetTimeFormat(TimeToFinish)}";
                if (_ins.ActivePveMode) VendingMarker.markerShopName += Owner == null ? "\nNo Owner" : $"\n{Owner.displayName}";
                VendingMarker.SendNetworkUpdate();
            }

            internal void UpdateMapMarkers() { foreach (MapMarkerGenericRadius marker in Markers) marker.SendUpdate(); }

            private void UpdateMarkerForPlayers()
            {
                if (Players.Count == 0) return;

                if (_config.MainPoint.Enabled)
                {
                    HashSet<Vector3> points = new HashSet<Vector3>();
                    if (!IsOpenDoorBlueOnce && DoorBlue.IsExists()) points.Add(DoorBlue.transform.position);
                    if (!IsOpenDoorRedOnce && DoorRed.IsExists()) points.Add(DoorRed.transform.position);
                    foreach (HackableLockedCrate hackCrate in HackCrates) if (hackCrate.IsExists()) points.Add(hackCrate.transform.position);
                    if (points.Count > 0)
                    {
                        foreach (BasePlayer player in Players)
                        {
                            Door door = Doors.Min(x => Vector3.Distance(player.transform.position, x.transform.position));
                            if (door.IsExists()) points.Add(door.transform.position);
                            foreach (Vector3 point in points) UpdateMarkerForPlayer(player, point, _config.MainPoint);
                        }
                    }
                    points = null;
                }

                if (_config.AdditionalPoint.Enabled && Crates.Count > 0)
                {
                    HashSet<Vector3> points = new HashSet<Vector3>();
                    foreach (LootContainer crate in Crates) if (crate.IsExists()) points.Add(crate.transform.position);
                    if (points.Count > 0) foreach (BasePlayer player in Players) foreach (Vector3 point in points) UpdateMarkerForPlayer(player, point, _config.AdditionalPoint);
                    points = null;
                }
            }

            private void UpdateTimeToFinish()
            {
                TimeToFinish--;
                if ((TimeToFinish == _config.PreFinishTime || Crates.Count + HackCrates.Count == 0) && TimeToFinish >= _config.PreFinishTime)
                {
                    TimeToFinish = _config.PreFinishTime;
                    _ins.AlertToAllPlayers("PreFinish", _config.Chat.Prefix, GetTimeFormat(_config.PreFinishTime));
                }
                else if (TimeToFinish == 0)
                {
                    CancelInvoke(InvokeUpdates);
                    _ins.Finish();
                }
            }

            private Vector3 GetGlobalPosition(Vector3 localPosition) => transform.TransformPoint(localPosition);

            private Quaternion GetGlobalRotation(Vector3 local) => transform.rotation * Quaternion.Euler(local);

            private Vector3 GetLocalPosition(Vector3 global) => transform.InverseTransformPoint(global);

            private static HashSet<T> GetEntities<T>(Vector3 position, float radius, int layerMask) where T : BaseEntity
            {
                HashSet<T> result = new HashSet<T>();
                List<T> list = Pool.Get<List<T>>();
                Vis.Entities<T>(position, radius, list, layerMask);
                foreach (T entity in list) result.Add(entity);
                Pool.FreeUnmanaged(ref list);
                return result;
            }

            private static void CheckTrash(Vector3 pos, float radius) { foreach (BaseEntity entity in GetEntities<BaseEntity>(pos, radius, -1)) if (_ins.TrashList.Contains(entity.ShortPrefabName) && entity.IsExists()) entity.Kill(); }

            private IEnumerator SpawnEntities(BasePlayer player)
            {
                int countCctv = 0;
                foreach (Prefab prefab in _ins.Prefabs)
                {
                    if (prefab.Path == "assets/prefabs/npc/autoturret/autoturret_deployed.prefab" && !_config.Turret.IsTurret) continue;

                    Vector3 localPos = prefab.Pos.ToVector3();
                    Vector3 pos = GetGlobalPosition(localPos);
                    Quaternion rot = GetGlobalRotation(prefab.Rot.ToVector3());

                    if (prefab.Path == "assets/prefabs/io/electric/switches/cardreader.prefab")
                    {
                        CardReader reader = GameManager.server.CreateEntity(prefab.Path, pos, rot) as CardReader;
                        reader.enableSaving = false;
                        if (Vector3.Distance(localPos, new Vector3(1.538f, 3f, 16.985f)) < 1f)
                        {
                            reader.accessLevel = 2;
                            CardReaderBlue = reader;
                        }
                        else
                        {
                            reader.accessLevel = 3;
                            CardReaderRed = reader;
                        }
                        reader.Spawn();
                        reader.UpdateFromInput(1, 0);
                        Entities.Add(reader);
                        yield return CoroutineEx.waitForSeconds(_config.Delay);
                        continue;
                    }

                    BaseEntity entity = prefab.IsSubEntity ? SpawnSubEntities(prefab.Path, pos, rot) : SpawnEntity(prefab.Path, pos, rot);
                    entity.enableSaving = false;

                    if (entity is BuildingBlock)
                    {
                        BuildingBlock buildingBlock = entity as BuildingBlock;
                        buildingBlock.ChangeGradeAndSkin(BuildingGrade.Enum.TopTier, 0);
                    }

                    if (entity is Door)
                    {
                        Door door = entity as Door;
                        switch (door.ShortPrefabName)
                        {
                            case "door.hinged.security.blue":
                                DoorBlue = door;
                                break;
                            case "door.hinged.security.red":
                                DoorRed = door;
                                break;
                            case "door.hinged.toptier":
                            case "door.double.hinged.toptier":
                                door.canTakeCloser = false;
                                door.canTakeKnocker = false;
                                door.canTakeLock = false;
                                door.canHandOpen = false;
                                door.hasHatch = false;
                                door.InitializeHealth(_config.DoorHealth, _config.DoorHealth);
                                Doors.Add(door);
                                break;
                            default:
                                door.SetOpen(true);
                                break;
                        }
                    }

                    if (entity is Workbench && !_config.IsWorkbench)
                    {
                        (entity as Workbench).Workbenchlevel = 0;
                        entity.SetFlag(BaseEntity.Flags.Busy, true);
                    }

                    if (entity is RepairBench && !_config.IsRepairBench) entity.SetFlag(BaseEntity.Flags.Busy, true);

                    if (entity is ResearchTable && !_config.IsResearchTable) entity.SetFlag(BaseEntity.Flags.Busy, true);

                    if (entity is MixingTable && !_config.IsMixingTable) entity.SetFlag(BaseEntity.Flags.Busy, true);

                    if (entity is Locker)
                    {
                        if (_config.IsLocker)
                        {
                            EntityCrateConfig crateConfig = _config.EntityCrates.FirstOrDefault(x => x.Prefab == prefab.Path);
                            if (crateConfig != null && crateConfig.IsOwnLootTable)
                            {
                                Locker locker = entity as Locker;
                                _ins.NextTick(() =>
                                {
                                    foreach (ItemConfig item in crateConfig.LootTable.Items)
                                    {
                                        if (UnityEngine.Random.Range(0f, 100f) > item.Chance) continue;
                                        int amount = UnityEngine.Random.Range(item.MinAmount, item.MaxAmount + 1);
                                        Item newItem = item.IsBluePrint ? ItemManager.CreateByName("blueprintbase") : ItemManager.CreateByName(item.ShortName, amount, item.SkinId);
                                        if (item.IsBluePrint) newItem.blueprintTarget = ItemManager.FindItemDefinition(item.ShortName).itemid;
                                        if (item.Name != string.Empty) newItem.name = item.Name;
                                        int targetPos = locker.GetIdealSlot(null, locker.inventory, newItem);
                                        if (!newItem.MoveToContainer(locker.inventory, targetPos)) newItem.Remove();
                                    }
                                });
                            }
                        }
                        else entity.SetFlag(BaseEntity.Flags.Busy, true);
                    }

                    if (entity is BoxStorage)
                    {
                        if (_config.IsBoxStorage)
                        {
                            EntityCrateConfig crateConfig = _config.EntityCrates.FirstOrDefault(x => x.Prefab == prefab.Path);
                            if (crateConfig != null && crateConfig.IsOwnLootTable)
                            {
                                BoxStorage storage = entity as BoxStorage;
                                _ins.NextTick(() => _ins.AddToContainerItem(storage.inventory, crateConfig.LootTable));
                            }
                        }
                        else entity.SetFlag(BaseEntity.Flags.Busy, true);
                    }

                    if (entity is BaseFuelLightSource)
                    {
                        entity.SetFlag(BaseEntity.Flags.On, true);
                        entity.SetFlag(BaseEntity.Flags.Busy, true);
                    }

                    if (entity is CCTV_RC)
                    {
                        countCctv++;
                        CCTV_RC cctv = entity as CCTV_RC;
                        cctv.UpdateFromInput(5, 0);
                        cctv.rcIdentifier = $"Submarine{countCctv}";
                    }

                    if (entity is SkullTrophy)
                    {
                        Item item = ItemManager.CreateByName("skull.human");
                        int number = UnityEngine.Random.Range(0, 100);
                        item.name = number < 50 ? "SKULL OF \"KpucTaJl\"" : number > 75 ? "SKULL OF \"Gruber\"" : "SKULL OF \"Jtedal\"";
                        item.MoveToContainer((entity as SkullTrophy).inventory);
                        entity.SetFlag(BaseEntity.Flags.Busy, true);
                    }

                    if (entity is ElectricalHeater)
                    {
                        ElectricalHeater heater = entity as ElectricalHeater;
                        heater.UpdateFromInput(3, 0);
                        heater.SetFlag(BaseEntity.Flags.Busy, true);
                    }

                    if (entity is AudioAlarm)
                    {
                        AudioAlarm alarm = entity as AudioAlarm;
                        alarm.UpdateFromInput(1, 0);
                        alarm.SetFlag(BaseEntity.Flags.Busy, true);
                    }

                    if (entity is SirenLight)
                    {
                        SirenLight siren = entity as SirenLight;
                        siren.UpdateFromInput(1, 0);
                        siren.SetFlag(BaseEntity.Flags.Busy, true);
                    }

                    if (entity is PressButton)
                    {
                        if (Vector3.Distance(localPos, new Vector3(1.466f, 2.859f, 17.164f)) < 1f) ButtonBlue = entity as PressButton;
                        else ButtonRed = entity as PressButton;
                    }

                    if (entity is ComputerStation)
                    {
                        ComputerStation computer = entity as ComputerStation;
                        for (int i = 1; i <= 4; i++) computer.ForceAddBookmark($"Submarine{i}");
                    }

                    if (entity is AutoTurret)
                    {
                        AutoTurret turret = entity as AutoTurret;
                        turret.inventory.Insert(ItemManager.CreateByName(_config.Turret.ShortNameWeapon));
                        turret.inventory.Insert(ItemManager.CreateByName(_config.Turret.ShortNameAmmo, _config.Turret.CountAmmo));
                        turret.SendNetworkUpdate();
                        turret.UpdateFromInput(10, 0);
                        turret.InitializeHealth(_config.Turret.Hp, _config.Turret.Hp);
                        Turrets.Add(turret);
                    }

                    Entities.Add(entity);

                    yield return CoroutineEx.waitForSeconds(_config.Delay);
                }

                SpawnCrates();
                SpawnHackCrates();

                foreach (KeyValuePair<string, SpawnInfoNpc> dic in _config.OutsideNpc) SpawnPreset(dic.Key, dic.Value, "WaterEvent_Outside");

                Interface.Oxide.CallHook($"On{_ins.Name}Start", Entities, transform.position, _config.Radius);

                EnablePveMode(_config.PveMode, player);

                SpawnMapMarker(_config.Marker);

                InvokeRepeating(InvokeUpdates, 0f, 1f);
            }

            private void SpawnCrates()
            {
                foreach (Prefab prefab in _ins.Crates)
                {
                    LootContainer crate = GameManager.server.CreateEntity(prefab.Path, GetGlobalPosition(prefab.Pos.ToVector3()), GetGlobalRotation(prefab.Rot.ToVector3())) as LootContainer;
                    crate.enableSaving = false;
                    crate.Spawn();

                    Crates.Add(crate);

                    CrateConfig config = _config.DefaultCrates.FirstOrDefault(x => x.Prefab == prefab.Path);
                    if (config == null) return;

                    if (config.TypeLootTable == 1 || config.TypeLootTable == 4 || config.TypeLootTable == 5)
                    {
                        _ins.NextTick(() =>
                        {
                            crate.inventory.ClearItemsContainer();
                            if (config.TypeLootTable == 4 || config.TypeLootTable == 5) _ins.AddToContainerPrefab(crate.inventory, config.PrefabLootTable);
                            if (config.TypeLootTable == 1 || config.TypeLootTable == 5) _ins.AddToContainerItem(crate.inventory, config.OwnLootTable);
                        });
                    }
                }
            }

            private void SpawnHackCrates()
            {
                foreach (LocationConfig location in _config.HackCrate.Coordinates)
                {
                    HackableLockedCrate hackCrate = GameManager.server.CreateEntity("assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab", GetGlobalPosition(location.Position.ToVector3()), GetGlobalRotation(location.Rotation.ToVector3())) as HackableLockedCrate;
                    hackCrate.enableSaving = false;
                    hackCrate.Spawn();

                    hackCrate.hackSeconds = HackableLockedCrate.requiredHackSeconds - _config.HackCrate.UnlockTime;

                    hackCrate.shouldDecay = false;
                    hackCrate.CancelInvoke(hackCrate.DelayedDestroy);

                    hackCrate.KillMapMarker();

                    HackCrates.Add(hackCrate);

                    if (_config.HackCrate.TypeLootTable is 1 or 4 or 5)
                    {
                        _ins.NextTick(() =>
                        {
                            hackCrate.inventory.ClearItemsContainer();
                            if (_config.HackCrate.TypeLootTable is 4 or 5) _ins.AddToContainerPrefab(hackCrate.inventory, _config.HackCrate.PrefabLootTable);
                            if (_config.HackCrate.TypeLootTable is 1 or 5) _ins.AddToContainerItem(hackCrate.inventory, _config.HackCrate.OwnLootTable);
                        });
                    }
                }
            }

            private void SpawnPreset(string shortname, SpawnInfoNpc spawnInfoNpc, string navmesh)
            {
                DoorCloser closer = GameManager.server.CreateEntity("assets/prefabs/misc/doorcloser/doorcloser.prefab", transform.position, transform.rotation) as DoorCloser;
                closer.enableSaving = false;
                closer.Spawn();

                int count = UnityEngine.Random.Range(spawnInfoNpc.Min, spawnInfoNpc.Max + 1);

                List<Vector3> positions = Pool.Get<List<Vector3>>();
                foreach (string pos in spawnInfoNpc.Positions) positions.Add(GetGlobalPosition(pos.ToVector3()));

                JObject objectConfig = GetObjectConfig(_config.Npc[shortname]);

                for (int i = 0; i < count; i++)
                {
                    Vector3 pos = positions.GetRandom();
                    positions.Remove(pos);

                    ScientistNPC npc = (ScientistNPC)_ins.NpcSpawn.Call("SpawnNpc", pos, objectConfig);
                    _ins.NpcSpawn.Call("SetCustomNavMesh", npc, closer.transform, navmesh);

                    Scientists.Add(npc);
                }

                Pool.FreeUnmanaged(ref positions);
                if (closer.IsExists()) closer.Kill();
            }

            private static JObject GetObjectConfig(NpcConfig config)
            {
                HashSet<string> states = config.Stationary ? new HashSet<string> { "IdleState", "CombatStationaryState" } : new HashSet<string> { "RoamState", "ChaseState", "CombatState" };
                if (config.BeltItems.Any(x => x.ShortName == "rocket.launcher" || x.ShortName == "explosive.timed")) states.Add("RaidState");
                return new JObject
                {
                    ["Name"] = config.Name,
                    ["WearItems"] = new JArray { config.WearItems.Select(x => new JObject { ["ShortName"] = x.ShortName, ["SkinID"] = x.SkinId }) },
                    ["BeltItems"] = new JArray { config.BeltItems.Select(x => new JObject { ["ShortName"] = x.ShortName, ["Amount"] = x.Amount, ["SkinID"] = x.SkinId, ["Mods"] = new JArray { x.Mods }, ["Ammo"] = x.Ammo }) },
                    ["Kit"] = config.Kit,
                    ["Health"] = config.Health,
                    ["RoamRange"] = config.RoamRange,
                    ["ChaseRange"] = config.ChaseRange,
                    ["SenseRange"] = config.SenseRange,
                    ["ListenRange"] = config.SenseRange / 2f,
                    ["AttackRangeMultiplier"] = config.AttackRangeMultiplier,
                    ["CheckVisionCone"] = config.CheckVisionCone,
                    ["VisionCone"] = config.VisionCone,
                    ["DamageScale"] = config.DamageScale,
                    ["TurretDamageScale"] = 0f,
                    ["AimConeScale"] = config.AimConeScale,
                    ["DisableRadio"] = config.DisableRadio,
                    ["CanRunAwayWater"] = false,
                    ["CanSleep"] = false,
                    ["SleepDistance"] = 100f,
                    ["Speed"] = config.Speed,
                    ["AreaMask"] = 1,
                    ["AgentTypeID"] = -1372625422,
                    ["HomePosition"] = string.Empty,
                    ["MemoryDuration"] = config.MemoryDuration,
                    ["States"] = new JArray { states }
                };
            }

            internal void KillDoor(Vector3 globalPosDoor)
            {
                Vector3 localPosDoor = GetLocalPosition(globalPosDoor);
                DoorsToScientists config = _config.InsideNpc.FirstOrDefault(x => !ActivatedDoorsToScientists.Contains(x.GetHashCode()) && x.Doors.Any(y => Vector3.Distance(y.ToVector3(), localPosDoor) < 1f));
                if (config == null) return;
                foreach (KeyValuePair<string, SpawnInfoNpc> dic in config.Scientists) SpawnPreset(dic.Key, dic.Value, localPosDoor.y > 2.5f ? "WaterEvent_Floor2" : "WaterEvent_Floor1");
                if (_ins.ActivePveMode) _ins.PveMode.Call("EventAddScientists", _ins.Name, Scientists.Select(x => x.net.ID.Value));
                ActivatedDoorsToScientists.Add(config.GetHashCode());
            }

            private void EnablePveMode(PveModeConfig config, BasePlayer player)
            {
                if (!_ins.ActivePveMode) return;

                Dictionary<string, object> dic = new Dictionary<string, object>
                {
                    ["Damage"] = config.Damage,
                    ["ScaleDamage"] = config.ScaleDamage,
                    ["LootCrate"] = config.LootCrate,
                    ["HackCrate"] = config.HackCrate,
                    ["LootNpc"] = config.LootNpc,
                    ["DamageNpc"] = config.DamageNpc,
                    ["DamageTank"] = false,
                    ["DamageHelicopter"] = false,
                    ["DamageTurret"] = config.DamageTurret,
                    ["TargetNpc"] = config.TargetNpc,
                    ["TargetTank"] = false,
                    ["TargetHelicopter"] = false,
                    ["TargetTurret"] = config.TargetTurret,
                    ["CanEnter"] = config.CanEnter,
                    ["CanEnterCooldownPlayer"] = config.CanEnterCooldownPlayer,
                    ["TimeExitOwner"] = config.TimeExitOwner,
                    ["AlertTime"] = config.AlertTime,
                    ["RestoreUponDeath"] = config.RestoreUponDeath,
                    ["CooldownOwner"] = config.CooldownOwner,
                    ["Darkening"] = config.Darkening
                };

                HashSet<ulong> crates = Crates.Select(x => x.net.ID.Value);
                foreach (HackableLockedCrate crate in HackCrates) crates.Add(crate.net.ID.Value);

                _ins.PveMode.Call("EventAddPveMode", _ins.Name, dic, transform.position, _config.Radius, crates, Scientists.Select(x => x.net.ID.Value), new HashSet<ulong>(), new HashSet<ulong>(), Turrets.Select(x => x.net.ID.Value), new HashSet<ulong>(), player);
            }
        }
        #endregion Controller

        #region Position Submarine
        private const int BlockedTopology = (int)(TerrainTopology.Enum.Oceanside | TerrainTopology.Enum.Monument | TerrainTopology.Enum.Building | TerrainTopology.Enum.Cliff | TerrainTopology.Enum.Cliffside | TerrainTopology.Enum.Clutter | TerrainTopology.Enum.Decor);
        private const int AvailableTopology = (int)TerrainTopology.Enum.Ocean;

        private static bool IsAvailableTopology(Vector3 position)
        {
            int topology = TerrainMeta.TopologyMap.GetTopology(position);
            return (topology & AvailableTopology) != 0 && (topology & BlockedTopology) == 0;
        }

        private static bool IsValidBiome(Vector3 position) => (TerrainBiome.Enum)TerrainMeta.BiomeMap.GetBiomeMaxType(position) != TerrainBiome.Enum.Arctic;

        private static bool IsValidHeight(Vector3 position) => TerrainMeta.HeightMap.GetHeight(position) <= -10f;

        private const int EntityLayers = 1 << 8 | 1 << 17 | 1 << 21;

        private bool IsEntities(Vector3 position)
        {
            List<BaseEntity> list = Pool.Get<List<BaseEntity>>();
            Vis.Entities(position, 69f + _config.DistanceToBlock, list, EntityLayers);
            bool hasEntity = list.Count > 0;
            Pool.FreeUnmanaged(ref list);
            return hasEntity;
        }

        private static bool IsOilRigs(Vector3 position, Dictionary<Vector3, float> oilRigs) => oilRigs.Any(x => Vector3.Distance(x.Key, position) < x.Value + 69f);

        internal Vector3 SpawnPos { get; set; } = Vector3.zero;

        private void CalculateSpawnPos()
        {
            List<Vector3> list = Pool.Get<List<Vector3>>();

            if (_config.Positions.Count > 0) foreach (string pos in _config.Positions) list.Add(pos.ToVector3());
            else
            {
                Dictionary<Vector3, float> oilRigs = new Dictionary<Vector3, float>();
                foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
                {
                    if (monument.name.Contains("OilrigAI2")) oilRigs.Add(monument.transform.position, 85f);
                    else if (monument.name.Contains("OilrigAI")) oilRigs.Add(monument.transform.position, 60f);
                }

                for (int attempt = 0; attempt < 1000; attempt++)
                {
                    Vector3 center = new Vector3(UnityEngine.Random.Range(-World.Size * 0.5f, World.Size * 0.5f), 0.5f, UnityEngine.Random.Range(-World.Size * 0.5f, World.Size * 0.5f));

                    if (!IsAvailableTopology(center) || !IsValidBiome(center) || !IsValidHeight(center)) continue;
                    if (IsOilRigs(center, oilRigs)) continue;
                    if (IsEntities(center)) continue;
                    if (GetNearDistanceToCargoPath(center) < 152f) continue;

                    bool isContinue = false;
                    for (int i = 1; i <= 12; i++)
                    {
                        for (float k = 0.1f; k <= 1f; k += 0.1f)
                        {
                            Vector3 pos = new Vector3(center.x + 69f * k * Mathf.Sin(i * 30f * Mathf.Deg2Rad), 0.5f, center.z + 69f * k * Mathf.Cos(i * 30f * Mathf.Deg2Rad));
                            if (IsAvailableTopology(pos) && IsValidBiome(pos) && IsValidHeight(pos)) continue;
                            isContinue = true;
                            break;
                        }
                        if (isContinue) break;
                    }
                    if (isContinue) continue;

                    list.Add(center);
                }
            }

            SpawnPos = list.Count > 0 ? list.GetRandom() : Vector3.zero;

            Pool.FreeUnmanaged(ref list);
        }

        private static int GetNearIndexPathCargo(Vector3 position)
        {
            int index = 0;
            float distance = float.MaxValue;
            for (int i = 0; i < TerrainMeta.Path.OceanPatrolFar.Count; i++)
            {
                Vector3 vector3 = TerrainMeta.Path.OceanPatrolFar[i];
                float single = Vector3.Distance(position, vector3);
                if (single < distance)
                {
                    index = i;
                    distance = single;
                }
            }
            return index;
        }

        private static double GetDistanceToCargoPath(Vector3 position, int index1, int index2)
        {
            Vector3 pos1 = TerrainMeta.Path.OceanPatrolFar[index1];
            Vector3 pos2 = TerrainMeta.Path.OceanPatrolFar[index2];

            float distance1 = Vector3.Distance(position, pos1);
            float distance2 = Vector3.Distance(position, pos2);
            float distance12 = Vector3.Distance(pos1, pos2);

            float p = (distance1 + distance2 + distance12) / 2;

            return (2 / distance12) * Math.Sqrt(p * (p - distance1) * (p - distance2) * (p - distance12));
        }

        private static double GetNearDistanceToCargoPath(Vector3 position)
        {
            int index = GetNearIndexPathCargo(position);
            int indexNext = TerrainMeta.Path.OceanPatrolFar.Count - 1 == index ? 0 : index + 1;
            int indexPrevious = index == 0 ? TerrainMeta.Path.OceanPatrolFar.Count - 1 : index - 1;

            double distanceNext = GetDistanceToCargoPath(position, index, indexNext);
            double distancePrevious = GetDistanceToCargoPath(position, indexPrevious, index);

            return distanceNext < distancePrevious ? distanceNext : distancePrevious;
        }
        #endregion Position Submarine

        #region Spawn Loot
        #region NPC
        private void OnCorpsePopulate(ScientistNPC entity, NPCPlayerCorpse corpse)
        {
            if (entity == null) return;

            if (!Controller.Scientists.Contains(entity)) return;
            Controller.Scientists.Remove(entity);

            NpcConfig preset = GetNpcConfig(entity.displayName);
            if (preset == null) return;

            NextTick(() =>
            {
                if (corpse == null) return;
                ItemContainer container = corpse.containers[0];
                if (preset.TypeLootTable == 1 || preset.TypeLootTable == 4 || preset.TypeLootTable == 5)
                {
                    container.ClearItemsContainer();
                    if (preset.TypeLootTable == 4 || preset.TypeLootTable == 5) AddToContainerPrefab(container, preset.PrefabLootTable);
                    if (preset.TypeLootTable == 1 || preset.TypeLootTable == 5) AddToContainerItem(container, preset.OwnLootTable);
                }
                if (preset.IsRemoveCorpse && corpse.IsExists()) corpse.Kill();
            });
        }

        private object CanPopulateLoot(ScientistNPC entity, NPCPlayerCorpse corpse)
        {
            if (entity == null || Controller == null) return null;

            if (!Controller.Scientists.Contains(entity)) return null;

            NpcConfig preset = GetNpcConfig(entity.displayName);
            if (preset == null) return true;

            if (preset.TypeLootTable == 2) return null;
            else return true;
        }

        private object OnCustomLootNPC(NetworkableId netId)
        {
            if (Controller == null) return null;

            ScientistNPC entity = Controller.Scientists.FirstOrDefault(x => x.IsExists() && x.net.ID.Value == netId.Value);
            if (entity == null) return null;

            NpcConfig preset = GetNpcConfig(entity.displayName);
            if (preset == null) return true;

            if (preset.TypeLootTable == 3) return null;
            else return true;
        }

        private NpcConfig GetNpcConfig(string name) => _config.Npc.Values.FirstOrDefault(x => x.Name == name);
        #endregion NPC

        #region Crates
        private object CanPopulateLoot(LootContainer container)
        {
            if (container == null || Controller == null) return null;

            if (Controller.Crates.Contains(container))
            {
                CrateConfig crateConfig = _config.DefaultCrates.FirstOrDefault(x => x.Prefab == container.PrefabName);
                if (crateConfig == null) return true;
                if (crateConfig.TypeLootTable == 2) return null;
                else return true;
            }

            HackableLockedCrate lockedCrate = container as HackableLockedCrate;
            if (lockedCrate != null && Controller.HackCrates.Contains(lockedCrate))
            {
                if (_config.HackCrate.TypeLootTable == 2) return null;
                else return true;
            }

            return null;
        }

        private object OnCustomLootContainer(NetworkableId netId)
        {
            if (Controller == null) return null;

            LootContainer crate = Controller.Crates.FirstOrDefault(x => x.IsExists() && x.net.ID.Value == netId.Value);
            if (crate != null)
            {
                CrateConfig crateConfig = _config.DefaultCrates.FirstOrDefault(x => x.Prefab == crate.PrefabName);
                if (crateConfig == null) return true;
                if (crateConfig.TypeLootTable == 3) return null;
                else return true;
            }

            if (Controller.HackCrates.Any(x => x.IsExists() && x.net.ID.Value == netId.Value))
            {
                if (_config.HackCrate.TypeLootTable == 3) return null;
                else return true;
            }

            return null;
        }

        private object OnContainerPopulate(LootContainer container)
        {
            if (container == null || Controller == null) return null;

            if (Controller.Crates.Contains(container))
            {
                CrateConfig crateConfig = _config.DefaultCrates.FirstOrDefault(x => x.Prefab == container.PrefabName);
                if (crateConfig == null) return true;
                if (crateConfig.TypeLootTable == 6) return null;
                else return true;
            }

            HackableLockedCrate lockedCrate = container as HackableLockedCrate;
            if (lockedCrate != null && Controller.HackCrates.Contains(lockedCrate))
            {
                if (_config.HackCrate.TypeLootTable == 6) return null;
                else return true;
            }

            return null;
        }
        #endregion Crates

        private void AddToContainerPrefab(ItemContainer container, PrefabLootTableConfig lootTable)
        {
            if (lootTable.UseCount)
            {
                int count = 0, max = UnityEngine.Random.Range(lootTable.Min, lootTable.Max + 1);
                while (count < max)
                {
                    foreach (PrefabConfig prefab in lootTable.Prefabs)
                    {
                        if (UnityEngine.Random.Range(0f, 100f) > prefab.Chance) continue;
                        SpawnIntoContainer(container, prefab.PrefabDefinition);
                        count++;
                        if (count == max) break;
                    }
                }
            }
            else foreach (PrefabConfig prefab in lootTable.Prefabs) if (UnityEngine.Random.Range(0f, 100f) <= prefab.Chance) SpawnIntoContainer(container, prefab.PrefabDefinition);
        }

        private void SpawnIntoContainer(ItemContainer container, string prefab)
        {
            if (AllLootSpawnSlots.ContainsKey(prefab))
            {
                foreach (LootContainer.LootSpawnSlot lootSpawnSlot in AllLootSpawnSlots[prefab])
                    for (int j = 0; j < lootSpawnSlot.numberToSpawn; j++)
                        if (UnityEngine.Random.Range(0f, 1f) <= lootSpawnSlot.probability)
                            lootSpawnSlot.definition.SpawnIntoContainer(container);
            }
            else AllLootSpawn[prefab].SpawnIntoContainer(container);
        }

        private void AddToContainerItem(ItemContainer container, LootTableConfig lootTable)
        {
            if (lootTable.UseCount)
            {
                HashSet<int> indexMove = new HashSet<int>();
                int count = UnityEngine.Random.Range(lootTable.Min, lootTable.Max + 1);
                while (indexMove.Count < count)
                {
                    for (int i = 0; i < lootTable.Items.Count; i++)
                    {
                        if (indexMove.Contains(i)) continue;
                        if (SpawnIntoContainer(container, lootTable.Items[i]))
                        {
                            indexMove.Add(i);
                            if (indexMove.Count == count) break;
                        }
                    }
                }
                indexMove = null;
            }
            else foreach (ItemConfig item in lootTable.Items) SpawnIntoContainer(container, item);
        }

        private bool SpawnIntoContainer(ItemContainer container, ItemConfig config)
        {
            if (UnityEngine.Random.Range(0f, 100f) > config.Chance) return false;
            Item item = config.IsBluePrint ? ItemManager.CreateByName("blueprintbase") : ItemManager.CreateByName(config.ShortName, UnityEngine.Random.Range(config.MinAmount, config.MaxAmount + 1), config.SkinId);
            if (item == null)
            {
                PrintWarning($"Failed to create item! ({config.ShortName})");
                return false;
            }
            if (config.IsBluePrint) item.blueprintTarget = ItemManager.FindItemDefinition(config.ShortName).itemid;
            if (!string.IsNullOrEmpty(config.Name)) item.name = config.Name;
            if (container.capacity < container.itemList.Count + 1) container.capacity++;
            if (!item.MoveToContainer(container))
            {
                item.Remove();
                return false;
            }
            return true;
        }

        private void CheckAllLootTables()
        {
            foreach (EntityCrateConfig entityCrateConfig in _config.EntityCrates) CheckLootTable(entityCrateConfig.LootTable);

            foreach (CrateConfig crateConfig in _config.DefaultCrates)
            {
                CheckLootTable(crateConfig.OwnLootTable);
                CheckPrefabLootTable(crateConfig.PrefabLootTable);
            }

            CheckLootTable(_config.HackCrate.OwnLootTable);
            CheckPrefabLootTable(_config.HackCrate.PrefabLootTable);

            foreach (NpcConfig preset in _config.Npc.Values)
            {
                CheckLootTable(preset.OwnLootTable);
                CheckPrefabLootTable(preset.PrefabLootTable);
            }

            SaveConfig();
        }

        private void CheckLootTable(LootTableConfig lootTable)
        {
            for (int i = lootTable.Items.Count - 1; i >= 0; i--)
            {
                ItemConfig item = lootTable.Items[i];

                if (!ItemManager.itemList.Any(x => x.shortname == item.ShortName))
                {
                    PrintWarning($"Unknown item removed! ({item.ShortName})");
                    lootTable.Items.Remove(item);
                    continue;
                }
                if (item.Chance <= 0f)
                {
                    PrintWarning($"An item with an incorrect probability has been removed from the loot table ({item.ShortName})");
                    lootTable.Items.Remove(item);
                    continue;
                }

                if (item.MinAmount <= 0) item.MinAmount = 1;
                if (item.MaxAmount < item.MinAmount) item.MaxAmount = item.MinAmount;
            }

            lootTable.Items = lootTable.Items.OrderByQuickSort(x => x.Chance);
            if (lootTable.Items.Any(x => x.Chance >= 100f))
            {
                HashSet<ItemConfig> newItems = new HashSet<ItemConfig>();

                for (int i = lootTable.Items.Count - 1; i >= 0; i--)
                {
                    ItemConfig itemConfig = lootTable.Items[i];
                    if (itemConfig.Chance < 100f) break;
                    newItems.Add(itemConfig);
                    lootTable.Items.Remove(itemConfig);
                }

                int count = newItems.Count;

                if (count > 0)
                {
                    foreach (ItemConfig itemConfig in lootTable.Items) newItems.Add(itemConfig);
                    lootTable.Items.Clear();
                    foreach (ItemConfig itemConfig in newItems) lootTable.Items.Add(itemConfig);
                }

                newItems = null;

                if (lootTable.Min < count) lootTable.Min = count;
                if (lootTable.Max < count) lootTable.Max = count;
            }

            if (lootTable.Max > lootTable.Items.Count) lootTable.Max = lootTable.Items.Count;
            if (lootTable.Min > lootTable.Max) lootTable.Min = lootTable.Max;
            if (lootTable.Items.Count == 0) lootTable.UseCount = false;
        }

        private void CheckPrefabLootTable(PrefabLootTableConfig lootTable)
        {
            HashSet<string> prefabs = new HashSet<string>();

            for (int i = lootTable.Prefabs.Count - 1; i >= 0; i--)
            {
                PrefabConfig prefab = lootTable.Prefabs[i];
                if (prefabs.Any(x => x == prefab.PrefabDefinition))
                {
                    lootTable.Prefabs.Remove(prefab);
                    PrintWarning($"Duplicate prefab removed from loot table! ({prefab.PrefabDefinition})");
                }
                else
                {
                    GameObject gameObject = GameManager.server.FindPrefab(prefab.PrefabDefinition);
                    global::HumanNPC humanNpc = gameObject.GetComponent<global::HumanNPC>();
                    ScarecrowNPC scarecrowNpc = gameObject.GetComponent<ScarecrowNPC>();
                    LootContainer lootContainer = gameObject.GetComponent<LootContainer>();
                    if (humanNpc != null && humanNpc.LootSpawnSlots.Length != 0)
                    {
                        if (!AllLootSpawnSlots.ContainsKey(prefab.PrefabDefinition)) AllLootSpawnSlots.Add(prefab.PrefabDefinition, humanNpc.LootSpawnSlots);
                        prefabs.Add(prefab.PrefabDefinition);
                    }
                    else if (scarecrowNpc != null && scarecrowNpc.LootSpawnSlots.Length != 0)
                    {
                        if (!AllLootSpawnSlots.ContainsKey(prefab.PrefabDefinition)) AllLootSpawnSlots.Add(prefab.PrefabDefinition, scarecrowNpc.LootSpawnSlots);
                        prefabs.Add(prefab.PrefabDefinition);
                    }
                    else if (lootContainer != null && lootContainer.LootSpawnSlots.Length != 0)
                    {
                        if (!AllLootSpawnSlots.ContainsKey(prefab.PrefabDefinition)) AllLootSpawnSlots.Add(prefab.PrefabDefinition, lootContainer.LootSpawnSlots);
                        prefabs.Add(prefab.PrefabDefinition);
                    }
                    else if (lootContainer != null && lootContainer.lootDefinition != null)
                    {
                        if (!AllLootSpawn.ContainsKey(prefab.PrefabDefinition)) AllLootSpawn.Add(prefab.PrefabDefinition, lootContainer.lootDefinition);
                        prefabs.Add(prefab.PrefabDefinition);
                    }
                    else
                    {
                        lootTable.Prefabs.Remove(prefab);
                        PrintWarning($"Unknown prefab removed! ({prefab.PrefabDefinition})");
                    }
                }
            }

            prefabs = null;

            lootTable.Prefabs = lootTable.Prefabs.OrderByQuickSort(x => x.Chance);
            if (lootTable.Prefabs.Any(x => x.Chance >= 100f))
            {
                HashSet<PrefabConfig> newPrefabs = new HashSet<PrefabConfig>();

                for (int i = lootTable.Prefabs.Count - 1; i >= 0; i--)
                {
                    PrefabConfig prefabConfig = lootTable.Prefabs[i];
                    if (prefabConfig.Chance < 100f) break;
                    newPrefabs.Add(prefabConfig);
                    lootTable.Prefabs.Remove(prefabConfig);
                }

                int count = newPrefabs.Count;

                if (count > 0)
                {
                    foreach (PrefabConfig prefabConfig in lootTable.Prefabs) newPrefabs.Add(prefabConfig);
                    lootTable.Prefabs.Clear();
                    foreach (PrefabConfig prefabConfig in newPrefabs) lootTable.Prefabs.Add(prefabConfig);
                }

                newPrefabs = null;

                if (lootTable.Min < count) lootTable.Min = count;
                if (lootTable.Max < count) lootTable.Max = count;
            }

            if (lootTable.Min > lootTable.Max) lootTable.Min = lootTable.Max;
            if (lootTable.Prefabs.Count == 0) lootTable.UseCount = false;
        }

        private Dictionary<string, LootSpawn> AllLootSpawn { get; } = new Dictionary<string, LootSpawn>();
        private Dictionary<string, LootContainer.LootSpawnSlot[]> AllLootSpawnSlots { get; } = new Dictionary<string, LootContainer.LootSpawnSlot[]>();
        #endregion Spawn Loot

        #region Spawn Entity
        private static BaseEntity SpawnSubEntities(string prefab, Vector3 pos, Quaternion rot)
        {
            BaseEntity entity = GameManager.server.CreateEntity(prefab, pos, rot);

            GroundWatch groundWatch = entity.GetComponent<GroundWatch>();
            if (groundWatch != null) UnityEngine.Object.DestroyImmediate(groundWatch);

            DestroyOnGroundMissing destroyOnGroundMissing = entity.GetComponent<DestroyOnGroundMissing>();
            if (destroyOnGroundMissing != null) UnityEngine.Object.DestroyImmediate(destroyOnGroundMissing);

            EntityFlag_Toggle entityFlagToggle = entity.GetComponent<EntityFlag_Toggle>();
            if (entityFlagToggle != null) UnityEngine.Object.DestroyImmediate(entityFlagToggle);

            InstrumentKeyController instrumentKeyController = entity.GetComponent<InstrumentKeyController>();
            if (instrumentKeyController != null) UnityEngine.Object.DestroyImmediate(instrumentKeyController);

            InstrumentIKController instrumentIkController = entity.GetComponent<InstrumentIKController>();
            if (instrumentIkController != null) UnityEngine.Object.DestroyImmediate(instrumentIkController);

            Buoyancy buoyancy = entity.GetComponent<Buoyancy>();
            if (buoyancy != null) UnityEngine.Object.DestroyImmediate(buoyancy);

            EntityCollisionMessage entityCollisionMessage = entity.GetComponent<EntityCollisionMessage>();
            if (entityCollisionMessage != null) UnityEngine.Object.DestroyImmediate(entityCollisionMessage);

            Rigidbody rigidbody = entity.GetComponent<Rigidbody>();
            if (rigidbody != null) UnityEngine.Object.DestroyImmediate(rigidbody);

            BaseEntity customEntity = entity.gameObject.AddComponent<BaseEntity>();
            CopySerializableFields(entity, customEntity);
            UnityEngine.Object.DestroyImmediate(entity, true);
            customEntity.enableSaving = false;
            customEntity.Spawn();

            customEntity.SetFlag(BaseEntity.Flags.Busy, true);

            return customEntity;
        }

        private static void CopySerializableFields<T>(T src, T dst)
        {
            FieldInfo[] srcFields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (FieldInfo field in srcFields)
            {
                object value = field.GetValue(src);
                field.SetValue(dst, value);
            }
        }

        private static BaseEntity SpawnEntity(string prefab, Vector3 pos, Quaternion rot)
        {
            BaseEntity entity = GameManager.server.CreateEntity(prefab, pos, rot);
            entity.enableSaving = false;

            GroundWatch groundWatch = entity.GetComponent<GroundWatch>();
            if (groundWatch != null) UnityEngine.Object.DestroyImmediate(groundWatch);

            DestroyOnGroundMissing destroyOnGroundMissing = entity.GetComponent<DestroyOnGroundMissing>();
            if (destroyOnGroundMissing != null) UnityEngine.Object.DestroyImmediate(destroyOnGroundMissing);

            Spawnable spawnable = entity.GetComponent<Spawnable>();
            if (spawnable != null) UnityEngine.Object.DestroyImmediate(spawnable);

            GrowableHeatSource growableHeatSource = entity.GetComponent<GrowableHeatSource>();
            if (growableHeatSource != null) UnityEngine.Object.DestroyImmediate(growableHeatSource);

            entity.Spawn();

            if (entity is StabilityEntity) (entity as StabilityEntity).grounded = true;

            if (entity is DecayEntity) (entity as DecayEntity).lastDecayTick = float.MaxValue;

            if (entity is BaseCombatEntity) (entity as BaseCombatEntity).pickup.enabled = false;

            return entity;
        }
        #endregion Spawn Entity

        #region PveMode
        [PluginReference] private readonly Plugin PveMode;

        internal bool ActivePveMode => _config.PveMode.Pve && plugins.Exists("PveMode");

        private void SetOwnerPveMode(string shortname, BasePlayer player)
        {
            if (string.IsNullOrEmpty(shortname) || shortname != Name || !player.IsPlayer()) return;
            Controller.Owner = player;
            AlertToAllPlayers("SetOwner", _config.Chat.Prefix, player.displayName);
        }

        private void ClearOwnerPveMode(string shortname)
        {
            if (string.IsNullOrEmpty(shortname) || shortname != Name) return;
            Controller.Owner = null;
        }
        #endregion PveMode

        #region TruePVE
        private object CanEntityTakeDamage(BasePlayer victim, HitInfo hitinfo)
        {
            if (!victim.IsPlayer() || hitinfo == null || Controller == null) return null;
            BaseEntity attacker = hitinfo.Initiator;
            if (attacker is AutoTurret && Controller.Turrets.Contains(attacker as AutoTurret)) return true;
            else if (attacker is BasePlayer && _config.IsCreateZonePvp && Controller.Players.Contains(victim) && Controller.Players.Contains(attacker as BasePlayer)) return true;
            else return null;
        }

        private object CanEntityTakeDamage(Door victim, HitInfo hitinfo)
        {
            if (victim == null || hitinfo == null || Controller == null) return null;
            if (!Controller.Doors.Contains(victim)) return null;
            BasePlayer attacker = hitinfo.InitiatorPlayer;
            if (attacker.IsPlayer() && Controller.Players.Contains(attacker)) return true;
            else return null;
        }

        private object CanEntityTakeDamage(AutoTurret victim, HitInfo hitinfo)
        {
            if (victim == null || hitinfo == null || Controller == null) return null;
            if (!Controller.Turrets.Contains(victim)) return null;
            BasePlayer attacker = hitinfo.InitiatorPlayer;
            if (attacker.IsPlayer() && Controller.Players.Contains(attacker)) return true;
            else return null;
        }

        private object CanEntityBeTargeted(BasePlayer player, AutoTurret turret)
        {
            if (!player.IsPlayer() || turret == null || Controller == null) return null;
            if (Controller.Players.Contains(player) && Controller.Turrets.Contains(turret)) return true;
            else return null;
        }
        #endregion TruePVE

        #region NTeleportation
        private object CanTeleport(BasePlayer player, Vector3 to)
        {
            if (_config.NTeleportationInterrupt && Controller != null && (Controller.Players.Contains(player) || Vector3.Distance(Controller.transform.position, to) < _config.Radius)) return GetMessage("NTeleportation", player.UserIDString, _config.Chat.Prefix);
            else return null;
        }

        private void OnPlayerTeleported(BasePlayer player, Vector3 oldPos, Vector3 newPos)
        {
            if (Controller == null || !player.IsPlayer()) return;
            if (!Controller.Players.Contains(player) && Vector3.Distance(Controller.transform.position, newPos) < _config.Radius) Controller.EnterPlayer(player);
            if (Controller.Players.Contains(player) && Vector3.Distance(Controller.transform.position, newPos) > _config.Radius) Controller.ExitPlayer(player);
        }
        #endregion NTeleportation

        #region Economy
        [PluginReference] private readonly Plugin Economics, ServerRewards, IQEconomic, XPerience;

        private Dictionary<ulong, double> PlayersBalance { get; } = new Dictionary<ulong, double>();

        private void ActionEconomy(ulong playerId, string type, string arg = "")
        {
            switch (type)
            {
                case "Crates":
                    if (_config.Economy.Crates.ContainsKey(arg)) AddBalance(playerId, _config.Economy.Crates[arg]);
                    break;
                case "Doors":
                    if (_config.Economy.Doors.ContainsKey(arg)) AddBalance(playerId, _config.Economy.Doors[arg]);
                    break;
                case "Npc":
                    AddBalance(playerId, _config.Economy.Npc);
                    break;
                case "LockedCrate":
                    AddBalance(playerId, _config.Economy.LockedCrate);
                    break;
                case "Cards":
                    if (_config.Economy.Cards.ContainsKey(arg)) AddBalance(playerId, _config.Economy.Cards[arg]);
                    break;
            }
        }

        private void AddBalance(ulong playerId, double balance)
        {
            if (balance == 0) return;
            if (PlayersBalance.ContainsKey(playerId)) PlayersBalance[playerId] += balance;
            else PlayersBalance.Add(playerId, balance);
        }

        private void SendBalance()
        {
            if (PlayersBalance.Count == 0) return;
            if (_config.Economy.Plugins.Count > 0)
            {
                foreach (KeyValuePair<ulong, double> dic in PlayersBalance)
                {
                    if (dic.Value < _config.Economy.Min) continue;
                    int intCount = Convert.ToInt32(dic.Value);
                    if (_config.Economy.Plugins.Contains("Economics") && plugins.Exists("Economics") && dic.Value > 0) Economics?.Call("Deposit", dic.Key.ToString(), dic.Value);
                    if (_config.Economy.Plugins.Contains("Server Rewards") && plugins.Exists("ServerRewards") && intCount > 0) ServerRewards?.Call("AddPoints", dic.Key, intCount);
                    if (_config.Economy.Plugins.Contains("IQEconomic") && plugins.Exists("IQEconomic") && intCount > 0) IQEconomic?.Call("API_SET_BALANCE", dic.Key, intCount);
                    BasePlayer player = BasePlayer.FindByID(dic.Key);
                    if (player != null)
                    {
                        if (_config.Economy.Plugins.Contains("XPerience") && plugins.Exists("XPerience") && dic.Value > 0) XPerience?.Call("GiveXP", player, dic.Value);
                        AlertToPlayer(player, GetMessage("SendEconomy", player.UserIDString, _config.Chat.Prefix, dic.Value));
                    }
                }
            }
            ulong winnerId = PlayersBalance.Max(x => x.Value).Key;
            Interface.Oxide.CallHook($"On{Name}Winner", winnerId);
            foreach (string command in _config.Economy.Commands) Server.Command(command.Replace("{steamid}", $"{winnerId}"));
            PlayersBalance.Clear();
        }
        #endregion Economy

        #region Alerts
        [PluginReference] private readonly Plugin GUIAnnouncements, DiscordMessages, Notify;

        private string ClearColorAndSize(string message)
        {
            message = message.Replace("</color>", string.Empty);
            message = message.Replace("</size>", string.Empty);
            while (message.Contains("<color="))
            {
                int index = message.IndexOf("<color=", StringComparison.Ordinal);
                message = message.Remove(index, message.IndexOf(">", index, StringComparison.Ordinal) - index + 1);
            }
            while (message.Contains("<size="))
            {
                int index = message.IndexOf("<size=", StringComparison.Ordinal);
                message = message.Remove(index, message.IndexOf(">", index, StringComparison.Ordinal) - index + 1);
            }
            if (!string.IsNullOrEmpty(_config.Chat.Prefix)) message = message.Replace(_config.Chat.Prefix + " ", string.Empty);
            return message;
        }

        private bool CanSendDiscordMessage => _config.Discord.IsDiscord && !string.IsNullOrEmpty(_config.Discord.WebhookUrl) && _config.Discord.WebhookUrl != "https://support.discordapp.com/hc/en-us/articles/228383668-Intro-to-Webhooks";

        private void AlertToAllPlayers(string langKey, params object[] args)
        {
            if (CanSendDiscordMessage && _config.Discord.Keys.Contains(langKey))
            {
                object fields = new[] { new { name = Title, value = ClearColorAndSize(GetMessage(langKey, null, args)), inline = false } };
                DiscordMessages?.Call("API_SendFancyMessage", _config.Discord.WebhookUrl, "", _config.Discord.EmbedColor, JsonConvert.SerializeObject(fields), null, this);
            }
            foreach (BasePlayer player in BasePlayer.activePlayerList)
                if (_config.DistanceAlerts == 0f || Vector3.Distance(player.transform.position, Controller.transform.position) <= _config.DistanceAlerts)
                    AlertToPlayer(player, GetMessage(langKey, player.UserIDString, args));
        }

        private void AlertToPlayer(BasePlayer player, string message)
        {
            if (_config.Chat.IsChat) PrintToChat(player, message);
            if (_config.GameTip.IsGameTip) player.SendConsoleCommand("gametip.showtoast", _config.GameTip.Style, ClearColorAndSize(message), string.Empty);
            if (_config.GuiAnnouncements.IsGuiAnnouncements) GUIAnnouncements?.Call("CreateAnnouncement", ClearColorAndSize(message), _config.GuiAnnouncements.BannerColor, _config.GuiAnnouncements.TextColor, player, _config.GuiAnnouncements.ApiAdjustVPosition);
            if (_config.Notify.IsNotify && plugins.Exists("Notify")) Notify?.Call("SendNotify", player, _config.Notify.Type, ClearColorAndSize(message));
        }
        #endregion Alerts

        #region GUI
        private HashSet<string> Names { get; } = new HashSet<string>
        {
            "Tab_KpucTaJl",
            "Clock_KpucTaJl",
            "Crate_KpucTaJl"
        };
        private Dictionary<string, string> Images { get; } = new Dictionary<string, string>();

        private IEnumerator DownloadImages()
        {
            foreach (string name in Names)
            {
                string url = "file://" + Interface.Oxide.DataDirectory + Path.DirectorySeparatorChar + "Images" + Path.DirectorySeparatorChar + name + ".png";
                using (UnityWebRequest unityWebRequest = UnityWebRequestTexture.GetTexture(url))
                {
                    yield return unityWebRequest.SendWebRequest();
                    if (unityWebRequest.result != UnityWebRequest.Result.Success)
                    {
                        PrintError($"Image {name} was not found. Maybe you didn't upload it to the .../oxide/data/Images/ folder");
                        break;
                    }
                    else
                    {
                        Texture2D tex = DownloadHandlerTexture.GetContent(unityWebRequest);
                        Images.Add(name, FileStorage.server.Store(tex.EncodeToPNG(), FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID).ToString());
                        Puts($"Image {name} download is complete");
                        UnityEngine.Object.DestroyImmediate(tex);
                    }
                }
            }
            if (Images.Count < Names.Count) Interface.Oxide.UnloadPlugin(Name);
        }

        private void CreateTabs(BasePlayer player, Dictionary<string, string> tabs)
        {
            CuiHelper.DestroyUi(player, "Tabs_KpucTaJl");

            CuiElementContainer container = new CuiElementContainer();

            float border = 52.5f + 54.5f * (tabs.Count - 1);
            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = $"{-border} {_config.Gui.OffsetMinY}", OffsetMax = $"{border} {_config.Gui.OffsetMinY + 20}" },
                CursorEnabled = false,
            }, "Under", "Tabs_KpucTaJl");

            int i = 0;

            foreach (KeyValuePair<string, string> dic in tabs)
            {
                i++;
                float xmin = 109f * (i - 1);
                container.Add(new CuiElement
                {
                    Name = $"Tab_{i}_KpucTaJl",
                    Parent = "Tabs_KpucTaJl",
                    Components =
                    {
                        new CuiRawImageComponent { Png = Images["Tab_KpucTaJl"] },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = $"{xmin} 0", OffsetMax = $"{xmin + 105f} 20" }
                    }
                });
                container.Add(new CuiElement
                {
                    Parent = $"Tab_{i}_KpucTaJl",
                    Components =
                    {
                        new CuiRawImageComponent { Png = Images[dic.Key] },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "9 3", OffsetMax = "23 17" }
                    }
                });
                container.Add(new CuiElement
                {
                    Parent = $"Tab_{i}_KpucTaJl",
                    Components =
                    {
                        new CuiTextComponent() { Color = "1 1 1 1", Text = dic.Value, Align = TextAnchor.MiddleCenter, FontSize = 10, Font = "robotocondensed-bold.ttf" },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "28 0", OffsetMax = "100 20" }
                    }
                });
            }

            CuiHelper.AddUi(player, container);
        }
        #endregion GUI

        #region Helpers
        [PluginReference] private readonly Plugin NpcSpawn;

        private HashSet<BaseEntity> IsWaterEventInProgress() => Active ? Controller.Entities : null;

        private HashSet<string> HooksInsidePlugin { get; } = new HashSet<string>
        {
            "OnEntityTakeDamage",
            "OnPlayerConnected",
            "OnPlayerDeath",
            "OnEntityDeath",
            "OnOvenToggle",
            "CanUseWires",
            "CanBuild",
            "CanChangeGrade",
            "OnStructureRotate",
            "OnCardSwipe",
            "OnButtonPress",
            "OnEntityKill",
            "CanHackCrate",
            "OnCrateHack",
            "OnLootEntity",
            "OnEntityEnter",
            "OnPlayerCommand",
            "OnServerCommand",
            "OnCargoShipEgress",
            "OnCorpsePopulate",
            "CanPopulateLoot",
            "OnCustomLootNPC",
            "OnCustomLootContainer",
            "OnContainerPopulate",
            "SetOwnerPveMode",
            "ClearOwnerPveMode",
            "CanEntityTakeDamage",
            "CanEntityBeTargeted",
            "CanTeleport",
            "OnPlayerTeleported"
        };

        private void ToggleHooks(bool subscribe)
        {
            foreach (string hook in HooksInsidePlugin)
            {
                if (subscribe) Subscribe(hook);
                else Unsubscribe(hook);
            }
        }

        private const string StrSec = En ? "sec." : "сек.";
        private const string StrMin = En ? "min." : "мин.";
        private const string StrH = En ? "h." : "ч.";

        private static string GetTimeFormat(int time)
        {
            if (time <= 60) return $"{time} {StrSec}";
            else if (time <= 3600)
            {
                int sec = time % 60;
                int min = (time - sec) / 60;
                return sec == 0 ? $"{min} {StrMin}" : $"{min} {StrMin} {sec} {StrSec}";
            }
            else
            {
                int minSec = time % 3600;
                int hour = (time - minSec) / 3600;
                int sec = minSec % 60;
                int min = (minSec - sec) / 60;
                if (min == 0 && sec == 0) return $"{hour} {StrH}";
                else if (sec == 0) return $"{hour} {StrH} {min} {StrMin}";
                else return $"{hour} {StrH} {min} {StrMin} {sec} {StrSec}";
            }
        }

        private static void UpdateMarkerForPlayer(BasePlayer player, Vector3 pos, PointConfig config)
        {
            if (player == null || player.IsSleeping()) return;
            bool isAdmin = player.IsAdmin;
            if (!isAdmin)
            {
                player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                player.SendNetworkUpdateImmediate();
            }
            try
            {
                player.SendConsoleCommand("ddraw.text", 1f, Color.white, pos, $"<size={config.Size}><color={config.Color}>{config.Text}</color></size>");
            }
            finally
            {
                if (!isAdmin)
                {
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                    player.SendNetworkUpdateImmediate();
                }
            }
        }

        private void CheckVersionPlugin()
        {
            webrequest.Enqueue("http://37.153.157.216:5000/Api/GetPluginVersions?pluginName=WaterEvent", null, (code, response) =>
            {
                if (code != 200 || string.IsNullOrEmpty(response)) return;
                string[] array = response.Replace("\"", string.Empty).Split('.');
                VersionNumber latestVersion = new VersionNumber(Convert.ToInt32(array[0]), Convert.ToInt32(array[1]), Convert.ToInt32(array[2]));
                if (Version < latestVersion) PrintWarning($"A new version ({latestVersion}) of the plugin is available! You need to update the plugin:\n- https://lone.design/product/water-event-rust-plugin\n- https://codefling.com/plugins/water-event");
            }, this);
        }

        private bool PluginExistsForStart(string pluginName)
        {
            if (plugins.Exists(pluginName)) return true;
            PrintError($"{pluginName} plugin doesn`t exist! (https://drive.google.com/drive/folders/1-18L-mG7yiGxR-PQYvd11VvXC2RQ4ZCu?usp=sharing)");
            Interface.Oxide.UnloadPlugin(Name);
            return false;
        }
        #endregion Helpers

        #region Commands
        [ChatCommand("waterstart")]
        private void ChatStartEvent(BasePlayer player)
        {
            if (player.IsAdmin)
            {
                if (!Active) Start(null);
                else PrintToChat(player, GetMessage("EventActive", player.UserIDString, _config.Chat.Prefix));
            }
        }

        [ChatCommand("waterstop")]
        private void ChatStopEvent(BasePlayer player)
        {
            if (player.IsAdmin)
            {
                if (Controller != null) Finish();
                else Interface.Oxide.ReloadPlugin(Name);
            }
        }

        [ChatCommand("waterpos")]
        private void ChatCommandPos(BasePlayer player)
        {
            if (!player.IsAdmin || Controller == null) return;
            Vector3 pos = Controller.transform.InverseTransformPoint(player.transform.position);
            Puts($"Position: {pos}");
            PrintToChat(player, $"Position: {pos}");
        }

        [ConsoleCommand("waterstart")]
        private void ConsoleStartEvent(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null) return;
            if (!Active)
            {
                if (arg.Args == null || arg.Args.Length != 1)
                {
                    Start(null);
                    return;
                }
                ulong steamId = Convert.ToUInt64(arg.Args[0]);
                BasePlayer target = BasePlayer.FindByID(steamId);
                if (target == null)
                {
                    Start(null);
                    Puts($"Player with SteamID {steamId} not found!");
                    return;
                }
                Start(target);
            }
            else Puts("This event is active now. To finish this event (waterstop), then to start the next one");
        }

        [ConsoleCommand("waterstop")]
        private void ConsoleStopEvent(ConsoleSystem.Arg arg)
        {
            if (arg.Player() == null)
            {
                if (Controller != null) Finish();
                else Interface.Oxide.ReloadPlugin(Name);
            }
        }
        #endregion Commands
    }
}

namespace Oxide.Plugins.WaterEventExtensionMethods
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

        public static HashSet<TResult> Select<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> predicate)
        {
            HashSet<TResult> result = new HashSet<TResult>();
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) result.Add(predicate(enumerator.Current));
            return result;
        }

        public static TSource Min<TSource>(this IEnumerable<TSource> source, Func<TSource, float> predicate)
        {
            TSource result = default(TSource);
            float resultValue = float.MaxValue;
            using (var enumerator = source.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    TSource element = enumerator.Current;
                    float elementValue = predicate(element);
                    if (elementValue < resultValue)
                    {
                        result = element;
                        resultValue = elementValue;
                    }
                }
            }
            return result;
        }

        public static TSource Max<TSource>(this IEnumerable<TSource> source, Func<TSource, double> predicate)
        {
            TSource result = default(TSource);
            double resultValue = double.MinValue;
            using (var enumerator = source.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    TSource element = enumerator.Current;
                    double elementValue = predicate(element);
                    if (elementValue > resultValue)
                    {
                        result = element;
                        resultValue = elementValue;
                    }
                }
            }
            return result;
        }

        private static void Replace<TSource>(this IList<TSource> source, int x, int y)
        {
            TSource t = source[x];
            source[x] = source[y];
            source[y] = t;
        }

        private static List<TSource> QuickSort<TSource>(this List<TSource> source, Func<TSource, float> predicate, int minIndex, int maxIndex)
        {
            if (minIndex >= maxIndex) return source;

            int pivotIndex = minIndex - 1;
            for (int i = minIndex; i < maxIndex; i++)
            {
                if (predicate(source[i]) < predicate(source[maxIndex]))
                {
                    pivotIndex++;
                    source.Replace(pivotIndex, i);
                }
            }
            pivotIndex++;
            source.Replace(pivotIndex, maxIndex);

            QuickSort(source, predicate, minIndex, pivotIndex - 1);
            QuickSort(source, predicate, pivotIndex + 1, maxIndex);

            return source;
        }

        public static List<TSource> OrderByQuickSort<TSource>(this List<TSource> source, Func<TSource, float> predicate) => source.QuickSort(predicate, 0, source.Count - 1);

        public static bool IsPlayer(this BasePlayer player) => player != null && player.userID.IsSteamId();

        public static bool IsExists(this BaseNetworkable entity) => entity != null && !entity.IsDestroyed;

        public static void ClearItemsContainer(this ItemContainer container)
        {
            for (int i = container.itemList.Count - 1; i >= 0; i--)
            {
                Item item = container.itemList[i];
                item.RemoveFromContainer();
                item.Remove();
            }
        }

        public static void KillMapMarker(this HackableLockedCrate crate)
        {
            if (!crate.mapMarkerInstance.IsExists()) return;
            crate.mapMarkerInstance.Kill();
            crate.mapMarkerInstance = null;
        }
    }
}