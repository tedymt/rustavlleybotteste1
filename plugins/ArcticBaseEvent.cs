using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using UnityEngine;
using Oxide.Core.Plugins;
using Oxide.Core;
using Facepunch;
using System.Collections;
using System.IO;
using Oxide.Game.Rust.Cui;
using UnityEngine.Networking;
using System.Reflection;
using Oxide.Plugins.ArcticBaseEventExtensionMethods;

namespace Oxide.Plugins
{
    [Info("ArcticBaseEvent", "KpucTaJl", "1.3.0")]
    internal class ArcticBaseEvent : RustPlugin
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
            if (_config.PluginVersion < new VersionNumber(1, 0, 2)) _config.Radius = 70f;
            if (_config.PluginVersion < new VersionNumber(1, 0, 8))
            {
                _config.MainPoint = new PointConfig
                {
                    Enabled = true,
                    Text = "◈",
                    Size = 45,
                    Color = "#CCFF00"
                };
                _config.AdditionalPoint = new PointConfig
                {
                    Enabled = true,
                    Text = "◆",
                    Size = 25,
                    Color = "#FFC700"
                };
            }
            if (_config.PluginVersion < new VersionNumber(1, 1, 0))
            {
                _config.GameTip = new GameTipConfig
                {
                    IsGameTip = false,
                    Style = 2
                };
                _config.Marker = new MarkerConfig
                {
                    Enabled = true,
                    Type = 1,
                    Radius = 0.37967f,
                    Alpha = 0.35f,
                    Color = new ColorConfig { R = 0.81f, G = 0.25f, B = 0.15f },
                    Text = "ArcticBaseEvent"
                };
            }
            if (_config.PluginVersion < new VersionNumber(1, 1, 2))
            {
                _config.ScientistName = "Scientist";
                _config.ScientistWearItems = new HashSet<NpcWear>
                {
                    new NpcWear { ShortName = "hoodie", SkinId = 2524481578 },
                    new NpcWear { ShortName = "pants", SkinId = 2524482916 },
                    new NpcWear { ShortName = "shoes.boots", SkinId = 1644270941 },
                    new NpcWear { ShortName = "jacket", SkinId = 2784960675 },
                    new NpcWear { ShortName = "hat.beenie", SkinId = 854338485 },
                    new NpcWear { ShortName = "mask.bandana", SkinId = 835092389 },
                    new NpcWear { ShortName = "burlap.gloves", SkinId = 2403558237 }
                };
                _config.PilotName = "Pilot";
                _config.PilotWearItems = new HashSet<NpcWear>
                {
                    new NpcWear { ShortName = "shoes.boots", SkinId = 899942107 },
                    new NpcWear { ShortName = "burlap.headwrap", SkinId = 1694253807 },
                    new NpcWear { ShortName = "burlap.shirt", SkinId = 1694259867 },
                    new NpcWear { ShortName = "burlap.trousers", SkinId = 1694262795 },
                    new NpcWear { ShortName = "burlap.gloves", SkinId = 916319889 }
                };
            }
            if (_config.PluginVersion < new VersionNumber(1, 1, 5))
            {
                _config.PveMode.ScaleDamage = new Dictionary<string, float>
                {
                    ["Npc"] = 1f
                };
            }
            if (_config.PluginVersion < new VersionNumber(1, 1, 6))
            {
                _config.Chat = new ChatConfig
                {
                    IsChat = true,
                    Prefix = "[ArcticBaseEvent]"
                };
                _config.DistanceAlerts = 0f;
                _config.Notify.Type = 0;
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

        public class CrateConfig
        {
            [JsonProperty("Prefab")] public string Prefab { get; set; }
            [JsonProperty(En ? "Position" : "Позиция")] public string Position { get; set; }
            [JsonProperty(En ? "Rotation" : "Вращение")] public string Rotation { get; set; }
            [JsonProperty(En ? "Which loot table should the plugin use? (0 - default; 1 - own; 2 - AlphaLoot; 3 - CustomLoot; 4 - loot table of the Rust objects; 5 - combine the 1 and 4 methods)" : "Какую таблицу предметов необходимо использовать? (0 - стандартную; 1 - собственную; 2 - AlphaLoot; 3 - CustomLoot; 4 - таблица предметов объектов Rust; 5 - совместить 1 и 4 методы)")] public int TypeLootTable { get; set; }
            [JsonProperty(En ? "Loot table from prefabs (if the loot table type is 4 or 5)" : "Таблица предметов из prefab-ов (если тип таблицы предметов - 4 или 5)")] public PrefabLootTableConfig PrefabLootTable { get; set; }
            [JsonProperty(En ? "Own loot table (if the loot table type is 1 or 5)" : "Собственная таблица предметов (если тип таблицы предметов - 1 или 5)")] public LootTableConfig OwnLootTable { get; set; }
        }

        public class HackCrateConfig
        {
            [JsonProperty(En ? "Position" : "Позиция")] public string Position { get; set; }
            [JsonProperty(En ? "Rotation" : "Вращение")] public string Rotation { get; set; }
            [JsonProperty(En ? "Time to unlock the Crate [sec.]" : "Время разблокировки ящика [sec.]")] public float UnlockTime { get; set; }
            [JsonProperty(En ? "Increase the event time if it's not enough to unlock the locked crate? [true/false]" : "Увеличивать время ивента, если недостаточно чтобы разблокировать заблокированный ящик? [true/false]")] public bool IncreaseEventTime { get; set; }
            [JsonProperty(En ? "Which loot table should the plugin use? (0 - default; 1 - own; 2 - AlphaLoot; 3 - CustomLoot; 4 - loot table of the Rust objects; 5 - combine the 1 and 4 methods)" : "Какую таблицу лута необходимо использовать? (0 - стандартную; 1 - собственную; 2 - AlphaLoot; 3 - CustomLoot; 4 - таблица предметов объектов Rust; 5 - совместить 1 и 4 методы)")] public int TypeLootTable { get; set; }
            [JsonProperty(En ? "Loot table from prefabs (if the loot table type is 4 or 5)" : "Таблица предметов из prefab-ов (если тип таблицы предметов - 4 или 5)")] public PrefabLootTableConfig PrefabLootTable { get; set; }
            [JsonProperty(En ? "Own loot table (if the loot table type is 1 or 5)" : "Собственная таблица предметов (если тип таблицы предметов - 1 или 5)")] public LootTableConfig OwnLootTable { get; set; }
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
            [JsonProperty(En ? "Use the countdown timer? [true/false]" : "Использовать ли GUI обратного отсчета? [true/false]")] public bool IsGui { get; set; }
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
            [JsonProperty(En ? "Use the plugin DiscordMessages for posting event notifications? [true/false]" : "Использовать ли Discord? [true/false]")] public bool IsDiscord { get; set; }
            [JsonProperty("Webhook URL")] public string WebhookUrl { get; set; }
            [JsonProperty(En ? "Embed Color (DECIMAL)" : "Цвет полосы (DECIMAL)")] public int EmbedColor { get; set; }
            [JsonProperty(En ? "Keys of required messages" : "Ключи необходимых сообщений")] public HashSet<string> Keys { get; set; }
        }

        public class EconomyConfig
        {
            [JsonProperty(En ? "Which economy plugins do you want to use? (Economics, Server Rewards, IQEconomic)" : "Какие плагины экономики вы хотите использовать? (Economics, Server Rewards, IQEconomic)")] public HashSet<string> Plugins { get; set; }
            [JsonProperty(En ? "The minimum value that a player must collect to get points for the economy" : "Минимальное значение, которое игрок должен заработать, чтобы получить баллы за экономику")] public double Min { get; set; }
            [JsonProperty(En ? "Looting of crates" : "Ограбление ящиков")] public Dictionary<string, double> Crates { get; set; }
            [JsonProperty(En ? "Killing an NPC" : "Убийство NPC")] public double Npc { get; set; }
            [JsonProperty(En ? "Hacking a locked crate" : "Взлом заблокированного ящика")] public double LockedCrate { get; set; }
            [JsonProperty(En ? "Opening the Pilot's door" : "Открытие двери пилота")] public double OpenDoorPilot { get; set; }
            [JsonProperty(En ? "Opening the Scientist's door" : "Открытие двери ученого")] public double OpenDoorScientist { get; set; }
            [JsonProperty(En ? "List of commands that are executed in the console at the end of the event ({steamid} - the player who collected the highest number of points)" : "Список команд, которые выполняются в консоли по окончанию ивента ({steamid} - игрок, который набрал наибольшее кол-во баллов)")] public HashSet<string> Commands { get; set; }
        }

        public class PveModeConfig
        {
            [JsonProperty(En ? "Use PVE mode the plugin? [true/false]" : "Использовать PVE режим работы плагина? [true/false]")] public bool Pve { get; set; }
            [JsonProperty(En ? "The amount of damage that the player has to do to become the Event Owner" : "Кол-во урона, которое должен нанести игрок, чтобы стать владельцем ивента")] public float Damage { get; set; }
            [JsonProperty(En ? "Damage Multipliers for calculate to become the Event Owner" : "Коэффициенты урона для подсчета, чтобы стать владельцем ивента")] public Dictionary<string, float> ScaleDamage { get; set; }
            [JsonProperty(En ? "Can the non-owner of the event loot the crates? [true/false]" : "Может ли не владелец ивента грабить ящики? [true/false]")] public bool LootCrate { get; set; }
            [JsonProperty(En ? "Can the non-owner of the event hack locked crates? [true/false]" : "Может ли не владелец ивента взламывать заблокированные ящики? [true/false]")] public bool HackCrate { get; set; }
            [JsonProperty(En ? "Can the non-owner of the event loot NPC corpses? [true/false]" : "Может ли не владелец ивента грабить трупы NPC? [true/false]")] public bool LootNpc { get; set; }
            [JsonProperty(En ? "Can the non-owner of the event deal damage to the NPC? [true/false]" : "Может ли не владелец ивента наносить урон по NPC? [true/false]")] public bool DamageNpc { get; set; }
            [JsonProperty(En ? "Can Npc attack a non-owner of the event? [true/false]" : "Может ли Npc атаковать не владельца ивента? [true/false]")] public bool TargetNpc { get; set; }
            [JsonProperty(En ? "Allow a non-owner of the event to enter the event zone? [true/false]" : "Разрешать входить внутрь зоны ивента не владельцу ивента? [true/false]")] public bool CanEnter { get; set; }
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
        }

        public class PresetConfig
        {
            [JsonProperty(En ? "Minimum" : "Минимальное кол-во")] public int Min { get; set; }
            [JsonProperty(En ? "Maximum" : "Максимальное кол-во")] public int Max { get; set; }
            [JsonProperty(En ? "List of locations" : "Список расположений")] public HashSet<string> Positions { get; set; }
            [JsonProperty(En ? "NPCs setting" : "Настройки NPC")] public NpcConfig Config { get; set; }
            [JsonProperty(En ? "Which loot table should the plugin use? (0 - default; 1 - own; 2 - AlphaLoot; 3 - CustomLoot; 4 - loot table of the Rust objects; 5 - combine the 1 and 4 methods)" : "Какую таблицу предметов необходимо использовать? (0 - стандартную; 1 - собственную; 2 - AlphaLoot; 3 - CustomLoot; 4 - таблица предметов объектов Rust; 5 - совместить 1 и 4 методы)")] public int TypeLootTable { get; set; }
            [JsonProperty(En ? "Loot table from prefabs (if the loot table type is 4 or 5)" : "Таблица предметов из prefab-ов (если тип таблицы предметов - 4 или 5)")] public PrefabLootTableConfig PrefabLootTable { get; set; }
            [JsonProperty(En ? "Own loot table (if the loot table type is 1 or 5)" : "Собственная таблица предметов (если тип таблицы предметов - 1 или 5)")] public LootTableConfig OwnLootTable { get; set; }
        }

        public class AdditionalNpcConfig
        {
            [JsonProperty(En ? "Name" : "Название")] public string Name { get; set; }
            [JsonProperty(En ? "Health" : "Кол-во ХП")] public float Health { get; set; }
            [JsonProperty(En ? "Chase Range" : "Дальность погони за целью")] public float ChaseRange { get; set; }
            [JsonProperty(En ? "Attack Range Multiplier" : "Множитель радиуса атаки")] public float AttackRangeMultiplier { get; set; }
            [JsonProperty(En ? "Sense Range" : "Радиус обнаружения цели")] public float SenseRange { get; set; }
            [JsonProperty(En ? "Target Memory Duration [sec.]" : "Длительность памяти цели [sec.]")] public float MemoryDuration { get; set; }
            [JsonProperty(En ? "Scale damage" : "Множитель урона")] public float DamageScale { get; set; }
            [JsonProperty(En ? "Aim Cone Scale" : "Множитель разброса")] public float AimConeScale { get; set; }
            [JsonProperty(En ? "Speed" : "Скорость")] public float Speed { get; set; }
            [JsonProperty(En ? "Disable radio effects? [true/false]" : "Отключать эффекты рации? [true/false]")] public bool DisableRadio { get; set; }
            [JsonProperty(En ? "Remove a corpse after death? (it is recommended to use the true value to improve performance) [true/false]" : "Удалять труп после смерти? (рекомендуется использовать значение true для повышения производительности) [true/false]")] public bool IsRemoveCorpse { get; set; }
            [JsonProperty(En ? "Wear items" : "Одежда")] public HashSet<NpcWear> WearItems { get; set; }
            [JsonProperty(En ? "Belt items" : "Быстрые слоты")] public HashSet<NpcBelt> BeltItems { get; set; }
            [JsonProperty(En ? "Kit (it is recommended to use the previous 2 settings to improve performance)" : "Kit (рекомендуется использовать предыдущие 2 пункта настройки для повышения производительности)")] public string Kit { get; set; }
            [JsonProperty(En ? "Which loot table should the plugin use? (0 - default; 1 - own; 2 - AlphaLoot; 3 - CustomLoot; 4 - loot table of the Rust objects; 5 - combine the 1 and 4 methods)" : "Какую таблицу предметов необходимо использовать? (0 - стандартную; 1 - собственную; 2 - AlphaLoot; 3 - CustomLoot; 4 - таблица предметов объектов Rust; 5 - совместить 1 и 4 методы)")] public int TypeLootTable { get; set; }
            [JsonProperty(En ? "Loot table from prefabs (if the loot table type is 4 or 5)" : "Таблица предметов из prefab-ов (если тип таблицы предметов - 4 или 5)")] public PrefabLootTableConfig PrefabLootTable { get; set; }
            [JsonProperty(En ? "Own loot table (if the loot table type is 1 or 5)" : "Собственная таблица предметов (если тип таблицы предметов - 1 или 5)")] public LootTableConfig OwnLootTable { get; set; }
        }

        private class PluginConfig
        {
            [JsonProperty(En ? "Minimum time between events [sec.]" : "Минимальное время между ивентами [sec.]")] public float MinStartTime { get; set; }
            [JsonProperty(En ? "Maximum time between events [sec.]" : "Максимальное время между ивентами [sec.]")] public float MaxStartTime { get; set; }
            [JsonProperty(En ? "Is the countdown timer active for the event? [true/false]" : "Активен ли таймер для запуска ивента? [true/false]")] public bool EnabledTimer { get; set; }
            [JsonProperty(En ? "Duration of the event [sec.]" : "Время проведения ивента [sec.]")] public int FinishTime { get; set; }
            [JsonProperty(En ? "Time before the starting of the event after receiving a chat message [sec.]" : "Время до начала ивента после сообщения в чате [sec.]")] public float PreStartTime { get; set; }
            [JsonProperty(En ? "Notification time until the end of the event [sec.]" : "Время оповещения до окончания ивента [sec.]")] public int PreFinishTime { get; set; }
            [JsonProperty(En ? "List of crates if the scientist survived during evacuation" : "Список ящиков, если ученый выжил при эвакуации")] public HashSet<CrateConfig> CratesSuccess { get; set; }
            [JsonProperty(En ? "List of crates if the scientist died during evacuation" : "Список ящиков, если ученый умер при эвакуации")] public HashSet<CrateConfig> CratesFailure { get; set; }
            [JsonProperty(En ? "Locked crate setting" : "Настройка заблокированного ящика")] public HackCrateConfig HackCrate { get; set; }
            [JsonProperty(En ? "Settings of all NPC presets when start the event" : "Настройки всех пресетов NPC при запуске ивента")] public HashSet<PresetConfig> PresetsNpc { get; set; }
            [JsonProperty(En ? "The number of snowmobiles that appear during the opening of the door" : "Количество снегоходов, которые появляются во время открытия двери")] public int NumberSnowmobilesOpenDoor { get; set; }
            [JsonProperty(En ? "The number of snowmobiles that appear when a scientist starts to research a corpse" : "Количество снегоходов, которые появляются когда ученый начинает исследовать труп")] public int NumberSnowmobilesResearch { get; set; }
            [JsonProperty(En ? "NPC settings on snowmobiles" : "Настройки NPC на снегоходах")] public AdditionalNpcConfig ConfigNpcSnowmobile { get; set; }
            [JsonProperty(En ? "The number of NPCs when the minicopter repair process takes place" : "Количество NPC, которые появляются во время ремонта миникоптера")] public int NumberNpcRepairMinicopter { get; set; }
            [JsonProperty(En ? "NPC settings that appear during minicopter repair" : "Настройки NPC, которые появляются во время ремонта миникоптера")] public AdditionalNpcConfig ConfigNpcRepairMinicopter { get; set; }
            [JsonProperty(En ? "Scientist's Name" : "Название ученого")] public string ScientistName { get; set; }
            [JsonProperty(En ? "Scientist's Health" : "Кол-во ХП ученого")] public float ScientistHealth { get; set; }
            [JsonProperty(En ? "Wear items for a scientist" : "Одежда для ученого")] public HashSet<NpcWear> ScientistWearItems { get; set; }
            [JsonProperty(En ? "Time to research a corpse for a scientist [sec.]" : "Время изучения трупа для ученого [sec.]")] public int ResearchTime { get; set; }
            [JsonProperty(En ? "Pilot's Name" : "Название пилота")] public string PilotName { get; set; }
            [JsonProperty(En ? "Wear items for the pilot" : "Одежда для пилота")] public HashSet<NpcWear> PilotWearItems { get; set; }
            [JsonProperty(En ? "Minicopter repair time for the pilot [sec.]" : "Время ремонта миникоптера для пилота [sec.]")] public int RepairTime { get; set; }
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
            [JsonProperty(En ? "Interrupt the teleport in the event area? (only for users NTeleportation plugin) [true/false]" : "Запрещать телепорт в зоне проведения ивента? (только для тех, кто использует плагин NTeleportation) [true/false]")] public bool NTeleportationInterrupt { get; set; }
            [JsonProperty(En ? "Disable NPCs from the BetterNpc plugin on the monument while the event is on? [true/false]" : "Отключать NPC из плагина BetterNpc на монументе пока проходит ивент? [true/false]")] public bool RemoveBetterNpc { get; set; }
            [JsonProperty(En ? "Economy setting (total values will be added up and rewarded at the end of the event)" : "Настройка экономики (конечное значение суммируется и будет выдано игрокам по окончанию ивента)")] public EconomyConfig Economy { get; set; }
            [JsonProperty(En ? "List of commands banned in the event zone" : "Список команд запрещенных в зоне ивента")] public HashSet<string> Commands { get; set; }
            [JsonProperty(En ? "Disable default NPCs on the monument while the event is on? [true/false]" : "Отключать стандартных NPC на монументе пока проходит ивент? [true/false]")] public bool RemoveDefaultNpc { get; set; }
            [JsonProperty(En ? "The probability of a note with passwords appearing in the corpse of an NPC" : "Вероятность появления записки с паролями в трупе NPC [0.0-100.0]")] public float ChanceNote { get; set; }
            [JsonProperty(En ? "Configuration version" : "Версия конфигурации")] public VersionNumber PluginVersion { get; set; }

            public static PluginConfig DefaultConfig()
            {
                return new PluginConfig
                {
                    MinStartTime = 10800f,
                    MaxStartTime = 10800f,
                    EnabledTimer = true,
                    FinishTime = 1800,
                    PreStartTime = 300f,
                    PreFinishTime = 300,
                    CratesSuccess = new HashSet<CrateConfig>
                    {
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_elite.prefab",
                            Position = "(41.969, 1.412, 5.03)",
                            Rotation = "(0, 270, 0)",
                            TypeLootTable = 0,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_elite.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 25, MaxAmount = 25, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_elite.prefab",
                            Position = "(36.126, 1.412, 6.628)",
                            Rotation = "(0, 270, 0)",
                            TypeLootTable = 0,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_elite.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 25, MaxAmount = 25, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_normal.prefab",
                            Position = "(36.008, 2.875, 5.259)",
                            Rotation = "(0, 0, 0)",
                            TypeLootTable = 0,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_normal.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 8, MaxAmount = 8, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_normal.prefab",
                            Position = "(36.261, 2.008, 4.985)",
                            Rotation = "(0, 270, 0)",
                            TypeLootTable = 0,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_normal.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 8, MaxAmount = 8, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_normal_2.prefab",
                            Position = "(40.895, 3.661, 13.894)",
                            Rotation = "(0, 356.75, 0)",
                            TypeLootTable = 0,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_normal_2.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 5, MaxAmount = 5, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/autospawn/resource/loot/trash-pile-1.prefab",
                            Position = "(41.084, 3.691, 12.733)",
                            Rotation = "(0, 30.778, 0)",
                            TypeLootTable = 0,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/autospawn/resource/loot/trash-pile-1.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 1, MaxAmount = 1, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_tools.prefab",
                            Position = "(41.575, 2.419, 2.398)",
                            Rotation = "(0, 344.918, 0)",
                            TypeLootTable = 0,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_tools.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 5, MaxAmount = 5, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/dmloot/dm c4.prefab",
                            Position = "(41.428, 2.924, 1.514)",
                            Rotation = "(0, 1.676, 0)",
                            TypeLootTable = 0,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/dmloot/dm c4.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 5, MaxAmount = 10, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        }
                    },
                    CratesFailure = new HashSet<CrateConfig>
                    {
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_normal.prefab",
                            Position = "(41.969, 1.425, 4.918)",
                            Rotation = "(0, 270, 0)",
                            TypeLootTable = 0,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_normal.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 8, MaxAmount = 8, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_normal.prefab",
                            Position = "(36.126, 1.399, 6.563)",
                            Rotation = "(0, 270, 0)",
                            TypeLootTable = 0,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_normal.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 8, MaxAmount = 8, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_normal_2.prefab",
                            Position = "(36.224, 2.871, 5.19)",
                            Rotation = "(0, 180, 0)",
                            TypeLootTable = 0,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_normal_2.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 5, MaxAmount = 5, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_normal_2.prefab",
                            Position = "(36.314, 2.017, 4.935)",
                            Rotation = "(0, 270, 0)",
                            TypeLootTable = 0,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_normal_2.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 5, MaxAmount = 5, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_normal_2.prefab",
                            Position = "(40.895, 3.661, 13.894)",
                            Rotation = "(0, 356.75, 0)",
                            TypeLootTable = 0,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_normal_2.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 5, MaxAmount = 5, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/autospawn/resource/loot/trash-pile-1.prefab",
                            Position = "(41.084, 3.691, 12.733)",
                            Rotation = "(0, 30.778, 0)",
                            TypeLootTable = 0,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/autospawn/resource/loot/trash-pile-1.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 1, MaxAmount = 1, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_tools.prefab",
                            Position = "(41.575, 2.419, 2.398)",
                            Rotation = "(0, 344.918, 0)",
                            TypeLootTable = 0,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_tools.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 5, MaxAmount = 5, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/underwater_labs/crate_normal.prefab",
                            Position = "(41.428, 2.924, 1.514)",
                            Rotation = "(0, 1.676, 0)",
                            TypeLootTable = 0,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/underwater_labs/crate_normal.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 8, MaxAmount = 8, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        }
                    },
                    HackCrate = new HackCrateConfig
                    {
                        Position = "(36.175, 1.417, 7.998)",
                        Rotation = "(0, 90, 0)",
                        UnlockTime = 600f,
                        IncreaseEventTime = true,
                        TypeLootTable = 0,
                        PrefabLootTable = new PrefabLootTableConfig
                        {
                            Min = 1,
                            Max = 1,
                            UseCount = true,
                            Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab" } }
                        },
                        OwnLootTable = new LootTableConfig
                        {
                            Min = 1,
                            Max = 1,
                            UseCount = true,
                            Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 5, MaxAmount = 10, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" } }
                        }
                    },
                    PresetsNpc = new HashSet<PresetConfig>
                    {
                        new PresetConfig
                        {
                            Min = 5,
                            Max = 10,
                            Positions = new HashSet<string>
                            {
                                "(-53.342, 0.077, 12.44)",
                                "(-22.835, 0.068, 53.353)",
                                "(-13.385, 0.115, 2.556)",
                                "(1.523, 0.115, -24.996)",
                                "(-23.034, 0.047, -43.611)",
                                "(26.787, 0.115, -20.344)",
                                "(48.223, 0.115, -8.085)",
                                "(14.209, 0.115, -7.144)",
                                "(20.041, 0.875, 34.479)",
                                "(2.091, 0.875, 28.551)"
                            },
                            Config = new NpcConfig
                            {
                                Name = "Ticent",
                                Health = 175f,
                                RoamRange = 20f,
                                ChaseRange = 60f,
                                AttackRangeMultiplier = 1.5f,
                                SenseRange = 40f,
                                MemoryDuration = 30f,
                                DamageScale = 1.3f,
                                AimConeScale = 1.2f,
                                CheckVisionCone = false,
                                VisionCone = 135f,
                                Speed = 7.5f,
                                DisableRadio = true,
                                Stationary = false,
                                IsRemoveCorpse = true,
                                WearItems = new HashSet<NpcWear>
                                {
                                    new NpcWear { ShortName = "metal.facemask", SkinId = 2874256234 },
                                    new NpcWear { ShortName = "metal.plate.torso", SkinId = 287500648 },
                                    new NpcWear { ShortName = "hoodie", SkinId = 2516894941 },
                                    new NpcWear { ShortName = "pants", SkinId = 2516896097 },
                                    new NpcWear { ShortName = "shoes.boots", SkinId = 0 }
                                },
                                BeltItems = new HashSet<NpcBelt>
                                {
                                    new NpcBelt { ShortName = "smg.mp5", Amount = 1, SkinId = 0, Mods = new HashSet<string> { "weapon.mod.holosight", "weapon.mod.flashlight" }, Ammo = string.Empty },
                                    new NpcBelt { ShortName = "grenade.flashbang", Amount = 1, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty },
                                    new NpcBelt { ShortName = "syringe.medical", Amount = 2, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty }
                                },
                                Kit = ""
                            },
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = false,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_oilrig.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = false,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 8, MaxAmount = 33, Chance = 36f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new PresetConfig
                        {
                            Min = 5,
                            Max = 10,
                            Positions = new HashSet<string>
                            {
                                "(-25.155, 0.115, 13.839)",
                                "(-43.684, 6, 34.418)",
                                "(-22.834, 0.115, 34.135)",
                                "(-1.027, 0.115, -8.868)",
                                "(5.559, 0.115, -40.239)",
                                "(-32.474, 3, -32.481)",
                                "(24.821, 0.115, -36.753)",
                                "(29.997, 0.115, -8.793)",
                                "(27.06, 3, 35.723)",
                                "(10.986, 0.875, 31.53)"
                            },
                            Config = new NpcConfig
                            {
                                Name = "Arcase",
                                Health = 125f,
                                RoamRange = 10f,
                                ChaseRange = 100f,
                                AttackRangeMultiplier = 0.75f,
                                SenseRange = 50f,
                                MemoryDuration = 30f,
                                DamageScale = 2f,
                                AimConeScale = 1f,
                                CheckVisionCone = false,
                                VisionCone = 135f,
                                Speed = 8.5f,
                                DisableRadio = false,
                                Stationary = false,
                                IsRemoveCorpse = true,
                                WearItems = new HashSet<NpcWear>
                                {
                                    new NpcWear { ShortName = "hazmatsuit_scientist_arctic", SkinId = 0 }
                                },
                                BeltItems = new HashSet<NpcBelt>
                                {
                                    new NpcBelt { ShortName = "shotgun.pump", Amount = 1, SkinId = 630162685, Mods = new HashSet<string> { "weapon.mod.flashlight" }, Ammo = string.Empty },
                                    new NpcBelt { ShortName = "syringe.medical", Amount = 2, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty },
                                    new NpcBelt { ShortName = "grenade.f1", Amount = 1, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty }
                                },
                                Kit = ""
                            },
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = false,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_oilrig.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = false,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 8, MaxAmount = 33, Chance = 36f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        }
                    },
                    NumberSnowmobilesOpenDoor = 3,
                    NumberSnowmobilesResearch = 3,
                    ConfigNpcSnowmobile = new AdditionalNpcConfig
                    {
                        Name = "Snowmobile Security",
                        Health = 150f,
                        ChaseRange = 60f,
                        AttackRangeMultiplier = 1f,
                        SenseRange = 40f,
                        MemoryDuration = 30f,
                        DamageScale = 0.7f,
                        AimConeScale = 1.5f,
                        Speed = 7.5f,
                        DisableRadio = false,
                        IsRemoveCorpse = true,
                        WearItems = new HashSet<NpcWear>
                        {
                            new NpcWear { ShortName = "hazmatsuit.arcticsuit", SkinId = 0 }
                        },
                        BeltItems = new HashSet<NpcBelt>
                        {
                            new NpcBelt { ShortName = "rifle.ak.ice", Amount = 1, SkinId = 2525948777, Mods = new HashSet<string> { "weapon.mod.holosight", "weapon.mod.flashlight" }, Ammo = string.Empty },
                            new NpcBelt { ShortName = "syringe.medical", Amount = 2, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty },
                            new NpcBelt { ShortName = "grenade.f1", Amount = 1, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty }
                        },
                        Kit = "",
                        TypeLootTable = 5,
                        PrefabLootTable = new PrefabLootTableConfig
                        {
                            Min = 1,
                            Max = 1,
                            UseCount = false,
                            Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_oilrig.prefab" } }
                        },
                        OwnLootTable = new LootTableConfig
                        {
                            Min = 1,
                            Max = 1,
                            UseCount = false,
                            Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 8, MaxAmount = 33, Chance = 36f, IsBluePrint = false, SkinId = 0, Name = "" } }
                        }
                    },
                    NumberNpcRepairMinicopter = 10,
                    ConfigNpcRepairMinicopter = new AdditionalNpcConfig
                    {
                        Name = "Arctic Base Security",
                        Health = 150f,
                        ChaseRange = 60f,
                        AttackRangeMultiplier = 1f,
                        SenseRange = 40f,
                        MemoryDuration = 30f,
                        DamageScale = 0.7f,
                        AimConeScale = 1.5f,
                        Speed = 7.5f,
                        DisableRadio = false,
                        IsRemoveCorpse = true,
                        WearItems = new HashSet<NpcWear>
                        {
                            new NpcWear { ShortName = "hazmatsuit.arcticsuit", SkinId = 0 }
                        },
                        BeltItems = new HashSet<NpcBelt>
                        {
                            new NpcBelt { ShortName = "rifle.ak.ice", Amount = 1, SkinId = 2525948777, Mods = new HashSet<string> { "weapon.mod.holosight", "weapon.mod.flashlight" }, Ammo = string.Empty },
                            new NpcBelt { ShortName = "syringe.medical", Amount = 2, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty },
                            new NpcBelt { ShortName = "grenade.f1", Amount = 1, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty }
                        },
                        Kit = "",
                        TypeLootTable = 5,
                        PrefabLootTable = new PrefabLootTableConfig
                        {
                            Min = 1,
                            Max = 1,
                            UseCount = false,
                            Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_oilrig.prefab" } }
                        },
                        OwnLootTable = new LootTableConfig
                        {
                            Min = 1,
                            Max = 1,
                            UseCount = false,
                            Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 8, MaxAmount = 33, Chance = 36f, IsBluePrint = false, SkinId = 0, Name = "" } }
                        }
                    },
                    ScientistName = "Scientist",
                    ScientistHealth = 450f,
                    ScientistWearItems = new HashSet<NpcWear>
                    {
                        new NpcWear { ShortName = "hoodie", SkinId = 2524481578 },
                        new NpcWear { ShortName = "pants", SkinId = 2524482916 },
                        new NpcWear { ShortName = "shoes.boots", SkinId = 1644270941 },
                        new NpcWear { ShortName = "jacket", SkinId = 2784960675 },
                        new NpcWear { ShortName = "hat.beenie", SkinId = 854338485 },
                        new NpcWear { ShortName = "mask.bandana", SkinId = 835092389 },
                        new NpcWear { ShortName = "burlap.gloves", SkinId = 2403558237 }
                    },
                    ResearchTime = 60,
                    PilotName = "Pilot",
                    PilotWearItems = new HashSet<NpcWear>
                    {
                        new NpcWear { ShortName = "shoes.boots", SkinId = 899942107 },
                        new NpcWear { ShortName = "burlap.headwrap", SkinId = 1694253807 },
                        new NpcWear { ShortName = "burlap.shirt", SkinId = 1694259867 },
                        new NpcWear { ShortName = "burlap.trousers", SkinId = 1694262795 },
                        new NpcWear { ShortName = "burlap.gloves", SkinId = 916319889 }
                    },
                    RepairTime = 60,
                    Marker = new MarkerConfig
                    {
                        Enabled = true,
                        Type = 1,
                        Radius = 0.37967f,
                        Alpha = 0.35f,
                        Color = new ColorConfig { R = 0.81f, G = 0.25f, B = 0.15f },
                        Text = "ArcticBaseEvent"
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
                        Enabled = true,
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
                        Prefix = "[ArcticBaseEvent]"
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
                            "OpenDoorPilot",
                            "OpenDoorScientist",
                            "ScientistKill"
                        }
                    },
                    Radius = 70f,
                    IsCreateZonePvp = false,
                    PveMode = new PveModeConfig
                    {
                        Pve = false,
                        Damage = 500f,
                        ScaleDamage = new Dictionary<string, float> { ["Npc"] = 1f },
                        LootCrate = false,
                        HackCrate = false,
                        LootNpc = false,
                        DamageNpc = false,
                        TargetNpc = false,
                        CanEnter = false,
                        CanEnterCooldownPlayer = true,
                        TimeExitOwner = 300,
                        AlertTime = 60,
                        RestoreUponDeath = true,
                        CooldownOwner = 86400,
                        Darkening = 12
                    },
                    NTeleportationInterrupt = true,
                    RemoveBetterNpc = true,
                    Economy = new EconomyConfig
                    {
                        Plugins = new HashSet<string> { "Economics", "Server Rewards", "IQEconomic" },
                        Min = 0,
                        Crates = new Dictionary<string, double>
                        {
                            ["dm c4"] = 0.5,
                            ["crate_elite"] = 0.4,
                            ["crate_normal"] = 0.2,
                            ["crate_normal_2"] = 0.1,
                            ["trash-pile-1"] = 0.1,
                            ["crate_tools"] = 0.1
                        },
                        Npc = 0.3,
                        LockedCrate = 0.5,
                        OpenDoorPilot = 0.4,
                        OpenDoorScientist = 0.4,
                        Commands = new HashSet<string>()
                    },
                    Commands = new HashSet<string>
                    {
                        "/remove",
                        "remove.toggle"
                    },
                    RemoveDefaultNpc = true,
                    ChanceNote = 15f,
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
                ["PreStart"] = "{0} A recon team has been sent to the island to collect samples of the burnt zombie known as Sledge. Imminent sabotage in <color=#55aaff>{1}</color>",
                ["Start"] = "{0} The special reconnaissance team has had two people (a pilot and a scientist) <color=#ce3f27>captured</color> by security in grid <color=#55aaff>{1}</color>. You need to <color=#738d43>release</color> the prisoners and <color=#738d43>provide</color> them <color=#738d43>with security</color> for evacuation from the island. According to our intelligence data, <color=#55aaff>access keys are kept by security at the Arctic base</color>. The pilot can <color=#738d43>unlock access to the supply depot</color> at the secured facility in return for your help",
                ["PreFinish"] = "{0} The Acrtic Base Event <color=#ce3f27>will end</color> in <color=#55aaff>{1}</color>!",
                ["Finish"] = "{0} The Acrtic Base Event <color=#ce3f27>has concluded</color>!",
                ["OpenDoorPilot"] = "{0} Player <color=#55aaff>{1}</color> <color=#738d43>freed</color> the pilot. They have begun preparing the helicopter for evacuation",
                ["OpenDoorScientist"] = "{0} Player <color=#55aaff>{1}</color> <color=#738d43>freed</color> the scientist. They began collecting samples for extraction",
                ["Snowmobiles"] = "{0} Reinforcements were sent by security at the Arctic base, their orders are to kill the scientist. <color=#55aaff>Protect the scientist!</color>",
                ["ScientistSuccess"] = "{0} The scientist <color=#738d43>successfully completed</color> the collection of samples and began evacuating the island",
                ["ScientistKill"] = "{0} The scientist <color=#ce3f27>has been killed</color> by security. The pilot began evacuating the island",
                ["PilotOpenLoot"] = "{0} The pilot <color=#738d43>has unlocked</color> the supply depot at this secured facility",
                ["PilotRepair"] = "{0} The escape minicopter <color=#ce3f27>needs repairs</color>, the pilot will try to fix it. Reinforcements were sent by security at the Arctic base, their orders are to kill the scientist. <color=#55aaff>Protect him at all cost!</color>",
                ["SetOwner"] = "{0} Player <color=#55aaff>{1}</color> <color=#738d43>has received</color> the owner status for the <color=#55aaff>Arctic Base Event</color>",
                ["EventActive"] = "{0} This event is active now. To finish this event (<color=#55aaff>/abstop</color>), then (<color=#55aaff>/abstart</color>) to start the next one!",
                ["EnterPVP"] = "{0} You <color=#ce3f27>have entered</color> the PVP zone, now other players <color=#ce3f27>can damage</color> you!",
                ["ExitPVP"] = "{0} You <color=#738d43>have left</color> the PVP zone, now other players <color=#738d43>cannot damage</color> you!",
                ["NTeleportation"] = "{0} You <color=#ce3f27>cannot</color> teleport into the event zone!",
                ["SendEconomy"] = "{0} You <color=#738d43>have earned</color> <color=#55aaff>{1}</color> economics for participating in the event",
                ["NoCommand"] = "{0} You <color=#ce3f27>cannot</color> use this command in the event zone!"
            }, this);

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["PreStart"] = "{0} Разведывательная группа отправлена на остров, чтобы собрать образцы сгоревшего зомби - Sledge. Диверсия произойдет через <color=#55aaff>{1}</color>",
                ["Start"] = "{0} В результате разведывательной операции группа из двух человек (пилот и ученый) <color=#ce3f27>взята в плен</color> службой охраны в квадрате <color=#55aaff>{1}</color>. Вам <color=#738d43>необходимо освободить</color> данную группу и <color=#738d43>обеспечить</color> их <color=#738d43>охраной</color> для эвакуации с острова. По нашим разведывательным данным <color=#55aaff>ключи доступа хранятся у службы охраны арктической базы</color>. В качестве благодарности пилот <color=#738d43>откроет доступ к складу с припасами</color> на охраняемом объекте",
                ["PreFinish"] = "{0} Ивент на арктической базе <color=#ce3f27>закончится</color> через <color=#55aaff>{1}</color>!",
                ["Finish"] = "{0} Ивент на арктической базе <color=#ce3f27>закончен</color>!",
                ["OpenDoorPilot"] = "{0} Игрок <color=#55aaff>{1}</color> <color=#738d43>освободил</color> пилота из плена и он начал процесс подготовки вертолета для эвакуации",
                ["OpenDoorScientist"] = "{0} Игрок <color=#55aaff>{1}</color> <color=#738d43>освободил</color> ученого из плена и он начал процесс сбора образцов",
                ["Snowmobiles"] = "{0} Службой охраны арктической базы было отправлено подкрепление, цель которого - убийство ученого. <color=#55aaff>Защитите ученого!</color>",
                ["ScientistSuccess"] = "{0} Ученый <color=#738d43>успешно окончил</color> сбор образцов и начал процесс эвакуации с острова",
                ["ScientistKill"] = "{0} Ученый <color=#ce3f27>убит</color> службой охраны. Пилот начала процесс эвакуации с острова",
                ["PilotOpenLoot"] = "{0} Пилот <color=#738d43>открыл</color> доступ к складу с припасами на охраняемом объекте",
                ["PilotRepair"] = "{0} Миникоптер <color=#ce3f27>сломался</color>, пилот попытается починить его. Службой охраны арктической базы было отправлено подкрепление, цель которого - убийство ученого. <color=#55aaff>Защитите ученого!</color>",
                ["SetOwner"] = "{0} Игрок <color=#55aaff>{1}</color> <color=#738d43>получил</color> статус владельца ивента для <color=#55aaff>Arctic Base Event</color>",
                ["EventActive"] = "{0} Ивент в данный момент активен, сначала завершите текущий ивент (<color=#55aaff>/abstop</color>), чтобы начать следующий!",
                ["EnterPVP"] = "{0} Вы <color=#ce3f27>вошли</color> в PVP зону, теперь другие игроки <color=#ce3f27>могут</color> наносить вам урон!",
                ["ExitPVP"] = "{0} Вы <color=#738d43>вышли</color> из PVP зоны, теперь другие игроки <color=#738d43>не могут</color> наносить вам урон!",
                ["NTeleportation"] = "{0} Вы <color=#ce3f27>не можете</color> телепортироваться в зоне ивента!",
                ["SendEconomy"] = "{0} Вы <color=#738d43>получили</color> <color=#55aaff>{1}</color> баллов в экономику за прохождение ивента",
                ["NoCommand"] = "{0} Вы <color=#ce3f27>не можете</color> использовать данную команду в зоне ивента!"
            }, this, "ru");
        }

        private string GetMessage(string langKey, string userId) => lang.GetMessage(langKey, _ins, userId);

        private string GetMessage(string langKey, string userId, params object[] args) => (args.Length == 0) ? GetMessage(langKey, userId) : string.Format(GetMessage(langKey, userId), args);
        #endregion Lang

        #region Oxide Hooks
        private static ArcticBaseEvent _ins;

        private void Init()
        {
            _ins = this;
            ToggleHooks(false);
        }

        private void OnServerInitialized()
        {
            if (GetMonument() == null)
            {
                PrintError("The Arctic Research Base location is missing on the map. The plugin cannot be loaded!");
                NextTick(() => Interface.Oxide.UnloadPlugin(Name));
                return;
            }
            CheckAllLootTables();
            ServerMgr.Instance.StartCoroutine(DownloadImages());
            StartTimer();
        }

        private void Unload()
        {
            if (Controller != null) Finish();
            _ins = null;
        }

        private object OnEntityTakeDamage(Snowmobile entity, HitInfo info)
        {
            if (entity != null && Controller.Snowmobiles.Any(x => x.Snowmobile == entity)) return true;
            else return null;
        }

        private object OnEntityTakeDamage(Minicopter entity, HitInfo info)
        {
            if (entity != null && entity == Controller.MiniCopter) return true;
            else return null;
        }

        private object OnEntityTakeDamage(ScientistNPC entity, HitInfo info)
        {
            if (entity != null && Controller.Snowmobiles.Any(x => x.Driver == entity || x.Passenger == entity)) return true;
            else return null;
        }

        private object OnEntityTakeDamage(BasePlayer entity, HitInfo info)
        {
            if (entity == null || info == null) return null;
            if (entity == Controller.Pilot) return true;
            if (entity == Controller.Scientist)
            {
                ScientistNPC attacker = info.Initiator as ScientistNPC;
                if (attacker != null && Controller.Scientists.Contains(attacker)) Controller.TakeDamageScientist(info.damageTypes.Total());
                return true;
            }
            return null;
        }

        private void OnCodeEntered(CodeLock codeLock, BasePlayer player, string code)
        {
            if (codeLock == null || !player.IsPlayer() || string.IsNullOrEmpty(code)) return;
            Controller.TryToOpenDoor(player, codeLock, code);
        }

        private object OnCardSwipe(CardReader cardReader, Keycard card, BasePlayer player)
        {
            if (cardReader != null && (cardReader == Controller.CardReader1 || cardReader == Controller.CardReader2)) return true;
            else return null;
        }

        private object OnVehiclePush(Minicopter vehicle, BasePlayer player)
        {
            if (vehicle != null && vehicle == Controller.MiniCopter) return true;
            else return null;
        }

        private object CanMountEntity(BasePlayer player, BaseVehicleSeat entity)
        {
            if (player != null && (player == Controller.Pilot || player == Controller.Scientist)) return null;
            if (entity != null && (entity == Controller.PilotSeat || entity == Controller.ScientistSeat)) return true;
            return null;
        }

        private object OnPlayerWound(BasePlayer player, HitInfo info)
        {
            if (player != null && player == Controller.Scientist) return true;
            else return null;
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
            BasePlayer attacker = info.InitiatorPlayer;
            if (Controller.Scientists.Contains(npc) && attacker.IsPlayer()) ActionEconomy(attacker.userID, "Npc");
        }

        private Dictionary<ulong, BasePlayer> StartHackCrates { get; } = new Dictionary<ulong, BasePlayer>();

        private void CanHackCrate(BasePlayer player, HackableLockedCrate crate)
        {
            if (!player.IsPlayer() || crate == null) return;
            if (crate == Controller.HackCrate)
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
            }
        }

        private HashSet<ulong> LootableCrates { get; } = new HashSet<ulong>();

        private void OnLootEntity(BasePlayer player, LootContainer container)
        {
            if (!player.IsPlayer() || !container.IsExists() || LootableCrates.Contains(container.net.ID.Value)) return;
            if (Controller.Crates.Any(x => x.Key == container))
            {
                LootableCrates.Add(container.net.ID.Value);
                ActionEconomy(player.userID, "Crates", container.ShortPrefabName);
            }
        }

        private void OnEntityKill(LootContainer entity) { if (entity != null && Controller.Crates.ContainsKey(entity)) Controller.Crates.Remove(entity); }

        private object OnNpcTarget(BaseEntity attacker, BasePlayer victim)
        {
            if (attacker == null || victim == null) return null;
            if (victim == Controller.Pilot || victim == Controller.Scientist) return true;
            return null;
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
        #endregion Oxide Hooks

        #region Controller
        internal HashSet<Vector3> Marker { get; } = new HashSet<Vector3>
        {
            new Vector3(46f, 0f, 12),
            new Vector3(46f, 0f, 10),
            new Vector3(46f, 0f, 8),
            new Vector3(46f, 0f, 6),
            new Vector3(46f, 0f, 4),
            new Vector3(46f, 0f, 2),
            new Vector3(46f, 0f, 0),
            new Vector3(46f, 0f, -2),
            new Vector3(46f, 0f, -4),
            new Vector3(46f, 0f, -6),
            new Vector3(46f, 0f, -8),
            new Vector3(46f, 0f, -10),
            new Vector3(46f, 0f, -12),
            new Vector3(44f, 0f, 18),
            new Vector3(44f, 0f, 16),
            new Vector3(44f, 0f, 14),
            new Vector3(44f, 0f, 12),
            new Vector3(44f, 0f, 10),
            new Vector3(44f, 0f, 8),
            new Vector3(44f, 0f, -6),
            new Vector3(44f, 0f, -8),
            new Vector3(44f, 0f, -10),
            new Vector3(44f, 0f, -12),
            new Vector3(44f, 0f, -14),
            new Vector3(44f, 0f, -16),
            new Vector3(44f, 0f, -18),
            new Vector3(42f, 0f, 22),
            new Vector3(42f, 0f, 20),
            new Vector3(42f, 0f, 18),
            new Vector3(42f, 0f, 16),
            new Vector3(42f, 0f, -16),
            new Vector3(42f, 0f, -18),
            new Vector3(42f, 0f, -20),
            new Vector3(42f, 0f, -22),
            new Vector3(40f, 0f, 26),
            new Vector3(40f, 0f, 24),
            new Vector3(40f, 0f, 22),
            new Vector3(40f, 0f, 20),
            new Vector3(40f, 0f, -20),
            new Vector3(40f, 0f, -22),
            new Vector3(40f, 0f, -24),
            new Vector3(40f, 0f, -26),
            new Vector3(38f, 0f, 28),
            new Vector3(38f, 0f, 26),
            new Vector3(38f, 0f, 24),
            new Vector3(38f, 0f, -24),
            new Vector3(38f, 0f, -26),
            new Vector3(38f, 0f, -28),
            new Vector3(36f, 0f, 32),
            new Vector3(36f, 0f, 30),
            new Vector3(36f, 0f, 28),
            new Vector3(36f, 0f, -26),
            new Vector3(36f, 0f, -28),
            new Vector3(36f, 0f, -30),
            new Vector3(34f, 0f, 34),
            new Vector3(34f, 0f, 32),
            new Vector3(34f, 0f, 30),
            new Vector3(34f, 0f, 4),
            new Vector3(34f, 0f, 2),
            new Vector3(34f, 0f, 0),
            new Vector3(34f, 0f, -14),
            new Vector3(34f, 0f, -16),
            new Vector3(34f, 0f, -30),
            new Vector3(34f, 0f, -32),
            new Vector3(34f, 0f, -34),
            new Vector3(32f, 0f, 36),
            new Vector3(32f, 0f, 34),
            new Vector3(32f, 0f, 32),
            new Vector3(32f, 0f, 4),
            new Vector3(32f, 0f, 2),
            new Vector3(32f, 0f, 0),
            new Vector3(32f, 0f, -2),
            new Vector3(32f, 0f, -14),
            new Vector3(32f, 0f, -16),
            new Vector3(32f, 0f, -32),
            new Vector3(32f, 0f, -34),
            new Vector3(30f, 0f, 36),
            new Vector3(30f, 0f, 34),
            new Vector3(30f, 0f, 6),
            new Vector3(30f, 0f, 4),
            new Vector3(30f, 0f, 2),
            new Vector3(30f, 0f, 0),
            new Vector3(30f, 0f, -2),
            new Vector3(30f, 0f, -4),
            new Vector3(30f, 0f, -14),
            new Vector3(30f, 0f, -16),
            new Vector3(30f, 0f, -18),
            new Vector3(30f, 0f, -34),
            new Vector3(30f, 0f, -36),
            new Vector3(28f, 0f, 38),
            new Vector3(28f, 0f, 36),
            new Vector3(28f, 0f, 6),
            new Vector3(28f, 0f, 4),
            new Vector3(28f, 0f, -2),
            new Vector3(28f, 0f, -4),
            new Vector3(28f, 0f, -6),
            new Vector3(28f, 0f, -16),
            new Vector3(28f, 0f, -18),
            new Vector3(28f, 0f, -36),
            new Vector3(28f, 0f, -38),
            new Vector3(26f, 0f, 40),
            new Vector3(26f, 0f, 38),
            new Vector3(26f, 0f, 8),
            new Vector3(26f, 0f, 6),
            new Vector3(26f, 0f, 4),
            new Vector3(26f, 0f, -4),
            new Vector3(26f, 0f, -6),
            new Vector3(26f, 0f, -8),
            new Vector3(26f, 0f, -16),
            new Vector3(26f, 0f, -18),
            new Vector3(26f, 0f, -36),
            new Vector3(26f, 0f, -38),
            new Vector3(26f, 0f, -40),
            new Vector3(24f, 0f, 42),
            new Vector3(24f, 0f, 40),
            new Vector3(24f, 0f, 38),
            new Vector3(24f, 0f, 8),
            new Vector3(24f, 0f, 6),
            new Vector3(24f, 0f, -6),
            new Vector3(24f, 0f, -8),
            new Vector3(24f, 0f, -16),
            new Vector3(24f, 0f, -18),
            new Vector3(24f, 0f, -38),
            new Vector3(24f, 0f, -40),
            new Vector3(22f, 0f, 42),
            new Vector3(22f, 0f, 40),
            new Vector3(22f, 0f, 8),
            new Vector3(22f, 0f, 6),
            new Vector3(22f, 0f, -6),
            new Vector3(22f, 0f, -8),
            new Vector3(22f, 0f, -16),
            new Vector3(22f, 0f, -18),
            new Vector3(22f, 0f, -40),
            new Vector3(22f, 0f, -42),
            new Vector3(20f, 0f, 44),
            new Vector3(20f, 0f, 42),
            new Vector3(20f, 0f, 8),
            new Vector3(20f, 0f, 6),
            new Vector3(20f, 0f, -6),
            new Vector3(20f, 0f, -8),
            new Vector3(20f, 0f, -14),
            new Vector3(20f, 0f, -16),
            new Vector3(20f, 0f, -18),
            new Vector3(20f, 0f, -40),
            new Vector3(20f, 0f, -42),
            new Vector3(18f, 0f, 44),
            new Vector3(18f, 0f, 42),
            new Vector3(18f, 0f, 10),
            new Vector3(18f, 0f, 8),
            new Vector3(18f, 0f, 6),
            new Vector3(18f, 0f, -8),
            new Vector3(18f, 0f, -10),
            new Vector3(18f, 0f, -12),
            new Vector3(18f, 0f, -14),
            new Vector3(18f, 0f, -16),
            new Vector3(18f, 0f, -18),
            new Vector3(18f, 0f, -42),
            new Vector3(18f, 0f, -44),
            new Vector3(16f, 0f, 44),
            new Vector3(16f, 0f, 42),
            new Vector3(16f, 0f, 12),
            new Vector3(16f, 0f, 10),
            new Vector3(16f, 0f, 8),
            new Vector3(16f, 0f, -8),
            new Vector3(16f, 0f, -10),
            new Vector3(16f, 0f, -12),
            new Vector3(16f, 0f, -14),
            new Vector3(16f, 0f, -16),
            new Vector3(16f, 0f, -18),
            new Vector3(16f, 0f, -42),
            new Vector3(16f, 0f, -44),
            new Vector3(14f, 0f, 46),
            new Vector3(14f, 0f, 44),
            new Vector3(14f, 0f, 14),
            new Vector3(14f, 0f, 12),
            new Vector3(14f, 0f, 10),
            new Vector3(14f, 0f, -8),
            new Vector3(14f, 0f, -10),
            new Vector3(14f, 0f, -12),
            new Vector3(14f, 0f, -16),
            new Vector3(14f, 0f, -18),
            new Vector3(14f, 0f, -44),
            new Vector3(14f, 0f, -46),
            new Vector3(12f, 0f, 46),
            new Vector3(12f, 0f, 44),
            new Vector3(12f, 0f, 16),
            new Vector3(12f, 0f, 14),
            new Vector3(12f, 0f, 12),
            new Vector3(12f, 0f, -8),
            new Vector3(12f, 0f, -10),
            new Vector3(12f, 0f, -16),
            new Vector3(12f, 0f, -18),
            new Vector3(12f, 0f, -44),
            new Vector3(12f, 0f, -46),
            new Vector3(10f, 0f, 46),
            new Vector3(10f, 0f, 44),
            new Vector3(10f, 0f, 18),
            new Vector3(10f, 0f, 16),
            new Vector3(10f, 0f, 14),
            new Vector3(10f, 0f, 12),
            new Vector3(10f, 0f, -8),
            new Vector3(10f, 0f, -10),
            new Vector3(10f, 0f, -16),
            new Vector3(10f, 0f, -18),
            new Vector3(10f, 0f, -44),
            new Vector3(10f, 0f, -46),
            new Vector3(8f, 0f, 48),
            new Vector3(8f, 0f, 46),
            new Vector3(8f, 0f, 20),
            new Vector3(8f, 0f, 18),
            new Vector3(8f, 0f, 16),
            new Vector3(8f, 0f, 14),
            new Vector3(8f, 0f, 12),
            new Vector3(8f, 0f, 10),
            new Vector3(8f, 0f, -8),
            new Vector3(8f, 0f, -10),
            new Vector3(8f, 0f, -16),
            new Vector3(8f, 0f, -18),
            new Vector3(8f, 0f, -44),
            new Vector3(8f, 0f, -46),
            new Vector3(6f, 0f, 48),
            new Vector3(6f, 0f, 46),
            new Vector3(6f, 0f, 20),
            new Vector3(6f, 0f, 18),
            new Vector3(6f, 0f, 12),
            new Vector3(6f, 0f, 10),
            new Vector3(6f, 0f, 8),
            new Vector3(6f, 0f, -8),
            new Vector3(6f, 0f, -10),
            new Vector3(6f, 0f, -46),
            new Vector3(6f, 0f, -48),
            new Vector3(4f, 0f, 48),
            new Vector3(4f, 0f, 46),
            new Vector3(4f, 0f, 20),
            new Vector3(4f, 0f, 18),
            new Vector3(4f, 0f, 10),
            new Vector3(4f, 0f, 8),
            new Vector3(4f, 0f, 6),
            new Vector3(4f, 0f, -8),
            new Vector3(4f, 0f, -10),
            new Vector3(4f, 0f, -46),
            new Vector3(4f, 0f, -48),
            new Vector3(2f, 0f, 48),
            new Vector3(2f, 0f, 46),
            new Vector3(2f, 0f, 20),
            new Vector3(2f, 0f, 18),
            new Vector3(2f, 0f, 8),
            new Vector3(2f, 0f, 6),
            new Vector3(2f, 0f, 4),
            new Vector3(2f, 0f, -8),
            new Vector3(2f, 0f, -10),
            new Vector3(2f, 0f, -46),
            new Vector3(2f, 0f, -48),
            new Vector3(0f, 0f, 48),
            new Vector3(0f, 0f, 46),
            new Vector3(0f, 0f, 20),
            new Vector3(0f, 0f, 18),
            new Vector3(0f, 0f, 8),
            new Vector3(0f, 0f, 6),
            new Vector3(0f, 0f, 4),
            new Vector3(0f, 0f, 2),
            new Vector3(0f, 0f, -6),
            new Vector3(0f, 0f, -8),
            new Vector3(0f, 0f, -10),
            new Vector3(0f, 0f, -46),
            new Vector3(0f, 0f, -48),
            new Vector3(-2f, 0f, 48),
            new Vector3(-2f, 0f, 46),
            new Vector3(-2f, 0f, 18),
            new Vector3(-2f, 0f, 8),
            new Vector3(-2f, 0f, 6),
            new Vector3(-2f, 0f, 4),
            new Vector3(-2f, 0f, 2),
            new Vector3(-2f, 0f, 0),
            new Vector3(-2f, 0f, -6),
            new Vector3(-2f, 0f, -8),
            new Vector3(-2f, 0f, -10),
            new Vector3(-2f, 0f, -12),
            new Vector3(-2f, 0f, -46),
            new Vector3(-2f, 0f, -48),
            new Vector3(-4f, 0f, 48),
            new Vector3(-4f, 0f, 46),
            new Vector3(-4f, 0f, 8),
            new Vector3(-4f, 0f, 6),
            new Vector3(-4f, 0f, 2),
            new Vector3(-4f, 0f, 0),
            new Vector3(-4f, 0f, -2),
            new Vector3(-4f, 0f, -4),
            new Vector3(-4f, 0f, -6),
            new Vector3(-4f, 0f, -8),
            new Vector3(-4f, 0f, -10),
            new Vector3(-4f, 0f, -12),
            new Vector3(-4f, 0f, -14),
            new Vector3(-4f, 0f, -46),
            new Vector3(-4f, 0f, -48),
            new Vector3(-6f, 0f, 48),
            new Vector3(-6f, 0f, 46),
            new Vector3(-6f, 0f, 8),
            new Vector3(-6f, 0f, 6),
            new Vector3(-6f, 0f, -2),
            new Vector3(-6f, 0f, -4),
            new Vector3(-6f, 0f, -6),
            new Vector3(-6f, 0f, -12),
            new Vector3(-6f, 0f, -14),
            new Vector3(-6f, 0f, -16),
            new Vector3(-6f, 0f, -46),
            new Vector3(-6f, 0f, -48),
            new Vector3(-8f, 0f, 48),
            new Vector3(-8f, 0f, 46),
            new Vector3(-8f, 0f, 8),
            new Vector3(-8f, 0f, 6),
            new Vector3(-8f, 0f, -4),
            new Vector3(-8f, 0f, -6),
            new Vector3(-8f, 0f, -14),
            new Vector3(-8f, 0f, -16),
            new Vector3(-8f, 0f, -44),
            new Vector3(-8f, 0f, -46),
            new Vector3(-10f, 0f, 46),
            new Vector3(-10f, 0f, 44),
            new Vector3(-10f, 0f, 8),
            new Vector3(-10f, 0f, 6),
            new Vector3(-10f, 0f, -4),
            new Vector3(-10f, 0f, -6),
            new Vector3(-10f, 0f, -16),
            new Vector3(-10f, 0f, -18),
            new Vector3(-10f, 0f, -44),
            new Vector3(-10f, 0f, -46),
            new Vector3(-12f, 0f, 46),
            new Vector3(-12f, 0f, 44),
            new Vector3(-12f, 0f, 10),
            new Vector3(-12f, 0f, 8),
            new Vector3(-12f, 0f, 6),
            new Vector3(-12f, 0f, -2),
            new Vector3(-12f, 0f, -4),
            new Vector3(-12f, 0f, -6),
            new Vector3(-12f, 0f, -16),
            new Vector3(-12f, 0f, -18),
            new Vector3(-12f, 0f, -44),
            new Vector3(-12f, 0f, -46),
            new Vector3(-14f, 0f, 46),
            new Vector3(-14f, 0f, 44),
            new Vector3(-14f, 0f, 12),
            new Vector3(-14f, 0f, 10),
            new Vector3(-14f, 0f, 8),
            new Vector3(-14f, 0f, -2),
            new Vector3(-14f, 0f, -4),
            new Vector3(-14f, 0f, -16),
            new Vector3(-14f, 0f, -18),
            new Vector3(-14f, 0f, -44),
            new Vector3(-14f, 0f, -46),
            new Vector3(-16f, 0f, 46),
            new Vector3(-16f, 0f, 44),
            new Vector3(-16f, 0f, 12),
            new Vector3(-16f, 0f, 10),
            new Vector3(-16f, 0f, -2),
            new Vector3(-16f, 0f, -4),
            new Vector3(-16f, 0f, -16),
            new Vector3(-16f, 0f, -18),
            new Vector3(-16f, 0f, -42),
            new Vector3(-16f, 0f, -44),
            new Vector3(-18f, 0f, 44),
            new Vector3(-18f, 0f, 42),
            new Vector3(-18f, 0f, 12),
            new Vector3(-18f, 0f, 10),
            new Vector3(-18f, 0f, 0),
            new Vector3(-18f, 0f, -2),
            new Vector3(-18f, 0f, -4),
            new Vector3(-18f, 0f, -6),
            new Vector3(-18f, 0f, -16),
            new Vector3(-18f, 0f, -18),
            new Vector3(-18f, 0f, -42),
            new Vector3(-18f, 0f, -44),
            new Vector3(-20f, 0f, 44),
            new Vector3(-20f, 0f, 42),
            new Vector3(-20f, 0f, 12),
            new Vector3(-20f, 0f, 10),
            new Vector3(-20f, 0f, 0),
            new Vector3(-20f, 0f, -2),
            new Vector3(-20f, 0f, -4),
            new Vector3(-20f, 0f, -6),
            new Vector3(-20f, 0f, -16),
            new Vector3(-20f, 0f, -18),
            new Vector3(-20f, 0f, -42),
            new Vector3(-20f, 0f, -44),
            new Vector3(-22f, 0f, 42),
            new Vector3(-22f, 0f, 40),
            new Vector3(-22f, 0f, 12),
            new Vector3(-22f, 0f, 10),
            new Vector3(-22f, 0f, 0),
            new Vector3(-22f, 0f, -2),
            new Vector3(-22f, 0f, -4),
            new Vector3(-22f, 0f, -6),
            new Vector3(-22f, 0f, -8),
            new Vector3(-22f, 0f, -16),
            new Vector3(-22f, 0f, -18),
            new Vector3(-22f, 0f, -40),
            new Vector3(-22f, 0f, -42),
            new Vector3(-24f, 0f, 42),
            new Vector3(-24f, 0f, 40),
            new Vector3(-24f, 0f, 12),
            new Vector3(-24f, 0f, 10),
            new Vector3(-24f, 0f, 2),
            new Vector3(-24f, 0f, 0),
            new Vector3(-24f, 0f, -2),
            new Vector3(-24f, 0f, -6),
            new Vector3(-24f, 0f, -8),
            new Vector3(-24f, 0f, -16),
            new Vector3(-24f, 0f, -18),
            new Vector3(-24f, 0f, -38),
            new Vector3(-24f, 0f, -40),
            new Vector3(-24f, 0f, -42),
            new Vector3(-26f, 0f, 40),
            new Vector3(-26f, 0f, 38),
            new Vector3(-26f, 0f, 12),
            new Vector3(-26f, 0f, 10),
            new Vector3(-26f, 0f, 2),
            new Vector3(-26f, 0f, 0),
            new Vector3(-26f, 0f, -6),
            new Vector3(-26f, 0f, -8),
            new Vector3(-26f, 0f, -10),
            new Vector3(-26f, 0f, -16),
            new Vector3(-26f, 0f, -18),
            new Vector3(-26f, 0f, -38),
            new Vector3(-26f, 0f, -40),
            new Vector3(-28f, 0f, 40),
            new Vector3(-28f, 0f, 38),
            new Vector3(-28f, 0f, 36),
            new Vector3(-28f, 0f, 12),
            new Vector3(-28f, 0f, 10),
            new Vector3(-28f, 0f, 8),
            new Vector3(-28f, 0f, 2),
            new Vector3(-28f, 0f, 0),
            new Vector3(-28f, 0f, -8),
            new Vector3(-28f, 0f, -10),
            new Vector3(-28f, 0f, -16),
            new Vector3(-28f, 0f, -18),
            new Vector3(-28f, 0f, -36),
            new Vector3(-28f, 0f, -38),
            new Vector3(-30f, 0f, 38),
            new Vector3(-30f, 0f, 36),
            new Vector3(-30f, 0f, 10),
            new Vector3(-30f, 0f, 8),
            new Vector3(-30f, 0f, 6),
            new Vector3(-30f, 0f, 4),
            new Vector3(-30f, 0f, 2),
            new Vector3(-30f, 0f, 0),
            new Vector3(-30f, 0f, -10),
            new Vector3(-30f, 0f, -12),
            new Vector3(-30f, 0f, -16),
            new Vector3(-30f, 0f, -18),
            new Vector3(-30f, 0f, -34),
            new Vector3(-30f, 0f, -36),
            new Vector3(-30f, 0f, -38),
            new Vector3(-32f, 0f, 36),
            new Vector3(-32f, 0f, 34),
            new Vector3(-32f, 0f, 8),
            new Vector3(-32f, 0f, 6),
            new Vector3(-32f, 0f, 4),
            new Vector3(-32f, 0f, 2),
            new Vector3(-32f, 0f, 0),
            new Vector3(-32f, 0f, -10),
            new Vector3(-32f, 0f, -12),
            new Vector3(-32f, 0f, -16),
            new Vector3(-32f, 0f, -18),
            new Vector3(-32f, 0f, -32),
            new Vector3(-32f, 0f, -34),
            new Vector3(-32f, 0f, -36),
            new Vector3(-34f, 0f, 34),
            new Vector3(-34f, 0f, 32),
            new Vector3(-34f, 0f, 6),
            new Vector3(-34f, 0f, 4),
            new Vector3(-34f, 0f, 2),
            new Vector3(-34f, 0f, -12),
            new Vector3(-34f, 0f, -14),
            new Vector3(-34f, 0f, -16),
            new Vector3(-34f, 0f, -18),
            new Vector3(-34f, 0f, -30),
            new Vector3(-34f, 0f, -32),
            new Vector3(-34f, 0f, -34),
            new Vector3(-36f, 0f, 32),
            new Vector3(-36f, 0f, 30),
            new Vector3(-36f, 0f, 28),
            new Vector3(-36f, 0f, -28),
            new Vector3(-36f, 0f, -30),
            new Vector3(-36f, 0f, -32),
            new Vector3(-38f, 0f, 30),
            new Vector3(-38f, 0f, 28),
            new Vector3(-38f, 0f, 26),
            new Vector3(-38f, 0f, -26),
            new Vector3(-38f, 0f, -28),
            new Vector3(-38f, 0f, -30),
            new Vector3(-40f, 0f, 26),
            new Vector3(-40f, 0f, 24),
            new Vector3(-40f, 0f, 22),
            new Vector3(-40f, 0f, -22),
            new Vector3(-40f, 0f, -24),
            new Vector3(-40f, 0f, -26),
            new Vector3(-42f, 0f, 24),
            new Vector3(-42f, 0f, 22),
            new Vector3(-42f, 0f, 20),
            new Vector3(-42f, 0f, 18),
            new Vector3(-42f, 0f, -18),
            new Vector3(-42f, 0f, -20),
            new Vector3(-42f, 0f, -22),
            new Vector3(-42f, 0f, -24),
            new Vector3(-44f, 0f, 20),
            new Vector3(-44f, 0f, 18),
            new Vector3(-44f, 0f, 16),
            new Vector3(-44f, 0f, 14),
            new Vector3(-44f, 0f, 12),
            new Vector3(-44f, 0f, -12),
            new Vector3(-44f, 0f, -14),
            new Vector3(-44f, 0f, -16),
            new Vector3(-44f, 0f, -18),
            new Vector3(-44f, 0f, -20),
            new Vector3(-46f, 0f, 16),
            new Vector3(-46f, 0f, 14),
            new Vector3(-46f, 0f, 12),
            new Vector3(-46f, 0f, 10),
            new Vector3(-46f, 0f, 8),
            new Vector3(-46f, 0f, 6),
            new Vector3(-46f, 0f, 4),
            new Vector3(-46f, 0f, 2),
            new Vector3(-46f, 0f, 0),
            new Vector3(-46f, 0f, -2),
            new Vector3(-46f, 0f, -4),
            new Vector3(-46f, 0f, -6),
            new Vector3(-46f, 0f, -8),
            new Vector3(-46f, 0f, -10),
            new Vector3(-46f, 0f, -12),
            new Vector3(-46f, 0f, -14),
            new Vector3(-46f, 0f, -16),
            new Vector3(-48f, 0f, 10),
            new Vector3(-48f, 0f, 8),
            new Vector3(-48f, 0f, 6),
            new Vector3(-48f, 0f, 4),
            new Vector3(-48f, 0f, 2),
            new Vector3(-48f, 0f, 0),
            new Vector3(-48f, 0f, -2),
            new Vector3(-48f, 0f, -4),
            new Vector3(-48f, 0f, -6),
            new Vector3(-48f, 0f, -8)
        };

        private ControllerArcticBaseEvent Controller { get; set; } = null;
        private bool Active { get; set; } = false;

        private void StartTimer()
        {
            if (!_config.EnabledTimer) return;
            timer.In(UnityEngine.Random.Range(_config.MinStartTime, _config.MaxStartTime), () =>
            {
                if (!Active) Start(null);
                else Puts("This event is active now. To finish this event (abstop), then to start the next one");
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
                if (_config.RemoveBetterNpc && plugins.Exists("BetterNpc")) BetterNpc.Call("DestroyController", "Arctic Research Base");
                ToggleHooks(true);
                Controller = new GameObject().AddComponent<ControllerArcticBaseEvent>();
                if (plugins.Exists("MonumentOwner")) MonumentOwner.Call("RemoveZone", Controller.Monument);
                Controller.EnablePveMode(_config.PveMode, player);
                Interface.Oxide.CallHook($"On{Name}Start", Controller.transform.position, _config.Radius);
                AlertToAllPlayers("Start", _config.Chat.Prefix, MapHelper.GridToString(MapHelper.PositionToGrid(Controller.transform.position)));
            });
        }

        private void Finish()
        {
            ToggleHooks(false);
            if (ActivePveMode) PveMode.Call("EventRemovePveMode", Name, true);
            if (Controller != null)
            {
                if (plugins.Exists("MonumentOwner")) MonumentOwner.Call("CreateZone", Controller.Monument);
                EnableRadiation(Controller.Puzzle);
                UnityEngine.Object.Destroy(Controller.gameObject);
            }
            Active = false;
            SendBalance();
            LootableCrates.Clear();
            AlertToAllPlayers("Finish", _config.Chat.Prefix);
            Interface.Oxide.CallHook($"On{Name}End");
            if (_config.RemoveBetterNpc && plugins.Exists("BetterNpc")) BetterNpc.Call("CreateController", "Arctic Research Base");
            Puts($"{Name} has ended");
            StartTimer();
        }

        internal class ControllerArcticBaseEvent : FacepunchBehaviour
        {
            private PluginConfig _config => _ins._config;

            internal MonumentInfo Monument { get; set; } = null;
            internal PuzzleReset Puzzle { get; set; } = null;

            private SphereCollider SphereCollider { get; set; } = null;

            private VendingMachineMapMarker VendingMarker { get; set; } = null;
            private HashSet<MapMarkerGenericRadius> Markers { get; } = new HashSet<MapMarkerGenericRadius>();

            internal Door MainDoor1 { get; set; } = null;
            internal Door MainDoor2 { get; set; } = null;

            internal CardReader CardReader1 { get; set; } = null;
            internal CardReader CardReader2 { get; set; } = null;

            #region Minicopter Variables
            internal Minicopter MiniCopter { get; set; } = null;
            private AnimationTransformVehicle AnimationMinicopter { get; set; } = null;
            internal BaseVehicleSeat PilotSeat { get; set; } = null;
            internal BaseVehicleSeat ScientistSeat { get; set; } = null;
            private HashSet<PointAnimationTransform> MinicopterLocalPointsToCardReader2 { get; } = new HashSet<PointAnimationTransform>
            {
                new PointAnimationTransform { Pos = new Vector3(24f, 1.425f, 2.28f), Rot = new Vector3(0f, 180f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(24f, 1.441f, 2.04f), Rot = new Vector3(357f, 180f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(24f, 1.441f, 1.792f), Rot = new Vector3(357f, 180f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(24f, 1.425f, 1.48f), Rot = new Vector3(0f, 180f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(24f, 1.392f, -0.197f), Rot = new Vector3(2.5f, 180f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(24f, 1.272f, -0.545f), Rot = new Vector3(18.16f, 180f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(24f, 1.252f, -0.914f), Rot = new Vector3(21.565f, 180f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(24f, 0.984f, -1.675f), Rot = new Vector3(22.716f, 180f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(24f, 0.1f, -4.575f), Rot = new Vector3(0f, 180f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(24.207f, 0.1f, -5.295f), Rot = new Vector3(0f, 170f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(24.534f, 0.1f, -5.958f), Rot = new Vector3(0f, 160f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(24.977f, 0.1f, -6.583f), Rot = new Vector3(0f, 150f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(25.516f, 0.1f, -7.111f), Rot = new Vector3(0f, 140f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(26.131f, 0.1f, -7.532f), Rot = new Vector3(0f, 130f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(26.817f, 0.1f, -7.855f), Rot = new Vector3(0f, 120f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(27.538f, 0.1f, -8.047f), Rot = new Vector3(0f, 110f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(28.277f, 0.1f, -8.11f), Rot = new Vector3(0f, 100f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(29.01f, 0.1f, -8.04f), Rot = new Vector3(0f, 90f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(39f, 0.1f, -8.04f), Rot = new Vector3(0f, 90f, 0f) }
            };
            private HashSet<PointAnimationTransform> MinicopterGlobalPointsToCardReader2 { get; } = new HashSet<PointAnimationTransform>();
            private HashSet<PointAnimationTransform> MinicopterLocalPointsToExit { get; } = new HashSet<PointAnimationTransform>
            {
                new PointAnimationTransform { Pos = new Vector3(39f, 1.238f, -8.04f), Rot = new Vector3(0f, 70f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(39f, 2.518f, -8.04f), Rot = new Vector3(0f, 50f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(39f, 3.978f, -8.04f), Rot = new Vector3(0f, 30f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(39f, 5.25f, -8.04f), Rot = new Vector3(0f, 10f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(39f, 6.565f, -8.04f), Rot = new Vector3(0f, 350f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(39f, 7.97f, -8.04f), Rot = new Vector3(0f, 330f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(39f, 9.372f, -8.04f), Rot = new Vector3(0f, 310f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(39f, 10.769f, -8.04f), Rot = new Vector3(0f, 290f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(39f, 12.246f, -8.04f), Rot = new Vector3(0f, 270f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(37.472f, 12.246f, -8.04f), Rot = new Vector3(2f, 270f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(34.257f, 12.246f, -8.04f), Rot = new Vector3(4f, 270f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(30.725f, 12.246f, -8.04f), Rot = new Vector3(6f, 270f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(27.359f, 12.246f, -8.04f), Rot = new Vector3(8f, 270f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(24.147f, 12.246f, -8.04f), Rot = new Vector3(10f, 270f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(21.096f, 12.246f, -8.04f), Rot = new Vector3(12f, 270f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(17.943f, 12.246f, -8.04f), Rot = new Vector3(14f, 270f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(14.346f, 12.246f, -8.04f), Rot = new Vector3(15.923f, 270f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(11.04f, 12.246f, -8.04f), Rot = new Vector3(15.923f, 270f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(6.98f, 12.246f, -8.04f), Rot = new Vector3(15.923f, 270f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(3.172f, 12.246f, -8.462f), Rot = new Vector3(15.923f, 260f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(-0.324f, 12.246f, -9.308f), Rot = new Vector3(15.923f, 250f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(-3.523f, 12.246f, -10.658f), Rot = new Vector3(15.923f, 240f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(-6.45f, 12.246f, -12.485f), Rot = new Vector3(15.923f, 230f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(-9.569f, 12.246f, -15.102f), Rot = new Vector3(15.923f, 230f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(-12.416f, 12.246f, -17.491f), Rot = new Vector3(15.923f, 230f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(-15.922f, 12.246f, -20.433f), Rot = new Vector3(15.923f, 230f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(-20.38f, 12.246f, -24.173f), Rot = new Vector3(15.923f, 230f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(-25.366f, 12.246f, -28.357f), Rot = new Vector3(15.923f, 230f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(-30.402f, 12.246f, -32.583f), Rot = new Vector3(15.923f, 230f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(-35.496f, 12.246f, -36.857f), Rot = new Vector3(15.923f, 230f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(-41.089f, 12.246f, -41.55f), Rot = new Vector3(15.923f, 230f, 0f) },
                new PointAnimationTransform { Pos = new Vector3(-46.927f, 12.246f, -46.449f), Rot = new Vector3(15.923f, 230f, 0f) }
            };
            private HashSet<PointAnimationTransform> MinicopterGlobalPointsToExit { get; } = new HashSet<PointAnimationTransform>();
            private Vector3 MinicopterLastPointToCardReader2 { get; set; } = Vector3.zero;
            #endregion Minicopter Variables

            private static int GetRandomNumber => UnityEngine.Random.Range(0, 10);

            #region Pilot Variables
            internal BasePlayer Pilot { get; set; } = null;
            private AnimationTransformBasePlayer AnimationPilot { get; set; } = null;
            private Door Door0Pilot { get; set; } = null;
            private Door Door1Pilot { get; set; } = null;
            private HashSet<Vector3> PilotLocalPointsToCardReader1 { get; } = new HashSet<Vector3>
            {
                new Vector3(40.035f, 3.275f, -35.304f),
                new Vector3(37.271f, 3.275f, -39.443f),
                new Vector3(37.271f, 3f, -41.176f),
                new Vector3(31.262f, 3f, -41.176f),
                new Vector3(31.262f, 3f, -40.544f),
                new Vector3(31.262f, 0.2f, -35.53f),
                new Vector3(21.181f, 0.2f, -1.369f)
            };
            private HashSet<Vector3> PilotGlobalPointsToCardReader1 { get; } = new HashSet<Vector3>();
            private HashSet<Vector3> PilotLocalPointsToMinicopter1 { get; } = new HashSet<Vector3>
            {
                new Vector3(21.181f, 0.2f, -3.976f),
                new Vector3(24f, 0.2f, -3.976f),
                new Vector3(24f, 1.6f, 0f),
                new Vector3(24f, 1.425f, 3.503f)
            };
            private HashSet<Vector3> PilotGlobalPointsToMinicopter1 { get; } = new HashSet<Vector3>();
            private HashSet<Vector3> PilotLocalPointsToCardReader2 { get; } = new HashSet<Vector3>
            {
                new Vector3(36.181f, 0.2f, -1.369f)
            };
            private HashSet<Vector3> PilotGlobalPointsToCardReader2 { get; } = new HashSet<Vector3>();
            private HashSet<Vector3> PilotLocalPointsToMinicopter2 { get; } = new HashSet<Vector3>
            {
                new Vector3(39.308f, 0.2f, -7.15f)
            };
            private HashSet<Vector3> PilotGlobalPointsToMinicopter2 { get; } = new HashSet<Vector3>();
            private Vector3 PilotLastPointToCardReader1 { get; set; } = Vector3.zero;
            private Vector3 PilotLastPointToMinicopter1 { get; set; } = Vector3.zero;
            private Vector3 PilotLastPointToCardReader2 { get; set; } = Vector3.zero;
            private Vector3 PilotLastPointToMinicopter2 { get; set; } = Vector3.zero;
            internal CodeLock CodeLockPilot { get; set; } = null;
            internal string CodePilot { get; set; } = $"{GetRandomNumber}{GetRandomNumber}{GetRandomNumber}{GetRandomNumber}";
            private Coroutine RepairCoroutine { get; set; } = null;
            private int RepairTime { get; set; } = 0;
            #endregion Pilot Variables

            #region Scientist Variables
            internal BasePlayer Scientist { get; set; } = null;
            private AnimationTransformBasePlayer AnimationScientist { get; set; } = null;
            private Door Door0Scientist { get; set; } = null;
            private Door Door1Scientist { get; set; } = null;
            private Door Door2Scientist { get; set; } = null;
            private Door Door3Scientist { get; set; } = null;
            private Door Door4Scientist { get; set; } = null;
            private HashSet<Vector3> ScientistLocalPointsToCorpse { get; } = new HashSet<Vector3>
            {
                new Vector3(-20.976f, 4.752f, -18f),
                new Vector3(-11.553f, 4.752f, -18f),
                new Vector3(-9.829f, 4.5f, -18f),
                new Vector3(-9.829f, 4.5f, -15.069f),
                new Vector3(-8.319f, 4.5f, -15.069f),
                new Vector3(-8.319f, 4.5f, -15.706f),
                new Vector3(-8.319f, 1.567f, -21.267f),
                new Vector3(-5.928f, 1.567f, -21.267f),
                new Vector3(-3.629f, 0.924f, -20.619f),
                new Vector3(-2.014f, 0.375f, -20.002f),
                new Vector3(-0.662f, 0.06f, -19.613f),
                new Vector3(4.969f, 0.115f, -5.677f),
                new Vector3(4.969f, 2.5f, -1.293f),
                new Vector3(4.969f, 2.5f, -0.662f),
                new Vector3(-0.162f, 2.5f, -0.662f),
                new Vector3(-0.162f, 2.5f, 18.876f),
                new Vector3(5.101f, 5.5f, 18.876f),
                new Vector3(7.22f, 5.5f, 18.876f),
                new Vector3(7.22f, 5.525f, 17.432f),
                new Vector3(5.739f, 5.525f, 12.523f),
                new Vector3(5.739f, 5.525f, 11.469f),
                new Vector3(4.952f, 5.537f, 9.872f)
            };
            private HashSet<Vector3> ScientistGlobalPointsToCorpse { get; } = new HashSet<Vector3>();
            private HashSet<Vector3> ScientistLocalPointsToMinicopter { get; } = new HashSet<Vector3>
            {
                new Vector3(5.739f, 5.525f, 11.469f),
                new Vector3(5.739f, 5.525f, 12.523f),
                new Vector3(7.22f, 5.525f, 17.432f),
                new Vector3(7.22f, 5.5f, 18.876f),
                new Vector3(5.101f, 5.5f, 18.876f),
                new Vector3(-0.162f, 2.5f, 18.876f),
                new Vector3(-0.162f, 2.5f, -0.662f),
                new Vector3(4.969f, 2.5f, -0.662f),
                new Vector3(4.969f, 2.5f, -1.293f),
                new Vector3(4.969f, 0.2f, -5.677f),
                new Vector3(4.969f, 0.2f, -6.298f),
                new Vector3(24f, 0.2f, -6.298f),
                new Vector3(24f, 0.2f, -3.976f),
                new Vector3(24f, 1.6f, 0f),
                new Vector3(24f, 1.425f, 3.503f)
            };
            private HashSet<Vector3> ScientistGlobalPointsToMinicopter { get; } = new HashSet<Vector3>();
            private Vector3 ScientistLastPointToCorpse { get; set; } = Vector3.zero;
            internal CodeLock CodeLockScientist { get; set; } = null;
            internal string CodeScientist { get; set; } = $"{GetRandomNumber}{GetRandomNumber}{GetRandomNumber}{GetRandomNumber}";
            private float ScientistHealth { get; set; } = 0f;
            private Coroutine ResearchCoroutine { get; set; } = null;
            private int ResearchTime { get; set; } = 0;
            #endregion Scientist Variables

            #region Snowmobile Variables
            internal class SnowmobileData { public Snowmobile Snowmobile; public ScientistNPC Driver; public ScientistNPC Passenger; public AnimationTransformVehicle Animation; public Coroutine Coroutine; public bool IsGround; }
            internal HashSet<SnowmobileData> Snowmobiles { get; } = new HashSet<SnowmobileData>();
            internal class SnowmobilePath { public PointAnimationTransform Spawn; public HashSet<PointAnimationTransform> Path; }
            private HashSet<SnowmobilePath> SnowmobileLocalPoints { get; } = new HashSet<SnowmobilePath>
            {
                new SnowmobilePath
                {
                    Spawn = new PointAnimationTransform { Pos = new Vector3(-33.493f, 0.216f, -52.819f), Rot = new Vector3(0f, 66.179f, 0f) },
                    Path = new HashSet<PointAnimationTransform>
                    {
                        new PointAnimationTransform { Pos = new Vector3(-31.606f, 0.111f, -51.986f), Rot = new Vector3(2.824f, 66.179f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-29.645f, -0.001f, -51.12f), Rot = new Vector3(2.824f, 66.179f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-27.526f, 0f, -50.182f), Rot = new Vector3(0f, 66.179f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-25.669f, 0f, -49.491f), Rot = new Vector3(0f, 70f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.583f, 0f, -49.114f), Rot = new Vector3(0f, 80f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-21.493f, 0f, -49.08f), Rot = new Vector3(0f, 90f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-19.343f, 0f, -49.08f), Rot = new Vector3(0f, 90f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-17.175f, 0f, -49.08f), Rot = new Vector3(0f, 90f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-15.02f, 0f, -49.08f), Rot = new Vector3(0f, 90f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-12.897f, 0f, -49.08f), Rot = new Vector3(0f, 90f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-10.755f, 0f, -49.08f), Rot = new Vector3(0f, 90f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-8.65f, 0f, -48.747f), Rot = new Vector3(0f, 80f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-6.661f, 0f, -48.083f), Rot = new Vector3(0f, 70f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-4.593f, 0f, -47.33f), Rot = new Vector3(0f, 70f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-2.596f, 0f, -46.603f), Rot = new Vector3(0f, 70f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-0.689f, 0f, -45.673f), Rot = new Vector3(0f, 65f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(1.29f, 0.115f, -44.722f), Rot = new Vector3(0f, 65f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(3.265f, 0.115f, -43.801f), Rot = new Vector3(0f, 65f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(5.218f, 0.115f, -42.89f), Rot = new Vector3(0f, 65f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(7.203f, 0.115f, -41.965f), Rot = new Vector3(0f, 65f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(9.119f, 0.115f, -41.071f), Rot = new Vector3(0f, 65f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(11.113f, 0.115f, -40.343f), Rot = new Vector3(0f, 70f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(13.126f, 0.115f, -39.864f), Rot = new Vector3(0f, 75f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(15.047f, 0.115f, -39.557f), Rot = new Vector3(0f, 80f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(16.576f, 0.115f, -39.405f), Rot = new Vector3(0f, 85f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(17.973f, 0.062f, -39.33f), Rot = new Vector3(0f, 85f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(19.973f, 0.062f, -38.792f), Rot = new Vector3(0f, 75f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(21.934f, 0.062f, -37.891f), Rot = new Vector3(0f, 65f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(23.633f, 0.062f, -36.698f), Rot = new Vector3(0f, 55f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(25.081f, 0.062f, -35.233f), Rot = new Vector3(0f, 45f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(26.273f, 0.062f, -33.521f), Rot = new Vector3(0f, 35f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(27.135f, 0.062f, -31.634f), Rot = new Vector3(0f, 25f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(27.664f, 0.062f, -29.662f), Rot = new Vector3(0f, 15f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(27.863f, 0.062f, -27.699f), Rot = new Vector3(0f, 5f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(27.719f, 0.062f, -25.618f), Rot = new Vector3(0f, 355f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(27.536f, 0.062f, -23.527f), Rot = new Vector3(0f, 355f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(27.352f, 0.062f, -21.433f), Rot = new Vector3(0f, 355f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(27.169f, 0.062f, -19.334f), Rot = new Vector3(0f, 355f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(26.79f, 0.062f, -17.231f), Rot = new Vector3(0f, 350f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(26.244f, 0.062f, -15.189f), Rot = new Vector3(0f, 345f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(25.41f, 0.062f, -13.271f), Rot = new Vector3(0f, 335f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(24.282f, -0.005f, -11.52f), Rot = new Vector3(0f, 325f, 355.748f) },
                        new PointAnimationTransform { Pos = new Vector3(22.744f, 0.094f, -9.882f), Rot = new Vector3(0f, 315f, 355.748f) },
                        new PointAnimationTransform { Pos = new Vector3(21.119f, 0.094f, -8.712f), Rot = new Vector3(0f, 305f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(19.238f, 0.094f, -7.848f), Rot = new Vector3(0f, 295f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(17.26f, 0.094f, -7.286f), Rot = new Vector3(0f, 285f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(15.219f, 0.094f, -7.092f), Rot = new Vector3(0f, 275f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(13.135f, 0.094f, -7.265f), Rot = new Vector3(0f, 265f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(10.898f, 0.094f, -7.461f), Rot = new Vector3(0f, 265f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(8.626f, 0.094f, -7.66f), Rot = new Vector3(0f, 265f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(6.432f, 0.094f, -7.852f), Rot = new Vector3(0f, 265f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(4.279f, 0.094f, -7.861f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(2.157f, 0.094f, -7.528f), Rot = new Vector3(0f, 280f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(0.081f, 0.094f, -6.805f), Rot = new Vector3(0f, 290f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-1.792f, 0.094f, -5.783f), Rot = new Vector3(0f, 300f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-3.697f, 0.115f, -4.681f), Rot = new Vector3(0f, 300f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-5.586f, 0.115f, -3.59f), Rot = new Vector3(0f, 300f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-7.365f, 0.115f, -2.378f), Rot = new Vector3(0f, 305f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-8.914f, 0.115f, -0.909f), Rot = new Vector3(0f, 315f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-10.185f, 0.115f, 0.828f), Rot = new Vector3(0f, 325f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-11.127f, 0.115f, 2.76f), Rot = new Vector3(0f, 335f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-11.732f, 0.115f, 4.838f), Rot = new Vector3(0f, 345f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-12.275f, 0.115f, 6.861f), Rot = new Vector3(0f, 345f, 0f) }
                    }
                },
                new SnowmobilePath
                {
                    Spawn = new PointAnimationTransform { Pos = new Vector3(-33.493f, 0.216f, -52.819f), Rot = new Vector3(0f, 66.179f, 0f) },
                    Path = new HashSet<PointAnimationTransform>
                    {
                        new PointAnimationTransform { Pos = new Vector3(-31.606f, 0.111f, -51.986f), Rot = new Vector3(2.824f, 66.179f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-29.645f, -0.001f, -51.12f), Rot = new Vector3(2.824f, 66.179f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-27.526f, 0f, -50.182f), Rot = new Vector3(0f, 66.179f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-25.669f, 0f, -49.491f), Rot = new Vector3(0f, 70f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.583f, 0f, -49.114f), Rot = new Vector3(0f, 80f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-21.493f, 0f, -49.08f), Rot = new Vector3(0f, 90f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-19.343f, 0f, -49.08f), Rot = new Vector3(0f, 90f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-17.175f, 0f, -49.08f), Rot = new Vector3(0f, 90f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-15.02f, 0f, -49.08f), Rot = new Vector3(0f, 90f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-12.897f, 0f, -49.08f), Rot = new Vector3(0f, 90f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-10.755f, 0f, -49.08f), Rot = new Vector3(0f, 90f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-8.65f, 0f, -48.747f), Rot = new Vector3(0f, 80f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-6.661f, 0f, -48.083f), Rot = new Vector3(0f, 70f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-4.593f, 0f, -47.33f), Rot = new Vector3(0f, 70f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-2.596f, 0f, -46.603f), Rot = new Vector3(0f, 70f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-0.689f, 0f, -45.673f), Rot = new Vector3(0f, 65f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(1.29f, 0.115f, -44.722f), Rot = new Vector3(0f, 65f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(3.265f, 0.115f, -43.801f), Rot = new Vector3(0f, 65f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(5.218f, 0.115f, -42.89f), Rot = new Vector3(0f, 65f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(7.203f, 0.115f, -41.965f), Rot = new Vector3(0f, 65f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(9.119f, 0.115f, -41.071f), Rot = new Vector3(0f, 65f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(11.113f, 0.115f, -40.343f), Rot = new Vector3(0f, 70f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(13.152f, 0.115f, -39.581f), Rot = new Vector3(0f, 70f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(15.061f, 0.115f, -38.544f), Rot = new Vector3(0f, 60f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(16.965f, 0.115f, -37.446f), Rot = new Vector3(0f, 60f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(18.832f, 0.115f, -36.368f), Rot = new Vector3(0f, 60f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(20.51f, 0.115f, -34.997f), Rot = new Vector3(0f, 50f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(21.947f, 0.115f, -33.361f), Rot = new Vector3(0f, 40f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(23.356f, 0.003f, -31.668f), Rot = new Vector3(0f, 40f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(24.737f, 0f, -29.995f), Rot = new Vector3(0f, 40f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(26.134f, 0f, -28.331f), Rot = new Vector3(0f, 40f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(27.724f, 0f, -26.744f), Rot = new Vector3(0f, 45f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(29.409f, 0.002f, -25.361f), Rot = new Vector3(0f, 50f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(31.18f, 0.024f, -24.266f), Rot = new Vector3(0f, 60f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(32.86f, 0.024f, -23.654f), Rot = new Vector3(0f, 70f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(34.886f, 0.024f, -23.297f), Rot = new Vector3(0f, 80f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(37.065f, 0.104f, -22.916f), Rot = new Vector3(0f, 80f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(39.224f, 0.298f, -22.142f), Rot = new Vector3(0f, 70f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(41.468f, 0.569f, -21.32f), Rot = new Vector3(0f, 70f, 0f) }
                    }
                },
                new SnowmobilePath
                {
                    Spawn = new PointAnimationTransform { Pos = new Vector3(53.383f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                    Path = new HashSet<PointAnimationTransform>
                    {
                        new PointAnimationTransform { Pos = new Vector3(51.225f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(48.977f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(46.777f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(44.559f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(42.365f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(40.238f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(38.047f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(35.837f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(33.663f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(31.464f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(29.321f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(27.111f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(24.931f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(22.757f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(20.509f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(18.232f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(16.069f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(13.845f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(11.63f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(9.414f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(7.246f, 0f, -8.53f), Rot = new Vector3(0f, 260f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(5.246f, 0f, -9.258f), Rot = new Vector3(0f, 250f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(3.36f, 0f, -10.347f), Rot = new Vector3(0f, 240f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(1.697f, 0f, -11.742f), Rot = new Vector3(0f, 230f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(0.292f, 0f, -13.417f), Rot = new Vector3(0f, 220f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-0.817f, 0.098f, -15.286f), Rot = new Vector3(0f, 210f, 6.623f) },
                        new PointAnimationTransform { Pos = new Vector3(-1.918f, 0.338f, -17.264f), Rot = new Vector3(357.429f, 209.701f, 18.938f) },
                        new PointAnimationTransform { Pos = new Vector3(-2.955f, 0.634f, -19.219f), Rot = new Vector3(353.429f, 208.321f, 19.048f) },
                        new PointAnimationTransform { Pos = new Vector3(-3.917f, 1.037f, -21.118f), Rot = new Vector3(349.479f, 206.934f, 19.254f) },
                        new PointAnimationTransform { Pos = new Vector3(-5.28f, 1.208f, -22.831f), Rot = new Vector3(350.727f, 221.398f, 12.481f) },
                        new PointAnimationTransform { Pos = new Vector3(-6.873f, 1.458f, -24.255f), Rot = new Vector3(359.992f, 233.225f, 3.324f) },
                        new PointAnimationTransform { Pos = new Vector3(-8.882f, 1.543f, -25.101f), Rot = new Vector3(358.94f, 251.66f, 3.151f) },
                        new PointAnimationTransform { Pos = new Vector3(-11.085f, 1.647f, -25.42f), Rot = new Vector3(359.135f, 266.033f, 2.789f) },
                        new PointAnimationTransform { Pos = new Vector3(-13.415f, 1.692f, -25.583f), Rot = new Vector3(1.906f, 266.019f, 359.689f) },
                        new PointAnimationTransform { Pos = new Vector3(-15.827f, 1.611f, -25.75f), Rot = new Vector3(1.906f, 266.019f, 359.689f) },
                        new PointAnimationTransform { Pos = new Vector3(-18.203f, 1.582f, -25.918f), Rot = new Vector3(358.774f, 266.035f, 5.339f) }
                    }
                },
                new SnowmobilePath
                {
                    Spawn = new PointAnimationTransform { Pos = new Vector3(53.383f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                    Path = new HashSet<PointAnimationTransform>
                    {
                        new PointAnimationTransform { Pos = new Vector3(51.225f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(48.977f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(46.777f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(44.559f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(42.365f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(40.238f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(38.047f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(35.837f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(33.663f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(31.464f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(29.321f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(27.111f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(24.931f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(22.757f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(20.509f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(18.232f, 0f,-8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(16.069f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(13.845f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(11.63f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(9.414f, 0f, -8.148f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(7.241f, 0f, -7.765f), Rot = new Vector3(0f, 280f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(5.072f, 0f, -7.382f), Rot = new Vector3(0f, 280f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(2.93f, 0f, -7.004f), Rot = new Vector3(0f, 280f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(0.882f, 0f, -6.259f), Rot = new Vector3(0f, 290f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-1.148f, 0f, -5.52f), Rot = new Vector3(0f, 290f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-3.033f, 0f, -4.432f), Rot = new Vector3(0f, 300f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-4.903f, 0f, -3.352f), Rot = new Vector3(0f, 300f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-6.783f, 0f, -2.267f), Rot = new Vector3(0f, 300f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-8.461f, 0f, -0.859f), Rot = new Vector3(0f, 310f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-10.14f, 0.009f, 0.57f), Rot = new Vector3(0f, 310f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-11.536f, 0.009f, 2.233f), Rot = new Vector3(0f, 320f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-12.958f, 0.009f, 3.928f), Rot = new Vector3(0f, 320f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-14.06f, 0.009f, 5.837f), Rot = new Vector3(0f, 330f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-15.136f, 0.009f, 7.7f), Rot = new Vector3(0f, 330f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-15.855f, 0.009f, 9.675f), Rot = new Vector3(0f, 340f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-16.229f, 0.009f, 11.797f), Rot = new Vector3(0f, 350f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-16.215f, 0.005f, 13.97f), Rot = new Vector3(0f, 0f, 0f) }
                    }
                },
                new SnowmobilePath
                {
                    Spawn = new PointAnimationTransform { Pos = new Vector3(-23.937f, 0f, 56.574f), Rot = new Vector3(0f, 180f, 0f) },
                    Path = new HashSet<PointAnimationTransform>
                    {
                        new PointAnimationTransform { Pos = new Vector3(-23.937f, 0f, 54.389f), Rot = new Vector3(0f, 180f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.937f, 0f, 52.204f), Rot = new Vector3(0f, 180f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.937f, 0f, 50.017f), Rot = new Vector3(0f, 180f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.937f, 0f, 47.851f), Rot = new Vector3(0f, 180f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.937f, 0f, 45.64f), Rot = new Vector3(0f, 180f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.937f, 0f, 43.496f), Rot = new Vector3(0f, 180f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.937f, 0f, 41.344f), Rot = new Vector3(0f, 180f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.937f, 0f, 39.181f), Rot = new Vector3(0f, 180f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.937f, 0f, 37.051f), Rot = new Vector3(0f, 180f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.937f, 0f, 34.875f), Rot = new Vector3(0f, 180f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.937f, 0f, 32.717f), Rot = new Vector3(0f, 180f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.745f, 0f, 30.531f), Rot = new Vector3(0f, 175f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.555f, 0f, 28.351f), Rot = new Vector3(0f, 175f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.555f, 0f, 26.2f), Rot = new Vector3(0f, 180f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.744f, 0f, 24.038f), Rot = new Vector3(0f, 185f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.936f, 0f, 21.84f), Rot = new Vector3(0f, 185f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-24.131f, 0f, 19.615f), Rot = new Vector3(0f, 185f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-24.322f, 0f, 17.424f), Rot = new Vector3(0f, 185f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-24.51f, 0f, 15.284f), Rot = new Vector3(0f, 185f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-24.697f, 0.153f, 13.121f), Rot = new Vector3(0f, 185f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-24.887f, 0.109f, 10.897f), Rot = new Vector3(0f, 185f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-25.085f, 0.077f, 8.574f), Rot = new Vector3(0f, 185f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-25.304f, 0.155f, 6.197f), Rot = new Vector3(357.793f, 184.824f, 4.569f) },
                        new PointAnimationTransform { Pos = new Vector3(-25.486f, 0.485f, 3.802f), Rot = new Vector3(350.949f, 184.233f, 7.765f) },
                        new PointAnimationTransform { Pos = new Vector3(-25.643f, 0.916f, 1.557f), Rot = new Vector3(347.146f, 183.701f, 7.866f) },
                        new PointAnimationTransform { Pos = new Vector3(-25.832f, 1.102f, -0.672f), Rot = new Vector3(359.026f, 185.33f, 9.538f) },
                        new PointAnimationTransform { Pos = new Vector3(-25.61f, 1.283f, -2.932f), Rot = new Vector3(354.205f, 174.187f, 9.586f) },
                        new PointAnimationTransform { Pos = new Vector3(-25.378f, 1.427f, -5.314f), Rot = new Vector3(358.12f, 174.639f, 2.186f) }
                    }
                },
                new SnowmobilePath
                {
                    Spawn = new PointAnimationTransform { Pos = new Vector3(-24.264f, 0.1f, 54.614f), Rot = new Vector3(0f, 180f, 0f) },
                    Path = new HashSet<PointAnimationTransform>
                    {
                        new PointAnimationTransform { Pos = new Vector3(-24.264f, 0.1f, 51.97f), Rot = new Vector3(0f, 180f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-24.264f, 0.1f, 49.171f), Rot = new Vector3(0f, 180f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-24.264f, 0.1f, 46.414f), Rot = new Vector3(0f, 180f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-24.499f, 0.1f, 43.494f), Rot = new Vector3(0f, 191.585f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-24.823f, 0.1f, 40.779f), Rot = new Vector3(0f, 180.587f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-24.513f, 0.1f, 37.557f), Rot = new Vector3(0f, 173.39f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-24.155f, 0.1f, 34.465f), Rot = new Vector3(0f, 180.488f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-24.11f, 0.1f, 31.273f), Rot = new Vector3(0f, 180.488f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-24.397f, 0.1f, 28.148f), Rot = new Vector3(0f, 194.544f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-25.049f, 0.1f, 25.02f), Rot = new Vector3(0f, 187.617f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-25.292f, 0.1f, 21.894f), Rot = new Vector3(0f, 178.774f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-24.917f, 0.1f, 18.887f), Rot = new Vector3(0f, 167.312f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-24.154f, 0.1f, 15.762f), Rot = new Vector3(0f, 163.883f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.108f, 0.1f, 12.845f), Rot = new Vector3(0f, 155.812f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-21.322f, 0.1f, 9.761f), Rot = new Vector3(0f, 145.844f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-19.818f, 0.1f, 8.207f), Rot = new Vector3(0f, 134.926f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-18.184f, 0.1f, 6.758f), Rot = new Vector3(0f, 131.471f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-16.19f, 0.1f, 4.778f), Rot = new Vector3(0f, 140.728f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-14.496f, 0.1f, 2.394f), Rot = new Vector3(0f, 151.336f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-12.881f, 0.1f, 0.069f), Rot = new Vector3(0f, 139.99f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-10.73f, 0.1f, -2.2f), Rot = new Vector3(0f, 132.435f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-8.358f, 0.1f, -4.2f), Rot = new Vector3(0f, 128.475f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-6.154f, 0.1f, -5.955f), Rot = new Vector3(0f, 130.327f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-4.084f, 0.1f, -7.712f), Rot = new Vector3(0f, 130.327f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-1.777f, 0.1f, -9.67f), Rot = new Vector3(0f, 130.327f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(0.094f, 0.1f, -11.483f), Rot = new Vector3(0f, 137.691f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(1.943f, 0.1f, -13.515f), Rot = new Vector3(0f, 137.691f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(3.718f, 0.1f, -15.465f), Rot = new Vector3(0f, 137.691f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(5.258f, 0.1f, -17.418f), Rot = new Vector3(0f, 148.543f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(6.468f, 0.1f, -19.746f), Rot = new Vector3(0f, 156.629f, 0f) }
                    }
                },
                new SnowmobilePath
                {
                    Spawn = new PointAnimationTransform { Pos = new Vector3(-57.582f, 0.1f, 13.62f), Rot = new Vector3(0f, 90f, 0f) },
                    Path = new HashSet<PointAnimationTransform>
                    {
                        new PointAnimationTransform { Pos = new Vector3(-55.306f, 0.1f, 13.62f), Rot = new Vector3(0f, 90f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-52.75f, 0.1f, 13.62f), Rot = new Vector3(0f, 90f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-50.172f, 0.1f, 13.62f), Rot = new Vector3(0f, 92.867f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-47.45f, 0.1f, 13.484f), Rot = new Vector3(0f, 94.578f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-44.796f, 0.1f, 13.271f), Rot = new Vector3(0f, 94.578f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-42.119f, 0.1f, 13.057f), Rot = new Vector3(0f, 93.062f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-39.349f, 0.1f, 12.909f), Rot = new Vector3(0f, 90.933f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-36.421f, 0.1f, 12.861f), Rot = new Vector3(0f, 88.731f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-33.458f, 0.1f, 12.911f), Rot = new Vector3(0f, 86.082f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-31.007f, 0.1f, 12.95f), Rot = new Vector3(0f, 94.529f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-28.554f, 0.1f, 12.635f), Rot = new Vector3(0f, 100.693f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-26.045f, 0.1f, 12.079f), Rot = new Vector3(0f, 106.526f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.688f, 0.1f, 11.25f), Rot = new Vector3(0f, 113.266f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-21.341f, 0.1f, 10.123f), Rot = new Vector3(0f, 118.493f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-19.165f, 0.1f, 8.721f), Rot = new Vector3(0f, 125.785f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-17.192f, 0.1f, 7.117f), Rot = new Vector3(0f, 131.466f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-15.311f, 0.1f, 5.269f), Rot = new Vector3(0f, 137.389f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-13.688f, 0.1f, 3.376f), Rot = new Vector3(0f, 142.21f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-12.187f, 0.1f, 1.441f), Rot = new Vector3(0f, 142.21f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-10.63f, 0.1f, -0.567f), Rot = new Vector3(0f, 142.21f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-8.945f, 0.1f, -2.585f), Rot = new Vector3(0f, 137.672f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-7.195f, 0.1f, -4.428f), Rot = new Vector3(0f, 134.671f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-5.448f, 0.1f, -6.154f), Rot = new Vector3(0f, 134.671f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-3.643f, 0.1f, -7.94f), Rot = new Vector3(0f, 134.671f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-1.82f, 0.1f, -9.64f), Rot = new Vector3(0f, 130.992f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(0.082f, 0.1f, -11.162f), Rot = new Vector3(0f, 127.041f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(2.126f, 0.1f, -12.558f), Rot = new Vector3(0f, 120.984f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(4.269f, 0.1f, -13.797f), Rot = new Vector3(0f, 117.564f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(6.367f, 0.1f, -14.893f), Rot = new Vector3(0f, 117.564f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(8.613f, 0.1f, -16.018f), Rot = new Vector3(0f, 114.304f, 0f) }
                    }
                },
                new SnowmobilePath
                {
                    Spawn = new PointAnimationTransform { Pos = new Vector3(55.025f, 0.1f, -6.777f), Rot = new Vector3(0f, 270f, 0f) },
                    Path = new HashSet<PointAnimationTransform>
                    {
                        new PointAnimationTransform { Pos = new Vector3(52.503f, 0.1f, -6.777f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(50.011f, 0.1f, -6.777f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(47.367f, 0.1f, -6.777f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(44.891f, 0.1f, -6.777f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(42.334f, 0.1f, -6.777f), Rot = new Vector3(0f, 270, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(39.844f, 0.1f, -6.827f), Rot = new Vector3(0f, 265.58f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(37.443f, 0.1f, -7.122f), Rot = new Vector3(0f, 259.539f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(34.986f, 0.1f, -7.709f), Rot = new Vector3(0f, 253.228f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(32.616f, 0.1f, -8.607f), Rot = new Vector3(0f, 244.903f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(30.526f, 0.1f, -9.835f), Rot = new Vector3(0f, 235.186f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(28.692f, 0.1f, -11.373f), Rot = new Vector3(0f, 225.779f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(27.177f, 0.1f, -13.138f), Rot = new Vector3(0f, 217.829f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(25.911f, 0.1f, -15.216f), Rot = new Vector3(0f, 207.59f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(25.152f, 0.1f, -17.388f), Rot = new Vector3(0f, 194.189f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(24.837f, 0.1f, -19.842f), Rot = new Vector3(0f, 183.062f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(24.948f, 0.1f, -22.167f), Rot = new Vector3(0f, 172.146f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(25.489f, 0.1f, -24.414f), Rot = new Vector3(0f, 162.147f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(26.223f, 0.1f, -26.693f), Rot = new Vector3(0f, 162.147f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(26.734f, 0.1f, -28.943f), Rot = new Vector3(0f, 172.478f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(26.751f, 0.1f, -31.283f), Rot = new Vector3(0f, 184.183f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(26.38f, 0.1f, -33.539f), Rot = new Vector3(0f, 192.631f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(25.66f, 0.1f, -35.708f), Rot = new Vector3(0f, 203.575f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(24.475f, 0.1f, -37.617f), Rot = new Vector3(0f, 217.051f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(22.836f, 0.1f, -39.128f), Rot = new Vector3(0f, 234.044f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(20.773f, 0.1f, -40.133f), Rot = new Vector3(0f, 249.976f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(18.584f, 0.1f, -40.499f), Rot = new Vector3(0f, 267.709f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(16.21f, 0.1f, -40.515f), Rot = new Vector3(0f, 270f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(13.905f, 0.1f, -40.373f), Rot = new Vector3(0f, 277.503f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(11.584f, 0.1f, -39.88f), Rot = new Vector3(0f, 286.39f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(9.305f, 0.1f, -39.07f), Rot = new Vector3(0f, 292.64f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(7.205f, 0.1f, -38.01f), Rot = new Vector3(0f, 299.993f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(5.314f, 0.1f, -36.616f), Rot = new Vector3(0f, 309.373f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(3.624f, 0.1f, -34.92f), Rot = new Vector3(0f, 320.647f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(2.341f, 0.1f, -32.976f), Rot = new Vector3(0f, 330.969f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(1.44f, 0.1f, -30.84f), Rot = new Vector3(0f, 339.047f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(0.892f, 0.1f, -28.655f), Rot = new Vector3(0f, 349.822f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(0.638f, 0.1f, -26.28f), Rot = new Vector3(0f, 356.958f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(0.512f, 0.1f, -23.904f), Rot = new Vector3(0f, 356.958f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(0.388f, 0.1f, -21.58f), Rot = new Vector3(0f, 356.958f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(0.054f, 0.1f, -19.119f), Rot = new Vector3(0f, 347.574f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-0.595f, 0.1f, -16.729f), Rot = new Vector3(0f, 339.903f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-1.487f, 0.1f, -14.292f), Rot = new Vector3(0f, 339.903f, 0f) }
                    }
                },
                new SnowmobilePath
                {
                    Spawn = new PointAnimationTransform { Pos = new Vector3(-23.937f, 0f, 56.574f), Rot = new Vector3(0f, 180f, 0f) },
                    Path = new HashSet<PointAnimationTransform>
                    {
                        new PointAnimationTransform { Pos = new Vector3(-23.937f, 0f, 54.389f), Rot = new Vector3(0f, 180f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.937f, 0f, 52.204f), Rot = new Vector3(0f, 180f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.937f, 0f, 50.017f), Rot = new Vector3(0f, 180f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.937f, 0f, 47.851f), Rot = new Vector3(0f, 180f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.937f, 0f, 45.64f), Rot = new Vector3(0f, 180f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.937f, 0f, 43.496f), Rot = new Vector3(0f, 180f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.937f, 0f, 41.344f), Rot = new Vector3(0f, 180f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.937f, 0f, 39.181f), Rot = new Vector3(0f, 180f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.937f, 0f, 37.051f), Rot = new Vector3(0f, 180f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.937f, 0f, 34.875f), Rot = new Vector3(0f, 180f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.937f, 0f, 32.717f), Rot = new Vector3(0f, 180f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.745f, 0f, 30.531f), Rot = new Vector3(0f, 175f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.555f, 0f, 28.351f), Rot = new Vector3(0f, 175f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-23.363f, 0f, 26.163f), Rot = new Vector3(0f, 175f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-22.982f, 0f, 23.999f), Rot = new Vector3(0f, 170f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-22.607f, 0f, 21.873f), Rot = new Vector3(0f, 170f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-22.045f, 0f, 19.777f), Rot = new Vector3(0f, 165f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-21.493f, 0f, 17.718f), Rot = new Vector3(0f, 165f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-20.745f, 0f, 15.661f), Rot = new Vector3(0f, 160f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-19.839f, 0f, 13.718f), Rot = new Vector3(0f, 155f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-18.755f, 0f, 11.841f), Rot = new Vector3(0f, 150f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-17.686f, 0f, 9.99f), Rot = new Vector3(0f, 150f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-16.606f, 0f, 8.119f), Rot = new Vector3(0f, 150f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-15.378f, 0f, 6.365f), Rot = new Vector3(0f, 145f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-13.991f, 0f, 4.712f), Rot = new Vector3(0f, 140f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-12.618f, 0f, 3.076f), Rot = new Vector3(0f, 140f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-11.243f, 0f, 1.437f), Rot = new Vector3(0f, 140f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-9.846f, 0f, -0.228f), Rot = new Vector3(0f, 140f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-8.453f, 0f, -1.888f), Rot = new Vector3(0f, 140f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-7.097f, 0f, -3.504f), Rot = new Vector3(0f, 140f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-5.693f, 0f, -5.177f), Rot = new Vector3(0f, 140f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-4.459f, 0f, -6.94f), Rot = new Vector3(0f, 145f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-3.213f, 0f, -8.719f), Rot = new Vector3(0f, 145f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-2.145f, 0f, -10.569f), Rot = new Vector3(0f, 150f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-1.063f, 0f, -12.443f), Rot = new Vector3(0f, 150f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(-0.157f, 0f, -14.386f), Rot = new Vector3(0f, 155f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(0.585f, 0f, -16.425f), Rot = new Vector3(0f, 160f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(1.13f, 0f, -18.459f), Rot = new Vector3(0f, 165f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(1.498f, 0f, -20.547f), Rot = new Vector3(0f, 170f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(1.685f, 0f, -22.679f), Rot = new Vector3(0f, 175f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(1.87f, 0f, -24.792f), Rot = new Vector3(0f, 175f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(2.056f, 0f, -26.922f), Rot = new Vector3(0f, 175f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(2.427f, 0f, -29.028f), Rot = new Vector3(0f, 170f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(3.167f, 0f, -31.06f), Rot = new Vector3(0f, 160f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(4.256f, 0f, -32.946f), Rot = new Vector3(0f, 150f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(5.671f, 0f, -34.632f), Rot = new Vector3(0f, 140f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(7.353f, 0f, -36.044f), Rot = new Vector3(0f, 130f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(9.272f, 0f, -37.152f), Rot = new Vector3(0f, 120f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(11.307f, 0f, -37.892f), Rot = new Vector3(0f, 110f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(13.476f, 0f, -38.275f), Rot = new Vector3(0f, 100f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(15.62f, 0f, -38.653f), Rot = new Vector3(0f, 100f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(17.794f, 0f, -39.036f), Rot = new Vector3(0f, 100f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(19.915f, 0f, -39.41f), Rot = new Vector3(0f, 100f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(21.987f, 0f, -40.164f), Rot = new Vector3(0f, 110f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(24.092f, 0f, -40.93f), Rot = new Vector3(0f, 110f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(25.973f, 0f, -42.017f), Rot = new Vector3(0f, 120f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(27.862f, 0.085f, -43.107f), Rot = new Vector3(357.64f, 120f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(29.888f, 0.397f, -44.277f), Rot = new Vector3(354.09f, 119.968f, 355.923f) },
                        new PointAnimationTransform { Pos = new Vector3(31.953f, 0.834f, -45.483f), Rot = new Vector3(351.921f, 120.124f, 356.496f) }
                    }
                }
            };
            private List<SnowmobilePath> SnowmobileGlobalPoints { get; } = new List<SnowmobilePath>();
            private SnowmobilePath SnowmobileCh47StartLocalPoints { get; } = new SnowmobilePath
            {
                Spawn = new PointAnimationTransform { Pos = new Vector3(-23.6f, 10.3f, -18f), Rot = new Vector3(0f, 90f, 0f) },
                Path = new HashSet<PointAnimationTransform>
                {
                    new PointAnimationTransform { Pos = new Vector3(-21.2f, 10.1f, -18f), Rot = new Vector3(10f, 90f, 0f) },
                    new PointAnimationTransform { Pos = new Vector3(-19.848f, 9.862f, -18f), Rot = new Vector3(10f, 90f, 0f) },
                    new PointAnimationTransform { Pos = new Vector3(-18.155f, 9.572f, -18f), Rot = new Vector3(6.329f, 90f, 0f) },
                    new PointAnimationTransform { Pos = new Vector3(-16.362f, 9.396f, -18f), Rot = new Vector3(0f, 90f, 0f) },
                    new PointAnimationTransform { Pos = new Vector3(-14.328f, 9.396f, -18f), Rot = new Vector3(0f, 90f, 0f) },
                    new PointAnimationTransform { Pos = new Vector3(-12.744f, 9.396f, -18f), Rot = new Vector3(0f, 90f, 0f) },
                    new PointAnimationTransform { Pos = new Vector3(-11.201f, 9.377f, -18f), Rot = new Vector3(352.406f, 90f, 0f) }
                }
            };
            private SnowmobilePath SnowmobileCh47StartGlobalPoints { get; set; } = null;
            private HashSet<SnowmobilePath> SnowmobileCh47LocalPoints { get; } = new HashSet<SnowmobilePath>
            {
                new SnowmobilePath
                {
                    Spawn = new PointAnimationTransform { Pos = Vector3.zero, Rot = Vector3.zero },
                    Path = new HashSet<PointAnimationTransform>
                    {
                        new PointAnimationTransform { Pos = new Vector3(3.088f, -0.052f, -18f), Rot = new Vector3(0f, 90f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(5.414f, 0.071f, -17.695f), Rot = new Vector3(0f, 80f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(7.653f, 0.071f, -17.047f), Rot = new Vector3(0f, 70f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(9.561f, 0.071f, -16.113f), Rot = new Vector3(0f, 60f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(11.185f, 0.127f, -14.818f), Rot = new Vector3(0f, 50f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(12.544f, 0.198f, -13.281f), Rot = new Vector3(0f, 40f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(13.589f, 0.303f, -11.623f), Rot = new Vector3(0f, 30f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(14.543f, 0.083f, -9.972f), Rot = new Vector3(5.889f, 30f, 1.915f) },
                        new PointAnimationTransform { Pos = new Vector3(15.386f, 0.003f, -7.994f), Rot = new Vector3(0f, 20f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(15.861f, 0.003f, -5.883f), Rot = new Vector3(0f, 10f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(16.05f, 0.107f, -3.675f), Rot = new Vector3(0f, 0f, 352.233f) },
                        new PointAnimationTransform { Pos = new Vector3(16.05f, 0.107f, -1.465f), Rot = new Vector3(0f, 0f, 352.233f) }
                    }
                },
                new SnowmobilePath
                {
                    Spawn = new PointAnimationTransform { Pos = Vector3.zero, Rot = Vector3.zero },
                    Path = new HashSet<PointAnimationTransform>
                    {
                        new PointAnimationTransform { Pos = new Vector3(3.088f, -0.052f, -18f), Rot = new Vector3(0f, 90f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(5.414f, 0.071f, -17.695f), Rot = new Vector3(0f, 80f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(7.653f, 0.071f, -17.047f), Rot = new Vector3(0f, 70f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(9.561f, 0.071f, -16.113f), Rot = new Vector3(0f, 60f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(11.185f, 0.127f, -14.818f), Rot = new Vector3(0f, 50f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(12.85f, 0.245f, -13.422f), Rot = new Vector3(357.901f, 50f, 3.381f) },
                        new PointAnimationTransform { Pos = new Vector3(14.779f, 0.521f, -12.075f), Rot = new Vector3(351.333f, 58.99f, 6.558f) },
                        new PointAnimationTransform { Pos = new Vector3(16.231f, 0.597f, -11.226f), Rot = new Vector3(0.133f, 59.998f, 10.858f) },
                        new PointAnimationTransform { Pos = new Vector3(17.781f, 0.266f, -10.403f), Rot = new Vector3(6.515f, 61.227f, 10.93f) },
                        new PointAnimationTransform { Pos = new Vector3(19.77f, 0.078f, -9.286f), Rot = new Vector3(4.154f, 60.48f, 4.16f) },
                        new PointAnimationTransform { Pos = new Vector3(21.812f, 0.05f, -8.454f), Rot = new Vector3(0f, 70f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(23.786f, 0f, -8.101f), Rot = new Vector3(0f, 80f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(25.895f, 0f, -8.099f), Rot = new Vector3(0f, 90f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(27.977f, 0f, -8.425f), Rot = new Vector3(0f, 100f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(29.986f, 0f, -9.062f), Rot = new Vector3(0f, 110f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(32.031f, 0f, -9.806f), Rot = new Vector3(0f, 110f, 0f) },
                        new PointAnimationTransform { Pos = new Vector3(34.224f, 0.087f, -10.604f), Rot = new Vector3(356.462f, 110f, 5.248f) },
                        new PointAnimationTransform { Pos = new Vector3(36.332f, 0.324f, -11.364f), Rot = new Vector3(353.311f, 109.662f, 13.945f) }
                    }
                }
            };
            private List<SnowmobilePath> SnowmobileCh47GlobalPoints { get; } = new List<SnowmobilePath>();
            private Coroutine SnowmobilePilotCoroutine { get; set; } = null;
            private Coroutine SnowmobileScientistCoroutine { get; set; } = null;
            #endregion Snowmobile Variables

            #region CH47 Variables
            private CH47Helicopter Ch47 { get; set; } = null;
            private CH47HelicopterAIController Ch47Ai { get; set; } = null;
            private Coroutine Ch47Coroutine { get; set; } = null;
            #endregion CH47 Variables

            private HashSet<Vector3> LocalPointsInSecurityHouse { get; } = new HashSet<Vector3>
            {
                new Vector3(-16.596f, 3.025f, 38.073f),
                new Vector3(-47.425f, 6.275f, 28.067f),
                new Vector3(-48.932f, 3.025f, 3.4f),
                new Vector3(-34.9f, 3.275f, -35.953f)
            };
            private Coroutine SpawnNpcInSecurityHouseCoroutine { get; set; } = null;
            private string HomePositionForAdditionalNpc { get; set; } = string.Empty;

            internal HashSet<ScientistNPC> Scientists { get; } = new HashSet<ScientistNPC>();

            internal Dictionary<LootContainer, int> Crates { get; } = new Dictionary<LootContainer, int>();
            internal HackableLockedCrate HackCrate { get; set; } = null;

            internal int TimeToFinish { get; set; } = _ins._config.FinishTime;
            private bool CanChangeTimeToFinish { get; set; } = true;

            internal HashSet<BasePlayer> Players { get; } = new HashSet<BasePlayer>();
            internal BasePlayer Owner { get; set; } = null;

            private void Awake()
            {
                Monument = _ins.GetMonument();
                transform.position = Monument.transform.position;
                transform.rotation = Monument.transform.rotation;

                Puzzle = GetPuzzleReset(Monument);
                DisableRadiation(Puzzle);

                gameObject.layer = 3;
                SphereCollider = gameObject.AddComponent<SphereCollider>();
                SphereCollider.isTrigger = true;
                SphereCollider.radius = _config.Radius;

                CalculateLocations();

                FindEntities();

                MainDoor1.SetOpen(false);
                MainDoor2.SetOpen(false);
                Door0Pilot.SetOpen(false);
                Door0Scientist.SetOpen(false);

                CodeLockPilot = SetCodeLock(Door0Pilot, CodePilot);
                CodeLockScientist = SetCodeLock(Door0Scientist, CodeScientist);

                SpawnMinicopter();

                SpawnPilot();
                SpawnScientist();

                foreach (PresetConfig preset in _config.PresetsNpc) SpawnPreset(preset);

                SpawnMapMarker(_config.Marker);

                Invoke(() => { foreach (BasePlayer player in Players) TryTeleportPlayer(player); }, 1f);
                InvokeRepeating(InvokeUpdates, 0f, 1f);
            }

            private void OnDestroy()
            {
                if (ResearchCoroutine != null) ServerMgr.Instance.StopCoroutine(ResearchCoroutine);
                if (RepairCoroutine != null) ServerMgr.Instance.StopCoroutine(RepairCoroutine);
                if (SnowmobilePilotCoroutine != null) ServerMgr.Instance.StopCoroutine(SnowmobilePilotCoroutine);
                if (SnowmobileScientistCoroutine != null) ServerMgr.Instance.StopCoroutine(SnowmobileScientistCoroutine);
                if (Ch47Coroutine != null) ServerMgr.Instance.StopCoroutine(Ch47Coroutine);
                if (SpawnNpcInSecurityHouseCoroutine != null) ServerMgr.Instance.StopCoroutine(SpawnNpcInSecurityHouseCoroutine);

                CancelInvoke(InvokeUpdates);
                CancelInvoke(PilotGoToMinicopter1);
                CancelInvoke(MinicopterGoToExit);

                if (SphereCollider != null) Destroy(SphereCollider);

                if (VendingMarker.IsExists()) VendingMarker.Kill();
                foreach (MapMarkerGenericRadius marker in Markers) if (marker.IsExists()) marker.Kill();

                foreach (BasePlayer player in Players) CuiHelper.DestroyUi(player, "Tabs_KpucTaJl");

                if (Pilot.IsExists())
                {
                    Pilot.EnsureDismounted();
                    Pilot.Kill();
                }

                if (Scientist.IsExists())
                {
                    Scientist.EnsureDismounted();
                    Scientist.Kill();
                }

                foreach (ScientistNPC npc in Scientists) if (npc.IsExists()) npc.Kill();

                if (HackCrate.IsExists()) HackCrate.Kill();
                foreach (KeyValuePair<LootContainer, int> dic in Crates) if (dic.Key.IsExists()) dic.Key.Kill();

                if (CodeLockPilot.IsExists()) CodeLockPilot.Kill();
                if (CodeLockScientist.IsExists()) CodeLockScientist.Kill();

                DestroyMinicopter();

                if (Ch47.IsExists()) Ch47.Kill();

                foreach (SnowmobileData data in Snowmobiles)
                {
                    if (data.Coroutine != null) ServerMgr.Instance.StopCoroutine(data.Coroutine);
                    if (data.Passenger.IsExists())
                    {
                        data.Passenger.EnsureDismounted();
                        data.Passenger.Kill();
                    }
                    if (data.Driver.IsExists())
                    {
                        data.Driver.EnsureDismounted();
                        data.Driver.Kill();
                    }
                    if (data.Snowmobile.IsExists()) data.Snowmobile.Kill();
                }
            }

            private void OnTriggerEnter(Collider other)
            {
                EnterPlayer(other.GetComponentInParent<BasePlayer>());
                if (_config.RemoveDefaultNpc)
                {
                    ScientistNPC npc = other.GetComponentInParent<ScientistNPC>();
                    if (IsDefaultNpc(npc)) Invoke(() => { if (npc.IsExists()) npc.Kill(); }, 1f);
                }
            }

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
                CheckMinicopterKinematic();
                UpdateTimeToFinish();
            }

            private void UpdateGui(BasePlayer player)
            {
                Dictionary<string, string> dic = new Dictionary<string, string>();
                if (CodeLockScientist != null || (MiniCopter == null && Pilot == null && Scientist == null)) dic.Add("Clock_KpucTaJl", GetTimeFormat(TimeToFinish));
                if (Scientists.Count > 0) dic.Add("Npc_KpucTaJl", Scientists.Count.ToString());
                if (CodeLockPilot != null) dic.Add("Pilot_KpucTaJl", CodeLockScientist == null && Scientists.Count == 0 && Snowmobiles.Count == 0 ? CodePilot : "****");
                else if (RepairTime > 0) dic.Add("Clock_KpucTaJl", GetTimeFormat(RepairTime));
                if (CodeLockScientist != null) dic.Add("Scientist_KpucTaJl", Scientists.Count == 0 ? CodeScientist : "****");
                else if (Scientist != null)
                {
                    dic.Add("Plus_KpucTaJl", $"{(int)ScientistHealth}");
                    if (ResearchTime > 0) dic.Add("Clock_KpucTaJl", GetTimeFormat(ResearchTime));
                }
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
                VendingMarker.markerShopName = $"{_config.Marker.Text}";
                if (CodeLockScientist != null || (MiniCopter == null && Pilot == null && Scientist == null)) VendingMarker.markerShopName += $"\n{GetTimeFormat(TimeToFinish)}";
                else VendingMarker.markerShopName += $"\n{(int)ScientistHealth} HP";
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
                    if (CodeLockScientist != null) points.Add(CodeLockScientist.transform.position);
                    else
                    {
                        if (CodeLockPilot != null) points.Add(CodeLockPilot.transform.position);
                        if (Scientist != null) points.Add(Scientist.transform.position);
                    }
                    if (points.Count > 0) foreach (BasePlayer player in Players) foreach (Vector3 point in points) UpdateMarkerForPlayer(player, point, _config.MainPoint);
                    points = null;
                }
                if (_config.AdditionalPoint.Enabled && (Crates.Count > 0 || HackCrate.IsExists()))
                {
                    HashSet<Vector3> points = new HashSet<Vector3>();
                    foreach (KeyValuePair<LootContainer, int> dic in Crates) if (dic.Key.IsExists()) points.Add(dic.Key.transform.position);
                    if (HackCrate.IsExists()) points.Add(HackCrate.transform.position);
                    if (points.Count > 0) foreach (BasePlayer player in Players) foreach (Vector3 point in points) UpdateMarkerForPlayer(player, point, _config.AdditionalPoint);
                    points = null;
                }
            }

            private void UpdateTimeToFinish()
            {
                if (!CanChangeTimeToFinish) return;
                if (MiniCopter == null && Pilot == null && Scientist == null && HackCrate == null && Crates.Count == 0 && TimeToFinish > _config.PreFinishTime) TimeToFinish = _config.PreFinishTime;
                else TimeToFinish--;
                if (TimeToFinish == _config.PreFinishTime) _ins.AlertToAllPlayers("PreFinish", _config.Chat.Prefix, GetTimeFormat(_config.PreFinishTime));
                else if (TimeToFinish == 0)
                {
                    CancelInvoke(InvokeUpdates);
                    _ins.Finish();
                }
            }

            private Vector3 GetGlobalPosition(Vector3 localPosition) => transform.TransformPoint(localPosition);

            private Quaternion GetGlobalRotation(Vector3 localRotation) => transform.rotation * Quaternion.Euler(localRotation);

            private static T GetNearEntity<T>(Vector3 position, float radius, int layerMask) where T : BaseEntity
            {
                List<T> list = Pool.Get<List<T>>();
                Vis.Entities<T>(position, radius, list, layerMask);
                T result = list.Count == 0 ? null : list.Min(s => Vector3.Distance(position, s.transform.position));
                Pool.FreeUnmanaged(ref list);
                return result;
            }

            private void FindEntities()
            {
                MainDoor1 = GetNearEntity<Door>(GetGlobalPosition(new Vector3(24f, 1.6f, 0f)), 1f, -1);
                MainDoor2 = GetNearEntity<Door>(GetGlobalPosition(new Vector3(39f, 1.6f, 0f)), 1f, -1);

                CardReader1 = GetNearEntity<CardReader>(GetGlobalPosition(new Vector3(21.2f, 1.861f, -0.622f)), 1f, 1 << 16);
                CardReader2 = GetNearEntity<CardReader>(GetGlobalPosition(new Vector3(36.2f, 1.861f, -0.622f)), 1f, 1 << 16);

                Door0Pilot = GetNearEntity<Door>(GetGlobalPosition(new Vector3(41f, 3.275f, -35.282f)), 1f, 1 << 21);
                Door1Pilot = GetNearEntity<Door>(GetGlobalPosition(new Vector3(37.24f, 3.275f, -40.485f)), 1f, 1 << 21);

                Door0Scientist = GetNearEntity<Door>(GetGlobalPosition(new Vector3(-20.976f, 4.752f, -19.55f)), 1f, 1 << 21);
                Door1Scientist = GetNearEntity<Door>(GetGlobalPosition(new Vector3(-16.5f, 4.752f, -18f)), 1f, 1 << 21);
                Door2Scientist = GetNearEntity<Door>(GetGlobalPosition(new Vector3(-10.522f, 4.752f, -18f)), 1f, 1 << 21);
                Door3Scientist = GetNearEntity<Door>(GetGlobalPosition(new Vector3(7.22f, 5.525f, 17.954f)), 1f, 1 << 21);
                Door4Scientist = GetNearEntity<Door>(GetGlobalPosition(new Vector3(5.739f, 5.525f, 11.933f)), 1f, 1 << 21);
            }

            private static CodeLock SetCodeLock(Door door, string code)
            {
                CodeLock codelock = GameManager.server.CreateEntity("assets/prefabs/locks/keypad/lock.code.prefab") as CodeLock;
                codelock.SetParent(door, door.GetSlotAnchorName(BaseEntity.Slot.Lock));
                codelock.enableSaving = false;
                codelock.Spawn();
                codelock.skinID = 11162132011012;
                door.SetSlot(BaseEntity.Slot.Lock, codelock);
                codelock.code = code;
                codelock.hasCode = true;
                codelock.SetFlag(BaseEntity.Flags.Locked, true);
                return codelock;
            }

            internal void TryToOpenDoor(BasePlayer target, CodeLock codeLock, string code)
            {
                if (codeLock == CodeLockPilot)
                {
                    if (code != CodePilot) return;
                    if (_ins.ActivePveMode && _ins.PveMode.Call("CanActionEvent", _ins.Name, target) != null) return;
                    _ins.AlertToAllPlayers("OpenDoorPilot", _config.Chat.Prefix, target.displayName);
                    _ins.ActionEconomy(target.userID, "OpenDoorPilot");
                    Door0Pilot.SetOpen(true);
                    PilotGoToCardReader1();
                    _ins.NextTick(() => { if (codeLock.IsExists()) codeLock.Kill(); });
                    SnowmobilePilotCoroutine = ServerMgr.Instance.StartCoroutine(SpawnSnowmobiles());
                }
                else if (codeLock == CodeLockScientist)
                {
                    if (code != CodeScientist) return;
                    if (_ins.ActivePveMode && _ins.PveMode.Call("CanActionEvent", _ins.Name, target) != null) return;
                    _ins.AlertToAllPlayers("OpenDoorScientist", _config.Chat.Prefix, target.displayName);
                    _ins.ActionEconomy(target.userID, "OpenDoorScientist");
                    CanChangeTimeToFinish = false;
                    Door0Scientist.SetOpen(true);
                    Scientist.skinID = 19395142091920;
                    ScientistGoToCorpse();
                    _ins.NextTick(() => { if (codeLock.IsExists()) codeLock.Kill(); });
                    SnowmobileScientistCoroutine = ServerMgr.Instance.StartCoroutine(SpawnSnowmobiles());
                }
            }

            private void CalculateLocations()
            {
                foreach (PointAnimationTransform point in MinicopterLocalPointsToCardReader2) MinicopterGlobalPointsToCardReader2.Add(new PointAnimationTransform { Pos = GetGlobalPosition(point.Pos), Rot = GetGlobalRotation(point.Rot).eulerAngles });
                foreach (PointAnimationTransform point in MinicopterLocalPointsToExit) MinicopterGlobalPointsToExit.Add(new PointAnimationTransform { Pos = GetGlobalPosition(point.Pos), Rot = GetGlobalRotation(point.Rot).eulerAngles });

                MinicopterLastPointToCardReader2 = GetGlobalPosition(new Vector3(39f, 0.1f, -8.04f));

                foreach (Vector3 vector3 in PilotLocalPointsToCardReader1) PilotGlobalPointsToCardReader1.Add(GetGlobalPosition(vector3));
                foreach (Vector3 vector3 in PilotLocalPointsToMinicopter1) PilotGlobalPointsToMinicopter1.Add(GetGlobalPosition(vector3));
                foreach (Vector3 vector3 in PilotLocalPointsToCardReader2) PilotGlobalPointsToCardReader2.Add(GetGlobalPosition(vector3));
                foreach (Vector3 vector3 in PilotLocalPointsToMinicopter2) PilotGlobalPointsToMinicopter2.Add(GetGlobalPosition(vector3));

                PilotLastPointToCardReader1 = GetGlobalPosition(new Vector3(21.181f, 0.2f, -1.369f));
                PilotLastPointToMinicopter1 = GetGlobalPosition(new Vector3(24f, 1.425f, 3.503f));
                PilotLastPointToCardReader2 = GetGlobalPosition(new Vector3(36.181f, 0.2f, -1.369f));
                PilotLastPointToMinicopter2 = GetGlobalPosition(new Vector3(39.308f, 0.2f, -7.15f));

                foreach (Vector3 vector3 in ScientistLocalPointsToCorpse) ScientistGlobalPointsToCorpse.Add(GetGlobalPosition(vector3));
                foreach (Vector3 vector3 in ScientistLocalPointsToMinicopter) ScientistGlobalPointsToMinicopter.Add(GetGlobalPosition(vector3));

                ScientistLastPointToCorpse = GetGlobalPosition(new Vector3(4.952f, 5.537f, 9.872f));

                HomePositionForAdditionalNpc = GetGlobalPosition(new Vector3(39f, 0.1f, -6f)).ToString();

                foreach (SnowmobilePath path in SnowmobileLocalPoints)
                {
                    SnowmobilePath result = new SnowmobilePath { Spawn = new PointAnimationTransform { Pos = GetGlobalPosition(path.Spawn.Pos), Rot = GetGlobalRotation(path.Spawn.Rot).eulerAngles }, Path = new HashSet<PointAnimationTransform>() };
                    foreach (PointAnimationTransform point in path.Path) result.Path.Add(new PointAnimationTransform { Pos = GetGlobalPosition(point.Pos), Rot = GetGlobalRotation(point.Rot).eulerAngles });
                    SnowmobileGlobalPoints.Add(result);
                }

                SnowmobileCh47StartGlobalPoints = new SnowmobilePath { Spawn = new PointAnimationTransform { Pos = GetGlobalPosition(SnowmobileCh47StartLocalPoints.Spawn.Pos), Rot = GetGlobalRotation(SnowmobileCh47StartLocalPoints.Spawn.Rot).eulerAngles }, Path = new HashSet<PointAnimationTransform>() };
                foreach (PointAnimationTransform point in SnowmobileCh47StartLocalPoints.Path) SnowmobileCh47StartGlobalPoints.Path.Add(new PointAnimationTransform { Pos = GetGlobalPosition(point.Pos), Rot = GetGlobalRotation(point.Rot).eulerAngles });

                foreach (SnowmobilePath path in SnowmobileCh47LocalPoints)
                {
                    SnowmobilePath result = new SnowmobilePath { Spawn = new PointAnimationTransform { Pos = Vector3.zero, Rot = Vector3.zero }, Path = new HashSet<PointAnimationTransform>() };
                    foreach (PointAnimationTransform point in path.Path) result.Path.Add(new PointAnimationTransform { Pos = GetGlobalPosition(point.Pos), Rot = GetGlobalRotation(point.Rot).eulerAngles });
                    SnowmobileCh47GlobalPoints.Add(result);
                }
            }

            private void SpawnCrates()
            {
                foreach (CrateConfig crateConfig in Scientist == null ? _config.CratesFailure : _config.CratesSuccess)
                {
                    LootContainer crate = GameManager.server.CreateEntity(crateConfig.Prefab, GetGlobalPosition(crateConfig.Position.ToVector3()), GetGlobalRotation(crateConfig.Rotation.ToVector3())) as LootContainer;
                    crate.enableSaving = false;
                    crate.Spawn();
                    Crates.Add(crate, crateConfig.TypeLootTable);
                    if (crateConfig.TypeLootTable == 1 || crateConfig.TypeLootTable == 4 || crateConfig.TypeLootTable == 5)
                    {
                        _ins.NextTick(() =>
                        {
                            crate.inventory.ClearItemsContainer();
                            if (crateConfig.TypeLootTable == 4 || crateConfig.TypeLootTable == 5) _ins.AddToContainerPrefab(crate.inventory, crateConfig.PrefabLootTable);
                            if (crateConfig.TypeLootTable == 1 || crateConfig.TypeLootTable == 5) _ins.AddToContainerItem(crate.inventory, crateConfig.OwnLootTable);
                        });
                    }
                }
            }

            private void SpawnHackCrate()
            {
                HackCrate = GameManager.server.CreateEntity("assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab", GetGlobalPosition(_config.HackCrate.Position.ToVector3()), GetGlobalRotation(_config.HackCrate.Rotation.ToVector3())) as HackableLockedCrate;
                HackCrate.enableSaving = false;
                HackCrate.Spawn();

                HackCrate.hackSeconds = HackableLockedCrate.requiredHackSeconds - _config.HackCrate.UnlockTime;

                HackCrate.shouldDecay = false;
                HackCrate.CancelInvoke(HackCrate.DelayedDestroy);

                HackCrate.KillMapMarker();

                if (_config.HackCrate.TypeLootTable is 1 or 4 or 5)
                {
                    _ins.NextTick(() =>
                    {
                        HackCrate.inventory.ClearItemsContainer();
                        if (_config.HackCrate.TypeLootTable is 4 or 5) _ins.AddToContainerPrefab(HackCrate.inventory, _config.HackCrate.PrefabLootTable);
                        if (_config.HackCrate.TypeLootTable is 1 or 5) _ins.AddToContainerItem(HackCrate.inventory, _config.HackCrate.OwnLootTable);
                    });
                }
            }

            private static bool IsDefaultNpc(ScientistNPC npc)
            {
                if (!npc.IsExists()) return false;
                if (npc.skinID != 0) return false;
                if (npc.ShortPrefabName != "scientistnpc_roam" && npc.ShortPrefabName != "scientistnpc_roamtethered" && npc.ShortPrefabName != "scientistnpc_patrol") return false;
                if (!npc.inventory.containerWear.itemList.Any(x => x.info.shortname == "hazmatsuit_scientist_arctic")) return false;
                return true;
            }

            private void SpawnPreset(PresetConfig preset)
            {
                int count = UnityEngine.Random.Range(preset.Min, preset.Max + 1);

                List<Vector3> positions = Pool.Get<List<Vector3>>();
                foreach (string pos in preset.Positions) positions.Add(GetGlobalPosition(pos.ToVector3()));

                JObject config = GetObjectConfig(preset.Config);

                for (int i = 0; i < count; i++)
                {
                    Vector3 pos = positions.GetRandom();
                    positions.Remove(pos);

                    ScientistNPC npc = (ScientistNPC)_ins.NpcSpawn.Call("SpawnNpc", pos, config);
                    Scientists.Add(npc);
                }

                Pool.FreeUnmanaged(ref positions);
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
                    ["HostileTargetsOnly"] = false,
                    ["VisionCone"] = config.VisionCone,
                    ["DamageScale"] = config.DamageScale,
                    ["TurretDamageScale"] = 0f,
                    ["AimConeScale"] = config.AimConeScale,
                    ["DisableRadio"] = config.DisableRadio,
                    ["CanRunAwayWater"] = true,
                    ["CanSleep"] = false,
                    ["SleepDistance"] = 100f,
                    ["Speed"] = config.Speed,
                    ["AreaMask"] = 25,
                    ["AgentTypeID"] = 0,
                    ["HomePosition"] = string.Empty,
                    ["MemoryDuration"] = config.MemoryDuration,
                    ["States"] = new JArray { states },
                    ["NpcWhitelist"] = "FrankensteinPet,14922524,19395142091920"
                };
            }

            private IEnumerator SpawnNpcInSecurityHouse()
            {
                JObject objectConfig = new JObject
                {
                    ["Name"] = _config.ConfigNpcRepairMinicopter.Name,
                    ["WearItems"] = new JArray { _config.ConfigNpcRepairMinicopter.WearItems.Select(x => new JObject { ["ShortName"] = x.ShortName, ["SkinID"] = x.SkinId }) },
                    ["BeltItems"] = new JArray { _config.ConfigNpcRepairMinicopter.BeltItems.Select(x => new JObject { ["ShortName"] = x.ShortName, ["Amount"] = x.Amount, ["SkinID"] = x.SkinId, ["Mods"] = new JArray { x.Mods }, ["Ammo"] = x.Ammo }) },
                    ["Kit"] = _config.ConfigNpcRepairMinicopter.Kit,
                    ["Health"] = _config.ConfigNpcRepairMinicopter.Health,
                    ["RoamRange"] = 10f,
                    ["ChaseRange"] = _config.ConfigNpcRepairMinicopter.ChaseRange,
                    ["SenseRange"] = _config.ConfigNpcRepairMinicopter.SenseRange,
                    ["ListenRange"] = _config.ConfigNpcRepairMinicopter.SenseRange / 2f,
                    ["AttackRangeMultiplier"] = _config.ConfigNpcRepairMinicopter.AttackRangeMultiplier,
                    ["CheckVisionCone"] = false,
                    ["VisionCone"] = 135f,
                    ["HostileTargetsOnly"] = false,
                    ["DamageScale"] = _config.ConfigNpcRepairMinicopter.DamageScale,
                    ["TurretDamageScale"] = 0f,
                    ["AimConeScale"] = _config.ConfigNpcRepairMinicopter.AimConeScale,
                    ["DisableRadio"] = _config.ConfigNpcRepairMinicopter.DisableRadio,
                    ["CanRunAwayWater"] = false,
                    ["CanSleep"] = false,
                    ["SleepDistance"] = 100f,
                    ["Speed"] = _config.ConfigNpcRepairMinicopter.Speed,
                    ["AreaMask"] = 25,
                    ["AgentTypeID"] = 0,
                    ["HomePosition"] = HomePositionForAdditionalNpc,
                    ["MemoryDuration"] = _config.ConfigNpcRepairMinicopter.MemoryDuration,
                    ["States"] = new JArray { "RoamState", "ChaseState", "CombatState" },
                    ["NpcWhitelist"] = "FrankensteinPet,14922524,19395142091920"
                };

                List<Vector3> positions = Pool.Get<List<Vector3>>();
                foreach (Vector3 pos in LocalPointsInSecurityHouse) positions.Add(GetGlobalPosition(pos));

                for (int i = 0; i < _config.NumberNpcRepairMinicopter; i++)
                {
                    ScientistNPC npc = (ScientistNPC)_ins.NpcSpawn.Call("SpawnNpc", positions.GetRandom(), objectConfig);
                    Scientists.Add(npc);
                    yield return CoroutineEx.waitForSeconds(1f);
                }

                Pool.FreeUnmanaged(ref positions);
            }

            private void SpawnPilot()
            {
                Pilot = GameManager.server.CreateEntity("assets/prefabs/player/player.prefab", GetGlobalPosition(new Vector3(42.257f, 3.275f, -35.304f))) as BasePlayer;
                Pilot.enableSaving = false;
                Pilot.Spawn();

                Pilot.displayName = _config.PilotName;

                foreach (Item item in _config.PilotWearItems.Select(x => ItemManager.CreateByName(x.ShortName, 1, x.SkinId)))
                {
                    if (item == null) continue;
                    if (!Pilot.inventory.containerWear.Insert(item)) item.Remove();
                }

                AnimationPilot = Pilot.gameObject.AddComponent<AnimationTransformBasePlayer>();
            }

            private IEnumerator StartRepair()
            {
                RepairTime = _config.RepairTime;

                Item item = ItemManager.CreateByName("longsword", 1, 1797999811);
                item.MoveToContainer(Pilot.inventory.containerBelt);
                Pilot.UpdateActiveItem(item.uid);

                BaseMelee weapon = Pilot.GetHeldEntity() as BaseMelee;

                while (RepairTime > 0)
                {
                    if (!weapon.HasAttackCooldown())
                    {
                        weapon.StartAttackCooldown(weapon.repeatDelay);
                        Pilot.SignalBroadcast(BaseEntity.Signal.Attack, string.Empty, null);
                        if (weapon.swingEffect.isValid) Effect.server.Run(weapon.swingEffect.resourcePath, weapon.transform.position, Vector3.forward, Pilot.net.connection, false);
                        Effect.server.Run("assets/bundled/prefabs/fx/build/repair_metal.prefab", Pilot.transform.position);
                    }
                    yield return CoroutineEx.waitForSeconds(1f);
                    if (RepairTime > 0) RepairTime--;
                }

                item.RemoveFromContainer();
                item.Remove();

                PilotSeat.AttemptMount(Pilot, false);
                MiniCopter.engineController.TryStartEngine(Pilot);
                Invoke(MinicopterGoToExit, 3f);
            }

            private void SpawnScientist()
            {
                Scientist = GameManager.server.CreateEntity("assets/prefabs/player/player.prefab", GetGlobalPosition(new Vector3(-20.976f, 4.752f, -20.289f))) as BasePlayer;
                Scientist.enableSaving = false;
                Scientist.Spawn();

                Scientist.displayName = _config.ScientistName;
                ScientistHealth = _config.ScientistHealth;

                foreach (Item item in _config.ScientistWearItems.Select(x => ItemManager.CreateByName(x.ShortName, 1, x.SkinId)))
                {
                    if (item == null) continue;
                    if (!Scientist.inventory.containerWear.Insert(item)) item.Remove();
                }

                AnimationScientist = Scientist.gameObject.AddComponent<AnimationTransformBasePlayer>();
            }

            internal void TakeDamageScientist(float damage)
            {
                ScientistHealth -= damage;
                if (ScientistHealth > 0f) return;
                ScientistHealth = 0f;
                if (Scientist.IsExists())
                {
                    Scientist.EnsureDismounted();
                    Scientist.Kill();
                }
                if (CodeLockPilot.IsExists())
                {
                    Door0Pilot.SetOpen(true);
                    PilotGoToCardReader1();
                    _ins.NextTick(() => { if (CodeLockPilot.IsExists()) CodeLockPilot.Kill(); });
                    SnowmobilePilotCoroutine = ServerMgr.Instance.StartCoroutine(SpawnSnowmobiles());
                }
                else if (Pilot.isMounted) MinicopterGoToCardReader2();
                _ins.AlertToAllPlayers("ScientistKill", _config.Chat.Prefix);
            }

            private IEnumerator StartResearch()
            {
                ResearchTime = _config.ResearchTime;

                Item item = ItemManager.CreateByName("knife.butcher");
                item.MoveToContainer(Scientist.inventory.containerBelt);
                Scientist.UpdateActiveItem(item.uid);

                BaseMelee weapon = Scientist.GetHeldEntity() as BaseMelee;

                while (ResearchTime > 0 || !MainDoor1.IsOpen())
                {
                    if (Scientist == null)
                    {
                        item.Remove();
                        yield break;
                    }
                    if (!weapon.HasAttackCooldown())
                    {
                        weapon.StartAttackCooldown(weapon.repeatDelay);
                        Scientist.SignalBroadcast(BaseEntity.Signal.Attack, string.Empty, null);
                        if (weapon.swingEffect.isValid) Effect.server.Run(weapon.swingEffect.resourcePath, weapon.transform.position, Vector3.forward, Scientist.net.connection, false);
                        Effect.server.Run("assets/bundled/prefabs/fx/gestures/cut_meat.prefab", Scientist.transform.position);
                    }
                    yield return CoroutineEx.waitForSeconds(1f);
                    if (ResearchTime > 0) ResearchTime--;
                }

                item.RemoveFromContainer();
                item.Remove();

                foreach (BasePlayer player in Players) _ins.AlertToPlayer(player, _ins.GetMessage("ScientistSuccess", player.UserIDString, _config.Chat.Prefix));

                ScientistGoToMinicopter();
            }

            private void SpawnMinicopter()
            {
                MiniCopter = GameManager.server.CreateEntity("assets/content/vehicles/minicopter/minicopter.entity.prefab", GetGlobalPosition(new Vector3(24f, 1.425f, 5.747f)), GetGlobalRotation(new Vector3(0f, 180f, 0f))) as Minicopter;
                MiniCopter.enableSaving = false;
                MiniCopter.Spawn();

                foreach (BaseEntity entity in MiniCopter.children)
                {
                    if (entity.ShortPrefabName == "miniheliseat") PilotSeat = entity as BaseVehicleSeat;
                    else if (entity.ShortPrefabName == "minihelipassenger") ScientistSeat = entity as BaseVehicleSeat;
                    else if (entity.ShortPrefabName == "fuel_storage")
                    {
                        StorageContainer container = entity as StorageContainer;
                        ItemManager.CreateByName("lowgradefuel", 1000000).MoveToContainer(container.inventory);
                        container.SetFlag(BaseEntity.Flags.Locked, true);
                    }
                }

                MiniCopter.SetToKinematic();

                AnimationMinicopter = MiniCopter.gameObject.AddComponent<AnimationTransformVehicle>();
            }

            private void CheckMinicopterKinematic()
            {
                if (MiniCopter == null || MiniCopter.rigidBody == null) return;
                if (!MiniCopter.rigidBody.isKinematic) MiniCopter.SetToKinematic();
            }

            private void DestroyMinicopter()
            {
                if (!MiniCopter.IsExists()) return;
                EntityFuelSystem fuelSystem = MiniCopter.GetFuelSystem() as EntityFuelSystem;
                fuelSystem.GetFuelContainer().inventory.ClearItemsContainer();
                MiniCopter.Kill();
            }

            private IEnumerator SpawnSnowmobiles()
            {
                foreach (BasePlayer player in Players) _ins.AlertToPlayer(player, _ins.GetMessage("Snowmobiles", player.UserIDString, _config.Chat.Prefix));
                for (int i = 0; i < _config.NumberSnowmobilesOpenDoor; i++)
                {
                    SnowmobilePath path = SnowmobileGlobalPoints.GetRandom();

                    Snowmobile snowmobile = GameManager.server.CreateEntity("assets/content/vehicles/snowmobiles/tomahasnowmobile.prefab", path.Spawn.Pos, Quaternion.Euler(path.Spawn.Rot)) as Snowmobile;
                    snowmobile.enableSaving = false;
                    snowmobile.Spawn();

                    SnowmobileData data = new SnowmobileData { Snowmobile = snowmobile, Driver = null, Passenger = null, Animation = null, Coroutine = null, IsGround = true };

                    foreach (BaseEntity entity in snowmobile.children)
                    {
                        if (entity.ShortPrefabName == "snowmobilefuelstorage")
                        {
                            StorageContainer container = entity as StorageContainer;
                            ItemManager.CreateByName("lowgradefuel", 1000000).MoveToContainer(container.inventory);
                            container.SetFlag(BaseEntity.Flags.Locked, true);
                        }
                        else if (entity.ShortPrefabName == "snowmobileitemstorage") entity.SetFlag(BaseEntity.Flags.Locked, true);
                        else if (entity.ShortPrefabName == "snowmobiledriverseat")
                        {
                            ScientistNPC npc = SpawnSnowmobileNpc(path.Spawn.Pos, true, true);
                            (entity as MouseSteerableSeat).AttemptMount(npc, false);
                            snowmobile.engineController.TryStartEngine(npc);
                            snowmobile.LightToggle(npc);
                            data.Driver = npc;
                        }
                        else if (entity.ShortPrefabName == "snowmobilepassengerseat tomaha")
                        {
                            ScientistNPC npc = SpawnSnowmobileNpc(path.Spawn.Pos, true, false);
                            (entity as BaseVehicleSeat).AttemptMount(npc, false);
                            data.Passenger = npc;
                        }
                    }

                    Snowmobiles.Add(data);

                    snowmobile.rigidBody.isKinematic = true;

                    data.Animation = snowmobile.gameObject.AddComponent<AnimationTransformVehicle>();
                    data.Animation.AddPath(path.Path, 4f, 12f);

                    yield return CoroutineEx.waitForSeconds(3f);
                }
            }

            private ScientistNPC SpawnSnowmobileNpc(Vector3 pos, bool isMount, bool isDriver)
            {
                HashSet<string> states = new HashSet<string>();
                if (isMount)
                {
                    states.Add("IdleState");
                    if (!isDriver) states.Add("CombatStationaryState");
                }
                else
                {
                    states.Add("RoamState");
                    states.Add("ChaseState");
                    states.Add("CombatState");
                }

                JObject objectConfig = new JObject
                {
                    ["Name"] = _config.ConfigNpcSnowmobile.Name,
                    ["WearItems"] = new JArray { _config.ConfigNpcSnowmobile.WearItems.Select(x => new JObject { ["ShortName"] = x.ShortName, ["SkinID"] = x.SkinId }) },
                    ["BeltItems"] = new JArray(),
                    ["Kit"] = _config.ConfigNpcSnowmobile.Kit,
                    ["Health"] = _config.ConfigNpcSnowmobile.Health,
                    ["RoamRange"] = 10f,
                    ["ChaseRange"] = _config.ConfigNpcSnowmobile.ChaseRange,
                    ["SenseRange"] = _config.ConfigNpcSnowmobile.SenseRange,
                    ["ListenRange"] = _config.ConfigNpcSnowmobile.SenseRange / 2f,
                    ["AttackRangeMultiplier"] = _config.ConfigNpcSnowmobile.AttackRangeMultiplier,
                    ["CheckVisionCone"] = false,
                    ["VisionCone"] = 135f,
                    ["HostileTargetsOnly"] = false,
                    ["DamageScale"] = _config.ConfigNpcSnowmobile.DamageScale,
                    ["TurretDamageScale"] = 0f,
                    ["AimConeScale"] = _config.ConfigNpcSnowmobile.AimConeScale,
                    ["DisableRadio"] = _config.ConfigNpcSnowmobile.DisableRadio,
                    ["CanRunAwayWater"] = false,
                    ["CanSleep"] = false,
                    ["SleepDistance"] = 100f,
                    ["Speed"] = _config.ConfigNpcSnowmobile.Speed,
                    ["AreaMask"] = 25,
                    ["AgentTypeID"] = 0,
                    ["HomePosition"] = string.Empty,
                    ["MemoryDuration"] = _config.ConfigNpcSnowmobile.MemoryDuration,
                    ["States"] = new JArray { states },
                    ["NpcWhitelist"] = "FrankensteinPet,14922524,19395142091920"
                };
                if (!isMount)
                {
                    objectConfig["HomePosition"] = HomePositionForAdditionalNpc;
                    objectConfig["BeltItems"] = new JArray { _config.ConfigNpcSnowmobile.BeltItems.Select(x => new JObject { ["ShortName"] = x.ShortName, ["Amount"] = x.Amount, ["SkinID"] = x.SkinId, ["Mods"] = new JArray { x.Mods }, ["Ammo"] = x.Ammo }) };
                }

                ScientistNPC npc = (ScientistNPC)_ins.NpcSpawn.Call("SpawnNpc", pos, objectConfig);

                states = null;

                if (isMount && npc.NavAgent.enabled)
                {
                    npc.NavAgent.destination = npc.transform.position;
                    npc.NavAgent.isStopped = true;
                    npc.NavAgent.enabled = false;
                }

                return npc;
            }

            private IEnumerator DestroySnowmobile(SnowmobileData data)
            {
                yield return CoroutineEx.waitForSeconds(1f);

                if (data.Driver.IsExists())
                {
                    data.Driver.EnsureDismounted();
                    data.Driver.Kill();
                }
                ScientistNPC driver = SpawnSnowmobileNpc(data.Snowmobile.transform.position, false, false);
                _ins.NpcSpawn.Call("SetParent", driver, Scientist.transform, Vector3.zero, 1f);
                Scientists.Add(driver);

                if (data.Passenger.IsExists())
                {
                    data.Passenger.EnsureDismounted();
                    data.Passenger.Kill();
                }
                ScientistNPC passenger = SpawnSnowmobileNpc(data.Snowmobile.transform.position, false, false);
                _ins.NpcSpawn.Call("SetParent", passenger, Scientist.transform, Vector3.zero, 1f);
                Scientists.Add(passenger);

                if (_ins.ActivePveMode) _ins.PveMode.Call("EventAddScientists", _ins.Name, new HashSet<ulong> { driver.net.ID.Value, passenger.net.ID.Value });

                yield return CoroutineEx.waitForSeconds(1f);

                if (data.Snowmobile.IsExists())
                {
                    EntityFuelSystem fuelSystem = data.Snowmobile.GetFuelSystem() as EntityFuelSystem;
                    fuelSystem.GetFuelContainer().inventory.ClearItemsContainer();
                    data.Snowmobile.Kill();
                }

                Snowmobiles.Remove(data);
            }

            private IEnumerator ProcessCh47()
            {
                Vector3 pos = GetGlobalPosition(new Vector3(-27f, 9f, -18f));
                Quaternion rot = GetGlobalRotation(new Vector3(0f, 270f, 0f));

                SpawnNewCh47(pos + new Vector3(0f, 100f, 0f), rot, pos);

                while (Ch47.transform.position.y - Ch47Ai.currentDesiredAltitude > 1f) yield return CoroutineEx.waitForSeconds(1f);

                Ch47Ai.AiAltitudeForce = 0f;
                Ch47Ai.rigidBody.AddForce(Vector3.down * 10000f, ForceMode.Force);

                while (Ch47.transform.position.y - pos.y > 1.5f)
                {
                    Ch47Ai.rigidBody.AddForce(Vector3.down * 10000f, ForceMode.Force);
                    yield return CoroutineEx.waitForSeconds(1f);
                }

                Ch47Ai.rigidBody.isKinematic = true;
                Ch47.transform.position = pos;

                for (int i = 0; i < _config.NumberSnowmobilesResearch; i++)
                {
                    Snowmobile snowmobile = GameManager.server.CreateEntity("assets/content/vehicles/snowmobiles/tomahasnowmobile.prefab", SnowmobileCh47StartGlobalPoints.Spawn.Pos, Quaternion.Euler(SnowmobileCh47StartGlobalPoints.Spawn.Rot)) as Snowmobile;
                    snowmobile.enableSaving = false;
                    snowmobile.Spawn();

                    SnowmobileData data = new SnowmobileData { Snowmobile = snowmobile, Driver = null, Passenger = null, Coroutine = null, IsGround = false };

                    foreach (BaseEntity entity in snowmobile.children)
                    {
                        if (entity.ShortPrefabName == "snowmobilefuelstorage")
                        {
                            StorageContainer container = entity as StorageContainer;
                            ItemManager.CreateByName("lowgradefuel", 1000000).MoveToContainer(container.inventory);
                            container.SetFlag(BaseEntity.Flags.Locked, true);
                        }
                        else if (entity.ShortPrefabName == "snowmobileitemstorage") entity.SetFlag(BaseEntity.Flags.Locked, true);
                        else if (entity.ShortPrefabName == "snowmobiledriverseat")
                        {
                            ScientistNPC npc = SpawnSnowmobileNpc(SnowmobileCh47StartGlobalPoints.Spawn.Pos, true, true);
                            (entity as MouseSteerableSeat).AttemptMount(npc, false);
                            snowmobile.engineController.TryStartEngine(npc);
                            snowmobile.LightToggle(npc);
                            data.Driver = npc;
                        }
                        else if (entity.ShortPrefabName == "snowmobilepassengerseat tomaha")
                        {
                            ScientistNPC npc = SpawnSnowmobileNpc(SnowmobileCh47StartGlobalPoints.Spawn.Pos, true, false);
                            (entity as BaseVehicleSeat).AttemptMount(npc, false);
                            data.Passenger = npc;
                        }
                    }

                    Snowmobiles.Add(data);

                    snowmobile.rigidBody.isKinematic = true;

                    data.Animation = snowmobile.gameObject.AddComponent<AnimationTransformVehicle>();
                    data.Animation.AddPath(SnowmobileCh47StartGlobalPoints.Path, 4f, 12f);

                    yield return CoroutineEx.waitForSeconds(3f);
                }

                List<float> list = Pool.Get<List<float>>();
                list.Add(-World.Size / 2f);
                list.Add(World.Size / 2f);
                Vector3 destroyPos = new Vector3(list.GetRandom(), 200f, list.GetRandom());
                Pool.FreeUnmanaged(ref list);

                SpawnNewCh47(Ch47.transform.position, Ch47.transform.rotation, destroyPos);

                while (Vector2.Distance(new Vector2(Ch47.transform.position.x, Ch47.transform.position.z), new Vector2(destroyPos.x, destroyPos.z)) > 1f) yield return CoroutineEx.waitForSeconds(1f);

                if (Ch47.IsExists()) Ch47.Kill();
            }

            private void SpawnNewCh47(Vector3 pos, Quaternion rot, Vector3 landingTarget)
            {
                CH47Helicopter ch47New = GameManager.server.CreateEntity("assets/prefabs/npc/ch47/ch47scientists.entity.prefab", pos, rot) as CH47Helicopter;
                CH47HelicopterAIController ch47AInew = ch47New.GetComponent<CH47HelicopterAIController>();

                ch47AInew.SetLandingTarget(landingTarget);

                if (Ch47.IsExists()) Ch47.Kill();

                Ch47 = ch47New;
                Ch47Ai = ch47AInew;

                Ch47.Spawn();
                Ch47Ai.CancelInvoke(Ch47Ai.GetPrivateAction("CheckSpawnScientists"));
                Ch47.rigidBody.detectCollisions = false;
                Ch47Ai.numCrates = 0;
                Ch47Ai.SetMinHoverHeight(0f);
            }

            internal void CheckOpenDoor(BasePlayer basePlayer)
            {
                if (basePlayer == Pilot)
                {
                    if (Vector3.Distance(basePlayer.transform.position, Door0Pilot.transform.position) < 2f) Door0Pilot.SetOpen(true);
                    else if (Vector3.Distance(basePlayer.transform.position, Door1Pilot.transform.position) < 2f) Door1Pilot.SetOpen(true);
                }
                else
                {
                    if (Vector3.Distance(basePlayer.transform.position, Door0Scientist.transform.position) < 2f) Door0Scientist.SetOpen(true);
                    else if (Vector3.Distance(basePlayer.transform.position, Door1Scientist.transform.position) < 2f) Door1Scientist.SetOpen(true);
                    else if (Vector3.Distance(basePlayer.transform.position, Door2Scientist.transform.position) < 2f) Door2Scientist.SetOpen(true);
                    else if (Vector3.Distance(basePlayer.transform.position, Door3Scientist.transform.position) < 2f) Door3Scientist.SetOpen(true);
                    else if (Vector3.Distance(basePlayer.transform.position, Door4Scientist.transform.position) < 2f) Door4Scientist.SetOpen(true);
                }
            }

            internal void FinishPathBasePlayer(BasePlayer basePlayer)
            {
                if (basePlayer == Pilot)
                {
                    if (basePlayer.transform.position == PilotLastPointToCardReader1)
                    {
                        MainDoor1.SetOpen(true);
                        Invoke(PilotGoToMinicopter1, 15f);
                    }
                    else if (basePlayer.transform.position == PilotLastPointToMinicopter1)
                    {
                        PilotSeat.AttemptMount(Pilot, false);
                        MiniCopter.engineController.TryStartEngine(Pilot);
                        if (Scientist == null) MinicopterGoToCardReader2();
                    }
                    else if (basePlayer.transform.position == PilotLastPointToCardReader2)
                    {
                        SpawnCrates();
                        SpawnHackCrate();
                        if (_ins.ActivePveMode)
                        {
                            HashSet<ulong> crates = new HashSet<ulong> { HackCrate.net.ID.Value };
                            foreach (KeyValuePair<LootContainer, int> dic in Crates) crates.Add(dic.Key.net.ID.Value);
                            _ins.PveMode.Call("EventAddCrates", _ins.Name, crates);
                            crates = null;
                        }
                        MainDoor2.SetOpen(true);
                        foreach (BasePlayer player in Players) _ins.AlertToPlayer(player, _ins.GetMessage("PilotOpenLoot", player.UserIDString, _config.Chat.Prefix));
                        PilotGoToMinicopter2();
                    }
                    else if (basePlayer.transform.position == PilotLastPointToMinicopter2)
                    {
                        foreach (BasePlayer player in Players) _ins.AlertToPlayer(player, _ins.GetMessage("PilotRepair", player.UserIDString, _config.Chat.Prefix));
                        RepairCoroutine = ServerMgr.Instance.StartCoroutine(StartRepair());
                        SpawnNpcInSecurityHouseCoroutine = ServerMgr.Instance.StartCoroutine(SpawnNpcInSecurityHouse());
                    }
                }
                else
                {
                    if (basePlayer.transform.position == ScientistLastPointToCorpse)
                    {
                        if (_config.NumberSnowmobilesResearch > 0) Ch47Coroutine = ServerMgr.Instance.StartCoroutine(ProcessCh47());
                        ResearchCoroutine = ServerMgr.Instance.StartCoroutine(StartResearch());
                    }
                    else
                    {
                        ScientistSeat.AttemptMount(Scientist, false);
                        MinicopterGoToCardReader2();
                    }
                }
            }

            internal void FinishPathVehicle(BaseEntity entity)
            {
                if (entity == MiniCopter)
                {
                    if (MiniCopter.transform.position == MinicopterLastPointToCardReader2)
                    {
                        MiniCopter.engineController.FinishStartingEngine();
                        PilotSeat.AttemptDismount(Pilot);
                        PilotGoToCardReader2();
                    }
                    else
                    {
                        if (Pilot.IsExists())
                        {
                            Pilot.EnsureDismounted();
                            Pilot.Kill();
                        }
                        if (Scientist.IsExists())
                        {
                            Scientist.EnsureDismounted();
                            Scientist.Kill();
                        }
                        DestroyMinicopter();
                        TimeToFinish = _config.FinishTime;
                        CanChangeTimeToFinish = true;
                    }
                }
                else
                {
                    SnowmobileData data = Snowmobiles.FirstOrDefault(x => x.Snowmobile == entity);
                    if (data.IsGround) data.Coroutine = ServerMgr.Instance.StartCoroutine(DestroySnowmobile(data));
                    else
                    {
                        data.IsGround = true;
                        data.Snowmobile.rigidBody.isKinematic = false;
                        data.Snowmobile.rigidBody.AddForce(data.Snowmobile.transform.forward * 50000f, ForceMode.Force);
                        Invoke(() =>
                        {
                            data.Snowmobile.rigidBody.isKinematic = true;
                            data.Animation.AddPath(SnowmobileCh47GlobalPoints.GetRandom().Path, 8f, 12f);
                        }, 1.7f);
                    }
                }
            }

            private void PilotGoToCardReader1() => AnimationPilot.AddPath(PilotGlobalPointsToCardReader1);

            private void PilotGoToMinicopter1() => AnimationPilot.AddPath(PilotGlobalPointsToMinicopter1);

            private void PilotGoToCardReader2() => AnimationPilot.AddPath(PilotGlobalPointsToCardReader2);

            private void PilotGoToMinicopter2() => AnimationPilot.AddPath(PilotGlobalPointsToMinicopter2);

            private void ScientistGoToCorpse() => AnimationScientist.AddPath(ScientistGlobalPointsToCorpse);

            private void ScientistGoToMinicopter() => AnimationScientist.AddPath(ScientistGlobalPointsToMinicopter);

            private void MinicopterGoToCardReader2() => AnimationMinicopter.AddPath(MinicopterGlobalPointsToCardReader2, 0.25f, 4f);

            private void MinicopterGoToExit() => AnimationMinicopter.AddPath(MinicopterGlobalPointsToExit, 1f, 10f);

            private void TryTeleportPlayer(BasePlayer player)
            {
                if (player._limitedNetworking) return;
                if (IsInsideRoom1(player) || IsInsideRoom2(player))
                {
                    Vector3 pos = GetGlobalPosition(new Vector3(51.512f, 0f, -44.277f));
                    player.Teleport(pos);
                }
            }

            private bool IsInsideRoom1(BasePlayer player)
            {
                Vector3 localPos = transform.InverseTransformPoint(player.transform.position);
                if (localPos.x is < 40.954f or > 43.954f) return false;
                if (localPos.y is < 3.235f or > 5.735f) return false;
                if (localPos.z is < -40.495f or > -34.495f) return false;
                return true;
            }

            private bool IsInsideRoom2(BasePlayer player)
            {
                Vector3 localPos = transform.InverseTransformPoint(player.transform.position);
                if (localPos.x is < -24.45f or > -19.45f) return false;
                if (localPos.y is < 4.701f or > 8.101f) return false;
                if (localPos.z is < -22.455f or > -19.455f) return false;
                return true;
            }

            internal void EnablePveMode(PveModeConfig config, BasePlayer player)
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
                    ["DamageTurret"] = false,
                    ["TargetNpc"] = config.TargetNpc,
                    ["TargetTank"] = false,
                    ["TargetHelicopter"] = false,
                    ["TargetTurret"] = false,
                    ["CanEnter"] = config.CanEnter,
                    ["CanEnterCooldownPlayer"] = config.CanEnterCooldownPlayer,
                    ["TimeExitOwner"] = config.TimeExitOwner,
                    ["AlertTime"] = config.AlertTime,
                    ["RestoreUponDeath"] = config.RestoreUponDeath,
                    ["CooldownOwner"] = config.CooldownOwner,
                    ["Darkening"] = config.Darkening
                };

                _ins.PveMode.Call("EventAddPveMode", _ins.Name, dic, transform.position, _config.Radius, new HashSet<ulong>(), Scientists.Select(x => x.net.ID.Value), new HashSet<ulong>(), new HashSet<ulong>(), new HashSet<ulong>(), new HashSet<ulong>(), player);
            }
        }
        #endregion Controller

        #region Animation
        internal class AnimationTransformBasePlayer : FacepunchBehaviour
        {
            private BasePlayer Main { get; set; } = null;

            private List<Vector3> Path { get; } = new List<Vector3>();

            private float SecondsTaken { get; set; } = 0f;
            private float SecondsToTake { get; set; } = 0f;
            private float WaypointDone { get; set; } = 0f;

            private Vector3 StartPos { get; set; } = Vector3.zero;
            private Vector3 EndPos { get; set; } = Vector3.zero;

            private void Awake()
            {
                Main = GetComponent<BasePlayer>();
                enabled = false;
            }

            internal void AddPath(HashSet<Vector3> path)
            {
                foreach (Vector3 point in path) Path.Add(point);
                enabled = true;
            }

            private void FixedUpdate()
            {
                if (SecondsTaken == 0f)
                {
                    if (Path.Count == 0)
                    {
                        StartPos = EndPos = Vector3.zero;
                        SecondsToTake = 0f;
                        SecondsTaken = 0f;
                        WaypointDone = 0f;
                        enabled = false;
                        _ins.Controller.FinishPathBasePlayer(Main);
                        return;
                    }
                    StartPos = transform.position;
                    if (Path[0] != StartPos)
                    {
                        EndPos = Path[0];
                        SecondsToTake = Vector3.Distance(EndPos, StartPos) / 3f;
                        Main.viewAngles = Quaternion.LookRotation(EndPos - StartPos).eulerAngles;
                        SecondsTaken = 0f;
                        WaypointDone = 0f;
                    }
                    Path.RemoveAt(0);
                }
                if (StartPos != EndPos)
                {
                    _ins.Controller.CheckOpenDoor(Main);
                    SecondsTaken += Time.deltaTime;
                    WaypointDone = Mathf.InverseLerp(0f, SecondsToTake, SecondsTaken);
                    transform.position = Vector3.Lerp(StartPos, EndPos, WaypointDone);
                    Main.viewAngles = Quaternion.LookRotation(EndPos - StartPos).eulerAngles;
                    Main.TransformChanged();
                    Main.SendNetworkUpdate();
                    if (WaypointDone >= 1f) SecondsTaken = 0f;
                }
            }
        }

        internal class PointAnimationTransform { public Vector3 Pos; public Vector3 Rot; }

        internal class AnimationTransformVehicle : FacepunchBehaviour
        {
            private BaseEntity Main { get; set; } = null;

            private List<PointAnimationTransform> Path { get; } = new List<PointAnimationTransform>();

            private float SecondsTaken { get; set; } = 0f;
            private float SecondsToTake { get; set; } = 0f;
            private float WaypointDone { get; set; } = 0f;

            private Vector3 StartPos { get; set; } = Vector3.zero;
            private Vector3 EndPos { get; set; } = Vector3.zero;

            private Vector3 StartRot { get; set; } = Vector3.zero;
            private Vector3 EndRot { get; set; } = Vector3.zero;

            private float Acceleration { get; set; } = 0f;
            private float SpeedMax { get; set; } = 0f;
            private float Speed { get; set; } = 0f;

            private void Awake()
            {
                Main = GetComponent<BaseEntity>();
                enabled = false;
            }

            internal void AddPath(HashSet<PointAnimationTransform> path, float acceleration, float speedMax)
            {
                foreach (PointAnimationTransform point in path) Path.Add(point);
                Acceleration = acceleration;
                SpeedMax = speedMax;
                enabled = true;
            }

            private void FixedUpdate()
            {
                if (SecondsTaken == 0f)
                {
                    if (Path.Count == 0)
                    {
                        StartPos = EndPos = Vector3.zero;
                        StartRot = EndRot = Vector3.zero;
                        SecondsToTake = 0f;
                        SecondsTaken = 0f;
                        WaypointDone = 0f;
                        enabled = false;
                        _ins.Controller.FinishPathVehicle(Main);
                        return;
                    }
                    StartPos = transform.position;
                    StartRot = transform.rotation.eulerAngles;
                    if (Path[0].Pos != StartPos || Path[0].Rot != StartRot)
                    {
                        EndPos = Path[0].Pos != StartPos ? Path[0].Pos : StartPos;
                        EndRot = Path[0].Rot != StartRot ? Path[0].Rot : StartRot;
                        SecondsToTake = Vector3.Distance(EndPos, StartPos) / Speed;
                        SecondsTaken = 0f;
                        WaypointDone = 0f;
                    }
                    Path.RemoveAt(0);
                }
                if (StartPos != EndPos || StartRot != EndRot)
                {
                    if (Acceleration > 0f)
                    {
                        if (Speed < SpeedMax)
                        {
                            Speed += Acceleration * Time.deltaTime;
                            if (Speed > SpeedMax) Speed = SpeedMax;
                            SecondsToTake = Vector3.Distance(EndPos, StartPos) / Speed;
                        }
                    }
                    else if (Acceleration < 0f)
                    {
                        if (Speed > 1f)
                        {
                            Speed += Acceleration * Time.deltaTime;
                            if (Speed < 1f) Speed = 1f;
                            SecondsToTake = Vector3.Distance(EndPos, StartPos) / Speed;
                        }
                    }
                    SecondsTaken += Time.deltaTime;
                    WaypointDone = Mathf.InverseLerp(0f, SecondsToTake, SecondsTaken);
                    if (StartPos != EndPos) transform.position = Vector3.Lerp(StartPos, EndPos, WaypointDone);
                    if (StartRot != EndRot) transform.rotation = Quaternion.Lerp(Quaternion.Euler(StartRot), Quaternion.Euler(EndRot), WaypointDone);
                    Main.TransformChanged();
                    Main.SendNetworkUpdate();
                    if (WaypointDone >= 1f) SecondsTaken = 0f;
                }
            }
        }
        #endregion Animation

        #region Find Position
        internal MonumentInfo GetMonument()
        {
            List<MonumentInfo> list = Pool.Get<List<MonumentInfo>>();
            foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
            {
                if (monument.displayPhrase.english != "Arctic Research Base") continue;
                list.Add(monument);
            }
            MonumentInfo result = list.Count > 0 ? list.GetRandom() : null;
            Pool.FreeUnmanaged(ref list);
            return result;
        }
        #endregion Find Position

        #region Spawn Loot
        #region NPC
        private void OnCorpsePopulate(ScientistNPC entity, NPCPlayerCorpse corpse)
        {
            if (entity == null) return;
            if (!Controller.Scientists.Contains(entity)) return;

            Controller.Scientists.Remove(entity);

            int typeLootTable;
            PrefabLootTableConfig prefabLootTable;
            LootTableConfig ownTableConfig;
            bool isRemoveCorpse;

            if (entity.displayName == _config.ConfigNpcSnowmobile.Name)
            {
                typeLootTable = _config.ConfigNpcSnowmobile.TypeLootTable;
                prefabLootTable = _config.ConfigNpcSnowmobile.PrefabLootTable;
                ownTableConfig = _config.ConfigNpcSnowmobile.OwnLootTable;
                isRemoveCorpse = _config.ConfigNpcSnowmobile.IsRemoveCorpse;
            }
            else if (entity.displayName == _config.ConfigNpcRepairMinicopter.Name)
            {
                typeLootTable = _config.ConfigNpcRepairMinicopter.TypeLootTable;
                prefabLootTable = _config.ConfigNpcRepairMinicopter.PrefabLootTable;
                ownTableConfig = _config.ConfigNpcRepairMinicopter.OwnLootTable;
                isRemoveCorpse = _config.ConfigNpcRepairMinicopter.IsRemoveCorpse;
            }
            else
            {
                PresetConfig preset = _config.PresetsNpc.FirstOrDefault(x => x.Config.Name == entity.displayName);
                typeLootTable = preset.TypeLootTable;
                prefabLootTable = preset.PrefabLootTable;
                ownTableConfig = preset.OwnLootTable;
                isRemoveCorpse = preset.Config.IsRemoveCorpse;
            }

            NextTick(() =>
            {
                if (corpse == null) return;
                ItemContainer container = corpse.containers[0];
                if (typeLootTable == 1 || typeLootTable == 4 || typeLootTable == 5)
                {
                    container.ClearItemsContainer();
                    if (typeLootTable == 4 || typeLootTable == 5) AddToContainerPrefab(container, prefabLootTable);
                    if (typeLootTable == 1 || typeLootTable == 5) AddToContainerItem(container, ownTableConfig);
                }
                if (Controller.CodeLockPilot != null && UnityEngine.Random.Range(0f, 100f) <= _config.ChanceNote)
                {
                    Item note = ItemManager.CreateByName("note");
                    note.text = Controller.CodeLockScientist != null ? $"Scientist = {Controller.CodeScientist}" : $"Pilot = {Controller.CodePilot}";
                    if (container.capacity < container.itemList.Count + 1) container.capacity++;
                    if (!note.MoveToContainer(container)) note.Remove();
                }
                if (isRemoveCorpse && corpse.IsExists()) corpse.Kill();
            });
        }

        private object CanPopulateLoot(ScientistNPC entity, NPCPlayerCorpse corpse)
        {
            if (entity == null || corpse == null || Controller == null) return null;
            if (Controller.Scientists.Contains(entity))
            {
                if (GetTypeLootTableNpc(entity.displayName) == 2) return null;
                else return true;
            }
            return null;
        }

        private object OnCustomLootNPC(NetworkableId netId)
        {
            if (Controller == null) return null;
            ScientistNPC entity = Controller.Scientists.FirstOrDefault(x => x.IsExists() && x.net.ID.Value == netId.Value);
            if (entity == null) return null;
            if (GetTypeLootTableNpc(entity.displayName) == 3) return null;
            else return true;
        }

        private int GetTypeLootTableNpc(string name)
        {
            if (name == _config.ConfigNpcSnowmobile.Name) return _config.ConfigNpcSnowmobile.TypeLootTable;
            else if (name == _config.ConfigNpcRepairMinicopter.Name) return _config.ConfigNpcRepairMinicopter.TypeLootTable;
            else
            {
                PresetConfig preset = _config.PresetsNpc.FirstOrDefault(x => x.Config.Name == name);
                return preset.TypeLootTable;
            }
        }
        #endregion NPC

        #region Crates
        private object CanPopulateLoot(LootContainer container)
        {
            if (container == null || Controller == null) return null;
            int typeLootTable;
            if (Controller.Crates.TryGetValue(container, out typeLootTable))
            {
                if (typeLootTable == 2) return null;
                else return true;
            }
            else if (container is HackableLockedCrate && container == Controller.HackCrate)
            {
                if (_config.HackCrate.TypeLootTable == 2) return null;
                else return true;
            }
            else return null;
        }

        private object OnCustomLootContainer(NetworkableId netId)
        {
            if (Controller == null) return null;
            if (Controller.Crates.Any(x => x.Key.IsExists() && x.Key.net.ID.Value == netId.Value))
            {
                if (Controller.Crates.FirstOrDefault(x => x.Key.IsExists() && x.Key.net.ID.Value == netId.Value).Value == 3) return null;
                else return true;
            }
            else if (Controller.HackCrate.IsExists() && Controller.HackCrate.net.ID.Value == netId.Value)
            {
                if (_config.HackCrate.TypeLootTable == 3) return null;
                else return true;
            }
            return null;
        }

        private object OnContainerPopulate(LootContainer container)
        {
            if (container == null || Controller == null) return null;
            int typeLootTable;
            if (Controller.Crates.TryGetValue(container, out typeLootTable))
            {
                if (typeLootTable == 6) return null;
                else return true;
            }
            else if (container is HackableLockedCrate && container == Controller.HackCrate)
            {
                if (_config.HackCrate.TypeLootTable == 6) return null;
                else return true;
            }
            else return null;
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
            CheckLootTable(_config.HackCrate.OwnLootTable);
            CheckPrefabLootTable(_config.HackCrate.PrefabLootTable);
            foreach (CrateConfig crate in _config.CratesSuccess)
            {
                CheckLootTable(crate.OwnLootTable);
                CheckPrefabLootTable(crate.PrefabLootTable);
            }
            foreach (CrateConfig crate in _config.CratesFailure)
            {
                CheckLootTable(crate.OwnLootTable);
                CheckPrefabLootTable(crate.PrefabLootTable);
            }
            foreach (PresetConfig preset in _config.PresetsNpc)
            {
                CheckLootTable(preset.OwnLootTable);
                CheckPrefabLootTable(preset.PrefabLootTable);
            }
            CheckLootTable(_config.ConfigNpcSnowmobile.OwnLootTable);
            CheckPrefabLootTable(_config.ConfigNpcSnowmobile.PrefabLootTable);
            CheckLootTable(_config.ConfigNpcRepairMinicopter.OwnLootTable);
            CheckPrefabLootTable(_config.ConfigNpcRepairMinicopter.PrefabLootTable);
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
            if (!_config.IsCreateZonePvp || victim == null || hitinfo == null || Controller == null) return null;
            BasePlayer attacker = hitinfo.InitiatorPlayer;
            if (Controller.Players.Contains(victim) && (attacker == null || Controller.Players.Contains(attacker))) return true;
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
                case "Npc":
                    AddBalance(playerId, _config.Economy.Npc);
                    break;
                case "LockedCrate":
                    AddBalance(playerId, _config.Economy.LockedCrate);
                    break;
                case "OpenDoorPilot":
                    AddBalance(playerId, _config.Economy.OpenDoorPilot);
                    break;
                case "OpenDoorScientist":
                    AddBalance(playerId, _config.Economy.OpenDoorScientist);
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
                    if (_config.Economy.Plugins.Contains("Economics") && plugins.Exists("Economics") && dic.Value > 0) Economics.Call("Deposit", dic.Key.ToString(), dic.Value);
                    if (_config.Economy.Plugins.Contains("Server Rewards") && plugins.Exists("ServerRewards") && intCount > 0) ServerRewards.Call("AddPoints", dic.Key, intCount);
                    if (_config.Economy.Plugins.Contains("IQEconomic") && plugins.Exists("IQEconomic") && intCount > 0) IQEconomic.Call("API_SET_BALANCE", dic.Key, intCount);
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
                if (_config.DistanceAlerts == 0f || Controller == null || Vector3.Distance(player.transform.position, Controller.transform.position) <= _config.DistanceAlerts)
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

        #region Radiation Puzzle Reset
        private static PuzzleReset GetPuzzleReset(MonumentInfo monument)
        {
            PuzzleReset result = null;
            float distance = float.MaxValue;
            foreach (PuzzleReset puzzleReset in PuzzleReset.AllResets)
            {
                if (!puzzleReset.radiationReset) continue;
                float single = Vector3.Distance(monument.transform.position, puzzleReset.transform.position);
                if (single < distance)
                {
                    result = puzzleReset;
                    distance = single;
                }
            }
            return result;
        }

        private static void DisableRadiation(PuzzleReset puzzleReset)
        {
            if (puzzleReset == null) return;
            puzzleReset.CallPrivateMethod("SetRadiusRadiationAmount", 0f);
            puzzleReset.radiationReset = false;
        }

        private static void EnableRadiation(PuzzleReset puzzleReset)
        {
            if (puzzleReset == null) return;
            puzzleReset.radiationReset = true;
        }
        #endregion Radiation Puzzle Reset

        #region GUI
        private HashSet<string> Names { get; } = new HashSet<string>
        {
            "Tab_KpucTaJl",
            "Clock_KpucTaJl",
            "Npc_KpucTaJl",
            "Plus_KpucTaJl",
            "Pilot_KpucTaJl",
            "Scientist_KpucTaJl"
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
        [PluginReference] private readonly Plugin NpcSpawn, BetterNpc, MonumentOwner;

        private HashSet<string> HooksInsidePlugin { get; } = new HashSet<string>
        {
            "OnEntityTakeDamage",
            "OnCodeEntered",
            "OnCardSwipe",
            "OnVehiclePush",
            "CanMountEntity",
            "OnPlayerWound",
            "OnPlayerConnected",
            "OnPlayerDeath",
            "OnEntityDeath",
            "CanHackCrate",
            "OnCrateHack",
            "OnLootEntity",
            "OnEntityKill",
            "OnNpcTarget",
            "OnPlayerCommand",
            "OnServerCommand",
            "OnCorpsePopulate",
            "CanPopulateLoot",
            "OnCustomLootNPC",
            "OnCustomLootContainer",
            "OnContainerPopulate",
            "SetOwnerPveMode",
            "ClearOwnerPveMode",
            "CanEntityTakeDamage",
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
            webrequest.Enqueue("http://37.153.157.216:5000/Api/GetPluginVersions?pluginName=ArcticBaseEvent", null, (code, response) =>
            {
                if (code != 200 || string.IsNullOrEmpty(response)) return;
                string[] array = response.Replace("\"", string.Empty).Split('.');
                VersionNumber latestVersion = new VersionNumber(Convert.ToInt32(array[0]), Convert.ToInt32(array[1]), Convert.ToInt32(array[2]));
                if (Version < latestVersion) PrintWarning($"A new version ({latestVersion}) of the plugin is available! You need to update the plugin:\n- https://lone.design/product/arcticbaseevent-rust-event-plugin\n- https://codefling.com/plugins/arctic-base-event");
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
        [ChatCommand("abstart")]
        private void ChatStartEvent(BasePlayer player)
        {
            if (player.IsAdmin)
            {
                if (!Active) Start(null);
                else PrintToChat(player, GetMessage("EventActive", player.UserIDString, _config.Chat.Prefix));
            }
        }

        [ChatCommand("abstop")]
        private void ChatStopEvent(BasePlayer player)
        {
            if (player.IsAdmin)
            {
                if (Controller != null) Finish();
                else Interface.Oxide.ReloadPlugin(Name);
            }
        }

        [ChatCommand("abpos")]
        private void ChatCommandPos(BasePlayer player)
        {
            if (!player.IsAdmin || Controller == null) return;
            Vector3 pos = Controller.transform.InverseTransformPoint(player.transform.position);
            Puts($"Position: {pos}");
            PrintToChat(player, $"Position: {pos}");
        }

        [ConsoleCommand("abstart")]
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
            else Puts("This event is active now. To finish this event (abstop), then to start the next one");
        }

        [ConsoleCommand("abstop")]
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

namespace Oxide.Plugins.ArcticBaseEventExtensionMethods
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

        public static Action GetPrivateAction(this object obj, string methodName)
        {
            MethodInfo mi = obj.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (mi != null) return (Action)Delegate.CreateDelegate(typeof(Action), obj, mi);
            else return null;
        }

        public static object CallPrivateMethod(this object obj, string methodName, params object[] args)
        {
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            MethodInfo mi = obj.GetType().GetMethod(methodName, flags);
            if (mi != null) return mi.Invoke(obj, args);
            else return null;
        }
    }
}